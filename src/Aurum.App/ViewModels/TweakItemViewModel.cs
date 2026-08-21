using Aurum.Core;

namespace Aurum.App.ViewModels;

public sealed class TweakItemViewModel : ObservableObject
{
    private readonly TweakEngine _engine;
    private readonly Action<string, bool> _reportStatus;
    private bool _isSelected;
    private bool _isBusy;
    private TweakStateKind _state = TweakStateKind.Available;

    public TweakItemViewModel(
        TweakDefinition definition,
        TweakEngine engine,
        Action<string, bool> reportStatus)
    {
        Definition = definition;
        _engine = engine;
        _reportStatus = reportStatus;
        ApplyCommand = new AsyncRelayCommand(ApplyAsync, () => CanApply);
        RepairCommand = new AsyncRelayCommand(RepairAsync, () => CanRepair);
        RevertCommand = new AsyncRelayCommand(RevertAsync, () => CanRevert);
        ToggleExpandCommand = new RelayCommand<object>(_ => IsExpanded = !IsExpanded);
    }

    public RelayCommand<object> ToggleExpandCommand { get; }

    public TweakDefinition Definition { get; }

    public string Name => Definition.Name;

    public string Category => Definition.Category.ToUpperInvariant();

    public string Description => Definition.Description;

    public string Impact => Definition.Impact;

    public string RiskLabel => Definition.Risk switch
    {
        TweakRisk.Safe => "НИЗКИЙ РИСК",
        TweakRisk.Moderate => "НУЖНО ВНИМАНИЕ",
        _ => "ВЫСОКИЙ РИСК"
    };

    public string RestartLabel => Definition.Restart switch
    {
        RestartRequirement.None => "Без перезапуска",
        RestartRequirement.Explorer => "Перезапуск Проводника",
        RestartRequirement.SignOut => "Повторный вход",
        RestartRequirement.Restart => "Перезагрузка",
        _ => string.Empty
    };

    public string RegistryPaths => string.Join("  •  ", Definition.Mutations.Select(static mutation =>
        $"{mutation.Target.DisplayPath} = {mutation.DesiredValue.Data}"));

    public string StateLabel => State switch
    {
        TweakStateKind.Available => "Не применено",
        TweakStateKind.AlreadyConfigured => "Уже настроено вне Aurum",
        TweakStateKind.Applied => "Применено Aurum",
        TweakStateKind.Drifted => "Изменено после применения",
        _ => "Неизвестно"
    };

    public bool IsSafe => Definition.Risk == TweakRisk.Safe;
    public bool IsModerateRisk => Definition.Risk == TweakRisk.Moderate;
    public bool RequiresRestart => Definition.Restart != RestartRequirement.None;
    public bool IsModifiedOutside => State == TweakStateKind.Drifted;

    private bool _isExpanded;
    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanApply));
                OnPropertyChanged(nameof(CanRevert));
                OnPropertyChanged(nameof(CanRepair));
                OnPropertyChanged(nameof(CanToggle));
                ApplyCommand.RaiseCanExecuteChanged();
                RepairCommand.RaiseCanExecuteChanged();
                RevertCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public TweakStateKind State
    {
        get => _state;
        private set
        {
            if (SetProperty(ref _state, value))
            {
                OnPropertyChanged(nameof(StateLabel));
                OnPropertyChanged(nameof(CanApply));
                OnPropertyChanged(nameof(CanRevert));
                OnPropertyChanged(nameof(CanRepair));
                OnPropertyChanged(nameof(CanToggle));
                OnPropertyChanged(nameof(IsToggleChecked));
                ApplyCommand.RaiseCanExecuteChanged();
                RepairCommand.RaiseCanExecuteChanged();
                RevertCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool CanApply => !IsBusy && State == TweakStateKind.Available;

    public bool CanRevert => !IsBusy && State is TweakStateKind.Applied or TweakStateKind.Drifted;

    public bool CanRepair => !IsBusy && State == TweakStateKind.Drifted;

    public bool CanToggle => !IsBusy && (CanApply || CanRevert);

    public bool IsToggleChecked
    {
        get => State is TweakStateKind.Applied or TweakStateKind.AlreadyConfigured;
        set
        {
            if (value && CanApply)
            {
                _ = TryApplyAsync();
            }
            else if (!value && CanRevert)
            {
                _ = TryRevertAsync();
            }
            else
            {
                OnPropertyChanged(nameof(IsToggleChecked));
            }
        }
    }

    public AsyncRelayCommand ApplyCommand { get; }

    public AsyncRelayCommand RepairCommand { get; }

    public AsyncRelayCommand RevertCommand { get; }

    public async Task RefreshAsync()
    {
        var evaluation = await _engine.EvaluateAsync(Definition);
        State = evaluation.State;
    }

    public async Task<bool> TryApplyAsync()
    {
        if (!CanApply)
        {
            return true;
        }

        IsBusy = true;
        try
        {
            await _engine.ApplyAsync(Definition);
            await RefreshAsync();
            IsSelected = false;
            _reportStatus($"Настройка «{Name}» применена. Исходное значение сохранено.", false);
            return true;
        }
        catch (Exception error)
        {
            await RefreshAfterFailureAsync();
            _reportStatus($"Не удалось применить «{Name}»: {error.Message}", true);
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private Task ApplyAsync() => TryApplyAsync();

    public Task<bool> TryRevertAsync() => RevertAsync();

    private async Task RepairAsync()
    {
        IsBusy = true;
        try
        {
            await _engine.RepairAsync(Definition);
            await RefreshAsync();
            _reportStatus($"Настройка «{Name}» восстановлена без изменения исходной точки отката.", false);
        }
        catch (Exception error)
        {
            await RefreshAfterFailureAsync();
            _reportStatus($"Не удалось восстановить действие «{Name}»: {error.Message}", true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<bool> RevertAsync()
    {
        IsBusy = true;
        try
        {
            await _engine.RevertAsync(Definition);
            await RefreshAsync();
            _reportStatus($"Исходное состояние для «{Name}» восстановлено.", false);
            return true;
        }
        catch (Exception error)
        {
            await RefreshAfterFailureAsync();
            _reportStatus($"Не удалось восстановить «{Name}»: {error.Message}", true);
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RefreshAfterFailureAsync()
    {
        try
        {
            await RefreshAsync();
        }
        catch
        {
            // Preserve the original operation error in the status bar.
        }
    }
}
