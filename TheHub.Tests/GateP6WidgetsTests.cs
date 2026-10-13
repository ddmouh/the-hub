using System.Drawing;
using TheHub.Core.Bridge;
using TheHub.Core.Bus;
using TheHub.Core.Modules;
using TheHub.Core.Services;
using TheHub.Overlays.Widgets;
using Xunit;

namespace TheHub.Tests;

public class GateP6WidgetsTests
{
    [Fact]
    public void WidgetSnapEngine_ScreenEdgeSnap_SnapsNearBoundaries()
    {
        var screen = new RectangleF(0, 0, 1920, 1080);

        // Near Left edge (within 16px)
        var nearLeft = new RectangleF(10, 200, 300, 150);
        var resultLeft = WidgetSnapEngine.ComputeSnap(nearLeft, screen);
        Assert.True(resultLeft.SnappedToEdge);
        Assert.Equal(0, resultLeft.SnappedBounds.X);

        // Near Top edge
        var nearTop = new RectangleF(200, 12, 300, 150);
        var resultTop = WidgetSnapEngine.ComputeSnap(nearTop, screen);
        Assert.True(resultTop.SnappedToEdge);
        Assert.Equal(0, resultTop.SnappedBounds.Y);

        // Near Right edge
        var nearRight = new RectangleF(1610, 200, 300, 150); // Right edge = 1910, screen = 1920
        var resultRight = WidgetSnapEngine.ComputeSnap(nearRight, screen);
        Assert.True(resultRight.SnappedToEdge);
        Assert.Equal(1620, resultRight.SnappedBounds.X); // 1920 - 300
    }

    [Fact]
    public void WidgetSnapEngine_InterWidgetSnap_AlignsToNeighbors()
    {
        var screen = new RectangleF(0, 0, 1920, 1080);
        var neighbor = new RectangleF(400, 400, 200, 100);

        // Target left is near neighbor right (neighbor right is 600, target is at 610)
        var target = new RectangleF(610, 405, 150, 80);
        var result = WidgetSnapEngine.ComputeSnap(target, screen, new[] { neighbor });

        Assert.True(result.SnappedToWidget);
        Assert.Equal(600, result.SnappedBounds.X); // Snapped to neighbor.Right
        Assert.Equal(400, result.SnappedBounds.Y); // Snapped to neighbor.Top
    }

    [Fact]
    public void WidgetSnapEngine_GridSnap_AlignsToGridUnits()
    {
        var screen = new RectangleF(0, 0, 1920, 1080);
        var target = new RectangleF(255, 307, 100, 50);

        var result = WidgetSnapEngine.ComputeSnap(target, screen, threshold: 4, enableGrid: true, gridSize: 16);

        Assert.True(result.SnappedToGrid);
        Assert.Equal(256, result.SnappedBounds.X); // 16 * 16 = 256
        Assert.Equal(304, result.SnappedBounds.Y); // 19 * 16 = 304
    }

    [Fact]
    public void WidgetSnapEngine_NoSnap_PreservesFreeformPosition()
    {
        var screen = new RectangleF(0, 0, 1920, 1080);
        var target = new RectangleF(500, 500, 200, 100);

        var result = WidgetSnapEngine.ComputeSnap(target, screen, threshold: 16);

        Assert.False(result.SnappedToEdge);
        Assert.False(result.SnappedToWidget);
        Assert.False(result.SnappedToGrid);
        Assert.Equal(500, result.SnappedBounds.X);
        Assert.Equal(500, result.SnappedBounds.Y);
    }

    [Fact]
    public void WidgetsModule_LifecycleAndPowerPolicy_SuspendsAndResumes()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var bus = new HubBus();
                var bridge = new UiBridge();
                using var module = new WidgetsModule(bus, bridge);

                Assert.Equal(ModuleState.Stopped, module.State);
                Assert.False(module.IsPaused);

                module.StartAsync().GetAwaiter().GetResult();
                Assert.Equal(ModuleState.Running, module.State);

                // Suspend due to fullscreen app
                module.Suspend(SuspendReason.Fullscreen);
                Assert.Equal(ModuleState.Suspended, module.State);
                Assert.True(module.IsPaused);

                // Resume when fullscreen exits
                module.Resume(SuspendReason.Fullscreen);
                Assert.Equal(ModuleState.Running, module.State);
                Assert.False(module.IsPaused);

                module.StopAsync().GetAwaiter().GetResult();
                Assert.Equal(ModuleState.Stopped, module.State);
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (error != null)
        {
            throw new AggregateException(error);
        }
    }
}
