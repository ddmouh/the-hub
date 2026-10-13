using System.Diagnostics;
using System.Text;
using TheHub.Core.Bridge;

namespace TheHub.Core.Diagnostics;

public class UiWatchdog : IDisposable
{
    private readonly IUiDispatcher _uiDispatcher;
    private readonly TimeSpan _timeout;
    private readonly Action<string> _logger;
    private readonly System.Threading.Timer _timer;
    private long _lastPingSentTicks;
    private long _lastPongReceivedTicks;
    private int _isPinging;
    private bool _hasLoggedUnresponsive;
    private bool _isDisposed;

    public event Action<TimeSpan, IReadOnlyList<BreadcrumbEntry>>? UnresponsiveDetected;

    public UiWatchdog(
        IUiDispatcher? uiDispatcher = null,
        TimeSpan? timeout = null,
        Action<string>? logger = null)
    {
        _uiDispatcher = uiDispatcher ?? new WpfUiDispatcher();
        _timeout = timeout ?? TimeSpan.FromSeconds(1.5);
        _logger = logger ?? (msg => AppDiagnostics.Log($"[UI WATCHDOG] {msg}"));

        long now = Stopwatch.GetTimestamp();
        _lastPingSentTicks = now;
        _lastPongReceivedTicks = now;

        _timer = new System.Threading.Timer(OnWatchdogTick, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    private void OnWatchdogTick(object? state)
    {
        if (_isDisposed) return;

        long now = Stopwatch.GetTimestamp();
        double elapsedSincePong = (double)(now - _lastPongReceivedTicks) / Stopwatch.Frequency;

        if (elapsedSincePong > _timeout.TotalSeconds)
        {
            if (!_hasLoggedUnresponsive)
            {
                _hasLoggedUnresponsive = true;
                ReportUnresponsiveness(TimeSpan.FromSeconds(elapsedSincePong));
            }
        }
        else
        {
            _hasLoggedUnresponsive = false;
        }

        // Send a new ping if not currently waiting
        if (Interlocked.CompareExchange(ref _isPinging, 1, 0) == 0)
        {
            _lastPingSentTicks = Stopwatch.GetTimestamp();
            try
            {
                _uiDispatcher.InvokeAsync(() =>
                {
                    _lastPongReceivedTicks = Stopwatch.GetTimestamp();
                    Interlocked.Exchange(ref _isPinging, 0);
                });
            }
            catch
            {
                Interlocked.Exchange(ref _isPinging, 0);
            }
        }
    }

    private void ReportUnresponsiveness(TimeSpan stallDuration)
    {
        var breadcrumbs = Breadcrumbs.GetRecent(20);
        UnresponsiveDetected?.Invoke(stallDuration, breadcrumbs);

        var sb = new StringBuilder();
        sb.AppendLine($"UI thread stall detected! Unresponsive for {stallDuration.TotalMilliseconds:F0}ms (Threshold: {_timeout.TotalMilliseconds}ms).");
        sb.AppendLine("Recent Breadcrumbs:");
        foreach (var b in breadcrumbs)
        {
            sb.AppendLine($"  [{b.Timestamp:HH:mm:ss.fff}] {b.Message}");
        }

        sb.AppendLine($"Active Process Threads: {Process.GetCurrentProcess().Threads.Count}");
        _logger(sb.ToString());
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _timer.Dispose();
    }
}
