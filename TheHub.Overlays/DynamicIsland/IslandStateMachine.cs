using TheHub.Common.Models;

namespace TheHub.Overlays.DynamicIsland;

public enum IslandState
{
    // Compact ambient / persistent
    CompactIdle,
    CompactMusic,

    // Compact alerts
    CompactVolume,
    CompactMicMuted,

    // Events (toast / temporary)
    EventUsb,
    EventDownload,
    EventClipboard,
    EventPrayer,
    EventTimer,

    // Expanded modals
    ExpandedMedia,
    ExpandedControls,
    ExpandedWorkspaces
}

/// <summary>
/// Pure C# state machine governing Dynamic Island state transitions, preemption, and timers.
/// Contains zero WPF dependencies for fast and rigorous unit testing.
/// </summary>
public class IslandStateMachine
{
    public IslandState CurrentState { get; private set; } = IslandState.CompactIdle;
    public IslandState PersistentState { get; private set; } = IslandState.CompactIdle;
    public object? PersistentData { get; private set; }
    public object? CurrentData { get; private set; }

    public bool IsHovered { get; private set; }
    public double RemainingTimeMs { get; private set; }
    public int DefaultDurationMs { get; set; } = 2500;

    public event Action<IslandState, object?>? StateChanged;

    public static int GetPriority(IslandState state)
    {
        return state switch
        {
            IslandState.CompactMicMuted => 50,
            IslandState.EventPrayer => 50,
            IslandState.EventUsb => 40,
            IslandState.EventDownload => 40,
            IslandState.CompactVolume => 30,
            IslandState.EventClipboard => 20,
            IslandState.EventTimer => 20,
            IslandState.CompactMusic => 10,
            IslandState.CompactIdle => 0,
            IslandState.ExpandedMedia => 100,
            IslandState.ExpandedControls => 100,
            IslandState.ExpandedWorkspaces => 100,
            _ => 0
        };
    }

    public static bool IsTransient(IslandState state)
    {
        return state is IslandState.CompactVolume
            or IslandState.CompactMicMuted
            or IslandState.EventUsb
            or IslandState.EventDownload
            or IslandState.EventClipboard
            or IslandState.EventPrayer
            or IslandState.EventTimer;
    }

    public static bool IsExpanded(IslandState state)
    {
        return state is IslandState.ExpandedMedia
            or IslandState.ExpandedControls
            or IslandState.ExpandedWorkspaces;
    }

    public void SetPersistentState(IslandState state, object? data = null)
    {
        PersistentState = state;
        PersistentData = data;
        if (!IsTransient(CurrentState) && !IsExpanded(CurrentState))
        {
            TransitionTo(state, data, 0);
        }
    }

    public bool PostEvent(IslandEventType type, object? data, int? customDurationMs = null)
    {
        var targetState = MapEventTypeToState(type);
        int targetPriority = GetPriority(targetState);
        int currentPriority = GetPriority(CurrentState);

        int duration = customDurationMs ?? DefaultDurationMs;

        // If currently expanded, do not preempt unless modal is dismissed
        if (IsExpanded(CurrentState))
        {
            return false;
        }

        // Same state: update in place and reset timer
        if (CurrentState == targetState)
        {
            CurrentData = data;
            RemainingTimeMs = duration;
            StateChanged?.Invoke(CurrentState, CurrentData);
            return true;
        }

        // Higher or equal priority: preempt immediately
        if (targetPriority >= currentPriority)
        {
            TransitionTo(targetState, data, duration);
            return true;
        }

        return false;
    }

    public void Expand(IslandState targetExpandedState, object? data = null)
    {
        if (IsExpanded(targetExpandedState))
        {
            TransitionTo(targetExpandedState, data, 0);
        }
    }

    public void Collapse()
    {
        TransitionTo(PersistentState, PersistentData, 0);
    }

    public void SetHover(bool isHovered)
    {
        IsHovered = isHovered;
    }

    public void Tick(double deltaMs)
    {
        if (IsHovered) return; // Hover pauses the dismiss timer

        if (IsTransient(CurrentState))
        {
            RemainingTimeMs -= deltaMs;
            if (RemainingTimeMs <= 0)
            {
                Dismiss();
            }
        }
    }

    public void Dismiss()
    {
        RemainingTimeMs = 0;
        TransitionTo(PersistentState, PersistentData, 0);
    }

    private void TransitionTo(IslandState state, object? data, double durationMs)
    {
        CurrentState = state;
        CurrentData = data;
        RemainingTimeMs = durationMs;
        StateChanged?.Invoke(CurrentState, CurrentData);
    }

    private static IslandState MapEventTypeToState(IslandEventType type)
    {
        return type switch
        {
            IslandEventType.Volume => IslandState.CompactVolume,
            IslandEventType.Media => IslandState.CompactMusic,
            IslandEventType.Clipboard => IslandState.EventClipboard,
            IslandEventType.Usb => IslandState.EventUsb,
            IslandEventType.SystemAlert => IslandState.CompactMicMuted,
            IslandEventType.Timer => IslandState.EventTimer,
            _ => IslandState.CompactIdle
        };
    }
}
