using System.Collections.Concurrent;
using System.Threading.Channels;

namespace TheHub.Core.Bus;

/// <summary>
/// High-performance asynchronous typed event bus built on System.Threading.Channels.
/// Guarantees non-blocking publish from any thread (including low-level COM callbacks)
/// and sequential ordering per publisher.
/// </summary>
public class HubBus : IHubBus
{
    public static HubBus Default { get; } = new();

    private readonly Channel<IHubEvent> _channel;
    private readonly ConcurrentDictionary<Type, List<Delegate>> _subscriptions = new();
    private readonly object _subLock = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _readerTask;
    private bool _isDisposed;

    public HubBus()
    {
        _channel = Channel.CreateUnbounded<IHubEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

        _readerTask = Task.Run(ReaderLoopAsync);
    }

    public void Publish<T>(T evt) where T : IHubEvent
    {
        if (_isDisposed) return;
        _channel.Writer.TryWrite(evt);
    }

    public IDisposable Subscribe<T>(Action<T> handler) where T : IHubEvent
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(HubBus));

        var eventType = typeof(T);
        lock (_subLock)
        {
            var list = _subscriptions.GetOrAdd(eventType, _ => new List<Delegate>());
            list.Add(handler);
        }

        return new SubscriptionToken(() =>
        {
            lock (_subLock)
            {
                if (_subscriptions.TryGetValue(eventType, out var list))
                {
                    list.Remove(handler);
                    if (list.Count == 0)
                    {
                        _subscriptions.TryRemove(eventType, out _);
                    }
                }
            }
        });
    }

    private async Task ReaderLoopAsync()
    {
        var reader = _channel.Reader;
        var token = _cts.Token;

        try
        {
            while (await reader.WaitToReadAsync(token).ConfigureAwait(false))
            {
                while (reader.TryRead(out var evt))
                {
                    DispatchEvent(evt);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
    }

    private void DispatchEvent(IHubEvent evt)
    {
        var eventType = evt.GetType();
        List<Delegate>? handlersCopy = null;

        lock (_subLock)
        {
            if (_subscriptions.TryGetValue(eventType, out var list) && list.Count > 0)
            {
                handlersCopy = new List<Delegate>(list);
            }
        }

        if (handlersCopy != null)
        {
            foreach (var handler in handlersCopy)
            {
                try
                {
                    handler.DynamicInvoke(evt);
                }
                catch (Exception)
                {
                    // A faulty subscriber must never crash the bus or prevent other subscribers from receiving events
                }
            }
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        _channel.Writer.TryComplete();
        _cts.Cancel();
        try
        {
            _readerTask.Wait(500);
        }
        catch { }

        _cts.Dispose();
        lock (_subLock)
        {
            _subscriptions.Clear();
        }
    }

    private sealed class SubscriptionToken : IDisposable
    {
        private Action? _unsubscribe;

        public SubscriptionToken(Action unsubscribe)
        {
            _unsubscribe = unsubscribe;
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref _unsubscribe, null)?.Invoke();
        }
    }
}
