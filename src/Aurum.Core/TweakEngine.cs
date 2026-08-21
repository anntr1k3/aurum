namespace Aurum.Core;

public sealed class TweakEngine
{
    private readonly ISystemStore _systemStore;
    private readonly ITweakStateRepository _stateRepository;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public TweakEngine(ISystemStore systemStore, ITweakStateRepository stateRepository)
    {
        _systemStore = systemStore ?? throw new ArgumentNullException(nameof(systemStore));
        _stateRepository = stateRepository ?? throw new ArgumentNullException(nameof(stateRepository));
    }

    public async Task<TweakEvaluation> EvaluateAsync(
        TweakDefinition definition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var persistedState = await _stateRepository.GetAsync(definition.Id, cancellationToken);
        var matchesDesired = true;

        foreach (var mutation in definition.Mutations)
        {
            var current = await _systemStore.ReadRegistryAsync(mutation.Target, cancellationToken);
            if (!current.Exists || current.Value != mutation.DesiredValue)
            {
                matchesDesired = false;
            }
        }

        var state = (persistedState, matchesDesired) switch
        {
            (null, false) => TweakStateKind.Available,
            (null, true) => TweakStateKind.AlreadyConfigured,
            (not null, true) => TweakStateKind.Applied,
            (not null, false) => TweakStateKind.Drifted
        };

        return new TweakEvaluation(definition, state, matchesDesired, persistedState?.AppliedAtUtc);
    }

    public async Task ApplyAsync(TweakDefinition definition, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        await _gate.WaitAsync(cancellationToken);

        try
        {
            if (await _stateRepository.GetAsync(definition.Id, cancellationToken) is not null)
            {
                throw new InvalidOperationException($"Tweak '{definition.Id}' is already tracked by Aurum.");
            }

            var snapshots = new List<RegistryStateEntry>(definition.Mutations.Count);
            foreach (var mutation in definition.Mutations)
            {
                var snapshot = await _systemStore.ReadRegistryAsync(mutation.Target, cancellationToken);
                snapshots.Add(new RegistryStateEntry(mutation.Target, snapshot));
            }

            if (snapshots.Select((entry, index) => entry.OriginalValue.Exists &&
                    entry.OriginalValue.Value == definition.Mutations[index].DesiredValue).All(static value => value))
            {
                throw new InvalidOperationException(
                    $"Tweak '{definition.Id}' is already configured outside Aurum; no original state was claimed.");
            }

            // The snapshot has to reach disk before the first mutation. Once a value is
            // overwritten its original only exists in this record, so a process kill
            // between the write and the save would strand the change permanently.
            var state = new PersistedTweakState(definition.Id, DateTimeOffset.UtcNow, snapshots);
            await _stateRepository.SaveAsync(state, cancellationToken);

            try
            {
                foreach (var mutation in definition.Mutations)
                {
                    await _systemStore.WriteRegistryAsync(mutation.Target, mutation.DesiredValue, cancellationToken);
                }
            }
            catch (Exception operationError)
            {
                var recoveryErrors = await RestoreEntriesBestEffortAsync(snapshots, CancellationToken.None);

                try
                {
                    await _stateRepository.RemoveAsync(definition.Id, CancellationToken.None);
                }
                catch (Exception recoveryError)
                {
                    recoveryErrors.Add(recoveryError);
                }

                throw new TweakTransactionException(
                    $"Applying '{definition.Id}' failed. Aurum attempted to restore the original state.",
                    operationError,
                    recoveryErrors);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RevertAsync(TweakDefinition definition, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        await _gate.WaitAsync(cancellationToken);

        try
        {
            var state = await _stateRepository.GetAsync(definition.Id, cancellationToken)
                ?? throw new InvalidOperationException($"No original state exists for tweak '{definition.Id}'.");

            EnsureSnapshotTargetsAreDeclared(definition, state);

            var recoveryErrors = await RestoreEntriesBestEffortAsync(state.Entries, cancellationToken);
            if (recoveryErrors.Count != 0)
            {
                throw new TweakTransactionException(
                    $"Reverting '{definition.Id}' was incomplete. Its recovery snapshot was retained.",
                    recoveryErrors[0],
                    recoveryErrors);
            }

            await _stateRepository.RemoveAsync(definition.Id, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RepairAsync(TweakDefinition definition, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        await _gate.WaitAsync(cancellationToken);

        try
        {
            var state = await _stateRepository.GetAsync(definition.Id, cancellationToken)
                ?? throw new InvalidOperationException(
                    $"Tweak '{definition.Id}' was not applied by Aurum and cannot be repaired safely.");

            var currentValues = new List<RegistryStateEntry>(definition.Mutations.Count);
            foreach (var mutation in definition.Mutations)
            {
                currentValues.Add(new RegistryStateEntry(
                    mutation.Target,
                    await _systemStore.ReadRegistryAsync(mutation.Target, cancellationToken)));
            }

            try
            {
                foreach (var mutation in definition.Mutations)
                {
                    await _systemStore.WriteRegistryAsync(mutation.Target, mutation.DesiredValue, cancellationToken);
                }
            }
            catch (Exception operationError)
            {
                var recoveryErrors = await RestoreEntriesBestEffortAsync(currentValues, CancellationToken.None);
                throw new TweakTransactionException(
                    $"Repairing '{definition.Id}' failed. Aurum attempted to restore the pre-repair state.",
                    operationError,
                    recoveryErrors);
            }

            // The first apply snapshot remains authoritative. A repair must never
            // replace it with already-modified values or the real rollback point is lost.
            _ = state;
        }
        finally
        {
            _gate.Release();
        }
    }

    // Revert is the only path that takes a registry location from persisted state rather
    // than from the catalog. The snapshot lives in the user's profile and is writable
    // without elevation, while the revert itself usually runs elevated, so the value is
    // trusted but the location it is written to is not: it must name something this tweak
    // declared it would touch.
    private static void EnsureSnapshotTargetsAreDeclared(TweakDefinition definition, PersistedTweakState state)
    {
        foreach (var entry in state.Entries)
        {
            if (!definition.Mutations.Any(mutation => IsSameTarget(mutation.Target, entry.Target)))
            {
                throw new InvalidOperationException(
                    $"The rollback snapshot for '{definition.Id}' names '{entry.Target.DisplayPath}', which this tweak does not manage. Aurum will not write it.");
            }
        }
    }

    private static bool IsSameTarget(RegistryTarget left, RegistryTarget right) =>
        left.Hive == right.Hive &&
        string.Equals(left.SubKey, right.SubKey, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(left.ValueName, right.ValueName, StringComparison.OrdinalIgnoreCase);

    private async Task<List<Exception>> RestoreEntriesBestEffortAsync(
        IReadOnlyList<RegistryStateEntry> entries,
        CancellationToken cancellationToken)
    {
        var errors = new List<Exception>();

        foreach (var entry in entries.Reverse())
        {
            try
            {
                if (entry.OriginalValue.Exists)
                {
                    await _systemStore.WriteRegistryAsync(
                        entry.Target,
                        entry.OriginalValue.Value
                            ?? throw new InvalidOperationException("An existing registry snapshot has no value."),
                        cancellationToken);
                }
                else
                {
                    await _systemStore.DeleteRegistryValueAsync(entry.Target, cancellationToken);
                }
            }
            catch (Exception error)
            {
                errors.Add(error);
            }
        }

        return errors;
    }
}
