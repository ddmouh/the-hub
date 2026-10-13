using TheHub.Core.Radial;
using Xunit;

namespace TheHub.Tests;

public class RadialGestureRecognizerTests
{
    [Fact]
    public void Test01_InitialState_IsIdle()
    {
        var recognizer = new RadialGestureRecognizer();
        Assert.Equal(RadialGestureState.Idle, recognizer.State);
    }

    [Fact]
    public void Test02_MouseDown_StartsPendingHold()
    {
        var recognizer = new RadialGestureRecognizer { HoldDelayMs = 120 };
        bool handled = recognizer.ProcessMouseDown(RadialGestureRecognizer.MouseButtonMiddle, 500, 500, 1000);

        Assert.True(handled);
        Assert.Equal(RadialGestureState.PendingHold, recognizer.State);
        Assert.Equal(500, recognizer.CenterX);
        Assert.Equal(500, recognizer.CenterY);
    }

    [Fact]
    public void Test03_MouseUp_BeforeHoldDelay_Cancels()
    {
        var recognizer = new RadialGestureRecognizer { HoldDelayMs = 120 };
        recognizer.ProcessMouseDown(RadialGestureRecognizer.MouseButtonMiddle, 500, 500, 1000);

        // Released at 1050ms (< 120ms)
        bool handled = recognizer.ProcessMouseUp(RadialGestureRecognizer.MouseButtonMiddle, 500, 500);

        Assert.False(handled);
        Assert.Equal(RadialGestureState.Idle, recognizer.State);
    }

    [Fact]
    public void Test04_CheckHoldTimer_Expires_ActivatesWheel()
    {
        var recognizer = new RadialGestureRecognizer { HoldDelayMs = 120 };
        bool opened = false;
        recognizer.Opened += (x, y) => opened = true;

        recognizer.ProcessMouseDown(RadialGestureRecognizer.MouseButtonMiddle, 500, 500, 1000);
        recognizer.CheckHoldTimer(1050); // only 50ms passed
        Assert.False(opened);

        recognizer.CheckHoldTimer(1130); // 130ms passed >= 120ms
        Assert.True(opened);
        Assert.Equal(RadialGestureState.Active, recognizer.State);
    }

    [Fact]
    public void Test05_MouseMove_CalculatesDistanceAndCompassAngle()
    {
        var recognizer = new RadialGestureRecognizer { HoldDelayMs = 0 };
        recognizer.ProcessMouseDown(RadialGestureRecognizer.MouseButtonMiddle, 500, 500, 1000);

        // Move North (dx=0, dy=-100) -> 0°
        recognizer.ProcessMouseMove(500, 400);
        Assert.Equal(100.0, recognizer.Distance, precision: 1);
        Assert.Equal(0.0, recognizer.AngleDegrees, precision: 1);

        // Move East (dx=100, dy=0) -> 90°
        recognizer.ProcessMouseMove(600, 500);
        Assert.Equal(100.0, recognizer.Distance, precision: 1);
        Assert.Equal(90.0, recognizer.AngleDegrees, precision: 1);

        // Move South (dx=0, dy=100) -> 180°
        recognizer.ProcessMouseMove(500, 600);
        Assert.Equal(100.0, recognizer.Distance, precision: 1);
        Assert.Equal(180.0, recognizer.AngleDegrees, precision: 1);

        // Move West (dx=-100, dy=0) -> 270°
        recognizer.ProcessMouseMove(400, 500);
        Assert.Equal(100.0, recognizer.Distance, precision: 1);
        Assert.Equal(270.0, recognizer.AngleDegrees, precision: 1);
    }

    [Fact]
    public void Test06_MouseMove_EntersTargeting_BeyondActivationZone()
    {
        var recognizer = new RadialGestureRecognizer { HoldDelayMs = 0, ActivationZone = 60 };
        recognizer.ProcessMouseDown(RadialGestureRecognizer.MouseButtonMiddle, 500, 500, 1000);

        // Small movement (distance 30 < 60) -> Active
        recognizer.ProcessMouseMove(500, 470);
        Assert.Equal(RadialGestureState.Active, recognizer.State);
        Assert.Equal(-1, recognizer.SelectedSliceIndex);

        // Large movement (distance 80 >= 60) -> Targeting
        recognizer.ProcessMouseMove(500, 420);
        Assert.Equal(RadialGestureState.Targeting, recognizer.State);
        Assert.True(recognizer.SelectedSliceIndex >= 0);
    }

    [Fact]
    public void Test07_CalculateSliceIndex_4Slices_MapsCardinalDirections()
    {
        // 4 slices: 0=North (315°..45°), 1=East (45°..135°), 2=South (135°..225°), 3=West (225°..315°)
        Assert.Equal(0, RadialGestureRecognizer.CalculateSliceIndex(0.0, 4));
        Assert.Equal(0, RadialGestureRecognizer.CalculateSliceIndex(350.0, 4));
        Assert.Equal(0, RadialGestureRecognizer.CalculateSliceIndex(10.0, 4));
        Assert.Equal(1, RadialGestureRecognizer.CalculateSliceIndex(90.0, 4));
        Assert.Equal(2, RadialGestureRecognizer.CalculateSliceIndex(180.0, 4));
        Assert.Equal(3, RadialGestureRecognizer.CalculateSliceIndex(270.0, 4));
    }

    [Fact]
    public void Test08_CalculateSliceIndex_6Slices_Valid()
    {
        // 6 slices (60° per slice)
        Assert.Equal(0, RadialGestureRecognizer.CalculateSliceIndex(0.0, 6));
        Assert.Equal(1, RadialGestureRecognizer.CalculateSliceIndex(60.0, 6));
        Assert.Equal(2, RadialGestureRecognizer.CalculateSliceIndex(120.0, 6));
        Assert.Equal(3, RadialGestureRecognizer.CalculateSliceIndex(180.0, 6));
        Assert.Equal(4, RadialGestureRecognizer.CalculateSliceIndex(240.0, 6));
        Assert.Equal(5, RadialGestureRecognizer.CalculateSliceIndex(300.0, 6));
    }

    [Fact]
    public void Test09_MouseUp_OutsideActivationZone_ConfirmsSlice()
    {
        var recognizer = new RadialGestureRecognizer { HoldDelayMs = 0, ActivationZone = 60, SliceCount = 4 };
        recognizer.ProcessMouseDown(RadialGestureRecognizer.MouseButtonMiddle, 500, 500, 1000);

        int? confirmedSlice = null;
        recognizer.Confirmed += slice => confirmedSlice = slice;

        // Move East (90°, dist 100 >= 60) -> Slice 1
        recognizer.ProcessMouseMove(600, 500);
        recognizer.ProcessMouseUp(RadialGestureRecognizer.MouseButtonMiddle, 600, 500);

        Assert.Equal(1, confirmedSlice);
        Assert.Equal(RadialGestureState.Idle, recognizer.State);
    }

    [Fact]
    public void Test10_MouseUp_InsideActivationZone_Cancels()
    {
        var recognizer = new RadialGestureRecognizer { HoldDelayMs = 0, ActivationZone = 60 };
        recognizer.ProcessMouseDown(RadialGestureRecognizer.MouseButtonMiddle, 500, 500, 1000);

        bool cancelled = false;
        recognizer.Cancelled += () => cancelled = true;

        // Small movement within deadzone (dist 20 < 60)
        recognizer.ProcessMouseMove(510, 510);
        recognizer.ProcessMouseUp(RadialGestureRecognizer.MouseButtonMiddle, 510, 510);

        Assert.True(cancelled);
        Assert.Equal(RadialGestureState.Idle, recognizer.State);
    }

    [Fact]
    public void Test11_EscapeKey_CancelsActiveGesture()
    {
        var recognizer = new RadialGestureRecognizer { HoldDelayMs = 0 };
        recognizer.ProcessMouseDown(RadialGestureRecognizer.MouseButtonMiddle, 500, 500, 1000);

        bool cancelled = false;
        recognizer.Cancelled += () => cancelled = true;

        bool handled = recognizer.ProcessKeyDown(RadialGestureRecognizer.VkEscape, false, false, false);

        Assert.True(handled);
        Assert.True(cancelled);
        Assert.Equal(RadialGestureState.Idle, recognizer.State);
    }

    [Fact]
    public void Test12_AltZ_Shortcut_ImmediatelyActivates()
    {
        var recognizer = new RadialGestureRecognizer();
        bool opened = false;
        recognizer.Opened += (x, y) => opened = true;

        bool handled = recognizer.ProcessKeyDown(RadialGestureRecognizer.VkZ, isAlt: true, isControl: false, isShift: false);

        Assert.True(handled);
        Assert.True(opened);
        Assert.Equal(RadialGestureState.Active, recognizer.State);
    }

    [Fact]
    public void Test13_AltSpace_Shortcut_ImmediatelyActivates()
    {
        var recognizer = new RadialGestureRecognizer();
        bool opened = false;
        recognizer.Opened += (x, y) => opened = true;

        bool handled = recognizer.ProcessKeyDown(RadialGestureRecognizer.VkSpace, isAlt: true, isControl: false, isShift: false);

        Assert.True(handled);
        Assert.True(opened);
        Assert.Equal(RadialGestureState.Active, recognizer.State);
    }

    [Fact]
    public void Test14_ClickMode_ToggleBehavior()
    {
        var recognizer = new RadialGestureRecognizer { GestureBehavior = "Click", ActivationZone = 60 };
        recognizer.ProcessMouseDown(RadialGestureRecognizer.MouseButtonMiddle, 500, 500, 1000);

        Assert.Equal(RadialGestureState.Active, recognizer.State);

        // Releasing mouse does not cancel in click mode
        recognizer.ProcessMouseUp(RadialGestureRecognizer.MouseButtonMiddle, 500, 500);
        Assert.Equal(RadialGestureState.Active, recognizer.State);

        // Clicking second time in deadzone cancels
        bool cancelled = false;
        recognizer.Cancelled += () => cancelled = true;
        recognizer.ProcessMouseDown(RadialGestureRecognizer.MouseButtonMiddle, 500, 500, 1500);

        Assert.True(cancelled);
        Assert.Equal(RadialGestureState.Idle, recognizer.State);
    }

    [Fact]
    public void Test15_PendingHold_AbortsIfDriftingFast()
    {
        var recognizer = new RadialGestureRecognizer { HoldDelayMs = 120 };
        recognizer.ProcessMouseDown(RadialGestureRecognizer.MouseButtonMiddle, 500, 500, 1000);
        Assert.Equal(RadialGestureState.PendingHold, recognizer.State);

        // Mouse drifts 50px away before hold timer fires -> aborted
        recognizer.ProcessMouseMove(550, 500);
        Assert.Equal(RadialGestureState.Idle, recognizer.State);
    }
}
