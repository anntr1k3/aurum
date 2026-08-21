using System.Windows;
using Aurum.App.ViewModels;
using Aurum.Core;
using Aurum.Infrastructure.Windows;

namespace Aurum.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();

        var registryStore = new WindowsRegistryStore();
        var stateRepository = new JsonTweakStateRepository();
        var engine = new TweakEngine(registryStore, stateRepository);
        var systemProbe = new WindowsSystemProbe();
        var powerPlanStateRepository = new JsonPowerPlanStateRepository();
        var coreParkingStateRepository = new JsonCoreParkingStateRepository();

        // Both managers claim the active power plan, so they must share one critical
        // section for their mutual-exclusion guards to hold.
        var powerPlanScope = new PowerPlanTransactionScope();

        _viewModel = new MainViewModel(
            engine,
            systemProbe,
            new AtlasHealthService(),
            new SystemCleanupService(),
            new HardwareMonitorService(),
            new PowerPlanManager(
                new WindowsPowerPlanStore(),
                powerPlanStateRepository,
                async cancellationToken => await coreParkingStateRepository.GetAsync(cancellationToken) is not null,
                powerPlanScope),
            new StorageMaintenanceManager(
                new WindowsStorageInventoryStore(),
                new DefragStorageOptimizer(),
                () => systemProbe.Capture().IsAdministrator),
            new StorageTuningManager(
                new WindowsStorageTuningStore(),
                new JsonStorageTuningStateRepository(),
                () => systemProbe.Capture().IsAdministrator),
            new CoreParkingManager(
                new WindowsCoreParkingStore(),
                coreParkingStateRepository,
                powerPlanStateRepository,
                powerPlanScope),
            new WindowsServiceInventory(),
            new ServiceManager(
                new WindowsServiceControlStore(),
                new JsonServiceStateRepository()),
            new NetworkDiagnosticsManager(
                new WindowsNetworkInventoryStore(),
                new WindowsNetworkProbe()),
            new NetworkTuningManager(
                new WindowsNetworkTuningStore(),
                new JsonNetworkTuningStateRepository(),
                () => systemProbe.Capture().IsAdministrator),
            new MsiModeManager(
                new WindowsPciDeviceInventory(),
                new JsonMsiStateRepository(),
                () => systemProbe.Capture().IsAdministrator),
            new WindowsSystemTimerService(),
            message => MessageBox.Show(
                this,
                message,
                "Подтверждение Aurum",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No) == MessageBoxResult.Yes);
        DataContext = _viewModel;

        Loaded += OnLoaded;
        Closed += (_, _) => _viewModel.Dispose();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        await _viewModel.InitializeAsync();
        if (Environment.GetCommandLineArgs().Contains("--monitor", StringComparer.OrdinalIgnoreCase))
        {
            _viewModel.CurrentView = MainViewModel.ActiveView.Monitoring;
        }
    }
}
