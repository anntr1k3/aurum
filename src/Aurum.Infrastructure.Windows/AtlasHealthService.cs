using System.Security.Cryptography;
using Microsoft.Win32;

namespace Aurum.Infrastructure.Windows;

public enum HealthCheckStatus
{
    Healthy,
    Warning,
    Failed,
    NotApplicable
}

public sealed record HealthCheckResult(
    string Id,
    string Name,
    string Details,
    HealthCheckStatus Status);

public sealed record AtlasHealthReport(
    bool IsDetected,
    string VersionLabel,
    IReadOnlyList<HealthCheckResult> Checks,
    DateTimeOffset CheckedAt);

public sealed class AtlasHealthService
{
    private static readonly IReadOnlyDictionary<string, string> OfficialHashes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [@"Tools\multichoice.exe"] = "6AB2FF0163AFE0FAC4E7506F9A63293421A1880076944339700A59A06578927D",
            [@"Tools\SetTimerResolution.exe"] = "0515C2428E8960C751AD697ACA1C8D03BD43E2F0F1A0C0D2B4D998361C35EB57"
        };

    public Task<AtlasHealthReport> CheckAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => Check(cancellationToken), cancellationToken);

    private static AtlasHealthReport Check(CancellationToken cancellationToken)
    {
        var windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var modulesDirectory = Path.Combine(windowsDirectory, "AtlasModules");
        var desktopDirectory = Path.Combine(windowsDirectory, "AtlasDesktop");
        var model = ReadRegistryString(
            Registry.LocalMachine,
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\OEMInformation",
            "Model");
        var organization = ReadRegistryString(
            Registry.LocalMachine,
            @"SOFTWARE\Microsoft\Windows NT\CurrentVersion",
            "RegisteredOrganization");

        var isDetected = Directory.Exists(modulesDirectory) ||
                         Directory.Exists(desktopDirectory) ||
                         ContainsAtlas(model) ||
                         ContainsAtlas(organization);

        if (!isDetected)
        {
            return new AtlasHealthReport(
                false,
                "AtlasOS не обнаружен",
                [new HealthCheckResult(
                    "atlas.installation",
                    "Установка AtlasOS",
                    "Не найдены официальные каталоги или версионные метаданные. На обычной Windows это нормально.",
                    HealthCheckStatus.NotApplicable)],
                DateTimeOffset.Now);
        }

        var checks = new List<HealthCheckResult>();
        AddDirectoryCheck(
            checks,
            "atlas.modules",
            "Модули AtlasOS",
            modulesDirectory,
            required: true);
        AddDirectoryCheck(
            checks,
            "atlas.desktop",
            "Папка конфигурации AtlasOS",
            desktopDirectory,
            required: true);
        AddDirectoryCheck(
            checks,
            "atlas.scripts",
            "Сценарии AtlasOS",
            Path.Combine(modulesDirectory, "Scripts"),
            required: true);

        var versionLabel = FirstAtlasValue(model, organization) ?? "Версия не определена";
        checks.Add(new HealthCheckResult(
            "atlas.version",
            "Версионные метаданные",
            FirstAtlasValue(model, organization) is null
                ? "AtlasOS обнаружен, но OEM-метаданные версии отсутствуют или были изменены."
                : versionLabel,
            FirstAtlasValue(model, organization) is null
                ? HealthCheckStatus.Warning
                : HealthCheckStatus.Healthy));

        using (var atlasStateKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\AtlasOS\Services", writable: false))
        {
            checks.Add(new HealthCheckResult(
                "atlas.component-state",
                "Состояния компонентов AtlasOS",
                atlasStateKey is null
                    ? "Раздел состояний не найден. Это возможно на старой версии AtlasOS."
                    : $"Найдено сохранённых состояний компонентов: {atlasStateKey.GetSubKeyNames().Length}.",
                atlasStateKey is null ? HealthCheckStatus.Warning : HealthCheckStatus.Healthy));
        }

        foreach (var knownHash in OfficialHashes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var filePath = Path.Combine(modulesDirectory, knownHash.Key);
            checks.Add(CheckHash(filePath, knownHash.Value));
        }

        return new AtlasHealthReport(true, versionLabel, checks, DateTimeOffset.Now);
    }

    private static HealthCheckResult CheckHash(string filePath, string expectedHash)
    {
        var fileName = Path.GetFileName(filePath);
        if (!File.Exists(filePath))
        {
            return new HealthCheckResult(
                $"atlas.hash.{fileName.ToLowerInvariant()}",
                $"Целостность {fileName}",
                "Файл отсутствует. Компонент может быть необязательным для этой сборки AtlasOS.",
                HealthCheckStatus.NotApplicable);
        }

        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var actualHash = Convert.ToHexString(SHA256.HashData(stream));
            var matches = string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase);
            return new HealthCheckResult(
                $"atlas.hash.{fileName.ToLowerInvariant()}",
                $"Целостность {fileName}",
                matches
                    ? "SHA-256 совпадает с опубликованным AtlasOS."
                    : $"SHA-256 не совпадает. Ожидался {expectedHash}, получен {actualHash}.",
                matches ? HealthCheckStatus.Healthy : HealthCheckStatus.Failed);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return new HealthCheckResult(
                $"atlas.hash.{fileName.ToLowerInvariant()}",
                $"Целостность {fileName}",
                $"Не удалось прочитать файл: {error.Message}",
                HealthCheckStatus.Warning);
        }
    }

    private static void AddDirectoryCheck(
        ICollection<HealthCheckResult> checks,
        string id,
        string name,
        string directory,
        bool required)
    {
        var exists = Directory.Exists(directory);
        checks.Add(new HealthCheckResult(
            id,
            name,
            exists ? directory : $"Каталог отсутствует: {directory}",
            exists ? HealthCheckStatus.Healthy : required ? HealthCheckStatus.Failed : HealthCheckStatus.Warning));
    }

    private static string? ReadRegistryString(RegistryKey hive, string subKey, string valueName)
    {
        using var key = hive.OpenSubKey(subKey, writable: false);
        return key?.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
    }

    private static bool ContainsAtlas(string? value) =>
        value?.Contains("Atlas", StringComparison.OrdinalIgnoreCase) == true;

    private static string? FirstAtlasValue(params string?[] values) =>
        values.FirstOrDefault(ContainsAtlas);
}
