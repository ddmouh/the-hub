using TheHub.Common.Models;

namespace TheHub.Common.Config;

public class AppSection
{
    public bool StartWithWindows { get; set; } = false;
    public bool FullscreenProtection { get; set; } = true;
    public bool PerformanceMode { get; set; } = false;
    public double UiScale { get; set; } = 1.0;
    public string? GlobalFontFamily { get; set; }
    public List<string> CustomFonts { get; set; } = new();
}

public class WallpaperSection
{
    public List<WallpaperItem> Wallpapers { get; set; } = new();
    public string? ActiveWallpaperId { get; set; }
    public Dictionary<string, string> MonitorWallpapers { get; set; } = new();
    public string SelectedTargetMonitor { get; set; } = "ALL";
    public bool WallpaperMuted { get; set; } = true;
    public bool WallpaperPauseOnFullscreen { get; set; } = true;
    public bool WallpaperPauseOnBattery { get; set; } = true;
    public string WallpaperFitMode { get; set; } = "Fill";
    public bool PauseGraphicsOnFullscreen { get; set; } = true;
    public bool LowPowerMode { get; set; } = false;
}

public class IslandSection
{
    public bool DynamicIslandEnabled { get; set; } = true;
    public bool IslandShowMedia { get; set; } = true;
    public bool IslandShowVolume { get; set; } = true;
    public bool IslandShowClipboard { get; set; } = true;
    public bool IslandShowUsb { get; set; } = true;
    public bool IslandShowMicMute { get; set; } = true;
    public bool IslandShowBrightnessControl { get; set; } = true;
    public bool IslandShowActiveApp { get; set; } = true;
    public bool IslandHideOnFullscreen { get; set; } = true;
    public bool IslandAlwaysShowOnFullscreen { get; set; } = false;
    public int IslandMonitorIndex { get; set; } = 0;
    public int IslandDurationMs { get; set; } = 2500;
    public double IslandTopOffset { get; set; } = 12;
    public double? IslandCustomX { get; set; } = null;
    public double? IslandCustomY { get; set; } = null;
    public double IslandScale { get; set; } = 1.0;
    public bool IslandLockPosition { get; set; } = false;
    public bool IslandShowHoverQuickControls { get; set; } = true;
}

public class RadialSection
{
    public bool RadialLauncherEnabled { get; set; } = true;
    public bool RadialMiddleClickTrigger { get; set; } = true;
    public bool RadialAltSpaceTrigger { get; set; } = true;
    public int RadialRadius { get; set; } = 150;
    public int RadialCenterRadius { get; set; } = 60;
    public int HoldDelayMs { get; set; } = 120;
    public string GestureBehavior { get; set; } = "Hold";
    public int ActivationZone { get; set; } = 60;
    public string GlobalShortcut { get; set; } = "Alt+Z";
    public string MouseTriggerButton { get; set; } = "Middle";
    public string AppearanceTheme { get; set; } = "Black";
    public bool EnableMouseTrigger { get; set; } = true;
    public int IconSize { get; set; } = 64;
    public int TargetSpacing { get; set; } = 10;
    public string HoverColor { get; set; } = "#FFFFFF";
    public string TargetingMode { get; set; } = "Direction";
    public bool PersistentLabels { get; set; } = false;
    public int WheelOpacity { get; set; } = 85;
    public int BackgroundDimming { get; set; } = 25;
    public List<AppShortcut> LauncherShortcuts { get; set; } = new();
    public List<RadialWheelProfile> RadialProfiles { get; set; } = new();
}

public class WorkspacesSection
{
    public string WorkspaceSwitchMode { get; set; } = "Picker";
    public List<WorkspaceConfig> Workspaces { get; set; } = new();
}

public class WidgetsSection
{
    public string ClockMode { get; set; } = "Analog";
    public bool ShowClockWidget { get; set; } = true;
    public bool ShowCpuWidget { get; set; } = true;
    public bool ShowGpuWidget { get; set; } = true;
    public bool ShowRamWidget { get; set; } = true;
    public double Opacity { get; set; } = 0.9;
}

public class AssistantSection
{
    public bool AIEmergencyKillSwitch { get; set; } = false;
    public List<AIPermissionSetting> AIPermissions { get; set; } = new();
}
