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
                var file = new FileInfo(candidate.FullPath);
                if (!file.Exists || file.Attributes.HasFlag(FileAttributes.ReparsePoint) ||
                    file.Length != candidate.Length || file.LastWriteTimeUtc != candidate.LastWriteTimeUtc)
                {
                    skippedCount++;
                    continue;
                }

                file.Delete();
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
}
