using System.Diagnostics;
using Aurum.Core;
using Microsoft.Win32;

namespace Aurum.Infrastructure.Windows;

public sealed class WindowsStorageTuningStore : IStorageTuningStore
{
    private const string FileSystemKeyPath = @"SYSTEM\CurrentControlSet\Control\FileSystem";
    private const string PowerKeyPath = @"SYSTEM\CurrentControlSet\Control\Power";
    private readonly IServiceControlStore _serviceStore;

    public WindowsStorageTuningStore(IServiceControlStore? serviceStore = null)
    {
        _serviceStore = serviceStore ?? new WindowsServiceControlStore();
    }

    public async Task<StorageTuningSnapshot> CaptureSnapshotAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var is8dot3Disabled = false;
        var isLastAccessDisabled = false;
        using (var key = Registry.LocalMachine.OpenSubKey(FileSystemKeyPath, false))
        {
            if (key is not null)
            {
                var val8dot3 = key.GetValue("NtfsDisable8dot3NameCreation");
                if (val8dot3 is int intVal8dot3)
                {
                    is8dot3Disabled = intVal8dot3 is 1 or 2;
                }

                var valLastAccess = key.GetValue("NtfsDisableLastAccessUpdate");
                if (valLastAccess is int intValLastAccess)
                {
                    isLastAccessDisabled = (intValLastAccess & 1) != 0 || intValLastAccess == 1;
                }
            }
        }

        var isHibernationDisabled = true;
        long hiberfilBytes = 0;
        using (var key = Registry.LocalMachine.OpenSubKey(PowerKeyPath, false))
        {
            if (key is not null)
            {
                var valHibernate = key.GetValue("HibernateEnabled");
                if (valHibernate is int intValHibernate)
                {
                    isHibernationDisabled = intValHibernate == 0;
                }
            }
        }

        var hiberfilPath = Path.Combine(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\", "hiberfil.sys");
        if (File.Exists(hiberfilPath))
        {
            try
            {
                var info = new FileInfo(hiberfilPath);
                hiberfilBytes = info.Length;
                isHibernationDisabled = false;
            }
            catch
            {
                // Access check
            }
        }

        var sysMainService = await _serviceStore.GetServiceAsync("SysMain", cancellationToken);
        var sysMainStartMode = sysMainService?.StartMode ?? ServiceStartMode.Unknown;
        var sysMainState = sysMainService?.State ?? ServiceRunState.Unknown;

        return new StorageTuningSnapshot(
            is8dot3Disabled,
            isLastAccessDisabled,
            isHibernationDisabled,
            hiberfilBytes,
            sysMainStartMode,
            sysMainState);
    }

    public Task Set8dot3DisabledAsync(bool disabled, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var key = Registry.LocalMachine.OpenSubKey(FileSystemKeyPath, true)
            ?? throw new UnauthorizedAccessException("Не удалось открыть раздел реестра FileSystem для записи.");

        key.SetValue("NtfsDisable8dot3NameCreation", disabled ? 1 : 0, RegistryValueKind.DWord);
        return Task.CompletedTask;
    }

    public Task SetLastAccessDisabledAsync(bool disabled, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var key = Registry.LocalMachine.OpenSubKey(FileSystemKeyPath, true)
            ?? throw new UnauthorizedAccessException("Не удалось открыть раздел реестра FileSystem для записи.");

        key.SetValue("NtfsDisableLastAccessUpdate", disabled ? 1 : 0, RegistryValueKind.DWord);
        return Task.CompletedTask;
    }

    public async Task SetHibernationDisabledAsync(bool disabled, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var powercfgPath = Path.Combine(Environment.SystemDirectory, "powercfg.exe");
        var startInfo = new ProcessStartInfo(powercfgPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("-h");
        startInfo.ArgumentList.Add(disabled ? "off" : "on");

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Не удалось запустить утилиту powercfg.");

        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0)
        {
            var err = await process.StandardError.ReadToEndAsync(cancellationToken);
            throw new InvalidOperationException($"powercfg завершился с кодом {process.ExitCode}: {err}");
        }
    }

    public async Task SetSysMainDisabledAsync(bool disabled, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (disabled)
        {
            await _serviceStore.ChangeStartModeAsync("SysMain", ServiceStartMode.Disabled, null, cancellationToken);
            try
            {
                await _serviceStore.StopServiceAsync("SysMain", cancellationToken);
            }
            catch
            {
                // Best effort stop
            }
        }
        else
        {
            await _serviceStore.ChangeStartModeAsync("SysMain", ServiceStartMode.Automatic, null, cancellationToken);
            try
            {
                await _serviceStore.StartServiceAsync("SysMain", cancellationToken);
            }
            catch
            {
                // Best effort start
            }
        }
    }
}
