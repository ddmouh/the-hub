using System.Runtime.InteropServices;
using TheHub.Common.Config;
using TheHub.Common.Models;
using TheHub.Core.Bus;
using TheHub.Core.Native;

namespace TheHub.Core.Monitors;

public class VolumeMonitor : IDisposable
{
    private readonly System.Threading.Timer _pollingTimer;
    private readonly IHubBus _bus;
    private int _lastVolume = -1;
    private bool _lastMute;
    private bool _isInitialized;
    private IAudioEndpointVolume? _audioEndpointVolume;
    private VolumeCallback? _callback;

    public event Action<DynamicIslandEvent>? VolumeEventOccurred;

    public VolumeMonitor(IHubBus? bus = null)
    {
        _bus = bus ?? HubBus.Default;
        _pollingTimer = new System.Threading.Timer(CheckVolumeState, null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start()
    {
        try
        {
            InitializeCoreAudio();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[VolumeMonitor] CoreAudio init failed: {ex.Message}");
        }

        // Poll every 250ms as an additional fail-safe
        _pollingTimer.Change(100, 250);
    }

    public void Stop()
    {
        _pollingTimer.Change(Timeout.Infinite, Timeout.Infinite);
        if (_audioEndpointVolume != null && _callback != null)
        {
            try
            {
                _audioEndpointVolume.UnregisterControlChangeNotify(_callback);
            }
            catch { }
        }
        _audioEndpointVolume = null;
        _callback = null;
        _isInitialized = false;
    }

    private void InitializeCoreAudio()
    {
        if (_isInitialized) return;

        var enumerator = ComObjectFactory.CreateMMDeviceEnumerator();
        // eRender = 0, eMultimedia = 1
        int hr = enumerator.GetDefaultAudioEndpoint(0, 1, out var device);
        if (hr == 0 && device != null)
        {
            var iid = typeof(IAudioEndpointVolume).GUID;
            hr = device.Activate(ref iid, 1 /* CLSCTX_INPROC_SERVER */, IntPtr.Zero, out var endpointObj);
            if (hr == 0 && endpointObj is IAudioEndpointVolume endpoint)
            {
                _audioEndpointVolume = endpoint;
                _callback = new VolumeCallback(this);
                _audioEndpointVolume.RegisterControlChangeNotify(_callback);
                _isInitialized = true;

                // Read initial state without firing event
                if (_audioEndpointVolume.GetMasterVolumeLevelScalar(out float vol) == 0)
                {
                    _lastVolume = (int)Math.Round(vol * 100.0f);
                }
                if (_audioEndpointVolume.GetMute(out bool mute) == 0)
                {
                    _lastMute = mute;
                }
            }
        }
    }

    private void CheckVolumeState(object? state)
    {
        if (!SettingsManager.Current.IslandShowVolume) return;

        try
        {
            if (!_isInitialized)
            {
                InitializeCoreAudio();
            }

            if (_audioEndpointVolume != null)
            {
                if (_audioEndpointVolume.GetMasterVolumeLevelScalar(out float vol) == 0 &&
                    _audioEndpointVolume.GetMute(out bool mute) == 0)
                {
                    int pct = (int)Math.Round(vol * 100.0f);
                    OnVolumeChanged(pct, mute);
                }
            }
        }
        catch
        {
            // Transient device switching or driver reload
            _isInitialized = false;
        }
    }

    internal void OnVolumeChanged(int volume, bool isMuted)
    {
        if (!SettingsManager.Current.IslandShowVolume) return;

        // Only fire if volume or mute status actually changed
        if (_lastVolume == -1)
        {
            _lastVolume = volume;
            _lastMute = isMuted;
            return;
        }

        if (volume == _lastVolume && isMuted == _lastMute)
        {
            return;
        }

        _lastVolume = volume;
        _lastMute = isMuted;

        _bus.Publish(new VolumeChanged(volume, isMuted));

        string glyph = isMuted ? "🔇" : (volume == 0 ? "🔈" : (volume > 50 ? "🔊" : "🔉"));
        string subtitle = isMuted ? "Muted" : $"{volume}%";

        VolumeEventOccurred?.Invoke(new DynamicIslandEvent
        {
            Type = IslandEventType.Volume,
            Title = isMuted ? "Volume Muted" : "Volume",
            Subtitle = subtitle,
            Glyph = glyph,
            Value = volume,
            DisplayDurationMs = 2200
        });
    }

    public void Dispose()
    {
        Stop();
        _pollingTimer.Dispose();
    }

    #region COM Interfaces

    [Guid("5BC64874-388D-4433-AC59-59E473330C29"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig]
        int RegisterControlChangeNotify(IAudioEndpointVolumeCallback pNotify);
        [PreserveSig]
        int UnregisterControlChangeNotify(IAudioEndpointVolumeCallback pNotify);
        [PreserveSig]
        int GetChannelCount(out int pnChannelCount);
        [PreserveSig]
        int SetMasterVolumeLevel(float fLevelDB, ref Guid pguidEventContext);
        [PreserveSig]
        int SetMasterVolumeLevelScalar(float fLevel, ref Guid pguidEventContext);
        [PreserveSig]
        int GetMasterVolumeLevel(out float pfLevelDB);
        [PreserveSig]
        int GetMasterVolumeLevelScalar(out float pfLevel);
        [PreserveSig]
        int SetMute([MarshalAs(UnmanagedType.Bool)] bool bMute, ref Guid pguidEventContext);
        [PreserveSig]
        int GetMute([MarshalAs(UnmanagedType.Bool)] out bool pbMute);
    }

    [Guid("657804FA-D6AD-4496-8A60-352752AF4F89"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolumeCallback
    {
        [PreserveSig]
        int OnNotify(IntPtr pNotify);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AUDIO_VOLUME_NOTIFICATION_DATA
    {
        public Guid guidEventContext;
        public bool bMuted;
        public float fMasterVolume;
        public uint nChannels;
    }

    private class VolumeCallback : IAudioEndpointVolumeCallback
    {
        private readonly VolumeMonitor _monitor;

        public VolumeCallback(VolumeMonitor monitor)
        {
            _monitor = monitor;
        }

        public int OnNotify(IntPtr pNotify)
        {
            try
            {
                if (pNotify != IntPtr.Zero)
                {
                    var data = Marshal.PtrToStructure<AUDIO_VOLUME_NOTIFICATION_DATA>(pNotify);
                    int pct = (int)Math.Round(data.fMasterVolume * 100.0f);
                    _monitor.OnVolumeChanged(pct, data.bMuted);
                }
            }
            catch { }
            return 0;
        }
    }

    #endregion
}
