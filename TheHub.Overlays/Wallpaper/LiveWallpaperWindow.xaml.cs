using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using FlyleafLib;
using FlyleafLib.MediaPlayer;
using TheHub.Common.Config;
using TheHub.Core.Native;

namespace TheHub.Overlays.Wallpaper;

public partial class LiveWallpaperWindow : Window
{
    private static bool _engineStarted;
    private static readonly object _engineLock = new();

    private IntPtr _hwnd = IntPtr.Zero;
    private bool _isAttached = false;
    private bool _isPlaying = false;
    private bool _isPaused = false;
    private bool _isBatteryPaused = false;
    private bool _isCoveragePaused = false;
    private string _fitMode = "Fill";
    private Rectangle _bounds;

    public Player? Player { get; private set; }
    public bool IsLiveActive => _isPlaying && !_isPaused;
    public bool IsBatteryPaused => _isBatteryPaused;
    public bool IsCoveragePaused => _isCoveragePaused;
    public string CurrentFitMode => _fitMode;
    public Rectangle ScreenBounds => _bounds;
    public string DeviceName { get; }
    public event Action<string, string>? MediaPlaybackFailed;

    public static void EnsureFlyleafEngineStarted()
    {
        if (_engineStarted) return;
        lock (_engineLock)
        {
            if (_engineStarted) return;
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string ffmpegDir = Path.Combine(baseDir, "FFmpeg");
                string path = Directory.Exists(ffmpegDir) && File.Exists(Path.Combine(ffmpegDir, "avcodec-63.dll"))
                    ? ffmpegDir
                    : baseDir;

                Engine.Start(new EngineConfig
                {
                    FFmpegPath = path,
                    UIRefreshInterval = 1000 / 60
                });
                _engineStarted = true;
                AppLogger.Log($"[WallpaperEngine] Flyleaf Engine started successfully (FFmpegPath: '{path}')");
            }
            catch (Exception ex)
            {
                AppLogger.Log($"[WallpaperEngine] Flyleaf Engine initialization exception: {ex.Message}");
            }
        }
    }

    public LiveWallpaperWindow(MonitorItem monitor)
        : this(new Rectangle(monitor.Bounds.Left, monitor.Bounds.Top, monitor.Width, monitor.Height), monitor.DeviceName)
    {
    }

    public LiveWallpaperWindow(Rectangle screenBounds, string deviceName = "")
    {
        InitializeComponent();
        _bounds = screenBounds;
        DeviceName = !string.IsNullOrEmpty(deviceName) ? deviceName : "Display";

        // Enforce window properties without setting DIP Width/Height
        // Setting DIP Width/Height causes WPF to clamp window dimensions to secondary screen height
        WindowState = WindowState.Normal;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Focusable = false;

        Loaded += OnLoaded;
    }

    public LiveWallpaperWindow()
        : this(DisplayMonitorHelper.GetAllMonitors().FirstOrDefault() ?? new MonitorItem
        {
            DeviceName = "\\\\.\\DISPLAY1",
            Bounds = new Win32Api.RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1080 }
        })
    {
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).EnsureHandle();

        // Hook WndProc to intercept WM_GETMINMAXINFO to prevent Windows from clamping window dimensions
        var source = HwndSource.FromHwnd(_hwnd);
        source?.AddHook(WndProc);

        ApplyExtendedWindowStyles(_hwnd);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Win32Api.WM_GETMINMAXINFO)
        {
            // Allow window to freely resize to physical bounds without being clamped by virtual screen or secondary monitor height
            var mmi = Marshal.PtrToStructure<Win32Api.MINMAXINFO>(lParam);
            mmi.ptMaxPosition.x = -50000;
            mmi.ptMaxPosition.y = -50000;
            mmi.ptMaxSize.x = 100000;
            mmi.ptMaxSize.y = 100000;
            mmi.ptMaxTrackSize.x = 100000;
            mmi.ptMaxTrackSize.y = 100000;
            Marshal.StructureToPtr(mmi, lParam, true);
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AttachToDesktopWorkerW();
    }

    public void AttachToDesktopWorkerW()
    {
        if (_isAttached) return;

        try
        {
            if (_hwnd == IntPtr.Zero)
            {
                _hwnd = new WindowInteropHelper(this).EnsureHandle();
            }

            // Ensure extended styles are applied prior to SetParent so the window is 
            // completely hidden from Alt+Tab and Win+Tab (Task View)
            ApplyExtendedWindowStyles(_hwnd);

            IntPtr workerW = DesktopWorkerHelper.GetDesktopWorkerW();

            if (workerW != IntPtr.Zero && _hwnd != IntPtr.Zero)
            {
                DesktopWorkerHelper.SetParent(_hwnd, workerW);
                _isAttached = true;
                AppLogger.Log($"LiveWallpaperWindow attached to WorkerW: {workerW} for screen '{DeviceName}' at [{_bounds.X},{_bounds.Y} {_bounds.Width}x{_bounds.Height}]");
            }
            else
            {
                AppLogger.Log("LiveWallpaperWindow: Failed to locate WorkerW window");
            }

            // Immediately enforce exact coordinates and dimensions relative to WorkerW
            DesktopWorkerHelper.SetWallpaperBounds(_hwnd, workerW, _bounds);
        }
        catch (Exception ex)
        {
            AppLogger.Log($"AttachToDesktopWorkerW exception: {ex.Message}");
        }
    }

    private void ApplyExtendedWindowStyles(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        try
        {
            int exStyle = Win32Api.GetWindowLong(hwnd, Win32Api.GWL_EXSTYLE);
            // WS_EX_TOOLWINDOW (0x00000080): Prevents appearing in Alt+Tab and Task View (Win+Tab)
            // WS_EX_NOACTIVATE (0x08000000): Window does not activate or steal focus
            // ~WS_EX_APPWINDOW (~0x00040000): Forces removal from taskbar / Alt+Tab
            exStyle |= Win32Api.WS_EX_TOOLWINDOW;
            exStyle |= Win32Api.WS_EX_NOACTIVATE;
            exStyle &= ~Win32Api.WS_EX_APPWINDOW;
            Win32Api.SetWindowLong(hwnd, Win32Api.GWL_EXSTYLE, exStyle);
            AppLogger.Log($"LiveWallpaperWindow applied extended styles to HWND {hwnd}: 0x{exStyle:X8}");
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Error applying extended window styles: {ex.Message}");
        }
    }

    public void InitializePlayer()
    {
        if (Player != null) return;
        EnsureFlyleafEngineStarted();

        try
        {
            var config = new Config();
            config.Video.VideoAcceleration = true; // Hardware DirectX 11 D3D11VA decoding
            config.Video.AspectRatio = MapFitModeToAspectRatio(_fitMode);
            config.Player.AutoPlay = true;

            Player = new Player(config);
            Player.LoopPlayback = true; // Internal seamless zero-gap loop powered by FFmpeg

            Player.OpenCompleted += (s, e) =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    if (e.Error != null)
                    {
                        string err = $"Flyleaf playback error: {e.Error}";
                        AppLogger.Log($"[WallpaperEngine] Flyleaf Player open error on '{DeviceName}': {err}");
                        MediaPlaybackFailed?.Invoke(DeviceName, err);
                        TeardownAndDestroy();
                        DesktopWorkerHelper.RefreshDesktopWallpaper();
                    }
                    else
                    {
                        _isPlaying = true;
                        _isPaused = false;
                        AppLogger.Log($"[WallpaperEngine] Flyleaf playing video successfully on '{DeviceName}'");
                    }
                });
            };

            FlyleafHostControl.Player = Player;
        }
        catch (Exception ex)
        {
            AppLogger.Log($"[WallpaperEngine] InitializePlayer exception on '{DeviceName}': {ex.Message}");
        }
    }

    public bool PlayVideo(string filePath, bool isMuted = true, double volume = 0.0)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                AppLogger.Log("[WallpaperEngine] PlayVideo error: filePath is empty or null.");
                return false;
            }

            // 1. Resolve full absolute physical path
            string absolutePath = Path.GetFullPath(filePath);

            // 2. Debug Logging for diagnostics
            System.Diagnostics.Debug.WriteLine($"[WallpaperEngine] PlayVideo invoked for screen '{DeviceName}':");
            System.Diagnostics.Debug.WriteLine($"[WallpaperEngine]  -> Resolved absolute path: '{absolutePath}'");
            System.Diagnostics.Debug.WriteLine($"[WallpaperEngine]  -> File.Exists: {File.Exists(absolutePath)}");

            // 3. Verify file exists on physical disk
            if (!File.Exists(absolutePath))
            {
                string notFoundMsg = $"Cannot find the media file: {absolutePath}";
                System.Diagnostics.Debug.WriteLine($"[WallpaperEngine] ERROR: {notFoundMsg}");
                AppLogger.Log($"[WallpaperEngine] PlayVideo error: {notFoundMsg}");
                MediaPlaybackFailed?.Invoke(DeviceName, notFoundMsg);
                return false;
            }

            long fileSizeBytes = new FileInfo(absolutePath).Length;
            System.Diagnostics.Debug.WriteLine($"[WallpaperEngine]  -> File size: {fileSizeBytes:N0} bytes");

            InitializePlayer();

            if (!_isAttached)
            {
                Show();
                AttachToDesktopWorkerW();
            }
            else
            {
                Show();
                IntPtr workerW = DesktopWorkerHelper.GetDesktopWorkerW();
                DesktopWorkerHelper.SetWallpaperBounds(_hwnd, workerW, _bounds);
            }

            if (Player != null)
            {
                Player.Audio.Mute = isMuted;
                Player.Audio.Volume = (int)Math.Clamp(volume * 100, 0, 100);
                Player.Open(absolutePath);
            }

            _isPlaying = true;
            _isPaused = false;

            AppLogger.Log($"[WallpaperEngine] Flyleaf playing video: '{absolutePath}' (Muted: {isMuted}) on screen '{DeviceName}' [{_bounds.X},{_bounds.Y} {_bounds.Width}x{_bounds.Height}]");
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WallpaperEngine] PlayVideo exception: {ex}");
            AppLogger.Log($"PlayVideo exception: {ex.Message}");
            return false;
        }
    }

    public void SetFitMode(string fitMode)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => SetFitMode(fitMode));
            return;
        }

        _fitMode = string.IsNullOrWhiteSpace(fitMode) ? "Fill" : fitMode;
        if (Player != null)
        {
            try
            {
                Player.Config.Video.AspectRatio = MapFitModeToAspectRatio(_fitMode);
                AppLogger.Log($"[WallpaperEngine] LiveWallpaperWindow '{DeviceName}' set fit mode to '{_fitMode}' (AspectRatio: {Player.Config.Video.AspectRatio})");
            }
            catch (Exception ex)
            {
                AppLogger.Log($"[WallpaperEngine] SetFitMode error on '{DeviceName}': {ex.Message}");
            }
        }
    }

    public static AspectRatio MapFitModeToAspectRatio(string fitMode)
    {
        return fitMode?.ToLowerInvariant() switch
        {
            "fit" => AspectRatio.Keep,
            "stretch" => AspectRatio.Custom,
            "fill" => AspectRatio.Fill,
            _ => AspectRatio.Fill
        };
    }

    public void Pause()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(Pause);
            return;
        }

        if (_isPlaying && !_isPaused)
        {
            Player?.Pause();
            _isPaused = true;
            AppLogger.Log($"LiveWallpaperWindow paused on '{DeviceName}'");
        }
    }

    public void Resume()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(Resume);
            return;
        }

        if (_isPlaying && _isPaused)
        {
            Player?.Play();
            _isPaused = false;
            AppLogger.Log($"LiveWallpaperWindow resumed on '{DeviceName}'");
        }
    }

    public void SetBatteryPaused(bool paused)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => SetBatteryPaused(paused));
            return;
        }

        _isBatteryPaused = paused;
        if (paused)
        {
            Pause();
        }
        else if (!_isCoveragePaused)
        {
            Resume();
        }
    }

    public void SetCoveragePaused(bool paused)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => SetCoveragePaused(paused));
            return;
        }

        _isCoveragePaused = paused;
        if (paused)
        {
            Pause();
        }
        else if (!_isBatteryPaused)
        {
            Resume();
        }
    }

    public void StopVideo()
    {
        TeardownAndDestroy();
    }

    public void TeardownAndDestroy()
    {
        try
        {
            _isPlaying = false;
            _isPaused = false;
            _isBatteryPaused = false;
            _isCoveragePaused = false;

            // 1. Stop playback and release Flyleaf Player resources
            if (Player != null)
            {
                try
                {
                    FlyleafHostControl.Player = null;
                    Player.Stop();
                    Player.Dispose();
                }
                catch (Exception pEx)
                {
                    AppLogger.Log($"Flyleaf Player dispose error: {pEx.Message}");
                }
                Player = null;
            }

            // 2. Detach window from WorkerW by unparenting
            if (_isAttached && _hwnd != IntPtr.Zero)
            {
                DesktopWorkerHelper.SetParent(_hwnd, IntPtr.Zero);
                _isAttached = false;
            }

            // 3. Destroy Win32 HWND if valid
            if (_hwnd != IntPtr.Zero)
            {
                DesktopWorkerHelper.DestroyWindow(_hwnd);
                _hwnd = IntPtr.Zero;
            }

            // 4. Close WPF window
            Close();
            AppLogger.Log($"LiveWallpaperWindow TeardownAndDestroy completed for screen '{DeviceName}'");
        }
        catch (Exception ex)
        {
            AppLogger.Log($"LiveWallpaperWindow TeardownAndDestroy exception for '{DeviceName}': {ex.Message}");
        }
    }

    public void SetMuted(bool isMuted)
    {
        if (Player != null)
            Player.Audio.Mute = isMuted;
    }

    public void SetVolume(double volume)
    {
        if (Player != null)
            Player.Audio.Volume = (int)Math.Clamp(volume * 100, 0, 100);
    }
}
