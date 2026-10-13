using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Threading;

namespace TheHub.Core.Bridge;

public interface IUiDispatcher
{
    void InvokeAsync(Action action, DispatcherPriority priority = DispatcherPriority.Normal);
}

public class WpfUiDispatcher : IUiDispatcher
{
    private readonly Dispatcher _dispatcher;

    public WpfUiDispatcher(Dispatcher? dispatcher = null)
    {
        _dispatcher = dispatcher ?? Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
    }

    public void InvokeAsync(Action action, DispatcherPriority priority = DispatcherPriority.Normal)
    {
        if (Application.Current?.Dispatcher != null && !Application.Current.Dispatcher.HasShutdownStarted)
        {
            Application.Current.Dispatcher.InvokeAsync(action, priority);
        }
        else
        {
            // Headless / unit testing environment without active WPF Application pump
            Task.Run(action);
        }
    }
}

/// <summary>
/// Sole authority for marshaling data and notifications from background/COM threads to the UI thread.
/// Enforces non-blocking InvokeAsync with batching and per-key coalescing.
/// </summary>
public class UiBridge
{
    private static UiBridge? _instance;
    public static UiBridge Instance => _instance ??= new UiBridge();

    private readonly IUiDispatcher _dispatcher;
    private readonly ConcurrentDictionary<string, Action> _coalescedActions = new();
    private readonly ConcurrentDictionary<string, byte> _pendingKeys = new();

    public UiBridge(IUiDispatcher? dispatcher = null)
    {
        _dispatcher = dispatcher ?? new WpfUiDispatcher();
    }

    public static void Initialize(IUiDispatcher dispatcher)
    {
        _instance = new UiBridge(dispatcher);
    }

    /// <summary>
    /// Posts a standard UI action asynchronously without blocking the caller.
    /// </summary>
    public void Post(Action action)
    {
        _dispatcher.InvokeAsync(action, DispatcherPriority.Normal);
    }

    /// <summary>
    /// Invokes a UI action asynchronously and returns a Task that completes when the action finishes.
    /// </summary>
    public Task InvokeAsync(Action action, DispatcherPriority priority = DispatcherPriority.Normal)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _dispatcher.InvokeAsync(() =>
        {
            try
            {
                action();
                tcs.SetResult();
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        }, priority);
        return tcs.Task;
    }

    /// <summary>
    /// Invokes a UI function asynchronously and returns a Task with the result.
    /// </summary>
    public Task<T> InvokeAsync<T>(Func<T> func, DispatcherPriority priority = DispatcherPriority.Normal)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _dispatcher.InvokeAsync(() =>
        {
            try
            {
                tcs.SetResult(func());
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        }, priority);
        return tcs.Task;
    }

    /// <summary>
    /// Posts a high-frequency action (e.g. volume slider, media progress).
    /// If multiple calls occur before the UI thread executes, only the latest action for the key runs.
    /// </summary>
    public void PostCoalesced(string key, Action action)
    {
        _coalescedActions[key] = action;

        if (_pendingKeys.TryAdd(key, 0))
        {
            _dispatcher.InvokeAsync(() =>
            {
                _pendingKeys.TryRemove(key, out _);
                if (_coalescedActions.TryRemove(key, out var latestAction))
                {
                    try
                    {
                        latestAction();
                    }
                    catch (Exception)
                    {
                        // Prevent UI dispatch failure from propagating
                    }
                }
            }, DispatcherPriority.Normal);
        }
    }

    /// <summary>
    /// Batches multiple actions into a single invocation.
    /// </summary>
    public void PostBatch(IEnumerable<Action> actions)
    {
        var list = actions.ToList();
        if (list.Count == 0) return;

        _dispatcher.InvokeAsync(() =>
        {
            foreach (var act in list)
            {
                try
                {
                    act();
                }
                catch (Exception)
                {
                }
            }
        }, DispatcherPriority.Normal);
    }
}
