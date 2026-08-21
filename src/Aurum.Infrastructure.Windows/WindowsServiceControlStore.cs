using System.ComponentModel;
using System.Runtime.InteropServices;
using Aurum.Core;

namespace Aurum.Infrastructure.Windows;

public sealed class WindowsServiceControlStore : IServiceControlStore
{
    private const uint SC_MANAGER_ALL_ACCESS = 0xF003F;
    private const uint SC_MANAGER_CONNECT = 0x0001;
    private const uint SC_MANAGER_ENUMERATE_SERVICE = 0x0004;

    private const uint SERVICE_QUERY_CONFIG = 0x0001;
    private const uint SERVICE_CHANGE_CONFIG = 0x0002;
    private const uint SERVICE_QUERY_STATUS = 0x0004;
    private const uint SERVICE_START = 0x0010;
    private const uint SERVICE_STOP = 0x0020;

    private const uint SERVICE_NO_CHANGE = 0xFFFFFFFF;
    private const uint SERVICE_CONTROL_STOP = 0x00000001;

    public Task<ServiceDefinition?> GetServiceAsync(string serviceName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        using var manager = ServiceNativeMethods.OpenSCManager(null, null, SC_MANAGER_CONNECT | SC_MANAGER_ENUMERATE_SERVICE);
        if (manager.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Не удалось открыть Service Control Manager.");
        }

        using var service = ServiceNativeMethods.OpenService(manager, serviceName, SERVICE_QUERY_CONFIG | SERVICE_QUERY_STATUS);
        if (service.IsInvalid)
        {
            return Task.FromResult<ServiceDefinition?>(null);
        }

        _ = ServiceNativeMethods.QueryServiceConfig(service, IntPtr.Zero, 0, out var needed);
        var buffer = Marshal.AllocHGlobal((int)needed);
        try
        {
            if (!ServiceNativeMethods.QueryServiceConfig(service, buffer, needed, out _))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            var config = Marshal.PtrToStructure<QueryServiceConfig>(buffer);
            var status = new ServiceStatusProcess();
            _ = ServiceControlNativeMethods.QueryServiceStatusEx(service, 0, ref status, (uint)Marshal.SizeOf<ServiceStatusProcess>(), out _);

            var definition = new ServiceDefinition(
                serviceName,
                Marshal.PtrToStringUni(config.DisplayName) ?? serviceName,
                ReadDescription(service),
                MapState(status.CurrentState),
                MapStartMode(config.StartType),
                ReadDelayedStart(service),
                status.ProcessId,
                ReadMultiString(config.Dependencies));

            return Task.FromResult<ServiceDefinition?>(definition);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public Task ChangeStartModeAsync(string serviceName, ServiceStartMode startMode, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        using var manager = ServiceNativeMethods.OpenSCManager(null, null, SC_MANAGER_ALL_ACCESS);
        if (manager.IsInvalid)
        {
            throw new UnauthorizedAccessException("Для изменения настроек служб требуются права администратора.");
        }

        using var service = ServiceNativeMethods.OpenService(manager, serviceName, SERVICE_CHANGE_CONFIG);
        if (service.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Не удалось открыть службу '{serviceName}' для изменения конфигурации.");
        }

        var nativeStartType = startMode switch
        {
            ServiceStartMode.Boot => 0u,
            ServiceStartMode.System => 1u,
            ServiceStartMode.Automatic => 2u,
            ServiceStartMode.Manual => 3u,
            ServiceStartMode.Disabled => 4u,
            _ => throw new ArgumentOutOfRangeException(nameof(startMode), startMode, null)
        };

        if (!ServiceControlNativeMethods.ChangeServiceConfig(
                service,
                SERVICE_NO_CHANGE,
                nativeStartType,
                SERVICE_NO_CHANGE,
                null,
                null,
                IntPtr.Zero,
                null,
                null,
                null,
                null))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Не удалось изменить тип запуска службы '{serviceName}'.");
        }

        return Task.CompletedTask;
    }

    public async Task StopServiceAsync(string serviceName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        using var manager = ServiceNativeMethods.OpenSCManager(null, null, SC_MANAGER_ALL_ACCESS);
        if (manager.IsInvalid)
        {
            throw new UnauthorizedAccessException("Для остановки служб требуются права администратора.");
        }

        using var service = ServiceNativeMethods.OpenService(manager, serviceName, SERVICE_STOP | SERVICE_QUERY_STATUS);
        if (service.IsInvalid)
        {
            return;
        }

        var status = new ServiceStatus();
        if (ServiceControlNativeMethods.ControlService(service, SERVICE_CONTROL_STOP, ref status))
        {
            var procStatus = new ServiceStatusProcess();
            var bufSize = (uint)Marshal.SizeOf<ServiceStatusProcess>();
            // Ожидание до 5 секунд, пока служба действительно перейдёт в состояние Stopped (1)
            for (var i = 0; i < 50; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (ServiceControlNativeMethods.QueryServiceStatusEx(service, 0, ref procStatus, bufSize, out _) && procStatus.CurrentState == 1)
                {
                    break;
                }
                await Task.Delay(100, cancellationToken);
            }
        }
    }

    public async Task StartServiceAsync(string serviceName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        using var manager = ServiceNativeMethods.OpenSCManager(null, null, SC_MANAGER_ALL_ACCESS);
        if (manager.IsInvalid)
        {
            throw new UnauthorizedAccessException("Для запуска служб требуются права администратора.");
        }

        using var service = ServiceNativeMethods.OpenService(manager, serviceName, SERVICE_START | SERVICE_QUERY_STATUS);
        if (service.IsInvalid)
        {
            return;
        }

        if (ServiceControlNativeMethods.StartService(service, 0, null))
        {
            var procStatus = new ServiceStatusProcess();
            var bufSize = (uint)Marshal.SizeOf<ServiceStatusProcess>();
            // Ожидание до 5 секунд, пока служба действительно перейдёт в состояние Running (4)
            for (var i = 0; i < 50; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (ServiceControlNativeMethods.QueryServiceStatusEx(service, 0, ref procStatus, bufSize, out _) && procStatus.CurrentState == 4)
                {
                    break;
                }
                await Task.Delay(100, cancellationToken);
            }
        }
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
        1 => ServiceRunState.Stopped,
        2 => ServiceRunState.StartPending,
        3 => ServiceRunState.StopPending,
        4 => ServiceRunState.Running,
        5 => ServiceRunState.ContinuePending,
        6 => ServiceRunState.PausePending,
        7 => ServiceRunState.Paused,
        _ => ServiceRunState.Unknown,
    };

    private static ServiceStartMode MapStartMode(uint value) => value switch
    {
        0 => ServiceStartMode.Boot,
        1 => ServiceStartMode.System,
        2 => ServiceStartMode.Automatic,
        3 => ServiceStartMode.Manual,
        4 => ServiceStartMode.Disabled,
        _ => ServiceStartMode.Unknown,
    };
}

[StructLayout(LayoutKind.Sequential)]
internal struct ServiceStatus
{
    public uint ServiceType, CurrentState, ControlsAccepted, Win32ExitCode, ServiceSpecificExitCode,
        CheckPoint, WaitHint;
}

internal static class ServiceControlNativeMethods
{
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ChangeServiceConfig(
        SafeServiceHandle service,
        uint serviceType,
        uint startType,
        uint errorControl,
        string? binaryPathName,
        string? loadOrderGroup,
        IntPtr tagId,
        string? dependencies,
        string? serviceStartName,
        string? password,
        string? displayName);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ControlService(
        SafeServiceHandle service,
        uint control,
        ref ServiceStatus status);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool StartService(
        SafeServiceHandle service,
        uint numServiceArgs,
        string[]? serviceArgVectors);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool QueryServiceStatusEx(
        SafeServiceHandle service,
        int infoLevel,
        ref ServiceStatusProcess buffer,
        uint bufSize,
        out uint bytesNeeded);
}
