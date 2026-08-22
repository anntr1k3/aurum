using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Aurum.Infrastructure.Windows;

public sealed record CleanupCategory(
    string Id,
    string Name,
    string Description,
    string RootPath,
    TimeSpan MinimumAge);

public sealed record CleanupCandidate(
    string CategoryId,
    string CategoryName,
    string FullPath,
    long Length,
    DateTime LastWriteTimeUtc);

public sealed record CleanupScanResult(
    IReadOnlyList<CleanupCandidate> Candidates,
    IReadOnlyList<string> Errors,
    bool IsTruncated,
    DateTimeOffset ScannedAt)
{
    public long TotalBytes => Candidates.Sum(static candidate => candidate.Length);
}

public sealed record CleanupExecutionResult(
    int DeletedCount,
    int SkippedCount,
    long FreedBytes,
    IReadOnlyList<string> Errors);

public sealed class SystemCleanupService
{
    private const int MaximumCandidates = 50_000;
    private readonly IReadOnlyDictionary<string, CleanupCategory> _categories;

    public SystemCleanupService(IReadOnlyList<CleanupCategory>? categories = null)
    {
        Categories = categories ?? CreateDefaultCategories();
        if (Categories.Select(static category => category.Id).Distinct(StringComparer.Ordinal).Count() != Categories.Count)
        {
            throw new ArgumentException("Cleanup category identifiers must be unique.", nameof(categories));
        }

        _categories = Categories.ToDictionary(static category => category.Id, StringComparer.Ordinal);
    }

    public IReadOnlyList<CleanupCategory> Categories { get; }

    public Task<CleanupScanResult> ScanAsync(
        IReadOnlyCollection<string> categoryIds,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Scan(categoryIds, cancellationToken), cancellationToken);

    public Task<CleanupExecutionResult> CleanAsync(
        IReadOnlyCollection<CleanupCandidate> candidates,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Clean(candidates, cancellationToken), cancellationToken);

    private CleanupScanResult Scan(IReadOnlyCollection<string> categoryIds, CancellationToken cancellationToken)
    {
        var candidates = new List<CleanupCandidate>();
        var errors = new List<string>();
        var isTruncated = false;

        foreach (var categoryId in categoryIds.Distinct(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_categories.TryGetValue(categoryId, out var category) || !Directory.Exists(category.RootPath))
            {
                continue;
            }

            var cutoff = DateTime.UtcNow - category.MinimumAge;
            try
            {
                // AttributesToSkip applies to directories as well as files, so this also
                // stops the recursion from following a junction planted inside the root
                // and enumerating files outside the category.
                var enumerationOptions = new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true,
                    ReturnSpecialDirectories = false,
                    AttributesToSkip = FileAttributes.ReparsePoint
                };

                foreach (var filePath in Directory.EnumerateFiles(category.RootPath, "*", enumerationOptions))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (candidates.Count >= MaximumCandidates)
                    {
                        isTruncated = true;
                        break;
                    }

                    try
                    {
                        var file = new FileInfo(filePath);
                        if (!file.Exists || file.LastWriteTimeUtc > cutoff ||
                            file.Attributes.HasFlag(FileAttributes.ReparsePoint))
                        {
                            continue;
                        }

                        candidates.Add(new CleanupCandidate(
                            category.Id,
                            category.Name,
                            file.FullName,
                            file.Length,
                            file.LastWriteTimeUtc));
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                    {
                        errors.Add($"{filePath}: {error.Message}");
                    }
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                errors.Add($"{category.RootPath}: {error.Message}");
            }

            if (isTruncated)
            {
                break;
            }
        }

        return new CleanupScanResult(candidates, errors, isTruncated, DateTimeOffset.Now);
    }

    private CleanupExecutionResult Clean(
        IReadOnlyCollection<CleanupCandidate> candidates,
        CancellationToken cancellationToken)
    {
        var deletedCount = 0;
        var skippedCount = 0;
        long freedBytes = 0;
        var errors = new List<string>();

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_categories.TryGetValue(candidate.CategoryId, out var category) ||
                !IsPathInsideRoot(candidate.FullPath, category.RootPath))
            {
                skippedCount++;
                errors.Add($"Пропущен путь вне разрешённой категории: {candidate.FullPath}");
                continue;
            }

            try
            {
                // Everything scanned here lives in directories the unelevated user can
                // write, while the delete itself usually runs elevated. Re-reading the
                // file and requiring size and write time to still match what the scan saw
                // means a file swapped between scan and delete is skipped rather than
                // removed, and the reparse-point check keeps a planted link from
                // redirecting the delete outside the category root.
                if (!TryDeleteUnchangedFile(candidate.FullPath, candidate.Length, candidate.LastWriteTimeUtc))
                {
                    skippedCount++;
                    continue;
                }

                deletedCount++;
                freedBytes += candidate.Length;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                skippedCount++;
                errors.Add($"{candidate.FullPath}: {error.Message}");
            }
        }

        return new CleanupExecutionResult(deletedCount, skippedCount, freedBytes, errors);
    }

    private static IReadOnlyList<CleanupCategory> CreateDefaultCategories()
    {
        var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return
        [
            new CleanupCategory(
                "user-temp",
                "Временные файлы пользователя",
                "Файлы старше суток. Активные и новые файлы не затрагиваются.",
                Path.GetTempPath(),
                TimeSpan.FromDays(1)),
            new CleanupCategory(
                "shader-cache",
                "Кэш шейдеров DirectX",
                "Будет создан заново драйвером или игрой при необходимости.",
                Path.Combine(localApplicationData, "D3DSCache"),
                TimeSpan.FromDays(1)),
            new CleanupCategory(
                "crash-dumps",
                "Дампы сбоев приложений",
                "Диагностические дампы старше семи дней. Они могут быть нужны для анализа ошибок.",
                Path.Combine(localApplicationData, "CrashDumps"),
                TimeSpan.FromDays(7))
        ];
    }

    private static bool IsPathInsideRoot(string candidatePath, string rootPath)
    {
        var candidate = Path.GetFullPath(candidatePath);
        var root = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var rootPrefix = root + Path.DirectorySeparatorChar;
        return candidate.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Opens the path with exclusive DELETE access and a reparse-point flag so a
    /// replacement between scan and delete cannot redirect the unlink. Size and write
    /// time are read from the handle, then delete-on-close is set before the handle is
    /// released.
    /// </summary>
    private static bool TryDeleteUnchangedFile(string path, long expectedLength, DateTime expectedWriteUtc)
    {
        using var handle = NativeMethods.CreateFile(
            path,
            NativeMethods.GenericRead | NativeMethods.DeleteAccess,
            NativeMethods.FileShareNone,
            IntPtr.Zero,
            NativeMethods.OpenExisting,
            NativeMethods.FileFlagOpenReparsePoint,
            IntPtr.Zero);
        if (handle.IsInvalid)
        {
            return false;
        }

        if (!NativeMethods.GetFileInformationByHandle(handle, out var info))
        {
            return false;
        }

        if ((info.FileAttributes & NativeMethods.FileAttributeReparsePoint) != 0)
        {
            return false;
        }

        var size = ((long)info.FileSizeHigh << 32) | info.FileSizeLow;
        if (size != expectedLength)
        {
            return false;
        }

        var writeTime = DateTime.FromFileTimeUtc(
            ((long)info.LastWriteTimeHigh << 32) | info.LastWriteTimeLow);
        if (writeTime != expectedWriteUtc)
        {
            return false;
        }

        var disposition = new NativeMethods.FileDispositionInfo { DeleteFile = 1 };
        return NativeMethods.SetFileInformationByHandle(
            handle,
            NativeMethods.FileDispositionInfoClass,
            ref disposition,
            NativeMethods.FileDispositionInfoSize);
    }

    private static class NativeMethods
    {
        internal const uint GenericRead = 0x80000000;
        internal const uint DeleteAccess = 0x00010000;
        internal const uint FileShareNone = 0;
        internal const uint OpenExisting = 3;
        internal const uint FileFlagOpenReparsePoint = 0x00200000;
        internal const uint FileAttributeReparsePoint = 0x400;
        internal const int FileDispositionInfoClass = 4;
        internal static readonly int FileDispositionInfoSize = Marshal.SizeOf<FileDispositionInfo>();

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern SafeFileHandle CreateFile(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool GetFileInformationByHandle(
            SafeFileHandle hFile,
            out ByHandleFileInformation lpFileInformation);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool SetFileInformationByHandle(
            SafeFileHandle hFile,
            int fileInformationClass,
            ref FileDispositionInfo fileInformation,
            int bufferSize);

        [StructLayout(LayoutKind.Sequential)]
        internal struct ByHandleFileInformation
        {
            internal uint FileAttributes;
            internal uint CreationTimeLow;
            internal uint CreationTimeHigh;
            internal uint LastAccessTimeLow;
            internal uint LastAccessTimeHigh;
            internal uint LastWriteTimeLow;
            internal uint LastWriteTimeHigh;
            internal uint VolumeSerialNumber;
            internal uint FileSizeHigh;
            internal uint FileSizeLow;
            internal uint NumberOfLinks;
            internal uint FileIndexHigh;
            internal uint FileIndexLow;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct FileDispositionInfo
        {
            internal byte DeleteFile;
        }
    }
}
