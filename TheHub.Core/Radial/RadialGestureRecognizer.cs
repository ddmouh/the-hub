namespace TheHub.Core.Radial;

public enum RadialGestureState
{
    Idle,
    PendingHold,
    Active,
    Targeting,
    Confirmed,
    Cancelled
}

/// <summary>
/// Pure C# state machine and gesture recognizer for the Radial Launcher.
/// Governs hold detection, flick direction calculation, activation zones, and shortcuts.
/// Zero WPF dependencies for high speed, pure concurrency, and comprehensive unit testing.
/// </summary>
public class RadialGestureRecognizer
{
    public const int MouseButtonMiddle = 0;
    public const int MouseButtonRight = 1;
    public const int MouseButtonLeft = 2;
    public const int MouseButtonX1 = 3;
    public const int MouseButtonX2 = 4;

    public const int VkEscape = 0x1B;
    public const int VkZ = 0x5A;
    public const int VkSpace = 0x20;

    public RadialGestureState State { get; private set; } = RadialGestureState.Idle;

    public int HoldDelayMs { get; set; } = 120;
    public double ActivationZone { get; set; } = 60.0;
    public double CenterRadius { get; set; } = 60.0;
    public string GestureBehavior { get; set; } = "Hold"; // "Hold" or "Click"
    public int SliceCount { get; set; } = 6;
    public int TriggerButton { get; set; } = MouseButtonMiddle;

    public int CenterX { get; private set; }
    public int CenterY { get; private set; }
    public int CurrentX { get; private set; }
    public int CurrentY { get; private set; }
    public double Distance { get; private set; }
    public double AngleDegrees { get; private set; }
    public int SelectedSliceIndex { get; private set; } = -1;

    private long _pendingHoldStartMs;

    public event Action<int, int>? Opened;
    public event Action<int, int, double, double, int>? PointerMoved; // x, y, angle, distance, sliceIndex
    public event Action<int>? Confirmed; // sliceIndex
    public event Action? Cancelled;
    public event Action<RadialGestureState>? StateChanged;

    public bool ProcessMouseDown(int button, int x, int y, long timestampMs)
    {
        if (button != TriggerButton) return false;

        bool isClickMode = string.Equals(GestureBehavior, "Click", StringComparison.OrdinalIgnoreCase);

        if (isClickMode && (State == RadialGestureState.Active || State == RadialGestureState.Targeting))
        {
            // Second click in Click mode: confirm or cancel
            if (Distance >= ActivationZone && SelectedSliceIndex >= 0)
            {
                TransitionTo(RadialGestureState.Confirmed);
                Confirmed?.Invoke(SelectedSliceIndex);
            }
            else
            {
                TransitionTo(RadialGestureState.Cancelled);
                Cancelled?.Invoke();
            }
            TransitionTo(RadialGestureState.Idle);
            return true;
        }

        CenterX = x;
        CenterY = y;
        CurrentX = x;
        CurrentY = y;
        Distance = 0;
        AngleDegrees = 0;
        SelectedSliceIndex = -1;
        _pendingHoldStartMs = timestampMs;

        if (isClickMode || HoldDelayMs <= 0)
        {
            Activate(x, y);
        }
        else
        {
            TransitionTo(RadialGestureState.PendingHold);
        }

        return true;
    }

    public void CheckHoldTimer(long currentTimestampMs)
    {
        if (State == RadialGestureState.PendingHold)
        {
            if (currentTimestampMs - _pendingHoldStartMs >= HoldDelayMs)
            {
                Activate(CenterX, CenterY);
            }
        }
    }

    public bool ProcessMouseMove(int x, int y)
    {
        CurrentX = x;
        CurrentY = y;

        if (State != RadialGestureState.Active && State != RadialGestureState.Targeting && State != RadialGestureState.PendingHold)
        {
            return false;
        }

        double dx = x - CenterX;
        double dy = y - CenterY;
        Distance = Math.Sqrt(dx * dx + dy * dy);

        // Angle in standard compass/clock coordinates: 0° is North (12 o'clock), 90° is East, 180° is South, 270° is West
        double rad = Math.Atan2(dx, -dy);
        double deg = rad * (180.0 / Math.PI);
        if (deg < 0) deg += 360.0;
        AngleDegrees = deg;

        if (State == RadialGestureState.PendingHold)
        {
            // Moving significantly before hold expires cancels hold detection
            if (Distance > 15.0)
            {
                TransitionTo(RadialGestureState.Cancelled);
                TransitionTo(RadialGestureState.Idle);
                return false;
            }
            return true;
        }

        if (Distance >= ActivationZone)
        {
            if (State != RadialGestureState.Targeting)
            {
                TransitionTo(RadialGestureState.Targeting);
            }
            SelectedSliceIndex = CalculateSliceIndex(AngleDegrees, SliceCount);
        }
        else
        {
            if (State != RadialGestureState.Active)
            {
                TransitionTo(RadialGestureState.Active);
            }
            SelectedSliceIndex = -1;
        }

        PointerMoved?.Invoke(CurrentX, CurrentY, AngleDegrees, Distance, SelectedSliceIndex);
        return true;
    }

    public bool ProcessMouseUp(int button, int x, int y)
    {
        if (button != TriggerButton) return false;

        if (State == RadialGestureState.PendingHold)
        {
            // Released before hold duration reached -> cancelled
            TransitionTo(RadialGestureState.Cancelled);
            TransitionTo(RadialGestureState.Idle);
            return false;
        }

        if (string.Equals(GestureBehavior, "Click", StringComparison.OrdinalIgnoreCase))
        {
            // In click mode, releasing mouse does not confirm or cancel; wheel stays open
            return true;
        }

        ProcessMouseMove(x, y);

        if (State == RadialGestureState.Targeting && Distance >= ActivationZone && SelectedSliceIndex >= 0)
        {
            int selected = SelectedSliceIndex;
            TransitionTo(RadialGestureState.Confirmed);
            Confirmed?.Invoke(selected);
        }
        else
        {
            TransitionTo(RadialGestureState.Cancelled);
            Cancelled?.Invoke();
        }

        TransitionTo(RadialGestureState.Idle);
        return true;
    }

    public bool ProcessKeyDown(int vkCode, bool isAlt, bool isControl, bool isShift)
    {
        if (vkCode == VkEscape)
        {
            if (State == RadialGestureState.Active || State == RadialGestureState.Targeting || State == RadialGestureState.PendingHold)
            {
                Cancel();
                return true;
            }
            return false;
        }

        // Shortcut triggers (Alt+Z or Alt+Space)
        if (isAlt && (vkCode == VkZ || vkCode == VkSpace))
        {
            if (State == RadialGestureState.Idle)
            {
                Activate(CurrentX, CurrentY);
                return true;
            }
            else if (string.Equals(GestureBehavior, "Click", StringComparison.OrdinalIgnoreCase))
            {
                Cancel();
                return true;
            }
        }

        return false;
    }

    public void Cancel()
    {
        if (State != RadialGestureState.Idle)
        {
            TransitionTo(RadialGestureState.Cancelled);
            Cancelled?.Invoke();
            TransitionTo(RadialGestureState.Idle);
        }
    }

    public void TriggerAt(int x, int y)
    {
        CenterX = x;
        CenterY = y;
        CurrentX = x;
        CurrentY = y;
        Distance = 0;
        AngleDegrees = 0;
        SelectedSliceIndex = -1;
        Activate(x, y);
    }

    private void Activate(int x, int y)
    {
        TransitionTo(RadialGestureState.Active);
        Opened?.Invoke(x, y);
    }

    private void TransitionTo(RadialGestureState newState)
    {
        if (State == newState) return;
        State = newState;
        StateChanged?.Invoke(newState);
    }

    public static int CalculateSliceIndex(double angleDegrees, int sliceCount)
    {
        if (sliceCount <= 0) return 0;
        double sliceArc = 360.0 / sliceCount;
        double halfArc = sliceArc / 2.0;

        // Shift angle so that slice 0 is centered at top (0°)
        double normalized = (angleDegrees + halfArc) % 360.0;
        int idx = (int)(normalized / sliceArc);
        return Math.Clamp(idx, 0, sliceCount - 1);
    }
}
