using System.Windows;
using System.Windows.Interop;
using TheHub.Common.Config;
using TheHub.Common.Models;
using TheHub.Core.Bridge;
using TheHub.Core.Bus;
using TheHub.Core.Modules;
using TheHub.Core.Monitors;

namespace TheHub.Overlays.DynamicIsland;

/// <summary>
/// Dynamic Island Module implementing IHubModule lifecycle.
/// Manages DynamicIslandWindow creation, positioning, and lifecycle strictly through UiBridge.
/// Subscribes to HubBus events to render Volume, Media, Usb, Downloads, Prayer, Pomodoro, and Workspace alerts.
/// </summary>
public class IslandModule : IHubModule
{
    public string Id => "DynamicIsland";
    public ModuleState State { get; private set; } = ModuleState.Stopped;

    private readonly IHubBus _bus;
    private readonly UiBridge _uiBridge;
    private DynamicIslandWindow? _window;
    private readonly List<IDisposable> _subscriptions = new();
    private bool _isDisposed;

    public event Action? OpenSettingsRequested;
    public event Func<Task>? PlayPauseRequested;
    public event Func<Task>? NextRequested;
    public event Func<Task>? PrevRequested;

    public IslandModule(IHubBus? bus = null, UiBridge? uiBridge = null)
    {
        _bus = bus ?? HubBus.Default;
        _uiBridge = uiBridge ?? UiBridge.Instance;
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (State == ModuleState.Running) return;

        await _uiBridge.InvokeAsync(() =>
        {
            if (_window == null)
            {
                _window = new DynamicIslandWindow();
                _window.OpenSettingsRequested += () => OpenSettingsRequested?.Invoke();
                _window.PlayPauseRequested += () => PlayPauseRequested != null ? PlayPauseRequested() : Task.CompletedTask;
                _window.NextRequested += () => NextRequested != null ? NextRequested() : Task.CompletedTask;
                _window.PrevRequested += () => PrevRequested != null ? PrevRequested() : Task.CompletedTask;
                _window.SetWorkspaces(SettingsManager.Current.Workspaces);
            }
            _window.PositionWindow();
            _window.Show();
        });

        SubscribeToEvents();
        State = ModuleState.Running;
    }

    public async Task StopAsync()
    {
        if (State == ModuleState.Stopped) return;

        UnsubscribeFromEvents();

        await _uiBridge.InvokeAsync(() =>
        {
            try
            {
                _window?.Hide();
            }
            catch { }
        });

        State = ModuleState.Stopped;
    }

    public void Suspend(SuspendReason reason)
    {
        if (State != ModuleState.Running) return;

        _uiBridge.Post(() =>
        {
            try
            {
                _window?.Hide();
            }
            catch { }
        });

        State = ModuleState.Suspended;
    }

    public void Resume(SuspendReason reason)
    {
        if (State != ModuleState.Suspended) return;

        _uiBridge.Post(() =>
        {
            try
            {
                _window?.PositionWindow();
                _window?.Show();
            }
            catch { }
        });

        State = ModuleState.Running;
    }

    public void ToggleVisibility(bool? show = null)
    {
        _uiBridge.Post(() =>
        {
            if (_window == null) return;
            bool shouldShow = show ?? (_window.Visibility != Visibility.Visible);
            if (shouldShow)
            {
                _window.PositionWindow();
                _window.Show();
            }
            else
            {
                _window.Hide();
            }
        });
    }

    public void UpdateLayout()
    {
        _uiBridge.Post(() =>
        {
            _window?.PositionWindow();
            _window?.UpdateLayoutSettings();
        });
    }

    public void ShowSettingsPreview()
    {
        _uiBridge.Post(() =>
        {
            _window?.ShowSettingsPreview();
        });
    }

    public void SetFontFamily(System.Windows.Media.FontFamily fontFamily)
    {
        _uiBridge.Post(() =>
        {
            if (_window != null)
            {
                _window.FontFamily = fontFamily;
            }
        });
    }

    public void PostEvent(DynamicIslandEvent evt)
    {
        _uiBridge.Post(() =>
        {
            _window?.PostEvent(evt);
        });
    }

    public void ResetPositionToCenter()
    {
        _uiBridge.Post(() =>
        {
            _window?.ResetPositionToCenter();
        });
    }

    public void SetWorkspaces(IEnumerable<WorkspaceConfig> workspaces)
    {
        _uiBridge.Post(() =>
        {
            _window?.SetWorkspaces(workspaces);
        });
    }

    public IntPtr GetWindowHandle()
    {
        if (_window == null) return IntPtr.Zero;
        return new WindowInteropHelper(_window).EnsureHandle();
    }

    private void SubscribeToEvents()
    {
        UnsubscribeFromEvents();

        _subscriptions.Add(_bus.Subscribe<VolumeChanged>(evt =>
        {
            _uiBridge.Post(() =>
            {
                if (_window == null) return;
                string glyph = evt.IsMuted ? "🔇" : (evt.VolumePercent == 0 ? "🔈" : (evt.VolumePercent > 50 ? "🔊" : "🔉"));
                string subtitle = evt.IsMuted ? "Muted" : $"{evt.VolumePercent}%";
                _window.PostEvent(new DynamicIslandEvent
                {
                    Type = IslandEventType.Volume,
                    Title = evt.IsMuted ? "Volume Muted" : "Volume",
                    Subtitle = subtitle,
                    Glyph = glyph,
                    Value = evt.VolumePercent,
                    DisplayDurationMs = 2200
                });
            });
        }));

        _subscriptions.Add(_bus.Subscribe<MediaChanged>(evt =>
        {
            _uiBridge.Post(() =>
            {
                if (_window == null || string.IsNullOrWhiteSpace(evt.Title)) return;
                _window.PostEvent(new DynamicIslandEvent
                {
                    Type = IslandEventType.Media,
                    Title = evt.Title,
                    Subtitle = string.IsNullOrWhiteSpace(evt.Artist) ? "Now Playing" : evt.Artist,
                    Glyph = evt.Glyph ?? "🎵",
                    IsPlaying = evt.IsPlaying,
                    IsPersistent = evt.IsPlaying,
                    DisplayDurationMs = evt.IsPlaying ? 4000 : 2500
                });
            });
        }));

        _subscriptions.Add(_bus.Subscribe<ClipboardChanged>(evt =>
        {
            _uiBridge.Post(() =>
            {
                if (_window == null) return;
                _window.PostEvent(new DynamicIslandEvent
                {
                    Type = IslandEventType.Clipboard,
                    Title = "Copied to Clipboard",
                    Subtitle = evt.TextPreview,
                    Glyph = "📋",
                    DisplayDurationMs = SettingsManager.Current.IslandDurationMs
                });
            });
        }));

        _subscriptions.Add(_bus.Subscribe<UsbChanged>(evt =>
        {
            _uiBridge.Post(() =>
            {
                if (_window == null || !SettingsManager.Current.IslandShowUsb) return;
                _window.PostEvent(new DynamicIslandEvent
                {
                    Type = IslandEventType.Usb,
                    Title = evt.IsConnected ? "Device Connected" : "Device Removed",
                    Subtitle = evt.DeviceName,
                    Glyph = evt.IsConnected ? "🔌" : "⏏️",
                    DisplayDurationMs = 3000
                });
            });
        }));

        _subscriptions.Add(_bus.Subscribe<DownloadProgress>(evt =>
        {
            _uiBridge.Post(() =>
            {
                if (_window == null) return;
                _window.PostEvent(new DynamicIslandEvent
                {
                    Type = IslandEventType.SystemAlert,
                    Title = evt.IsCompleted ? "Download Complete" : "Downloading...",
                    Subtitle = evt.FileName,
                    Glyph = evt.IsCompleted ? "✅" : "⬇️",
                    Value = evt.ProgressPercent,
                    DisplayDurationMs = evt.IsCompleted ? 3000 : 2500
                });
            });
        }));

        _subscriptions.Add(_bus.Subscribe<PrayerTimeDue>(evt =>
        {
            _uiBridge.Post(() =>
            {
                if (_window == null) return;
                _window.PostEvent(new DynamicIslandEvent
                {
                    Type = IslandEventType.SystemAlert,
                    Title = $"Prayer Time: {evt.PrayerName}",
                    Subtitle = evt.TimeString,
                    Glyph = "🕌",
                    DisplayDurationMs = 5000
                });
            });
        }));

        _subscriptions.Add(_bus.Subscribe<PomodoroTick>(evt =>
        {
            _uiBridge.Post(() =>
            {
                if (_window == null) return;
                int min = evt.RemainingSeconds / 60;
                int sec = evt.RemainingSeconds % 60;
                _window.PostEvent(new DynamicIslandEvent
                {
                    Type = IslandEventType.Timer,
                    Title = $"Pomodoro ({evt.Phase})",
                    Subtitle = $"{min:D2}:{sec:D2}",
                    Glyph = "⏱️",
                    IsPersistent = true,
                    DisplayDurationMs = 2000
                });
            });
        }));

        _subscriptions.Add(_bus.Subscribe<ActiveAppChanged>(evt =>
        {
            _uiBridge.Post(() =>
            {
                if (_window == null || !SettingsManager.Current.IslandShowActiveApp) return;
                _window.SetAmbientStatus(new AmbientStatusInfo("⚡", evt.WindowTitle ?? evt.AppName, evt.AppName, "App"));
            });
        }));

        _subscriptions.Add(_bus.Subscribe<WorkspaceChanged>(evt =>
        {
            _uiBridge.Post(() =>
            {
                if (_window == null) return;
                _window.SetWorkspaces(SettingsManager.Current.Workspaces);
                _window.PostEvent(new DynamicIslandEvent
                {
                    Type = IslandEventType.SystemAlert,
                    Title = "Workspace Activated",
                    Subtitle = evt.WorkspaceName,
                    Glyph = "💼",
                    DisplayDurationMs = 2500
                });
            });
        }));

        _subscriptions.Add(_bus.Subscribe<FullscreenChanged>(evt =>
        {
            if (!SettingsManager.Current.IslandHideOnFullscreen) return;
            if (evt.IsFullscreen)
            {
                Suspend(SuspendReason.Fullscreen);
            }
            else
            {
                Resume(SuspendReason.Fullscreen);
            }
        }));

        _subscriptions.Add(_bus.Subscribe<SettingsChanged>(evt =>
        {
            if (evt.SectionName.Equals("island", StringComparison.OrdinalIgnoreCase))
            {
                UpdateLayout();
            }
        }));
    }

    private void UnsubscribeFromEvents()
    {
        foreach (var sub in _subscriptions)
        {
            try { sub.Dispose(); } catch { }
        }
        _subscriptions.Clear();
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        UnsubscribeFromEvents();

        _uiBridge.Post(() =>
        {
            try
            {
                _window?.Close();
                _window = null;
            }
            catch { }
        });
    }
}
