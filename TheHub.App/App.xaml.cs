using System.IO;
using System.Linq;
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

        string currentExePath = Environment.ProcessPath ?? "";
        DateTime buildTime = File.Exists(currentExePath) ? File.GetLastWriteTime(currentExePath) : File.GetLastWriteTime(typeof(App).Assembly.Location);
        AppLogger.Log($"App.OnStartup started: ExePath='{currentExePath}', PID={Environment.ProcessId}, BuildTime='{buildTime:yyyy-MM-dd HH:mm:ss}', args=[{string.Join(",", e.Args)}]");

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

        bool isShowcase = e.Args.Any(arg => arg.Equals("--showcase", StringComparison.OrdinalIgnoreCase));
        if (isShowcase)
        {
            AppLogger.Log("App.OnStartup --showcase detected - opening ScreensGalleryWindow and exporting snapshots");
            var galleryWin = new Showcase.ScreensGalleryWindow();
            MainWindow = galleryWin;
            galleryWin.Show();
            galleryWin.Activate();
            galleryWin.Focus();

            _ = Task.Run(async () =>
            {
                try
                {
                    var registry = new Showcase.ViewSnapshotRegistry();
                    await Showcase.ScreenSnapshotExporter.ExportAllAsync(registry.GetAllSnapshots());
                    AppLogger.Log("Automated showcase PNG export completed successfully.");
                }
                catch (Exception ex)
                {
                    AppLogger.Log($"Showcase PNG export error: {ex}");
                }
            });
            return;
        }

        var win = new MainWindow();
        MainWindow = win;

        // Determine if this is a silent autostart (Windows OS boot) - only hide if explicitly requested
        bool isAutostart = e.Args.Any(arg =>
            arg.Equals("--autostart", StringComparison.OrdinalIgnoreCase) ||
            arg.Equals("--minimized", StringComparison.OrdinalIgnoreCase) ||
            arg.Equals("--start-minimized", StringComparison.OrdinalIgnoreCase) ||
            arg.Equals("/autostart", StringComparison.OrdinalIgnoreCase) ||
            arg.Equals("-autostart", StringComparison.OrdinalIgnoreCase) ||
            arg.Equals("-minimized", StringComparison.OrdinalIgnoreCase));

        // Fallback: support values like --autostart=true or any arg containing the keyword
        if (!isAutostart)
        {
            isAutostart = e.Args.Any(arg =>
                arg.IndexOf("autostart", StringComparison.OrdinalIgnoreCase) >= 0 ||
                arg.IndexOf("minimized", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        if (isAutostart)
        {
            AppLogger.Log("App.OnStartup autostart detected - starting minimized to tray (Dashboard hidden)");
            // Initialize window but keep hidden; tray icon remains active and user can open via tray
            // Ensure window is not visible on autostart
            win.Hide();
        }
        else
        {
            // Normal manual launch via desktop .exe shortcut - explicitly show Dashboard in foreground
            win.Show();
            win.WindowState = WindowState.Normal;
            win.Activate();
            win.Focus();
            AppLogger.Log("App.OnStartup win.Show() completed successfully - Dashboard shown in foreground");
        }
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
