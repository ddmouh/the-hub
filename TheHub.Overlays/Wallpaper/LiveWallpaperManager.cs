using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;
using TheHub.Common.Config;
using TheHub.Core.Native;
using TheHub.Core.Services;

namespace TheHub.Overlays.Wallpaper;

/// <summary>
/// Manages live animated wallpaper instances across connected physical monitors.
/// Features battery energy saving, per-monitor display pause optimization, and fit modes.
/// </summary>
public class WallpaperManager : IDisposable
{
    private readonly Dictionary<string, LiveWallpaperWindow> _activeWallpapers = new(StringComparer.OrdinalIgnoreCase);
    private bool _isMuted = true;
    private double _volume = 0.0;
    private string _currentFitMode = "Fill";
    private bool _isBatteryOffline = false;
    private readonly DispatcherTimer _autoPauseTimer;
    private bool _disposed = false;

    public bool IsLiveActive => _activeWallpapers.Values.Any(w => w.IsLiveActive);
    public IReadOnlyDictionary<string, LiveWallpaperWindow> ActiveWallpapers => _activeWallpapers;
    public IReadOnlyDictionary<string, LiveWallpaperWindow> ActiveInstances => _activeWallpapers;
    public string CurrentFitMode => _currentFitMode;
    public bool IsBatteryOffline => _isBatteryOffline;
    public event Action<string, string>? WallpaperPlaybackFailed;

    public WallpaperManager()
    {
        _currentFitMode = SettingsManager.Current.WallpaperFitMode ?? "Fill";

        try
        {
            Microsoft.Win32.SystemEvents.PowerModeChanged += OnPowerModeChanged;
        }
        catch (Exception ex)
        {
            AppLogger.Log($"WallpaperManager: Error subscribing to PowerModeChanged: {ex.Message}");
        }

        CheckPowerState();

        // Task 1: Smart Auto-Pause DispatcherTimer at 500ms
        _autoPauseTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _autoPauseTimer.Tick += (s, e) => CheckDisplayOptimizationState();
        _autoPauseTimer.Start();
    }

    public void CheckPowerState()
    {
        try
        {
            if (Win32Api.GetSystemPowerStatus(out var status))
            {
                // ACLineStatus: 0 = Offline (battery), 1 = Online (AC), 255 = Unknown
                bool onBattery = status.ACLineStatus == 0;
                _isBatteryOffline = onBattery;
                bool shouldPause = onBattery && SettingsManager.Current.WallpaperPauseOnBattery;

                foreach (var win in _activeWallpapers.Values)
                {
                    win.SetBatteryPaused(shouldPause);
                }

                AppLogger.Log($"[WallpaperManager] PowerState check: ACLineStatus={status.ACLineStatus}, OnBattery={onBattery}, ShouldPause={shouldPause}");
            }
        }
        catch (Exception ex)
        {
            AppLogger.Log($"[WallpaperManager] CheckPowerState error: {ex.Message}");
        }
    }

    private void OnPowerModeChanged(object sender, Microsoft.Win32.PowerModeChangedEventArgs e)
    {
        if (e.Mode == Microsoft.Win32.PowerModes.StatusChange)
        {
            AppLogger.Log("[WallpaperManager] PowerModeChanged status event received.");
            CheckPowerState();
        }
    }

    public void SetFitMode(string fitMode)
    {
        _currentFitMode = string.IsNullOrWhiteSpace(fitMode) ? "Fill" : fitMode;
        foreach (var win in _activeWallpapers.Values)
        {
            win.SetFitMode(_currentFitMode);
        }
        AppLogger.Log($"[WallpaperManager] Applied fit mode '{_currentFitMode}' to {_activeWallpapers.Count} active wallpaper window(s).");
    }

    /// <summary>
    /// Checks the current foreground window rect and maximized state.
    /// Implements per-screen pause: freezes the player on the covered monitor, while leaving uncovered monitors playing.
    /// </summary>
    public void CheckDisplayOptimizationState()
    {
        if (_disposed || _activeWallpapers.Count == 0) return;

        try
        {
            var settings = SettingsManager.Current;

            // If auto-pause on fullscreen/maximized is disabled, ensure any coverage pause is released
            if (!settings.WallpaperPauseOnFullscreen)
            {
                foreach (var win in _activeWallpapers.Values)
                {
                    if (win.IsCoveragePaused)
                    {
                        win.SetCoveragePaused(false);
                    }
                }
                return;
            }

            // Find foreground window
            IntPtr foreground = Win32Api.GetForegroundWindow();
            if (foreground == IntPtr.Zero)
            {
                ResumeAllCoverage();
                return;
            }

            // If foreground window is desktop, shell, or WorkerW: desktop is visible, so resume all
            var desktop = Win32Api.GetDesktopWindow();
            var shell = Win32Api.GetShellWindow();
            if (foreground == desktop || foreground == shell)
            {
                ResumeAllCoverage();
                return;
            }

            var classNameSb = new StringBuilder(256);
            Win32Api.GetClassName(foreground, classNameSb, classNameSb.Capacity);
            string className = classNameSb.ToString();
            if (className == "Progman" ||
                className == "WorkerW" ||
                className == "Shell_TrayWnd" ||
                className == "Shell_SecondaryTrayWnd")
            {
                ResumeAllCoverage();
                return;
            }

            if (!Win32Api.IsWindowVisible(foreground))
            {
                ResumeAllCoverage();
                return;
            }

            // If window is minimized, it does not cover any screen
            if (Win32Api.IsIconic(foreground))
            {
                ResumeAllCoverage();
                return;
            }

            int style = Win32Api.GetWindowLong(foreground, Win32Api.GWL_STYLE);
            if ((style & Win32Api.WS_MINIMIZE) != 0)
            {
                ResumeAllCoverage();
                return;
            }

            // Find monitor of foreground window
            IntPtr foregroundMonitor = Win32Api.MonitorFromWindow(foreground, Win32Api.MONITOR_DEFAULTTONEAREST);
            if (foregroundMonitor == IntPtr.Zero)
            {
                ResumeAllCoverage();
                return;
            }

            var mi = new DisplayMonitorHelper.MONITORINFOEX();
            mi.cbSize = Marshal.SizeOf<DisplayMonitorHelper.MONITORINFOEX>();
            if (!DisplayMonitorHelper.GetMonitorInfo(foregroundMonitor, ref mi))
            {
                ResumeAllCoverage();
                return;
            }

            string activeMonitorDevice = mi.szDevice ?? string.Empty;

            // Determine if window covers this monitor:
            // 1. IsZoomed (Maximized window)
            // 2. Window covers entire physical monitor bounds (Fullscreen game / borderless app)
            bool isZoomed = Win32Api.IsZoomed(foreground);
            bool isFullscreen = false;
            if (!isZoomed && Win32Api.GetWindowRect(foreground, out var rect))
            {
                isFullscreen = (rect.Left <= mi.rcMonitor.Left + 1 &&
                                rect.Top <= mi.rcMonitor.Top + 1 &&
                                rect.Right >= mi.rcMonitor.Right - 1 &&
                                rect.Bottom >= mi.rcMonitor.Bottom - 1);
            }

            bool coversMonitor = isZoomed || isFullscreen;

            // Apply per-screen pause optimization:
            foreach (var kvp in _activeWallpapers)
            {
                string deviceName = kvp.Key;
                var win = kvp.Value;

                bool isTargetScreen = string.Equals(deviceName, activeMonitorDevice, StringComparison.OrdinalIgnoreCase)
                    || (win.ScreenBounds.Left == mi.rcMonitor.Left && win.ScreenBounds.Top == mi.rcMonitor.Top);

                if (isTargetScreen)
                {
                    if (coversMonitor && !win.IsCoveragePaused)
                    {
                        win.SetCoveragePaused(true);
                        AppLogger.Log($"[WallpaperManager] Auto-paused screen '{deviceName}' (Foreground window '{className}' covers monitor).");
                    }
                    else if (!coversMonitor && win.IsCoveragePaused)
                    {
                        win.SetCoveragePaused(false);
                        AppLogger.Log($"[WallpaperManager] Auto-resumed screen '{deviceName}' (Window no longer covers monitor).");
                    }
                }
                else
                {
                    // Non-covered screens continue playing
                    if (win.IsCoveragePaused)
                    {
                        win.SetCoveragePaused(false);
                    }
                }
            }
        }
        catch
        {
            // Transient polling catch
        }
    }

    private void ResumeAllCoverage()
    {
        foreach (var win in _activeWallpapers.Values)
        {
            if (win.IsCoveragePaused)
            {
                win.SetCoveragePaused(false);
            }
        }
    }

    /// <summary>
    /// Launches independent live wallpaper instances across all connected physical monitors (Duplicate / All mode).
    /// </summary>
    public void PlayVideoOnAll(string filePath, bool isMuted = true, double volume = 0.0)
    {
        _isMuted = isMuted;
        _volume = volume;

        if (string.IsNullOrWhiteSpace(filePath))
        {
            AppLogger.Log("WallpaperManager: File path is null or empty.");
            return;
        }

        string absolutePath = Path.GetFullPath(filePath);
        System.Diagnostics.Debug.WriteLine($"[WallpaperManager] PlayVideoOnAll requested with path: '{absolutePath}'");
        AppLogger.Log($"WallpaperManager: PlayVideoOnAll resolving path: '{absolutePath}' (Exists: {File.Exists(absolutePath)})");

        if (!File.Exists(absolutePath))
        {
            string err = $"Cannot find the media file: {absolutePath}";
            System.Diagnostics.Debug.WriteLine($"[WallpaperManager] ERROR: {err}");
            AppLogger.Log($"WallpaperManager: {err}");
            WallpaperPlaybackFailed?.Invoke("All", err);
            return;
        }

        // Cleanly dispose existing instances before recreating
        DisposeWallpapers();

        var monitors = DisplayMonitorHelper.GetAllMonitors();
        AppLogger.Log($"WallpaperManager: Found {monitors.Count} physical monitor(s) for All Displays mode.");

        if (monitors.Count == 0)
        {
            var defaultWin = new LiveWallpaperWindow();
            defaultWin.SetFitMode(_currentFitMode);
            defaultWin.SetBatteryPaused(_isBatteryOffline && SettingsManager.Current.WallpaperPauseOnBattery);
            defaultWin.Show();
            defaultWin.PlayVideo(absolutePath, isMuted, volume);
            _activeWallpapers["Primary"] = defaultWin;
            TriggerMemoryTrimPostPlayback();
            return;
        }

        foreach (var monitor in monitors)
        {
            try
            {
                var win = new LiveWallpaperWindow(monitor);
                win.SetFitMode(_currentFitMode);
                win.SetBatteryPaused(_isBatteryOffline && SettingsManager.Current.WallpaperPauseOnBattery);
                win.MediaPlaybackFailed += (dev, err) =>
                {
                    _activeWallpapers.Remove(dev);
                    WallpaperPlaybackFailed?.Invoke(dev, err);
                };
                win.Show();
                win.PlayVideo(absolutePath, isMuted, volume);
                _activeWallpapers[monitor.DeviceName] = win;

                AppLogger.Log($"WallpaperManager: Created wallpaper window for '{monitor.DeviceName}' [{monitor.Width}x{monitor.Height} at ({monitor.Bounds.Left},{monitor.Bounds.Top})] FitMode: {_currentFitMode}");
            }
            catch (Exception ex)
            {
                AppLogger.Log($"WallpaperManager error creating screen window for {monitor.DeviceName}: {ex.Message}");
            }
        }

        TriggerMemoryTrimPostPlayback();
    }

    /// <summary>
    /// Applies an independent live wallpaper to a single specific screen (Individual mode).
    /// Closes active wallpaper windows on other screens and refreshes the desktop to clear inactive displays.
    /// </summary>
    public void PlayVideoOnMonitor(string deviceName, string filePath, bool isMuted = true, double volume = 0.0)
    {
        _isMuted = isMuted;
        _volume = volume;

        if (string.IsNullOrWhiteSpace(filePath))
        {
            AppLogger.Log("WallpaperManager: File path is null or empty.");
            return;
        }

        string absolutePath = Path.GetFullPath(filePath);
        System.Diagnostics.Debug.WriteLine($"[WallpaperManager] PlayVideoOnMonitor '{deviceName}' requested with path: '{absolutePath}'");
        AppLogger.Log($"WallpaperManager: PlayVideoOnMonitor for '{deviceName}' resolving path: '{absolutePath}' (Exists: {File.Exists(absolutePath)})");

        if (!File.Exists(absolutePath))
        {
            string err = $"Cannot find the media file: {absolutePath}";
            System.Diagnostics.Debug.WriteLine($"[WallpaperManager] ERROR: {err}");
            AppLogger.Log($"WallpaperManager: {err}");
            WallpaperPlaybackFailed?.Invoke(deviceName, err);
            return;
        }

        var monitors = DisplayMonitorHelper.GetAllMonitors();
        var targetMonitor = monitors.FirstOrDefault(s => string.Equals(s.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase))
                           ?? monitors.FirstOrDefault();

        if (targetMonitor == null)
        {
            AppLogger.Log($"WallpaperManager: Target monitor '{deviceName}' not found.");
            return;
        }

        string targetDevice = targetMonitor.DeviceName;

        // 1. Close windows on all OTHER screens
        var keysToRemove = _activeWallpapers.Keys.Where(k => !string.Equals(k, targetDevice, StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var key in keysToRemove)
        {
            if (_activeWallpapers.TryGetValue(key, out var win))
            {
                try { win.TeardownAndDestroy(); } catch { }
                _activeWallpapers.Remove(key);
            }
        }

        // 2. If target screen already has an active instance, stop and destroy it first
        if (_activeWallpapers.TryGetValue(targetDevice, out var existing))
        {
            try { existing.TeardownAndDestroy(); } catch { }
            _activeWallpapers.Remove(targetDevice);
        }

        // 3. Create independent window for the target screen with exact physical bounds
        try
        {
            var win = new LiveWallpaperWindow(targetMonitor);
            win.SetFitMode(_currentFitMode);
            win.SetBatteryPaused(_isBatteryOffline && SettingsManager.Current.WallpaperPauseOnBattery);
            win.MediaPlaybackFailed += (dev, err) =>
            {
                _activeWallpapers.Remove(dev);
                WallpaperPlaybackFailed?.Invoke(dev, err);
            };
            win.Show();
            win.PlayVideo(absolutePath, isMuted, volume);
            _activeWallpapers[targetDevice] = win;

            AppLogger.Log($"WallpaperManager: Created individual wallpaper window for '{targetDevice}' [{targetMonitor.Width}x{targetMonitor.Height} at ({targetMonitor.Bounds.Left},{targetMonitor.Bounds.Top})] FitMode: {_currentFitMode}");
        }
        catch (Exception ex)
        {
            AppLogger.Log($"WallpaperManager error creating individual window for {targetDevice}: {ex.Message}");
        }

        // 4. Refresh desktop wallpaper so inactive displays immediately clear any video remnants
        DesktopWorkerHelper.RefreshDesktopWallpaper();

        TriggerMemoryTrimPostPlayback();
    }

    private static void TriggerMemoryTrimPostPlayback()
    {
        // Flush buffer allocations and trim memory 1.5s after video starts
        _ = Task.Delay(1500).ContinueWith(_ => MemoryOptimizer.TrimMemory());
    }

    /// <summary>
    /// Legacy/convenience method: plays video across all displays.
    /// </summary>
    public void PlayVideo(string filePath, bool isMuted = true, double volume = 0.0)
    {
        PlayVideoOnAll(filePath, isMuted, volume);
    }

    /// <summary>
    /// Stops and removes the live wallpaper instance on a specific screen.
    /// </summary>
    public void StopMonitor(string deviceName)
    {
        if (_activeWallpapers.TryGetValue(deviceName, out var win))
        {
            try { win.TeardownAndDestroy(); } catch { }
            _activeWallpapers.Remove(deviceName);
            AppLogger.Log($"WallpaperManager: Stopped wallpaper on monitor '{deviceName}'. Remaining active: {_activeWallpapers.Count}");
        }

        DesktopWorkerHelper.RefreshDesktopWallpaper();
        MemoryOptimizer.TrimMemory();
    }

    /// <summary>
    /// Central teardown mechanism that unparents all windows from WorkerW, destroys their HWNDs,
    /// closes the windows, and forces Windows Explorer to repaint the original desktop wallpaper.
    /// </summary>
    public void DisposeWallpapers()
    {
        AppLogger.Log($"WallpaperManager.DisposeWallpapers: Cleaning up {_activeWallpapers.Count} active wallpaper window(s)...");

        foreach (var kvp in _activeWallpapers)
        {
            try
            {
                kvp.Value.TeardownAndDestroy();
            }
            catch (Exception ex)
            {
                AppLogger.Log($"WallpaperManager error disposing window for {kvp.Key}: {ex.Message}");
            }
        }
        _activeWallpapers.Clear();

        // Repaint desktop wallpaper to clean any WorkerW residue and restore original wallpaper
        DesktopWorkerHelper.RefreshDesktopWallpaper();
        AppLogger.Log("WallpaperManager.DisposeWallpapers: All windows destroyed and desktop refreshed.");
    }

    /// <summary>
    /// Stops all wallpaper instances and refreshes the desktop.
    /// </summary>
    public void StopVideo()
    {
        DisposeWallpapers();
        MemoryOptimizer.TrimMemory();
    }

    public void Pause()
    {
        foreach (var win in _activeWallpapers.Values)
        {
            win.Pause();
        }
    }

    public void Resume()
    {
        foreach (var win in _activeWallpapers.Values)
        {
            win.Resume();
        }
    }

    public void SetMuted(bool isMuted)
    {
        _isMuted = isMuted;
        foreach (var win in _activeWallpapers.Values)
        {
            win.SetMuted(isMuted);
        }
    }

    public void SetVolume(double volume)
    {
        _volume = volume;
        foreach (var win in _activeWallpapers.Values)
        {
            win.SetVolume(volume);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            Microsoft.Win32.SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        }
        catch { }

        try
        {
            _autoPauseTimer.Stop();
        }
        catch { }

        DisposeWallpapers();
        MemoryOptimizer.TrimMemory(force: true);
    }
}

/// <summary>
/// Alias class for backwards compatibility.
/// </summary>
public class LiveWallpaperManager : WallpaperManager
{
}
