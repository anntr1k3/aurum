namespace Aurum.Core;

public sealed record PowerPlanInfo(Guid Id, string Name);

public sealed record PowerPlanSnapshot(IReadOnlyList<PowerPlanInfo> Plans, Guid ActivePlanId);

public sealed record PersistedPowerPlanState(
    Guid OriginalPlanId,
    Guid DesiredPlanId,
    DateTimeOffset AppliedAtUtc,
    int SchemaVersion = 1);

public enum PowerPlanStateKind
{
    Untracked,
    Applied,
    Drifted,
}

public sealed record PowerPlanEvaluation(
    PowerPlanSnapshot Snapshot,
    PowerPlanStateKind State,
    PersistedPowerPlanState? PersistedState)
{
    public PowerPlanInfo? ActivePlan => Snapshot.Plans.FirstOrDefault(plan => plan.Id == Snapshot.ActivePlanId);

    public PowerPlanInfo? DesiredPlan => PersistedState is null
        ? null
        : Snapshot.Plans.FirstOrDefault(plan => plan.Id == PersistedState.DesiredPlanId);

    public PowerPlanInfo? OriginalPlan => PersistedState is null
        ? null
        : Snapshot.Plans.FirstOrDefault(plan => plan.Id == PersistedState.OriginalPlanId);
}

public interface IPowerPlanStore
{
    Task<PowerPlanSnapshot> CaptureAsync(CancellationToken cancellationToken = default);

    Task SetActiveAsync(Guid planId, CancellationToken cancellationToken = default);
}

public interface IPowerPlanStateRepository
{
    Task<PersistedPowerPlanState?> GetAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(PersistedPowerPlanState state, CancellationToken cancellationToken = default);

    Task RemoveAsync(CancellationToken cancellationToken = default);
}

public sealed class PowerPlanTransactionException : Exception
{
    public PowerPlanTransactionException(string message, Exception operationError, bool recoverySucceeded)
        : base(message, operationError)
    {
        RecoverySucceeded = recoverySucceeded;
    }

    public bool RecoverySucceeded { get; }
}

/// <summary>
/// Serializes every transaction that claims ownership of the active Windows power plan.
/// <see cref="PowerPlanManager"/> and <see cref="CoreParkingManager"/> are mutually
/// exclusive, so sharing one instance keeps a conflict guard and the mutation it
/// protects inside the same critical section. Without it, both managers can pass their
/// guard concurrently and end up with two competing rollback owners.
/// </summary>
public sealed class PowerPlanTransactionScope
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<IDisposable> EnterAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Releaser(_gate);
    }

    private sealed class Releaser : IDisposable
    {
        private SemaphoreSlim? _gate;

        public Releaser(SemaphoreSlim gate) => _gate = gate;

        public void Dispose() => Interlocked.Exchange(ref _gate, null)?.Release();
    }
}

public sealed class PowerPlanManager
{
    private readonly IPowerPlanStore _store;
    private readonly IPowerPlanStateRepository _repository;
    private readonly Func<CancellationToken, Task<bool>>? _hasConflictingFeature;
    private readonly PowerPlanTransactionScope _scope;
    private readonly IAuditJournal? _auditJournal;

    public PowerPlanManager(
        IPowerPlanStore store,
        IPowerPlanStateRepository repository,
        Func<CancellationToken, Task<bool>>? hasConflictingFeature = null,
        PowerPlanTransactionScope? scope = null,
        IAuditJournal? auditJournal = null)
    {
        _store = store;
        _repository = repository;
        _hasConflictingFeature = hasConflictingFeature;
        _scope = scope ?? new PowerPlanTransactionScope();
        _auditJournal = auditJournal;
    }

    public async Task<PowerPlanEvaluation> EvaluateAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await _store.CaptureAsync(cancellationToken);
        var persistedState = await _repository.GetAsync(cancellationToken);
        var state = persistedState is null
            ? PowerPlanStateKind.Untracked
            : snapshot.ActivePlanId == persistedState.DesiredPlanId
                ? PowerPlanStateKind.Applied
                : PowerPlanStateKind.Drifted;
        return new PowerPlanEvaluation(snapshot, state, persistedState);
    }

    public async Task ApplyAsync(Guid desiredPlanId, CancellationToken cancellationToken = default)
    {
        using var _ = await _scope.EnterAsync(cancellationToken);

        if (_hasConflictingFeature is not null && await _hasConflictingFeature(cancellationToken))
        {
            throw new InvalidOperationException("Сначала откатите план парковки ядер Aurum, затем меняйте схему питания.");
        }

        var existingState = await _repository.GetAsync(cancellationToken);
        if (existingState is not null)
        {
            throw new InvalidOperationException("Изменение схемы питания уже отслеживается. Откатите его, прежде чем выбирать другую схему.");
        }

        var snapshot = await _store.CaptureAsync(cancellationToken);
        EnsurePlanExists(snapshot, desiredPlanId);
        if (snapshot.ActivePlanId == desiredPlanId)
        {
            throw new InvalidOperationException("Выбранная схема питания уже активна.");
        }

        // Claim the rollback point before touching Windows, so an interrupted apply can
        // never leave the active plan changed with no record of what it was.
        try
        {
            await _repository.SaveAsync(
                new PersistedPowerPlanState(snapshot.ActivePlanId, desiredPlanId, DateTimeOffset.UtcNow),
                cancellationToken);
        }
        catch (Exception error)
        {
            throw new PowerPlanTransactionException(
                    "Не удалось сохранить состояние для откака, поэтому активная схема питания осталась без изменений.",
                error,
                true);
        }

        try
        {
            await _store.SetActiveAsync(desiredPlanId, cancellationToken);
            await AuditJournal.RecordAsync(
                _auditJournal,
                "power",
                desiredPlanId.ToString(),
                AuditAction.Applied,
                succeeded: true,
                "Активная схема питания заменена.",
                cancellationToken);
        }
        catch (Exception error)
        {
            var discarded = await TryRemoveStateAsync(cancellationToken);
            throw new PowerPlanTransactionException(
                discarded
                    ? "Не удалось сменить схему питания. Состояние для откака удалено."
                    : "Не удалось сменить схему питания и удалить состояние для откака, поэтому Aurum по-прежнему считает изменение отслеживаемым.",
                error,
                discarded);
        }
    }

    public async Task RepairAsync(CancellationToken cancellationToken = default)
    {
        using var _ = await _scope.EnterAsync(cancellationToken);

        var state = await _repository.GetAsync(cancellationToken)
            ?? throw new InvalidOperationException("Нет отслеживаемого изменения схемы питания, которое можно восстановить.");
        var snapshot = await _store.CaptureAsync(cancellationToken);
        EnsurePlanExists(snapshot, state.DesiredPlanId);
        await _store.SetActiveAsync(state.DesiredPlanId, cancellationToken);
        await AuditJournal.RecordAsync(
            _auditJournal, "power", state.DesiredPlanId.ToString(), AuditAction.Repaired, true,
            "Желаемая схема снова активна.", cancellationToken);
    }

    public async Task RevertAsync(CancellationToken cancellationToken = default)
    {
        using var _ = await _scope.EnterAsync(cancellationToken);

        var state = await _repository.GetAsync(cancellationToken)
            ?? throw new InvalidOperationException("Нет отслеживаемого изменения схемы питания, которое можно откатить.");
        var snapshot = await _store.CaptureAsync(cancellationToken);
        EnsurePlanExists(snapshot, state.OriginalPlanId);

        await _store.SetActiveAsync(state.OriginalPlanId, cancellationToken);
        try
        {
            await _repository.RemoveAsync(cancellationToken);
            await AuditJournal.RecordAsync(
                _auditJournal, "power", state.OriginalPlanId.ToString(), AuditAction.Reverted, true,
                "Исходная схема питания восстановлена.", cancellationToken);
        }
        catch (Exception error)
        {
            var recovered = await TrySetActiveAsync(state.DesiredPlanId, cancellationToken);
            throw new PowerPlanTransactionException(
                recovered
                    ? "Исходная схема активирована, но сохранённое состояние отслеживания удалить не удалось. Схема Aurum восстановлена."
                    : "Исходная схема активирована, сохранённое состояние отслеживания удалить не удалось, и автоматическое восстановление не сработало.",
                error,
                recovered);
        }
    }

    private static void EnsurePlanExists(PowerPlanSnapshot snapshot, Guid planId)
    {
        if (!snapshot.Plans.Any(plan => plan.Id == planId))
        {
            throw new InvalidOperationException($"Схема питания '{planId}' больше не доступна в Windows.");
        }
    }

    private async Task<bool> TryRemoveStateAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _repository.RemoveAsync(cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> TrySetActiveAsync(Guid planId, CancellationToken cancellationToken)
    {
        try
        {
            await _store.SetActiveAsync(planId, cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
