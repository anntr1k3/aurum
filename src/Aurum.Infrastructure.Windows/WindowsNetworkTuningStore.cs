using System.Diagnostics;
using Aurum.Core;

namespace Aurum.Infrastructure.Windows;

public sealed class WindowsNetworkTuningStore : INetworkTuningStore
{
    public async Task SetDnsAsync(
        string adapterName,
        IReadOnlyList<string> dnsServers,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(adapterName);
        if (dnsServers.Count == 0)
        {
            await ResetDnsToDhcpAsync(adapterName, cancellationToken);
            return;
        }

        var primary = dnsServers[0];
        await RunNetshAsync(["interface", "ipv4", "set", "dnsservers", $"name={adapterName}", "source=static", $"address={primary}", "register=none"], cancellationToken);

        for (var i = 1; i < dnsServers.Count; i++)
        {
            var secondary = dnsServers[i];
            await RunNetshAsync(["interface", "ipv4", "add", "dnsservers", $"name={adapterName}", $"address={secondary}", $"index={i + 1}"], cancellationToken);
        }
    }

    public async Task ResetDnsToDhcpAsync(
        string adapterName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(adapterName);
        await RunNetshAsync(["interface", "ipv4", "set", "dnsservers", $"name={adapterName}", "source=dhcp"], cancellationToken);
    }

    public async Task FlushDnsCacheAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var ipconfigPath = Path.Combine(Environment.SystemDirectory, "ipconfig.exe");
        var startInfo = new ProcessStartInfo(ipconfigPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("/flushdns");

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Не удалось запустить ipconfig.");

        await process.WaitForExitAsync(cancellationToken);
    }

    public async Task SetTcpAutoTuningLevelAsync(
        string level,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(level);
        await RunNetshAsync(["interface", "tcp", "set", "global", $"autotuninglevel={level}"], cancellationToken);
    }

    public async Task SetEcnCapabilityAsync(
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await RunNetshAsync(["interface", "tcp", "set", "global", $"ecncapability={(enabled ? "enabled" : "disabled")}"], cancellationToken);
    }

    private static async Task RunNetshAsync(IEnumerable<string> arguments, CancellationToken cancellationToken)
    {
        var netshPath = Path.Combine(Environment.SystemDirectory, "netsh.exe");
        var startInfo = new ProcessStartInfo(netshPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var arg in arguments)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Не удалось запустить netsh.");

        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0)
        {
            var err = await process.StandardError.ReadToEndAsync(cancellationToken);
            var outText = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            var msg = string.IsNullOrWhiteSpace(err) ? outText : err;
            throw new InvalidOperationException($"netsh завершился с ошибкой: {msg.Trim()}");
        }
    }
}
