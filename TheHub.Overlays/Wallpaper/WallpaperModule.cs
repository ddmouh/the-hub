using System.IO;
using Microsoft.Win32;
using TheHub.Common.Config;
using TheHub.Common.Models;
using TheHub.Core.Bridge;
using TheHub.Core.Bus;
using TheHub.Core.Modules;
using TheHub.Core.Services;

namespace TheHub.Overlays.Wallpaper;

/// <summary>
/// Hub module responsible for desktop wallpapers (live video & static).
/// Integrates with HubBus, PowerPolicy, multi-monitor configuration, and lazy engine lifecycle.
/// </summary>
public sealed class WallpaperModule : IHubModule
{
    private readonly IHubBus _bus;
    private readonly UiBridge _uiBridge;
    private readonly WallpaperManager _wallpaperManager;
    private readonly List<IDisposable> _subscriptions = new();
    private readonly HashSet<SuspendReason> _activeSuspendReasons = new();
    private readonly object _lock = new();
    private bool _isDisposed;

    public string Id => "wallpaper";
    public ModuleState State { get; private set; } = ModuleState.Stopped;
    public WallpaperManager WallpaperManager => _wallpaperManager;
    public IReadOnlySet<SuspendReason> ActiveSuspendReasons
    {
        get
        {
            lock (_lock) return _activeSuspendReasons.ToHashSet();
        }
    }
    public bool IsPaused
    {
        get
        {
            lock (_lock) return _activeSuspendReasons.Count > 0;
        }
    }

    public WallpaperModule(IHubBus bus, UiBridge uiBridge, WallpaperManager? wallpaperManager = null)
    {
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        _uiBridge = uiBridge ?? throw new ArgumentNullException(nameof(uiBridge));
        _wallpaperManager = wallpaperManager ?? new WallpaperManager();
    }

    public Task StartAsync(CancellationToken ct = default)
    {
        if (State == ModuleState.Running) return Task.CompletedTask;

        try
        {
            _subscriptions.Add(_bus.Subscribe<FullscreenChanged>(OnFullscreenChanged));
            _subscriptions.Add(_bus.Subscribe<PowerStateChanged>(OnPowerStateChanged));
            _subscriptions.Add(_bus.Subscribe<WorkspaceChanged>(OnWorkspaceChanged));
            _subscriptions.Add(_bus.Subscribe<SettingsChanged>(OnSettingsChanged));

            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

            State = ModuleState.Running;
            AppLogger.Log("[WallpaperModule] Started successfully.");
        }
        catch (Exception ex)
        {
            State = ModuleState.Faulted;
            AppLogger.Log($"[WallpaperModule] Failed to start: {ex.Message}");
            throw;
        }

        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        if (State == ModuleState.Stopped) return Task.CompletedTask;

        try
        {
            foreach (var sub in _subscriptions)
            {
                sub.Dispose();
            }
            _subscriptions.Clear();

            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;

            _wallpaperManager.StopVideo();
            lock (_lock)
            {
                _activeSuspendReasons.Clear();
            }

            State = ModuleState.Stopped;
            AppLogger.Log("[WallpaperModule] Stopped successfully.");
        }
        catch (Exception ex)
        {
            AppLogger.Log($"[WallpaperModule] Error stopping: {ex.Message}");
        }

        return Task.CompletedTask;
    }

    public void Suspend(SuspendReason reason)
    {
        lock (_lock)
        {
            _activeSuspendReasons.Add(reason);
        }

        _wallpaperManager.Pause();
        State = ModuleState.Suspended;
        AppLogger.Log($"[WallpaperModule] Suspended due to reason: {reason}. Active reasons: {string.Join(", ", _activeSuspendReasons)}");
    }

    public void Resume(SuspendReason reason)
    {
        bool canResume = false;
        lock (_lock)
        {
            _activeSuspendReasons.Remove(reason);
            canResume = _activeSuspendReasons.Count == 0;
        }

        if (canResume)
        {
            _wallpaperManager.Resume();
            State = ModuleState.Running;
            AppLogger.Log($"[WallpaperModule] Resumed after clearing reason: {reason}. All reasons cleared.");
        }
        else
        {
            AppLogger.Log($"[WallpaperModule] Cleared reason {reason}, but still suspended due to: {string.Join(", ", _activeSuspendReasons)}");
        }
    }

    private void OnFullscreenChanged(FullscreenChanged evt)
    {
        var settings = SettingsManager.Current;
        if (evt.IsFullscreen && (settings.WallpaperPauseOnFullscreen || settings.PauseGraphicsOnFullscreen))
        {
            Suspend(SuspendReason.Fullscreen);
        }
        else if (!evt.IsFullscreen)
        {
            Resume(SuspendReason.Fullscreen);
        }
    }

    private void OnPowerStateChanged(PowerStateChanged evt)
    {
        var settings = SettingsManager.Current;
        if (evt.IsOnBattery && (settings.WallpaperPauseOnBattery || settings.LowPowerMode))
        {
            Suspend(SuspendReason.Battery);
        }
        else if (!evt.IsOnBattery)
        {
            Resume(SuspendReason.Battery);
        }
    }

    private void OnWorkspaceChanged(WorkspaceChanged evt)
    {
        // When workspace changes, check if the active workspace requires pausing or wallpaper update
        var currentWs = SettingsManager.Current.Workspaces.FirstOrDefault(w => string.Equals(w.Name, evt.WorkspaceName, StringComparison.OrdinalIgnoreCase));
        if (currentWs != null)
        {
            if (currentWs.PauseWallpapers)
            {
                Suspend(SuspendReason.Workspace);
            }
            else
            {
                Resume(SuspendReason.Workspace);
            }
        }
    }

    private void OnSettingsChanged(SettingsChanged evt)
    {
        if (evt.SectionName.Equals("wallpaper", StringComparison.OrdinalIgnoreCase) ||
            evt.SectionName.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            var settings = SettingsManager.Current;
            _wallpaperManager.SetFitMode(settings.WallpaperFitMode);
            _wallpaperManager.SetMuted(settings.WallpaperMuted);
        }
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        AppLogger.Log("[WallpaperModule] Display settings changed (resolution or monitor connection change). Re-evaluating displays...");
        _uiBridge.Post(() =>
        {
            try
            {
                _wallpaperManager.CheckDisplayOptimizationState();
            }
            catch (Exception ex)
            {
                AppLogger.Log($"[WallpaperModule] Error handling DisplaySettingsChanged: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Applies a wallpaper item (static or video) to a target monitor or all monitors.
    /// Handles missing files gracefully.
    /// </summary>
    public bool ApplyWallpaper(WallpaperItem item, string targetMonitor = "ALL")
    {
        if (item == null) return false;

        string path = item.FilePath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            string fallback = Path.Combine(WallpaperService.GetWallpapersStorageDirectory(), Path.GetFileName(path ?? ""));
            if (File.Exists(fallback))
            {
                path = fallback;
                item.FilePath = fallback;
            }
            else
            {
                AppLogger.Log($"[WallpaperModule] Missing wallpaper file: '{item.FilePath}'. Aborting apply.");
                item.NotifyStateChanged();
                return false;
            }
        }

        var settings = SettingsManager.Current;
        if (item.Type == WallpaperType.Animated)
        {
            if (string.Equals(targetMonitor, "ALL", StringComparison.OrdinalIgnoreCase))
            {
                _wallpaperManager.PlayVideoOnAll(path, settings.WallpaperMuted, 0.0);
            }
            else
            {
                _wallpaperManager.PlayVideoOnMonitor(targetMonitor, path, settings.WallpaperMuted, 0.0);
            }
        }
        else
        {
            // Static wallpaper
            _wallpaperManager.StopVideo();
            WallpaperService.ApplyStaticWallpaper(path);
        }

        item.IsActive = true;
        item.NotifyStateChanged();
        return true;
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        _ = StopAsync();
        _wallpaperManager.Dispose();
    }
}
