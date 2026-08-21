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

public sealed class PowerPlanManager
{
    private readonly IPowerPlanStore _store;
    private readonly IPowerPlanStateRepository _repository;
    private readonly Func<CancellationToken, Task<bool>>? _hasConflictingFeature;

    public PowerPlanManager(
        IPowerPlanStore store,
        IPowerPlanStateRepository repository,
        Func<CancellationToken, Task<bool>>? hasConflictingFeature = null)
    {
        _store = store;
        _repository = repository;
        _hasConflictingFeature = hasConflictingFeature;
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
        if (_hasConflictingFeature is not null && await _hasConflictingFeature(cancellationToken))
        {
            throw new InvalidOperationException("Revert the Aurum core-parking plan before tracking another power-plan change.");
        }

        var existingState = await _repository.GetAsync(cancellationToken);
        if (existingState is not null)
        {
            throw new InvalidOperationException("A power-plan change is already tracked. Revert it before choosing another plan.");
        }

        var snapshot = await _store.CaptureAsync(cancellationToken);
        EnsurePlanExists(snapshot, desiredPlanId);
        if (snapshot.ActivePlanId == desiredPlanId)
        {
            throw new InvalidOperationException("The selected power plan is already active.");
        }

        await _store.SetActiveAsync(desiredPlanId, cancellationToken);
        try
        {
            await _repository.SaveAsync(
                new PersistedPowerPlanState(snapshot.ActivePlanId, desiredPlanId, DateTimeOffset.UtcNow),
                cancellationToken);
        }
        catch (Exception error)
        {
            var recovered = await TrySetActiveAsync(snapshot.ActivePlanId, cancellationToken);
            throw new PowerPlanTransactionException(
                recovered
                    ? "The power plan changed, but its rollback state could not be saved. The original plan was restored."
                    : "The power plan changed, its rollback state could not be saved, and automatic recovery failed.",
                error,
                recovered);
        }
    }

    public async Task RepairAsync(CancellationToken cancellationToken = default)
    {
        var state = await _repository.GetAsync(cancellationToken)
            ?? throw new InvalidOperationException("There is no tracked power-plan change to repair.");
        var snapshot = await _store.CaptureAsync(cancellationToken);
        EnsurePlanExists(snapshot, state.DesiredPlanId);
        await _store.SetActiveAsync(state.DesiredPlanId, cancellationToken);
    }

    public async Task RevertAsync(CancellationToken cancellationToken = default)
    {
        var state = await _repository.GetAsync(cancellationToken)
            ?? throw new InvalidOperationException("There is no tracked power-plan change to revert.");
        var snapshot = await _store.CaptureAsync(cancellationToken);
        EnsurePlanExists(snapshot, state.OriginalPlanId);

        await _store.SetActiveAsync(state.OriginalPlanId, cancellationToken);
        try
        {
            await _repository.RemoveAsync(cancellationToken);
        }
        catch (Exception error)
        {
            var recovered = await TrySetActiveAsync(state.DesiredPlanId, cancellationToken);
            throw new PowerPlanTransactionException(
                recovered
                    ? "The original plan was activated, but saved tracking state could not be removed. The Aurum plan was restored."
                    : "The original plan was activated, saved tracking state could not be removed, and automatic recovery failed.",
                error,
                recovered);
        }
    }

    private static void EnsurePlanExists(PowerPlanSnapshot snapshot, Guid planId)
    {
        if (!snapshot.Plans.Any(plan => plan.Id == planId))
        {
            throw new InvalidOperationException($"Power plan '{planId}' is no longer available in Windows.");
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
