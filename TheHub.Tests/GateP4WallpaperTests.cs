using System.Drawing;
using TheHub.Common.Config;
using TheHub.Common.Models;
using TheHub.Core.Bridge;
using TheHub.Core.Bus;
using TheHub.Core.Modules;
using TheHub.Overlays.Wallpaper;
using Xunit;

namespace TheHub.Tests;

public class GateP4WallpaperTests
{
    [Fact]
    public void WallpaperEngine_PlayerCountAndTrimming()
    {
        int initialCount = WallpaperEngine.ActivePlayerCount;

        WallpaperEngine.IncrementActivePlayers();
        Assert.Equal(initialCount + 1, WallpaperEngine.ActivePlayerCount);

        WallpaperEngine.DecrementActivePlayers();
        Assert.Equal(initialCount, WallpaperEngine.ActivePlayerCount);

        // Ensure TrimMemoryAfterInactive runs without throwing
        WallpaperEngine.TrimMemoryAfterInactive();
    }

    [Fact]
    public async Task WallpaperModule_Lifecycle_StartsAndStops()
    {
        var bus = new HubBus();
        var bridge = new UiBridge();
        using var module = new WallpaperModule(bus, bridge);

        Assert.Equal(ModuleState.Stopped, module.State);
        Assert.False(module.IsPaused);

        await module.StartAsync();
        Assert.Equal(ModuleState.Running, module.State);

        await module.StopAsync();
        Assert.Equal(ModuleState.Stopped, module.State);
    }

    [Fact]
    public async Task WallpaperModule_FullscreenEvent_PausesAndResumes()
    {
        var bus = new HubBus();
        var bridge = new UiBridge();
        using var module = new WallpaperModule(bus, bridge);
        await module.StartAsync();

        SettingsManager.Current.WallpaperPauseOnFullscreen = true;

        // Publish fullscreen event
        bus.Publish(new FullscreenChanged(true, IntPtr.Zero));
        await Task.Delay(50);
        Assert.True(module.IsPaused);
        Assert.Contains(SuspendReason.Fullscreen, module.ActiveSuspendReasons);

        // Publish fullscreen cleared event
        bus.Publish(new FullscreenChanged(false, IntPtr.Zero));
        await Task.Delay(50);
        Assert.False(module.IsPaused);
        Assert.DoesNotContain(SuspendReason.Fullscreen, module.ActiveSuspendReasons);
    }

    [Fact]
    public async Task WallpaperModule_BatteryEvent_PausesAndResumes()
    {
        var bus = new HubBus();
        var bridge = new UiBridge();
        using var module = new WallpaperModule(bus, bridge);
        await module.StartAsync();

        SettingsManager.Current.WallpaperPauseOnBattery = true;

        // On battery
        bus.Publish(new PowerStateChanged(true, 45));
        await Task.Delay(50);
        Assert.True(module.IsPaused);
        Assert.Contains(SuspendReason.Battery, module.ActiveSuspendReasons);

        // AC power restored
        bus.Publish(new PowerStateChanged(false, 100));
        await Task.Delay(50);
        Assert.False(module.IsPaused);
        Assert.DoesNotContain(SuspendReason.Battery, module.ActiveSuspendReasons);
    }

    [Fact]
    public void WallpaperModule_MultipleSuspendReasons_RequiresAllToClear()
    {
        var bus = new HubBus();
        var bridge = new UiBridge();
        using var module = new WallpaperModule(bus, bridge);

        module.Suspend(SuspendReason.Fullscreen);
        module.Suspend(SuspendReason.Battery);

        Assert.True(module.IsPaused);
        Assert.Equal(2, module.ActiveSuspendReasons.Count);

        // Resume only fullscreen
        module.Resume(SuspendReason.Fullscreen);
        Assert.True(module.IsPaused, "Should still be paused because battery is active");
        Assert.Contains(SuspendReason.Battery, module.ActiveSuspendReasons);

        // Resume battery
        module.Resume(SuspendReason.Battery);
        Assert.False(module.IsPaused, "Should resume once all reasons are cleared");
        Assert.Empty(module.ActiveSuspendReasons);
    }

    [Fact]
    public void Wallpaper_FitModes_MapCorrectly()
    {
        Assert.Equal(FlyleafLib.AspectRatio.Fill, LiveWallpaperWindow.MapFitModeToAspectRatio("Fill"));
        Assert.Equal(FlyleafLib.AspectRatio.Keep, LiveWallpaperWindow.MapFitModeToAspectRatio("Fit"));
        Assert.Equal(FlyleafLib.AspectRatio.Custom, LiveWallpaperWindow.MapFitModeToAspectRatio("Stretch"));
        Assert.Equal(FlyleafLib.AspectRatio.Keep, LiveWallpaperWindow.MapFitModeToAspectRatio("Center"));
        Assert.Equal(FlyleafLib.AspectRatio.Fill, LiveWallpaperWindow.MapFitModeToAspectRatio("Span"));
        Assert.Equal(FlyleafLib.AspectRatio.Fill, LiveWallpaperWindow.MapFitModeToAspectRatio("unknown"));
    }

    [Fact]
    public void WallpaperItem_MissingFile_ReportsWarningBadge()
    {
        var item = new WallpaperItem
        {
            Id = "test-missing",
            Title = "Ghost Video",
            FilePath = @"C:\NonExistentFolder\ghost_video_99999.mp4",
            Type = WallpaperType.Animated
        };

        Assert.True(item.IsMissingFile);
        Assert.Equal("⚠️ MISSING FILE", item.WarningBadgeText);
    }

    [Fact]
    public void WallpaperModule_MissingFile_FailsGracefullyWithoutThrowing()
    {
        var bus = new HubBus();
        var bridge = new UiBridge();
        using var module = new WallpaperModule(bus, bridge);

        var item = new WallpaperItem
        {
            Id = "missing-1",
            Title = "Missing",
            FilePath = @"C:\NonExistent\nonexistent.mp4",
            Type = WallpaperType.Animated
        };

        bool result = module.ApplyWallpaper(item, "ALL");
        Assert.False(result);
    }

    [Fact]
    public void MultiMonitor_SpanBounds_CalculatesFullVirtualDesktopBoundingBox()
    {
        var monitor1 = new Rectangle(0, 0, 1920, 1080);
        var monitor2 = new Rectangle(1920, 0, 2560, 1440);

        int minLeft = Math.Min(monitor1.Left, monitor2.Left);
        int minTop = Math.Min(monitor1.Top, monitor2.Top);
        int maxRight = Math.Max(monitor1.Right, monitor2.Right);
        int maxBottom = Math.Max(monitor1.Bottom, monitor2.Bottom);

        var spanRect = new Rectangle(minLeft, minTop, maxRight - minLeft, maxBottom - minTop);

        Assert.Equal(0, spanRect.Left);
        Assert.Equal(0, spanRect.Top);
        Assert.Equal(4480, spanRect.Width); // 1920 + 2560
        Assert.Equal(1440, spanRect.Height); // max of 1080 and 1440
    }
}
