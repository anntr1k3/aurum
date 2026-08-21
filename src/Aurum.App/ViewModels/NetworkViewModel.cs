using System.Collections.ObjectModel;
using Aurum.Core;

namespace Aurum.App.ViewModels;

public sealed class NetworkAdapterItemViewModel
{
    public NetworkAdapterItemViewModel(NetworkAdapterInfo adapter) => Adapter = adapter;

    public NetworkAdapterInfo Adapter { get; }
    public string Id => Adapter.Id;
    public string Name => Adapter.Name;
    public string Description => Adapter.Description;
    public string RoleLabel => Adapter.IsPrimary ? "ОСНОВНОЙ МАРШРУТ" : Adapter.OperationalStatus == "Up" ? "ПОДКЛЮЧЁН" : "НЕАКТИВЕН";
    public string StatusLabel => Adapter.OperationalStatus == "Up" ? "Работает" : Adapter.OperationalStatus;
    public string TypeLabel => TranslateType(Adapter.InterfaceType);
    public string SpeedLabel => FormatSpeed(Adapter.SpeedBitsPerSecond);
    public string MtuLabel => Adapter.Mtu is null ? "—" : Adapter.Mtu.Value.ToString();
    public string PhysicalAddressLabel => Adapter.PhysicalAddress;
    public string IPv4Label => JoinOrDash(Adapter.IPv4Addresses);
    public string IPv6Label => JoinOrDash(Adapter.IPv6Addresses);
    public string GatewaysLabel => JoinOrDash(Adapter.Gateways);
    public string DnsLabel => JoinOrDash(Adapter.DnsServers);
    public string AddressSummary => Adapter.IPv4Addresses.FirstOrDefault()
                                    ?? Adapter.IPv6Addresses.FirstOrDefault()
                                    ?? "Адрес не назначен";

    private static string JoinOrDash(IReadOnlyList<string> values) => values.Count == 0 ? "—" : string.Join(" · ", values);

    private static string TranslateType(string type) => type switch
    {
        "Ethernet" => "Ethernet",
        "Wireless80211" => "Wi-Fi",
        "Tunnel" => "Туннель / VPN",
        "Ppp" => "PPP",
        _ => type,
    };

    private static string FormatSpeed(long bitsPerSecond)
    {
        if (bitsPerSecond <= 0) return "Не определена";
        if (bitsPerSecond >= 1_000_000_000) return $"{bitsPerSecond / 1_000_000_000d:0.##} Гбит/с";
        if (bitsPerSecond >= 1_000_000) return $"{bitsPerSecond / 1_000_000d:0.##} Мбит/с";
        return $"{bitsPerSecond / 1_000d:0.##} Кбит/с";
    }
}

public sealed class NetworkViewModel : ObservableObject
{
    private readonly NetworkDiagnosticsManager _manager;
    private readonly NetworkTuningManager _tuningManager;
    private readonly Action<string, bool> _report;
    private readonly Func<string, bool> _confirm;
    private NetworkAdapterItemViewModel? _selectedAdapter;
    private string _summary = "Считываем сетевые интерфейсы…";
    private string _tcpStatus = "Глобальные TCP-параметры ещё не прочитаны.";
    private string _probeTarget = "1.1.1.1";
    private string _probeSummary = "Тест не запускался";
    private string _probeDetails = "Aurum отправит 4 ICMP-запроса только после нажатия кнопки.";
    private bool _probeFailed;
    private bool _isAdvancedVisible;
    private bool _isBusy;

    public NetworkViewModel(
        NetworkDiagnosticsManager manager,
        NetworkTuningManager tuningManager,
        Action<string, bool> report,
        Func<string, bool> confirm)
    {
        _manager = manager;
        _tuningManager = tuningManager;
        _report = report;
        _confirm = confirm;

        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
        ProbeCommand = new AsyncRelayCommand(ProbeAsync, () => CanProbe);
        ToggleAdvancedCommand = new RelayCommand<object>(_ => IsAdvancedVisible = !IsAdvancedVisible);

        ApplyDnsCommand = new AsyncRelayCommand<DnsPresetDefinition>(ApplyDnsAsync, _ => !IsBusy && SelectedAdapter is not null);
        RevertDnsCommand = new AsyncRelayCommand(RevertDnsAsync, () => !IsBusy && SelectedAdapter is not null);
        FlushDnsCommand = new AsyncRelayCommand(FlushDnsAsync, () => !IsBusy);
        SetTcpNormalCommand = new AsyncRelayCommand(() => SetTcpLevelAsync("normal"), () => !IsBusy);
        SetTcpDisabledCommand = new AsyncRelayCommand(() => SetTcpLevelAsync("disabled"), () => !IsBusy);
        OptimizeAllNetworkCommand = new AsyncRelayCommand(OptimizeAllNetworkAsync, () => !IsBusy);
    }

    public ObservableCollection<NetworkAdapterItemViewModel> Adapters { get; } = [];
    public ObservableCollection<NetworkSettingInfo> TcpSettings { get; } = [];
    public IReadOnlyList<DnsPresetDefinition> DnsPresets => BuiltInDnsPresets.All;

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand ProbeCommand { get; }
    public RelayCommand<object> ToggleAdvancedCommand { get; }
    public AsyncRelayCommand<DnsPresetDefinition> ApplyDnsCommand { get; }
    public AsyncRelayCommand RevertDnsCommand { get; }
    public AsyncRelayCommand FlushDnsCommand { get; }
    public AsyncRelayCommand SetTcpNormalCommand { get; }
    public AsyncRelayCommand SetTcpDisabledCommand { get; }
    public AsyncRelayCommand OptimizeAllNetworkCommand { get; }

    public string Summary { get => _summary; private set => SetProperty(ref _summary, value); }
    public string TcpStatus { get => _tcpStatus; private set => SetProperty(ref _tcpStatus, value); }
    public string ProbeSummary { get => _probeSummary; private set => SetProperty(ref _probeSummary, value); }
    public string ProbeDetails { get => _probeDetails; private set => SetProperty(ref _probeDetails, value); }
    public bool ProbeFailed { get => _probeFailed; private set => SetProperty(ref _probeFailed, value); }
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
    public bool CanProbe => !IsBusy && !string.IsNullOrWhiteSpace(ProbeTarget);

    public NetworkAdapterItemViewModel? SelectedAdapter
    {
        get => _selectedAdapter;
        set
        {
            if (SetProperty(ref _selectedAdapter, value))
            {
                ApplyDnsCommand.RaiseCanExecuteChanged();
                RevertDnsCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string ProbeTarget
    {
        get => _probeTarget;
        set
        {
            if (SetProperty(ref _probeTarget, value))
            {
                OnPropertyChanged(nameof(CanProbe));
                ProbeCommand.RaiseCanExecuteChanged();
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
                OnPropertyChanged(nameof(CanProbe));
                RefreshCommand.RaiseCanExecuteChanged();
                ProbeCommand.RaiseCanExecuteChanged();
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
            var selectedId = SelectedAdapter?.Id;
            var snapshot = await _manager.CaptureAsync();
            Adapters.Clear();
            foreach (var adapter in snapshot.Adapters)
                Adapters.Add(new NetworkAdapterItemViewModel(adapter));
            SelectedAdapter = Adapters.FirstOrDefault(adapter => adapter.Id == selectedId)
                              ?? Adapters.FirstOrDefault(static adapter => adapter.Adapter.IsPrimary)
                              ?? Adapters.FirstOrDefault();

            TcpSettings.Clear();
            foreach (var setting in snapshot.TcpSettings)
                TcpSettings.Add(setting);
            TcpStatus = snapshot.TcpStatus;
            var active = Adapters.Count(static adapter => adapter.Adapter.OperationalStatus == "Up");
            Summary = $"Интерфейсов: {Adapters.Count} · активно: {active} · снимок {snapshot.CapturedAt:HH:mm:ss}.";
            if (ProbeSummary == "Тест не запускался" && SelectedAdapter?.Adapter.Gateways.FirstOrDefault() is { } gateway)
                ProbeTarget = gateway;
        }
        catch (Exception error)
        {
            Summary = $"Не удалось прочитать сеть: {error.Message}";
            _report(Summary, true);
        }
        finally { IsBusy = false; }
    }

    private async Task ProbeAsync()
    {
        if (!CanProbe) return;
        IsBusy = true;
        ProbeFailed = false;
        ProbeSummary = "Проверяем задержку…";
        ProbeDetails = "Отправляется 4 ICMP-запроса с тайм-аутом 1,5 секунды.";
        try
        {
            var result = await _manager.ProbeAsync(ProbeTarget);
            ProbeFailed = result.Received == 0;
            ProbeSummary = result.Received == 0
                ? $"{result.Target} · ответов нет · потери 100%"
                : $"{result.Target} · средняя {result.AverageMilliseconds:0.#} мс · потери {result.LossPercent:0.#}%";
            ProbeDetails = result.Received == 0
                ? string.Join(" · ", result.Samples.Select((sample, index) => $"#{index + 1}: {sample.Status}"))
                : $"Получено {result.Received} из {result.Sent} · минимум {result.MinimumMilliseconds:0.#} мс · " +
                  $"максимум {result.MaximumMilliseconds:0.#} мс · {result.CompletedAt:HH:mm:ss}";
            _report($"Тест сети завершён: {ProbeSummary}.", ProbeFailed);
        }
        catch (Exception error)
        {
            ProbeFailed = true;
            ProbeSummary = "Тест не выполнен";
            ProbeDetails = error.Message;
            _report($"Тест сети не выполнен: {error.Message}", true);
        }
        finally { IsBusy = false; }
    }

    public bool CanRevertDns => SelectedAdapter != null;

    public async Task ApplyDnsDirectAsync(DnsPresetDefinition preset)
    {
        if (SelectedAdapter is null) return;
        var adapter = SelectedAdapter.Adapter;
        IsBusy = true;
        try
        {
            await _tuningManager.ApplyDnsPresetAsync(adapter, preset);
            _report($"Профиль DNS «{preset.Name}» успешно применён к «{adapter.Name}».", false);
            await RefreshAsync();
        }
        catch (Exception error)
        {
            _report($"Не удалось применить DNS к «{adapter.Name}»: {error.Message}", true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task RevertDnsDirectAsync()
    {
        if (SelectedAdapter is null) return;
        var adapter = SelectedAdapter.Adapter;
        IsBusy = true;
        try
        {
            await _tuningManager.RevertDnsAsync(adapter);
            _report($"Исходная конфигурация DNS адаптера «{adapter.Name}» восстановлена.", false);
            await RefreshAsync();
        }
        catch (Exception error)
        {
            _report($"Не удалось восстановить DNS для «{adapter.Name}»: {error.Message}", true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ApplyDnsAsync(DnsPresetDefinition? preset)
    {
        if (preset is null || SelectedAdapter is null) return;
        var adapter = SelectedAdapter.Adapter;

        var serverList = preset.DnsServers.Count > 0 ? string.Join(", ", preset.DnsServers) : "DHCP";
        if (!_confirm(
                $"Применить профиль DNS «{preset.Name}» ({serverList}) к адаптеру «{adapter.Name}»?\n\n" +
                $"Преимущество:\n{preset.Benefit}\n\n" +
                "Aurum сохранит исходные адреса DNS для точного отката."))
        {
            return;
        }

        await ApplyDnsDirectAsync(preset);
    }

    private async Task RevertDnsAsync()
    {
        if (SelectedAdapter is null) return;
        var adapter = SelectedAdapter.Adapter;

        if (!_confirm($"Восстановить исходные настройки DNS для адаптера «{adapter.Name}»?"))
        {
            return;
        }

        await RevertDnsDirectAsync();
    }

    private async Task FlushDnsAsync()
    {
        IsBusy = true;
        try
        {
            await _tuningManager.FlushDnsCacheAsync();
            _report("Кэш распознавателя DNS успешно очищен (ipconfig /flushdns).", false);
        }
        catch (Exception error)
        {
            _report($"Не удалось очистить кэш DNS: {error.Message}", true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SetTcpLevelAsync(string level)
    {
        if (!_confirm($"Установить уровень автотюнинга окна TCP: {level}?"))
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _tuningManager.SetTcpAutoTuningLevelAsync(level);
            _report($"Уровень автотюнинга TCP изменён на '{level}'.", false);
            await RefreshAsync();
        }
        catch (Exception error)
        {
            _report($"Не удалось изменить параметры TCP: {error.Message}", true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task OptimizeAllNetworkAsync()
    {
        if (!_confirm("Применить комплексную оптимизацию сети (установка DNS Cloudflare 1.1.1.1, включение TCP Auto-Tuning Normal и сброс кэша DNS)?\n\nВсе параметры сохраняются для возможности отката к DHCP."))
        {
            return;
        }

        IsBusy = true;
        try
        {
            var cloudflarePreset = DnsPresets.FirstOrDefault(p => p.Id == "cloudflare") ?? DnsPresets.FirstOrDefault();
            if (SelectedAdapter is not null && cloudflarePreset is not null)
            {
                await _tuningManager.ApplyDnsPresetAsync(SelectedAdapter.Adapter, cloudflarePreset);
            }
            await _tuningManager.SetTcpAutoTuningLevelAsync("normal");
            await _tuningManager.FlushDnsCacheAsync();
            await RefreshAsync();
            _report("Оптимизация сети успешно применена: Cloudflare DNS + TCP Normal + DNS Cache Flush.", false);
        }
        catch (Exception ex)
        {
            _report($"Ошибка при оптимизации сети: {ex.Message}", true);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
