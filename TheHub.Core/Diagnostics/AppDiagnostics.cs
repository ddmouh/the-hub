using System.IO;
using System.Windows.Threading;

namespace TheHub.Core.Diagnostics;

public static class AppDiagnostics
{
    private static readonly string LogFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TheHub",
        "app_debug.log");

    private static readonly object FileLock = new();

    public static void Log(string message)
    {
        try
        {
            lock (FileLock)
            {
                var dir = Path.GetDirectoryName(LogFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                string line = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}";
                File.AppendAllText(LogFilePath, line);
            }
        }
        catch
        {
            // Logging must never crash the application
        }
    }

    public static void LogException(string moduleOrSource, Exception ex)
    {
        Breadcrumbs.Add($"exception:{moduleOrSource}: {ex.Message}");
        Log($"[ERROR] [{moduleOrSource}] {ex.GetType().Name}: {ex.Message}{Environment.NewLine}{ex.StackTrace}");
    }

    public static void InstallGlobalHandlers(AppDomain domain, Dispatcher? dispatcher = null)
    {
        domain.UnhandledException += (s, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                LogException("AppDomain.UnhandledException", ex);
            }
        };

        TaskScheduler.UnobservedTaskException += (s, args) =>
        {
            LogException("TaskScheduler.UnobservedTaskException", args.Exception);
            args.SetObserved();
        };

        if (dispatcher != null)
        {
            dispatcher.UnhandledException += (s, args) =>
            {
                LogException("DispatcherUnhandledException", args.Exception);
                args.Handled = true;
            };
        }
    }
}
