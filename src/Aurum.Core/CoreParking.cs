namespace Aurum.Core;

public sealed record CoreParkingSettings(uint MinimumAc, uint MaximumAc, uint MinimumDc, uint MaximumDc)
{
    public void Validate()
    {
        if (MinimumAc > 100 || MaximumAc > 100 || MinimumDc > 100 || MaximumDc > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(MinimumAc), "Core-parking percentages must be between 0 and 100.");
        }

        if (MinimumAc > MaximumAc || MinimumDc > MaximumDc)
        {
            throw new ArgumentException("The minimum unparked percentage cannot exceed the maximum.");
        }
    }
}

public sealed record CoreParkingPlan(Guid Id, string Name, CoreParkingSettings Settings);

public sealed record PersistedCoreParkingState(
    Guid OriginalPlanId,
    string OriginalPlanName,
    Guid ManagedPlanId,
    CoreParkingSettings DesiredSettings,
    DateTimeOffset AppliedAtUtc,
    int SchemaVersion = 1);

public enum CoreParkingStateKind { Untracked, Applied, Drifted }

public sealed record CoreParkingEvaluation(
    CoreParkingPlan ActivePlan,
    CoreParkingStateKind State,
    PersistedCoreParkingState? PersistedState,
    bool ManagedPlanExists,
    CoreParkingSettings? ManagedSettings);

public interface ICoreParkingStore
{
    Task<CoreParkingPlan> CaptureActiveAsync(CancellationToken cancellationToken = default);
    Task<Guid> DuplicateAsync(Guid sourcePlanId, string newName, CancellationToken cancellationToken = default);
    Task<CoreParkingSettings> ReadSettingsAsync(Guid planId, CancellationToken cancellationToken = default);
    Task WriteSettingsAsync(Guid planId, CoreParkingSettings settings, CancellationToken cancellationToken = default);
    Task SetActiveAsync(Guid planId, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(Guid planId, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid planId, CancellationToken cancellationToken = default);
}

public interface ICoreParkingStateRepository
{
    Task<PersistedCoreParkingState?> GetAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(PersistedCoreParkingState state, CancellationToken cancellationToken = default);
    Task RemoveAsync(CancellationToken cancellationToken = default);
}

public sealed class CoreParkingManager
{
    private readonly ICoreParkingStore _store;
    private readonly ICoreParkingStateRepository _repository;
    private readonly IPowerPlanStateRepository _powerPlanRepository;
    private readonly PowerPlanTransactionScope _scope;

    public CoreParkingManager(
        ICoreParkingStore store,
        ICoreParkingStateRepository repository,
        IPowerPlanStateRepository powerPlanRepository,
        PowerPlanTransactionScope? scope = null)
    {
        _store = store;
        _repository = repository;
        _powerPlanRepository = powerPlanRepository;
        _scope = scope ?? new PowerPlanTransactionScope();
    }

    public async Task<CoreParkingEvaluation> EvaluateAsync(CancellationToken cancellationToken = default)
    {
        var active = await _store.CaptureActiveAsync(cancellationToken);
        var state = await _repository.GetAsync(cancellationToken);
        if (state is null)
        {
            return new CoreParkingEvaluation(active, CoreParkingStateKind.Untracked, null, false, null);
        }

        var exists = await _store.ExistsAsync(state.ManagedPlanId, cancellationToken);
        var settings = exists ? await _store.ReadSettingsAsync(state.ManagedPlanId, cancellationToken) : null;
        var kind = exists && active.Id == state.ManagedPlanId && settings == state.DesiredSettings
            ? CoreParkingStateKind.Applied
            : CoreParkingStateKind.Drifted;
        return new CoreParkingEvaluation(active, kind, state, exists, settings);
    }

    public async Task ApplyAsync(CoreParkingSettings settings, CancellationToken cancellationToken = default)
    {
        settings.Validate();
        using var _ = await _scope.EnterAsync(cancellationToken);

        if (await _repository.GetAsync(cancellationToken) is not null)
        {
            throw new InvalidOperationException("Aurum already tracks a core-parking plan. Revert it first.");
        }

        if (await _powerPlanRepository.GetAsync(cancellationToken) is not null)
        {
            throw new InvalidOperationException("Revert the tracked power-plan change before creating a core-parking plan.");
        }

        var original = await _store.CaptureActiveAsync(cancellationToken);
        Guid? managedId = null;
        try
        {
            managedId = await _store.DuplicateAsync(original.Id, $"Aurum · Core Parking · {original.Name}", cancellationToken);

            // Recorded as soon as the managed plan exists and before it is populated or
            // activated. Saving last would let an interrupted apply leave the Aurum plan
            // active with nothing on disk pointing back to the original plan.
            await _repository.SaveAsync(
                new PersistedCoreParkingState(original.Id, original.Name, managedId.Value, settings, DateTimeOffset.UtcNow),
                cancellationToken);

            await _store.WriteSettingsAsync(managedId.Value, settings, cancellationToken);
            await _store.SetActiveAsync(managedId.Value, cancellationToken);
        }
        catch
        {
            await TrySetActiveAsync(original.Id, cancellationToken);
            if (managedId is not null)
            {
                await TryDeleteAsync(managedId.Value, cancellationToken);
            }

            await TryRemoveStateAsync(cancellationToken);
            throw;
        }
    }

    public async Task RepairAsync(CancellationToken cancellationToken = default)
    {
        using var _ = await _scope.EnterAsync(cancellationToken);

        var state = await _repository.GetAsync(cancellationToken)
            ?? throw new InvalidOperationException("There is no tracked core-parking plan to repair.");
        if (!await _store.ExistsAsync(state.ManagedPlanId, cancellationToken))
        {
            throw new InvalidOperationException("The Aurum core-parking plan was deleted outside the application.");
        }

        await _store.WriteSettingsAsync(state.ManagedPlanId, state.DesiredSettings, cancellationToken);
        await _store.SetActiveAsync(state.ManagedPlanId, cancellationToken);
    }

    public async Task RevertAsync(CancellationToken cancellationToken = default)
    {
        using var _ = await _scope.EnterAsync(cancellationToken);

        var state = await _repository.GetAsync(cancellationToken)
            ?? throw new InvalidOperationException("There is no tracked core-parking plan to revert.");
        if (!await _store.ExistsAsync(state.OriginalPlanId, cancellationToken))
        {
            throw new InvalidOperationException("The original power plan no longer exists; automatic rollback is unsafe.");
        }

        await _store.SetActiveAsync(state.OriginalPlanId, cancellationToken);
        if (await _store.ExistsAsync(state.ManagedPlanId, cancellationToken))
        {
            await _store.DeleteAsync(state.ManagedPlanId, cancellationToken);
        }

        await _repository.RemoveAsync(cancellationToken);
    }

    private async Task TrySetActiveAsync(Guid planId, CancellationToken cancellationToken)
    {
        try { await _store.SetActiveAsync(planId, cancellationToken); } catch { }
    }

    private async Task TryDeleteAsync(Guid planId, CancellationToken cancellationToken)
    {
        try { await _store.DeleteAsync(planId, cancellationToken); } catch { }
    }

    private async Task TryRemoveStateAsync(CancellationToken cancellationToken)
    {
        try { await _repository.RemoveAsync(cancellationToken); } catch { }
    }
}
