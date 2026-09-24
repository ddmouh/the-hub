using System;
using System.IO;
using System.Text.Json;

namespace TheHub.Common.Config;

/// <summary>
/// Configuration model and persistence provider for Desktop Widgets (widgets_config.json).
/// Stores CyberClock settings (Digital/Analog, 12H/24H, styles, opacity, custom hex color, font)
/// and System Gauges telemetry settings.
/// </summary>
public class WidgetsConfig
{
    private static readonly string ConfigDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TheHub"
    );

    private static readonly string ConfigFilePath = Path.Combine(ConfigDirectory, "widgets_config.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    // ==========================================
    // CYBERCLOCK WIDGET PREFERENCES
    // ==========================================
    public bool IsClockActive { get; set; } = false;
    public bool IsClockLocked { get; set; } = false;
    public string DisplayMode { get; set; } = "Digital"; // "Digital" or "Analog"
    public bool Is24Hour { get; set; } = true;
    public bool ShowSeconds { get; set; } = true;
    public bool ShowDate { get; set; } = true;
    public double? ClockLeft { get; set; }
    public double? ClockTop { get; set; }
    public string VisualStyle { get; set; } = "Cyber Neon"; // "Cyber Neon", "Clean Minimalist", "Glassmorphism", "Retro Digital"
    public double BackgroundOpacity { get; set; } = 85.0; // 0% (transparent) to 100% (opaque)
    public string HexColor { get; set; } = "#00F0FF";
    public string FontFamily { get; set; } = "Consolas";
    public string DynamicIslandFontFamily { get; set; } = "Segoe UI";

    // ==========================================
    // SYSTEM GAUGES WIDGET PREFERENCES
    // ==========================================
    public bool IsGaugesActive { get; set; } = false;
    public bool IsGaugesLocked { get; set; } = false;
    public bool IsCpuActive { get; set; } = false;
    public bool IsGpuActive { get; set; } = false;
    public bool IsRamActive { get; set; } = false;

    private static readonly object SyncLock = new();

    public static WidgetsConfig Load()
    {
        lock (SyncLock)
        {
            try
            {
                // 1. Try AppData folder
                if (File.Exists(ConfigFilePath))
                {
                    string json = File.ReadAllText(ConfigFilePath);
                    var cfg = JsonSerializer.Deserialize<WidgetsConfig>(json, JsonOptions);
                    if (cfg != null) return cfg;
                }

                // 2. Try working directory fallback
                string localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "widgets_config.json");
                if (File.Exists(localPath))
                {
                    string json = File.ReadAllText(localPath);
                    var cfg = JsonSerializer.Deserialize<WidgetsConfig>(json, JsonOptions);
                    if (cfg != null) return cfg;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log($"WidgetsConfig.Load error: {ex.Message}");
            }

            return new WidgetsConfig();
        }
    }

    public void Save()
    {
        lock (SyncLock)
        {
            try
            {
                if (!Directory.Exists(ConfigDirectory))
                {
                    Directory.CreateDirectory(ConfigDirectory);
                }

                string json = JsonSerializer.Serialize(this, JsonOptions);
                File.WriteAllText(ConfigFilePath, json);

                // Also keep local fallback updated
                try
                {
                    string localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "widgets_config.json");
                    File.WriteAllText(localPath, json);
                }
                catch { }
            }
            catch (Exception ex)
            {
                AppLogger.Log($"WidgetsConfig.Save error: {ex.Message}");
            }
        }
    }
}
