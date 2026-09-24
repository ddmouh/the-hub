namespace TheHub.Common.Models;

public class AppSettings
{
    // Radial Launcher
    public bool RadialLauncherEnabled { get; set; } = true;
    public bool RadialMiddleClickTrigger { get; set; } = true;
    public bool RadialAltSpaceTrigger { get; set; } = true;
    public int RadialRadius { get; set; } = 150;
    public int RadialCenterRadius { get; set; } = 60;
    public int HoldDelayMs { get; set; } = 120;
    public List<AppShortcut> LauncherShortcuts { get; set; } = new();

    // Rovyl Wheel Config
    /// <summary>Click = stays open until selection, Hold = launches on mouse release.</summary>
    public string GestureBehavior { get; set; } = "Hold";
    /// <summary>Dead zone radius (px) before angle selection activates (20-120).</summary>
    public int ActivationZone { get; set; } = 60;
    /// <summary>Global keyboard shortcut to open the radial wheel.</summary>
    public string GlobalShortcut { get; set; } = "Alt+Z";
    /// <summary>Mouse button for trigger: Middle, Back, Forward.</summary>
    public string MouseTriggerButton { get; set; } = "Middle";
    /// <summary>Appearance theme: Black, White.</summary>
    public string AppearanceTheme { get; set; } = "Black";
    /// <summary>Workspace switch mode: Picker or Keys.</summary>
    public string WorkspaceSwitchMode { get; set; } = "Picker";
    /// <summary>Whether to start the app with Windows.</summary>
    public bool StartWithWindows { get; set; } = false;
    /// <summary>Enable mouse trigger to open wheel.</summary>
    public bool EnableMouseTrigger { get; set; } = true;
    /// <summary>Performance mode: reduce animations (Precision mode).</summary>
    public bool PerformanceMode { get; set; } = false;
    /// <summary>Fullscreen protection: hide wheel during fullscreen apps.</summary>
    public bool FullscreenProtection { get; set; } = true;

    // Detailed Appearance Options
    /// <summary>Icon size on the wheel (36px to 92px).</summary>
    public int IconSize { get; set; } = 64;
    /// <summary>Target spacing between slices (0px to 40px).</summary>
    public int TargetSpacing { get; set; } = 10;
    /// <summary>Hover accent color hex (e.g. #FFFFFF).</summary>
    public string HoverColor { get; set; } = "#FFFFFF";
    /// <summary>Targeting mode: Direction (polar vector) or Pointer (hitbox collision).</summary>
    public string TargetingMode { get; set; } = "Direction";
    /// <summary>Always display shortcut labels around the wheel.</summary>
    public bool PersistentLabels { get; set; } = false;
    /// <summary>Wheel opacity percentage (35% to 100%).</summary>
    public int WheelOpacity { get; set; } = 85;
    /// <summary>Full screen background dimming percentage (0% to 100%).</summary>
    public int BackgroundDimming { get; set; } = 25;

    // Dynamic Island
    public bool DynamicIslandEnabled { get; set; } = true;
    public bool IslandShowMedia { get; set; } = true;
    public bool IslandShowVolume { get; set; } = true;
    public bool IslandShowClipboard { get; set; } = true;
    public bool IslandShowUsb { get; set; } = true;
    public bool IslandHideOnFullscreen { get; set; } = true;
    public bool IslandAlwaysShowOnFullscreen { get; set; } = false;
    public int IslandMonitorIndex { get; set; } = 0; // 0 = Primary Display, 1 = Secondary Display
    public int IslandDurationMs { get; set; } = 2500;
    public double IslandTopOffset { get; set; } = 12;
    public double? IslandCustomX { get; set; } = null;
    public double? IslandCustomY { get; set; } = null;
    public double IslandScale { get; set; } = 1.0;
    public bool IslandLockPosition { get; set; } = false;
    public bool IslandShowHoverQuickControls { get; set; } = true;
    public bool IslandShowMicMute { get; set; } = true;
    public bool IslandShowBrightnessControl { get; set; } = true;
    public bool IslandShowActiveApp { get; set; } = true;

    // Radial Wheel Profiles (Dedicated exclusively to the Radial Launcher overlay)
    public List<RadialWheelProfile> RadialProfiles { get; set; } = new();

    // Workspaces (Dedicated exclusively to Batch Application Launching)
    public List<WorkspaceConfig> Workspaces { get; set; } = new();

    // AI Safety & Emergency
    public bool AIEmergencyKillSwitch { get; set; } = false;
    public List<AIPermissionSetting> AIPermissions { get; set; } = new();
    public List<AIAuditEntry> AIAuditLogs { get; set; } = new();

    // Performance & Gaming
    public bool PauseGraphicsOnFullscreen { get; set; } = true;
    public bool LowPowerMode { get; set; } = false;

    // Wallpapers (Static & Live Video)
    public List<WallpaperItem> Wallpapers { get; set; } = new();
    public string? ActiveWallpaperId { get; set; }
    public Dictionary<string, string> MonitorWallpapers { get; set; } = new();
    public string SelectedTargetMonitor { get; set; } = "ALL";
    public bool WallpaperMuted { get; set; } = true;
    public bool WallpaperPauseOnFullscreen { get; set; } = true;
    public bool WallpaperPauseOnBattery { get; set; } = true;
    public string WallpaperFitMode { get; set; } = "Fill"; // "Fill", "Fit", "Stretch"

    // Global Typography & Custom Fonts (Section 6.3)
    public string? GlobalFontFamily { get; set; }
    public List<string> CustomFonts { get; set; } = new();

    public static AppSettings CreateDefault()
    {
        var settings = new AppSettings();

        // Default radial launcher apps
        settings.LauncherShortcuts = new List<AppShortcut>
        {
            new() { Title = "Discord", ExecutablePath = "discord.exe", IconGlyph = "💬", Order = 0 },
            new() { Title = "Spotify", ExecutablePath = "spotify.exe", IconGlyph = "🎵", Order = 1 },
            new() { Title = "Browser", ExecutablePath = "chrome.exe", IconGlyph = "🌐", Order = 2 },
            new() { Title = "Terminal", ExecutablePath = "wt.exe", IconGlyph = "💻", Order = 3 },
            new() { Title = "Explorer", ExecutablePath = "explorer.exe", IconGlyph = "📁", Order = 4 },
            new() { Title = "Settings", ExecutablePath = "control.exe", IconGlyph = "⚙️", Order = 5 },
            new() { Title = "Notepad", ExecutablePath = "notepad.exe", IconGlyph = "📝", Order = 6 },
            new() { Title = "Calculator", ExecutablePath = "calc.exe", IconGlyph = "🔢", Order = 7 }
        };

        // Default Radial Profiles
        settings.RadialProfiles = new List<RadialWheelProfile>
        {
            new()
            {
                Name = "Main Wheel",
                IconGlyph = "🎯",
                AccentColor = "#3B82F6",
                IsActive = true,
                IsVisibleInSwitcher = true,
                Slices = new(settings.LauncherShortcuts)
            }
        };

        // Default Workspaces
        settings.Workspaces = new List<WorkspaceConfig>
        {
            new()
            {
                Name = "Gaming",
                Description = "High performance profile with silent notifications",
                IconGlyph = "🎮",
                AccentColor = "#EF4444",
                PauseWallpapers = true,
                HideTaskbar = true,
                EnableDoNotDisturb = true,
                Apps = new()
                {
                    new() { Title = "Steam", ExecutablePath = "steam.exe", IconGlyph = "🎮" },
                    new() { Title = "Discord", ExecutablePath = "discord.exe", IconGlyph = "💬" }
                }
            },
            new()
            {
                Name = "Work",
                Description = "Productivity environment with focus setup",
                IconGlyph = "💼",
                AccentColor = "#3B82F6",
                PauseWallpapers = false,
                Apps = new()
                {
                    new() { Title = "Browser", ExecutablePath = "chrome.exe", IconGlyph = "🌐" },
                    new() { Title = "Terminal", ExecutablePath = "wt.exe", IconGlyph = "💻" },
                    new() { Title = "Notepad", ExecutablePath = "notepad.exe", IconGlyph = "📝" }
                }
            },
            new()
            {
                Name = "Study",
                Description = "Minimal distractions with pomodoro timer",
                IconGlyph = "📚",
                AccentColor = "#10B981",
                EnableDoNotDisturb = true,
                Apps = new()
                {
                    new() { Title = "Browser", ExecutablePath = "chrome.exe", IconGlyph = "🌐" },
                    new() { Title = "Notes", ExecutablePath = "notepad.exe", IconGlyph = "📝" }
                }
            }
        };

        // Default Permissions
        settings.AIPermissions = new List<AIPermissionSetting>
        {
            new() { ActionName = "OpenApplications", Description = "Launch installed programs and web URLs", Level = AIPermissionLevel.Level0_Safe, IsGranted = true },
            new() { ActionName = "ReadSystemSpecs", Description = "Read CPU, RAM, and battery metrics", Level = AIPermissionLevel.Level0_Safe, IsGranted = true },
            new() { ActionName = "AdjustAudioVolume", Description = "Change speaker volume and mute states", Level = AIPermissionLevel.Level1_Moderate, IsGranted = true },
            new() { ActionName = "CloseApplications", Description = "Terminate background and foreground tasks", Level = AIPermissionLevel.Level1_Moderate, IsGranted = true },
            new() { ActionName = "ManageFiles", Description = "Create or delete user files", Level = AIPermissionLevel.Level2_Sensitive, IsGranted = false },
            new() { ActionName = "SystemAdminScripts", Description = "Run administrative PowerShell / cmd commands", Level = AIPermissionLevel.Level3_Dangerous, IsGranted = false }
        };

        return settings;
    }
}
