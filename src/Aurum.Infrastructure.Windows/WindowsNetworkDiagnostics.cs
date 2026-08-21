using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Aurum.Core;

namespace Aurum.Infrastructure.Windows;

public sealed class WindowsNetworkInventoryStore : INetworkInventoryStore
{
    public Task<NetworkSnapshot> CaptureAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => Capture(cancellationToken), cancellationToken);

    private static NetworkSnapshot Capture(CancellationToken cancellationToken)
    {
        var adapters = new List<NetworkAdapterInfo>();
        foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;

            try
            {
                var properties = networkInterface.GetIPProperties();
                var ipv4 = properties.UnicastAddresses
                    .Where(static address => address.Address.AddressFamily == AddressFamily.InterNetwork)
                    .Select(static address => address.Address.ToString()).ToArray();
                var ipv6 = properties.UnicastAddresses
                    .Where(static address => address.Address.AddressFamily == AddressFamily.InterNetworkV6)
                    .Select(static address => address.Address.ToString()).ToArray();
                var gateways = properties.GatewayAddresses
                    .Select(static gateway => gateway.Address.ToString())
                    .Where(static address => address is not "0.0.0.0" and not "::")
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                var dns = properties.DnsAddresses.Select(static address => address.ToString())
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                int? mtu = null;
                try { mtu = properties.GetIPv4Properties()?.Mtu; } catch (NetworkInformationException) { }

                adapters.Add(new NetworkAdapterInfo(
                    networkInterface.Id,
                    networkInterface.Name,
                    networkInterface.Description,
                    networkInterface.NetworkInterfaceType.ToString(),
                    networkInterface.OperationalStatus.ToString(),
                    Math.Max(0, networkInterface.Speed),
                    mtu,
                    FormatPhysicalAddress(networkInterface.GetPhysicalAddress()),
                    ipv4,
                    ipv6,
                    gateways,
                    dns,
                    networkInterface.OperationalStatus == OperationalStatus.Up && gateways.Length != 0));
            }
            catch (NetworkInformationException)
            {
                // A device can disappear while Windows is enumerating it. The next refresh will retry it.
            }
        }

        var ordered = adapters
            .OrderByDescending(static adapter => adapter.IsPrimary)
            .ThenByDescending(static adapter => adapter.OperationalStatus == nameof(OperationalStatus.Up))
            .ThenByDescending(static adapter => adapter.SpeedBitsPerSecond)
            .ThenBy(static adapter => adapter.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        var (settings, status) = CaptureTcpSettings(cancellationToken);
        return new NetworkSnapshot(ordered, settings, status, DateTimeOffset.Now);
    }

    private static (IReadOnlyList<NetworkSettingInfo> Settings, string Status) CaptureTcpSettings(
        CancellationToken cancellationToken)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = Path.Combine(Environment.SystemDirectory, "netsh.exe"),
                    Arguments = "interface tcp show global",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                },
            };
            process.Start();
            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            process.WaitForExit(5000);
            if (!process.HasExited)
            {
                process.Kill(true);
                return ([], "netsh не завершил чтение параметров за 5 секунд.");
            }

            var output = outputTask.GetAwaiter().GetResult();
            var error = errorTask.GetAwaiter().GetResult();
            var settings = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(static line => line.Trim())
                .Where(static line => line.Contains(':'))
                .Select(static line =>
                {
                    var separator = line.IndexOf(':');
                    return new NetworkSettingInfo(line[..separator].Trim(), line[(separator + 1)..].Trim());
                })
                .Where(static setting => setting.Name.Length != 0 && setting.Value.Length != 0)
                .ToArray();
            return settings.Length == 0
                ? ([], string.IsNullOrWhiteSpace(error) ? "Windows не вернула глобальные TCP-параметры." : error.Trim())
                : (settings, $"Прочитано параметров: {settings.Length}. Значения не изменялись.");
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            return ([], $"TCP-параметры недоступны: {error.Message}");
        }
    }

    private static string FormatPhysicalAddress(PhysicalAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes.Length == 0 ? "—" : string.Join("-", bytes.Select(static value => value.ToString("X2")));
    }
}

public sealed class WindowsNetworkProbe : INetworkProbe
{
    public async Task<NetworkProbeSample> SendAsync(
        string target,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(target, timeout, cancellationToken: cancellationToken);
            return reply.Status == IPStatus.Success
                ? new NetworkProbeSample(true, reply.RoundtripTime, "Успешно")
                : new NetworkProbeSample(false, null, TranslateStatus(reply.Status));
        }
        catch (PingException error)
        {
            return new NetworkProbeSample(false, null, error.InnerException?.Message ?? error.Message);
        }
    }

    private static string TranslateStatus(IPStatus status) => status switch
    {
        IPStatus.TimedOut => "Тайм-аут",
        IPStatus.DestinationHostUnreachable => "Узел недоступен",
        IPStatus.DestinationNetworkUnreachable => "Сеть недоступна",
        IPStatus.BadDestination => "Некорректный адрес",
        _ => status.ToString(),
    };
}
