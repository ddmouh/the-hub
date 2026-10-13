namespace TheHub.Core.Bus;

/// <summary>
/// Marker interface for all strongly-typed events transmitted over HubBus.
/// </summary>
public interface IHubEvent
{
}

/// <summary>
/// Thread-safe, non-blocking in-process event bus based on System.Threading.Channels.
/// Can be safely called from COM callbacks, background worker threads, and UI threads.
/// </summary>
public interface IHubBus : IDisposable
{
    void Publish<T>(T evt) where T : IHubEvent;
    IDisposable Subscribe<T>(Action<T> handler) where T : IHubEvent;
}
