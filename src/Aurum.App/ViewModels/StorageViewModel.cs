using System.Collections.ObjectModel;
using Aurum.Core;

namespace Aurum.App.ViewModels;

public sealed class StorageVolumeItemViewModel
{
    public StorageVolumeItemViewModel(
        StorageVolumeInfo volume,
        StorageOperationAvailability analyze,
        StorageOperationAvailability retrim)
    {
        Volume = volume;
        AnalyzeAvailability = analyze;
        RetrimAvailability = retrim;
    }

    public StorageVolumeInfo Volume { get; }
    public StorageOperationAvailability AnalyzeAvailability { get; }
    public StorageOperationAvailability RetrimAvailability { get; }
    public string RootPath => Volume.RootPath;
    public string Title => $"{Volume.RootPath.TrimEnd('\\')} · {Volume.Label}";
    public string Model => Volume.Model;
    public string FileSystemLabel => $"{Volume.FileSystem} · {FormatBytes(Volume.TotalBytes)}";
    public string CapacityLabel => $"Свободно {FormatBytes(Volume.FreeBytes)} из {FormatBytes(Volume.TotalBytes)}";
    public string DeviceLabel => Volume.DeviceNumber is null
        ? Volume.BusType
        : $"{Volume.BusType} · PhysicalDrive{Volume.DeviceNumber}";
    public string MediaLabel => Volume.MediaKind switch
    {
        StorageMediaKind.SolidState => "SSD",
        StorageMediaKind.HardDisk => "HDD",
        StorageMediaKind.Removable => "СЪЁМНЫЙ",
        StorageMediaKind.Virtual => "ВИРТУАЛЬНЫЙ",
        _ => "ТИП НЕ ОПРЕДЕЛЁН",
    };
    public string TrimLabel => Volume.TrimSupported switch
    {
        true => "Поддерживается устройством",
        false => "Не поддерживается устройством",
        null => "Устройство не сообщило состояние",
    };
    public string DeleteNotifyLabel => Volume.DeleteNotificationsEnabled switch
    {
        true => $"Включены для {Volume.FileSystem}",
        false => $"Отключены для {Volume.FileSystem}",
        null => $"Не определены для {Volume.FileSystem}",
    };
    public string SystemLabel => Volume.IsSystem ? "СИСТЕМНЫЙ ТОМ" : string.Empty;
    public double UsedPercent => Volume.TotalBytes == 0
        ? 0
        : (Volume.TotalBytes - Volume.FreeBytes) * 100d / Volume.TotalBytes;
    public string UsedPercentLabel => $"{UsedPercent:0}% занято";
    public bool CanAnalyze => AnalyzeAvailability.CanRun;
    public bool CanRetrim => RetrimAvailability.CanRun;

    private static string FormatBytes(long bytes)
    {
        string[] units = ["Б", "КБ", "МБ", "ГБ", "ТБ"];
        var value = (double)Math.Max(0, bytes);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.#} {units[unit]}";
    }
}

public sealed class StorageViewModel : ObservableObject
{
    private readonly StorageMaintenanceManager _manager;
    private readonly StorageTuningManager _tuningManager;
    private readonly Action<string, bool> _reportStatus;
    private readonly Func<string, bool> _confirm;
    private StorageVolumeItemViewModel? _selectedVolume;
    private string _summary = "Считываем параметры накопителей…";
    private string _operationOutput = "Журнал появится после запуска анализа или ReTrim.";
    private string _lastOperationLabel = "ОПЕРАЦИИ ЕЩЁ НЕ ЗАПУСКАЛИСЬ";
    private bool _lastOperationFailed;
    private bool _isAdvancedVisible;
    private bool _isBusy;

    private bool _is8dot3Disabled;
    private bool _isLastAccessDisabled;
    private bool _isHibernationDisabled;
    private long _hiberfilBytes;
    private ServiceStartMode _sysMainStartMode = ServiceStartMode.Unknown;
    private ServiceRunState _sysMainState = ServiceRunState.Unknown;

    public StorageViewModel(
        StorageMaintenanceManager manager,
        StorageTuningManager tuningManager,
        Action<string, bool> reportStatus,
        Func<string, bool> confirm)
    {
        _manager = manager;
        _tuningManager = tuningManager;
        _reportStatus = reportStatus;
        _confirm = confirm;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
        AnalyzeCommand = new AsyncRelayCommand(AnalyzeAsync, () => CanAnalyze);
        RetrimCommand = new AsyncRelayCommand(RetrimAsync, () => CanRetrim);
        ToggleAdvancedCommand = new RelayCommand<object>(_ => IsAdvancedVisible = !IsAdvancedVisible);

        Toggle8dot3Command = new AsyncRelayCommand(Toggle8dot3Async, () => !IsBusy);
        ToggleLastAccessCommand = new AsyncRelayCommand(ToggleLastAccessAsync, () => !IsBusy);
        ToggleHibernationCommand = new AsyncRelayCommand(ToggleHibernationAsync, () => !IsBusy);
        ToggleSysMainCommand = new AsyncRelayCommand(ToggleSysMainAsync, () => !IsBusy);
        OptimizeAllStorageCommand = new AsyncRelayCommand(OptimizeAllStorageAsync, () => !IsBusy);
        RevertCommand = new AsyncRelayCommand(RevertTuningAsync, () => !IsBusy);
    }

    public ObservableCollection<StorageVolumeItemViewModel> Volumes { get; } = [];
    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand AnalyzeCommand { get; }
    public AsyncRelayCommand RetrimCommand { get; }
    public RelayCommand<object> ToggleAdvancedCommand { get; }

    public AsyncRelayCommand Toggle8dot3Command { get; }
    public AsyncRelayCommand ToggleLastAccessCommand { get; }
    public AsyncRelayCommand ToggleHibernationCommand { get; }
    public AsyncRelayCommand ToggleSysMainCommand { get; }
    public AsyncRelayCommand OptimizeAllStorageCommand { get; }
    public AsyncRelayCommand RevertCommand { get; }

    public bool Is8dot3Disabled { get => _is8dot3Disabled; private set => SetProperty(ref _is8dot3Disabled, value); }
    public string Is8dot3StatusLabel => Is8dot3Disabled ? "ОТКЛЮЧЕНО" : "ВКЛЮЧЕНО";

    public bool IsLastAccessDisabled { get => _isLastAccessDisabled; private set => SetProperty(ref _isLastAccessDisabled, value); }
    public string IsLastAccessStatusLabel => IsLastAccessDisabled ? "ОТКЛЮЧЕНО" : "ВКЛЮЧЕНО";

    public bool IsHibernationDisabled { get => _isHibernationDisabled; private set => SetProperty(ref _isHibernationDisabled, value); }
    public string IsHibernationStatusLabel => IsHibernationDisabled ? "ОТКЛЮЧЕНА" : "ВКЛЮЧЕНА";

    public long HiberfilBytes { get => _hiberfilBytes; private set => SetProperty(ref _hiberfilBytes, value); }
    public string HiberfilSizeLabel => HiberfilBytes > 0
        ? $"Размер hiberfil.sys: {HiberfilBytes / (1024.0 * 1024 * 1024):0.#} ГБ"
        : (IsHibernationDisabled ? "Файл hiberfil.sys удалён" : "0 ГБ");

    public bool IsSysMainDisabled => _sysMainStartMode == ServiceStartMode.Disabled;
    public string SysMainStatusLabel => IsSysMainDisabled ? "ОТКЛЮЧЕНА" : (_sysMainState == ServiceRunState.Running ? "РАБОТАЕТ" : "ВКЛЮЧЕНА");

    public StorageVolumeItemViewModel? SelectedVolume
    {
        get => _selectedVolume;
        set
        {
            if (SetProperty(ref _selectedVolume, value))
            {
                OnPropertyChanged(nameof(AnalyzeAvailabilityLabel));
                OnPropertyChanged(nameof(RetrimAvailabilityLabel));
                OnPropertyChanged(nameof(AnalyzeCommandPreview));
                OnPropertyChanged(nameof(RetrimCommandPreview));
                NotifyCommandStateChanged();
            }
        }
    }

    public string Summary { get => _summary; private set => SetProperty(ref _summary, value); }
    public string OperationOutput { get => _operationOutput; private set => SetProperty(ref _operationOutput, value); }
    public string LastOperationLabel { get => _lastOperationLabel; private set => SetProperty(ref _lastOperationLabel, value); }
    public bool LastOperationFailed { get => _lastOperationFailed; private set => SetProperty(ref _lastOperationFailed, value); }
    public bool IsAdvancedVisible
    {
        get => _isAdvancedVisible;
        set
        {
            if (SetProperty(ref _isAdvancedVisible, value))
            {
                OnPropertyChanged(nameof(AdvancedButtonLabel));
                OnPropertyChanged(nameof(IsSimpleVisible));
            }
        }
    }
    public bool IsSimpleVisible => !IsAdvancedVisible;
    public string AdvancedButtonLabel => IsAdvancedVisible ? "Обычный режим" : "Продвинутый режим";
    public bool CanAnalyze => !IsBusy && SelectedVolume?.CanAnalyze == true;
    public bool CanRetrim => !IsBusy && SelectedVolume?.CanRetrim == true;
    public string AnalyzeAvailabilityLabel => SelectedVolume?.AnalyzeAvailability.Reason ?? "Выберите том.";
    public string RetrimAvailabilityLabel => SelectedVolume?.RetrimAvailability.Reason ?? "Выберите том.";
    public string AnalyzeCommandPreview => SelectedVolume is null
        ? "defrag <том> /A /U /V"
        : $"defrag {SelectedVolume.RootPath.TrimEnd('\\')} /A /U /V";
    public string RetrimCommandPreview => SelectedVolume is null
        ? "defrag <том> /L /U /V"
        : $"defrag {SelectedVolume.RootPath.TrimEnd('\\')} /L /U /V";

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                NotifyCommandStateChanged();
            }
        }
    }

    public Task InitializeAsync() => RefreshAsync();

    public async Task RefreshAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var selectedRoot = SelectedVolume?.RootPath;
            var volumes = await _manager.CaptureAsync();
            Volumes.Clear();
            foreach (var volume in volumes)
            {
                Volumes.Add(new StorageVolumeItemViewModel(
                    volume,
                    _manager.EvaluateAvailability(volume, StorageOperationKind.Analyze),
                    _manager.EvaluateAvailability(volume, StorageOperationKind.Retrim)));
            }

            SelectedVolume = Volumes.FirstOrDefault(volume =>
                                 string.Equals(volume.RootPath, selectedRoot, StringComparison.OrdinalIgnoreCase))
                             ?? Volumes.FirstOrDefault(static volume => volume.Volume.IsSystem)
                             ?? Volumes.FirstOrDefault();
            var solidStateCount = Volumes.Count(static volume => volume.Volume.MediaKind == StorageMediaKind.SolidState);
            Summary = $"Томов: {Volumes.Count} · SSD: {solidStateCount} · ничего не оптимизируется автоматически.";

            var snapshot = await _tuningManager.CaptureSnapshotAsync();
            Is8dot3Disabled = snapshot.Is8dot3Disabled;
            IsLastAccessDisabled = snapshot.IsLastAccessDisabled;
            IsHibernationDisabled = snapshot.IsHibernationDisabled;
            HiberfilBytes = snapshot.HiberfilBytes;
            _sysMainStartMode = snapshot.SysMainStartMode;
            _sysMainState = snapshot.SysMainState;

            OnPropertyChanged(nameof(Is8dot3StatusLabel));
            OnPropertyChanged(nameof(IsLastAccessStatusLabel));
            OnPropertyChanged(nameof(IsHibernationStatusLabel));
            OnPropertyChanged(nameof(HiberfilSizeLabel));
            OnPropertyChanged(nameof(IsSysMainDisabled));
            OnPropertyChanged(nameof(SysMainStatusLabel));
        }
        catch (Exception error)
        {
            Summary = $"Не удалось прочитать накопители: {error.Message}";
            _reportStatus(Summary, true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task Toggle8dot3Async()
    {
        var targetState = !Is8dot3Disabled;
        if (!_confirm(
                $"{(targetState ? "Отключить" : "Включить")} генерацию коротких 8.3 имён NTFS?\n\n" +
                (targetState
                    ? "Отключение устраняет лишние операции создания DOS-псевдонимов при записи файлов на SSD."
                    : "Включение возвращает совместимость с очень старыми 16-битными утилитами.")))
        {
            return;
        }

        try
        {
            await _tuningManager.Toggle8dot3Async(targetState);
            _reportStatus($"Генерация имён 8.3 {(targetState ? "отключена" : "включена")}.", false);
            await RefreshAsync();
        }
        catch (Exception error)
        {
            _reportStatus($"Не удалось изменить параметр 8.3 имён: {error.Message}", true);
        }
    }

    private async Task ToggleLastAccessAsync()
    {
        var targetState = !IsLastAccessDisabled;
        if (!_confirm(
                $"{(targetState ? "Отключить" : "Включить")} обновление штампов последнего доступа к файлам?\n\n" +
                (targetState
                    ? "Отключение снижает износ ячеек SSD/NVMe за счёт исключения постоянных записей в таблицу метаданных при обычном чтении."
                    : "Включение возвращает стандартное поведение Windows.")))
        {
            return;
        }

        try
        {
            await _tuningManager.ToggleLastAccessAsync(targetState);
            _reportStatus($"Обновление штампов доступа {(targetState ? "отключено" : "включено")}.", false);
            await RefreshAsync();
        }
        catch (Exception error)
        {
            _reportStatus($"Не удалось изменить параметр LastAccess: {error.Message}", true);
        }
    }

    private async Task ToggleHibernationAsync()
    {
        var targetState = !IsHibernationDisabled;
        if (!_confirm(
                $"{(targetState ? "Отключить" : "Включить")} режим гибернации Windows?\n\n" +
                (targetState
                    ? "Отключение удалит файл hiberfil.sys и освободит память на системном SSD.\n(Быстрый запуск Fast Startup также будет отключён, что рекомендуется для SSD)."
                    : "Включение создаст файл hiberfil.sys и вернёт возможность гибернации.")))
        {
            return;
        }

        try
        {
            await _tuningManager.ToggleHibernationAsync(targetState);
            _reportStatus($"Режим гибернации {(targetState ? "отключен" : "включен")}.", false);
            await RefreshAsync();
        }
        catch (Exception error)
        {
            _reportStatus($"Не удалось изменить режим гибернации: {error.Message}", true);
        }
    }

    private async Task ToggleSysMainAsync()
    {
        var targetState = !IsSysMainDisabled;
        if (!_confirm(
                $"{(targetState ? "Отключить" : "Включить")} службу SysMain (SuperFetch)?\n\n" +
                (targetState
                    ? "На современных SSD и NVMe упреждающее кэширование в RAM не требуется и лишь тратит ресурсы процессора и накопителя."
                    : "Включение возвращает автоматический запуск службы SysMain.")))
        {
            return;
        }

        try
        {
            await _tuningManager.ToggleSysMainAsync(targetState);
            _reportStatus($"Служба SysMain {(targetState ? "отключена" : "включена")}.", false);
            await RefreshAsync();
        }
        catch (Exception error)
        {
            _reportStatus($"Не удалось изменить состояние SysMain: {error.Message}", true);
        }
    }

    private async Task AnalyzeAsync()
    {
        var volume = SelectedVolume;
        if (volume is null || !_confirm(
                $"Запустить штатный анализ Windows для {volume.RootPath}?\n\n" +
                $"Команда: {AnalyzeCommandPreview}\nИзменения на томе не выполняются."))
        {
            return;
        }

        await RunOperationAsync(StorageOperationKind.Analyze, volume);
    }

    private async Task RetrimAsync()
    {
        var volume = SelectedVolume;
        if (volume is null || !_confirm(
                $"Выполнить ReTrim для {volume.RootPath}?\n\n" +
                $"Команда: {RetrimCommandPreview}\nWindows отправит накопителю сведения об освобождённых блоках."))
        {
            return;
        }

        await RunOperationAsync(StorageOperationKind.Retrim, volume);
    }

    public async Task<bool> OptimizeAllSsdDirectAsync()
    {
        var ssdVolumes = Volumes.Where(v => v.CanRetrim).ToArray();
        if (ssdVolumes.Length == 0)
        {
            _reportStatus("Подходящих SSD томов для ReTrim не найдено.", false);
            return true;
        }

        var allSucceeded = true;
        foreach (var vol in ssdVolumes)
        {
            allSucceeded &= await RunOperationAsync(StorageOperationKind.Retrim, vol);
        }
        return allSucceeded;
    }

    public async Task OptimizeAllStorageAsync()
    {
        if (!_confirm("Применить все оптимизации накопителей (отключение 8.3 имён, LastAccess, службы SysMain и ReTrim для всех SSD)?\n\nВсе параметры полностью обратимы."))
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _tuningManager.Toggle8dot3Async(true);
            await _tuningManager.ToggleLastAccessAsync(true);
            await _tuningManager.ToggleSysMainAsync(true);
            await OptimizeAllSsdDirectAsync();
            await RefreshAsync();
            _reportStatus("Все параметры накопителей успешно оптимизированы.", false);
        }
        catch (Exception ex)
        {
            _reportStatus($"Ошибка при оптимизации накопителей: {ex.Message}", true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<bool> RunOperationAsync(StorageOperationKind operation, StorageVolumeItemViewModel volume)
    {
        IsBusy = true;
        LastOperationFailed = false;
        LastOperationLabel = operation == StorageOperationKind.Analyze ? "ВЫПОЛНЯЕТСЯ АНАЛИЗ…" : "ВЫПОЛНЯЕТСЯ RETRIM…";
        OperationOutput = "Ожидание отчёта Windows…";
        try
        {
            var result = operation == StorageOperationKind.Analyze
                ? await _manager.AnalyzeAsync(volume.RootPath)
                : await _manager.RetrimAsync(volume.RootPath);
            LastOperationFailed = !result.Succeeded;
            LastOperationLabel = result.Succeeded
                ? $"ГОТОВО · {result.CompletedAt:HH:mm:ss}"
                : $"WINDOWS ВЕРНУЛ КОД {result.ExitCode} · {result.CompletedAt:HH:mm:ss}";
            OperationOutput = string.IsNullOrWhiteSpace(result.Output)
                ? "Windows не вернула текстовый отчёт."
                : result.Output;
            _reportStatus(
                result.Succeeded
                    ? $"{(operation == StorageOperationKind.Analyze ? "Анализ" : "ReTrim")} тома {volume.RootPath} завершён."
                    : $"Операция для {volume.RootPath} завершилась с кодом {result.ExitCode}.",
                !result.Succeeded);
            return result.Succeeded;
        }
        catch (Exception error)
        {
            LastOperationFailed = true;
            LastOperationLabel = "ОПЕРАЦИЯ НЕ ВЫПОЛНЕНА";
            OperationOutput = error.Message;
            _reportStatus($"Операция с накопителем не выполнена: {error.Message}", true);
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<bool> RevertTuningAsync()
    {
        if (!_confirm("Вы действительно хотите вернуть все параметры накопителей (8.3 имена, LastAccess, SysMain, гибернацию) к исходным значениям?"))
        {
            return false;
        }

        IsBusy = true;
        try
        {
            var reverted = await _tuningManager.RevertAsync();
            await RefreshAsync();
            if (reverted)
            {
                _reportStatus("Параметры накопителей успешно возвращены к исходным значениям Windows.", false);
            }
            else
            {
                _reportStatus("Сохранённый снимок параметров накопителей не найден или не требовал отката.", false);
            }
            return reverted;
        }
        catch (Exception ex)
        {
            _reportStatus($"Не удалось вернуть параметры накопителей: {ex.Message}", true);
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<bool> RevertTuningDirectAsync()
    {
        var reverted = await _tuningManager.RevertAsync();
        await RefreshAsync();
        return reverted;
    }

    private void NotifyCommandStateChanged()
    {
        OnPropertyChanged(nameof(CanAnalyze));
        OnPropertyChanged(nameof(CanRetrim));
        RefreshCommand.RaiseCanExecuteChanged();
        AnalyzeCommand.RaiseCanExecuteChanged();
        RetrimCommand.RaiseCanExecuteChanged();
        RevertCommand.RaiseCanExecuteChanged();
    }
}
