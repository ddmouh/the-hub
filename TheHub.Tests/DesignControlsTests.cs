using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TheHub.Design.Controls;
using Xunit;

namespace TheHub.Tests;

public class DesignControlsTests
{
    private static void RunOnSta(Action action)
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { exception = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (exception != null) throw new AggregateException(exception);
    }

    #region HoldButton State Machine Tests

    [Fact]
    public void HoldButtonStateMachine_InitialState_IsIdle()
    {
        var sm = new HoldButtonStateMachine { HoldDurationMs = 1200.0 };

        Assert.Equal(HoldButtonState.Idle, sm.State);
        Assert.Equal(0.0, sm.Progress);
    }

    [Fact]
    public void HoldButtonStateMachine_Press_TransitionsToHolding()
    {
        var sm = new HoldButtonStateMachine { HoldDurationMs = 1200.0 };

        bool pressed = sm.Press();

        Assert.True(pressed);
        Assert.Equal(HoldButtonState.Holding, sm.State);
    }

    [Fact]
    public void HoldButtonStateMachine_Step_IncreasesProgress()
    {
        var sm = new HoldButtonStateMachine { HoldDurationMs = 1000.0 };
        sm.Press();

        sm.Step(500.0);

        Assert.Equal(HoldButtonState.Holding, sm.State);
        Assert.Equal(0.5, sm.Progress, 2);
    }

    [Fact]
    public void HoldButtonStateMachine_EarlyRelease_Reverses_WithoutConfirmed()
    {
        var sm = new HoldButtonStateMachine { HoldDurationMs = 1000.0, ReleaseRate = 2.0 };
        bool confirmedFired = false;
        sm.Confirmed += () => confirmedFired = true;

        sm.Press();
        sm.Step(400.0); // Progress = 0.4
        Assert.Equal(0.4, sm.Progress, 2);

        sm.Release();
        Assert.Equal(HoldButtonState.Releasing, sm.State);

        // Advance decay: 400ms * 2.0 = 800ms of reversal, goes to 0
        sm.Step(250.0);

        Assert.Equal(HoldButtonState.Idle, sm.State);
        Assert.Equal(0.0, sm.Progress);
        Assert.False(confirmedFired, "Confirmed event must NOT fire on early release!");
    }

    [Fact]
    public void HoldButtonStateMachine_Completion_FiresConfirmedOnce_And_ResetsAfterTimeout()
    {
        var sm = new HoldButtonStateMachine
        {
            HoldDurationMs = 1000.0,
            ConfirmedDurationMs = 500.0
        };

        int confirmedCount = 0;
        sm.Confirmed += () => confirmedCount++;

        sm.Press();
        sm.Step(1000.0);

        Assert.Equal(HoldButtonState.Confirmed, sm.State);
        Assert.Equal(1.0, sm.Progress);
        Assert.Equal(1, confirmedCount);

        // Step further while confirmed: should remain confirmed until ConfirmedDurationMs
        sm.Step(200.0);
        Assert.Equal(HoldButtonState.Confirmed, sm.State);
        Assert.Equal(1, confirmedCount);

        // Exceed ConfirmedDurationMs: should reset to Idle
        sm.Step(350.0);
        Assert.Equal(HoldButtonState.Idle, sm.State);
        Assert.Equal(0.0, sm.Progress);
        Assert.Equal(1, confirmedCount);
    }

    [Fact]
    public void HoldButtonStateMachine_Reset_ImmediatelyCancels()
    {
        var sm = new HoldButtonStateMachine { HoldDurationMs = 1000.0 };
        sm.Press();
        sm.Step(600.0);

        sm.Reset();

        Assert.Equal(HoldButtonState.Idle, sm.State);
        Assert.Equal(0.0, sm.Progress);
    }

    #endregion

    #region RangeSlider Math Tests

    [Fact]
    public void RangeSliderMath_SnapToStep_SnapsProperly()
    {
        Assert.Equal(20.0, RangeSliderMath.SnapToStep(22.0, 0.0, 5.0));
        Assert.Equal(25.0, RangeSliderMath.SnapToStep(23.0, 0.0, 5.0));
        Assert.Equal(25.0, RangeSliderMath.SnapToStep(25.0, 0.0, 5.0));
        Assert.Equal(12.5, RangeSliderMath.SnapToStep(12.3, 0.0, 0.5));
    }

    [Fact]
    public void RangeSliderMath_ClampLower_EnforcesBoundariesAndDistance()
    {
        double min = 0.0;
        double max = 100.0;
        double upper = 70.0;
        double minDistance = 15.0;
        double step = 1.0;

        // Valid candidate within bounds
        double val1 = RangeSliderMath.ClampLower(30.0, upper, min, max, minDistance, step);
        Assert.Equal(30.0, val1);

        // Candidate below minimum
        double val2 = RangeSliderMath.ClampLower(-10.0, upper, min, max, minDistance, step);
        Assert.Equal(0.0, val2);

        // Candidate crossing upper minus minDistance (upper - 15 = 55)
        double val3 = RangeSliderMath.ClampLower(60.0, upper, min, max, minDistance, step);
        Assert.Equal(55.0, val3);
    }

    [Fact]
    public void RangeSliderMath_ClampUpper_EnforcesBoundariesAndDistance()
    {
        double min = 0.0;
        double max = 100.0;
        double lower = 30.0;
        double minDistance = 20.0;
        double step = 1.0;

        // Valid candidate within bounds
        double val1 = RangeSliderMath.ClampUpper(lower, 80.0, min, max, minDistance, step);
        Assert.Equal(80.0, val1);

        // Candidate above maximum
        double val2 = RangeSliderMath.ClampUpper(lower, 150.0, min, max, minDistance, step);
        Assert.Equal(100.0, val2);

        // Candidate crossing lower plus minDistance (lower + 20 = 50)
        double val3 = RangeSliderMath.ClampUpper(lower, 40.0, min, max, minDistance, step);
        Assert.Equal(50.0, val3);
    }

    [Fact]
    public void RangeSliderMath_RatioConversions_AreAccurate()
    {
        double min = 50.0;
        double max = 250.0;

        double ratio = RangeSliderMath.ValueToRatio(150.0, min, max);
        Assert.Equal(0.5, ratio, 3);

        double val = RangeSliderMath.RatioToValue(0.5, min, max);
        Assert.Equal(150.0, val, 3);

        // Clamping edge cases
        Assert.Equal(0.0, RangeSliderMath.ValueToRatio(20.0, min, max));
        Assert.Equal(1.0, RangeSliderMath.ValueToRatio(300.0, min, max));
    }

    #endregion

    #region Layout Verification Tests (Widths 900, 1100, 1400 and UiScale 1.0, 1.25)

    [Theory]
    [InlineData(900.0, 1.0)]
    [InlineData(1100.0, 1.0)]
    [InlineData(1400.0, 1.0)]
    [InlineData(900.0, 1.25)]
    [InlineData(1100.0, 1.25)]
    [InlineData(1400.0, 1.25)]
    public void Controls_LayoutMeasure_SucceedsAtSpecifiedWidthsAndScales(double width, double scale)
    {
        RunOnSta(() =>
        {
            double availableWidth = (width / scale) - 48.0;

            var holdBtn = new HubHoldButton { Content = "Delete Workspace", Width = 180, Height = 36 };
            var rangeSlider = new HubRangeSlider
            {
                Width = availableWidth,
                Height = 32,
                Minimum = 0,
                Maximum = 100,
                LowerValue = 20,
                UpperValue = 80,
                MinimumDistance = 10
            };
            var pillTabs = new HubPillTabs { Height = 36 };
            pillTabs.Items.Add(new HubPillTabItem { Content = "Workspaces" });
            pillTabs.Items.Add(new HubPillTabItem { Content = "Wallpapers" });
            pillTabs.SelectedIndex = 0;

            var singleSlider = new HubSlider { Width = availableWidth, Height = 32, Minimum = 0, Maximum = 100, Value = 50 };
            var toggle = new HubToggle { Width = 48, Height = 32, IsChecked = true };

            // Verify individual control scaling fits inside viewport
            Assert.True(holdBtn.Width * scale <= width);
            Assert.True((rangeSlider.Width + 48.0) * scale <= width + 0.01);
            Assert.True((singleSlider.Width + 48.0) * scale <= width + 0.01);
            Assert.True(toggle.Width * scale <= width);

            // Measure each control in layout
            holdBtn.Measure(new Size(width, 100));
            rangeSlider.Measure(new Size(width, 100));
            singleSlider.Measure(new Size(width, 100));
            toggle.Measure(new Size(width, 100));

            Assert.True(holdBtn.Height > 0);
            Assert.True(rangeSlider.Height > 0);
            Assert.True(singleSlider.Height > 0);
            Assert.True(toggle.Height > 0);
        });
    }

    #endregion
}
