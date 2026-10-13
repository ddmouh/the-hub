using TheHub.Common.Models;
using TheHub.Overlays.DynamicIsland;
using Xunit;

namespace TheHub.Tests;

public class IslandStateMachineTests
{
    [Fact]
    public void InitialState_IsCompactIdle()
    {
        var sm = new IslandStateMachine();
        Assert.Equal(IslandState.CompactIdle, sm.CurrentState);
        Assert.Equal(IslandState.CompactIdle, sm.PersistentState);
    }

    [Fact]
    public void SetPersistentState_UpdatesCurrentWhenIdle()
    {
        var sm = new IslandStateMachine();
        sm.SetPersistentState(IslandState.CompactMusic, "TrackData");

        Assert.Equal(IslandState.CompactMusic, sm.CurrentState);
        Assert.Equal(IslandState.CompactMusic, sm.PersistentState);
    }

    [Fact]
    public void PostEvent_Volume_TransitionsToCompactVolume()
    {
        var sm = new IslandStateMachine();
        bool success = sm.PostEvent(IslandEventType.Volume, 75);

        Assert.True(success);
        Assert.Equal(IslandState.CompactVolume, sm.CurrentState);
        Assert.Equal(75, sm.CurrentData);
    }

    [Fact]
    public void PostEvent_MicMute_PreemptsVolume()
    {
        var sm = new IslandStateMachine();
        sm.PostEvent(IslandEventType.Volume, 50);
        Assert.Equal(IslandState.CompactVolume, sm.CurrentState);

        // SystemAlert (MicMuted) has higher priority (50 vs 30)
        sm.PostEvent(IslandEventType.SystemAlert, "Muted");
        Assert.Equal(IslandState.CompactMicMuted, sm.CurrentState);
    }

    [Fact]
    public void PostEvent_Usb_PreemptsVolume()
    {
        var sm = new IslandStateMachine();
        sm.PostEvent(IslandEventType.Volume, 50);

        // Usb has priority 40 vs Volume 30
        sm.PostEvent(IslandEventType.Usb, "Flash Drive");
        Assert.Equal(IslandState.EventUsb, sm.CurrentState);
    }

    [Fact]
    public void PostEvent_LowerPriorityDoesNotPreemptHigher()
    {
        var sm = new IslandStateMachine();
        sm.PostEvent(IslandEventType.SystemAlert, "Alert");
        Assert.Equal(IslandState.CompactMicMuted, sm.CurrentState);

        // Volume (30) cannot preempt SystemAlert (50)
        bool result = sm.PostEvent(IslandEventType.Volume, 60);
        Assert.False(result);
        Assert.Equal(IslandState.CompactMicMuted, sm.CurrentState);
    }

    [Fact]
    public void PostEvent_SameType_UpdatesInPlaceAndResetsTimer()
    {
        var sm = new IslandStateMachine();
        sm.PostEvent(IslandEventType.Volume, 40, 2000);

        // Step 1000ms
        sm.Tick(1000);
        Assert.Equal(1000, sm.RemainingTimeMs);

        // Post volume again -> updates in place and resets duration to 2000
        sm.PostEvent(IslandEventType.Volume, 80, 2000);
        Assert.Equal(IslandState.CompactVolume, sm.CurrentState);
        Assert.Equal(80, sm.CurrentData);
        Assert.Equal(2000, sm.RemainingTimeMs);
    }

    [Fact]
    public void Tick_TransientEvent_DismissesToPersistentStateWhenElapsed()
    {
        var sm = new IslandStateMachine();
        sm.SetPersistentState(IslandState.CompactIdle);
        sm.PostEvent(IslandEventType.Volume, 50, 1000);

        sm.Tick(500);
        Assert.Equal(IslandState.CompactVolume, sm.CurrentState);

        sm.Tick(600); // 1100ms total > 1000ms duration
        Assert.Equal(IslandState.CompactIdle, sm.CurrentState);
    }

    [Fact]
    public void Tick_HoverPausesDismissTimer()
    {
        var sm = new IslandStateMachine();
        sm.PostEvent(IslandEventType.Volume, 50, 1000);

        sm.SetHover(true);
        sm.Tick(1500); // Ticks while hovered

        // Must still be CompactVolume because timer is paused
        Assert.Equal(IslandState.CompactVolume, sm.CurrentState);
        Assert.Equal(1000, sm.RemainingTimeMs);
    }

    [Fact]
    public void Tick_HoverLeaveResumesDismissTimer()
    {
        var sm = new IslandStateMachine();
        sm.PostEvent(IslandEventType.Volume, 50, 1000);

        sm.SetHover(true);
        sm.Tick(500);
        sm.SetHover(false); // Mouse left

        sm.Tick(1100); // Timer resumes and expires
        Assert.Equal(IslandState.CompactIdle, sm.CurrentState);
    }

    [Fact]
    public void PersistentMusic_ReturnsAfterVolumeDismiss()
    {
        var sm = new IslandStateMachine();
        sm.SetPersistentState(IslandState.CompactMusic, "Song A");

        sm.PostEvent(IslandEventType.Volume, 70, 500);
        Assert.Equal(IslandState.CompactVolume, sm.CurrentState);

        sm.Tick(600);
        // Returns to music, not idle!
        Assert.Equal(IslandState.CompactMusic, sm.CurrentState);
        Assert.Equal("Song A", sm.CurrentData);
    }

    [Fact]
    public void PersistentMusic_ReturnsAfterUsbDismiss()
    {
        var sm = new IslandStateMachine();
        sm.SetPersistentState(IslandState.CompactMusic, "Song B");

        sm.PostEvent(IslandEventType.Usb, "Keyboard Connected", 400);
        Assert.Equal(IslandState.EventUsb, sm.CurrentState);

        sm.Tick(500);
        Assert.Equal(IslandState.CompactMusic, sm.CurrentState);
    }

    [Fact]
    public void Expand_TransitionsToExpandedMedia()
    {
        var sm = new IslandStateMachine();
        sm.Expand(IslandState.ExpandedMedia, "PlayerData");

        Assert.Equal(IslandState.ExpandedMedia, sm.CurrentState);
        Assert.Equal("PlayerData", sm.CurrentData);
    }

    [Fact]
    public void Collapse_ReturnsToPersistentState()
    {
        var sm = new IslandStateMachine();
        sm.SetPersistentState(IslandState.CompactMusic, "Song");
        sm.Expand(IslandState.ExpandedControls);

        Assert.Equal(IslandState.ExpandedControls, sm.CurrentState);

        sm.Collapse();
        Assert.Equal(IslandState.CompactMusic, sm.CurrentState);
    }

    [Fact]
    public void PostEvent_IgnoredWhileExpanded()
    {
        var sm = new IslandStateMachine();
        sm.Expand(IslandState.ExpandedMedia);

        bool posted = sm.PostEvent(IslandEventType.Volume, 50);
        Assert.False(posted);
        Assert.Equal(IslandState.ExpandedMedia, sm.CurrentState);
    }

    [Fact]
    public void Dismiss_ManuallyReturnsToPersistentState()
    {
        var sm = new IslandStateMachine();
        sm.PostEvent(IslandEventType.Clipboard, "Text Snippet", 5000);
        Assert.Equal(IslandState.EventClipboard, sm.CurrentState);

        sm.Dismiss();
        Assert.Equal(IslandState.CompactIdle, sm.CurrentState);
    }
}
