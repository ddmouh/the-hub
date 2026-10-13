using TheHub.Core.Bus;

namespace TheHub.Core.Services;

public enum PomodoroPhase
{
    Focus,
    ShortBreak,
    LongBreak
}

/// <summary>
/// Pomodoro focus and break management service.
/// Publishes PomodoroTick events to IHubBus every second while running.
/// </summary>
public class PomodoroService : IDisposable
{
    private readonly IHubBus _bus;
    private readonly System.Threading.Timer _timer;
    private readonly object _lock = new();

    public int FocusMinutes { get; set; } = 25;
    public int ShortBreakMinutes { get; set; } = 5;
    public int LongBreakMinutes { get; set; } = 15;
    public int SessionsBeforeLongBreak { get; set; } = 4;

    public PomodoroPhase CurrentPhase { get; private set; } = PomodoroPhase.Focus;
    public int RemainingSeconds { get; private set; }
    public bool IsRunning { get; private set; }
    public int CompletedSessions { get; private set; }

    public PomodoroService(IHubBus? bus = null)
    {
        _bus = bus ?? HubBus.Default;
        RemainingSeconds = FocusMinutes * 60;
        _timer = new System.Threading.Timer(OnTick, null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start()
    {
        lock (_lock)
        {
            if (RemainingSeconds <= 0)
            {
                RemainingSeconds = GetPhaseDurationSeconds(CurrentPhase);
            }

            IsRunning = true;
            _timer.Change(1000, 1000);
            BroadcastTick();
        }
    }

    public void Pause()
    {
        lock (_lock)
        {
            IsRunning = false;
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
            BroadcastTick();
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            IsRunning = false;
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
            RemainingSeconds = GetPhaseDurationSeconds(CurrentPhase);
            BroadcastTick();
        }
    }

    public void SkipToNextPhase()
    {
        lock (_lock)
        {
            AdvancePhase();
            RemainingSeconds = GetPhaseDurationSeconds(CurrentPhase);
            BroadcastTick();
        }
    }

    private void OnTick(object? state)
    {
        lock (_lock)
        {
            if (!IsRunning) return;

            RemainingSeconds--;
            if (RemainingSeconds <= 0)
            {
                AdvancePhase();
                RemainingSeconds = GetPhaseDurationSeconds(CurrentPhase);
            }

            BroadcastTick();
        }
    }

    private void AdvancePhase()
    {
        if (CurrentPhase == PomodoroPhase.Focus)
        {
            CompletedSessions++;
            if (CompletedSessions % SessionsBeforeLongBreak == 0)
            {
                CurrentPhase = PomodoroPhase.LongBreak;
            }
            else
            {
                CurrentPhase = PomodoroPhase.ShortBreak;
            }
        }
        else
        {
            CurrentPhase = PomodoroPhase.Focus;
        }
    }

    private int GetPhaseDurationSeconds(PomodoroPhase phase)
    {
        return phase switch
        {
            PomodoroPhase.Focus => FocusMinutes * 60,
            PomodoroPhase.ShortBreak => ShortBreakMinutes * 60,
            PomodoroPhase.LongBreak => LongBreakMinutes * 60,
            _ => 25 * 60
        };
    }

    private void BroadcastTick()
    {
        string phaseName = CurrentPhase switch
        {
            PomodoroPhase.Focus => "Focus",
            PomodoroPhase.ShortBreak => "Short Break",
            PomodoroPhase.LongBreak => "Long Break",
            _ => "Focus"
        };

        _bus.Publish(new PomodoroTick(RemainingSeconds, phaseName));
    }

    public void Dispose()
    {
        _timer.Dispose();
    }
}
