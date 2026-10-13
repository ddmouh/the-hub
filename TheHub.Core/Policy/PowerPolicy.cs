using TheHub.Common.Config;
using TheHub.Core.Bus;
using TheHub.Core.Modules;

namespace TheHub.Core.Policy;

public class PowerPolicy : IDisposable
{
    private readonly IHubBus _bus;
    private readonly ModuleHost _moduleHost;
    private readonly List<IDisposable> _subscriptions = new();
    private bool _isDisposed;

    public bool IsFullscreenActive { get; private set; }
    public bool IsOnBatteryActive { get; private set; }

    public PowerPolicy(IHubBus bus, ModuleHost moduleHost)
    {
        _bus = bus;
        _moduleHost = moduleHost;

        _subscriptions.Add(_bus.Subscribe<FullscreenChanged>(OnFullscreenChanged));
        _subscriptions.Add(_bus.Subscribe<PowerStateChanged>(OnPowerStateChanged));
    }

    private void OnFullscreenChanged(FullscreenChanged evt)
    {
        IsFullscreenActive = evt.IsFullscreen;
        var settings = SettingsManager.Current;

        if (evt.IsFullscreen)
        {
            if (settings.WallpaperPauseOnFullscreen || settings.PauseGraphicsOnFullscreen)
            {
                _moduleHost.GetModule("wallpaper")?.Suspend(SuspendReason.Fullscreen);
            }

            if (settings.IslandHideOnFullscreen && !settings.IslandAlwaysShowOnFullscreen)
            {
                _moduleHost.GetModule("island")?.Suspend(SuspendReason.Fullscreen);
            }

            if (settings.FullscreenProtection)
            {
                _moduleHost.GetModule("radial")?.Suspend(SuspendReason.Fullscreen);
            }

            _moduleHost.GetModule("widgets")?.Suspend(SuspendReason.Fullscreen);
        }
        else
        {
            _moduleHost.GetModule("wallpaper")?.Resume(SuspendReason.Fullscreen);
            _moduleHost.GetModule("island")?.Resume(SuspendReason.Fullscreen);
            _moduleHost.GetModule("radial")?.Resume(SuspendReason.Fullscreen);
            _moduleHost.GetModule("widgets")?.Resume(SuspendReason.Fullscreen);
        }
    }

    private void OnPowerStateChanged(PowerStateChanged evt)
    {
        IsOnBatteryActive = evt.IsOnBattery;
        var settings = SettingsManager.Current;

        if (evt.IsOnBattery)
        {
            if (settings.WallpaperPauseOnBattery || settings.LowPowerMode)
            {
                _moduleHost.GetModule("wallpaper")?.Suspend(SuspendReason.Battery);
            }
        }
        else
        {
            _moduleHost.GetModule("wallpaper")?.Resume(SuspendReason.Battery);
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        foreach (var sub in _subscriptions)
        {
            sub.Dispose();
        }
        _subscriptions.Clear();
    }
}
