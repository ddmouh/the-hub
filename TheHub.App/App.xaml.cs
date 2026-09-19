using System.IO;
using System.Threading;
using System.Windows;
using TheHub.Common.Config;

namespace TheHub.App;

public partial class App : System.Windows.Application
{
    private const string MutexName = "TheHub_SingleInstance_4A3C9B7E";
    private Mutex? _mutex;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private const int SW_RESTORE = 9;

    protected override void OnStartup(StartupEventArgs e)
    {
        bool createdNew = false;
        try
        {
            _mutex = new Mutex(true, MutexName, out createdNew);
        }
        catch (AbandonedMutexException)
        {
            createdNew = true;
        }

        if (!createdNew)
        {
            var existingProcesses = System.Diagnostics.Process.GetProcessesByName("TheHub")
                .Concat(System.Diagnostics.Process.GetProcessesByName("TheHub.App"))
                .Where(p => p.Id != Environment.ProcessId)
                .ToList();

            if (existingProcesses.Count > 0)
            {
                var processWithWindow = existingProcesses.FirstOrDefault(p => p.MainWindowHandle != IntPtr.Zero);
                if (processWithWindow != null)
                {
                    ShowWindow(processWithWindow.MainWindowHandle, SW_RESTORE);
                    SetForegroundWindow(processWithWindow.MainWindowHandle);
                    Environment.Exit(0);
                    return;
                }

                foreach (var p in existingProcesses)
                {
                    try { p.Kill(); p.WaitForExit(500); } catch { }
                }
            }
        }

        AppLogger.Log("App.OnStartup started");

        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            AppLogger.Log($"UnhandledException: {args.ExceptionObject}");
        };

        DispatcherUnhandledException += (s, args) =>
        {
            AppLogger.Log($"DispatcherUnhandledException: {args.Exception}");
            if (MainWindow == null || !MainWindow.IsVisible)
            {
                System.Windows.MessageBox.Show(
                    $"Failed to start The Hub: {args.Exception.Message}\n\nCheck logs at %APPDATA%\\TheHub\\app_debug.log",
                    "The Hub Startup Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Environment.Exit(1);
            }
            args.Handled = true;
        };

        base.OnStartup(e);
        AppLogger.Log("App.OnStartup base finished");

        ShutdownMode = ShutdownMode.OnMainWindowClose;
        var win = new MainWindow();
        MainWindow = win;
        win.Show();
        win.Activate();
        AppLogger.Log("App.OnStartup win.Show() completed successfully");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AppLogger.Log($"App.OnExit: ApplicationExitCode={e.ApplicationExitCode}");
        try
        {
            (MainWindow as MainWindow)?.WallpaperManager?.DisposeWallpapers();
        }
        catch { }

        try
        {
            _mutex?.ReleaseMutex();
            _mutex?.Dispose();
        }
        catch { }
        base.OnExit(e);
        try
        {
            System.Diagnostics.Process.GetCurrentProcess().Kill();
        }
        catch { }
    }
}
