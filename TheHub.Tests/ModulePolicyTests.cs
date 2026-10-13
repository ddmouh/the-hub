using TheHub.Common.Config;
using TheHub.Core.Bus;
using TheHub.Core.Modules;
using TheHub.Core.Policy;
using Xunit;

namespace TheHub.Tests;

public class ModulePolicyTests
{
    private class FakeModule : IHubModule
    {
        public string Id { get; }
        public ModuleState State { get; private set; } = ModuleState.Stopped;
        public int StartCount { get; private set; }
        public int SuspendCount { get; private set; }
        public int ResumeCount { get; private set; }
        public SuspendReason? LastSuspendReason { get; private set; }
        public bool ThrowOnStart { get; set; }

        public FakeModule(string id) => Id = id;

        public Task StartAsync(CancellationToken ct = default)
        {
            StartCount++;
            if (ThrowOnStart)
            {
                State = ModuleState.Faulted;
                throw new InvalidOperationException($"Fault in module {Id}");
            }
            State = ModuleState.Running;
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            State = ModuleState.Stopped;
            return Task.CompletedTask;
        }

        public void Suspend(SuspendReason reason)
        {
            SuspendCount++;
            LastSuspendReason = reason;
            State = ModuleState.Suspended;
        }

        public void Resume(SuspendReason reason)
        {
            ResumeCount++;
            State = ModuleState.Running;
        }

        public void Dispose() { }
    }

    [Fact]
    public async Task ModuleHost_StartAll_StartsEnabledModules()
    {
        var host = new ModuleHost();
        var mod1 = new FakeModule("mod1");
        var mod2 = new FakeModule("mod2");

        host.Register(mod1, isEnabled: true);
        host.Register(mod2, isEnabled: false);

        await host.StartAllAsync();

        Assert.Equal(ModuleState.Running, mod1.State);
        Assert.Equal(ModuleState.Stopped, mod2.State);
    }

    [Fact]
    public async Task ModuleHost_FaultRecovery_StopsAtThreeAttempts()
    {
        var errors = new List<string>();
        var host = new ModuleHost((msg, ex) => errors.Add(msg));

        var failing = new FakeModule("fragile") { ThrowOnStart = true };
        host.Register(failing, isEnabled: true);

        await host.StartAllAsync();

        // 1 initial attempt + 3 restarts = 4 start attempts total
        Assert.True(failing.StartCount >= 4, $"Expected at least 4 start attempts, got {failing.StartCount}");
        Assert.Contains(errors, e => e.Contains("faulted 3 times"));
    }

    [Fact]
    public async Task PowerPolicy_SimulatedFullscreen_SuspendsConfiguredModules()
    {
        using var bus = new HubBus();
        var host = new ModuleHost();

        var wallpaper = new FakeModule("wallpaper");
        var island = new FakeModule("island");
        var radial = new FakeModule("radial");
        var widgets = new FakeModule("widgets");

        host.Register(wallpaper);
        host.Register(island);
        host.Register(radial);
        host.Register(widgets);

        await wallpaper.StartAsync();
        await island.StartAsync();
        await radial.StartAsync();
        await widgets.StartAsync();

        using var policy = new PowerPolicy(bus, host);

        // Ensure default settings have pause flags enabled
        SettingsManager.Current.WallpaperPauseOnFullscreen = true;
        SettingsManager.Current.IslandHideOnFullscreen = true;
        SettingsManager.Current.IslandAlwaysShowOnFullscreen = false;
        SettingsManager.Current.FullscreenProtection = true;

        // Publish Fullscreen event
        bus.Publish(new FullscreenChanged(true, IntPtr.Zero));
        Thread.Sleep(100);

        Assert.True(policy.IsFullscreenActive);
        Assert.Equal(ModuleState.Suspended, wallpaper.State);
        Assert.Equal(ModuleState.Suspended, island.State);
        Assert.Equal(ModuleState.Suspended, radial.State);
        Assert.Equal(ModuleState.Suspended, widgets.State);

        // Publish Exit Fullscreen event
        bus.Publish(new FullscreenChanged(false, IntPtr.Zero));
        Thread.Sleep(100);

        Assert.False(policy.IsFullscreenActive);
        Assert.Equal(ModuleState.Running, wallpaper.State);
        Assert.Equal(ModuleState.Running, island.State);
        Assert.Equal(ModuleState.Running, radial.State);
        Assert.Equal(ModuleState.Running, widgets.State);
    }

    [Fact]
    public async Task PowerPolicy_BatteryPower_SuspendsWallpaperOnly()
    {
        using var bus = new HubBus();
        var host = new ModuleHost();

        var wallpaper = new FakeModule("wallpaper");
        var island = new FakeModule("island");

        host.Register(wallpaper);
        host.Register(island);

        await wallpaper.StartAsync();
        await island.StartAsync();

        using var policy = new PowerPolicy(bus, host);
        SettingsManager.Current.WallpaperPauseOnBattery = true;

        // On Battery
        bus.Publish(new PowerStateChanged(true, 45));
        Thread.Sleep(100);

        Assert.True(policy.IsOnBatteryActive);
        Assert.Equal(ModuleState.Suspended, wallpaper.State);
        Assert.Equal(SuspendReason.Battery, wallpaper.LastSuspendReason);
        Assert.Equal(ModuleState.Running, island.State); // Island stays active

        // Back to AC Power
        bus.Publish(new PowerStateChanged(false, 100));
        Thread.Sleep(100);

        Assert.False(policy.IsOnBatteryActive);
        Assert.Equal(ModuleState.Running, wallpaper.State);
    }
}
