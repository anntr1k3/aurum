using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Aurum.App.ViewModels;

public sealed class SimpleFeatureItemViewModel : ObservableObject
{
    private readonly Func<Task<bool>> _onApply;
    private readonly Func<Task<bool>> _onRevert;
    private readonly Action<string, bool> _reportStatus;
    private bool _isActive;
    private bool _isBusy;
    private bool _isUpdatingInternally;

    public SimpleFeatureItemViewModel(
        string id,
        string title,
        string category,
        string categoryLabel,
        string icon,
        string description,
        IReadOnlyList<string> badges,
        Func<Task<bool>> onApply,
        Func<Task<bool>> onRevert,
        Action<string, bool> reportStatus)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Title = title ?? throw new ArgumentNullException(nameof(title));
        Category = category ?? throw new ArgumentNullException(nameof(category));
        CategoryLabel = categoryLabel ?? throw new ArgumentNullException(nameof(categoryLabel));
        Icon = icon ?? "⚡";
        Description = description ?? string.Empty;
        Badges = badges ?? Array.Empty<string>();
        _onApply = onApply ?? throw new ArgumentNullException(nameof(onApply));
        _onRevert = onRevert ?? throw new ArgumentNullException(nameof(onRevert));
        _reportStatus = reportStatus ?? throw new ArgumentNullException(nameof(reportStatus));

        ToggleCommand = new AsyncRelayCommand(ToggleAsync, () => !IsBusy);
    }

    public string Id { get; }
    public string Title { get; }
    public string Category { get; }
    public string CategoryLabel { get; }
    public string Icon { get; }
    public string Description { get; }
    public IReadOnlyList<string> Badges { get; }
    public ICommand ToggleCommand { get; }

    public bool IsActive
    {
        get => _isActive;
        private set
        {
            if (SetProperty(ref _isActive, value))
            {
                OnPropertyChanged(nameof(StatusLabel));
                OnPropertyChanged(nameof(IsToggleChecked));
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
                OnPropertyChanged(nameof(CanToggle));
                (ToggleCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    public bool CanToggle => !IsBusy;
    public string StatusLabel => IsActive ? "Активно (Включено)" : "По умолчанию";

    public bool IsToggleChecked
    {
        get => _isActive;
        set
        {
            if (_isUpdatingInternally || _isActive == value)
            {
                return;
            }

            _ = SetActiveStateAsync(value);
        }
    }

    public void UpdateActiveStateDirectly(bool active)
    {
        _isUpdatingInternally = true;
        try
        {
            IsActive = active;
        }
        finally
        {
            _isUpdatingInternally = false;
        }
    }

    private async Task ToggleAsync()
    {
        await SetActiveStateAsync(!IsActive);
    }

    public async Task<bool> ToggleDirectAsync(bool targetState)
    {
        return await SetActiveStateAsync(targetState);
    }

    private async Task<bool> SetActiveStateAsync(bool targetState)
    {
        if (IsBusy)
        {
            return false;
        }

        IsBusy = true;
        try
        {
            bool success;
            if (targetState)
            {
                success = await _onApply();
                if (success)
                {
                    UpdateActiveStateDirectly(true);
                    _reportStatus($"Включено: {Title}", false);
                }
                else
                {
                    UpdateActiveStateDirectly(false);
                    _reportStatus($"Не удалось включить: {Title}", true);
                }
            }
            else
            {
                success = await _onRevert();
                if (success)
                {
                    UpdateActiveStateDirectly(false);
                    _reportStatus($"Отключено (исходное состояние): {Title}", false);
                }
                else
                {
                    UpdateActiveStateDirectly(true);
                    _reportStatus($"Не удалось отключить: {Title}", true);
                }
            }
            return success;
        }
        catch (Exception ex)
        {
            _reportStatus($"Ошибка при изменении {Title}: {ex.Message}", true);
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
