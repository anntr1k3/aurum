using System.ComponentModel;
using System.Runtime.InteropServices;
using Aurum.Core;
using Microsoft.Win32.SafeHandles;

namespace Aurum.Infrastructure.Windows;

public sealed class WindowsServiceInventory
{
    public Task<IReadOnlyList<ServiceAnalysisItem>> CaptureAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => Capture(cancellationToken), cancellationToken);

    private static IReadOnlyList<ServiceAnalysisItem> Capture(CancellationToken cancellationToken)
    {
        using var manager = ServiceNativeMethods.OpenSCManager(null, null, 0x0004);
        if (manager.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), "Не удалось открыть Service Control Manager.");

        uint resume = 0;
        _ = ServiceNativeMethods.EnumServicesStatusEx(manager, 0, 0x30, 0x03, IntPtr.Zero, 0,
            out var bytesNeeded, out _, ref resume, null);
        if (bytesNeeded == 0) return [];
        var buffer = Marshal.AllocHGlobal((int)bytesNeeded);
        try
        {
            resume = 0;
            if (!ServiceNativeMethods.EnumServicesStatusEx(manager, 0, 0x30, 0x03, buffer, bytesNeeded,
                    out _, out var returned, ref resume, null))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Не удалось перечислить службы Windows.");

            var definitions = new List<ServiceDefinition>((int)returned);
            var size = Marshal.SizeOf<EnumServiceStatusProcess>();
            for (var index = 0; index < returned; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var status = Marshal.PtrToStructure<EnumServiceStatusProcess>(buffer + (index * size));
                var name = Marshal.PtrToStringUni(status.ServiceName) ?? string.Empty;
                var displayName = Marshal.PtrToStringUni(status.DisplayName) ?? name;
                definitions.Add(ReadConfiguration(manager, name, displayName, status.Status));
            }

            return ServiceAnalyzer.Analyze(definitions);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static ServiceDefinition ReadConfiguration(
        SafeServiceHandle manager, string name, string displayName, ServiceStatusProcess status)
    {
        using var service = ServiceNativeMethods.OpenService(manager, name, 0x0001);
        if (service.IsInvalid)
            return new ServiceDefinition(name, displayName, string.Empty, MapState(status.CurrentState),
                ServiceStartMode.Unknown, false, status.ProcessId, []);

        _ = ServiceNativeMethods.QueryServiceConfig(service, IntPtr.Zero, 0, out var needed);
        var buffer = Marshal.AllocHGlobal((int)needed);
        try
        {
            if (!ServiceNativeMethods.QueryServiceConfig(service, buffer, needed, out _))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            var config = Marshal.PtrToStructure<QueryServiceConfig>(buffer);
            return new ServiceDefinition(
                name, displayName, ReadDescription(service), MapState(status.CurrentState),
                MapStartMode(config.StartType), ReadDelayedStart(service), status.ProcessId,
                ReadMultiString(config.Dependencies));
        }
        catch (Win32Exception)
        {
            return new ServiceDefinition(name, displayName, string.Empty, MapState(status.CurrentState),
                ServiceStartMode.Unknown, false, status.ProcessId, []);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static string ReadDescription(SafeServiceHandle service)
    {
        _ = ServiceNativeMethods.QueryServiceConfig2(service, 1, IntPtr.Zero, 0, out var needed);
        if (needed == 0) return string.Empty;
        var buffer = Marshal.AllocHGlobal((int)needed);
        try
        {
            if (!ServiceNativeMethods.QueryServiceConfig2(service, 1, buffer, needed, out _)) return string.Empty;
            var pointer = Marshal.ReadIntPtr(buffer);
            return pointer == IntPtr.Zero ? string.Empty : Marshal.PtrToStringUni(pointer) ?? string.Empty;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static bool ReadDelayedStart(SafeServiceHandle service)
    {
        var buffer = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            return ServiceNativeMethods.QueryServiceConfig2(service, 3, buffer, sizeof(int), out _) && Marshal.ReadInt32(buffer) != 0;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static IReadOnlyList<string> ReadMultiString(IntPtr pointer)
    {
        if (pointer == IntPtr.Zero) return [];
        var values = new List<string>();
        var offset = 0;
        while (true)
        {
            var value = Marshal.PtrToStringUni(pointer + offset);
            if (string.IsNullOrEmpty(value)) break;
            values.Add(value.TrimStart('+'));
            offset += (value.Length + 1) * sizeof(char);
        }
        return values;
    }

    private static ServiceRunState MapState(uint value) => value switch
    {
        1 => ServiceRunState.Stopped, 2 => ServiceRunState.StartPending, 3 => ServiceRunState.StopPending,
        4 => ServiceRunState.Running, 5 => ServiceRunState.ContinuePending, 6 => ServiceRunState.PausePending,
        7 => ServiceRunState.Paused, _ => ServiceRunState.Unknown,
    };

    private static ServiceStartMode MapStartMode(uint value) => value switch
    {
        0 => ServiceStartMode.Boot, 1 => ServiceStartMode.System, 2 => ServiceStartMode.Automatic,
        3 => ServiceStartMode.Manual, 4 => ServiceStartMode.Disabled, _ => ServiceStartMode.Unknown,
    };
}

[StructLayout(LayoutKind.Sequential)]
internal struct EnumServiceStatusProcess
{
    public IntPtr ServiceName;
    public IntPtr DisplayName;
    public ServiceStatusProcess Status;
}

[StructLayout(LayoutKind.Sequential)]
internal struct ServiceStatusProcess
{
    public uint ServiceType, CurrentState, ControlsAccepted, Win32ExitCode, ServiceSpecificExitCode,
        CheckPoint, WaitHint, ProcessId, ServiceFlags;
}

[StructLayout(LayoutKind.Sequential)]
internal struct QueryServiceConfig
{
    public uint ServiceType, StartType, ErrorControl;
    public IntPtr BinaryPathName, LoadOrderGroup;
    public uint TagId;
    public IntPtr Dependencies, ServiceStartName, DisplayName;
}

internal sealed class SafeServiceHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    private SafeServiceHandle() : base(true) { }
    protected override bool ReleaseHandle() => ServiceNativeMethods.CloseServiceHandle(handle);
}

internal static class ServiceNativeMethods
{
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern SafeServiceHandle OpenSCManager(string? machine, string? database, uint access);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern SafeServiceHandle OpenService(SafeServiceHandle manager, string name, uint access);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumServicesStatusEx(SafeServiceHandle manager, int level, uint type, uint state, IntPtr services, uint size, out uint needed, out uint returned, ref uint resume, string? group);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool QueryServiceConfig(SafeServiceHandle service, IntPtr config, uint size, out uint needed);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool QueryServiceConfig2(SafeServiceHandle service, uint level, IntPtr buffer, uint size, out uint needed);
    [DllImport("advapi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool CloseServiceHandle(IntPtr handle);
}
