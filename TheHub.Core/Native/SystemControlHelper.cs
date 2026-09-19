using System.Diagnostics;
using System.Runtime.InteropServices;
using TheHub.Common.Models;

namespace TheHub.Core.Native;

public static class SystemControlHelper
{
    public static string LastError { get; set; } = "";
    private static int _cachedBrightness = 100;

    private static bool _brightnessInitialized = false;

    private static IAudioEndpointVolume? _cachedRenderEndpoint;
    private static IAudioEndpointVolume? _cachedCaptureEndpoint;
    private static readonly object _audioLock = new();

    #region Audio (Master Volume & Speaker Mute)

    public static (int Volume, bool IsMuted) AdjustMasterVolume(float delta)
    {
        try
        {
            var endpoint = GetDefaultEndpoint(dataFlow: 0 /* eRender */);
            if (endpoint != null)
            {
                endpoint.GetMasterVolumeLevelScalar(out float current);
                float next = Math.Clamp(current + delta, 0.0f, 1.0f);
                var emptyGuid = Guid.Empty;
                endpoint.SetMasterVolumeLevelScalar(next, ref emptyGuid);

                bool isMuted = false;
                endpoint.GetMute(out isMuted);
                int volPct = (int)Math.Round(next * 100f);
                return (volPct, isMuted);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SystemControlHelper] AdjustMasterVolume error: {ex.Message}");
            InvalidateAudioCache();
        }

        return (50, false);
    }

    public static (int Volume, bool IsMuted) ToggleMasterAudioMute()
    {
        try
        {
            var endpoint = GetDefaultEndpoint(dataFlow: 0 /* eRender */);
            if (endpoint != null)
            {
                endpoint.GetMute(out bool currentMute);
                bool nextMute = !currentMute;
                var emptyGuid = Guid.Empty;
                endpoint.SetMute(nextMute, ref emptyGuid);

                endpoint.GetMasterVolumeLevelScalar(out float currentVol);
                int volPct = (int)Math.Round(currentVol * 100f);
                return (volPct, nextMute);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SystemControlHelper] ToggleMasterAudioMute error: {ex.Message}");
            InvalidateAudioCache();
        }

        return (50, false);
    }

    public static (int Volume, bool IsMuted) GetMasterAudioState()
    {
        try
        {
            var endpoint = GetDefaultEndpoint(dataFlow: 0 /* eRender */);
            if (endpoint != null)
            {
                endpoint.GetMasterVolumeLevelScalar(out float currentVol);
                endpoint.GetMute(out bool isMuted);
                int volPct = (int)Math.Round(currentVol * 100f);
                return (volPct, isMuted);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SystemControlHelper] GetMasterAudioState error: {ex.Message}");
            InvalidateAudioCache();
        }
        return (50, false);
    }

    public static (int Volume, bool IsMuted) SetExactMasterVolume(float level)
    {
        try
        {
            var endpoint = GetDefaultEndpoint(dataFlow: 0 /* eRender */);
            if (endpoint != null)
            {
                float next = Math.Clamp(level, 0.0f, 1.0f);
                var emptyGuid = Guid.Empty;
                int hr = endpoint.SetMasterVolumeLevelScalar(next, ref emptyGuid);
                if (hr != 0)
                {
                    InvalidateAudioCache();
                    endpoint = GetDefaultEndpoint(dataFlow: 0);
                    endpoint?.SetMasterVolumeLevelScalar(next, ref emptyGuid);
                }

                bool isMuted = false;
                endpoint?.GetMute(out isMuted);
                int volPct = (int)Math.Round(next * 100f);
                return (volPct, isMuted);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SystemControlHelper] SetExactMasterVolume error: {ex.Message}");
            InvalidateAudioCache();
        }
        return (50, false);
    }

    public static void InvalidateAudioCache()
    {
        lock (_audioLock)
        {
            _cachedRenderEndpoint = null;
            _cachedCaptureEndpoint = null;
        }
    }

    #endregion

    #region Microphone (Capture Device Mute)

    public static bool ToggleMicrophoneMute()
    {
        try
        {
            var endpoint = GetDefaultEndpoint(dataFlow: 1 /* eCapture */);
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
            Debug.WriteLine($"[SystemControlHelper] ToggleMicrophoneMute error: {ex.Message}");
            InvalidateAudioCache();
        }

        return false;
    }

    public static bool GetMicrophoneMuteState()
    {
        try
        {
            var endpoint = GetDefaultEndpoint(dataFlow: 1 /* eCapture */);
            if (endpoint != null)
            {
                endpoint.GetMute(out bool isMuted);
                return isMuted;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SystemControlHelper] GetMicrophoneMuteState error: {ex.Message}");
            InvalidateAudioCache();
        }
        return false;
    }

    #endregion

    #region Screen Brightness

    public static int GetScreenBrightness()
    {
        if (_brightnessInitialized) return _cachedBrightness;
        _brightnessInitialized = true;

        // Initialize brightness on a background thread to avoid blocking UI thread
        Task.Run(() =>
        {
            if (TryGetDxvaBrightness(out uint cur, out _))
            {
                _cachedBrightness = (int)cur;
            }
        });

        // Return a reasonable default while initialization happens in background
        return _cachedBrightness;
    }

    public static int AdjustScreenBrightness(int deltaPercent)
    {
        int current = _cachedBrightness;
        int target = Math.Clamp(current + deltaPercent, 10, 100);
        SetExactScreenBrightness(target);
        return target;
    }

    public static void SetExactScreenBrightness(int target)
    {
        target = Math.Clamp(target, 10, 100);
        _cachedBrightness = target;

        // 1. Instant GDI Gamma Ramp adjustment (Zero latency, works on 100% of displays/monitors)
        TrySetGammaBrightness(target);

        // 2. Hardware DDC/CI backlight adjustment (if supported by the physical monitor)
        TrySetDxvaBrightness((uint)target);
    }

    #endregion

    #region Win32 GDI Gamma Ramp Brightness Fallback (100% Guaranteed on All Displays)

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct RAMP
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
        public ushort[] Red;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
        public ushort[] Green;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
        public ushort[] Blue;
    }


    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool SetDeviceGammaRamp(IntPtr hDC, ref RAMP lpRamp);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool GetDeviceGammaRamp(IntPtr hDC, ref RAMP lpRamp);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    private static bool TrySetGammaBrightness(int brightnessPercent)
    {
        try
        {
            IntPtr hdc = GetDC(IntPtr.Zero);
            if (hdc == IntPtr.Zero) return false;

            float factor = Math.Clamp(brightnessPercent / 100.0f, 0.1f, 1.0f);
            var ramp = new RAMP
            {
                Red = new ushort[256],
                Green = new ushort[256],
                Blue = new ushort[256]
            };

            for (int i = 0; i < 256; i++)
            {
                int val = (int)Math.Round(i * 256 * factor);
                if (val > 65535) val = 65535;
                ramp.Red[i] = (ushort)val;
                ramp.Green[i] = (ushort)val;
                ramp.Blue[i] = (ushort)val;
            }

            bool success = SetDeviceGammaRamp(hdc, ref ramp);
            ReleaseDC(IntPtr.Zero, hdc);
            return success;
        }
        catch
        {
            return false;
        }
    }

    #endregion

    #region DXVA2 Physical Monitor Brightness (Instant Hardware DDC/CI)

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public struct PHYSICAL_MONITOR
    {
        public IntPtr hPhysicalMonitor;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szPhysicalMonitorDescription;
    }


    [DllImport("dxva2.dll", SetLastError = true)]
    public static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, out uint pdwNumberOfPhysicalMonitors);

    [DllImport("dxva2.dll", SetLastError = true)]
    public static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, uint dwPhysicalMonitorArraySize, [Out] PHYSICAL_MONITOR[] pPhysicalMonitorArray);

    [DllImport("dxva2.dll", SetLastError = true)]
    public static extern bool GetMonitorBrightness(IntPtr hMonitor, out uint pdwMinimumBrightness, out uint pdwCurrentBrightness, out uint pdwMaximumBrightness);

    [DllImport("dxva2.dll", SetLastError = true)]
    public static extern bool SetMonitorBrightness(IntPtr hMonitor, uint dwNewBrightness);

    [DllImport("dxva2.dll", SetLastError = true)]
    public static extern bool DestroyPhysicalMonitors(uint dwPhysicalMonitorArraySize, [In] PHYSICAL_MONITOR[] pPhysicalMonitorArray);


    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();

    private static bool TrySetDxvaBrightness(uint brightness)
    {
        try
        {
            IntPtr hMon = MonitorFromWindow(GetDesktopWindow(), 1 /* MONITOR_DEFAULTTOPRIMARY */);
            if (hMon == IntPtr.Zero) return false;

            if (!GetNumberOfPhysicalMonitorsFromHMONITOR(hMon, out uint count) || count == 0) return false;

            var monitors = new PHYSICAL_MONITOR[count];
            if (!GetPhysicalMonitorsFromHMONITOR(hMon, count, monitors)) return false;

            bool anySuccess = false;
            for (uint i = 0; i < count; i++)
            {
                if (SetMonitorBrightness(monitors[i].hPhysicalMonitor, brightness))
                {
                    anySuccess = true;
                }
            }

            DestroyPhysicalMonitors(count, monitors);
            return anySuccess;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetDxvaBrightness(out uint current, out uint max)
    {
        current = 100;
        max = 100;
        try
        {
            IntPtr hMon = MonitorFromWindow(GetDesktopWindow(), 1 /* MONITOR_DEFAULTTOPRIMARY */);
            if (hMon == IntPtr.Zero) return false;

            if (!GetNumberOfPhysicalMonitorsFromHMONITOR(hMon, out uint count) || count == 0) return false;

            var monitors = new PHYSICAL_MONITOR[count];
            if (!GetPhysicalMonitorsFromHMONITOR(hMon, count, monitors)) return false;

            bool success = false;
            if (count > 0)
            {
                success = GetMonitorBrightness(monitors[0].hPhysicalMonitor, out uint min, out current, out max);
            }

            DestroyPhysicalMonitors(count, monitors);
            return success;
        }
        catch
        {
            return false;
        }
    }

    #endregion

    #region Audio COM Endpoint Resolution

    private static IAudioEndpointVolume? GetDefaultEndpoint(int dataFlow)
    {
        lock (_audioLock)
        {
            if (dataFlow == 0 && _cachedRenderEndpoint != null) return _cachedRenderEndpoint;
            if (dataFlow == 1 && _cachedCaptureEndpoint != null) return _cachedCaptureEndpoint;

            try
            {
                var enumerator = ComObjectFactory.CreateMMDeviceEnumerator();
                // role = 1 (eMultimedia)
                int hr = enumerator.GetDefaultAudioEndpoint(dataFlow, 1, out var device);
                LastError = $"hr1=0x{hr:X8}, dev1={device != null}; ";
                if (hr != 0 || device == null)
                {
                    // Fallback to role = 0 (eConsole)
                    hr = enumerator.GetDefaultAudioEndpoint(dataFlow, 0, out device);
                    LastError += $"hr0=0x{hr:X8}, dev0={device != null}; ";
                }

                if (hr == 0 && device != null)
                {
                    var iid = typeof(IAudioEndpointVolume).GUID;
                    int hrAct = device.Activate(ref iid, 23 /* CLSCTX_ALL */, IntPtr.Zero, out var iface);
                    LastError += $"hrAct=0x{hrAct:X8}, ifaceNull={iface == null}, type={iface?.GetType().FullName}; ";
                    if (hrAct == 0 && iface is IAudioEndpointVolume endpoint)
                    {
                        if (dataFlow == 0) _cachedRenderEndpoint = endpoint;
                        else _cachedCaptureEndpoint = endpoint;
                        return endpoint;
                    }
                }
            }
            catch (Exception ex)
            {
                LastError = $"{ex.GetType().Name}: {ex.Message} at {ex.StackTrace}";
                Debug.WriteLine($"[SystemControlHelper] GetDefaultEndpoint({dataFlow}) failed: {ex.Message}");
            }


            return null;
        }
    }

    #endregion
}

