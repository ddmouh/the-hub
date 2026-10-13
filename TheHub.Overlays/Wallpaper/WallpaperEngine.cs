using System.IO;
using FlyleafLib;
using TheHub.Common.Config;
using TheHub.Core.Services;

namespace TheHub.Overlays.Wallpaper;

/// <summary>
/// Encapsulates the FlyleafLib/FFmpeg engine lifecycle, ensuring it is lazily initialized
/// only when animated/video playback is requested, and aggressively trimmed when inactive.
/// </summary>
public static class WallpaperEngine
{
    private static bool _isStarted;
    private static readonly object _syncLock = new();
    private static int _activePlayerCount = 0;

    public static bool IsStarted
    {
        get
        {
            lock (_syncLock) return _isStarted;
        }
    }

    public static int ActivePlayerCount => Volatile.Read(ref _activePlayerCount);

    /// <summary>
    /// Starts the underlying media engine if not already running.
    /// Safe to call concurrently.
    /// </summary>
    public static void EnsureEngineStarted()
    {
        if (_isStarted) return;

        lock (_syncLock)
        {
            if (_isStarted) return;

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

                _isStarted = true;
                AppLogger.Log($"[WallpaperEngine] Flyleaf media engine started lazily (FFmpegPath: '{path}')");
            }
            catch (Exception ex)
            {
                AppLogger.Log($"[WallpaperEngine] Failed to initialize Flyleaf media engine: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Increments the active video player count.
    /// </summary>
    public static void IncrementActivePlayers()
    {
        Interlocked.Increment(ref _activePlayerCount);
    }

    /// <summary>
    /// Decrements the active video player count and triggers memory trimming if 0 players remain.
    /// </summary>
    public static void DecrementActivePlayers()
    {
        int count = Interlocked.Decrement(ref _activePlayerCount);
        if (count <= 0)
        {
            Interlocked.Exchange(ref _activePlayerCount, 0);
            TrimMemoryAfterInactive();
        }
    }

    /// <summary>
    /// Reclaims video memory, collects garbage and reduces working set size when no video wallpapers are active.
    /// </summary>
    public static void TrimMemoryAfterInactive()
    {
        try
        {
            AppLogger.Log("[WallpaperEngine] Inactive state detected. Aggressively trimming process memory...");
            MemoryOptimizer.TrimMemory(force: true);
        }
        catch (Exception ex)
        {
            AppLogger.Log($"[WallpaperEngine] Error trimming memory: {ex.Message}");
        }
    }
}
