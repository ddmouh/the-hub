using TheHub.Core.Bridge;
using TheHub.Core.Bus;
using TheHub.Core.Diagnostics;
using TheHub.Core.Radial;
using TheHub.Overlays.RadialLauncher;
using Xunit;

namespace TheHub.Tests;

public class GateP3StressTests
{
    private class FakeDispatcher : IUiDispatcher
    {
        public void InvokeAsync(Action action, System.Windows.Threading.DispatcherPriority priority = System.Windows.Threading.DispatcherPriority.Normal)
        {
            action();
        }
    }

    [Fact]
    public async Task GateP3_ThirtyOpenConfirmCycles_StableStateAndZeroViolations()
    {
        var bus = new HubBus();
        var fakeDispatcher = new FakeDispatcher();
        var bridge = new UiBridge(fakeDispatcher);
        var watchdog = new UiWatchdog(fakeDispatcher, TimeSpan.FromSeconds(1.5));

        int watchdogStalls = 0;
        watchdog.UnresponsiveDetected += (_, _) => Interlocked.Increment(ref watchdogStalls);

        var recognizer = new RadialGestureRecognizer
        {
            HoldDelayMs = 0,
            ActivationZone = 60,
            SliceCount = 6
        };

        int openCount = 0;
        int confirmCount = 0;
        recognizer.Opened += (x, y) => openCount++;
        recognizer.Confirmed += slice => confirmCount++;

        // Execute 30 rapid open and confirm gesture cycles
        for (int i = 0; i < 30; i++)
        {
            // 1. Mouse down at center
            recognizer.ProcessMouseDown(RadialGestureRecognizer.MouseButtonMiddle, 500, 500, i * 100);

            // 2. Flick outward in varying directions (East, South, West, North)
            int targetX = 500 + (int)(100 * Math.Cos(i * Math.PI / 4));
            int targetY = 500 + (int)(100 * Math.Sin(i * Math.PI / 4));
            recognizer.ProcessMouseMove(targetX, targetY);

            // 3. Mouse up to confirm selection
            recognizer.ProcessMouseUp(RadialGestureRecognizer.MouseButtonMiddle, targetX, targetY);
        }

        await Task.Delay(100);

        Assert.Equal(30, openCount);
        Assert.Equal(30, confirmCount);
        Assert.Equal(RadialGestureState.Idle, recognizer.State);
        Assert.Equal(0, watchdogStalls);

        watchdog.Dispose();
        bus.Dispose();
    }

    [Fact]
    public void GateP3_FullscreenProtection_BlocksRadialActivationWhenSuspended()
    {
        var bus = new HubBus();
        var fakeDispatcher = new FakeDispatcher();
        var bridge = new UiBridge(fakeDispatcher);
        var module = new RadialModule(bus, bridge);

        // Suspend due to fullscreen game
        module.Suspend(TheHub.Core.Modules.SuspendReason.Fullscreen);

        bool opened = false;
        module.Recognizer.Opened += (x, y) => opened = true;

        // Try to trigger recognizer while suspended
        module.Recognizer.ProcessMouseDown(RadialGestureRecognizer.MouseButtonMiddle, 500, 500, 0);

        Assert.False(opened, "Radial wheel should be blocked while suspended in fullscreen");

        // Resume after exiting fullscreen
        module.Resume(TheHub.Core.Modules.SuspendReason.Fullscreen);
        Assert.Equal(TheHub.Core.Modules.ModuleState.Running, module.State);

        module.Dispose();
        bus.Dispose();
    }
}
