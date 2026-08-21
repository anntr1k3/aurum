using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace Aurum.Infrastructure.Windows;

public sealed record HardwareInventory(
    string CpuName,
    string CpuDetails,
    string GpuName,
    string GpuDetails,
    ulong TotalMemoryBytes,
    string MemoryDetails,
    string SystemDriveName,
    string SystemDriveDetails,
    double SystemDriveUsedPercent,
    ulong SystemDriveFreeBytes,
    string NetworkName,
    string NetworkDetails,
    string PowerPlanName);

public sealed record HardwareMetrics(
    double CpuUsagePercent,
    double? GpuUsagePercent,
    double MemoryUsagePercent,
    ulong UsedMemoryBytes,
    double DiskUsedPercent,
    long DiskFreeBytes,
    double? DiskBytesPerSecond,
    double NetworkReceiveBytesPerSecond,
    double NetworkSendBytesPerSecond,
    TimeSpan Uptime,
    DateTimeOffset SampledAt);

public sealed class HardwareMonitorService : IDisposable
{
    private readonly object _sync = new();
    private readonly PdhSampler _pdhSampler = new();
    private ulong? _previousIdleTime;
    private ulong? _previousKernelTime;
    private ulong? _previousUserTime;
    private string? _networkInterfaceId;
    private long? _previousNetworkReceived;
    private long? _previousNetworkSent;
    private DateTimeOffset? _previousNetworkSample;
    private bool _disposed;

    public Task<HardwareInventory> CaptureInventoryAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => CaptureInventory(cancellationToken), cancellationToken);

    public Task<HardwareMetrics> SampleAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => Sample(cancellationToken), cancellationToken);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _pdhSampler.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private HardwareInventory CaptureInventory(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        var cpuName = ReadRegistryString(
                          Registry.LocalMachine,
                          @"HARDWARE\DESCRIPTION\System\CentralProcessor\0",
                          "ProcessorNameString")?.Trim() ?? "Процессор Windows";
        var cpuMhz = ReadRegistryDword(
            Registry.LocalMachine,
            @"HARDWARE\DESCRIPTION\System\CentralProcessor\0",
            "~MHz");
        var cpuDetails = $"{Environment.ProcessorCount} логических процессоров · {RuntimeInformation.ProcessArchitecture}" +
                         (cpuMhz is null ? string.Empty : $" · {cpuMhz} МГц");

        var gpuName = GetPrimaryDisplayAdapterName() ?? "Видеоадаптер не определён";
        var gpuDetails = gpuName == "Видеоадаптер не определён"
            ? "Драйвер не предоставил сведения"
            : "Активный графический адаптер Windows";

        var memory = ReadMemoryStatus();
        var memoryDetails = $"Установлено {FormatBytes(memory.TotalPhysical)}";

        var systemDrive = GetSystemDrive();
        var systemDriveName = $"{systemDrive.Name.TrimEnd('\\')} · {systemDrive.DriveFormat}";
        var systemDriveDetails = $"{FormatBytes((ulong)systemDrive.AvailableFreeSpace)} свободно из {FormatBytes((ulong)systemDrive.TotalSize)}";
        var systemDriveUsedPercent = systemDrive.TotalSize == 0
            ? 0
            : (systemDrive.TotalSize - systemDrive.AvailableFreeSpace) * 100d / systemDrive.TotalSize;

        var network = GetPrimaryNetworkInterface();
        _networkInterfaceId = network?.Id;
        var networkName = network?.Description ?? "Активное подключение не найдено";
        var networkDetails = network is null
            ? "Сетевой интерфейс не подключён"
            : $"{network.NetworkInterfaceType} · {FormatBitsPerSecond(network.Speed)}";

        return new HardwareInventory(
            cpuName,
            cpuDetails,
            gpuName,
            gpuDetails,
            memory.TotalPhysical,
            memoryDetails,
            systemDriveName,
            systemDriveDetails,
            Math.Clamp(systemDriveUsedPercent, 0, 100),
            (ulong)Math.Max(0, systemDrive.AvailableFreeSpace),
            networkName,
            networkDetails,
            GetActivePowerPlanName() ?? "Не определён");
    }

    private HardwareMetrics Sample(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        lock (_sync)
        {
            var sampledAt = DateTimeOffset.Now;
            var cpuUsage = ReadCpuUsage();
            var memory = ReadMemoryStatus();
            var usedMemory = memory.TotalPhysical - memory.AvailablePhysical;
            var memoryUsage = memory.TotalPhysical == 0
                ? 0
                : usedMemory * 100d / memory.TotalPhysical;
            var systemDrive = GetSystemDrive();
            var diskUsed = systemDrive.TotalSize == 0
                ? 0
                : (systemDrive.TotalSize - systemDrive.AvailableFreeSpace) * 100d / systemDrive.TotalSize;
            var (receiveRate, sendRate) = ReadNetworkRates(sampledAt);
            var (gpuUsage, diskBytesPerSecond) = _pdhSampler.Sample();

            return new HardwareMetrics(
                Math.Clamp(cpuUsage, 0, 100),
                gpuUsage is null ? null : Math.Clamp(gpuUsage.Value, 0, 100),
                Math.Clamp(memoryUsage, 0, 100),
                usedMemory,
                Math.Clamp(diskUsed, 0, 100),
                systemDrive.AvailableFreeSpace,
                diskBytesPerSecond,
                Math.Max(0, receiveRate),
                Math.Max(0, sendRate),
                TimeSpan.FromMilliseconds(Environment.TickCount64),
                sampledAt);
        }
    }

    private double ReadCpuUsage()
    {
        if (!NativeMethods.GetSystemTimes(out var idleFileTime, out var kernelFileTime, out var userFileTime))
        {
            return 0;
        }

        var idle = idleFileTime.ToUInt64();
        var kernel = kernelFileTime.ToUInt64();
        var user = userFileTime.ToUInt64();

        if (_previousIdleTime is null || _previousKernelTime is null || _previousUserTime is null)
        {
            _previousIdleTime = idle;
            _previousKernelTime = kernel;
            _previousUserTime = user;
            return 0;
        }

        var idleDelta = idle - _previousIdleTime.Value;
        var kernelDelta = kernel - _previousKernelTime.Value;
        var userDelta = user - _previousUserTime.Value;
        var total = kernelDelta + userDelta;

        _previousIdleTime = idle;
        _previousKernelTime = kernel;
        _previousUserTime = user;

        return total == 0 ? 0 : (total - idleDelta) * 100d / total;
    }

    private (double ReceiveRate, double SendRate) ReadNetworkRates(DateTimeOffset sampledAt)
    {
        var network = NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(item => item.Id == _networkInterfaceId)
                      ?? GetPrimaryNetworkInterface();
        if (network is null)
        {
            return (0, 0);
        }

        _networkInterfaceId = network.Id;
        var statistics = network.GetIPv4Statistics();
        var received = statistics.BytesReceived;
        var sent = statistics.BytesSent;

        if (_previousNetworkReceived is null || _previousNetworkSent is null || _previousNetworkSample is null)
        {
            _previousNetworkReceived = received;
            _previousNetworkSent = sent;
            _previousNetworkSample = sampledAt;
            return (0, 0);
        }

        var seconds = (sampledAt - _previousNetworkSample.Value).TotalSeconds;
        var receiveRate = seconds <= 0 ? 0 : (received - _previousNetworkReceived.Value) / seconds;
        var sendRate = seconds <= 0 ? 0 : (sent - _previousNetworkSent.Value) / seconds;

        _previousNetworkReceived = received;
        _previousNetworkSent = sent;
        _previousNetworkSample = sampledAt;
        return (receiveRate, sendRate);
    }

    private static MemoryStatus ReadMemoryStatus()
    {
        var status = new NativeMethods.MemoryStatusEx();
        if (!NativeMethods.GlobalMemoryStatusEx(ref status))
        {
            throw new InvalidOperationException("Windows не вернула сведения об оперативной памяти.");
        }

        return new MemoryStatus(status.TotalPhysical, status.AvailablePhysical);
    }

    private static DriveInfo GetSystemDrive()
    {
        var root = Path.GetPathRoot(Environment.SystemDirectory)
            ?? throw new InvalidOperationException("Не удалось определить системный диск Windows.");
        return new DriveInfo(root);
    }

    private static NetworkInterface? GetPrimaryNetworkInterface() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(static item => item.OperationalStatus == OperationalStatus.Up &&
                                  item.NetworkInterfaceType is not NetworkInterfaceType.Loopback and
                                  not NetworkInterfaceType.Tunnel)
            .OrderByDescending(static item => item.Speed)
            .FirstOrDefault();

    private static string? GetPrimaryDisplayAdapterName()
    {
        var device = new NativeMethods.DisplayDevice();
        for (uint index = 0; NativeMethods.EnumDisplayDevices(null, index, ref device, 0); index++)
        {
            const int attachedToDesktop = 0x1;
            const int mirroringDriver = 0x8;
            if ((device.StateFlags & attachedToDesktop) != 0 && (device.StateFlags & mirroringDriver) == 0 &&
                !string.IsNullOrWhiteSpace(device.DeviceString))
            {
                return device.DeviceString.Trim();
            }

            device = new NativeMethods.DisplayDevice();
        }

        return null;
    }

    private static string? GetActivePowerPlanName()
    {
        if (NativeMethods.PowerGetActiveScheme(IntPtr.Zero, out var guidPointer) != 0 || guidPointer == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var schemeGuid = Marshal.PtrToStructure<Guid>(guidPointer);
            uint bufferSize = 0;
            _ = NativeMethods.PowerReadFriendlyName(
                IntPtr.Zero,
                ref schemeGuid,
                IntPtr.Zero,
                IntPtr.Zero,
                IntPtr.Zero,
                ref bufferSize);
            if (bufferSize == 0)
            {
                return null;
            }

            var buffer = Marshal.AllocHGlobal((int)bufferSize);
            try
            {
                return NativeMethods.PowerReadFriendlyName(
                           IntPtr.Zero,
                           ref schemeGuid,
                           IntPtr.Zero,
                           IntPtr.Zero,
                           buffer,
                           ref bufferSize) == 0
                    ? Marshal.PtrToStringUni(buffer)?.TrimEnd('\0')
                    : null;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        finally
        {
            _ = NativeMethods.LocalFree(guidPointer);
        }
    }

    private static string? ReadRegistryString(RegistryKey hive, string subKey, string valueName)
    {
        using var key = hive.OpenSubKey(subKey, writable: false);
        return key?.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
    }

    private static int? ReadRegistryDword(RegistryKey hive, string subKey, string valueName)
    {
        using var key = hive.OpenSubKey(subKey, writable: false);
        return key?.GetValue(valueName) is int value ? value : null;
    }

    private static string FormatBytes(ulong bytes)
    {
        string[] units = ["Б", "КБ", "МБ", "ГБ", "ТБ"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.##} {units[unit]}";
    }

    private static string FormatBitsPerSecond(long bitsPerSecond)
    {
        if (bitsPerSecond <= 0)
        {
            return "скорость не определена";
        }

        return bitsPerSecond >= 1_000_000_000
            ? $"{bitsPerSecond / 1_000_000_000d:0.##} Гбит/с"
            : $"{bitsPerSecond / 1_000_000d:0.##} Мбит/с";
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private sealed record MemoryStatus(ulong TotalPhysical, ulong AvailablePhysical);
}

internal sealed class PdhSampler : IDisposable
{
    private const uint PdhFormatDouble = 0x00000200;
    private const uint PdhMoreData = 0x800007D2;
    private readonly List<IntPtr> _gpuCounters = [];
    private IntPtr _query;
    private IntPtr _diskBytesCounter;
    private bool _hasCollected;

    public PdhSampler()
    {
        try
        {
            if (NativeMethods.PdhOpenQuery(null, IntPtr.Zero, out _query) != 0)
            {
                _query = IntPtr.Zero;
                return;
            }

            _ = NativeMethods.PdhAddEnglishCounter(
                _query,
                @"\PhysicalDisk(_Total)\Disk Bytes/sec",
                IntPtr.Zero,
                out _diskBytesCounter);
            AddGpuCounters();
        }
        catch
        {
            Dispose();
        }
    }

    public (double? GpuUsage, double? DiskBytesPerSecond) Sample()
    {
        if (_query == IntPtr.Zero || NativeMethods.PdhCollectQueryData(_query) != 0)
        {
            return (null, null);
        }

        if (!_hasCollected)
        {
            _hasCollected = true;
            return (null, null);
        }

        var gpuValues = _gpuCounters.Select(ReadCounter).Where(static value => value is not null).Select(static value => value!.Value).ToArray();
        double? gpuUsage = gpuValues.Length == 0 ? null : gpuValues.Sum();
        var diskBytes = ReadCounter(_diskBytesCounter);
        return (gpuUsage, diskBytes);
    }

    public void Dispose()
    {
        if (_query != IntPtr.Zero)
        {
            _ = NativeMethods.PdhCloseQuery(_query);
            _query = IntPtr.Zero;
        }
        _gpuCounters.Clear();
        _diskBytesCounter = IntPtr.Zero;
    }

    private void AddGpuCounters()
    {
        uint requiredLength = 0;
        var status = NativeMethods.PdhExpandWildCardPath(
            null,
            @"\GPU Engine(*)\Utilization Percentage",
            null,
            ref requiredLength,
            0);
        if (status != PdhMoreData || requiredLength == 0)
        {
            return;
        }

        var pathsBuffer = new char[requiredLength];
        if (NativeMethods.PdhExpandWildCardPath(
                null,
                @"\GPU Engine(*)\Utilization Percentage",
                pathsBuffer,
                ref requiredLength,
                0) != 0)
        {
            return;
        }

        var paths = new string(pathsBuffer)
            .Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Where(static path => path.Contains("engtype_3D", StringComparison.OrdinalIgnoreCase));
        foreach (var path in paths)
        {
            if (NativeMethods.PdhAddEnglishCounter(_query, path, IntPtr.Zero, out var counter) == 0)
            {
                _gpuCounters.Add(counter);
            }
        }
    }

    private static double? ReadCounter(IntPtr counter)
    {
        if (counter == IntPtr.Zero ||
            NativeMethods.PdhGetFormattedCounterValue(counter, PdhFormatDouble, out _, out var value) != 0 ||
            value.Status != 0 || double.IsNaN(value.DoubleValue) || double.IsInfinity(value.DoubleValue))
        {
            return null;
        }

        return Math.Max(0, value.DoubleValue);
    }
}

internal static class NativeMethods
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct FileTime
    {
        public uint Low;
        public uint High;

        public readonly ulong ToUInt64() => ((ulong)High << 32) | Low;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct DisplayDevice
    {
        public int Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;

        public DisplayDevice()
        {
            Size = Marshal.SizeOf<DisplayDevice>();
            DeviceName = string.Empty;
            DeviceString = string.Empty;
            DeviceId = string.Empty;
            DeviceKey = string.Empty;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    internal struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;

        public MemoryStatusEx()
        {
            Length = (uint)Marshal.SizeOf<MemoryStatusEx>();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PdhFormattedCounterValue
    {
        public uint Status;
        public double DoubleValue;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetSystemTimes(out FileTime idleTime, out FileTime kernelTime, out FileTime userTime);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumDisplayDevices(string? device, uint deviceNumber, ref DisplayDevice displayDevice, uint flags);

    [DllImport("powrprof.dll")]
    internal static extern uint PowerGetActiveScheme(IntPtr userRootPowerKey, out IntPtr activePolicyGuid);

    [DllImport("powrprof.dll", CharSet = CharSet.Unicode)]
    internal static extern uint PowerReadFriendlyName(
        IntPtr rootPowerKey,
        ref Guid schemeGuid,
        IntPtr subgroupOfPowerSettingsGuid,
        IntPtr powerSettingGuid,
        IntPtr buffer,
        ref uint bufferSize);

    [DllImport("kernel32.dll")]
    internal static extern IntPtr LocalFree(IntPtr memory);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    internal static extern uint PdhOpenQuery(string? dataSource, IntPtr userData, out IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    internal static extern uint PdhAddEnglishCounter(IntPtr query, string fullCounterPath, IntPtr userData, out IntPtr counter);

    [DllImport("pdh.dll")]
    internal static extern uint PdhCollectQueryData(IntPtr query);

    [DllImport("pdh.dll")]
    internal static extern uint PdhGetFormattedCounterValue(
        IntPtr counter,
        uint format,
        out uint counterType,
        out PdhFormattedCounterValue value);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    internal static extern uint PdhExpandWildCardPath(
        string? dataSource,
        string wildcardPath,
        [Out] char[]? expandedPathList,
        ref uint pathListLength,
        uint flags);

    [DllImport("pdh.dll")]
    internal static extern uint PdhCloseQuery(IntPtr query);
}
