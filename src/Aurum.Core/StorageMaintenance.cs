namespace Aurum.Core;

public enum StorageMediaKind
{
    Unknown,
    SolidState,
    HardDisk,
    Removable,
    Virtual,
}

public sealed record StorageVolumeInfo(
    string RootPath,
    string Label,
    string FileSystem,
    string Model,
    string BusType,
    StorageMediaKind MediaKind,
    long TotalBytes,
    long FreeBytes,
    uint? DeviceNumber,
    bool? TrimSupported,
    bool? DeleteNotificationsEnabled,
    bool IsSystem);

public enum StorageOperationKind
{
    Analyze,
    Retrim,
}

public sealed record StorageOperationResult(
    StorageOperationKind Operation,
    string RootPath,
    int ExitCode,
    string Output,
    DateTimeOffset CompletedAt)
{
    public bool Succeeded => ExitCode == 0;
}

public sealed record StorageOperationAvailability(bool CanRun, string Reason);

public interface IStorageInventoryStore
{
    Task<IReadOnlyList<StorageVolumeInfo>> CaptureAsync(CancellationToken cancellationToken = default);
}

public interface IStorageOptimizer
{
    Task<StorageOperationResult> RunAsync(
        string rootPath,
        StorageOperationKind operation,
        CancellationToken cancellationToken = default);
}

public sealed class StorageMaintenanceManager
{
    private static readonly string[] AnalyzableFileSystems = ["NTFS", "ReFS", "FAT", "FAT32"];
    private static readonly string[] RetrimmableFileSystems = ["NTFS", "ReFS"];
    private readonly IStorageInventoryStore _inventory;
    private readonly IStorageOptimizer _optimizer;
    private readonly Func<bool> _isAdministrator;
    private readonly IAuditJournal? _auditJournal;

    public StorageMaintenanceManager(
        IStorageInventoryStore inventory,
        IStorageOptimizer optimizer,
        Func<bool> isAdministrator,
        IAuditJournal? auditJournal = null)
    {
        _inventory = inventory;
        _optimizer = optimizer;
        _isAdministrator = isAdministrator;
        _auditJournal = auditJournal;
    }

    public Task<IReadOnlyList<StorageVolumeInfo>> CaptureAsync(CancellationToken cancellationToken = default) =>
        _inventory.CaptureAsync(cancellationToken);

    public Task<StorageOperationResult> AnalyzeAsync(
        string rootPath,
        CancellationToken cancellationToken = default) =>
        RunValidatedAsync(rootPath, StorageOperationKind.Analyze, cancellationToken);

    public Task<StorageOperationResult> RetrimAsync(
        string rootPath,
        CancellationToken cancellationToken = default) =>
        RunValidatedAsync(rootPath, StorageOperationKind.Retrim, cancellationToken);

    public StorageOperationAvailability EvaluateAvailability(
        StorageVolumeInfo volume,
        StorageOperationKind operation)
    {
        if (volume.MediaKind == StorageMediaKind.Removable)
        {
            return new StorageOperationAvailability(false, "Съёмные накопители доступны только для диагностики.");
        }

        if (!_isAdministrator())
        {
            return new StorageOperationAvailability(false, "Для операции перезапустите Aurum от имени администратора.");
        }

        var supportedFileSystems = operation == StorageOperationKind.Analyze
            ? AnalyzableFileSystems
            : RetrimmableFileSystems;
        if (!supportedFileSystems.Contains(volume.FileSystem, StringComparer.OrdinalIgnoreCase))
        {
            return new StorageOperationAvailability(false, $"Файловая система {volume.FileSystem} не поддерживается.");
        }

        if (operation == StorageOperationKind.Retrim)
        {
            if (volume.MediaKind == StorageMediaKind.HardDisk)
            {
                return new StorageOperationAvailability(false, "ReTrim не предлагается для вращающегося диска.");
            }

            if (volume.TrimSupported != true)
            {
                return new StorageOperationAvailability(false, "Накопитель не подтвердил поддержку TRIM.");
            }

            if (volume.DeleteNotificationsEnabled != true)
            {
                return new StorageOperationAvailability(false, "Уведомления TRIM отключены или их состояние неизвестно.");
            }
        }

        return new StorageOperationAvailability(
            true,
            operation == StorageOperationKind.Analyze
                ? "Доступен безопасный анализ тома."
                : "Доступен штатный Windows ReTrim.");
    }

    private async Task<StorageOperationResult> RunValidatedAsync(
        string rootPath,
        StorageOperationKind operation,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        var volumes = await _inventory.CaptureAsync(cancellationToken);
        var volume = volumes.FirstOrDefault(candidate =>
            string.Equals(candidate.RootPath, rootPath, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Выбранный том больше не доступен.");

        var availability = EvaluateAvailability(volume, operation);
        if (!availability.CanRun)
        {
            throw new InvalidOperationException(availability.Reason);
        }

        var result = await _optimizer.RunAsync(volume.RootPath, operation, cancellationToken);
        if (operation == StorageOperationKind.Retrim)
        {
            await AuditJournal.RecordAsync(
                _auditJournal,
                "storage",
                "ReTrim",
                result.Succeeded ? AuditAction.Applied : AuditAction.Failed,
                result.Succeeded,
                result.Succeeded
                    ? $"Windows ReTrim выполнен для {volume.RootPath}."
                    : $"ReTrim {volume.RootPath} завершился с кодом {result.ExitCode}.",
                cancellationToken);
        }

        return result;
    }

}
