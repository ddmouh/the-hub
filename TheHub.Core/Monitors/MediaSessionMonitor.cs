using Windows.Media.Control;
using TheHub.Common.Models;
using TheHub.Core.Bus;

namespace TheHub.Core.Monitors;

public class MediaSessionMonitor : IDisposable
{
    private GlobalSystemMediaTransportControlsSessionManager? _sessionManager;
    private GlobalSystemMediaTransportControlsSession? _currentSession;
    private readonly System.Threading.Timer _refreshTimer;
    private readonly IHubBus _bus;
    private string? _lastBroadcastTrack;

    public event Action<DynamicIslandEvent>? MediaEventOccurred;

    public MediaSessionMonitor(IHubBus? bus = null)
    {
        _bus = bus ?? HubBus.Default;
        _refreshTimer = new System.Threading.Timer(OnPeriodicCheck, null, Timeout.Infinite, Timeout.Infinite);
    }

    public async Task InitializeAsync()
    {
        try
        {
            _sessionManager = await Task.Run(async () =>
            {
                using var cts = new CancellationTokenSource(3000);
                var task = GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask(cts.Token);
                return await task;
            });

            if (_sessionManager != null)
            {
                _sessionManager.CurrentSessionChanged += OnCurrentSessionChanged;
                UpdateCurrentSession(_sessionManager.GetCurrentSession());
            }

            _refreshTimer.Change(2000, 2000);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MediaMonitor] Failed to initialize: {ex.Message}");
        }
    }

    private void OnPeriodicCheck(object? state)
    {
        try
        {
            if (_sessionManager != null)
            {
                var active = _sessionManager.GetCurrentSession();
                if (active != _currentSession)
                {
                    UpdateCurrentSession(active);
                }
            }
        }
        catch { }
    }

    private void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args)
    {
        UpdateCurrentSession(sender.GetCurrentSession());
    }

    private void UpdateCurrentSession(GlobalSystemMediaTransportControlsSession? session)
    {
        if (_currentSession != null)
        {
            _currentSession.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            _currentSession.PlaybackInfoChanged -= OnPlaybackInfoChanged;
        }

        _currentSession = session;

        if (_currentSession != null)
        {
            _currentSession.MediaPropertiesChanged += OnMediaPropertiesChanged;
            _currentSession.PlaybackInfoChanged += OnPlaybackInfoChanged;
            _ = BroadcastCurrentMediaAsync();
        }
    }

    private void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
    {
        _ = BroadcastCurrentMediaAsync();
    }

    private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args)
    {
        _ = BroadcastCurrentMediaAsync();
    }

    public async Task BroadcastCurrentMediaAsync()
    {
        if (_currentSession == null) return;

        try
        {
            var props = await _currentSession.TryGetMediaPropertiesAsync();
            var playback = _currentSession.GetPlaybackInfo();

            if (props != null && !string.IsNullOrEmpty(props.Title))
            {
                bool isPlaying = playback?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                string trackSignature = $"{props.Title}_{props.Artist}_{isPlaying}";
                if (trackSignature == _lastBroadcastTrack) return;
                _lastBroadcastTrack = trackSignature;

                _bus.Publish(new MediaChanged(
                    Title: props.Title,
                    Artist: props.Artist,
                    IsPlaying: isPlaying,
                    Glyph: "🎵"
                ));

                var evt = new DynamicIslandEvent
                {
                    Type = IslandEventType.Media,
                    Title = props.Title,
                    Subtitle = string.IsNullOrEmpty(props.Artist) ? "Now Playing" : props.Artist,
                    Glyph = "🎵",
                    IsPlaying = isPlaying,
                    IsPersistent = isPlaying,
                    DisplayDurationMs = isPlaying ? 4000 : 2500
                };

                MediaEventOccurred?.Invoke(evt);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MediaMonitor] Error broadcasting media: {ex.Message}");
        }
    }

    public async Task TogglePlayPauseAsync()
    {
        if (_currentSession != null)
        {
            try
            {
                await _currentSession.TryTogglePlayPauseAsync();
            }
            catch { }
        }
    }

    public async Task SkipNextAsync()
    {
        if (_currentSession != null)
        {
            try
            {
                await _currentSession.TrySkipNextAsync();
            }
            catch { }
        }
    }

    public async Task SkipPreviousAsync()
    {
        if (_currentSession != null)
        {
            try
            {
                await _currentSession.TrySkipPreviousAsync();
            }
            catch { }
        }
    }

    public void Dispose()
    {
        _refreshTimer.Dispose();
    }
}
