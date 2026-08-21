using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Aurum.Core;
using Microsoft.Win32.SafeHandles;
using Microsoft.Win32;

namespace Aurum.Infrastructure.Windows;

public sealed partial class WindowsStorageInventoryStore : IStorageInventoryStore
{
    public async Task<IReadOnlyList<StorageVolumeInfo>> CaptureAsync(
        CancellationToken cancellationToken = default)
    {
        var deleteNotifications = await ReadDeleteNotificationStateAsync(cancellationToken);
        var systemRoot = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
        var volumes = new List<StorageVolumeInfo>();

        foreach (var drive in DriveInfo.GetDrives()
                     .Where(static drive => drive.DriveType is DriveType.Fixed or DriveType.Removable))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (!drive.IsReady)
                {
                    continue;
                }

                var device = ReadDeviceInfo(drive.Name, drive.DriveType);
                deleteNotifications.TryGetValue(drive.DriveFormat, out var deleteNotifyEnabled);
                volumes.Add(new StorageVolumeInfo(
                    drive.Name,
                    string.IsNullOrWhiteSpace(drive.VolumeLabel) ? "Без метки" : drive.VolumeLabel,
                    drive.DriveFormat,
                    device.Model,
                    device.BusType,
                    device.MediaKind,
                    drive.TotalSize,
                    drive.AvailableFreeSpace,
                    device.DeviceNumber,
                    device.TrimSupported,
                    deleteNotifyEnabled,
                    string.Equals(drive.Name, systemRoot, StringComparison.OrdinalIgnoreCase)));
            }
            catch (IOException)
            {
                // A removable or encrypted volume may disappear during enumeration.
            }
            catch (UnauthorizedAccessException)
            {
                // Keep inaccessible volumes outside the actionable inventory.
            }
        }

        return volumes
            .OrderByDescending(static volume => volume.IsSystem)
            .ThenBy(static volume => volume.RootPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static StorageDeviceInfo ReadDeviceInfo(string rootPath, DriveType driveType)
    {
        var volumePath = $@"\\.\{rootPath.TrimEnd('\\')}";
        using var handle = StorageNativeMethods.CreateFile(
            volumePath,
            0,
            StorageNativeMethods.FileShareRead | StorageNativeMethods.FileShareWrite,
            IntPtr.Zero,
            StorageNativeMethods.OpenExisting,
            0,
            IntPtr.Zero);
        if (handle.IsInvalid)
        {
            return StorageDeviceInfo.Unknown(driveType);
        }

        var deviceNumber = QueryDeviceNumber(handle);
        var descriptor = QueryProperty(handle, 0, 1024);
        var incursSeekPenalty = ReadBooleanDescriptor(handle, 7);
        var trimSupported = ReadBooleanDescriptor(handle, 8);
        if (deviceNumber is not null &&
            (descriptor is null || incursSeekPenalty is null || trimSupported is null))
        {
            using var physicalHandle = StorageNativeMethods.CreateFile(
                $@"\\.\PhysicalDrive{deviceNumber.Value}",
                0,
                StorageNativeMethods.FileShareRead | StorageNativeMethods.FileShareWrite,
                IntPtr.Zero,
                StorageNativeMethods.OpenExisting,
                0,
                IntPtr.Zero);
            if (!physicalHandle.IsInvalid)
            {
                descriptor ??= QueryProperty(physicalHandle, 0, 1024);
                incursSeekPenalty ??= ReadBooleanDescriptor(physicalHandle, 7);
                trimSupported ??= ReadBooleanDescriptor(physicalHandle, 8);
            }
        }

        var registryDevice = deviceNumber is null ? null : ReadRegistryDeviceInfo(deviceNumber.Value);
        var model = ReadModel(descriptor) ?? registryDevice?.Model ?? "Модель не определена";
        var busCode = descriptor is { Length: >= 32 } ? BitConverter.ToUInt32(descriptor, 28) : 0;
        var removable = driveType == DriveType.Removable || descriptor is { Length: >= 11 } && descriptor[10] != 0;
        var mediaKind = removable
            ? StorageMediaKind.Removable
            : busCode is 14 or 15
                ? StorageMediaKind.Virtual
                : incursSeekPenalty == false || busCode == 17
                    ? StorageMediaKind.SolidState
                    : incursSeekPenalty == true
                        ? StorageMediaKind.HardDisk
                        : registryDevice?.MediaKind ?? StorageMediaKind.Unknown;
        return new StorageDeviceInfo(
            model,
            busCode == 0 ? registryDevice?.BusType ?? "Не определён" : FormatBusType(busCode),
            mediaKind,
            deviceNumber,
            trimSupported);
    }

    private static byte[]? QueryProperty(SafeFileHandle handle, uint propertyId, int outputSize)
    {
        var query = new byte[8];
        BitConverter.GetBytes(propertyId).CopyTo(query, 0);
        var output = new byte[outputSize];
        return StorageNativeMethods.DeviceIoControl(
            handle,
            StorageNativeMethods.IoctlStorageQueryProperty,
            query,
            (uint)query.Length,
            output,
            (uint)output.Length,
            out var bytesReturned,
            IntPtr.Zero)
            ? output[..(int)bytesReturned]
            : null;
    }

    private static bool? ReadBooleanDescriptor(SafeFileHandle handle, uint propertyId)
    {
        var descriptor = QueryProperty(handle, propertyId, 32);
        return descriptor is { Length: >= 9 } ? descriptor[8] != 0 : null;
    }

    private static uint? QueryDeviceNumber(SafeFileHandle handle)
    {
        var output = new byte[12];
        return StorageNativeMethods.DeviceIoControl(
            handle,
            StorageNativeMethods.IoctlStorageGetDeviceNumber,
            null,
            0,
            output,
            (uint)output.Length,
            out var bytesReturned,
            IntPtr.Zero) && bytesReturned >= 8
            ? BitConverter.ToUInt32(output, 4)
            : null;
    }

    private static string? ReadModel(byte[]? descriptor)
    {
        if (descriptor is not { Length: >= 32 })
        {
            return null;
        }

        var vendor = ReadDescriptorString(descriptor, BitConverter.ToUInt32(descriptor, 12));
        var product = ReadDescriptorString(descriptor, BitConverter.ToUInt32(descriptor, 16));
        var model = $"{vendor} {product}".Trim();
        return string.IsNullOrWhiteSpace(model) ? null : model;
    }

    private static RegistryStorageDeviceInfo? ReadRegistryDeviceInfo(uint deviceNumber)
    {
        using var diskEnum = Registry.LocalMachine.OpenSubKey(
            @"SYSTEM\CurrentControlSet\Services\disk\Enum",
            writable: false);
        var instancePath = diskEnum?.GetValue(deviceNumber.ToString()) as string;
        if (string.IsNullOrWhiteSpace(instancePath))
        {
            return null;
        }

        using var deviceKey = Registry.LocalMachine.OpenSubKey(
            $@"SYSTEM\CurrentControlSet\Enum\{instancePath}",
            writable: false);
        var friendlyName = deviceKey?.GetValue("FriendlyName") as string;
        var evidence = $"{instancePath} {friendlyName}";
        var busType = evidence.Contains("NVMe", StringComparison.OrdinalIgnoreCase)
            ? "NVMe"
            : evidence.Contains("USB", StringComparison.OrdinalIgnoreCase)
                ? "USB"
                : evidence.Contains("SATA", StringComparison.OrdinalIgnoreCase)
                    ? "SATA"
                    : "Не определён";
        var mediaKind = busType == "NVMe"
            ? StorageMediaKind.SolidState
            : evidence.Contains("VIRTUAL", StringComparison.OrdinalIgnoreCase) ||
              evidence.Contains("VHD", StringComparison.OrdinalIgnoreCase)
                ? StorageMediaKind.Virtual
                : StorageMediaKind.Unknown;
        return new RegistryStorageDeviceInfo(
            string.IsNullOrWhiteSpace(friendlyName) ? null : friendlyName.Trim(),
            busType,
            mediaKind);
    }

    private static string ReadDescriptorString(byte[] descriptor, uint offset)
    {
        if (offset == 0 || offset >= descriptor.Length)
        {
            return string.Empty;
        }

        var end = Array.IndexOf(descriptor, (byte)0, (int)offset);
        if (end < 0)
        {
            end = descriptor.Length;
        }

        return Encoding.ASCII.GetString(descriptor, (int)offset, end - (int)offset).Trim();
    }

    private static async Task<Dictionary<string, bool?>> ReadDeleteNotificationStateAsync(
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, bool?>(StringComparer.OrdinalIgnoreCase);
        var executable = Path.Combine(Environment.SystemDirectory, "fsutil.exe");
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("behavior");
        startInfo.ArgumentList.Add("query");
        startInfo.ArgumentList.Add("DisableDeleteNotify");

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return result;
            }

            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var output = (await outputTask) + Environment.NewLine + (await errorTask);
            foreach (Match match in DeleteNotifyRegex().Matches(output))
            {
                result[match.Groups[1].Value] = match.Groups[2].Value == "0";
            }
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // Device-level TRIM support remains available when fsutil cannot run.
        }

        return result;
    }

    private static string FormatBusType(uint busType) => busType switch
    {
        1 => "SCSI",
        2 => "ATAPI",
        3 => "ATA",
        7 => "USB",
        8 => "RAID",
        10 => "SAS",
        11 => "SATA",
        12 => "SD",
        13 => "MMC",
        14 => "Virtual",
        15 => "File-backed virtual",
        17 => "NVMe",
        18 => "SCM",
        19 => "UFS",
        _ => "Не определён",
    };

    [GeneratedRegex(@"(?im)^\s*(NTFS|ReFS)\s+DisableDeleteNotify\s*=\s*([01])")]
    private static partial Regex DeleteNotifyRegex();

    private sealed record StorageDeviceInfo(
        string Model,
        string BusType,
        StorageMediaKind MediaKind,
        uint? DeviceNumber,
        bool? TrimSupported)
    {
        public static StorageDeviceInfo Unknown(DriveType driveType) => new(
            "Модель не определена",
            "Не определён",
            driveType == DriveType.Removable ? StorageMediaKind.Removable : StorageMediaKind.Unknown,
            null,
            null);
    }

    private sealed record RegistryStorageDeviceInfo(
        string? Model,
        string BusType,
        StorageMediaKind MediaKind);
}

internal static class StorageNativeMethods
{
    internal const uint FileShareRead = 0x00000001;
    internal const uint FileShareWrite = 0x00000002;
    internal const uint OpenExisting = 3;
    internal const uint IoctlStorageQueryProperty = 0x002D1400;
    internal const uint IoctlStorageGetDeviceNumber = 0x002D1080;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeviceIoControl(
        SafeFileHandle device,
        uint ioControlCode,
        byte[]? inputBuffer,
        uint inputBufferSize,
        [Out] byte[] outputBuffer,
        uint outputBufferSize,
        out uint bytesReturned,
        IntPtr overlapped);
}
