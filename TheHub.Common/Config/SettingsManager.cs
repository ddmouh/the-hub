using System.IO;
using TheHub.Common.Models;

namespace TheHub.Common.Config;

/// <summary>
/// Backward-compatible adapter façade over SectionSettingsStore.
/// Keeps existing call sites fully functional while storing configuration across section JSON files.
/// </summary>
public class SettingsManager
{
    private static readonly string SettingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TheHub"
    );

    private static SectionSettingsStore? _store;
    private static AppSettings? _cachedSettings;
    private static readonly object LockObj = new();

    public static event Action<AppSettings>? OnSettingsChanged;

    public static SectionSettingsStore Store
    {
        get
        {
            if (_store == null)
            {
                lock (LockObj)
                {
                    _store ??= new SectionSettingsStore(SettingsDirectory);
                }
            }
            return _store;
        }
    }

    public static AppSettings Current
    {
        get
        {
            if (_cachedSettings == null)
            {
                lock (LockObj)
                {
                    _cachedSettings ??= LoadSettings();
                }
            }
            return _cachedSettings;
        }
    }

    public static AppSettings LoadSettings()
    {
        lock (LockObj)
        {
            Store.LoadAll(msg => System.Diagnostics.Debug.WriteLine(msg));
            var settings = Store.ToAppSettings();

            if (settings.RadialProfiles == null || settings.RadialProfiles.Count == 0)
            {
                var defaultShortcuts = (settings.LauncherShortcuts != null && settings.LauncherShortcuts.Count > 0)
                    ? settings.LauncherShortcuts
                    : AppSettings.CreateDefault().LauncherShortcuts;

                settings.RadialProfiles = new List<RadialWheelProfile>
                {
                    new()
                    {
                        Name = "Main Wheel",
                        IconGlyph = "🎯",
                        AccentColor = "#3B82F6",
                        IsActive = true,
                        IsVisibleInSwitcher = true,
                        Slices = new(defaultShortcuts)
                    }
                };
            }

            _cachedSettings = settings;
            return settings;
        }
    }

    public static void SaveSettings(AppSettings? settings = null)
    {
        lock (LockObj)
        {
            settings ??= _cachedSettings ?? AppSettings.CreateDefault();
            _cachedSettings = settings;

            try
            {
                Store.SaveAllFromAppSettings(settings);
                OnSettingsChanged?.Invoke(settings);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save settings: {ex.Message}");
            }
        }
    }
}
