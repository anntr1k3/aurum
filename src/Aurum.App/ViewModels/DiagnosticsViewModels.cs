using Aurum.Infrastructure.Windows;

namespace Aurum.App.ViewModels;

public sealed class AtlasCheckItemViewModel
{
    public AtlasCheckItemViewModel(HealthCheckResult result)
    {
        Result = result;
    }

    public HealthCheckResult Result { get; }

    public string Name => Result.Name;

    public string Details => Result.Details;

    public string StatusLabel => Result.Status switch
    {
        HealthCheckStatus.Healthy => "ИСПРАВНО",
        HealthCheckStatus.Warning => "ВНИМАНИЕ",
        HealthCheckStatus.Failed => "НАРУШЕНО",
        HealthCheckStatus.NotApplicable => "НЕ ПРИМЕНИМО",
        _ => "НЕИЗВЕСТНО"
    };
}

public sealed class CleanupCategoryViewModel : ObservableObject
{
    private bool _isSelected;

    public CleanupCategoryViewModel(CleanupCategory category, bool isSelected)
    {
        Category = category;
        _isSelected = isSelected;
    }

    public CleanupCategory Category { get; }

    public string Id => Category.Id;

    public string Name => Category.Name;

    public string Description => Category.Description;

    public string RootPath => Category.RootPath;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
