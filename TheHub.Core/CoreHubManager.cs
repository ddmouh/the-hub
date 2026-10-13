using TheHub.Common.Config;
using TheHub.Common.Models;
using TheHub.Core.Hooks;
using TheHub.Core.Monitors;
using TheHub.Core.Services;

namespace TheHub.Core;

public class CoreHubManager : IDisposable
{
    private readonly LowLevelMouseHook _mouseHook;
    private readonly LowLevelKeyboardHook _keyboardHook;
    private readonly MediaSessionMonitor _mediaMonitor;
    private readonly FullscreenDetector _fullscreenDetector;
    private readonly ClipboardMonitor _clipboardMonitor;
    private readonly VolumeMonitor _volumeMonitor;
    private readonly ActiveAppMonitor _activeAppMonitor;

    public event Action<int, int>? RadialLauncherRequested;
    public event Action<int, int>? RadialPointerMoved;
    public event Action? RadialReleaseRequested;
    public event Action<DynamicIslandEvent>? DynamicIslandEventReceived;
    public event Action<AmbientStatusInfo>? AmbientStatusChanged;
    public event Action<bool>? FullscreenModeChanged;

    public CoreHubManager()
    {
        var settings = SettingsManager.Current;

        _mouseHook = new LowLevelMouseHook(settings.HoldDelayMs);
        _keyboardHook = new LowLevelKeyboardHook();
        _mediaMonitor = new MediaSessionMonitor();
        _fullscreenDetector = new FullscreenDetector();
        _clipboardMonitor = new ClipboardMonitor();
        _volumeMonitor = new VolumeMonitor();
        _activeAppMonitor = new ActiveAppMonitor();

        _clipboardMonitor.ClipboardEventOccurred += evt => DynamicIslandEventReceived?.Invoke(evt);
        _volumeMonitor.VolumeEventOccurred += evt => DynamicIslandEventReceived?.Invoke(evt);
        _activeAppMonitor.AmbientStatusChanged += status => AmbientStatusChanged?.Invoke(status);
    }

    public Task StartAsync()
    {
        var settings = SettingsManager.Current;

        // 1. Hooks
        if (settings.RadialLauncherEnabled)
        {
            _mouseHook.RadialOpenRequested += (x, y) => RadialLauncherRequested?.Invoke(x, y);
            _mouseHook.RadialPointerMoved += (x, y) => RadialPointerMoved?.Invoke(x, y);
            _mouseHook.RadialReleaseRequested += () => RadialReleaseRequested?.Invoke();
            _mouseHook.Start();

            _keyboardHook.ShortcutTriggered += () =>
            {
                Native.Win32Api.GetCursorPos(out var pt);
                RadialLauncherRequested?.Invoke(pt.x, pt.y);
            };
            _keyboardHook.ShortcutReleased += () =>
            {
                if (string.Equals(SettingsManager.Current.GestureBehavior, "Hold", StringComparison.OrdinalIgnoreCase))
                {
                    RadialReleaseRequested?.Invoke();
                }
            };
            _keyboardHook.Start();
        }

        // 3. Monitors
        _fullscreenDetector.FullscreenChanged += isFs => FullscreenModeChanged?.Invoke(isFs);
        _fullscreenDetector.Start();

        _volumeMonitor.Start();
        _activeAppMonitor.Start();

        if (settings.IslandShowMedia)
        {
            _mediaMonitor.MediaEventOccurred += evt => DynamicIslandEventReceived?.Invoke(evt);
            _ = _mediaMonitor.InitializeAsync();
        }

        return Task.CompletedTask;
    }

    public void TriggerIslandEvent(DynamicIslandEvent evt)
    {
        DynamicIslandEventReceived?.Invoke(evt);
    }

    public void AttachClipboardListener(IntPtr hwnd)
    {
        _clipboardMonitor.Start(hwnd);
    }

    public void SetIslandWindowHandle(IntPtr hwnd)
    {
        _fullscreenDetector.TargetIslandHwnd = hwnd;
    }

    public void ResetRadialMouseState() => _mouseHook.ResetWheelState();

    public async Task MediaPlayPauseAsync() => await _mediaMonitor.TogglePlayPauseAsync();
    public async Task MediaNextAsync() => await _mediaMonitor.SkipNextAsync();
    public async Task MediaPrevAsync() => await _mediaMonitor.SkipPreviousAsync();

    public void Dispose()
    {
        try { _mouseHook.Dispose(); } catch { }
        try { _keyboardHook.Dispose(); } catch { }
        try { _fullscreenDetector.Dispose(); } catch { }
        try { _clipboardMonitor.Dispose(); } catch { }
        try { _volumeMonitor.Dispose(); } catch { }
        try { _activeAppMonitor.Dispose(); } catch { }
        try { _mediaMonitor.Dispose(); } catch { }
    }
}
