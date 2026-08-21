using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Aurum.Core;

namespace Aurum.App.ViewModels;

public sealed class PciDeviceMsiItemViewModel : ObservableObject
{
    private readonly MsiModeManager _manager;
    private readonly Action<string, bool> _reportStatus;
    private bool _isMsiSupported;
    private MsiDevicePriority _priority;

    public PciDeviceMsiItemViewModel(
        PciDeviceMsiInfo info,
        MsiModeManager manager,
        Action<string, bool> reportStatus)
    {
        Info = info;
        _manager = manager;
        _reportStatus = reportStatus;
        _isMsiSupported = info.IsMsiSupported;
        _priority = info.Priority;
    }

    public PciDeviceMsiInfo Info { get; }
    public string DeviceInstanceId => Info.DeviceInstanceId;
    public string Name => Info.Name;
    public MsiDeviceCategory Category => Info.Category;
    public string CategoryLabel => Info.CategoryLabel;
    public string CategoryIcon => Category switch
    {
        MsiDeviceCategory.Gpu => "🎮",
        MsiDeviceCategory.Network => "🌐",
        MsiDeviceCategory.Audio => "🔊",
        MsiDeviceCategory.Usb => "🔌",
        MsiDeviceCategory.Storage => "💾",
        _ => "⚡"
    };
    public string LocationInfo => Info.LocationInfo;
    public bool CanModifyMsi => Info.CanModifyMsi;
    public bool IsRecommendedDevice => Category is MsiDeviceCategory.Gpu or MsiDeviceCategory.Network;

    public bool IsMsiSupported
    {
        get => _isMsiSupported;
        set
        {
            if (SetProperty(ref _isMsiSupported, value))
            {
                OnPropertyChanged(nameof(StatusBadge));
                _ = ApplyChangesAsync();
            }
        }
    }

    public MsiDevicePriority Priority
    {
        get => _priority;
        set
        {
            if (SetProperty(ref _priority, value))
            {
                OnPropertyChanged(nameof(PriorityLabel));
                _ = ApplyChangesAsync();
            }
        }
    }

    public string PriorityLabel => _priority switch
    {
        MsiDevicePriority.High => "Высокий (High)",
        MsiDevicePriority.Normal => "Обычный (Normal)",
        MsiDevicePriority.Low => "Низкий (Low)",
        _ => "По умолчанию (Undefined)"
    };

    public string StatusBadge => _isMsiSupported ? "MSI Включён" : "Line IRQ";

    public IReadOnlyList<string> PriorityOptions { get; } =
    [
        "По умолчанию (Undefined)",
        "Обычный (Normal)",
        "Высокий (High)",
        "Низкий (Low)"
    ];

    public string SelectedPriorityOption
    {
        get => PriorityLabel;
        set
        {
            var target = value switch
            {
                "Высокий (High)" => MsiDevicePriority.High,
                "Обычный (Normal)" => MsiDevicePriority.Normal,
                "Низкий (Low)" => MsiDevicePriority.Low,
                _ => MsiDevicePriority.Undefined
            };
            Priority = target;
        }
    }

    private async Task ApplyChangesAsync()
    {
        try
        {
            await _manager.ApplyDeviceMsiAsync(DeviceInstanceId, _isMsiSupported, _priority);
            _reportStatus($"Обновлены параметры MSI для: {Name}", false);
        }
        catch (Exception ex)
        {
            _reportStatus($"Ошибка настройки MSI: {ex.Message}", true);
        }
    }
}

public sealed class MsiViewModel : ObservableObject
{
    private readonly MsiModeManager _manager;
    private readonly Action<string, bool> _reportStatus;
    private readonly Func<string, bool> _confirm;
    private string _summary = "Считываем параметры аппаратных прерываний…";
    private string _searchText = string.Empty;
    private MsiDeviceCategory? _selectedCategory;
    private bool _isBusy;
    private bool _hasActiveModifications;

    public MsiViewModel(
        MsiModeManager manager,
        Action<string, bool> reportStatus,
        Func<string, bool> confirm)
    {
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));
        _reportStatus = reportStatus ?? throw new ArgumentNullException(nameof(reportStatus));
        _confirm = confirm ?? throw new ArgumentNullException(nameof(confirm));

        Devices = new ObservableCollection<PciDeviceMsiItemViewModel>();
        FilteredDevices = new ObservableCollection<PciDeviceMsiItemViewModel>();

        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
        ApplyGamingPresetCommand = new AsyncRelayCommand(ApplyGamingPresetAsync, () => !IsBusy);
        RevertCommand = new AsyncRelayCommand(RevertAsync, () => !IsBusy && HasActiveModifications);
        SetFilterCommand = new RelayCommand<string>(SetFilter);
    }

    public ObservableCollection<PciDeviceMsiItemViewModel> Devices { get; }
    public ObservableCollection<PciDeviceMsiItemViewModel> FilteredDevices { get; }

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand ApplyGamingPresetCommand { get; }
    public AsyncRelayCommand RevertCommand { get; }
    public RelayCommand<string> SetFilterCommand { get; }

    public string Summary
    {
        get => _summary;
        private set => SetProperty(ref _summary, value);
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                ApplyFilter();
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                (RefreshCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
                (ApplyGamingPresetCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
                (RevertCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    public bool HasActiveModifications
    {
        get => _hasActiveModifications;
        private set
        {
            if (SetProperty(ref _hasActiveModifications, value))
            {
                (RevertCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    private bool _showOnlyKeyDevices = true;
    public bool ShowOnlyKeyDevices
    {
        get => _showOnlyKeyDevices;
        set
        {
            if (SetProperty(ref _showOnlyKeyDevices, value))
            {
                ApplyFilter();
            }
        }
    }

    public Task InitializeAsync() => RefreshAsync();

    public async Task RefreshAsync()
    {
        if (IsBusy) return;

        IsBusy = true;
        try
        {
            var rawDevices = await _manager.CaptureAsync();
            Devices.Clear();

            foreach (var dev in rawDevices)
            {
                Devices.Add(new PciDeviceMsiItemViewModel(dev, _manager, _reportStatus));
            }

            HasActiveModifications = await _manager.HasActiveModificationsAsync();
            ApplyFilter();

            var msiCount = Devices.Count(d => d.IsMsiSupported);
            Summary = $"Обнаружено устройств: {Devices.Count} · В режиме MSI: {msiCount} · Снижение DPC Latency";
        }
        catch (Exception ex)
        {
            Summary = $"Ошибка сканирования устройств: {ex.Message}";
            _reportStatus(Summary, true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<int> ApplyGamingPresetDirectAsync()
    {
        var updated = await _manager.ApplyGamingPresetAsync();
        await RefreshAsync();
        return updated;
    }

    public async Task RevertDirectAsync()
    {
        await _manager.RevertAsync();
        await RefreshAsync();
    }

    public async Task ApplyGamingPresetAsync()
    {
        if (IsBusy) return;

        if (!_confirm("Применить оптимизацию MSI для гейминга?\n\nБудет включён режим MSI для видеокарты, сетевого адаптера, звука и USB, а для GPU и сети установлен высокий приоритет прерываний (High). Исходное состояние сохранится для отката."))
        {
            return;
        }

        IsBusy = true;
        try
        {
            var updated = await _manager.ApplyGamingPresetAsync();
            _reportStatus($"Оптимизировано {updated} устройств. Для вступления в силу может потребоваться перезагрузка.", false);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            _reportStatus($"Ошибка применения пресета MSI: {ex.Message}", true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task RevertAsync()
    {
        if (IsBusy) return;

        if (!_confirm("Вернуть исходные параметры прерываний всех устройств?"))
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _manager.RevertAsync();
            _reportStatus("Все параметры прерываний возвращены к исходным значениям.", false);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            _reportStatus($"Ошибка отката MSI: {ex.Message}", true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void SetFilter(string? filterName)
    {
        _selectedCategory = filterName switch
        {
            "gpu" => MsiDeviceCategory.Gpu,
            "net" => MsiDeviceCategory.Network,
            "audio" => MsiDeviceCategory.Audio,
            "usb" => MsiDeviceCategory.Usb,
            "storage" => MsiDeviceCategory.Storage,
            _ => null
        };
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        FilteredDevices.Clear();
        var query = Devices.AsEnumerable();

        if (_selectedCategory.HasValue)
        {
            query = query.Where(d => d.Category == _selectedCategory.Value);
        }
        else if (ShowOnlyKeyDevices)
        {
            query = query.Where(d => d.Category is MsiDeviceCategory.Gpu or MsiDeviceCategory.Network or MsiDeviceCategory.Audio or MsiDeviceCategory.Storage or MsiDeviceCategory.Usb);
        }

        if (!string.IsNullOrWhiteSpace(_searchText))
        {
            var search = _searchText.Trim();
            query = query.Where(d =>
                d.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                d.LocationInfo.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                d.CategoryLabel.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var item in query)
        {
            FilteredDevices.Add(item);
        }
    }
}
