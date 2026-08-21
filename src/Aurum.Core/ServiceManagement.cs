namespace Aurum.Core;

public sealed record ServiceGroupDefinition(
    string Id,
    string Name,
    string Description,
    IReadOnlyList<string> ServiceNames,
    string Impact);

public enum ServiceTrackingState
{
    NotTracked,
    Applied,
    Drifted,
    AlreadyDisabledOutside
}

public sealed record ServiceEvaluation(
    ServiceDefinition Service,
    ServiceTrackingState TrackingState,
    PersistedServiceEntry? PersistedState);

public sealed record ServiceGroupEvaluation(
    ServiceGroupDefinition Group,
    bool IsApplied,
    bool IsDrifted,
    int AppliedCount,
    int TrackedCount,
    int TotalCount);

public sealed record PersistedServiceEntry(
    string ServiceName,
    ServiceStartMode OriginalStartMode,
    bool OriginalDelayedAutoStart,
    DateTimeOffset AppliedAtUtc,
    int SchemaVersion = 1);

public interface IServiceStateRepository
{
    Task<IReadOnlyList<PersistedServiceEntry>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<PersistedServiceEntry?> GetAsync(string serviceName, CancellationToken cancellationToken = default);
    Task SaveAsync(PersistedServiceEntry entry, CancellationToken cancellationToken = default);
    Task RemoveAsync(string serviceName, CancellationToken cancellationToken = default);
}

public interface IServiceControlStore
{
    Task<ServiceDefinition?> GetServiceAsync(string serviceName, CancellationToken cancellationToken = default);

    /// <param name="delayedAutoStart">
    /// When set, also restores the delayed-auto-start flag. Windows treats this as a
    /// separate configuration value, so an Automatic service reverts to immediate start
    /// unless the captured flag is passed back.
    /// </param>
    Task ChangeStartModeAsync(
        string serviceName,
        ServiceStartMode startMode,
        bool? delayedAutoStart = null,
        CancellationToken cancellationToken = default);

    Task StopServiceAsync(string serviceName, CancellationToken cancellationToken = default);
    Task StartServiceAsync(string serviceName, CancellationToken cancellationToken = default);
}

public static class BuiltInServiceGroups
{
    public static IReadOnlyList<ServiceGroupDefinition> All { get; } = Array.AsReadOnly(
        new ServiceGroupDefinition[]
    {
        new(
            Id: "telemetry",
            Name: "Диагностика и телеметрия",
            Description: "Службы сбора телеметрии, диагностических данных и отправки отчётов об ошибках.",
            ServiceNames: new[] { "DiagTrack", "dmwappushservice", "weridsvc" },
            Impact: "Предотвращает сбор фоновой телеметрии и отправку отчётов в Microsoft."),

        new(
            Id: "xbox",
            Name: "Службы Xbox Live",
            Description: "Службы авторизации, облачных сохранений и мультиплеера Xbox.",
            ServiceNames: new[] { "XblAuthManager", "XblGameSave", "XboxNetApiSvc", "XboxGipSvc" },
            Impact: "Снижает фоновую активность. Не рекомендуется отключать при игре через Microsoft Store / Xbox Game Pass."),

        new(
            Id: "print",
            Name: "Печать и факс",
            Description: "Диспетчер очереди печати и служба факса Windows.",
            ServiceNames: new[] { "Spooler", "Fax" },
            Impact: "Освобождает ОЗУ, но отправка на физические и виртуальные (PDF) принтеры перестанет работать."),

        new(
            Id: "maps-location",
            Name: "Карты и геолокация",
            Description: "Менеджер загруженных карт и служба определения географического положения.",
            ServiceNames: new[] { "MapsBroker", "lfsvc" },
            Impact: "Отключает фоновые проверки карт и обращение к службам геопозиционирования."),

        new(
            Id: "touch",
            Name: "Сенсорный ввод и перо",
            Description: "Служба сенсорной клавиатуры и рукописного ввода.",
            ServiceNames: new[] { "TabletInputService" },
            Impact: "Рекомендуется для обычных настольных ПК без тачскринов и графических стилусов."),

        new(
            Id: "insider",
            Name: "Тестирование и демо",
            Description: "Службы программы предварительной оценки Windows и демонстрационного режима.",
            ServiceNames: new[] { "wisvc", "RetailDemo" },
            Impact: "Безопасно для отключения в повседневных рабочих и игровых системах.")
    });
}

public sealed class ServiceManager
{
    private readonly IServiceControlStore _controlStore;
    private readonly IServiceStateRepository _stateRepository;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ServiceManager(IServiceControlStore controlStore, IServiceStateRepository stateRepository)
    {
        _controlStore = controlStore ?? throw new ArgumentNullException(nameof(controlStore));
        _stateRepository = stateRepository ?? throw new ArgumentNullException(nameof(stateRepository));
    }

    private static void EnsureNotProtected(string serviceName)
    {
        if (ServiceAnalyzer.IsProtected(serviceName))
        {
            throw new InvalidOperationException(
                $"Служба '{serviceName}' входит в системную основу. Aurum исключает её из оптимизаций и не будет её отключать.");
        }
    }

    public async Task<ServiceEvaluation> EvaluateServiceAsync(
        ServiceDefinition service,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(service);

        var persisted = await _stateRepository.GetAsync(service.Name, cancellationToken);
        var isCurrentlyDisabled = service.StartMode == ServiceStartMode.Disabled;

        var state = (persisted, isCurrentlyDisabled) switch
        {
            (null, false) => ServiceTrackingState.NotTracked,
            (null, true) => ServiceTrackingState.AlreadyDisabledOutside,
            (not null, true) => ServiceTrackingState.Applied,
            (not null, false) => ServiceTrackingState.Drifted
        };

        return new ServiceEvaluation(service, state, persisted);
    }

    public async Task DisableServiceAsync(string serviceName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        await _gate.WaitAsync(cancellationToken);

        try
        {
            EnsureNotProtected(serviceName);

            var service = await _controlStore.GetServiceAsync(serviceName, cancellationToken);
            if (service is null)
            {
                return;
            }

            var existing = await _stateRepository.GetAsync(serviceName, cancellationToken);
            if (existing is not null)
            {
                throw new InvalidOperationException($"Служба '{serviceName}' уже отслеживается Aurum.");
            }

            if (service.StartMode == ServiceStartMode.Disabled)
            {
                throw new InvalidOperationException($"Служба '{serviceName}' уже была отключена вне Aurum.");
            }

            var entry = new PersistedServiceEntry(
                service.Name,
                service.StartMode,
                service.IsDelayedAutoStart,
                DateTimeOffset.UtcNow);

            await _stateRepository.SaveAsync(entry, cancellationToken);

            try
            {
                await _controlStore.ChangeStartModeAsync(serviceName, ServiceStartMode.Disabled, null, cancellationToken);
                if (service.State == ServiceRunState.Running)
                {
                    try
                    {
                        await _controlStore.StopServiceAsync(serviceName, cancellationToken);
                    }
                    catch
                    {
                        // Best-effort stop
                    }
                }
            }
            catch (Exception ex)
            {
                try
                {
                    await _controlStore.ChangeStartModeAsync(
                        serviceName,
                        entry.OriginalStartMode,
                        entry.OriginalDelayedAutoStart,
                        CancellationToken.None);
                    await _stateRepository.RemoveAsync(serviceName, CancellationToken.None);
                }
                catch
                {
                    // Recovery best effort
                }

                throw new InvalidOperationException($"Не удалось отключить службу '{serviceName}': {ex.Message}", ex);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RevertServiceAsync(string serviceName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        await _gate.WaitAsync(cancellationToken);

        try
        {
            var entry = await _stateRepository.GetAsync(serviceName, cancellationToken);
            if (entry is null)
            {
                return;
            }

            // Both the start mode and the service name come from a file in the user's
            // profile, which is writable without elevation, while the revert itself
            // usually runs elevated. Aurum only ever sets a service to Disabled, so a
            // service that is not currently disabled has nothing left to restore, and
            // writing the recorded mode anyway would turn an edited snapshot into a way
            // to enable an arbitrary service. Dropping the tracking entry is also the
            // right answer for drift: the change is already gone.
            var service = await _controlStore.GetServiceAsync(serviceName, cancellationToken);
            if (service is null || service.StartMode != ServiceStartMode.Disabled)
            {
                await _stateRepository.RemoveAsync(serviceName, cancellationToken);
                return;
            }

            if (!Enum.IsDefined(entry.OriginalStartMode))
            {
                throw new InvalidOperationException(
                    $"Снимок службы '{serviceName}' содержит неизвестный режим запуска. Aurum не будет его применять.");
            }

            await _controlStore.ChangeStartModeAsync(
                serviceName,
                entry.OriginalStartMode,
                entry.OriginalDelayedAutoStart,
                cancellationToken);

            if (entry.OriginalStartMode is ServiceStartMode.Automatic or ServiceStartMode.System or ServiceStartMode.Boot)
            {
                try
                {
                    await _controlStore.StartServiceAsync(serviceName, cancellationToken);
                }
                catch
                {
                    // Best-effort start
                }
            }

            await _stateRepository.RemoveAsync(serviceName, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RepairServiceAsync(string serviceName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        await _gate.WaitAsync(cancellationToken);

        try
        {
            var entry = await _stateRepository.GetAsync(serviceName, cancellationToken);
            if (entry is null)
            {
                return;
            }

            // Repair re-disables a service named by the snapshot rather than by the user's
            // click, so an edited snapshot would otherwise be enough to turn off the
            // firewall or Defender through an elevated Aurum.
            EnsureNotProtected(serviceName);

            await _controlStore.ChangeStartModeAsync(serviceName, ServiceStartMode.Disabled, null, cancellationToken);
            try
            {
                await _controlStore.StopServiceAsync(serviceName, cancellationToken);
            }
            catch
            {
                // Best-effort stop
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ServiceGroupEvaluation> EvaluateGroupAsync(
        ServiceGroupDefinition group,
        IEnumerable<ServiceDefinition> currentServices,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(currentServices);

        var serviceLookup = currentServices.ToDictionary(static s => s.Name, StringComparer.OrdinalIgnoreCase);
        var tracked = (await _stateRepository.GetAllAsync(cancellationToken))
            .ToDictionary(static s => s.ServiceName, StringComparer.OrdinalIgnoreCase);

        var appliedCount = 0;
        var trackedCount = 0;
        var hasDrift = false;
        var presentTotal = 0;

        foreach (var name in group.ServiceNames)
        {
            if (!serviceLookup.TryGetValue(name, out var service))
            {
                continue;
            }

            presentTotal++;
            var isTracked = tracked.TryGetValue(name, out _);
            var isDisabled = service.StartMode == ServiceStartMode.Disabled;

            if (isTracked)
            {
                trackedCount++;
                if (isDisabled)
                {
                    appliedCount++;
                }
                else
                {
                    hasDrift = true;
                }
            }
        }

        var isApplied = presentTotal > 0 && appliedCount == presentTotal;

        return new ServiceGroupEvaluation(
            group,
            isApplied,
            hasDrift,
            appliedCount,
            trackedCount,
            presentTotal);
    }
}
