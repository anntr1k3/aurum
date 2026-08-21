using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Aurum.Core;
using Microsoft.Win32;

namespace Aurum.Infrastructure.Windows;

public sealed class WindowsPciDeviceInventory : IMsiDeviceInventory
{
    private const string PciEnumPath = @"SYSTEM\CurrentControlSet\Enum\PCI";

    public Task<IReadOnlyList<PciDeviceMsiInfo>> CaptureAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<PciDeviceMsiInfo>();

        try
        {
            using var pciKey = Registry.LocalMachine.OpenSubKey(PciEnumPath, writable: false);
            if (pciKey is null)
            {
                return Task.FromResult<IReadOnlyList<PciDeviceMsiInfo>>(result);
            }

            foreach (var hardwareId in pciKey.GetSubKeyNames())
            {
                cancellationToken.ThrowIfCancellationRequested();

                using var hardwareKey = pciKey.OpenSubKey(hardwareId, writable: false);
                if (hardwareKey is null)
                {
                    continue;
                }

                foreach (var instanceId in hardwareKey.GetSubKeyNames())
                {
                    using var instanceKey = hardwareKey.OpenSubKey(instanceId, writable: false);
                    if (instanceKey is null)
                    {
                        continue;
                    }

                    var deviceInstancePath = $@"PCI\{hardwareId}\{instanceId}";
                    var friendlyName = instanceKey.GetValue("FriendlyName") as string;
                    var deviceDesc = instanceKey.GetValue("DeviceDesc") as string;
                    var name = CleanDeviceName(friendlyName, deviceDesc, hardwareId);

                    var deviceClass = instanceKey.GetValue("Class") as string ?? string.Empty;
                    var locationInfo = instanceKey.GetValue("LocationInformation") as string ?? string.Empty;

                    var category = DetermineCategory(deviceClass, name);

                    var isMsiSupported = false;
                    var messageNumberLimit = 1;
                    var priority = MsiDevicePriority.Undefined;

                    using var msiPropsKey = instanceKey.OpenSubKey(@"Device Parameters\Interrupt Management\MessageSignaledInterruptProperties", writable: false);
                    if (msiPropsKey is not null)
                    {
                        if (msiPropsKey.GetValue("MSISupported") is int msiVal)
                        {
                            isMsiSupported = msiVal == 1;
                        }
                        if (msiPropsKey.GetValue("MessageNumberLimit") is int limitVal && limitVal > 0)
                        {
                            messageNumberLimit = limitVal;
                        }
                    }

                    using var affinityKey = instanceKey.OpenSubKey(@"Device Parameters\Interrupt Management\Affinity Policy", writable: false);
                    if (affinityKey is not null)
                    {
                        if (affinityKey.GetValue("DevicePriority") is int prioVal)
                        {
                            priority = prioVal switch
                            {
                                1 => MsiDevicePriority.Low,
                                2 => MsiDevicePriority.High,
                                0 => MsiDevicePriority.Normal,
                                _ => MsiDevicePriority.Undefined
                            };
                        }
                    }

                    // Системные мосты PCI-to-PCI и host bridge обычно не поддерживают прямое изменение MSI
                    var canModify = category != MsiDeviceCategory.System &&
                                    !name.Contains("PCI-to-PCI", StringComparison.OrdinalIgnoreCase) &&
                                    !name.Contains("Host Bridge", StringComparison.OrdinalIgnoreCase);

                    result.Add(new PciDeviceMsiInfo(
                        DeviceInstanceId: deviceInstancePath,
                        Name: name,
                        Category: category,
                        LocationInfo: locationInfo,
                        IsMsiSupported: isMsiSupported,
                        MessageNumberLimit: messageNumberLimit,
                        Priority: priority,
                        IsPciDevice: true,
                        CanModifyMsi: canModify));
                }
            }
        }
        catch
        {
            // Возвращаем то, что удалось прочитать
        }

        // Сортируем: сначала GPU, потом Сеть, Звук, USB, Накопители, затем остальные
        var ordered = result
            .OrderBy(d => GetCategorySortOrder(d.Category))
            .ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Task.FromResult<IReadOnlyList<PciDeviceMsiInfo>>(ordered);
    }

    public Task SetMsiPropertiesAsync(
        string deviceInstanceId,
        bool enableMsi,
        int messageNumberLimit,
        MsiDevicePriority priority,
        CancellationToken cancellationToken = default)
    {
        if (!MsiDeviceId.TryParse(deviceInstanceId, out var hardwareId, out var instanceId))
        {
            throw new InvalidOperationException(
                $"Идентификатор устройства '{deviceInstanceId}' не является допустимым путём PCI. Aurum не будет записывать в реестр по этому имени.");
        }

        using var pciKey = Registry.LocalMachine.OpenSubKey(PciEnumPath, writable: true);
        if (pciKey is null)
        {
            throw new InvalidOperationException($"Не удалось открыть ключ реестра {PciEnumPath}.");
        }

        using var hardwareKey = pciKey.OpenSubKey(hardwareId, writable: true);
        if (hardwareKey is null)
        {
            throw new InvalidOperationException($"Устройство PCI '{hardwareId}' не найдено в инвентаре.");
        }

        using var deviceKey = hardwareKey.OpenSubKey(instanceId, writable: true);
        if (deviceKey is null)
        {
            throw new InvalidOperationException($"Экземпляр устройства '{hardwareId}\\{instanceId}' не найден в инвентаре.");
        }

        using var devParamsKey = deviceKey.CreateSubKey("Device Parameters", writable: true);
        using var intMgmtKey = devParamsKey.CreateSubKey("Interrupt Management", writable: true);
        using var msiKey = intMgmtKey.CreateSubKey("MessageSignaledInterruptProperties", writable: true);

        msiKey.SetValue("MSISupported", enableMsi ? 1 : 0, RegistryValueKind.DWord);
        if (messageNumberLimit > 0)
        {
            msiKey.SetValue("MessageNumberLimit", messageNumberLimit, RegistryValueKind.DWord);
        }

        using var affinityKey = intMgmtKey.CreateSubKey("Affinity Policy", writable: true);
        if (priority != MsiDevicePriority.Undefined)
        {
            var rawPriority = priority switch
            {
                MsiDevicePriority.Low => 1,
                MsiDevicePriority.Normal => 0,
                MsiDevicePriority.High => 2,
                _ => 0
            };
            affinityKey.SetValue("DevicePriority", rawPriority, RegistryValueKind.DWord);
        }
        else
        {
            try
            {
                affinityKey.DeleteValue("DevicePriority", throwOnMissingValue: false);
            }
            catch
            {
                // Игнорируем
            }
        }

        return Task.CompletedTask;
    }

    private static string CleanDeviceName(string? friendlyName, string? deviceDesc, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(friendlyName))
        {
            return friendlyName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(deviceDesc))
        {
            var idx = deviceDesc.IndexOf(';');
            if (idx >= 0 && idx < deviceDesc.Length - 1)
            {
                return deviceDesc.Substring(idx + 1).Trim();
            }
            return deviceDesc.Trim();
        }

        return fallback;
    }

    private static MsiDeviceCategory DetermineCategory(string deviceClass, string name)
    {
        if (deviceClass.Contains("Display", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("GeForce", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Radeon", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Intel(R) UHD", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Intel(R) Iris", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Graphics", StringComparison.OrdinalIgnoreCase))
        {
            return MsiDeviceCategory.Gpu;
        }

        if (deviceClass.Contains("Net", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Ethernet", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Wi-Fi", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Wireless", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Network", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("LAN", StringComparison.OrdinalIgnoreCase))
        {
            return MsiDeviceCategory.Network;
        }

        if (deviceClass.Contains("Media", StringComparison.OrdinalIgnoreCase) ||
            deviceClass.Contains("Audio", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Audio", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Sound", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Realtek High Definition", StringComparison.OrdinalIgnoreCase))
        {
            return MsiDeviceCategory.Audio;
        }

        if (deviceClass.Contains("USB", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Host Controller", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("xHCI", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("USB 3", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("USB4", StringComparison.OrdinalIgnoreCase))
        {
            return MsiDeviceCategory.Usb;
        }

        if (deviceClass.Contains("SCSI", StringComparison.OrdinalIgnoreCase) ||
            deviceClass.Contains("HDC", StringComparison.OrdinalIgnoreCase) ||
            deviceClass.Contains("Disk", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("NVM Express", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("NVMe", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("SATA", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("AHCI", StringComparison.OrdinalIgnoreCase))
        {
            return MsiDeviceCategory.Storage;
        }

        if (deviceClass.Contains("System", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("PCI-to-PCI", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Bridge", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Root Complex", StringComparison.OrdinalIgnoreCase))
        {
            return MsiDeviceCategory.System;
        }

        return MsiDeviceCategory.Other;
    }

    private static int GetCategorySortOrder(MsiDeviceCategory category) => category switch
    {
        MsiDeviceCategory.Gpu => 1,
        MsiDeviceCategory.Network => 2,
        MsiDeviceCategory.Audio => 3,
        MsiDeviceCategory.Usb => 4,
        MsiDeviceCategory.Storage => 5,
        MsiDeviceCategory.Other => 6,
        MsiDeviceCategory.System => 7,
        _ => 8
    };
}
