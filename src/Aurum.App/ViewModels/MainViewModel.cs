using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using Aurum.Core;
using Aurum.Infrastructure.Windows;

namespace Aurum.App.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    /// <summary>ERROR_CANCELLED, which is what ShellExecute reports when UAC is dismissed.</summary>
    private const int ErrorCancelled = 1223;

    private readonly WindowsSystemProbe _systemProbe;
    private readonly AtlasHealthService _atlasHealthService;
    private readonly SystemCleanupService _cleanupService;
    private readonly IAuditJournal _auditJournal;
    private readonly Func<string, bool> _confirm;
    private string _statusMessage = "Подготовка безопасного снимка системы…";
    private string _lastCheckLabel = "Ещё не проверялось";
    private string _atlasSummary = "Проверка ещё не выполнялась";
    private string _cleanupSummary = "Выберите категории и запустите предварительное сканирование.";
    private bool _hasError;
    private bool _isBusy;
    private SystemSnapshot? _system;
    private int _selectedTabIndex;

    public MainViewModel(
        TweakEngine engine,
        WindowsSystemProbe systemProbe,
        AtlasHealthService atlasHealthService,
        SystemCleanupService cleanupService,
        HardwareMonitorService hardwareMonitorService,
        PowerPlanManager powerPlanManager,
        StorageMaintenanceManager storageMaintenanceManager,
        StorageTuningManager storageTuningManager,
        CoreParkingManager coreParkingManager,
        WindowsServiceInventory serviceInventory,
        ServiceManager serviceManager,
        NetworkDiagnosticsManager networkDiagnosticsManager,
        NetworkTuningManager networkTuningManager,
        MsiModeManager msiModeManager,
        ISystemTimerService systemTimerService,
        IAuditJournal auditJournal,
        Func<string, bool> confirm)
    {
        _systemProbe = systemProbe;
        _atlasHealthService = atlasHealthService;
        _cleanupService = cleanupService;
        _auditJournal = auditJournal ?? throw new ArgumentNullException(nameof(auditJournal));
        _confirm = confirm;
        AuditEntries = [];

        Tweaks = new ObservableCollection<TweakItemViewModel>(BuiltInTweakCatalog.All.Select(
            definition => new TweakItemViewModel(definition, engine, ReportStatus)));
        FilteredTweaks = new ObservableCollection<TweakItemViewModel>(Tweaks);
        SetTweakCategoryFilterCommand = new RelayCommand<string>(SetTweakCategoryFilter);
        AtlasChecks = [];
        CleanupFiles = [];
        Monitoring = new MonitoringViewModel(hardwareMonitorService);
        Power = new PowerPlanViewModel(powerPlanManager, ReportStatus, confirm);
        Storage = new StorageViewModel(storageMaintenanceManager, storageTuningManager, ReportStatus, confirm);
        CoreParking = new CoreParkingViewModel(coreParkingManager, ReportStatus, confirm);
        Services = new ServicesViewModel(serviceInventory, serviceManager, ReportStatus, confirm);
        Network = new NetworkViewModel(networkDiagnosticsManager, networkTuningManager, ReportStatus, confirm);
        Msi = new MsiViewModel(msiModeManager, ReportStatus, confirm);
        Timer = new SystemTimerViewModel(systemTimerService, ReportStatus);

        _powerPropertyChangedHandler = (_, args) =>
        {
            if (args.PropertyName == nameof(PowerPlanViewModel.ActivePlanName))
            {
                Monitoring.SetPowerPlanName(Power.ActivePlanName);
            }
        };
        Power.PropertyChanged += _powerPropertyChangedHandler;

        _coreParkingPropertyChangedHandler = (_, args) =>
        {
            if (args.PropertyName == nameof(CoreParkingViewModel.ActivePlanName))
            {
                Monitoring.SetPowerPlanName(CoreParking.ActivePlanName);
            }
        };
        CoreParking.PropertyChanged += _coreParkingPropertyChangedHandler;

        CleanupCategories = new ObservableCollection<CleanupCategoryViewModel>(
            cleanupService.Categories.Select(category => new CleanupCategoryViewModel(
                category,
                category.Id is "user-temp" or "shader-cache")));

        SelectProfileCommand = new RelayCommand<string>(SelectProfile);
        SetWelcomeModeCommand = new RelayCommand<object>(_ => CurrentView = ActiveView.Welcome);
        SetSimpleModeCommand = new RelayCommand<object>(_ => CurrentView = ActiveView.Simple);
        SetProModeCommand = new RelayCommand<object>(_ => CurrentView = ActiveView.Pro);
        OpenMonitoringCommand = new RelayCommand<object>(_ => CurrentView = ActiveView.Monitoring);
        NavigateFromWelcomeCommand = new RelayCommand<string>(mode =>
        {
            if (string.Equals(mode, "pro", StringComparison.OrdinalIgnoreCase))
            {
                CurrentView = ActiveView.Pro;
            }
            else if (string.Equals(mode, "monitoring", StringComparison.OrdinalIgnoreCase))
            {
                CurrentView = ActiveView.Monitoring;
            }
            else
            {
                CurrentView = ActiveView.Simple;
            }
        });
        ToggleUserModeCommand = new RelayCommand<object>(_ =>
        {
            CurrentView = (CurrentView == ActiveView.Pro) ? ActiveView.Simple : ActiveView.Pro;
        });
        ApplySelectedCommand = new AsyncRelayCommand(ApplySelectedAsync, () => CanApplySelected, ex => ReportStatus($"Ошибка: {ex.Message}", true));
        RefreshAllCommand = new AsyncRelayCommand(RefreshAllAsync, () => !IsBusy, ex => ReportStatus($"Ошибка: {ex.Message}", true));
        CheckAtlasCommand = new AsyncRelayCommand(CheckAtlasAsync, () => !IsBusy, ex => ReportStatus($"Ошибка: {ex.Message}", true));
        ScanCleanupCommand = new AsyncRelayCommand(ScanCleanupAsync, () => CanScanCleanup, ex => ReportStatus($"Ошибка: {ex.Message}", true));
        CleanCommand = new AsyncRelayCommand(CleanAsync, () => CanClean, ex => ReportStatus($"Ошибка: {ex.Message}", true));

        SimpleFeatures = new ObservableCollection<SimpleFeatureItemViewModel>();
        FilteredSimpleFeatures = new ObservableCollection<SimpleFeatureItemViewModel>();
        SetSimpleCategoryCommand = new RelayCommand<string>(SetSimpleCategory);
        ApplySimpleGamingPresetCommand = new AsyncRelayCommand(ApplySimpleGamingPresetAsync, () => !IsBusy, ex => ReportStatus($"Ошибка: {ex.Message}", true));
        ApplyAllSimpleCurrentCategoryCommand = new AsyncRelayCommand(ApplyAllSimpleCurrentCategoryAsync, () => !IsBusy, ex => ReportStatus($"Ошибка: {ex.Message}", true));
        RevertAllSimpleCommand = new AsyncRelayCommand(RevertEverythingAsync, () => !IsBusy, ex => ReportStatus($"Ошибка: {ex.Message}", true));
        RestartAsAdministratorCommand = new RelayCommand<object>(_ => RestartAsAdministrator());

        SelectAllTweaksCommand = new RelayCommand<object>(_ => SelectAllFilteredTweaks());
        DeselectAllTweaksCommand = new RelayCommand<object>(_ => DeselectAllFilteredTweaks());
        ApplyAllTweaksCommand = new AsyncRelayCommand(ApplyAllFilteredTweaksAsync, () => !IsBusy, ex => ReportStatus($"Ошибка: {ex.Message}", true));

        ApplyAllProcessorTimerGamingCommand = new AsyncRelayCommand(ApplyAllProcessorTimerGamingAsync, () => !IsBusy, ex => ReportStatus($"Ошибка: {ex.Message}", true));
        RevertProcessorTimerCommand = new AsyncRelayCommand(RevertProcessorTimerAsync, () => !IsBusy, ex => ReportStatus($"Ошибка: {ex.Message}", true));
        SelectAllCleanupCategoriesCommand = new RelayCommand<object>(_ => SelectAllCleanupCategories());
        FlushDnsQuickCommand = new AsyncRelayCommand(FlushDnsQuickAsync, () => !IsBusy, ex => ReportStatus($"Ошибка: {ex.Message}", true));

        BuildSimpleFeatures();

        _tweakPropertyChangedHandler = (_, args) =>
        {
            if (args.PropertyName is nameof(TweakItemViewModel.IsSelected) or
                nameof(TweakItemViewModel.CanApply) or nameof(TweakItemViewModel.State))
            {
                NotifyTweakSummaryChanged();
                SyncSimpleFeatures();
            }
        };

        foreach (var tweak in Tweaks)
        {
            tweak.PropertyChanged += _tweakPropertyChangedHandler;
        }

        _cleanupPropertyChangedHandler = (_, args) =>
        {
            if (args.PropertyName == nameof(CleanupCategoryViewModel.IsSelected))
            {
                OnPropertyChanged(nameof(CanScanCleanup));
                ScanCleanupCommand.RaiseCanExecuteChanged();
            }
        };

        foreach (var category in CleanupCategories)
        {
            category.PropertyChanged += _cleanupPropertyChangedHandler;
        }
    }

    private readonly System.ComponentModel.PropertyChangedEventHandler _powerPropertyChangedHandler;
    private readonly System.ComponentModel.PropertyChangedEventHandler _coreParkingPropertyChangedHandler;
    private readonly System.ComponentModel.PropertyChangedEventHandler _tweakPropertyChangedHandler;
    private readonly System.ComponentModel.PropertyChangedEventHandler _cleanupPropertyChangedHandler;

    public enum ActiveView
    {
        Welcome,
        Simple,
        Pro,
        Monitoring
    }

    private ActiveView _currentView = ActiveView.Welcome;
    public ActiveView CurrentView
    {
        get => _currentView;
        set
        {
            if (SetProperty(ref _currentView, value))
            {
                OnPropertyChanged(nameof(IsWelcomeVisible));
                OnPropertyChanged(nameof(IsSimpleModeVisible));
                OnPropertyChanged(nameof(IsProModeVisible));
                OnPropertyChanged(nameof(IsMonitoringVisible));
                OnPropertyChanged(nameof(IsSimpleMode));
                OnPropertyChanged(nameof(IsProMode));
                OnPropertyChanged(nameof(UserModeLabel));

                Storage.IsAdvancedVisible = (value == ActiveView.Pro);
                Network.IsAdvancedVisible = (value == ActiveView.Pro);

                if (value == ActiveView.Monitoring)
                {
                    Monitoring.Start();
                }
                else
                {
                    Monitoring.Stop();
                }
            }
        }
    }

    public bool IsWelcomeVisible => CurrentView == ActiveView.Welcome;
    public bool IsSimpleModeVisible => CurrentView == ActiveView.Simple;
    public bool IsProModeVisible => CurrentView == ActiveView.Pro;
    public bool IsMonitoringVisible => CurrentView == ActiveView.Monitoring;

    public bool IsSimpleMode
    {
        get => CurrentView == ActiveView.Simple;
        set
        {
            if (value)
            {
                CurrentView = ActiveView.Simple;
            }
        }
    }

    public bool IsProMode
    {
        get => CurrentView == ActiveView.Pro;
        set
        {
            if (value)
            {
                CurrentView = ActiveView.Pro;
            }
        }
    }

    public string UserModeLabel => CurrentView switch
    {
        ActiveView.Welcome => "Информация и возможности",
        ActiveView.Simple => "Базовый режим (Экспресс)",
        ActiveView.Pro => "Продвинутый режим (Pro)",
        ActiveView.Monitoring => "Аппаратный мониторинг",
        _ => "Aurum"
    };

    public ObservableCollection<SimpleFeatureItemViewModel> SimpleFeatures { get; }
    public ObservableCollection<SimpleFeatureItemViewModel> FilteredSimpleFeatures { get; }
    public RelayCommand<string> SetSimpleCategoryCommand { get; }
    public AsyncRelayCommand ApplySimpleGamingPresetCommand { get; }
    public AsyncRelayCommand RevertAllSimpleCommand { get; }
    public RelayCommand<object> RestartAsAdministratorCommand { get; }

    private string _simpleSearchText = string.Empty;
    public string SimpleSearchText
    {
        get => _simpleSearchText;
        set
        {
            if (SetProperty(ref _simpleSearchText, value))
            {
                ApplySimpleFilter();
            }
        }
    }

    private string _selectedSimpleCategory = "all";
    public string SelectedSimpleCategory
    {
        get => _selectedSimpleCategory;
        private set => SetProperty(ref _selectedSimpleCategory, value);
    }

    public ObservableCollection<AuditEntry> AuditEntries { get; }

    private bool _hasAuditEntries;
    public bool HasAuditEntries
    {
        get => _hasAuditEntries;
        private set => SetProperty(ref _hasAuditEntries, value);
    }

    public ObservableCollection<TweakItemViewModel> Tweaks { get; }
    public ObservableCollection<TweakItemViewModel> FilteredTweaks { get; }
    public RelayCommand<string> SetTweakCategoryFilterCommand { get; }

    private string _tweakSearchText = string.Empty;
    public string TweakSearchText
    {
        get => _tweakSearchText;
        set
        {
            if (SetProperty(ref _tweakSearchText, value))
            {
                ApplyTweakFilter();
            }
        }
    }

    private string _tweakCategoryFilter = "all";
    public string TweakCategoryFilter
    {
        get => _tweakCategoryFilter;
        private set
        {
            if (SetProperty(ref _tweakCategoryFilter, value))
            {
                ApplyTweakFilter();
            }
        }
    }

    public int AppliedTweaksCount => Tweaks.Count(static t => t.State is TweakStateKind.Applied or TweakStateKind.AlreadyConfigured);
    public int TotalTweaksCount => Tweaks.Count;

    public void SetTweakCategoryFilter(string? category)
    {
        TweakCategoryFilter = string.IsNullOrWhiteSpace(category) ? "all" : category;
    }

    public void ApplyTweakFilter()
    {
        var search = TweakSearchText?.Trim() ?? string.Empty;
        var filter = (TweakCategoryFilter ?? "all").ToLowerInvariant();

        FilteredTweaks.Clear();
        foreach (var tweak in Tweaks)
        {
            if (!string.IsNullOrEmpty(search))
            {
                var matchesName = tweak.Name.Contains(search, StringComparison.OrdinalIgnoreCase);
                var matchesDesc = tweak.Description.Contains(search, StringComparison.OrdinalIgnoreCase);
                var matchesCategory = tweak.Category.Contains(search, StringComparison.OrdinalIgnoreCase);
                var matchesReg = tweak.RegistryPaths.Contains(search, StringComparison.OrdinalIgnoreCase);
                if (!matchesName && !matchesDesc && !matchesCategory && !matchesReg)
                {
                    continue;
                }
            }

            var matchesFilter = filter switch
            {
                "safe" => tweak.IsSafe,
                "gaming" => tweak.Category.Contains("ИГР") || tweak.Category.Contains("GAME"),
                "privacy" => tweak.Category.Contains("КОНФИДЕНЦ") || tweak.Category.Contains("PRIVACY"),
                "kernel" => tweak.Category.Contains("ЯДР") || tweak.Category.Contains("KERNEL"),
                "modified" => tweak.State == TweakStateKind.Drifted,
                "applied" => tweak.State is TweakStateKind.Applied or TweakStateKind.AlreadyConfigured,
                _ => true
            };

            if (matchesFilter)
            {
                FilteredTweaks.Add(tweak);
            }
        }
    }

    public ObservableCollection<AtlasCheckItemViewModel> AtlasChecks { get; }
    public ObservableCollection<CleanupCategoryViewModel> CleanupCategories { get; }
    public ObservableCollection<CleanupCandidate> CleanupFiles { get; }
    public MonitoringViewModel Monitoring { get; }
    public PowerPlanViewModel Power { get; }
    public StorageViewModel Storage { get; }
    public CoreParkingViewModel CoreParking { get; }
    public ServicesViewModel Services { get; }
    public NetworkViewModel Network { get; }
    public MsiViewModel Msi { get; }
    public SystemTimerViewModel Timer { get; }
    public RelayCommand<string> SelectProfileCommand { get; }
    public RelayCommand<object> SetWelcomeModeCommand { get; }
    public RelayCommand<object> OpenMonitoringCommand { get; }
    public RelayCommand<object> SetProModeCommand { get; }
    public RelayCommand<object> SetSimpleModeCommand { get; }
    public RelayCommand<object> ToggleUserModeCommand { get; }
    public RelayCommand<string> NavigateFromWelcomeCommand { get; }
    public AsyncRelayCommand ApplySelectedCommand { get; }
    public AsyncRelayCommand RefreshAllCommand { get; }
    public AsyncRelayCommand CheckAtlasCommand { get; }
    public AsyncRelayCommand ScanCleanupCommand { get; }
    public AsyncRelayCommand CleanCommand { get; }
    public AsyncRelayCommand ApplyAllSimpleCurrentCategoryCommand { get; }
    public RelayCommand<object> SelectAllTweaksCommand { get; }
    public RelayCommand<object> DeselectAllTweaksCommand { get; }
    public AsyncRelayCommand ApplyAllTweaksCommand { get; }
    public AsyncRelayCommand ApplyAllProcessorTimerGamingCommand { get; }
    public AsyncRelayCommand RevertProcessorTimerCommand { get; }
    public RelayCommand<object> SelectAllCleanupCategoriesCommand { get; }
    public AsyncRelayCommand FlushDnsQuickCommand { get; }

    public string SimpleCategoryTitle => SelectedSimpleCategory switch
    {
        "gaming" => "Гейминг и задержки",
        "system" => "Система и ядро",
        "privacy" => "Приватность и телеметрия",
        _ => "Все разделы"
    };

    public string TimerStatusBadge => Timer.Is05MsActive ? "0.500 мс (2000 Гц)" : $"{Timer.Info.CurrentMs:0.000} мс";
    public string MsiStatusBadge => Msi.Devices.Any(d => d.Category == MsiDeviceCategory.Gpu && d.IsMsiSupported && d.Priority == MsiDevicePriority.High)
        ? "GPU High Priority"
        : "Стандарт";

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string LastCheckLabel
    {
        get => _lastCheckLabel;
        private set => SetProperty(ref _lastCheckLabel, value);
    }

    public string AtlasSummary
    {
        get => _atlasSummary;
        private set => SetProperty(ref _atlasSummary, value);
    }

    public string CleanupSummary
    {
        get => _cleanupSummary;
        private set => SetProperty(ref _cleanupSummary, value);
    }

    public bool HasError
    {
        get => _hasError;
        private set => SetProperty(ref _hasError, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanApplySelected));
                OnPropertyChanged(nameof(CanScanCleanup));
                OnPropertyChanged(nameof(CanClean));
                ApplySelectedCommand.RaiseCanExecuteChanged();
                RefreshAllCommand.RaiseCanExecuteChanged();
                CheckAtlasCommand.RaiseCanExecuteChanged();
                ScanCleanupCommand.RaiseCanExecuteChanged();
                CleanCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public SystemSnapshot? System
    {
        get => _system;
        private set
        {
            if (SetProperty(ref _system, value))
            {
                OnPropertyChanged(nameof(WindowsLabel));
                OnPropertyChanged(nameof(BuildLabel));
                OnPropertyChanged(nameof(AccessLabel));
                OnPropertyChanged(nameof(AtlasLabel));
                OnPropertyChanged(nameof(IsElevationWarningVisible));
            }
        }
    }

    public string WindowsLabel => System?.OperatingSystem ?? "Определение…";
    public string BuildLabel => System is null ? "—" : $"Build {System.WindowsBuild} · {System.Architecture}";
    public string AccessLabel => System is null
        ? "—"
        : System.IsAdministrator ? "Администратор" : "Обычный пользователь";

    /// <summary>
    /// Aurum keeps the asInvoker manifest so that monitoring, diagnostics and browsing the
    /// catalog work without a UAC prompt. Everything that writes needs elevation, though,
    /// and without this banner the user only found that out from an error after choosing
    /// what to change.
    /// </summary>
    public bool IsElevationWarningVisible => System is not null && !System.IsAdministrator;
    public string AtlasLabel => System is null
        ? "—"
        : System.AtlasMarkerDetected ? "Маркеры найдены" : "Маркеры не найдены";
    public int TrackedTweakCount => Tweaks.Count(static tweak =>
        tweak.State is TweakStateKind.Applied or TweakStateKind.Drifted);
    public int DriftedTweakCount => Tweaks.Count(static tweak => tweak.State == TweakStateKind.Drifted);
    public string TweakHealthLabel => DriftedTweakCount == 0
        ? $"Отслеживается: {TrackedTweakCount} · всё актуально"
        : $"Требуют восстановления: {DriftedTweakCount}";
    public bool CanApplySelected => !IsBusy && Tweaks.Any(static tweak => tweak.IsSelected && tweak.CanApply);
    public bool CanScanCleanup => !IsBusy && CleanupCategories.Any(static category => category.IsSelected);
    public bool CanClean => !IsBusy && CleanupFiles.Count != 0;

    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set
        {
            if (!SetProperty(ref _selectedTabIndex, value))
            {
                return;
            }

            if (value == 1)
            {
                _ = Services.RefreshAsync();
            }
            else if (value == 2)
            {
                _ = CoreParking.RefreshAsync();
            }
            else if (value == 3)
            {
                _ = Msi.RefreshAsync();
            }
            else if (value == 4)
            {
                _ = Power.RefreshAsync();
            }
            else if (value == 5)
            {
                _ = Network.RefreshAsync();
            }
            else if (value == 6)
            {
                _ = Storage.RefreshAsync();
            }
            else if (value == 8)
            {
                _ = CheckAtlasAsync();
            }
        }
    }

    public async Task InitializeAsync()
    {
        IsBusy = true;
        try
        {
            System = _systemProbe.Capture();
            Timer.Refresh();

            // Run independent initializations and system probes in parallel for instant cold start
            await Task.WhenAll(
                Monitoring.InitializeAsync(),
                Power.InitializeAsync(),
                Storage.InitializeAsync(),
                CoreParking.InitializeAsync(),
                Services.InitializeAsync(),
                Network.InitializeAsync(),
                Msi.InitializeAsync(),
                RefreshTweaksInternalAsync(),
                CheckAtlasInternalAsync());

            SyncSimpleFeatures();
            await ReloadAuditAsync();
            ReportStatus(
                $"Проверка завершена. {TweakHealthLabel}. Очистка запускается только вручную.",
                DriftedTweakCount != 0);
        }
        catch (Exception error)
        {
            ReportStatus($"Диагностика завершилась с ошибкой: {error.Message}", true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void Dispose()
    {
        Power.PropertyChanged -= _powerPropertyChangedHandler;
        CoreParking.PropertyChanged -= _coreParkingPropertyChangedHandler;

        foreach (var tweak in Tweaks)
        {
            tweak.PropertyChanged -= _tweakPropertyChangedHandler;
        }

        foreach (var category in CleanupCategories)
        {
            category.PropertyChanged -= _cleanupPropertyChangedHandler;
        }

        Monitoring.Dispose();
        (Timer as IDisposable)?.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task RefreshAllAsync()
    {
        IsBusy = true;
        try
        {
            await RefreshTweaksInternalAsync();
            SyncSimpleFeatures();
            ReportStatus($"Настройки перепроверены. {TweakHealthLabel}.", DriftedTweakCount != 0);
        }
        catch (Exception error)
        {
            ReportStatus($"Не удалось проверить настройки: {error.Message}", true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RefreshTweaksInternalAsync()
    {
        foreach (var tweak in Tweaks)
        {
            await tweak.RefreshAsync();
        }

        LastCheckLabel = $"Проверено {DateTime.Now:dd.MM.yyyy HH:mm:ss}";
        NotifyTweakSummaryChanged();
    }

    private async Task CheckAtlasAsync()
    {
        IsBusy = true;
        try
        {
            await CheckAtlasInternalAsync();
            var failed = AtlasChecks.Count(static check => check.Result.Status == HealthCheckStatus.Failed);
            ReportStatus(
                failed == 0
                    ? "Проверка AtlasOS завершена без критических нарушений."
                    : $"Проверка AtlasOS обнаружила нарушений: {failed}.",
                failed != 0);
        }
        catch (Exception error)
        {
            ReportStatus($"Не удалось проверить AtlasOS: {error.Message}", true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task CheckAtlasInternalAsync()
    {
        var report = await _atlasHealthService.CheckAsync();
        AtlasChecks.Clear();
        foreach (var check in report.Checks)
        {
            AtlasChecks.Add(new AtlasCheckItemViewModel(check));
        }

        var failed = report.Checks.Count(static check => check.Status == HealthCheckStatus.Failed);
        var warnings = report.Checks.Count(static check => check.Status == HealthCheckStatus.Warning);
        AtlasSummary = report.IsDetected
            ? $"{report.VersionLabel} · нарушений: {failed} · предупреждений: {warnings} · {report.CheckedAt:HH:mm:ss}"
            : $"AtlasOS не обнаружен · {report.CheckedAt:HH:mm:ss}";
    }

    private async Task ScanCleanupAsync()
    {
        IsBusy = true;
        try
        {
            await ScanCleanupInternalAsync();
            ReportStatus(
                CleanupFiles.Count == 0
                    ? "Безопасные кандидаты для очистки не найдены."
                    : $"Сканирование завершено: {CleanupFiles.Count} файлов, " +
                      $"{FormatBytes(CleanupFiles.Sum(static file => file.Length))}.",
                false);
        }
        catch (Exception error)
        {
            ReportStatus($"Сканирование очистки завершилось с ошибкой: {error.Message}", true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ScanCleanupInternalAsync()
    {
        CleanupSummary = "Сканирование выбранных каталогов…";
        var categoryIds = CleanupCategories
            .Where(static category => category.IsSelected)
            .Select(static category => category.Id)
            .ToArray();
        var result = await _cleanupService.ScanAsync(categoryIds);

        CleanupFiles.Clear();
        foreach (var candidate in result.Candidates.OrderByDescending(static candidate => candidate.Length))
        {
            CleanupFiles.Add(candidate);
        }

        CleanupSummary = $"Найдено {result.Candidates.Count} файлов · {FormatBytes(result.TotalBytes)}" +
                         (result.IsTruncated ? " · список ограничен 50 000 файлами" : string.Empty) +
                         (result.Errors.Count == 0 ? string.Empty : $" · недоступно: {result.Errors.Count}");
        OnPropertyChanged(nameof(CanClean));
        CleanCommand.RaiseCanExecuteChanged();
    }

    private async Task CleanAsync()
    {
        var candidates = CleanupFiles.ToArray();
        var totalBytes = candidates.Sum(static candidate => candidate.Length);
        if (!_confirm(
                $"Удалить {candidates.Length} файлов ({FormatBytes(totalBytes)})?\n\n" +
                "Aurum удалит только показанные файлы. Изменившиеся после сканирования файлы будут пропущены."))
        {
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _cleanupService.CleanAsync(candidates);
            await ScanCleanupInternalAsync();
            ReportStatus(
                $"Удалено файлов: {result.DeletedCount}, освобождено: {FormatBytes(result.FreedBytes)}, " +
                $"пропущено: {result.SkippedCount}.",
                result.Errors.Count != 0);
        }
        catch (Exception error)
        {
            ReportStatus($"Очистка завершилась с ошибкой: {error.Message}", true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void SelectProfile(string? profileId)
    {
        var profile = BuiltInTweakCatalog.Profiles.FirstOrDefault(profile => profile.Id == profileId);
        if (profile is null)
        {
            return;
        }

        foreach (var tweak in Tweaks)
        {
            tweak.IsSelected = profile.TweakIds.Contains(tweak.Definition.Id) && tweak.CanApply;
        }

        ReportStatus($"Профиль {profile.Name} только выбрал настройки. Проверьте список перед применением.", false);
    }

    private async Task ApplySelectedAsync()
    {
        var selected = Tweaks.Where(static tweak => tweak.IsSelected && tweak.CanApply).ToArray();
        if (selected.Length == 0)
        {
            return;
        }

        IsBusy = true;
        try
        {
            foreach (var tweak in selected)
            {
                if (!await tweak.TryApplyAsync())
                {
                    ReportStatus(
                        "Пакет остановлен после ошибки. Ранее применённые настройки сохранены и доступны для отката.",
                        true);
                    break;
                }
            }

            LastCheckLabel = $"Проверено {DateTime.Now:dd.MM.yyyy HH:mm:ss}";
            NotifyTweakSummaryChanged();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void NotifyTweakSummaryChanged()
    {
        OnPropertyChanged(nameof(CanApplySelected));
        OnPropertyChanged(nameof(TrackedTweakCount));
        OnPropertyChanged(nameof(DriftedTweakCount));
        OnPropertyChanged(nameof(TweakHealthLabel));
        OnPropertyChanged(nameof(AppliedTweaksCount));
        OnPropertyChanged(nameof(TotalTweaksCount));
        ApplySelectedCommand.RaiseCanExecuteChanged();
        ApplyTweakFilter();
    }

    private void ReportStatus(string message, bool isError)
    {
        StatusMessage = message;
        HasError = isError;
        _ = ReloadAuditAsync();
    }

    private async Task ReloadAuditAsync()
    {
        try
        {
            var entries = await _auditJournal.ReadRecentAsync(12);
            void Apply()
            {
                AuditEntries.Clear();
                foreach (var entry in entries)
                {
                    AuditEntries.Add(entry);
                }

                HasAuditEntries = AuditEntries.Count > 0;
            }

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.CheckAccess())
            {
                Apply();
            }
            else
            {
                dispatcher.Invoke(Apply);
            }
        }
        catch
        {
            // The journal is diagnostic; a read failure must not hide the last operation result.
        }
    }

    private void RestartAsAdministrator()
    {
        // Environment.ProcessPath rather than the assembly location, which is empty in the
        // single-file release build.
        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable))
        {
            ReportStatus("Не удалось определить путь к исполняемому файлу Aurum.", true);
            return;
        }

        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = true,
            Verb = "runas"
        };

        // Carried over so that a restart from, say, the monitoring shortcut reopens on the
        // same view instead of the welcome screen.
        foreach (var argument in Environment.GetCommandLineArgs().Skip(1))
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            Process.Start(startInfo);
        }
        catch (Win32Exception error) when (error.NativeErrorCode == ErrorCancelled)
        {
            ReportStatus(
                "Запуск с правами администратора отменён. Aurum продолжает работать в режиме чтения.",
                true);
            return;
        }
        catch (Exception error)
        {
            ReportStatus($"Не удалось перезапустить Aurum с правами администратора: {error.Message}", true);
            return;
        }

        Application.Current?.Shutdown();
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["Б", "КБ", "МБ", "ГБ", "ТБ"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.##} {units[unit]}";
    }

    private void SetSimpleCategory(string? category)
    {
        SelectedSimpleCategory = string.IsNullOrWhiteSpace(category) ? "all" : category;
        ApplySimpleFilter();
    }

    private void ApplySimpleFilter()
    {
        FilteredSimpleFeatures.Clear();
        var query = SimpleSearchText?.Trim() ?? string.Empty;

        foreach (var feature in SimpleFeatures)
        {
            var matchesCategory = SelectedSimpleCategory == "all" ||
                                  string.Equals(feature.Category, SelectedSimpleCategory, StringComparison.OrdinalIgnoreCase);

            var matchesSearch = string.IsNullOrEmpty(query) ||
                                feature.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                feature.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                feature.Badges.Any(b => b.Contains(query, StringComparison.OrdinalIgnoreCase));

            if (matchesCategory && matchesSearch)
            {
                FilteredSimpleFeatures.Add(feature);
            }
        }
    }

    public void SyncSimpleFeatures()
    {
        foreach (var feature in SimpleFeatures)
        {
            switch (feature.Id)
            {
                case "msi":
                    feature.UpdateActiveStateDirectly(Msi.Devices.Any(d =>
                        (d.Category == MsiDeviceCategory.Gpu || d.Category == MsiDeviceCategory.Network) &&
                        d.IsMsiSupported && d.Priority == MsiDevicePriority.High));
                    break;
                case "timer":
                    feature.UpdateActiveStateDirectly(Timer.Is05MsActive);
                    break;
                case "win32-priority":
                    feature.UpdateActiveStateDirectly(IsTweakApplied(TweakIds.Kernel.Win32PrioritySeparation));
                    break;
                case "paging-executive":
                    feature.UpdateActiveStateDirectly(IsTweakApplied(TweakIds.Kernel.DisablePagingExecutive));
                    break;
                case "system-responsiveness":
                    feature.UpdateActiveStateDirectly(IsTweakApplied(TweakIds.Kernel.SystemResponsiveness));
                    break;
                case "network-throttling":
                    feature.UpdateActiveStateDirectly(IsTweakApplied(TweakIds.Network.DisableThrottling));
                    break;
                case "gamedvr":
                    feature.UpdateActiveStateDirectly(
                        IsTweakApplied(TweakIds.Gaming.DisableBackgroundCapture) &&
                        IsTweakApplied(TweakIds.Gaming.DisableXboxGameBar));
                    break;
                case "core-parking":
                    feature.UpdateActiveStateDirectly(CoreParking.IsManagedPlanActive);
                    break;
                case "context-menu":
                    feature.UpdateActiveStateDirectly(IsTweakApplied(TweakIds.Explorer.ClassicContextMenu));
                    break;
                case "telemetry":
                    feature.UpdateActiveStateDirectly(
                        IsTweakApplied(TweakIds.Privacy.DisableAdvertisingId) ||
                        IsTweakApplied(TweakIds.Privacy.DisableFeedbackRequests) ||
                        IsTweakApplied(TweakIds.Privacy.DisableTailoredExperiences) ||
                        IsTweakApplied(TweakIds.Privacy.DisableSearchWebResults));
                    break;
                case "bing-search":
                    feature.UpdateActiveStateDirectly(IsTweakApplied(TweakIds.Privacy.DisableSearchWebResults));
                    break;
                case "defender-control":
                    feature.UpdateActiveStateDirectly(IsTweakApplied(TweakIds.Gaming.DisableDefenderRealtime));
                    break;
                case "uac-control":
                    feature.UpdateActiveStateDirectly(IsTweakApplied(TweakIds.Gaming.DisableUac));
                    break;
                case "winupdate-control":
                    feature.UpdateActiveStateDirectly(IsTweakApplied(TweakIds.Gaming.DisableWindowsUpdate));
                    break;
                case "systemrestore-control":
                    feature.UpdateActiveStateDirectly(IsTweakApplied(TweakIds.Gaming.DisableSystemRestore));
                    break;
                case "ssd-trim":
                    feature.UpdateActiveStateDirectly(false);
                    break;
            }
        }
    }

    private void BuildSimpleFeatures()
    {
        SimpleFeatures.Clear();

        // 1. Гейминг и задержки
        SimpleFeatures.Add(new SimpleFeatureItemViewModel(
            "msi",
            "Аппаратные прерывания MSI (GPU & Сеть)",
            "gaming",
            "Гейминг и задержки",
            "⚡",
            "Переводит видеокарту и сетевую карту в режим Message Signaled Interrupts с высоким приоритетом, устраняя очереди IRQ и снижая DPC Latency.",
            ["🚀 eSports Latency", "🛡️ Безопасно", "⚡ Рекомендуется"],
            () => Msi.ApplyGamingPresetAsync(),
            () => Msi.RevertAsync(),
            ReportStatus));

        SimpleFeatures.Add(new SimpleFeatureItemViewModel(
            "timer",
            "Высокоточный системный таймер 0.500 мс (2000 Гц)",
            "gaming",
            "Гейминг и задержки",
            "⏱️",
            "Повышает частоту системного таймера до 2000 Гц для синхронизации игрового цикла с высокоскоростными мышами (1000–8000 Гц).",
            ["⚡ 2000 Hz", "🎯 Плавность ввода", "🚀 eSports"],
            () => Task.FromResult(Timer.SetResolution(0.5)),
            () => Task.FromResult(Timer.ResetResolution()),
            ReportStatus));

        SimpleFeatures.Add(new SimpleFeatureItemViewModel(
            "win32-priority",
            "Квантовый приоритет процессора (Win32Priority 0x26)",
            "gaming",
            "Гейминг и задержки",
            "🧠",
            "Выделяет максимальные короткие кванты процессорного времени активному окну игры (Quantum Boost).",
            ["🎮 Quantum Boost", "⚡ Рекомендуется"],
            () => ApplyTweakByIdAsync(TweakIds.Kernel.Win32PrioritySeparation),
            () => RevertTweakByIdAsync(TweakIds.Kernel.Win32PrioritySeparation),
            ReportStatus));

        SimpleFeatures.Add(new SimpleFeatureItemViewModel(
            "system-responsiveness",
            "100% мощности процессора игре (Responsiveness 0)",
            "gaming",
            "Гейминг и задержки",
            "🎯",
            "Отключает 20% системное резервирование мощности процессора для фоновых мультимедийных служб.",
            ["🎮 100% CPU Game", "⚡ Рекомендуется"],
            () => ApplyTweakByIdAsync(TweakIds.Kernel.SystemResponsiveness),
            () => RevertTweakByIdAsync(TweakIds.Kernel.SystemResponsiveness),
            ReportStatus));

        SimpleFeatures.Add(new SimpleFeatureItemViewModel(
            "network-throttling",
            "Отключение троттлинга сетевых пакетов",
            "gaming",
            "Гейминг и задержки",
            "🌐",
            "Снимает искусственное ограничение пропускной способности не-мультимедийных сетевых пакетов для стабильного пинга.",
            ["⚡ Низкий пинг", "🌐 Без джиттера"],
            () => ApplyTweakByIdAsync(TweakIds.Network.DisableThrottling),
            () => RevertTweakByIdAsync(TweakIds.Network.DisableThrottling),
            ReportStatus));

        SimpleFeatures.Add(new SimpleFeatureItemViewModel(
            "gamedvr",
            "Отключение фонового захвата GameDVR и Game Bar",
            "gaming",
            "Гейминг и задержки",
            "🚀",
            "Отключает встроенную фоновую запись экрана Xbox и оверлей, устраняя микростаттеры и экономя ресурсы GPU.",
            ["🛡️ Без фоновой записи", "🎮 FPS"],
            async () =>
            {
                var capture = await ApplyTweakByIdAsync(TweakIds.Gaming.DisableBackgroundCapture);
                var gameBar = await ApplyTweakByIdAsync(TweakIds.Gaming.DisableXboxGameBar);
                return capture && gameBar;
            },
            async () =>
            {
                var capture = await RevertTweakByIdAsync(TweakIds.Gaming.DisableBackgroundCapture);
                var gameBar = await RevertTweakByIdAsync(TweakIds.Gaming.DisableXboxGameBar);
                return capture && gameBar;
            },
            ReportStatus));

        // 2. Система и ядро
        SimpleFeatures.Add(new SimpleFeatureItemViewModel(
            "core-parking",
            "Распарковка 100% ядер процессора (Core Parking)",
            "system",
            "Система и ядро",
            "🔌",
            "Держит все ядра процессора активными в изолированной схеме питания Aurum, исключая задержки пробуждения ядер.",
            ["⚡ 100% Unpark", "🚀 Моментальный отклик"],
            () =>
            {
                CoreParking.MinimumAc = 100;
                CoreParking.MaximumAc = 100;
                CoreParking.MinimumDc = 100;
                CoreParking.MaximumDc = 100;
                return CoreParking.ApplyDirectAsync();
            },
            () => CoreParking.RevertDirectAsync(),
            ReportStatus));

        SimpleFeatures.Add(new SimpleFeatureItemViewModel(
            "paging-executive",
            "Удержание ядра Windows в оперативной памяти",
            "system",
            "Система и ядро",
            "💾",
            "Запрещает операционной системе сбрасывать компоненты ядра и системных драйверов в файл подкачки на диск.",
            ["🧠 RAM Kernel", "⚡ Быстрый отклик"],
            () => ApplyTweakByIdAsync(TweakIds.Kernel.DisablePagingExecutive),
            () => RevertTweakByIdAsync(TweakIds.Kernel.DisablePagingExecutive),
            ReportStatus));

        SimpleFeatures.Add(new SimpleFeatureItemViewModel(
            "context-menu",
            "Классическое контекстное меню Windows 10 в Win 11",
            "system",
            "Система и ядро",
            "🖱️",
            "Возвращает быстрое классическое контекстное меню по правому клику мыши без лишнего подменю «Показать дополнительные параметры».",
            ["⚡ Мгновенный клик", "🖱️ Удобство"],
            () => ApplyTweakByIdAsync(TweakIds.Explorer.ClassicContextMenu),
            () => RevertTweakByIdAsync(TweakIds.Explorer.ClassicContextMenu),
            ReportStatus));

        SimpleFeatures.Add(new SimpleFeatureItemViewModel(
            "ssd-trim",
            "Оптимизация SSD ячеек (Безопасный ReTrim)",
            "system",
            "Система и ядро",
            "💾",
            "Отправляет команду очистки неиспользуемых блоков TRIM на все SSD/NVMe накопители с аппаратной защитой от запуска на HDD.",
            ["⚡ Скорость SSD", "🛡️ Защита HDD"],
            () => Storage.OptimizeAllSsdDirectAsync(),
            // ReTrim is a one-shot maintenance command with no inverse, so there is
            // nothing to undo. SyncSimpleFeatureStates always resets this toggle to off.
            () => Task.FromResult(true),
            ReportStatus));

        SimpleFeatures.Add(new SimpleFeatureItemViewModel(
            "defender-control",
            "Защитник Windows (Real-Time мониторинг)",
            "system",
            "Система и ядро",
            "🛡️",
            "Отключает сканирование файлов в реальном времени и SmartScreen для устранения микрофризов при подгрузке игровых ассетов.",
            ["⚡ Производительность", "🛡️ Real-Time"],
            () => ApplyTweakByIdAsync(TweakIds.Gaming.DisableDefenderRealtime),
            () => RevertTweakByIdAsync(TweakIds.Gaming.DisableDefenderRealtime),
            ReportStatus));

        SimpleFeatures.Add(new SimpleFeatureItemViewModel(
            "uac-control",
            "Контроль учётных записей (UAC)",
            "system",
            "Система и ядро",
            "🛠️",
            "Отключает диалоговые окна подтверждения администратора и затемнение рабочего стола (Secure Desktop).",
            ["⚡ Без пауз", "🛠️ UAC"],
            () => ApplyTweakByIdAsync(TweakIds.Gaming.DisableUac),
            () => RevertTweakByIdAsync(TweakIds.Gaming.DisableUac),
            ReportStatus));

        SimpleFeatures.Add(new SimpleFeatureItemViewModel(
            "winupdate-control",
            "Автообновления Windows",
            "system",
            "Система и ядро",
            "🔄",
            "Запрещает операционной системе автоматически скачивать и устанавливать обновления в фоне во время матчей.",
            ["⚡ Стабильный пинг", "🛡️ Без скачков CPU"],
            () => ApplyTweakByIdAsync(TweakIds.Gaming.DisableWindowsUpdate),
            () => RevertTweakByIdAsync(TweakIds.Gaming.DisableWindowsUpdate),
            ReportStatus));

        SimpleFeatures.Add(new SimpleFeatureItemViewModel(
            "systemrestore-control",
            "Точки восстановления системы (VSS)",
            "system",
            "Система и ядро",
            "💾",
            "Отключает автоматические теневые копии томов для экономии ресурса ячеек SSD/NVMe и дискового пространства.",
            ["💾 Экономия SSD", "⚡ Без VSS"],
            () => ApplyTweakByIdAsync(TweakIds.Gaming.DisableSystemRestore),
            () => RevertTweakByIdAsync(TweakIds.Gaming.DisableSystemRestore),
            ReportStatus));

        // 3. Приватность
        SimpleFeatures.Add(new SimpleFeatureItemViewModel(
            "telemetry",
            "Отключение телеметрии и сбора данных",
            "privacy",
            "Приватность",
            "🛡️",
            "Минимизирует отправку диагностических данных и телеметрии в Microsoft, отключая рекламный ID, запросы отзывов и персонализацию.",
            ["🛡️ Приватность", "🔒 0% телеметрии"],
            async () =>
            {
                var advertisingId = await ApplyTweakByIdAsync(TweakIds.Privacy.DisableAdvertisingId);
                var feedback = await ApplyTweakByIdAsync(TweakIds.Privacy.DisableFeedbackRequests);
                var tailored = await ApplyTweakByIdAsync(TweakIds.Privacy.DisableTailoredExperiences);
                var webResults = await ApplyTweakByIdAsync(TweakIds.Privacy.DisableSearchWebResults);
                return advertisingId && feedback && tailored && webResults;
            },
            async () =>
            {
                var advertisingId = await RevertTweakByIdAsync(TweakIds.Privacy.DisableAdvertisingId);
                var feedback = await RevertTweakByIdAsync(TweakIds.Privacy.DisableFeedbackRequests);
                var tailored = await RevertTweakByIdAsync(TweakIds.Privacy.DisableTailoredExperiences);
                var webResults = await RevertTweakByIdAsync(TweakIds.Privacy.DisableSearchWebResults);
                return advertisingId && feedback && tailored && webResults;
            },
            ReportStatus));

        SimpleFeatures.Add(new SimpleFeatureItemViewModel(
            "bing-search",
            "Отключение веб-поиска Bing в меню Пуск",
            "privacy",
            "Приватность",
            "🔍",
            "Убирает рекламу и медленный веб-поиск из меню Пуск, оставляя только мгновенный локальный поиск программ и файлов.",
            ["⚡ Мгновенный поиск", "🛡️ Без рекламы Bing"],
            () => ApplyTweakByIdAsync(TweakIds.Privacy.DisableSearchWebResults),
            () => RevertTweakByIdAsync(TweakIds.Privacy.DisableSearchWebResults),
            ReportStatus));

        ApplySimpleFilter();
    }

    public async Task ApplySimpleGamingPresetAsync()
    {
        if (!_confirm(
            "Применить рекомендуемый «Игровой пресет Aurum»?\n\n" +
            "Будут настроены:\n" +
            "• Высокоточный системный таймер 0.5 мс (2000 Гц)\n" +
            "• Аппаратные MSI-прерывания для видеокарты\n" +
            "• Разблокировка всех ядер процессора (Core Parking 100%)\n" +
            "• Приоритеты ядра Win32PrioritySeparation (0x26)\n" +
            "• Снятие сетевого троттлинга и выключение GameDVR\n\n" +
            "Все параметры полностью обратимы."))
        {
            return;
        }

        IsBusy = true;
        try
        {
            await Msi.ApplyGamingPresetAsync();
            Timer.SetResolution(0.5);

            await ApplyTweakByIdAsync(TweakIds.Kernel.Win32PrioritySeparation);
            await ApplyTweakByIdAsync(TweakIds.Kernel.DisablePagingExecutive);
            await ApplyTweakByIdAsync(TweakIds.Kernel.SystemResponsiveness);
            await ApplyTweakByIdAsync(TweakIds.Network.DisableThrottling);
            await ApplyTweakByIdAsync(TweakIds.Gaming.DisableBackgroundCapture);
            await ApplyTweakByIdAsync(TweakIds.Gaming.DisableXboxGameBar);
            await ApplyTweakByIdAsync(TweakIds.Explorer.ClassicContextMenu);

            if (!CoreParking.IsManagedPlanActive)
            {
                CoreParking.MinimumAc = 100;
                CoreParking.MaximumAc = 100;
                CoreParking.MinimumDc = 100;
                CoreParking.MaximumDc = 100;
                await CoreParking.ApplyDirectAsync();
            }

            SyncSimpleFeatures();
            ReportStatus("⚡ Рекомендуемый игровой пресет Aurum успешно применён!", false);
        }
        catch (Exception ex)
        {
            ReportStatus($"Не удалось полностью применить пресет: {ex.Message}", true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Undoes everything Aurum tracks, across all seven managers. Each subsystem is
    /// attempted independently: an earlier version stopped at the first exception, which
    /// left the remaining changes applied while reporting a single error, exactly when the
    /// user most needs the rest to be undone.
    /// </summary>
    public async Task RevertEverythingAsync()
    {
        if (!_confirm(
                "Вернуть всё, что Aurum изменил, к исходному состоянию Windows?\n\n" +
                "Будут отменены твики реестра, режим MSI, разрешение системного таймера, " +
                "настройки дисков, парковка ядер, схема питания, DNS и отключённые службы."))
        {
            return;
        }

        IsBusy = true;
        var failures = new List<string>();
        var restored = new List<string>();

        try
        {
            var tweaksToRevert = Tweaks.Where(static tweak => tweak.CanRevert).ToList();
            var revertedTweaks = 0;
            foreach (var tweak in tweaksToRevert)
            {
                if (await tweak.TryRevertAsync())
                {
                    revertedTweaks++;
                }
            }

            if (revertedTweaks > 0)
            {
                restored.Add($"твиков: {revertedTweaks}");
            }

            if (tweaksToRevert.Count != revertedTweaks)
            {
                failures.Add($"твики ({tweaksToRevert.Count - revertedTweaks} не удалось)");
            }

            if (Msi.HasActiveModifications)
            {
                await RevertStepAsync("режим MSI", () => Msi.RevertDirectAsync(), failures, restored);
            }

            if (Timer.ResetResolution())
            {
                restored.Add("таймер");
            }
            else
            {
                failures.Add("таймер");
            }

            await RevertStepAsync("настройки дисков", async () => await Storage.RevertTuningDirectAsync(), failures, restored);

            if (CoreParking.CanRevert)
            {
                await RevertStepAsync("парковка ядер", async () => await CoreParking.RevertDirectAsync(), failures, restored);
            }

            if (Power.CanRevert)
            {
                await RevertStepAsync("схема питания", async () => await Power.RevertDirectAsync(), failures, restored);
            }

            if (Network.CanRevertDns)
            {
                await RevertStepAsync("DNS", () => Network.RevertDnsDirectAsync(), failures, restored);
            }

            try
            {
                var revertedServices = await Services.RevertAllServicesDirectAsync();
                if (revertedServices > 0)
                {
                    restored.Add($"служб: {revertedServices}");
                }
            }
            catch (Exception error)
            {
                failures.Add($"службы ({error.Message})");
            }

            SyncSimpleFeatures();

            if (failures.Count == 0)
            {
                ReportStatus(
                    restored.Count == 0
                        ? "Отслеживаемых изменений не найдено: система уже в исходном состоянии."
                        : $"Возвращено к исходному состоянию — {string.Join(", ", restored)}.",
                    false);
            }
            else
            {
                // Named rather than counted, because the user has to go and finish these by
                // hand and needs to know which ones.
                ReportStatus(
                    $"Откат выполнен частично. Не удалось вернуть: {string.Join("; ", failures)}.",
                    true);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static async Task RevertStepAsync(
        string label,
        Func<Task> step,
        List<string> failures,
        List<string> restored)
    {
        try
        {
            await step();
            restored.Add(label);
        }
        catch (Exception error)
        {
            failures.Add($"{label} ({error.Message})");
        }
    }

    public async Task ApplyAllSimpleCurrentCategoryAsync()
    {
        if (IsBusy) return;
        if (!_confirm($"Включить все оптимизации в текущем разделе ({SimpleCategoryTitle})?\n\nВсе изменения полностью обратимы."))
        {
            return;
        }

        IsBusy = true;
        try
        {
            foreach (var feature in FilteredSimpleFeatures.Where(f => !f.IsActive))
            {
                await feature.ToggleDirectAsync(true);
            }
            SyncSimpleFeatures();
            ReportStatus($"Все оптимизации раздела «{SimpleCategoryTitle}» применены.", false);
        }
        catch (Exception ex)
        {
            ReportStatus($"Ошибка применения настроек: {ex.Message}", true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void SelectAllFilteredTweaks()
    {
        foreach (var tweak in FilteredTweaks)
        {
            if (tweak.CanApply) tweak.IsSelected = true;
        }
    }

    public void DeselectAllFilteredTweaks()
    {
        foreach (var tweak in FilteredTweaks)
        {
            tweak.IsSelected = false;
        }
    }

    public async Task ApplyAllFilteredTweaksAsync()
    {
        SelectAllFilteredTweaks();
        if (CanApplySelected)
        {
            await ApplySelectedAsync();
        }
        else
        {
            ReportStatus("Все выбранные твики уже применены или не требуют изменений.", false);
        }
    }

    public async Task ApplyAllProcessorTimerGamingAsync()
    {
        if (IsBusy) return;
        if (!_confirm("Применить полный комплекс для процессора, прерываний и таймера?\n\n• Системный таймер: 0.500 мс (2000 Гц)\n• Режим MSI: High priority для GPU и Сети\n• Разблокировка ядер (Core Parking): 100% активны\n\nВсе параметры полностью обратимы."))
        {
            return;
        }

        IsBusy = true;
        try
        {
            Timer.Set05MsCommand.Execute(null);
            await Msi.ApplyGamingPresetDirectAsync();
            await CoreParking.ApplyDirectAsync();
            ReportStatus("Комплекс оптимизации процессора и таймера успешно активирован.", false);
        }
        catch (Exception ex)
        {
            ReportStatus($"Ошибка оптимизации процессора: {ex.Message}", true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task RevertProcessorTimerAsync()
    {
        if (IsBusy) return;
        if (!_confirm("Сбросить параметры процессора, MSI и таймера к стандартным значениям Windows?"))
        {
            return;
        }

        IsBusy = true;
        try
        {
            Timer.SetDefaultCommand.Execute(null);
            await Msi.RevertDirectAsync();
            await CoreParking.RevertDirectAsync();
            ReportStatus("Параметры процессора и таймера возвращены к стандартным.", false);
        }
        catch (Exception ex)
        {
            ReportStatus($"Ошибка возврата параметров: {ex.Message}", true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void SelectAllCleanupCategories()
    {
        foreach (var cat in CleanupCategories)
        {
            cat.IsSelected = true;
        }
    }

    public async Task FlushDnsQuickAsync()
    {
        try
        {
            await Network.FlushDnsCommand.ExecuteAsync(null);
            ReportStatus("Кэш DNS успешно очищен.", false);
        }
        catch (Exception ex)
        {
            ReportStatus($"Ошибка сброса DNS: {ex.Message}", true);
        }
    }

    private async Task<bool> ApplyTweakByIdAsync(string tweakId)
    {
        var tweak = Tweaks.FirstOrDefault(t => t.Definition.Id == tweakId);
        if (tweak is null) return false;
        await tweak.TryApplyAsync();
        return tweak.State == TweakStateKind.Applied;
    }

    private async Task<bool> RevertTweakByIdAsync(string tweakId)
    {
        var tweak = Tweaks.FirstOrDefault(t => t.Definition.Id == tweakId);
        if (tweak is null) return false;
        await tweak.TryRevertAsync();
        return tweak.State != TweakStateKind.Applied;
    }

    private bool IsTweakApplied(string tweakId)
    {
        var tweak = Tweaks.FirstOrDefault(t => t.Definition.Id == tweakId);
        return tweak?.State == TweakStateKind.Applied;
    }
}

