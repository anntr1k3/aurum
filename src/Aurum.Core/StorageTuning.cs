namespace Aurum.Core;

public sealed record StorageTuningSnapshot(
    bool Is8dot3Disabled,
    bool IsLastAccessDisabled,
    bool IsHibernationDisabled,
    long HiberfilBytes,
    ServiceStartMode SysMainStartMode,
    ServiceRunState SysMainState);

public sealed record PersistedStorageTuningState(
    bool? Original8dot3Disabled,
    bool? OriginalLastAccessDisabled,
    bool? OriginalHibernationDisabled,
    ServiceStartMode? OriginalSysMainStartMode,
    DateTimeOffset AppliedAtUtc,
    int SchemaVersion = 1);

public interface IStorageTuningStore
{
    Task<StorageTuningSnapshot> CaptureSnapshotAsync(CancellationToken cancellationToken = default);
    Task Set8dot3DisabledAsync(bool disabled, CancellationToken cancellationToken = default);
    Task SetLastAccessDisabledAsync(bool disabled, CancellationToken cancellationToken = default);
    Task SetHibernationDisabledAsync(bool disabled, CancellationToken cancellationToken = default);
    Task SetSysMainDisabledAsync(bool disabled, CancellationToken cancellationToken = default);
}

public interface IStorageTuningStateRepository
{
    Task<PersistedStorageTuningState?> GetAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(PersistedStorageTuningState state, CancellationToken cancellationToken = default);
    Task RemoveAsync(CancellationToken cancellationToken = default);
}

public sealed class StorageTuningManager
{
    private readonly IStorageTuningStore _store;
    private readonly IStorageTuningStateRepository _stateRepository;
    private readonly Func<bool> _isAdministrator;
    private readonly IAuditJournal? _auditJournal;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public StorageTuningManager(
        IStorageTuningStore store,
        IStorageTuningStateRepository stateRepository,
        Func<bool> isAdministrator,
        IAuditJournal? auditJournal = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _stateRepository = stateRepository ?? throw new ArgumentNullException(nameof(stateRepository));
        _isAdministrator = isAdministrator ?? throw new ArgumentNullException(nameof(isAdministrator));
        _auditJournal = auditJournal;
    }

    public Task<StorageTuningSnapshot> CaptureSnapshotAsync(CancellationToken cancellationToken = default) =>
        _store.CaptureSnapshotAsync(cancellationToken);

    public async Task Toggle8dot3Async(bool disable, CancellationToken cancellationToken = default)
    {
        EnsureAdmin();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var snapshot = await _store.CaptureSnapshotAsync(cancellationToken);
            var state = await _stateRepository.GetAsync(cancellationToken) ??
                        new PersistedStorageTuningState(null, null, null, null, DateTimeOffset.UtcNow);

            if (state.Original8dot3Disabled is null)
            {
                state = state with { Original8dot3Disabled = snapshot.Is8dot3Disabled };
                await _stateRepository.SaveAsync(state, cancellationToken);
            }

            await _store.Set8dot3DisabledAsync(disable, cancellationToken);
            await AuditJournal.RecordAsync(
                _auditJournal, "storage", "8.3", AuditAction.Applied, true,
                disable ? "Короткие имена NTFS отключены." : "Короткие имена NTFS включены.",
                cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ToggleLastAccessAsync(bool disable, CancellationToken cancellationToken = default)
    {
        EnsureAdmin();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var snapshot = await _store.CaptureSnapshotAsync(cancellationToken);
            var state = await _stateRepository.GetAsync(cancellationToken) ??
                        new PersistedStorageTuningState(null, null, null, null, DateTimeOffset.UtcNow);

            if (state.OriginalLastAccessDisabled is null)
            {
                state = state with { OriginalLastAccessDisabled = snapshot.IsLastAccessDisabled };
                await _stateRepository.SaveAsync(state, cancellationToken);
            }

            await _store.SetLastAccessDisabledAsync(disable, cancellationToken);
            await AuditJournal.RecordAsync(
                _auditJournal, "storage", "LastAccess", AuditAction.Applied, true,
                disable ? "LastAccess отключён." : "LastAccess включён.",
                cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ToggleHibernationAsync(bool disable, CancellationToken cancellationToken = default)
    {
        EnsureAdmin();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var snapshot = await _store.CaptureSnapshotAsync(cancellationToken);
            var state = await _stateRepository.GetAsync(cancellationToken) ??
                        new PersistedStorageTuningState(null, null, null, null, DateTimeOffset.UtcNow);

            if (state.OriginalHibernationDisabled is null)
            {
                state = state with { OriginalHibernationDisabled = snapshot.IsHibernationDisabled };
                await _stateRepository.SaveAsync(state, cancellationToken);
            }

            await _store.SetHibernationDisabledAsync(disable, cancellationToken);
            await AuditJournal.RecordAsync(
                _auditJournal, "storage", "Hibernation", AuditAction.Applied, true,
                disable ? "Гибернация отключена." : "Гибернация включена.",
                cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ToggleSysMainAsync(bool disable, CancellationToken cancellationToken = default)
    {
        EnsureAdmin();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var snapshot = await _store.CaptureSnapshotAsync(cancellationToken);
            var state = await _stateRepository.GetAsync(cancellationToken) ??
                        new PersistedStorageTuningState(null, null, null, null, DateTimeOffset.UtcNow);

            if (state.OriginalSysMainStartMode is null)
            {
                state = state with { OriginalSysMainStartMode = snapshot.SysMainStartMode };
                await _stateRepository.SaveAsync(state, cancellationToken);
            }

            await _store.SetSysMainDisabledAsync(disable, cancellationToken);
            await AuditJournal.RecordAsync(
                _auditJournal, "storage", "SysMain", AuditAction.Applied, true,
                disable ? "SysMain отключён." : "SysMain включён.",
                cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> HasPersistedStateAsync(CancellationToken cancellationToken = default)
    {
        var state = await _stateRepository.GetAsync(cancellationToken);
        return state is not null;
    }

    public async Task<bool> RevertAsync(CancellationToken cancellationToken = default)
    {
        EnsureAdmin();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await _stateRepository.GetAsync(cancellationToken);
            if (state is null)
            {
                return false;
            }

            if (state.Original8dot3Disabled.HasValue)
            {
                await _store.Set8dot3DisabledAsync(state.Original8dot3Disabled.Value, cancellationToken);
            }

            if (state.OriginalLastAccessDisabled.HasValue)
            {
                await _store.SetLastAccessDisabledAsync(state.OriginalLastAccessDisabled.Value, cancellationToken);
            }

            if (state.OriginalHibernationDisabled.HasValue)
            {
                await _store.SetHibernationDisabledAsync(state.OriginalHibernationDisabled.Value, cancellationToken);
            }

            if (state.OriginalSysMainStartMode.HasValue)
            {
                var isSysMainDisabled = state.OriginalSysMainStartMode.Value == ServiceStartMode.Disabled;
                await _store.SetSysMainDisabledAsync(isSysMainDisabled, cancellationToken);
            }

            await _stateRepository.RemoveAsync(cancellationToken);
            await AuditJournal.RecordAsync(
                _auditJournal, "storage", "settings", AuditAction.Reverted, true,
                "Исходные параметры накопителей восстановлены.",
                cancellationToken);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private void EnsureAdmin()
    {
        if (!_isAdministrator())
        {
            throw new UnauthorizedAccessException("Для изменения параметров накопителей требуются права администратора.");
        }
    }
}
