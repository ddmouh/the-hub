using System.IO;
using System.Text.Json;
using TheHub.Common.Models;

namespace TheHub.Common.Config;

public class SectionSettingsStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public string SettingsDirectory { get; }

    public AppSection App { get; set; } = new();
    public WallpaperSection Wallpaper { get; set; } = new();
    public IslandSection Island { get; set; } = new();
    public RadialSection Radial { get; set; } = new();
    public WorkspacesSection Workspaces { get; set; } = new();
    public WidgetsSection Widgets { get; set; } = new();
    public AssistantSection Assistant { get; set; } = new();

    private readonly Dictionary<string, System.Threading.Timer> _debounceTimers = new();
    private readonly object _lock = new();

    public event Action<string>? OnSectionChanged;

    public SectionSettingsStore(string? customDirectory = null)
    {
        SettingsDirectory = customDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TheHub");

        if (!Directory.Exists(SettingsDirectory))
        {
            Directory.CreateDirectory(SettingsDirectory);
        }
    }

    public void LoadAll(Action<string>? logger = null)
    {
        MigrateIfLegacyExists(logger);

        App = LoadSection<AppSection>("app.json", logger);
        Wallpaper = LoadSection<WallpaperSection>("wallpaper.json", logger);
        Island = LoadSection<IslandSection>("island.json", logger);
        Radial = LoadSection<RadialSection>("radial.json", logger);
        Workspaces = LoadSection<WorkspacesSection>("workspaces.json", logger);
        Widgets = LoadSection<WidgetsSection>("widgets.json", logger);
        Assistant = LoadSection<AssistantSection>("assistant.json", logger);
    }

    public T LoadSection<T>(string fileName, Action<string>? logger = null) where T : new()
    {
        string filePath = Path.Combine(SettingsDirectory, fileName);
        if (File.Exists(filePath))
        {
            try
            {
                string json = File.ReadAllText(filePath);
                var obj = JsonSerializer.Deserialize<T>(json, JsonOpts);
                if (obj != null) return obj;
            }
            catch (Exception ex)
            {
                logger?.Invoke($"[WARN] Corrupted section file '{fileName}', falling back to defaults: {ex.Message}");
            }
        }
        return new T();
    }

    public void SaveSectionAtomic<T>(string fileName, T data)
    {
        string targetPath = Path.Combine(SettingsDirectory, fileName);
        string tempPath = targetPath + ".tmp";

        string json = JsonSerializer.Serialize(data, JsonOpts);
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, targetPath, overwrite: true);

        OnSectionChanged?.Invoke(Path.GetFileNameWithoutExtension(fileName));
    }

    public void SaveSectionDebounced<T>(string fileName, T data, int delayMs = 500)
    {
        lock (_lock)
        {
            if (_debounceTimers.TryGetValue(fileName, out var existingTimer))
            {
                existingTimer.Dispose();
            }

            _debounceTimers[fileName] = new System.Threading.Timer(_ =>
            {
                lock (_lock)
                {
                    SaveSectionAtomic(fileName, data);
                    if (_debounceTimers.TryGetValue(fileName, out var t))
                    {
                        t.Dispose();
                        _debounceTimers.Remove(fileName);
                    }
                }
            }, null, delayMs, Timeout.Infinite);
        }
    }

    public void AppendAuditLog(AIAuditEntry entry)
    {
        string auditFile = Path.Combine(SettingsDirectory, "audit.jsonl");
        string jsonLine = JsonSerializer.Serialize(entry);
        lock (_lock)
        {
            File.AppendAllText(auditFile, jsonLine + Environment.NewLine);
        }
    }

    public void MigrateIfLegacyExists(Action<string>? logger = null)
    {
        string legacyFile = Path.Combine(SettingsDirectory, "settings.json");
        if (!File.Exists(legacyFile)) return;

        try
        {
            string legacyJson = File.ReadAllText(legacyFile);
            var legacy = JsonSerializer.Deserialize<AppSettings>(legacyJson, JsonOpts);
            if (legacy != null)
            {
                logger?.Invoke("Migrating legacy settings.json into section files...");

                // Populate AppSection
                var appSec = new AppSection
                {
                    StartWithWindows = legacy.StartWithWindows,
                    FullscreenProtection = legacy.FullscreenProtection,
                    PerformanceMode = legacy.PerformanceMode,
                    GlobalFontFamily = legacy.GlobalFontFamily,
                    CustomFonts = legacy.CustomFonts ?? new()
                };
                SaveSectionAtomic("app.json", appSec);

                // Populate WallpaperSection
                var wallSec = new WallpaperSection
                {
                    Wallpapers = legacy.Wallpapers ?? new(),
                    ActiveWallpaperId = legacy.ActiveWallpaperId,
                    MonitorWallpapers = legacy.MonitorWallpapers ?? new(),
                    SelectedTargetMonitor = legacy.SelectedTargetMonitor ?? "ALL",
                    WallpaperMuted = legacy.WallpaperMuted,
                    WallpaperPauseOnFullscreen = legacy.WallpaperPauseOnFullscreen,
                    WallpaperPauseOnBattery = legacy.WallpaperPauseOnBattery,
                    WallpaperFitMode = legacy.WallpaperFitMode ?? "Fill",
                    PauseGraphicsOnFullscreen = legacy.PauseGraphicsOnFullscreen,
                    LowPowerMode = legacy.LowPowerMode
                };
                SaveSectionAtomic("wallpaper.json", wallSec);

                // Populate IslandSection
                var islandSec = new IslandSection
                {
                    DynamicIslandEnabled = legacy.DynamicIslandEnabled,
                    IslandShowMedia = legacy.IslandShowMedia,
                    IslandShowVolume = legacy.IslandShowVolume,
                    IslandShowClipboard = legacy.IslandShowClipboard,
                    IslandShowUsb = legacy.IslandShowUsb,
                    IslandShowMicMute = legacy.IslandShowMicMute,
                    IslandShowBrightnessControl = legacy.IslandShowBrightnessControl,
                    IslandShowActiveApp = legacy.IslandShowActiveApp,
                    IslandHideOnFullscreen = legacy.IslandHideOnFullscreen,
                    IslandAlwaysShowOnFullscreen = legacy.IslandAlwaysShowOnFullscreen,
                    IslandMonitorIndex = legacy.IslandMonitorIndex,
                    IslandDurationMs = legacy.IslandDurationMs,
                    IslandTopOffset = legacy.IslandTopOffset,
                    IslandCustomX = legacy.IslandCustomX,
                    IslandCustomY = legacy.IslandCustomY,
                    IslandScale = legacy.IslandScale,
                    IslandLockPosition = legacy.IslandLockPosition,
                    IslandShowHoverQuickControls = legacy.IslandShowHoverQuickControls
                };
                SaveSectionAtomic("island.json", islandSec);

                // Populate RadialSection
                var radSec = new RadialSection
                {
                    RadialLauncherEnabled = legacy.RadialLauncherEnabled,
                    RadialMiddleClickTrigger = legacy.RadialMiddleClickTrigger,
                    RadialAltSpaceTrigger = legacy.RadialAltSpaceTrigger,
                    RadialRadius = legacy.RadialRadius,
                    RadialCenterRadius = legacy.RadialCenterRadius,
                    HoldDelayMs = legacy.HoldDelayMs,
                    GestureBehavior = legacy.GestureBehavior ?? "Hold",
                    ActivationZone = legacy.ActivationZone,
                    GlobalShortcut = legacy.GlobalShortcut ?? "Alt+Z",
                    MouseTriggerButton = legacy.MouseTriggerButton ?? "Middle",
                    AppearanceTheme = legacy.AppearanceTheme ?? "Black",
                    EnableMouseTrigger = legacy.EnableMouseTrigger,
                    IconSize = legacy.IconSize,
                    TargetSpacing = legacy.TargetSpacing,
                    HoverColor = legacy.HoverColor ?? "#FFFFFF",
                    TargetingMode = legacy.TargetingMode ?? "Direction",
                    PersistentLabels = legacy.PersistentLabels,
                    WheelOpacity = legacy.WheelOpacity,
                    BackgroundDimming = legacy.BackgroundDimming,
                    LauncherShortcuts = legacy.LauncherShortcuts ?? new(),
                    RadialProfiles = legacy.RadialProfiles ?? new()
                };
                SaveSectionAtomic("radial.json", radSec);

                // Populate WorkspacesSection
                var wsSec = new WorkspacesSection
                {
                    WorkspaceSwitchMode = legacy.WorkspaceSwitchMode ?? "Picker",
                    Workspaces = legacy.Workspaces ?? new()
                };
                SaveSectionAtomic("workspaces.json", wsSec);

                // Populate WidgetsSection
                var widgetSec = new WidgetsSection
                {
                    ShowClockWidget = legacy.ShowClockWidget,
                    ShowCpuWidget = legacy.ShowCpuWidget,
                    ShowGpuWidget = legacy.ShowGpuWidget,
                    ShowRamWidget = legacy.ShowRamWidget,
                    Opacity = legacy.WidgetsOpacity / 100.0
                };
                SaveSectionAtomic("widgets.json", widgetSec);

                // Populate AssistantSection
                var aiSec = new AssistantSection
                {
                    AIEmergencyKillSwitch = legacy.AIEmergencyKillSwitch,
                    AIPermissions = legacy.AIPermissions ?? new()
                };
                SaveSectionAtomic("assistant.json", aiSec);

                // Move AIAuditLogs to audit.jsonl
                if (legacy.AIAuditLogs != null && legacy.AIAuditLogs.Count > 0)
                {
                    string auditFile = Path.Combine(SettingsDirectory, "audit.jsonl");
                    foreach (var entry in legacy.AIAuditLogs)
                    {
                        File.AppendAllText(auditFile, JsonSerializer.Serialize(entry) + Environment.NewLine);
                    }
                }

                // Rename legacy to settings.json.migrated
                string migratedFile = legacyFile + ".migrated";
                File.Move(legacyFile, migratedFile, overwrite: true);
                logger?.Invoke("Legacy settings successfully migrated and renamed to settings.json.migrated");
            }
        }
        catch (Exception ex)
        {
            logger?.Invoke($"Error migrating legacy settings: {ex.Message}");
        }
    }

    public AppSettings ToAppSettings()
    {
        return new AppSettings
        {
            StartWithWindows = App.StartWithWindows,
            FullscreenProtection = App.FullscreenProtection,
            PerformanceMode = App.PerformanceMode,
            GlobalFontFamily = App.GlobalFontFamily,
            CustomFonts = new(App.CustomFonts),

            Wallpapers = new(Wallpaper.Wallpapers),
            ActiveWallpaperId = Wallpaper.ActiveWallpaperId,
            MonitorWallpapers = new(Wallpaper.MonitorWallpapers),
            SelectedTargetMonitor = Wallpaper.SelectedTargetMonitor,
            WallpaperMuted = Wallpaper.WallpaperMuted,
            WallpaperPauseOnFullscreen = Wallpaper.WallpaperPauseOnFullscreen,
            WallpaperPauseOnBattery = Wallpaper.WallpaperPauseOnBattery,
            WallpaperFitMode = Wallpaper.WallpaperFitMode,
            PauseGraphicsOnFullscreen = Wallpaper.PauseGraphicsOnFullscreen,
            LowPowerMode = Wallpaper.LowPowerMode,

            DynamicIslandEnabled = Island.DynamicIslandEnabled,
            IslandShowMedia = Island.IslandShowMedia,
            IslandShowVolume = Island.IslandShowVolume,
            IslandShowClipboard = Island.IslandShowClipboard,
            IslandShowUsb = Island.IslandShowUsb,
            IslandShowMicMute = Island.IslandShowMicMute,
            IslandShowBrightnessControl = Island.IslandShowBrightnessControl,
            IslandShowActiveApp = Island.IslandShowActiveApp,
            IslandHideOnFullscreen = Island.IslandHideOnFullscreen,
            IslandAlwaysShowOnFullscreen = Island.IslandAlwaysShowOnFullscreen,
            IslandMonitorIndex = Island.IslandMonitorIndex,
            IslandDurationMs = Island.IslandDurationMs,
            IslandTopOffset = Island.IslandTopOffset,
            IslandCustomX = Island.IslandCustomX,
            IslandCustomY = Island.IslandCustomY,
            IslandScale = Island.IslandScale,
            IslandLockPosition = Island.IslandLockPosition,
            IslandShowHoverQuickControls = Island.IslandShowHoverQuickControls,

            RadialLauncherEnabled = Radial.RadialLauncherEnabled,
            RadialMiddleClickTrigger = Radial.RadialMiddleClickTrigger,
            RadialAltSpaceTrigger = Radial.RadialAltSpaceTrigger,
            RadialRadius = Radial.RadialRadius,
            RadialCenterRadius = Radial.RadialCenterRadius,
            HoldDelayMs = Radial.HoldDelayMs,
            GestureBehavior = Radial.GestureBehavior,
            ActivationZone = Radial.ActivationZone,
            GlobalShortcut = Radial.GlobalShortcut,
            MouseTriggerButton = Radial.MouseTriggerButton,
            AppearanceTheme = Radial.AppearanceTheme,
            EnableMouseTrigger = Radial.EnableMouseTrigger,
            IconSize = Radial.IconSize,
            TargetSpacing = Radial.TargetSpacing,
            HoverColor = Radial.HoverColor,
            TargetingMode = Radial.TargetingMode,
            PersistentLabels = Radial.PersistentLabels,
            WheelOpacity = Radial.WheelOpacity,
            BackgroundDimming = Radial.BackgroundDimming,
            LauncherShortcuts = new(Radial.LauncherShortcuts),
            RadialProfiles = new(Radial.RadialProfiles),

            WorkspaceSwitchMode = Workspaces.WorkspaceSwitchMode,
            Workspaces = new(Workspaces.Workspaces),

            ShowClockWidget = Widgets.ShowClockWidget,
            ShowCpuWidget = Widgets.ShowCpuWidget,
            ShowGpuWidget = Widgets.ShowGpuWidget,
            ShowRamWidget = Widgets.ShowRamWidget,
            WidgetsOpacity = Widgets.Opacity * 100.0,

            AIEmergencyKillSwitch = Assistant.AIEmergencyKillSwitch,
            AIPermissions = new(Assistant.AIPermissions)
        };
    }

    public void SaveAllFromAppSettings(AppSettings s)
    {
        App.StartWithWindows = s.StartWithWindows;
        App.FullscreenProtection = s.FullscreenProtection;
        App.PerformanceMode = s.PerformanceMode;
        App.GlobalFontFamily = s.GlobalFontFamily;
        App.CustomFonts = s.CustomFonts != null ? new(s.CustomFonts) : new();
        SaveSectionDebounced("app.json", App);

        Wallpaper.Wallpapers = s.Wallpapers != null ? new(s.Wallpapers) : new();
        Wallpaper.ActiveWallpaperId = s.ActiveWallpaperId;
        Wallpaper.MonitorWallpapers = s.MonitorWallpapers != null ? new(s.MonitorWallpapers) : new();
        Wallpaper.SelectedTargetMonitor = s.SelectedTargetMonitor ?? "ALL";
        Wallpaper.WallpaperMuted = s.WallpaperMuted;
        Wallpaper.WallpaperPauseOnFullscreen = s.WallpaperPauseOnFullscreen;
        Wallpaper.WallpaperPauseOnBattery = s.WallpaperPauseOnBattery;
        Wallpaper.WallpaperFitMode = s.WallpaperFitMode ?? "Fill";
        Wallpaper.PauseGraphicsOnFullscreen = s.PauseGraphicsOnFullscreen;
        Wallpaper.LowPowerMode = s.LowPowerMode;
        SaveSectionDebounced("wallpaper.json", Wallpaper);

        Island.DynamicIslandEnabled = s.DynamicIslandEnabled;
        Island.IslandShowMedia = s.IslandShowMedia;
        Island.IslandShowVolume = s.IslandShowVolume;
        Island.IslandShowClipboard = s.IslandShowClipboard;
        Island.IslandShowUsb = s.IslandShowUsb;
        Island.IslandShowMicMute = s.IslandShowMicMute;
        Island.IslandShowBrightnessControl = s.IslandShowBrightnessControl;
        Island.IslandShowActiveApp = s.IslandShowActiveApp;
        Island.IslandHideOnFullscreen = s.IslandHideOnFullscreen;
        Island.IslandAlwaysShowOnFullscreen = s.IslandAlwaysShowOnFullscreen;
        Island.IslandMonitorIndex = s.IslandMonitorIndex;
        Island.IslandDurationMs = s.IslandDurationMs;
        Island.IslandTopOffset = s.IslandTopOffset;
        Island.IslandCustomX = s.IslandCustomX;
        Island.IslandCustomY = s.IslandCustomY;
        Island.IslandScale = s.IslandScale;
        Island.IslandLockPosition = s.IslandLockPosition;
        Island.IslandShowHoverQuickControls = s.IslandShowHoverQuickControls;
        SaveSectionDebounced("island.json", Island);

        Radial.RadialLauncherEnabled = s.RadialLauncherEnabled;
        Radial.RadialMiddleClickTrigger = s.RadialMiddleClickTrigger;
        Radial.RadialAltSpaceTrigger = s.RadialAltSpaceTrigger;
        Radial.RadialRadius = s.RadialRadius;
        Radial.RadialCenterRadius = s.RadialCenterRadius;
        Radial.HoldDelayMs = s.HoldDelayMs;
        Radial.GestureBehavior = s.GestureBehavior ?? "Hold";
        Radial.ActivationZone = s.ActivationZone;
        Radial.GlobalShortcut = s.GlobalShortcut ?? "Alt+Z";
        Radial.MouseTriggerButton = s.MouseTriggerButton ?? "Middle";
        Radial.AppearanceTheme = s.AppearanceTheme ?? "Black";
        Radial.EnableMouseTrigger = s.EnableMouseTrigger;
        Radial.IconSize = s.IconSize;
        Radial.TargetSpacing = s.TargetSpacing;
        Radial.HoverColor = s.HoverColor ?? "#FFFFFF";
        Radial.TargetingMode = s.TargetingMode ?? "Direction";
        Radial.PersistentLabels = s.PersistentLabels;
        Radial.WheelOpacity = s.WheelOpacity;
        Radial.BackgroundDimming = s.BackgroundDimming;
        Radial.LauncherShortcuts = s.LauncherShortcuts != null ? new(s.LauncherShortcuts) : new();
        Radial.RadialProfiles = s.RadialProfiles != null ? new(s.RadialProfiles) : new();
        SaveSectionDebounced("radial.json", Radial);

        Workspaces.WorkspaceSwitchMode = s.WorkspaceSwitchMode ?? "Picker";
        Workspaces.Workspaces = s.Workspaces != null ? new(s.Workspaces) : new();
        SaveSectionDebounced("workspaces.json", Workspaces);

        Widgets.ShowClockWidget = s.ShowClockWidget;
        Widgets.ShowCpuWidget = s.ShowCpuWidget;
        Widgets.ShowGpuWidget = s.ShowGpuWidget;
        Widgets.ShowRamWidget = s.ShowRamWidget;
        Widgets.Opacity = s.WidgetsOpacity / 100.0;
        SaveSectionDebounced("widgets.json", Widgets);

        Assistant.AIEmergencyKillSwitch = s.AIEmergencyKillSwitch;
        Assistant.AIPermissions = s.AIPermissions != null ? new(s.AIPermissions) : new();
        SaveSectionDebounced("assistant.json", Assistant);
    }
}
