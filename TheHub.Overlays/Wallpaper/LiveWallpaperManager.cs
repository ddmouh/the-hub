using System.Drawing;
using System.IO;
using TheHub.Common.Config;
using TheHub.Core.Native;

namespace TheHub.Overlays.Wallpaper;

/// <summary>
/// Manages live animated wallpaper instances across connected physical monitors.
/// Holds a centralized dictionary of active windows keyed by monitor DeviceName.
/// </summary>
public class WallpaperManager
{
    private readonly Dictionary<string, LiveWallpaperWindow> _activeWallpapers = new(StringComparer.OrdinalIgnoreCase);
    private bool _isMuted = true;
    private double _volume = 0.0;

    public bool IsLiveActive => _activeWallpapers.Values.Any(w => w.IsLiveActive);
    public IReadOnlyDictionary<string, LiveWallpaperWindow> ActiveWallpapers => _activeWallpapers;
    public IReadOnlyDictionary<string, LiveWallpaperWindow> ActiveInstances => _activeWallpapers;
    public event Action<string, string>? WallpaperPlaybackFailed;

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
            defaultWin.Show();
            defaultWin.PlayVideo(absolutePath, isMuted, volume);
            _activeWallpapers["Primary"] = defaultWin;
            return;
        }

        foreach (var monitor in monitors)
        {
            try
            {
                var win = new LiveWallpaperWindow(monitor);
                win.MediaPlaybackFailed += (dev, err) =>
                {
                    _activeWallpapers.Remove(dev);
                    WallpaperPlaybackFailed?.Invoke(dev, err);
                };
                win.Show();
                win.PlayVideo(absolutePath, isMuted, volume);
                _activeWallpapers[monitor.DeviceName] = win;

                AppLogger.Log($"WallpaperManager: Created wallpaper window for '{monitor.DeviceName}' [{monitor.Width}x{monitor.Height} at ({monitor.Bounds.Left},{monitor.Bounds.Top})]");
            }
            catch (Exception ex)
            {
                AppLogger.Log($"WallpaperManager error creating screen window for {monitor.DeviceName}: {ex.Message}");
            }
        }
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
            win.MediaPlaybackFailed += (dev, err) =>
            {
                _activeWallpapers.Remove(dev);
                WallpaperPlaybackFailed?.Invoke(dev, err);
            };
            win.Show();
            win.PlayVideo(absolutePath, isMuted, volume);
            _activeWallpapers[targetDevice] = win;

            AppLogger.Log($"WallpaperManager: Created individual wallpaper window for '{targetDevice}' [{targetMonitor.Width}x{targetMonitor.Height} at ({targetMonitor.Bounds.Left},{targetMonitor.Bounds.Top})]");
        }
        catch (Exception ex)
        {
            AppLogger.Log($"WallpaperManager error creating individual window for {targetDevice}: {ex.Message}");
        }

        // 4. Refresh desktop wallpaper so inactive displays immediately clear any video remnants
        DesktopWorkerHelper.RefreshDesktopWallpaper();
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
}

/// <summary>
/// Alias class for backwards compatibility.
/// </summary>
public class LiveWallpaperManager : WallpaperManager
{
}
