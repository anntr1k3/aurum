using System.Collections.ObjectModel;
using Aurum.Core;

namespace Aurum.App.ViewModels;

public sealed class PowerPlanItemViewModel
{
    public PowerPlanItemViewModel(PowerPlanInfo plan, bool isActive, bool isDesired, bool isOriginal)
    {
        Plan = plan;
        IsActive = isActive;
        IsDesired = isDesired;
        IsOriginal = isOriginal;
    }

    public PowerPlanInfo Plan { get; }
    public Guid Id => Plan.Id;
    public string Name => Plan.Name;
    public bool IsActive { get; }
    public bool IsDesired { get; }
    public bool IsOriginal { get; }
    public string Identifier => Plan.Id.ToString("D");
    public string RoleLabel => IsActive
        ? "АКТИВЕН"
        : IsDesired
            ? "ОЖИДАЕТСЯ AURUM"
            : IsOriginal
                ? "ТОЧКА ОТКАТА"
                : string.Empty;
}

public sealed class PowerPlanViewModel : ObservableObject
{
    private readonly PowerPlanManager _manager;
    private readonly Action<string, bool> _reportStatus;
    private readonly Func<string, bool> _confirm;
    private PowerPlanItemViewModel? _selectedPlan;
    private PowerPlanStateKind _state;
    private string _activePlanName = "Определение…";
    private string _summary = "Считываем планы электропитания Windows…";
    private string _originalPlanLabel = "Не сохранён";
    private string _desiredPlanLabel = "Не выбран";
    private string _stateLabel = "НЕ ОТСЛЕЖИВАЕТСЯ";
    private bool _isBusy;

    public PowerPlanViewModel(
        PowerPlanManager manager,
        Action<string, bool> reportStatus,
        Func<string, bool> confirm)
    {
        _manager = manager;
        _reportStatus = reportStatus;
        _confirm = confirm;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
        ApplyCommand = new AsyncRelayCommand(ApplyAsync, () => CanApply);
        RepairCommand = new AsyncRelayCommand(RepairAsync, () => CanRepair);
        RevertCommand = new AsyncRelayCommand(RevertAsync, () => CanRevert);
    }

    public ObservableCollection<PowerPlanItemViewModel> Plans { get; } = [];
    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand ApplyCommand { get; }
    public AsyncRelayCommand RepairCommand { get; }
    public AsyncRelayCommand RevertCommand { get; }

    public PowerPlanItemViewModel? SelectedPlan
    {
        get => _selectedPlan;
        set
        {
            if (SetProperty(ref _selectedPlan, value))
            {
                NotifyCommandStateChanged();
            }
        }
    }

    public PowerPlanStateKind State
    {
        get => _state;
        private set
        {
            if (SetProperty(ref _state, value))
            {
                OnPropertyChanged(nameof(IsDrifted));
            }
        }
    }

    public string ActivePlanName { get => _activePlanName; private set => SetProperty(ref _activePlanName, value); }
    public string Summary { get => _summary; private set => SetProperty(ref _summary, value); }
    public string OriginalPlanLabel { get => _originalPlanLabel; private set => SetProperty(ref _originalPlanLabel, value); }
    public string DesiredPlanLabel { get => _desiredPlanLabel; private set => SetProperty(ref _desiredPlanLabel, value); }
    public string StateLabel { get => _stateLabel; private set => SetProperty(ref _stateLabel, value); }
    public bool IsDrifted => State == PowerPlanStateKind.Drifted;
    public bool CanApply => !IsBusy && State == PowerPlanStateKind.Untracked && SelectedPlan is { IsActive: false };
    public bool CanRepair => !IsBusy && State == PowerPlanStateKind.Drifted && Plans.Any(static plan => plan.IsDesired);
    public bool CanRevert => !IsBusy && State != PowerPlanStateKind.Untracked && Plans.Any(static plan => plan.IsOriginal);

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
            await RefreshInternalAsync();
        }
        catch (Exception error)
        {
            Summary = $"Не удалось прочитать планы питания: {error.Message}";
            _reportStatus(Summary, true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ApplyAsync()
    {
        var selected = SelectedPlan;
        if (selected is null || !_confirm(
                $"Активировать план «{selected.Name}»?\n\n" +
                $"Текущий план «{ActivePlanName}» будет сохранён как точка отката."))
        {
            return;
        }

        await RunMutationAsync(
            () => _manager.ApplyAsync(selected.Id),
            $"План «{selected.Name}» активирован. Исходный план сохранён для отката.");
    }

    private async Task RepairAsync()
    {
        var desired = Plans.FirstOrDefault(static plan => plan.IsDesired);
        if (desired is null)
        {
            return;
        }

        await RunMutationAsync(
            () => _manager.RepairAsync(),
            $"План «{desired.Name}» восстановлен после внешнего изменения.");
    }

    private async Task RevertAsync()
    {
        var original = Plans.FirstOrDefault(static plan => plan.IsOriginal);
        if (original is null || !_confirm(
                $"Вернуть исходный план «{original.Name}»?\n\n" +
                "После успешного возврата Aurum удалит сохранённую точку отката."))
        {
            return;
        }

        await RunMutationAsync(
            () => _manager.RevertAsync(),
            $"Исходный план «{original.Name}» восстановлен.");
    }

    private async Task RunMutationAsync(Func<Task> action, string successMessage)
    {
        IsBusy = true;
        try
        {
            await action();
            await RefreshInternalAsync();
            _reportStatus(successMessage, false);
        }
        catch (Exception error)
        {
            await TryRefreshInternalAsync();
            _reportStatus($"Операция с планом питания завершилась с ошибкой: {error.Message}", true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RefreshInternalAsync()
    {
        var evaluation = await _manager.EvaluateAsync();
        State = evaluation.State;
        var activeName = evaluation.ActivePlan?.Name ?? evaluation.Snapshot.ActivePlanId.ToString("D");
        ActivePlanName = activeName;
        var desiredId = evaluation.PersistedState?.DesiredPlanId;
        var originalId = evaluation.PersistedState?.OriginalPlanId;

        Plans.Clear();
        foreach (var plan in evaluation.Snapshot.Plans)
        {
            Plans.Add(new PowerPlanItemViewModel(
                plan,
                plan.Id == evaluation.Snapshot.ActivePlanId,
                plan.Id == desiredId,
                plan.Id == originalId));
        }

        OriginalPlanLabel = evaluation.OriginalPlan?.Name
                            ?? (originalId is null ? "Не сохранён" : "План больше не существует");
        DesiredPlanLabel = evaluation.DesiredPlan?.Name
                           ?? (desiredId is null ? "Не выбран" : "План больше не существует");

        switch (evaluation.State)
        {
            case PowerPlanStateKind.Applied:
                StateLabel = "ПРИМЕНЁН AURUM";
                Summary = $"Активен «{activeName}». Исходный план «{OriginalPlanLabel}» сохранён.";
                break;
            case PowerPlanStateKind.Drifted:
                StateLabel = "ОБНАРУЖЕН ДРЕЙФ";
                Summary = $"Ожидался «{DesiredPlanLabel}», но сейчас активен «{activeName}».";
                break;
            default:
                StateLabel = "НЕ ОТСЛЕЖИВАЕТСЯ";
                Summary = $"Активен «{activeName}». Aurum ещё не изменял план питания.";
                break;
        }

        SelectedPlan = Plans.FirstOrDefault(static plan => plan.IsDesired)
                       ?? Plans.FirstOrDefault(static plan => plan.IsActive);
        NotifyCommandStateChanged();
    }

    private async Task TryRefreshInternalAsync()
    {
        try
        {
            await RefreshInternalAsync();
        }
        catch
        {
            // Preserve the operation error reported by the caller.
        }
    }

    private void NotifyCommandStateChanged()
    {
        OnPropertyChanged(nameof(CanApply));
        OnPropertyChanged(nameof(CanRepair));
        OnPropertyChanged(nameof(CanRevert));
        RefreshCommand.RaiseCanExecuteChanged();
        ApplyCommand.RaiseCanExecuteChanged();
        RepairCommand.RaiseCanExecuteChanged();
        RevertCommand.RaiseCanExecuteChanged();
    }
}
