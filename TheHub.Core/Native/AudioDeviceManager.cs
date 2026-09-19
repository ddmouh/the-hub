using System.Diagnostics;
using System.Runtime.InteropServices;
using TheHub.Common.Config;

namespace TheHub.Core.Native;

public record AudioDeviceInfo(string Id, string Name, bool IsDefault, bool IsCapture);

public static class AudioDeviceManager
{
    public static string LastError { get; set; } = "";

    private static readonly PROPERTYKEY PKEY_Device_FriendlyName = new()
    {
        fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"),
        pid = 14
    };

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PROPVARIANT pvar);

    public static List<AudioDeviceInfo> GetPlaybackDevices()
    {
        return EnumerateDevices(dataFlow: 0 /* eRender */);
    }

    public static List<AudioDeviceInfo> GetRecordingDevices()
    {
        return EnumerateDevices(dataFlow: 1 /* eCapture */);
    }

    public static bool SetDefaultDevice(string deviceId)
    {
        try
        {
            var policyConfig = ComObjectFactory.CreatePolicyConfig();
            // 0 = eConsole, 1 = eMultimedia, 2 = eCommunications
            policyConfig.SetDefaultEndpoint(deviceId, 0);
            policyConfig.SetDefaultEndpoint(deviceId, 1);
            policyConfig.SetDefaultEndpoint(deviceId, 2);
            SystemControlHelper.InvalidateAudioCache();
            return true;
        }
        catch (Exception ex)
        {
            LastError = $"SetDefaultDevice: {ex.GetType().Name}: {ex.Message}";
            AppLogger.Log($"[AudioDeviceManager] SetDefaultDevice error: {ex}");
        }

        return false;
    }

    private static List<AudioDeviceInfo> EnumerateDevices(int dataFlow)
    {
        var result = new List<AudioDeviceInfo>();

        try
        {
            var enumerator = ComObjectFactory.CreateMMDeviceEnumerator();

            // Get default device ID
            string defaultId = "";
            try
            {
                int hrDef = enumerator.GetDefaultAudioEndpoint(dataFlow, 1 /* eMultimedia */, out var defaultDev);
                if (hrDef == 0 && defaultDev != null)
                {
                    defaultDev.GetId(out defaultId);
                }
                else
                {
                    // Fallback to role = 0 (eConsole)
                    hrDef = enumerator.GetDefaultAudioEndpoint(dataFlow, 0 /* eConsole */, out defaultDev);
                    if (hrDef == 0 && defaultDev != null)
                    {
                        defaultDev.GetId(out defaultId);
                    }
                    else
                    {
                        // Fallback to role = 2 (eCommunications) - typical for Microphones!
                        hrDef = enumerator.GetDefaultAudioEndpoint(dataFlow, 2 /* eCommunications */, out defaultDev);
                        if (hrDef == 0 && defaultDev != null)
                        {
                            defaultDev.GetId(out defaultId);
                        }
                    }
                }
            }
            catch (Exception exDef)
            {
                Debug.WriteLine($"[AudioDeviceManager] GetDefaultAudioEndpoint({dataFlow}) warning: {exDef.Message}");
            }

            // 1 = DEVICE_STATE_ACTIVE
            int hr = enumerator.EnumAudioEndpoints(dataFlow, 1, out var collection);
            if (hr != 0 || collection == null) return result;

            collection.GetCount(out uint count);
            for (uint i = 0; i < count; i++)
            {
                int hrItem = collection.Item(i, out var dev);
                if (hrItem != 0 || dev == null) continue;

                dev.GetId(out string id);

                string friendlyName = dataFlow == 1 ? "Microphone" : "Audio Device";
                try
                {
                    int hrProp = dev.OpenPropertyStore(0 /* STGM_READ */, out var propStore);
                    if (hrProp == 0 && propStore != null)
                    {
                        var key = PKEY_Device_FriendlyName;
                        var propVal = new PROPVARIANT();
                        int hrVal = propStore.GetValue(ref key, out propVal);
                        if (hrVal == 0 && propVal.pwszVal != IntPtr.Zero)
                        {
                            string? name = Marshal.PtrToStringUni(propVal.pwszVal);
                            if (!string.IsNullOrWhiteSpace(name))
                            {
                                friendlyName = name;
                            }
                        }
                        try { PropVariantClear(ref propVal); } catch { }
                    }
                }
                catch (Exception exProp)
                {
                    Debug.WriteLine($"[AudioDeviceManager] OpenPropertyStore for {id} warning: {exProp.Message}");
                }

                bool isDefault = !string.IsNullOrEmpty(defaultId) && string.Equals(id, defaultId, StringComparison.OrdinalIgnoreCase);
                result.Add(new AudioDeviceInfo(id, friendlyName, isDefault, dataFlow == 1));
            }

            // If no item matched default but items exist, mark the first one default
            if (result.Count > 0 && !result.Any(d => d.IsDefault))
            {
                result[0] = result[0] with { IsDefault = true };
            }
        }
        catch (Exception ex)
        {
            LastError = $"{ex.GetType().Name}: {ex.Message} at {ex.StackTrace}";
            AppLogger.Log($"[AudioDeviceManager] EnumerateDevices({dataFlow}) error: {ex}");
        }

        return result;
    }
}
