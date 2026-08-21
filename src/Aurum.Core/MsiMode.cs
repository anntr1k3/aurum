using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Aurum.Core;

public enum MsiDevicePriority
{
    Undefined = 0,
    Low = 1,
    Normal = 2,
    High = 3
}

public enum MsiDeviceCategory
{
    Gpu,
    Network,
    Audio,
    Usb,
    Storage,
    System,
    Other
}

public sealed record PciDeviceMsiInfo(
    string DeviceInstanceId,
    string Name,
    MsiDeviceCategory Category,
    string LocationInfo,
    bool IsMsiSupported,
    int MessageNumberLimit,
    MsiDevicePriority Priority,
    bool IsPciDevice,
    bool CanModifyMsi)
{
    public string CategoryLabel => Category switch
    {
        MsiDeviceCategory.Gpu => "Видеокарта (GPU)",
        MsiDeviceCategory.Network => "Сетевой адаптер (LAN/Wi-Fi)",
        MsiDeviceCategory.Audio => "Звуковой контроллер (Audio)",
        MsiDeviceCategory.Usb => "USB контроллер (xHCI/USB4)",
        MsiDeviceCategory.Storage => "Контроллер дисков (NVMe/SATA)",
        MsiDeviceCategory.System => "Системное устройство",
        _ => "Другое устройство"
    };

    public string PriorityLabel => Priority switch
    {
        MsiDevicePriority.High => "Высокий (High)",
        MsiDevicePriority.Normal => "Обычный (Normal)",
        MsiDevicePriority.Low => "Низкий (Low)",
        _ => "По умолчанию (Undefined)"
    };
}

public sealed record MsiDeviceSnapshot(
    string DeviceInstanceId,
    bool? WasMsiSupported,
    int? PreviousMessageLimit,
    MsiDevicePriority? PreviousPriority);

public sealed record MsiStateSnapshot(
    DateTimeOffset CapturedAtUtc,
    IReadOnlyList<MsiDeviceSnapshot> Devices,
    int SchemaVersion = 1);

public interface IMsiDeviceInventory
{
    Task<IReadOnlyList<PciDeviceMsiInfo>> CaptureAsync(CancellationToken cancellationToken = default);
    Task SetMsiPropertiesAsync(
        string deviceInstanceId,
        bool enableMsi,
        int messageNumberLimit,
        MsiDevicePriority priority,
        CancellationToken cancellationToken = default);
}

public interface IMsiStateRepository
{
    Task<MsiStateSnapshot?> ReadAsync(CancellationToken cancellationToken = default);
    Task WriteAsync(MsiStateSnapshot snapshot, CancellationToken cancellationToken = default);
    Task ClearAsync(CancellationToken cancellationToken = default);
}

public sealed class MsiModeManager
{
    private readonly IMsiDeviceInventory _inventory;
    private readonly IMsiStateRepository _repository;
    private readonly Func<bool> _isAdministrator;

    public MsiModeManager(
        IMsiDeviceInventory inventory,
        IMsiStateRepository repository,
        Func<bool> isAdministrator)
    {
        _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _isAdministrator = isAdministrator ?? throw new ArgumentNullException(nameof(isAdministrator));
    }

    public async Task<IReadOnlyList<PciDeviceMsiInfo>> CaptureAsync(CancellationToken cancellationToken = default)
    {
        return await _inventory.CaptureAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> HasActiveModificationsAsync(CancellationToken cancellationToken = default)
    {
        var state = await _repository.ReadAsync(cancellationToken).ConfigureAwait(false);
        return state is not null && state.Devices.Count > 0;
    }

    public async Task ApplyDeviceMsiAsync(
        string deviceInstanceId,
        bool enableMsi,
        MsiDevicePriority priority,
        CancellationToken cancellationToken = default)
    {
        if (!_isAdministrator())
        {
            throw new InvalidOperationException("Для изменения параметров прерываний MSI требуются права администратора.");
        }

        var devices = await _inventory.CaptureAsync(cancellationToken).ConfigureAwait(false);
        var target = devices.FirstOrDefault(d => string.Equals(d.DeviceInstanceId, deviceInstanceId, StringComparison.OrdinalIgnoreCase));
        if (target is null)
        {
            throw new InvalidOperationException($"Устройство с ID '{deviceInstanceId}' не найдено.");
        }

        // Сохраняем исходное состояние, если еще не сохраняли
        var state = await _repository.ReadAsync(cancellationToken).ConfigureAwait(false);
        var snapshots = state?.Devices.ToList() ?? new List<MsiDeviceSnapshot>();
        if (!snapshots.Any(s => string.Equals(s.DeviceInstanceId, deviceInstanceId, StringComparison.OrdinalIgnoreCase)))
        {
            snapshots.Add(new MsiDeviceSnapshot(
                target.DeviceInstanceId,
                target.IsMsiSupported,
                target.MessageNumberLimit,
                target.Priority));

            await _repository.WriteAsync(
                new MsiStateSnapshot(DateTimeOffset.UtcNow, snapshots),
                cancellationToken).ConfigureAwait(false);
        }

        var limit = target.MessageNumberLimit > 0 ? target.MessageNumberLimit : 1;
        await _inventory.SetMsiPropertiesAsync(deviceInstanceId, enableMsi, limit, priority, cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> ApplyGamingPresetAsync(CancellationToken cancellationToken = default)
    {
        if (!_isAdministrator())
        {
            throw new InvalidOperationException("Для оптимизации MSI требуются права администратора.");
        }

        var devices = await _inventory.CaptureAsync(cancellationToken).ConfigureAwait(false);
        var targetDevices = devices.Where(d => d.CanModifyMsi && (
            d.Category == MsiDeviceCategory.Gpu ||
            d.Category == MsiDeviceCategory.Network ||
            d.Category == MsiDeviceCategory.Audio ||
            d.Category == MsiDeviceCategory.Usb ||
            d.Category == MsiDeviceCategory.Storage)).ToList();

        if (targetDevices.Count == 0)
        {
            return 0;
        }

        var state = await _repository.ReadAsync(cancellationToken).ConfigureAwait(false);
        var snapshots = state?.Devices.ToList() ?? new List<MsiDeviceSnapshot>();

        foreach (var dev in targetDevices)
        {
            if (!snapshots.Any(s => string.Equals(s.DeviceInstanceId, dev.DeviceInstanceId, StringComparison.OrdinalIgnoreCase)))
            {
                snapshots.Add(new MsiDeviceSnapshot(
                    dev.DeviceInstanceId,
                    dev.IsMsiSupported,
                    dev.MessageNumberLimit,
                    dev.Priority));
            }
        }

        await _repository.WriteAsync(
            new MsiStateSnapshot(DateTimeOffset.UtcNow, snapshots),
            cancellationToken).ConfigureAwait(false);

        var count = 0;
        foreach (var dev in targetDevices)
        {
            // Для видеокарты и сети - высокий приоритет; для USB, Audio, Storage - Normal или Undefined
            var priority = dev.Category switch
            {
                MsiDeviceCategory.Gpu => MsiDevicePriority.High,
                MsiDeviceCategory.Network => MsiDevicePriority.High,
                MsiDeviceCategory.Usb => MsiDevicePriority.Normal,
                MsiDeviceCategory.Audio => MsiDevicePriority.Normal,
                MsiDeviceCategory.Storage => MsiDevicePriority.Normal,
                _ => MsiDevicePriority.Undefined
            };

            var limit = dev.MessageNumberLimit > 0 ? dev.MessageNumberLimit : 1;
            await _inventory.SetMsiPropertiesAsync(dev.DeviceInstanceId, true, limit, priority, cancellationToken).ConfigureAwait(false);
            count++;
        }

        return count;
    }

    public async Task RevertAsync(CancellationToken cancellationToken = default)
    {
        if (!_isAdministrator())
        {
            throw new InvalidOperationException("Для отката параметров MSI требуются права администратора.");
        }

        var state = await _repository.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (state is null || state.Devices.Count == 0)
        {
            return;
        }

        foreach (var snapshot in state.Devices)
        {
            var enableMsi = snapshot.WasMsiSupported ?? false;
            var limit = snapshot.PreviousMessageLimit ?? 1;
            var priority = snapshot.PreviousPriority ?? MsiDevicePriority.Undefined;

            await _inventory.SetMsiPropertiesAsync(
                snapshot.DeviceInstanceId,
                enableMsi,
                limit,
                priority,
                cancellationToken).ConfigureAwait(false);
        }

        await _repository.ClearAsync(cancellationToken).ConfigureAwait(false);
    }
}
