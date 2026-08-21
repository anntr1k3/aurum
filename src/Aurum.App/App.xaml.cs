using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace Aurum.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnCurrentDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogCrash(e.Exception, "UI Thread (Dispatcher)");
        ShowCrashDialog(e.Exception);
        e.Handled = true;
    }

    private void OnCurrentDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            LogCrash(ex, "AppDomain UnhandledException");
            ShowCrashDialog(ex);
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogCrash(e.Exception, "TaskScheduler UnobservedTaskException");
        e.SetObserved();
    }

    private static void LogCrash(Exception ex, string source)
    {
        try
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Aurum");
            Directory.CreateDirectory(folder);
            var logPath = Path.Combine(folder, "crash.log");

            var sb = new StringBuilder();
            sb.AppendLine("==================================================");
            sb.AppendLine($"[CRASH REPORT] {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
            sb.AppendLine($"Source: {source}");
            sb.AppendLine($"OS: {Environment.OSVersion} ({(Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit")})");
            sb.AppendLine($".NET Runtime: {Environment.Version}");
            sb.AppendLine($"Exception: {ex.GetType().FullName}");
            sb.AppendLine($"Message: {ex.Message}");
            sb.AppendLine($"Stack Trace:\n{ex.StackTrace}");
            if (ex.InnerException != null)
            {
                sb.AppendLine($"Inner Exception: {ex.InnerException.GetType().FullName}: {ex.InnerException.Message}");
                sb.AppendLine($"Inner Stack Trace:\n{ex.InnerException.StackTrace}");
            }
            sb.AppendLine("==================================================\n");

            File.AppendAllText(logPath, sb.ToString(), Encoding.UTF8);
        }
        catch
        {
            // Defensive: ignore logging errors during crash
        }
    }

    private static void ShowCrashDialog(Exception ex)
    {
        try
        {
            var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Aurum", "crash.log");
            MessageBox.Show(
                $"Произошла непредвиденная ошибка в приложении:\n\n{ex.Message}\n\nПодробности записаны в лог-файл:\n{logPath}",
                "Aurum · Ошибка",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        catch
        {
            // Ignore UI errors during crash dialog
        }
    }
}
