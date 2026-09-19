using System.IO;
using System.Text.Json;
using TheHub.Common.Models;

namespace TheHub.Common.Config;

public class SettingsManager
{
    private static readonly string SettingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TheHub"
    );

    private static readonly string SettingsFilePath = Path.Combine(SettingsDirectory, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private static AppSettings? _cachedSettings;
    private static readonly object LockObj = new();

    public static event Action<AppSettings>? OnSettingsChanged;

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
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                string json = File.ReadAllText(SettingsFilePath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (settings != null)
                {
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
        }
        catch (Exception)
        {
            // Fall back to defaults on corrupt file
        }

        var defaultSettings = AppSettings.CreateDefault();
        SaveSettings(defaultSettings);
        _cachedSettings = defaultSettings;
        return defaultSettings;
    }

    public static void SaveSettings(AppSettings? settings = null)
    {
        lock (LockObj)
        {
            settings ??= _cachedSettings ?? AppSettings.CreateDefault();
            _cachedSettings = settings;

            try
            {
                if (!Directory.Exists(SettingsDirectory))
                {
                    Directory.CreateDirectory(SettingsDirectory);
                }

                var json = JsonSerializer.Serialize(settings, JsonOptions);
                File.WriteAllText(SettingsFilePath, json);

                OnSettingsChanged?.Invoke(settings);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save settings: {ex.Message}");
            }
        }
    }
}
