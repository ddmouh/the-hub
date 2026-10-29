namespace TheHub.Design.Controls;

public enum HoldButtonState
{
    Idle,
    Holding,
    Releasing,
    Confirmed
}

public enum HubHoldButtonVariant
{
    Danger,
    Neutral
}

/// <summary>
/// Pure mathematical state machine for HubHoldButton.
/// Decoupled from WPF UI for full unit testing.
/// </summary>
public class HoldButtonStateMachine
{
    public double HoldDurationMs { get; set; } = 1200.0;
    public double ConfirmedDurationMs { get; set; } = 1200.0;
    public double ReleaseRate { get; set; } = 3.0; // Decay speed factor on early release

    public HoldButtonState State { get; private set; } = HoldButtonState.Idle;
    public double Progress { get; private set; } = 0.0; // 0.0 to 1.0

    private double _elapsedMs = 0.0;
    private double _confirmedElapsedMs = 0.0;

    public event Action? Confirmed;
    public event Action? ResetToIdle;

    public bool Press()
    {
        if (State == HoldButtonState.Confirmed) return false;
        if (State == HoldButtonState.Holding) return false;

        State = HoldButtonState.Holding;
        return true;
    }

    public bool Release()
    {
        if (State != HoldButtonState.Holding) return false;

        State = HoldButtonState.Releasing;
        return true;
    }

    public void Reset()
    {
        State = HoldButtonState.Idle;
        _elapsedMs = 0.0;
        _confirmedElapsedMs = 0.0;
        Progress = 0.0;
        ResetToIdle?.Invoke();
    }

    /// <summary>
    /// Advance the state machine by dtMs milliseconds.
    /// </summary>
    /// <returns>True if progress or state changed.</returns>
    public bool Step(double dtMs)
    {
        if (dtMs <= 0) return false;

        switch (State)
        {
            case HoldButtonState.Holding:
                _elapsedMs += dtMs;
                if (_elapsedMs >= HoldDurationMs)
                {
                    _elapsedMs = HoldDurationMs;
                    Progress = 1.0;
                    State = HoldButtonState.Confirmed;
                    _confirmedElapsedMs = 0.0;
                    Confirmed?.Invoke();
                    return true;
                }
                Progress = Math.Clamp(_elapsedMs / Math.Max(1.0, HoldDurationMs), 0.0, 1.0);
                return true;

            case HoldButtonState.Releasing:
                _elapsedMs -= dtMs * ReleaseRate;
                if (_elapsedMs <= 0.0)
                {
                    _elapsedMs = 0.0;
                    Progress = 0.0;
                    State = HoldButtonState.Idle;
                    ResetToIdle?.Invoke();
                    return true;
                }
                Progress = Math.Clamp(_elapsedMs / Math.Max(1.0, HoldDurationMs), 0.0, 1.0);
                return true;

            case HoldButtonState.Confirmed:
                _confirmedElapsedMs += dtMs;
                if (_confirmedElapsedMs >= ConfirmedDurationMs)
                {
                    Reset();
                    return true;
                }
                return false;

            case HoldButtonState.Idle:
            default:
                return false;
        }
    }
}
