using System.Net;

namespace Aurum.Core;

public sealed record NetworkAdapterInfo(
    string Id,
    string Name,
    string Description,
    string InterfaceType,
    string OperationalStatus,
    long SpeedBitsPerSecond,
    int? Mtu,
    string PhysicalAddress,
    IReadOnlyList<string> IPv4Addresses,
    IReadOnlyList<string> IPv6Addresses,
    IReadOnlyList<string> Gateways,
    IReadOnlyList<string> DnsServers,
    bool IsPrimary);

public sealed record NetworkSettingInfo(string Name, string Value);

public sealed record NetworkSnapshot(
    IReadOnlyList<NetworkAdapterInfo> Adapters,
    IReadOnlyList<NetworkSettingInfo> TcpSettings,
    string TcpStatus,
    DateTimeOffset CapturedAt);

public sealed record NetworkProbeSample(bool Succeeded, long? RoundtripMilliseconds, string Status);

public sealed record NetworkProbeResult(
    string Target,
    int Sent,
    int Received,
    double LossPercent,
    double? MinimumMilliseconds,
    double? AverageMilliseconds,
    double? MaximumMilliseconds,
    IReadOnlyList<NetworkProbeSample> Samples,
    DateTimeOffset CompletedAt);

public interface INetworkInventoryStore
{
    Task<NetworkSnapshot> CaptureAsync(CancellationToken cancellationToken = default);
}

public interface INetworkProbe
{
    Task<NetworkProbeSample> SendAsync(
        string target,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}

public sealed class NetworkDiagnosticsManager
{
    private readonly INetworkInventoryStore _inventory;
    private readonly INetworkProbe _probe;

    public NetworkDiagnosticsManager(INetworkInventoryStore inventory, INetworkProbe probe)
    {
        _inventory = inventory;
        _probe = probe;
    }

    public Task<NetworkSnapshot> CaptureAsync(CancellationToken cancellationToken = default) =>
        _inventory.CaptureAsync(cancellationToken);

    public async Task<NetworkProbeResult> ProbeAsync(
        string target,
        int count = 4,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        target = ValidateTarget(target);
        if (count is < 1 or > 20)
            throw new ArgumentOutOfRangeException(nameof(count), "Количество запросов должно быть от 1 до 20.");

        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(1.5);
        if (effectiveTimeout <= TimeSpan.Zero || effectiveTimeout > TimeSpan.FromSeconds(10))
            throw new ArgumentOutOfRangeException(nameof(timeout), "Тайм-аут должен быть от 1 мс до 10 секунд.");

        var samples = new List<NetworkProbeSample>(count);
        for (var index = 0; index < count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            samples.Add(await _probe.SendAsync(target, effectiveTimeout, cancellationToken));
        }

        var successful = samples
            .Where(static sample => sample.Succeeded && sample.RoundtripMilliseconds is not null)
            .Select(static sample => (double)sample.RoundtripMilliseconds!.Value)
            .ToArray();

        return new NetworkProbeResult(
            target,
            count,
            successful.Length,
            (count - successful.Length) * 100d / count,
            successful.Length == 0 ? null : successful.Min(),
            successful.Length == 0 ? null : successful.Average(),
            successful.Length == 0 ? null : successful.Max(),
            samples,
            DateTimeOffset.Now);
    }

    public static string ValidateTarget(string? target)
    {
        var value = target?.Trim() ?? string.Empty;
        if (value.Length is 0 or > 253)
            throw new ArgumentException("Укажите IPv4, IPv6 или DNS-имя длиной до 253 символов.", nameof(target));
        if (value.Any(char.IsControl) || value.Any(char.IsWhiteSpace))
            throw new ArgumentException("Адрес не должен содержать пробелы или управляющие символы.", nameof(target));

        if (IPAddress.TryParse(value, out _))
            return value;

        if (Uri.CheckHostName(value) != UriHostNameType.Dns ||
            value.Split('.').Any(static label => label.Length is 0 or > 63 ||
                label[0] == '-' || label[^1] == '-' ||
                label.Any(static character => !char.IsLetterOrDigit(character) && character != '-')))
        {
            throw new ArgumentException("Укажите корректный IP-адрес или DNS-имя.", nameof(target));
        }

        return value;
    }
}
