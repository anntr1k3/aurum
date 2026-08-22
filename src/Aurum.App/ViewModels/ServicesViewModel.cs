using System.Collections.ObjectModel;
using Aurum.Core;
using Aurum.Infrastructure.Windows;

namespace Aurum.App.ViewModels;

public sealed class ServiceGroupItemViewModel : ObservableObject
{
    private readonly ServiceGroupDefinition _group;
    private readonly ServiceManager _manager;
    private readonly Action<string, bool> _report;
    private readonly Func<string, bool> _confirm;
    private readonly Func<Task> _refreshAll;
    private readonly Func<IReadOnlyList<ServiceAnalysisItem>> _liveAnalysis;
    private ServiceGroupEvaluation? _evaluation;

    public ServiceGroupItemViewModel(
        ServiceGroupDefinition group,
        ServiceManager manager,
        Action<string, bool> report,
        Func<string, bool> confirm,
        Func<Task> refreshAll,
        Func<IReadOnlyList<ServiceAnalysisItem>> liveAnalysis)
    {
        _group = group;
        _manager = manager;
        _report = report;
        _confirm = confirm;
        _refreshAll = refreshAll;
        _liveAnalysis = liveAnalysis;

        DisableCommand = new AsyncRelayCommand(DisableAsync, () => CanDisable);
        RevertCommand = new AsyncRelayCommand(RevertAsync, () => CanRevert);
    }

    public string Id => _group.Id;
    public string Name => _group.Name;
    public string Description => _group.Description;
    public string Impact => _group.Impact;
    public IReadOnlyList<string> ServiceNames => _group.ServiceNames;

    public ServiceGroupEvaluation? Evaluation
    {
        get => _evaluation;
        set
        {
            if (SetProperty(ref _evaluation, value))
            {
                OnPropertyChanged(nameof(StatusLabel));
                OnPropertyChanged(nameof(IsApplied));
                OnPropertyChanged(nameof(IsDrifted));
                OnPropertyChanged(nameof(CanDisable));
                OnPropertyChanged(nameof(CanRevert));
                OnPropertyChanged(nameof(IsToggleDisabled));
                DisableCommand.RaiseCanExecuteChanged();
                RevertCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsApplied => Evaluation?.IsApplied == true;
    public bool IsDrifted => Evaluation?.IsDrifted == true;
    public bool CanDisable => Evaluation is not null && !IsApplied;
    public bool CanRevert => Evaluation is not null && (Evaluation.TrackedCount > 0);

    public bool IsToggleDisabled
    {
        get => IsApplied;
        set
        {
            if (value && CanDisable)
            {
                _ = DisableAsync();
            }
            else if (!value && CanRevert)
            {
                _ = RevertAsync();
            }
            else
            {
                OnPropertyChanged(nameof(IsToggleDisabled));
            }
        }
    }

    public string StatusLabel
    {
        get
        {
            if (Evaluation is null) return "ПРОВЕРКА…";
            if (Evaluation.IsDrifted) return "ДРЕЙФ СОСТОЯНИЯ";
            if (Evaluation.IsApplied) return $"ОТКЛЮЧЕНО ({Evaluation.AppliedCount}/{Evaluation.TotalCount})";
            if (Evaluation.AppliedCount > 0) return $"ЧАСТИЧНО ({Evaluation.AppliedCount}/{Evaluation.TotalCount})";
            return "АКТИВНО";
        }
    }

    public AsyncRelayCommand DisableCommand { get; }
    public AsyncRelayCommand RevertCommand { get; }

    private async Task DisableAsync()
    {
        if (!_confirm(
                $"Отключить группу служб «{Name}» ({string.Join(", ", ServiceNames)})?\n\n" +
                $"Последствия:\n{Impact}\n\n" +
                "Aurum сохранит исходные типы запуска для возможности возврата."))
        {
            return;
        }

        try
        {
            ServiceAnalyzer.EnsureDisableBatchHasNoRunningDependants(ServiceNames, _liveAnalysis());
            foreach (var serviceName in ServiceNames)
            {
                try
                {
                    await _manager.DisableServiceAsync(serviceName);
                }
                catch (InvalidOperationException)
                {
                    // If already disabled or tracked, continue best effort
                }
            }

            _report($"Группа «{Name}» успешно отключена.", false);
            await _refreshAll();
        }
        catch (Exception error)
        {
            _report($"Ошибка при отключении группы «{Name}»: {error.Message}", true);
        }
    }

    private async Task RevertAsync()
    {
        if (!_confirm($"Восстановить исходное состояние служб группы «{Name}»?"))
        {
            return;
        }

        try
        {
            foreach (var serviceName in ServiceNames)
            {
                await _manager.RevertServiceAsync(serviceName);
            }

            _report($"Состояние служб группы «{Name}» восстановлено.", false);
            await _refreshAll();
        }
        catch (Exception error)
        {
            _report($"Ошибка при восстановлении группы «{Name}»: {error.Message}", true);
        }
    }
}

public sealed class ServiceItemViewModel : ObservableObject
{
    private readonly ServiceAnalysisItem _item;
    private readonly ServiceManager _manager;
    private readonly Action<string, bool> _report;
    private readonly Func<string, bool> _confirm;
    private readonly Func<Task> _refreshAll;
    private ServiceEvaluation? _evaluation;

    public ServiceItemViewModel(
        ServiceAnalysisItem item,
        ServiceManager manager,
        Action<string, bool> report,
        Func<string, bool> confirm,
        Func<Task> refreshAll)
    {
        _item = item;
        _manager = manager;
        _report = report;
        _confirm = confirm;
        _refreshAll = refreshAll;

        DisableCommand = new AsyncRelayCommand(DisableAsync, () => CanDisable);
        RevertCommand = new AsyncRelayCommand(RevertAsync, () => CanRevert);
        RepairCommand = new AsyncRelayCommand(RepairAsync, () => CanRepair);
    }

    public ServiceAnalysisItem Item => _item;
    public string Name => _item.Service.Name;
    public string DisplayName => _item.Service.DisplayName;
    public string Description => string.IsNullOrWhiteSpace(_item.Service.Description) ? "Описание не предоставлено службой." : _item.Service.Description;
    public string Capability => _item.Capability;
    public string Guidance => _item.Guidance;
    public ServiceSafetyClass Safety => _item.Safety;
    public string SafetyLabel => Safety switch { ServiceSafetyClass.Protected => "ЗАЩИЩЕНА", ServiceSafetyClass.ContextDependent => "ЗАВИСИТ ОТ СЦЕНАРИЯ", _ => "НЕ КЛАССИФИЦИРОВАНА" };
    public string StateLabel => _item.Service.State == ServiceRunState.Running ? "РАБОТАЕТ" : _item.Service.State == ServiceRunState.Stopped ? "ОСТАНОВЛЕНА" : _item.Service.State.ToString().ToUpperInvariant();
    public string StartModeLabel => _item.Service.StartMode switch { ServiceStartMode.Automatic when _item.Service.IsDelayedAutoStart => "Автоматически (отложенно)", ServiceStartMode.Automatic => "Автоматически", ServiceStartMode.Manual => "Вручную / по триггеру", ServiceStartMode.Disabled => "Отключена", _ => _item.Service.StartMode.ToString() };
    public string ProcessLabel => _item.Service.ProcessId == 0 ? "Нет активного процесса" : $"PID {_item.Service.ProcessId}";
    public string DependenciesLabel => _item.Service.Dependencies.Count == 0 ? "Нет прямых зависимостей" : string.Join(" · ", _item.Service.Dependencies);
    public string DependantsLabel => _item.Dependants.Count == 0 ? "Нет найденных потребителей" : string.Join(" · ", _item.Dependants);

    public ServiceEvaluation? Evaluation
    {
        get => _evaluation;
        set
        {
            if (SetProperty(ref _evaluation, value))
            {
                OnPropertyChanged(nameof(TrackingState));
                OnPropertyChanged(nameof(TrackingStateLabel));
                OnPropertyChanged(nameof(CanDisable));
                OnPropertyChanged(nameof(CanRevert));
                OnPropertyChanged(nameof(CanRepair));
                DisableCommand.RaiseCanExecuteChanged();
                RevertCommand.RaiseCanExecuteChanged();
                RepairCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public ServiceTrackingState TrackingState => Evaluation?.TrackingState ?? ServiceTrackingState.NotTracked;

    public string TrackingStateLabel => TrackingState switch
    {
        ServiceTrackingState.Applied => "ОТКЛЮЧЕНА AURUM",
        ServiceTrackingState.Drifted => "ИЗМЕНЕНА ПОСЛЕ ПРИМЕНЕНИЯ",
        ServiceTrackingState.AlreadyDisabledOutside => "ОТКЛЮЧЕНА ВНЕ AURUM",
        _ => string.Empty
    };

    public string CategoryCode => Name switch
    {
        "DiagTrack" or "dmwappushservice" or "weridsvc" => "telemetry",
        "XboxGipSvc" or "XblAuthManager" or "XblGameSave" or "XboxNetApiSvc" => "xbox",
        "Spooler" or "Fax" => "print",
        "MapsBroker" or "lfsvc" => "maps",
        "TabletInputService" => "touch",
        _ => "other"
    };

    public string CategoryLabel => Name switch
    {
        "DiagTrack" or "dmwappushservice" or "weridsvc" => "Телеметрия",
        "XboxGipSvc" or "XblAuthManager" or "XblGameSave" or "XboxNetApiSvc" => "Xbox Live",
        "Spooler" or "Fax" => "Печать & Факс",
        "MapsBroker" or "lfsvc" => "Карты & Гео",
        "TabletInputService" => "Сенсор & Перо",
        "wisvc" or "RetailDemo" => "Тестирование",
        "WbioSrvc" => "Биометрия",
        "bthserv" => "Bluetooth",
        "PhoneSvc" => "Связь",
        "WSearch" => "Поиск",
        _ => "Служба"
    };

    public bool IsServiceDisabled
    {
        get => TrackingState == ServiceTrackingState.Applied || _item.Service.StartMode == ServiceStartMode.Disabled;
        set
        {
            if (value && CanDisable)
            {
                _ = DisableAsync();
            }
            else if (!value && CanRevert)
            {
                _ = RevertAsync();
            }
            else
            {
                OnPropertyChanged(nameof(IsServiceDisabled));
            }
        }
    }

    public async Task<bool> DisableDirectAsync()
    {
        if (!CanDisable) return false;
        try
        {
            await _manager.DisableServiceAsync(Name);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> RevertDirectAsync()
    {
        if (!CanRevert)
        {
            return false;
        }

        await _manager.RevertServiceAsync(Name);
        return true;
    }

    public bool CanDisable => Safety == ServiceSafetyClass.ContextDependent &&
                              TrackingState == ServiceTrackingState.NotTracked &&
                              _item.Service.StartMode != ServiceStartMode.Disabled;

    public bool CanRevert => TrackingState is ServiceTrackingState.Applied or ServiceTrackingState.Drifted;
    public bool CanRepair => TrackingState == ServiceTrackingState.Drifted;

    public AsyncRelayCommand DisableCommand { get; }
    public AsyncRelayCommand RevertCommand { get; }
    public AsyncRelayCommand RepairCommand { get; }

    private async Task DisableAsync()
    {
        if (!_confirm(
                $"Отключить службу «{DisplayName}» ({Name})?\n\n" +
                $"Назначение: {Capability}\n" +
                $"{Guidance}\n\n" +
                (_item.Dependants.Count == 0
                    ? "От этой службы не зависит ни одна другая зарегистрированная служба."
                    : $"Внимание! От этой службы зависят: {string.Join(", ", _item.Dependants)}.\n\n") +
                "Aurum сохранит текущий тип запуска для точного отката."))
        {
            return;
        }

        try
        {
            await _manager.DisableServiceAsync(Name);
            _report($"Служба «{DisplayName}» отключена.", false);
            await _refreshAll();
        }
        catch (Exception error)
        {
            _report($"Не удалось отключить службу «{DisplayName}»: {error.Message}", true);
        }
    }

    private async Task RevertAsync()
    {
        if (!_confirm($"Восстановить исходный тип запуска службы «{DisplayName}»?"))
        {
            return;
        }

        try
        {
            await _manager.RevertServiceAsync(Name);
            _report($"Исходное состояние службы «{DisplayName}» восстановлено.", false);
            await _refreshAll();
        }
        catch (Exception error)
        {
            _report($"Не удалось восстановить службу «{DisplayName}»: {error.Message}", true);
        }
    }

    private async Task RepairAsync()
    {
        try
        {
            await _manager.RepairServiceAsync(Name);
            _report($"Служба «{DisplayName}» повторно отключена.", false);
            await _refreshAll();
        }
        catch (Exception error)
        {
            _report($"Не удалось восстановить состояние службы «{DisplayName}»: {error.Message}", true);
        }
    }
}

public sealed class ServicesViewModel : ObservableObject
{
    private readonly WindowsServiceInventory _inventory;
    private readonly ServiceManager _manager;
    private readonly Action<string, bool> _report;
    private readonly Func<string, bool> _confirm;
    private IReadOnlyList<ServiceItemViewModel> _all = [];
    private IReadOnlyList<ServiceAnalysisItem> _analysis = [];
    private ServiceItemViewModel? _selectedService;
    private string _searchText = string.Empty;
    private string _summary = "Считываем базу Service Control Manager…";
    private bool _contextualOnly = true;
    private string _selectedCategory = "all";
    private bool _isBusy;

    public ServicesViewModel(
        WindowsServiceInventory inventory,
        ServiceManager manager,
        Action<string, bool> report,
        Func<string, bool> confirm)
    {
        _inventory = inventory;
        _manager = manager;
        _report = report;
        _confirm = confirm;

        Groups = new ObservableCollection<ServiceGroupItemViewModel>(
            BuiltInServiceGroups.All.Select(g => new ServiceGroupItemViewModel(g, _manager, _report, _confirm, RefreshAsync, () => _analysis)));

        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
        ShowContextualCommand = new RelayCommand<object>(_ => { ContextualOnly = true; ApplyFilter(); });
        ShowAllCommand = new RelayCommand<object>(_ => { ContextualOnly = false; ApplyFilter(); });
        SetCategoryCommand = new RelayCommand<string>(SetCategory);
        OptimizeAllServicesCommand = new AsyncRelayCommand(OptimizeAllServicesAsync, () => !IsBusy);
        RevertAllServicesCommand = new AsyncRelayCommand(RevertAllServicesAsync, () => !IsBusy);
    }

    public ObservableCollection<ServiceGroupItemViewModel> Groups { get; }
    public ObservableCollection<ServiceItemViewModel> Services { get; } = [];
    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand OptimizeAllServicesCommand { get; }
    public AsyncRelayCommand RevertAllServicesCommand { get; }
    public RelayCommand<object> ShowContextualCommand { get; }
    public RelayCommand<object> ShowAllCommand { get; }
    public RelayCommand<string> SetCategoryCommand { get; }
    public ServiceItemViewModel? SelectedService { get => _selectedService; set => SetProperty(ref _selectedService, value); }
    public string Summary { get => _summary; private set => SetProperty(ref _summary, value); }
    public bool ContextualOnly { get => _contextualOnly; private set { if (SetProperty(ref _contextualOnly, value)) { OnPropertyChanged(nameof(ContextualFilterLabel)); } } }
    public string ContextualFilterLabel => ContextualOnly ? "ПОКАЗАНЫ КОНТЕКСТНЫЕ" : "ПОКАЗАНЫ ВСЕ";
    public string SearchText { get => _searchText; set { if (SetProperty(ref _searchText, value)) ApplyFilter(); } }
    public bool IsBusy { get => _isBusy; private set { if (SetProperty(ref _isBusy, value)) { RefreshCommand.RaiseCanExecuteChanged(); OptimizeAllServicesCommand.RaiseCanExecuteChanged(); RevertAllServicesCommand.RaiseCanExecuteChanged(); } } }
    
    public string SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (SetProperty(ref _selectedCategory, value))
            {
                ApplyFilter();
                OnPropertyChanged(nameof(IsCategoryAll));
                OnPropertyChanged(nameof(IsCategoryTelemetry));
                OnPropertyChanged(nameof(IsCategoryXbox));
                OnPropertyChanged(nameof(IsCategoryPrint));
                OnPropertyChanged(nameof(IsCategoryMaps));
                OnPropertyChanged(nameof(IsCategoryTouch));
                OnPropertyChanged(nameof(IsCategoryOther));
            }
        }
    }

    public bool IsCategoryAll => SelectedCategory == "all";
    public bool IsCategoryTelemetry => SelectedCategory == "telemetry";
    public bool IsCategoryXbox => SelectedCategory == "xbox";
    public bool IsCategoryPrint => SelectedCategory == "print";
    public bool IsCategoryMaps => SelectedCategory == "maps";
    public bool IsCategoryTouch => SelectedCategory == "touch";
    public bool IsCategoryOther => SelectedCategory == "other";

    public void SetCategory(string? category)
    {
        SelectedCategory = category ?? "all";
    }

    public async Task OptimizeAllServicesAsync()
    {
        if (!_confirm("Отключить все безопасные фоновые службы (диагностика, телеметрия, отчёты об ошибках, демонстрационный режим, автономные карты)?\n\nВсе исходные типы запуска будут сохранены для мгновенного отката."))
        {
            return;
        }

        IsBusy = true;
        try
        {
            var targetServiceNames = new[] { "DiagTrack", "dmwappushservice", "weridsvc", "wisvc", "RetailDemo", "MapsBroker", "lfsvc", "PhoneSvc" };
            ServiceAnalyzer.EnsureDisableBatchHasNoRunningDependants(targetServiceNames, _analysis);
            int count = 0;
            foreach (var name in targetServiceNames)
            {
                var serviceVm = _all.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (serviceVm is not null && serviceVm.CanDisable)
                {
                    await serviceVm.DisableDirectAsync();
                    count++;
                }
            }

            await RefreshAsync();
            _report($"Оптимизация служб завершена: отключено {count} фоновых служб.", false);
        }
        catch (Exception ex)
        {
            _report($"Ошибка при оптимизации служб: {ex.Message}", true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Revert every tracked service without prompting and without reporting its own status,
    /// for the global revert that confirms once and reports one summary. Returns how many
    /// services were restored; failures propagate so the caller can list them.
    /// </summary>
    public async Task<int> RevertAllServicesDirectAsync()
    {
        var count = 0;
        var failures = new List<Exception>();
        foreach (var serviceVm in _all.Where(static s => s.CanRevert).ToList())
        {
            try
            {
                if (await serviceVm.RevertDirectAsync())
                {
                    count++;
                }
            }
            catch (Exception error)
            {
                failures.Add(error);
            }
        }

        if (count > 0)
        {
            await RefreshAsync();
        }

        if (failures.Count != 0)
        {
            throw new AggregateException(
                $"Не удалось восстановить {failures.Count} служб.",
                failures);
        }

        return count;
    }

    public async Task RevertAllServicesAsync()
    {
        if (!_confirm("Восстановить исходные параметры всех служб, изменённых через Aurum?"))
        {
            return;
        }

        IsBusy = true;
        try
        {
            int count = 0;
            foreach (var serviceVm in _all.Where(s => s.CanRevert))
            {
                await serviceVm.RevertDirectAsync();
                count++;
            }

            await RefreshAsync();
            _report($"Восстановление завершено: возвращено {count} служб к исходному состоянию.", false);
        }
        catch (Exception ex)
        {
            _report($"Ошибка при восстановлении служб: {ex.Message}", true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public Task InitializeAsync() => RefreshAsync();

    public async Task RefreshAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            var captured = await _inventory.CaptureAsync();
            _analysis = captured.ToArray();
            var serviceDefs = captured.Select(static c => c.Service).ToArray();

            // Evaluate groups
            foreach (var groupVm in Groups)
            {
                var groupEval = await _manager.EvaluateGroupAsync(
                    BuiltInServiceGroups.All.First(g => g.Id == groupVm.Id),
                    serviceDefs);
                groupVm.Evaluation = groupEval;
            }

            var viewModels = new List<ServiceItemViewModel>(captured.Count);
            foreach (var item in captured)
            {
                var vm = new ServiceItemViewModel(item, _manager, _report, _confirm, RefreshAsync);
                var eval = await _manager.EvaluateServiceAsync(item.Service);
                vm.Evaluation = eval;
                viewModels.Add(vm);
            }

            _all = viewModels;
            ApplyFilter();

            var running = _all.Count(static item => item.Item.Service.State == ServiceRunState.Running);
            var contextual = _all.Count(static item => item.Safety == ServiceSafetyClass.ContextDependent);
            var tracked = _all.Count(static item => item.TrackingState is ServiceTrackingState.Applied or ServiceTrackingState.Drifted);
            var drifted = _all.Count(static item => item.TrackingState == ServiceTrackingState.Drifted);

            Summary = $"Всего: {_all.Count} · работает: {running} · контекстных: {contextual} · отключено Aurum: {tracked}" +
                      (drifted > 0 ? $" · требуют восстановления: {drifted}" : string.Empty);
        }
        catch (Exception error)
        {
            Summary = $"Не удалось прочитать службы: {error.Message}";
            _report(Summary, true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyFilter()
    {
        var query = _all.AsEnumerable();
        if (ContextualOnly) query = query.Where(static item => item.Safety == ServiceSafetyClass.ContextDependent);
        
        if (SelectedCategory != "all")
        {
            query = query.Where(item => item.CategoryCode.Equals(SelectedCategory, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            query = query.Where(item => item.Name.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase) ||
                                        item.DisplayName.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase) ||
                                        item.Capability.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase) ||
                                        item.CategoryLabel.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase));
        }
        Services.Clear();
        foreach (var item in query) Services.Add(item);
        if (SelectedService is null || !Services.Contains(SelectedService))
        {
            SelectedService = Services.FirstOrDefault();
        }
    }
}

