using System.Runtime.InteropServices;
using System.Security.Principal;

namespace Aurum.Infrastructure.Windows;

public sealed record SystemSnapshot(
    string OperatingSystem,
    string Architecture,
    string WindowsBuild,
    bool IsAdministrator,
    bool AtlasMarkerDetected,
    DateTimeOffset CollectedAt);

public sealed class WindowsSystemProbe
{
    public SystemSnapshot Capture()
    {
        var windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var atlasMarkerDetected = Directory.Exists(Path.Combine(windowsDirectory, "AtlasModules")) ||
                                  Directory.Exists(Path.Combine(windowsDirectory, "AtlasDesktop"));

        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        var isAdministrator = principal.IsInRole(WindowsBuiltInRole.Administrator);

        return new SystemSnapshot(
            RuntimeInformation.OSDescription,
            RuntimeInformation.OSArchitecture.ToString(),
            Environment.OSVersion.Version.ToString(),
            isAdministrator,
            atlasMarkerDetected,
            DateTimeOffset.Now);
    }
}
