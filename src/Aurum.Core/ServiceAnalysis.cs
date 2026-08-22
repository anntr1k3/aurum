namespace Aurum.Core;

public enum ServiceRunState { Unknown, Stopped, StartPending, StopPending, Running, ContinuePending, PausePending, Paused }
public enum ServiceStartMode { Boot, System, Automatic, Manual, Disabled, Unknown }
public enum ServiceSafetyClass { Protected, ContextDependent, Unclassified }

public sealed record ServiceDefinition(
    string Name,
    string DisplayName,
    string Description,
    ServiceRunState State,
    ServiceStartMode StartMode,
    bool IsDelayedAutoStart,
    uint ProcessId,
    IReadOnlyList<string> Dependencies);

public sealed record ServiceAnalysisItem(
    ServiceDefinition Service,
    IReadOnlyList<string> Dependants,
    ServiceSafetyClass Safety,
    string Capability,
    string Guidance);

public static class ServiceAnalyzer
{
    private static readonly HashSet<string> Protected = new(StringComparer.OrdinalIgnoreCase)
    {
        "Appinfo", "BFE", "BrokerInfrastructure", "CryptSvc", "DcomLaunch", "Dhcp", "Dnscache",
        "EventLog", "EventSystem", "gpsvc", "LanmanWorkstation", "LSM", "MpsSvc", "NlaSvc",
        "PlugPlay", "Power", "ProfSvc", "RpcEptMapper", "RpcSs", "SamSs", "Schedule", "SENS",
        "SystemEventsBroker", "TrustedInstaller", "UserManager", "WinDefend", "Winmgmt", "wuauserv",
    };

    private static readonly Dictionary<string, (string Capability, string Guidance)> Contextual =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Spooler"] = ("Печать", "Нужна для принтеров, виртуальной печати и части PDF-приложений."),
            ["Fax"] = ("Факс", "Имеет смысл только при использовании Windows Fax and Scan или факс-модема."),
            ["WSearch"] = ("Поиск и индексирование", "Отключение ухудшает поиск в Проводнике, меню Пуск и некоторых приложениях."),
            ["MapsBroker"] = ("Автономные карты", "Обслуживает загруженные автономные карты Windows."),
            ["bthserv"] = ("Bluetooth", "Нужна Bluetooth-устройствам, аудио, контроллерам и периферии."),
            ["lfsvc"] = ("Геолокация", "Используется приложениями, часовым поясом и функциями местоположения."),
            ["XblAuthManager"] = ("Xbox", "Аутентификация Xbox Live и игры Microsoft Store могут перестать работать."),
            ["XblGameSave"] = ("Xbox", "Синхронизирует облачные сохранения поддерживаемых игр Xbox."),
            ["XboxNetApiSvc"] = ("Xbox", "Поддерживает сетевые функции Xbox Live и мультиплеер."),
            ["XboxGipSvc"] = ("Xbox", "Служба вспомогательного ввода и аксессуаров Xbox."),
            ["DiagTrack"] = ("Диагностика и телеметрия", "Служба сбора диагностических данных (Connected User Experiences and Telemetry)."),
            ["dmwappushservice"] = ("Диагностика и телеметрия", "Служба маршрутизации WAP-сообщений телеметрии."),
            ["weridsvc"] = ("Отчёты об ошибках", "Служба отправки отчётов об ошибках Windows Error Reporting."),
            ["PhoneSvc"] = ("Связь с телефоном", "Используется телефонными интеграциями Windows."),
            ["WbioSrvc"] = ("Биометрия", "Нужна Windows Hello и биометрическим датчикам."),
            ["TabletInputService"] = ("Перо и сенсорный ввод", "Нужна экранной клавиатуре, перу и рукописному вводу."),
            ["wisvc"] = ("Windows Insider", "Используется функциями программы предварительной оценки Windows."),
            ["RetailDemo"] = ("Демонстрационный режим", "Предназначена для розничного демонстрационного режима Windows."),
        };

    /// <summary>
    /// Services Aurum must never disable, such as the firewall, Defender and the RPC
    /// infrastructure. The service views already hide the toggle for these, but the
    /// managers enforce it too: a start mode is also written during repair, and there the
    /// service name comes from the persisted snapshot rather than from a user action.
    /// </summary>
    public static bool IsProtected(string serviceName) => Protected.Contains(serviceName);

    /// <summary>
    /// Names Aurum may disable, revert, or repair. The snapshot file is writable
    /// without elevation, so repair and revert must not take an arbitrary service
    /// name from JSON: only this declared optional set, which matches the services
    /// the user can actually select in the UI.
    /// </summary>
    public static bool IsDeclaredOptional(string serviceName) => Contextual.ContainsKey(serviceName);

    /// <summary>
    /// A batch disable must not strand a running service that still needs one of the
    /// targets. Dependants that are themselves in the batch are allowed: they are going
    /// away together. Protected names are already refused by <see cref="ServiceManager"/>.
    /// </summary>
    public static void EnsureDisableBatchHasNoRunningDependants(
        IReadOnlyCollection<string> serviceNames,
        IReadOnlyList<ServiceAnalysisItem> analysis)
    {
        ArgumentNullException.ThrowIfNull(serviceNames);
        ArgumentNullException.ThrowIfNull(analysis);

        var batch = serviceNames
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (batch.Count == 0)
        {
            return;
        }

        var lookup = analysis.ToDictionary(static item => item.Service.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var name in batch)
        {
            if (!lookup.TryGetValue(name, out var item))
            {
                continue;
            }

            foreach (var dependantName in item.Dependants)
            {
                if (batch.Contains(dependantName))
                {
                    continue;
                }

                if (!lookup.TryGetValue(dependantName, out var dependant))
                {
                    continue;
                }

                if (dependant.Service.State == ServiceRunState.Running)
                {
                    throw new InvalidOperationException(
                        $"Нельзя отключить «{item.Service.DisplayName}»: служба «{dependant.Service.DisplayName}» сейчас запущена и зависит от неё.");
                }
            }
        }
    }

    public static IReadOnlyList<ServiceAnalysisItem> Analyze(IEnumerable<ServiceDefinition> definitions)
    {
        var services = definitions.ToArray();
        var dependants = services.ToDictionary(service => service.Name, _ => new List<string>(), StringComparer.OrdinalIgnoreCase);
        foreach (var service in services)
        {
            foreach (var dependency in service.Dependencies.Where(dependants.ContainsKey))
            {
                dependants[dependency].Add(service.Name);
            }
        }

        return services.Select(service =>
        {
            if (Protected.Contains(service.Name))
            {
                return new ServiceAnalysisItem(service, dependants[service.Name], ServiceSafetyClass.Protected,
                    "Системная основа", "Aurum исключает эту службу из оптимизаций.");
            }

            if (Contextual.TryGetValue(service.Name, out var context))
            {
                return new ServiceAnalysisItem(service, dependants[service.Name], ServiceSafetyClass.ContextDependent,
                    context.Capability, context.Guidance);
            }

            return new ServiceAnalysisItem(service, dependants[service.Name], ServiceSafetyClass.Unclassified,
                "Не классифицировано", "Нет достаточных оснований рекомендовать изменение этой службы.");
        }).OrderBy(item => item.Safety).ThenBy(item => item.Service.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }
}
