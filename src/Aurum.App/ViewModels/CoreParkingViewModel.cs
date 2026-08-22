using Aurum.Core;

namespace Aurum.App.ViewModels;

public sealed class CoreParkingViewModel : ObservableObject
{
    private readonly CoreParkingManager _manager;
    private readonly Action<string, bool> _report;
    private readonly Func<string, bool> _confirm;
    private readonly bool _isHeterogeneous;
    private CoreParkingStateKind _state;
    private CoreParkingSettings? _currentSettings;
    private double _minimumAc;
    private double _maximumAc = 100;
    private double _minimumDc;
    private double _maximumDc = 100;
    private string _activePlanName = "Определение…";
    private string _summary = "Считываем параметры парковки ядер…";
    private string _stateLabel = "НЕ ОТСЛЕЖИВАЕТСЯ";
    private string _originalPlanLabel = "Не сохранён";
    private bool _isBusy;

    public CoreParkingViewModel(
        CoreParkingManager manager,
        Action<string, bool> report,
        Func<string, bool> confirm,
        IProcessorTopology? topology = null)
    {
        _manager = manager;
        _report = report;
        _confirm = confirm;
        _isHeterogeneous = topology?.Capture().IsHeterogeneous == true;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
        ApplyCommand = new AsyncRelayCommand(ApplyAsync, () => CanApply);
        RepairCommand = new AsyncRelayCommand(RepairAsync, () => CanRepair);
        RevertCommand = new AsyncRelayCommand(RevertAsync, () => CanRevert);
        DisableParkingCommand = new RelayCommand<object>(_ => SetValues(100, 100, 100, 100), _ => !IsBusy);
    }

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand ApplyCommand { get; }
    public AsyncRelayCommand RepairCommand { get; }
    public AsyncRelayCommand RevertCommand { get; }
    public RelayCommand<object> DisableParkingCommand { get; }
    public CoreParkingStateKind State { get => _state; private set => SetProperty(ref _state, value); }
    public string ActivePlanName { get => _activePlanName; private set => SetProperty(ref _activePlanName, value); }
    public string Summary { get => _summary; private set => SetProperty(ref _summary, value); }
    public string StateLabel { get => _stateLabel; private set => SetProperty(ref _stateLabel, value); }
    public string OriginalPlanLabel { get => _originalPlanLabel; private set => SetProperty(ref _originalPlanLabel, value); }
    public bool IsHeterogeneous => _isHeterogeneous;
    public string HeterogeneousGuidance => CoreParkingGuidance.HeterogeneousUnparkWarning;

    public double MinimumAc { get => _minimumAc; set => SetValue(ref _minimumAc, value); }
    public double MaximumAc { get => _maximumAc; set => SetValue(ref _maximumAc, value); }
    public double MinimumDc { get => _minimumDc; set => SetValue(ref _minimumDc, value); }
    public double MaximumDc { get => _maximumDc; set => SetValue(ref _maximumDc, value); }
    public string MinimumAcLabel => $"{MinimumAc:0}%";
    public string MaximumAcLabel => $"{MaximumAc:0}%";
    public string MinimumDcLabel => $"{MinimumDc:0}%";
    public string MaximumDcLabel => $"{MaximumDc:0}%";
    public string ValidationLabel => IsValid
        ? "Минимум не превышает максимум. Значения применимы."
        : "Минимальный процент не может превышать максимальный.";
    public bool IsValid => MinimumAc <= MaximumAc && MinimumDc <= MaximumDc;
    public bool CanApply => !IsBusy && State == CoreParkingStateKind.Untracked && IsValid && _currentSettings != DesiredSettings;
    public bool CanRepair => !IsBusy && State == CoreParkingStateKind.Drifted;
    public bool CanRevert => !IsBusy && State != CoreParkingStateKind.Untracked;
    private CoreParkingSettings DesiredSettings => new(
        (uint)Math.Round(MinimumAc), (uint)Math.Round(MaximumAc),
        (uint)Math.Round(MinimumDc), (uint)Math.Round(MaximumDc));

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value)) NotifyCommands();
        }
    }

    public Task InitializeAsync() => RefreshAsync();

    public async Task RefreshAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try { await RefreshInternalAsync(); }
        catch (Exception error) { Summary = $"Не удалось прочитать Core Parking: {error.Message}"; _report(Summary, true); }
        finally { IsBusy = false; }
    }

    public bool IsManagedPlanActive => State == CoreParkingStateKind.Applied;

    public Task<bool> ApplyDirectAsync() => ApplyInternalAsync(confirmHeterogeneousUnpark: true);

    public Task<bool> RevertDirectAsync()
    {
        return RunAsync(() => _manager.RevertAsync(), "Исходный план питания восстановлен; копия Core Parking удалена.");
    }

    private async Task ApplyAsync()
    {
        var desired = DesiredSettings;
        var message =
            $"Создать отдельный план Aurum и применить Core Parking?\n\n" +
            $"Сеть: {desired.MinimumAc}–{desired.MaximumAc}% · батарея: {desired.MinimumDc}–{desired.MaximumDc}%.\n" +
            "Встроенный план Windows изменён не будет.";
        if (_isHeterogeneous && CoreParkingGuidance.IsBlanketUnpark(desired))
        {
            message += "\n\n" + CoreParkingGuidance.HeterogeneousUnparkWarning;
        }

        if (!_confirm(message)) return;
        await ApplyInternalAsync(confirmHeterogeneousUnpark: false);
    }

    private Task<bool> ApplyInternalAsync(bool confirmHeterogeneousUnpark)
    {
        if (confirmHeterogeneousUnpark && !ConfirmHeterogeneousUnparkIfNeeded())
        {
            return Task.FromResult(false);
        }

        var desired = DesiredSettings;
        return RunAsync(() => _manager.ApplyAsync(desired), "План Core Parking создан и активирован.");
    }

    private Task RepairAsync() => RunAsync(() => _manager.RepairAsync(), "Параметры и активность плана Core Parking восстановлены.");

    private async Task RevertAsync()
    {
        if (!_confirm($"Вернуть исходный план «{OriginalPlanLabel}» и удалить созданную Aurum копию?")) return;
        await RevertDirectAsync();
    }

    /// <summary>Returns false when the transaction failed, so callers never report a false success.</summary>
    private bool ConfirmHeterogeneousUnparkIfNeeded()
    {
        if (!_isHeterogeneous || !CoreParkingGuidance.IsBlanketUnpark(DesiredSettings))
        {
            return true;
        }

        return _confirm(CoreParkingGuidance.HeterogeneousUnparkWarning + "\n\nПродолжить применение 100% активных ядер?");
    }
    private async Task<bool> RunAsync(Func<Task> action, string success)
    {
        if (IsBusy) return false;

        IsBusy = true;
        try
        {
            await action();
            await RefreshInternalAsync();
            _report(success, false);
            return true;
        }
        catch (Exception error)
        {
            _report($"Core Parking: {error.Message}", true);
            try { await RefreshInternalAsync(); } catch { }
            return false;
        }
        finally { IsBusy = false; }
    }

    private async Task RefreshInternalAsync()
    {
        var evaluation = await _manager.EvaluateAsync();
        State = evaluation.State;
        ActivePlanName = evaluation.ActivePlan.Name;
        _currentSettings = evaluation.ActivePlan.Settings;
        var settings = evaluation.PersistedState?.DesiredSettings ?? evaluation.ActivePlan.Settings;
        SetValues(settings.MinimumAc, settings.MaximumAc, settings.MinimumDc, settings.MaximumDc);
        OriginalPlanLabel = evaluation.PersistedState?.OriginalPlanName ?? "Не сохранён";
        (StateLabel, Summary) = evaluation.State switch
        {
            CoreParkingStateKind.Applied => ("ПРИМЕНЁН AURUM", $"Активна изолированная копия плана «{ActivePlanName}»."),
            CoreParkingStateKind.Drifted => ("ОБНАРУЖЕН ДРЕЙФ", evaluation.ManagedPlanExists
                ? "Активный план или значения Core Parking изменились вне Aurum."
                : "Созданный Aurum план был удалён вне приложения."),
            _ => ("ДИАГНОСТИКА", $"Активен «{ActivePlanName}». Настройки пока только просматриваются."),
        };
        NotifyCommands();
    }

    private void SetValues(double minimumAc, double maximumAc, double minimumDc, double maximumDc)
    {
        MinimumAc = minimumAc; MaximumAc = maximumAc; MinimumDc = minimumDc; MaximumDc = maximumDc;
    }

    private void SetValue(ref double field, double value)
    {
        if (SetProperty(ref field, Math.Clamp(Math.Round(value), 0, 100)))
        {
            OnPropertyChanged(nameof(MinimumAcLabel)); OnPropertyChanged(nameof(MaximumAcLabel));
            OnPropertyChanged(nameof(MinimumDcLabel)); OnPropertyChanged(nameof(MaximumDcLabel));
            OnPropertyChanged(nameof(IsValid)); OnPropertyChanged(nameof(ValidationLabel)); NotifyCommands();
        }
    }

    private void NotifyCommands()
    {
        OnPropertyChanged(nameof(CanApply)); OnPropertyChanged(nameof(CanRepair)); OnPropertyChanged(nameof(CanRevert));
        ApplyCommand.RaiseCanExecuteChanged(); RepairCommand.RaiseCanExecuteChanged(); RevertCommand.RaiseCanExecuteChanged();
        DisableParkingCommand.RaiseCanExecuteChanged();
    }
}
