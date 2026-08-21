namespace Aurum.Core;

public static class BuiltInTweakCatalog
{
    private const string ExplorerAdvanced = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    private const string ControlPanelDesktop = @"Control Panel\Desktop";
    private const string ControlPanelMouse = @"Control Panel\Mouse";
    private const string ControlPanelStickyKeys = @"Control Panel\Accessibility\StickyKeys";
    private const string SearchSettings = @"Software\Microsoft\Windows\CurrentVersion\Search";
    private const string PrivacySettings = @"Software\Microsoft\Windows\CurrentVersion\Privacy";
    private const string AdvertisingSettings = @"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo";
    private const string SiufSettings = @"Software\Microsoft\Siuf\Rules";
    private const string GameBarSettings = @"Software\Microsoft\GameBar";
    private const string GameDvrSettings = @"Software\Microsoft\Windows\CurrentVersion\GameDVR";

    public static IReadOnlyList<TweakDefinition> All { get; } = Array.AsReadOnly(
        new TweakDefinition[]
    {
        // --- ПРОVОДНИК ---
        new(
            id: TweakIds.Explorer.ShowFileExtensions,
            category: "Проводник",
            name: "Показывать расширения файлов",
            description: "Показывает .exe, .zip и другие расширения в Проводнике.",
            impact: "Упрощает проверку типа файла и снижает риск маскировки исполняемых файлов.",
            risk: TweakRisk.Safe,
            restart: RestartRequirement.Explorer,
            requiresAdministrator: false,
            new RegistryMutation(
                new RegistryTarget(RegistryHiveId.CurrentUser, ExplorerAdvanced, "HideFileExt"),
                RegistryValue.DWord(0))),

        new(
            id: TweakIds.Explorer.ShowHiddenFiles,
            category: "Проводник",
            name: "Показывать скрытые файлы",
            description: "Включает отображение скрытых файлов и каталогов в Проводнике Windows.",
            impact: "Позволяет быстро находить файлы конфигураций и каталоги приложений (AppData).",
            risk: TweakRisk.Safe,
            restart: RestartRequirement.Explorer,
            requiresAdministrator: false,
            new RegistryMutation(
                new RegistryTarget(RegistryHiveId.CurrentUser, ExplorerAdvanced, "Hidden"),
                RegistryValue.DWord(1))),

        new(
            id: TweakIds.Explorer.OpenToThisPc,
            category: "Проводник",
            name: "Открывать «Этот компьютер» по умолчанию",
            description: "Открывает список локальных дисков вместо экрана «Главная / Быстрый доступ» при запуске Проводника.",
            impact: "Мгновенный переход к дискам без загрузки списка последних файлов.",
            risk: TweakRisk.Safe,
            restart: RestartRequirement.None,
            requiresAdministrator: false,
            new RegistryMutation(
                new RegistryTarget(RegistryHiveId.CurrentUser, ExplorerAdvanced, "LaunchTo"),
                RegistryValue.DWord(1))),

        // --- ИНТЕРФЕЙС И ОТКЛИК ---
        new(
            id: TweakIds.Visuals.DisableTransparency,
            category: "Интерфейс",
            name: "Отключить прозрачность",
            description: "Отключает полупрозрачные поверхности и эффекты размытия в интерфейсе Windows.",
            impact: "Снижает визуальную нагрузку на GPU и графический стек.",
            risk: TweakRisk.Safe,
            restart: RestartRequirement.None,
            requiresAdministrator: false,
            new RegistryMutation(
                new RegistryTarget(
                    RegistryHiveId.CurrentUser,
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                    "EnableTransparency"),
                RegistryValue.DWord(0))),

        new(
            id: TweakIds.Visuals.ReduceMenuDelay,
            category: "Интерфейс",
            name: "Ускорить раскрытие контекстных меню",
            description: "Уменьшает задержку всплытия выпадающих и контекстных меню с 400 мс до 100 мс.",
            impact: "Интерфейс ощущается более отзывчивым при навигации мышью.",
            risk: TweakRisk.Safe,
            restart: RestartRequirement.SignOut,
            requiresAdministrator: false,
            new RegistryMutation(
                new RegistryTarget(RegistryHiveId.CurrentUser, ControlPanelDesktop, "MenuShowDelay"),
                RegistryValue.String("100"))),

        new(
            id: TweakIds.Visuals.DisableWindowAnimation,
            category: "Интерфейс",
            name: "Отключить анимацию сворачивания окон",
            description: "Отключает анимации минимизации и разворачивания окон приложений.",
            impact: "Окна открываются и сворачиваются мгновенно без эффектов перемещения.",
            risk: TweakRisk.Safe,
            restart: RestartRequirement.SignOut,
            requiresAdministrator: false,
            new RegistryMutation(
                new RegistryTarget(RegistryHiveId.CurrentUser, @"Control Panel\Desktop\WindowMetrics", "MinAnimate"),
                RegistryValue.String("0"))),

        // --- ИГРЫ И МУЛЬТИМЕДИА ---
        new(
            id: TweakIds.Gaming.EnableGameMode,
            category: "Игры",
            name: "Включить игровой режим",
            description: "Включает штатный игровой режим Windows для текущего пользователя.",
            impact: "Windows приоритизирует игровой процесс и уменьшает активность фоновых задач.",
            risk: TweakRisk.Safe,
            restart: RestartRequirement.None,
            requiresAdministrator: false,
            new RegistryMutation(
                new RegistryTarget(RegistryHiveId.CurrentUser, GameBarSettings, "AutoGameModeEnabled"),
                RegistryValue.DWord(1))),

        new(
            id: TweakIds.Gaming.DisableBackgroundCapture,
            category: "Игры",
            name: "Отключить фоновую запись игр",
            description: "Запрещает постоянный захват последних моментов средствами Game DVR в фоне.",
            impact: "Освобождает видеопамять и ресурсы GPU от фонового кодирования видеопотока.",
            risk: TweakRisk.Moderate,
            restart: RestartRequirement.None,
            requiresAdministrator: false,
            new RegistryMutation(
                new RegistryTarget(RegistryHiveId.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled"),
                RegistryValue.DWord(0)),
            new RegistryMutation(
                new RegistryTarget(RegistryHiveId.CurrentUser, GameDvrSettings, "AppCaptureEnabled"),
                RegistryValue.DWord(0))),

        new(
            id: TweakIds.Gaming.DisableXboxGameBar,
            category: "Игры",
            name: "Отключить оверлей Xbox Game Bar",
            description: "Отключает вызов всплывающего оверлея Game Bar по горячей клавише Win+G.",
            impact: "Предотвращает случайные всплытия оверлея во время игры и снимает фоновые обработчики.",
            risk: TweakRisk.Safe,
            restart: RestartRequirement.None,
            requiresAdministrator: false,
            new RegistryMutation(
                new RegistryTarget(RegistryHiveId.CurrentUser, GameBarSettings, "UseNexusForGameBarEnabled"),
                RegistryValue.DWord(0))),

        // --- КОНФИДЕНЦИАЛЬНОСТЬ И ТЕЛЕМЕТРИЯ ---
        new(
            id: TweakIds.Privacy.DisableAdvertisingId,
            category: "Конфиденциальность",
            name: "Отключить рекламный идентификатор",
            description: "Запрещает приложениям использовать рекламный ID Windows для персонализации.",
            impact: "Повышает приватность; реклама в сторонних UWP-приложениях перестаёт быть таргетированной.",
            risk: TweakRisk.Safe,
            restart: RestartRequirement.None,
            requiresAdministrator: false,
            new RegistryMutation(
                new RegistryTarget(RegistryHiveId.CurrentUser, AdvertisingSettings, "Enabled"),
                RegistryValue.DWord(0))),

        new(
            id: TweakIds.Privacy.DisableFeedbackRequests,
            category: "Конфиденциальность",
            name: "Отключить запросы отзывов",
            description: "Отключает системные всплывающие окна с опросами и предложениями оценить функции Windows.",
            impact: "Устраняет отвлекающие системные уведомления.",
            risk: TweakRisk.Safe,
            restart: RestartRequirement.None,
            requiresAdministrator: false,
            new RegistryMutation(
                new RegistryTarget(RegistryHiveId.CurrentUser, SiufSettings, "NumberOfSIUFInPeriod"),
                RegistryValue.DWord(0))),

        new(
            id: TweakIds.Privacy.DisableTailoredExperiences,
            category: "Конфиденциальность",
            name: "Отключить персонализацию по диагностике",
            description: "Запрещает Microsoft использовать диагностические данные для рекомендаций и советов.",
            impact: "Уменьшает фоновый сбор поведенческих предпочтений пользователя.",
            risk: TweakRisk.Safe,
            restart: RestartRequirement.None,
            requiresAdministrator: false,
            new RegistryMutation(
                new RegistryTarget(RegistryHiveId.CurrentUser, PrivacySettings, "TailoredExperiencesWithDiagnosticDataEnabled"),
                RegistryValue.DWord(0))),

        new(
            id: TweakIds.Privacy.DisableSearchWebResults,
            category: "Конфиденциальность",
            name: "Отключить веб-поиск Bing в меню «Пуск»",
            description: "Отключает обращение к серверам Bing при локальном поиске программ и файлов в меню «Пуск».",
            impact: "Ускоряет отклик поиска и предотвращает отправку вводимых запросов в интернет.",
            risk: TweakRisk.Safe,
            restart: RestartRequirement.Explorer,
            requiresAdministrator: false,
            new RegistryMutation(
                new RegistryTarget(RegistryHiveId.CurrentUser, SearchSettings, "BingSearchEnabled"),
                RegistryValue.DWord(0)),
            new RegistryMutation(
                new RegistryTarget(RegistryHiveId.CurrentUser, SearchSettings, "CortanaConsent"),
                RegistryValue.DWord(0))),

        // --- ВВОД И УПРАВЛЕНИЕ ---
        new(
            id: TweakIds.Input.DisableMouseAcceleration,
            category: "Ввод",
            name: "Отключить ускорение мыши",
            description: "Отключает системную кривую ускорения указателя (Enhance Pointer Precision).",
            impact: "Движение указателя становится строго линейным 1:1, что критично для точности в играх.",
            risk: TweakRisk.Moderate,
            restart: RestartRequirement.SignOut,
            requiresAdministrator: false,
            new RegistryMutation(
                new RegistryTarget(RegistryHiveId.CurrentUser, ControlPanelMouse, "MouseSpeed"),
                RegistryValue.String("0")),
            new RegistryMutation(
                new RegistryTarget(RegistryHiveId.CurrentUser, ControlPanelMouse, "MouseThreshold1"),
                RegistryValue.String("0")),
            new RegistryMutation(
                new RegistryTarget(RegistryHiveId.CurrentUser, ControlPanelMouse, "MouseThreshold2"),
                RegistryValue.String("0"))),

        new(
            id: TweakIds.Input.DisableStickyKeysHotkey,
            category: "Ввод",
            name: "Отключить залипание клавиш (5x Shift)",
            description: "Отключает всплывающее окно и звук залипания клавиш при пятикратном нажатии Shift.",
            impact: "Предотвращает случайное сворачивание игр во время активного бега или приседаний.",
            risk: TweakRisk.Safe,
            restart: RestartRequirement.None,
            requiresAdministrator: false,
            new RegistryMutation(
                new RegistryTarget(RegistryHiveId.CurrentUser, ControlPanelStickyKeys, "Flags"),
                RegistryValue.String("506"))),

        // --- БЕЗОПАСНОСТЬ И СИСТЕМНЫЙ КОНТРОЛЬ ---
        new(
            id: TweakIds.Gaming.DisableDefenderRealtime,
            category: "Система",
            name: "Отключить Защитник Windows (Real-Time)",
            description: "Отключает фоновое сканирование ввода-вывода файлов в реальном времени и SmartScreen.",
            impact: "Устраняет микрофризы (stuttering) и ускоряет подгрузку игровых ассетов и текстур.",
            risk: TweakRisk.Moderate,
            restart: RestartRequirement.SignOut,
            requiresAdministrator: true,
            new RegistryMutation(
                new RegistryTarget(RegistryHiveId.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows Defender\Real-Time Protection", "DisableRealtimeMonitoring"),
                RegistryValue.DWord(1)),
            new RegistryMutation(
                new RegistryTarget(RegistryHiveId.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows Defender\Real-Time Protection", "DisableBehaviorMonitoring"),
                RegistryValue.DWord(1)),
            new RegistryMutation(
                new RegistryTarget(RegistryHiveId.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows Defender\Real-Time Protection", "DisableOnAccessProtection"),
                RegistryValue.DWord(1)),
            new RegistryMutation(
                new RegistryTarget(RegistryHiveId.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\System", "EnableSmartScreen"),
                RegistryValue.DWord(0))),

        new(
            id: TweakIds.Gaming.DisableUac,
            category: "Система",
            name: "Отключить контроль учётных записей (UAC)",
            description: "Отключает запросы подтверждения прав администратора и затемнение экрана (Secure Desktop).",
            impact: "Игры, моды и оверлеи запускаются без пауз и блокирующих модальных диалогов.",
            risk: TweakRisk.Moderate,
            restart: RestartRequirement.SignOut,
            requiresAdministrator: true,
            new RegistryMutation(
                new RegistryTarget(RegistryHiveId.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "EnableLUA"),
                RegistryValue.DWord(0)),
            new RegistryMutation(
                new RegistryTarget(RegistryHiveId.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "ConsentPromptBehaviorAdmin"),
                RegistryValue.DWord(0)),
            new RegistryMutation(
                new RegistryTarget(RegistryHiveId.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "PromptOnSecureDesktop"),
                RegistryValue.DWord(0))),

        new(
            id: TweakIds.Gaming.DisableWindowsUpdate,
            category: "Система",
            name: "Отключить автообновление Windows",
            description: "Запрещает Windows автоматически скачивать и устанавливать обновления в фоне.",
            impact: "Исключает внезапные скачки нагрузки на CPU, диск и интернет-канал во время сетевых матчей.",
            risk: TweakRisk.Moderate,
            restart: RestartRequirement.None,
            requiresAdministrator: true,
            new RegistryMutation(
                new RegistryTarget(RegistryHiveId.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "NoAutoUpdate"),
                RegistryValue.DWord(1)),
            new RegistryMutation(
                new RegistryTarget(RegistryHiveId.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "AUOptions"),
                RegistryValue.DWord(2))),

        new(
            id: TweakIds.Gaming.DisableSystemRestore,
            category: "Система",
            name: "Отключить защиту и восстановление системы",
            description: "Отключает службу теневых копий VSS и создание контрольных точек восстановления.",
            impact: "Экономит ресурс ячеек SSD и освобождает дисковое пространство от теневых копий.",
            risk: TweakRisk.Moderate,
            restart: RestartRequirement.None,
            requiresAdministrator: true,
            new RegistryMutation(
                new RegistryTarget(RegistryHiveId.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows NT\SystemRestore", "DisableSR"),
                RegistryValue.DWord(1)),
            new RegistryMutation(
                new RegistryTarget(RegistryHiveId.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows NT\SystemRestore", "DisableConfig"),
                RegistryValue.DWord(1))),

        // --- БЫСТРОДЕЙСТВИЕ ЯДРА, ПАМЯТИ И ЗАДЕРЖКИ (LATENCY & KERNEL) ---
        new(
            id: TweakIds.Kernel.DisablePagingExecutive,
            category: "Ядро",
            name: "Удерживать ядро и драйверы в RAM (DisablePagingExecutive)",
            description: "Запрещает операционной системе сбрасывать код ядра и драйверов в файл подкачки на диск.",
            impact: "Устраняет задержки обращения к драйверам и DPC-вызовам во время напряжённых игровых сцен.",
            risk: TweakRisk.Safe,
            restart: RestartRequirement.Restart,
            requiresAdministrator: true,
            new RegistryMutation(
                new RegistryTarget(RegistryHiveId.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", "DisablePagingExecutive"),
                RegistryValue.DWord(1))),

        new(
            id: TweakIds.Kernel.Win32PrioritySeparation,
            category: "Ядро",
            name: "Оптимизация квантования CPU для игр (Win32PrioritySeparation 0x26)",
            description: "Переводит планировщик потоков Windows на короткие переменные кванты с максимальным приоритетом активного окна.",
            impact: "Фокусирует ресурсы процессора на запущенной игре, снижая инпут-лаг и задержку кадров.",
            risk: TweakRisk.Safe,
            restart: RestartRequirement.None,
            requiresAdministrator: true,
            new RegistryMutation(
                new RegistryTarget(RegistryHiveId.LocalMachine, @"SYSTEM\CurrentControlSet\Control\PriorityControl", "Win32PrioritySeparation"),
                RegistryValue.DWord(38))),

        new(
            id: TweakIds.Kernel.SystemResponsiveness,
            category: "Ядро",
            name: "100% ресурсов CPU для игр (SystemResponsiveness = 0)",
            description: "Отключает резервирование 20% ресурсов процессора под фоновые мультимедийные службы Windows.",
            impact: "Предоставляет игре доступ ко всей вычислительной мощности процессора без ограничений.",
            risk: TweakRisk.Safe,
            restart: RestartRequirement.None,
            requiresAdministrator: true,
            new RegistryMutation(
                new RegistryTarget(RegistryHiveId.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "SystemResponsiveness"),
                RegistryValue.DWord(0))),

        new(
            id: TweakIds.Network.DisableThrottling,
            category: "Сеть",
            name: "Отключить троттлинг сетевых пакетов (NetworkThrottlingIndex)",
            description: "Отключает механизм Windows для искусственного ограничения пропускной способности не-мультимедийных сетевых пакетов.",
            impact: "Снижает пинг, сетевой джиттер и потерю пакетов в соревновательных онлайн-играх.",
            risk: TweakRisk.Safe,
            restart: RestartRequirement.None,
            requiresAdministrator: true,
            new RegistryMutation(
                new RegistryTarget(RegistryHiveId.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "NetworkThrottlingIndex"),
                RegistryValue.DWord(unchecked((int)0xFFFFFFFF)))),

        new(
            id: TweakIds.Explorer.ClassicContextMenu,
            category: "Интерфейс",
            name: "Классическое контекстное меню Windows 10 в Windows 11",
            description: "Возвращает мгновенное классическое меню по правому клику мыши без пункта «Показать дополнительные параметры».",
            impact: "Ускоряет работу с файлами и папками без лишних кликов и микрозадержек интерфейса.",
            risk: TweakRisk.Safe,
            restart: RestartRequirement.SignOut,
            requiresAdministrator: false,
            new RegistryMutation(
                new RegistryTarget(RegistryHiveId.CurrentUser, @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32", ""),
                RegistryValue.String("")))
    });

    public static IReadOnlyList<TweakProfile> Profiles { get; } = Array.AsReadOnly(
        new TweakProfile[]
    {
        new TweakProfile(
            "balanced",
            "Баланс",
            "Базовый комфортный набор: показ расширений, открытие «Этот компьютер», ускорение меню, игровой режим и базовая приватность.",
            new HashSet<string>(StringComparer.Ordinal)
            {
                TweakIds.Explorer.ShowFileExtensions,
                TweakIds.Explorer.OpenToThisPc,
                TweakIds.Visuals.ReduceMenuDelay,
                TweakIds.Gaming.EnableGameMode,
                TweakIds.Privacy.DisableAdvertisingId,
                TweakIds.Privacy.DisableFeedbackRequests,
                TweakIds.Explorer.ClassicContextMenu
            }),
        new TweakProfile(
            "gaming",
            "Игровой",
            "Максимальный отклик в играх: отключение GameDVR/GameBar, квантование ядра 0x26, RAM ядро, снятие сетевого троттлинга и отключение акселерации мыши.",
            new HashSet<string>(StringComparer.Ordinal)
            {
                TweakIds.Explorer.ShowFileExtensions,
                TweakIds.Visuals.DisableTransparency,
                TweakIds.Visuals.ReduceMenuDelay,
                TweakIds.Visuals.DisableWindowAnimation,
                TweakIds.Gaming.EnableGameMode,
                TweakIds.Gaming.DisableBackgroundCapture,
                TweakIds.Gaming.DisableXboxGameBar,
                TweakIds.Input.DisableMouseAcceleration,
                TweakIds.Input.DisableStickyKeysHotkey,
                TweakIds.Kernel.DisablePagingExecutive,
                TweakIds.Kernel.Win32PrioritySeparation,
                TweakIds.Kernel.SystemResponsiveness,
                TweakIds.Network.DisableThrottling,
                TweakIds.Explorer.ClassicContextMenu
            }),
        new TweakProfile(
            "privacy",
            "Приватность",
            "Фокус на конфиденциальности: отключение рекламного ID, опросов, персонализации и интернет-поиска Bing в меню Пуск.",
            new HashSet<string>(StringComparer.Ordinal)
            {
                TweakIds.Privacy.DisableAdvertisingId,
                TweakIds.Privacy.DisableFeedbackRequests,
                TweakIds.Privacy.DisableTailoredExperiences,
                TweakIds.Privacy.DisableSearchWebResults,
                TweakIds.Gaming.DisableBackgroundCapture
            }),
        new TweakProfile(
            "laptop",
            "Ноутбук / Офис",
            "Лёгкий профиль для ноутбуков и рабочих ПК: снижение нагрузки интерфейса и отключение лишней телеметрии.",
            new HashSet<string>(StringComparer.Ordinal)
            {
                TweakIds.Explorer.ShowFileExtensions,
                TweakIds.Explorer.OpenToThisPc,
                TweakIds.Visuals.DisableTransparency,
                TweakIds.Visuals.ReduceMenuDelay,
                TweakIds.Privacy.DisableAdvertisingId,
                TweakIds.Privacy.DisableFeedbackRequests
            })
    });
}
