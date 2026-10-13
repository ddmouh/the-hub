using TheHub.Core.Bridge;
using TheHub.Core.Bus;
using TheHub.Core.Diagnostics;
using TheHub.Core.Services;
using TheHub.Overlays.DynamicIsland;
using Xunit;

namespace TheHub.Tests;

public class GateP2StressTests
{
    private class FakeDispatcher : IUiDispatcher
    {
        public void InvokeAsync(Action action, System.Windows.Threading.DispatcherPriority priority = System.Windows.Threading.DispatcherPriority.Normal)
        {
            // Execute synchronously on test worker thread
            action();
        }
    }

    [Fact]
    public async Task GateP2_InteractionStressTest_ZeroWatchdogViolations()
    {
        var bus = new HubBus();
        var fakeDispatcher = new FakeDispatcher();
        var watchdog = new UiWatchdog(fakeDispatcher, TimeSpan.FromSeconds(1.5));

        var sm = new IslandStateMachine();
        var audioService = new AudioService();
        var usbWatcher = new UsbWatcher(bus, enableDrivePolling: false);

        int watchdogAlerts = 0;
        watchdog.UnresponsiveDetected += (stall, _) => Interlocked.Increment(ref watchdogAlerts);

        // 1. 50 rapid volume changes
        for (int i = 0; i < 50; i++)
        {
            int vol = i * 2;
            bus.Publish(new VolumeChanged(vol, false));
            sm.PostEvent(TheHub.Common.Models.IslandEventType.Volume, vol);
        }

        // 2. 20 media changes
        for (int i = 0; i < 20; i++)
        {
            bus.Publish(new MediaChanged($"Track {i}", "Artist", i % 2 == 0));
            sm.PostEvent(TheHub.Common.Models.IslandEventType.Media, $"Track {i}");
        }

        // 3. 30 expand / collapse cycles
        for (int i = 0; i < 30; i++)
        {
            sm.Expand(IslandState.ExpandedControls);
            sm.Collapse();
        }

        // 4. USB arrival event
        usbWatcher.HandleDeviceChange(true, "Ultra Fast NVMe");
        sm.PostEvent(TheHub.Common.Models.IslandEventType.Usb, "Ultra Fast NVMe");

        // Allow any background queue work to settle
        await Task.Delay(200);

        Assert.Equal(0, watchdogAlerts);
        watchdog.Dispose();
        usbWatcher.Dispose();
        bus.Dispose();
    }
}
