using System;
using System.Threading.Tasks;
using System.Windows.Input;
using Aurum.Core;

namespace Aurum.App.ViewModels;

public sealed class SystemTimerViewModel : ObservableObject, IDisposable
{
    private readonly ISystemTimerService _timerService;
    private readonly Action<string, bool> _reportStatus;
    private TimerResolutionInfo _info;
    private bool _isGlobalPolicyEnabled;

    public SystemTimerViewModel(
        ISystemTimerService timerService,
        Action<string, bool> reportStatus)
    {
        _timerService = timerService ?? throw new ArgumentNullException(nameof(timerService));
        _reportStatus = reportStatus ?? throw new ArgumentNullException(nameof(reportStatus));

        _info = _timerService.GetResolution();
        _isGlobalPolicyEnabled = _timerService.IsGlobalResolutionPolicyEnabled();

        Set05MsCommand = new RelayCommand<object>(_ => SetResolution(0.5));
        Set10MsCommand = new RelayCommand<object>(_ => SetResolution(1.0));
        SetDefaultCommand = new RelayCommand<object>(_ => ResetResolution());
        ToggleGlobalPolicyCommand = new AsyncRelayCommand(ToggleGlobalPolicyAsync);
        RefreshCommand = new RelayCommand<object>(_ => Refresh());
    }

    public ICommand Set05MsCommand { get; }
    public ICommand Set10MsCommand { get; }
    public ICommand SetDefaultCommand { get; }
    public ICommand ToggleGlobalPolicyCommand { get; }
    public ICommand RefreshCommand { get; }

    public TimerResolutionInfo Info
    {
        get => _info;
        private set
        {
            if (SetProperty(ref _info, value))
            {
                OnPropertyChanged(nameof(CurrentResolutionLabel));
                OnPropertyChanged(nameof(FrequencyLabel));
                OnPropertyChanged(nameof(MinimumResolutionLabel));
                OnPropertyChanged(nameof(MaximumResolutionLabel));
                OnPropertyChanged(nameof(Is05MsActive));
                OnPropertyChanged(nameof(Is10MsActive));
                OnPropertyChanged(nameof(IsDefaultActive));
            }
        }
    }

    public string CurrentResolutionLabel => $"{Info.CurrentMs:0.000} мс";
    public string FrequencyLabel => $"{Info.FrequencyHz:0} Гц (тиков в сек)";
    public string MinimumResolutionLabel => $"Мин: {Info.MinimumMs:0.000} мс";
    public string MaximumResolutionLabel => $"Макс: {Info.MaximumMs:0.000} мс";

    public bool Is05MsActive => Math.Abs(Info.CurrentMs - 0.5) < 0.05;
    public bool Is10MsActive => Math.Abs(Info.CurrentMs - 1.0) < 0.05;
    public bool IsDefaultActive => !Is05MsActive && !Is10MsActive;

    public bool IsGlobalPolicyEnabled
    {
        get => _isGlobalPolicyEnabled;
        private set
        {
            if (SetProperty(ref _isGlobalPolicyEnabled, value))
            {
                OnPropertyChanged(nameof(IsToggleGlobalPolicy));
            }
        }
    }

    public bool IsToggleGlobalPolicy
    {
        get => IsGlobalPolicyEnabled;
        set
        {
            if (_isGlobalPolicyEnabled != value)
            {
                _ = ToggleGlobalPolicyAsync();
            }
        }
    }

    public void Refresh()
    {
        Info = _timerService.GetResolution();
        IsGlobalPolicyEnabled = _timerService.IsGlobalResolutionPolicyEnabled();
    }

    public bool SetResolution(double milliseconds)
    {
        var success = _timerService.SetResolution(milliseconds);
        Refresh();

        if (success)
        {
            _reportStatus($"Системный таймер переведён на {milliseconds:0.0} мс ({1000.0 / milliseconds:0} Гц).", false);
        }
        else
        {
            _reportStatus("Не удалось изменить разрешение системного таймера.", true);
        }

        return success;
    }

    public bool ResetResolution()
    {
        var success = _timerService.ResetResolution();
        Refresh();

        if (success)
        {
            _reportStatus("Системный таймер возвращён к значению по умолчанию Windows.", false);
        }
        else
        {
            _reportStatus("Не удалось вернуть разрешение системного таймера к значению по умолчанию.", true);
        }

        return success;
    }

    public async Task ToggleGlobalPolicyAsync()
    {
        var targetState = !IsGlobalPolicyEnabled;
        try
        {
            await _timerService.SetGlobalResolutionPolicyAsync(targetState);
            IsGlobalPolicyEnabled = targetState;
            _reportStatus(
                targetState
                    ? "Включён глобальный запрос таймера (GlobalTimerResolutionRequests = 1)."
                    : "Отключён глобальный запрос таймера.",
                false);
        }
        catch (Exception ex)
        {
            _reportStatus($"Ошибка настройки политики таймера: {ex.Message}", true);
        }
    }

    public void Dispose()
    {
        (_timerService as IDisposable)?.Dispose();
    }
}
