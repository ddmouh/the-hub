using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using TheHub.Core.Native;

namespace TheHub.Core.Services;

/// <summary>
/// High-performance, thread-safe asynchronous Windows CoreAudio service.
/// Interacts directly with IMMDeviceEnumerator, IAudioEndpointVolume, and IPolicyConfig COM interfaces.
/// </summary>
public interface IAudioService
{
    Task<(int Volume, bool IsMuted)> GetMasterVolumeStateAsync();
    Task SetMasterVolumeAsync(int volumePercent);
    Task<(int Volume, bool IsMuted)> ToggleMasterMuteAsync();
    Task<bool> GetMicrophoneMuteStateAsync();
    Task<bool> ToggleMicrophoneMuteAsync();
    Task<bool> SetMicrophoneMuteAsync(bool isMuted);
    Task<(bool MasterMuted, bool MicMuted)> SetDeafenAsync(bool isDeafened);
    Task<IReadOnlyList<AudioDeviceInfo>> GetPlaybackDevicesAsync();
    Task<IReadOnlyList<AudioDeviceInfo>> GetRecordingDevicesAsync();
    Task<bool> SetDefaultDeviceAsync(string deviceId, bool isCapture = false);
}

public class AudioService : IAudioService
{
    private static readonly Guid IID_IAudioEndpointVolume = typeof(IAudioEndpointVolume).GUID;

    public Task<(int Volume, bool IsMuted)> GetMasterVolumeStateAsync()
    {
        return Task.Run(() =>
        {
            try
            {
                var endpoint = GetEndpointVolume(dataFlow: 0 /* eRender */);
                if (endpoint != null)
                {
                    endpoint.GetMasterVolumeLevelScalar(out float level);
                    endpoint.GetMute(out bool isMuted);
                    int vol = (int)Math.Round(level * 100f);
                    return (Math.Clamp(vol, 0, 100), isMuted);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AudioService] GetMasterVolumeState error: {ex.Message}");
            }
            return (50, false);
        });
    }

    public Task SetMasterVolumeAsync(int volumePercent)
    {
        return Task.Run(() =>
        {
            try
            {
                int clamped = Math.Clamp(volumePercent, 0, 100);
                float scalar = clamped / 100.0f;
                var endpoint = GetEndpointVolume(dataFlow: 0 /* eRender */);
                if (endpoint != null)
                {
                    var emptyGuid = Guid.Empty;
                    endpoint.SetMasterVolumeLevelScalar(scalar, ref emptyGuid);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AudioService] SetMasterVolume error: {ex.Message}");
            }
        });
    }

    public Task<(int Volume, bool IsMuted)> ToggleMasterMuteAsync()
    {
        return Task.Run(() =>
        {
            try
            {
                var endpoint = GetEndpointVolume(dataFlow: 0 /* eRender */);
                if (endpoint != null)
                {
                    endpoint.GetMute(out bool currentMute);
                    bool nextMute = !currentMute;
                    var emptyGuid = Guid.Empty;
                    endpoint.SetMute(nextMute, ref emptyGuid);

                    endpoint.GetMasterVolumeLevelScalar(out float currentVol);
                    int vol = (int)Math.Round(currentVol * 100f);
                    return (Math.Clamp(vol, 0, 100), nextMute);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AudioService] ToggleMasterMute error: {ex.Message}");
            }
            return (50, false);
        });
    }

    public Task<bool> GetMicrophoneMuteStateAsync()
    {
        return Task.Run(() =>
        {
            try
            {
                var endpoint = GetEndpointVolume(dataFlow: 1 /* eCapture */);
                if (endpoint != null)
                {
                    endpoint.GetMute(out bool isMuted);
                    return isMuted;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AudioService] GetMicrophoneMuteState error: {ex.Message}");
            }
            return false;
        });
    }

    public Task<bool> ToggleMicrophoneMuteAsync()
    {
        return Task.Run(() =>
        {
            try
            {
                var endpoint = GetEndpointVolume(dataFlow: 1 /* eCapture */);
                if (endpoint != null)
                {
                    endpoint.GetMute(out bool currentMute);
                    bool nextMute = !currentMute;
                    var emptyGuid = Guid.Empty;
                    endpoint.SetMute(nextMute, ref emptyGuid);
                    return nextMute;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AudioService] ToggleMicrophoneMute error: {ex.Message}");
            }
            return false;
        });
    }

    public Task<bool> SetMicrophoneMuteAsync(bool isMuted)
    {
        return Task.Run(() =>
        {
            try
            {
                var endpoint = GetEndpointVolume(dataFlow: 1 /* eCapture */);
                if (endpoint != null)
                {
                    var emptyGuid = Guid.Empty;
                    endpoint.SetMute(isMuted, ref emptyGuid);
                    return isMuted;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AudioService] SetMicrophoneMute error: {ex.Message}");
            }
            return false;
        });
    }

    public Task<(bool MasterMuted, bool MicMuted)> SetDeafenAsync(bool isDeafened)
    {
        return Task.Run(() =>
        {
            try
            {
                var emptyGuid = Guid.Empty;
                var renderEndpoint = GetEndpointVolume(0 /* eRender */);
                renderEndpoint?.SetMute(isDeafened, ref emptyGuid);

                // Mic is completely independent; read its current state without modifying it
                bool currentMicMute = false;
                var captureEndpoint = GetEndpointVolume(1 /* eCapture */);
                captureEndpoint?.GetMute(out currentMicMute);

                return (isDeafened, currentMicMute);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AudioService] SetDeafen error: {ex.Message}");
                return (false, false);
            }
        });
    }

    public Task<IReadOnlyList<AudioDeviceInfo>> GetPlaybackDevicesAsync()
    {
        return Task.Run<IReadOnlyList<AudioDeviceInfo>>(() => AudioDeviceManager.GetPlaybackDevices());
    }

    public Task<IReadOnlyList<AudioDeviceInfo>> GetRecordingDevicesAsync()
    {
        return Task.Run<IReadOnlyList<AudioDeviceInfo>>(() => AudioDeviceManager.GetRecordingDevices());
    }

    public Task<bool> SetDefaultDeviceAsync(string deviceId, bool isCapture = false)
    {
        return Task.Run(() =>
        {
            if (string.IsNullOrWhiteSpace(deviceId)) return false;
            bool ok = AudioDeviceManager.SetDefaultDevice(deviceId);
            SystemControlHelper.InvalidateAudioCache();
            return ok;
        });
    }

    private static IAudioEndpointVolume? GetEndpointVolume(int dataFlow)
    {
        try
        {
            var enumerator = ComObjectFactory.CreateMMDeviceEnumerator();
            int hr = enumerator.GetDefaultAudioEndpoint(dataFlow, 1 /* eMultimedia */, out var device);
            if (hr != 0 || device == null) return null;

            var iid = typeof(IAudioEndpointVolume).GUID;
            int hrAct = device.Activate(ref iid, 1 /* CLSCTX_INPROC_SERVER */, IntPtr.Zero, out var iface);
            if (hrAct != 0 || iface == null) return null;

            return (IAudioEndpointVolume)iface;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AudioService] GetEndpointVolume({dataFlow}) error: {ex.Message}");
            return null;
        }
    }
}
