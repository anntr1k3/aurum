namespace Aurum.Core;

public sealed record DnsPresetDefinition(
    string Id,
    string Name,
    string Description,
    IReadOnlyList<string> DnsServers,
    string Benefit);

public sealed record PersistedNetworkAdapterTuningState(
    string AdapterId,
    string AdapterName,
    IReadOnlyList<string> OriginalDnsServers,
    bool OriginalWasDhcp,
    DateTimeOffset AppliedAtUtc,
    int SchemaVersion = 1);

public interface INetworkTuningStore
{
    Task SetDnsAsync(string adapterName, IReadOnlyList<string> dnsServers, CancellationToken cancellationToken = default);
    Task ResetDnsToDhcpAsync(string adapterName, CancellationToken cancellationToken = default);
    Task FlushDnsCacheAsync(CancellationToken cancellationToken = default);
    Task SetTcpAutoTuningLevelAsync(string level, CancellationToken cancellationToken = default);
    Task SetEcnCapabilityAsync(bool enabled, CancellationToken cancellationToken = default);
}

public interface INetworkTuningStateRepository
{
    Task<PersistedNetworkAdapterTuningState?> GetAsync(string adapterName, CancellationToken cancellationToken = default);
    Task SaveAsync(PersistedNetworkAdapterTuningState state, CancellationToken cancellationToken = default);
    Task RemoveAsync(string adapterName, CancellationToken cancellationToken = default);
}

public static class BuiltInDnsPresets
{
    public static IReadOnlyList<DnsPresetDefinition> All { get; } = Array.AsReadOnly(
        new DnsPresetDefinition[]
    {
        new(
            Id: "cloudflare",
            Name: "Cloudflare DNS",
            Description: "1.1.1.1 · 1.0.0.1",
            DnsServers: new[] { "1.1.1.1", "1.0.0.1" },
            Benefit: "Минимальная задержка и строгая политика не сохранения логов."),

        new(
            Id: "google",
            Name: "Google Public DNS",
            Description: "8.8.8.8 · 8.8.4.4",
            DnsServers: new[] { "8.8.8.8", "8.8.4.4" },
            Benefit: "Глобальная надежность и высокая доступность по всему миру."),

        new(
            Id: "quad9",
            Name: "Quad9 Security",
            Description: "9.9.9.9 · 149.112.112.112",
            DnsServers: new[] { "9.9.9.9", "149.112.112.112" },
            Benefit: "Автоматическая блокировка фишинга, вредоносного ПО и ботнетов."),

        new(
            Id: "adguard",
            Name: "AdGuard DNS",
            Description: "94.140.14.14 · 94.140.15.15",
            DnsServers: new[] { "94.140.14.14", "94.140.15.15" },
            Benefit: "Блокировка баннеров, трекеров и рекламных доменов на уровне DNS."),

        new(
            Id: "dhcp",
            Name: "DHCP (Автоматически)",
            Description: "От роутера / провайдера",
            DnsServers: Array.Empty<string>(),
            Benefit: "Стандартная конфигурация сети без внешних статических DNS.")
    });
}

public sealed class NetworkTuningManager
{
    private readonly INetworkTuningStore _store;
    private readonly INetworkTuningStateRepository _stateRepository;
    private readonly Func<bool> _isAdministrator;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public NetworkTuningManager(
        INetworkTuningStore store,
        INetworkTuningStateRepository stateRepository,
        Func<bool> isAdministrator)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _stateRepository = stateRepository ?? throw new ArgumentNullException(nameof(stateRepository));
        _isAdministrator = isAdministrator ?? throw new ArgumentNullException(nameof(isAdministrator));
    }

    public async Task ApplyDnsPresetAsync(
        NetworkAdapterInfo adapter,
        DnsPresetDefinition preset,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentNullException.ThrowIfNull(preset);
        EnsureAdmin();

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var existing = await _stateRepository.GetAsync(adapter.Name, cancellationToken);
            if (existing is null)
            {
                var originalWasDhcp = adapter.DnsServers.Count == 0;
                var state = new PersistedNetworkAdapterTuningState(
                    adapter.Id,
                    adapter.Name,
                    adapter.DnsServers,
                    originalWasDhcp,
                    DateTimeOffset.UtcNow);
                await _stateRepository.SaveAsync(state, cancellationToken);
            }

            if (preset.DnsServers.Count == 0)
            {
                await _store.ResetDnsToDhcpAsync(adapter.Name, cancellationToken);
            }
            else
            {
                await _store.SetDnsAsync(adapter.Name, preset.DnsServers, cancellationToken);
            }

            await _store.FlushDnsCacheAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RevertDnsAsync(NetworkAdapterInfo adapter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        EnsureAdmin();

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await _stateRepository.GetAsync(adapter.Name, cancellationToken);
            if (state is null)
            {
                await _store.ResetDnsToDhcpAsync(adapter.Name, cancellationToken);
            }
            else if (state.OriginalWasDhcp || state.OriginalDnsServers.Count == 0)
            {
                await _store.ResetDnsToDhcpAsync(adapter.Name, cancellationToken);
                await _stateRepository.RemoveAsync(adapter.Name, cancellationToken);
            }
            else
            {
                await _store.SetDnsAsync(adapter.Name, state.OriginalDnsServers, cancellationToken);
                await _stateRepository.RemoveAsync(adapter.Name, cancellationToken);
            }

            await _store.FlushDnsCacheAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task FlushDnsCacheAsync(CancellationToken cancellationToken = default)
    {
        EnsureAdmin();
        await _store.FlushDnsCacheAsync(cancellationToken);
    }

    public async Task SetTcpAutoTuningLevelAsync(string level, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(level);
        EnsureAdmin();
        await _store.SetTcpAutoTuningLevelAsync(level, cancellationToken);
    }

    public async Task SetEcnCapabilityAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        EnsureAdmin();
        await _store.SetEcnCapabilityAsync(enabled, cancellationToken);
    }

    private void EnsureAdmin()
    {
        if (!_isAdministrator())
        {
            throw new UnauthorizedAccessException("Для изменения сетевых параметров требуются права администратора.");
        }
    }
}
