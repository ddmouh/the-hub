using System.Runtime.InteropServices;
using System.Text.Json;
using TheHub.Common.Config;
using TheHub.Common.Models;
using TheHub.Core.Services;
using Xunit;

namespace TheHub.Tests;

public class HubCoreTests
{
    [Fact]
    public void AppSettings_Default_ContainsExpectedShortcutsAndWorkspaces()
    {
        var settings = AppSettings.CreateDefault();

        Assert.NotNull(settings.LauncherShortcuts);
        Assert.Equal(8, settings.LauncherShortcuts.Count);
        Assert.Contains(settings.LauncherShortcuts, s => s.Title == "Discord");
        Assert.Contains(settings.LauncherShortcuts, s => s.Title == "Spotify");

        Assert.NotNull(settings.Workspaces);
        Assert.Equal(3, settings.Workspaces.Count);
        Assert.Contains(settings.Workspaces, w => w.Name == "Gaming");
        Assert.Contains(settings.Workspaces, w => w.Name == "Work");
        Assert.Contains(settings.Workspaces, w => w.Name == "Study");
    }

    [Fact]
    public void AIPermissions_Default_EnforcesLevelsCorrectly()
    {
        var settings = AppSettings.CreateDefault();

        var safeAppOpener = settings.AIPermissions.FirstOrDefault(p => p.ActionName == "OpenApplications");
        Assert.NotNull(safeAppOpener);
        Assert.Equal(AIPermissionLevel.Level0_Safe, safeAppOpener.Level);
        Assert.True(safeAppOpener.IsGranted);

        var adminScript = settings.AIPermissions.FirstOrDefault(p => p.ActionName == "SystemAdminScripts");
        Assert.NotNull(adminScript);
        Assert.Equal(AIPermissionLevel.Level3_Dangerous, adminScript.Level);
        Assert.False(adminScript.IsGranted); // Must be disabled by default according to PRD
    }

    [Fact]
    public void DynamicIslandEvent_SerializesAndDeserializes_Correctly()
    {
        var original = new DynamicIslandEvent
        {
            Type = IslandEventType.Media,
            Title = "Starboy",
            Subtitle = "The Weeknd",
            Glyph = "🎵",
            IsPlaying = true,
            Value = 75,
            DisplayDurationMs = 3500
        };

        var json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<DynamicIslandEvent>(json);

        Assert.NotNull(restored);
        Assert.Equal(original.Type, restored.Type);
        Assert.Equal(original.Title, restored.Title);
        Assert.Equal(original.Subtitle, restored.Subtitle);
        Assert.Equal(original.IsPlaying, restored.IsPlaying);
        Assert.Equal(original.Value, restored.Value);
    }

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateDC(string lpszDriver, string? lpszDevice, string? lpszOutput, IntPtr lpInitData);
    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool SetDeviceGammaRamp(IntPtr hDC, ref TheHub.Core.Native.SystemControlHelper.RAMP lpRamp);
    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [Fact]
    public void Brightness_Diagnostics_InspectMethods()
    {
        IntPtr hMon = TheHub.Core.Native.Win32Api.MonitorFromWindow(IntPtr.Zero, 1);
        bool numOk = TheHub.Core.Native.SystemControlHelper.GetNumberOfPhysicalMonitorsFromHMONITOR(hMon, out uint count);
        var phys = new TheHub.Core.Native.SystemControlHelper.PHYSICAL_MONITOR[count];
        bool getPhys = TheHub.Core.Native.SystemControlHelper.GetPhysicalMonitorsFromHMONITOR(hMon, count, phys);
        bool getB = false;
        bool setB = false;
        uint minB = 0, curB = 0, maxB = 0;
        int getBErr = 0, setBErr = 0;

        if (getPhys && count > 0)
        {
            getB = TheHub.Core.Native.SystemControlHelper.GetMonitorBrightness(phys[0].hPhysicalMonitor, out minB, out curB, out maxB);
            getBErr = Marshal.GetLastWin32Error();
            setB = TheHub.Core.Native.SystemControlHelper.SetMonitorBrightness(phys[0].hPhysicalMonitor, curB);
            setBErr = Marshal.GetLastWin32Error();
            TheHub.Core.Native.SystemControlHelper.DestroyPhysicalMonitors(count, phys);
        }

        Console.WriteLine($"DXVA2: numOk={numOk} count={count} getPhys={getPhys} desc='{phys.FirstOrDefault().szPhysicalMonitorDescription}' getB={getB}(min={minB},cur={curB},max={maxB},err={getBErr}) setB={setB}(err={setBErr})");
        Console.WriteLine($"VirtualScreen: L={System.Windows.SystemParameters.VirtualScreenLeft} T={System.Windows.SystemParameters.VirtualScreenTop} W={System.Windows.SystemParameters.VirtualScreenWidth} H={System.Windows.SystemParameters.VirtualScreenHeight}");
        Assert.True(true);
    }
















    [Theory]
    [InlineData(0, -100, 0)]   // Directly North / Top (Angle 0 -> Index 0)
    [InlineData(100, 0, 2)]    // Directly East / Right (Angle 90 -> Index 2 for 8 items)
    [InlineData(0, 100, 4)]    // Directly South / Bottom (Angle 180 -> Index 4 for 8 items)
    [InlineData(-100, 0, 6)]   // Directly West / Left (Angle 270 -> Index 6 for 8 items)
    public void RadialAngleMapping_MapsAnglesToCorrectSegment(double dx, double dy, int expectedIndex)
    {
        int count = 8;
        double angleRad = Math.Atan2(dy, dx);
        double angleDeg = angleRad * (180.0 / Math.PI) + 90.0;
        if (angleDeg < 0) angleDeg += 360.0;

        double step = 360.0 / count;
        int calculatedIndex = (int)Math.Round(angleDeg / step) % count;

        Assert.Equal(expectedIndex, calculatedIndex);
    }

    [Fact]
    public void IconGallery_ContainsOver100IconsAndCategories()
    {
        Assert.NotNull(IconGallery.Categories);
        Assert.True(IconGallery.Categories.Count >= 7);
        Assert.Contains("Gaming", IconGallery.Categories);
        Assert.Contains("Media & Audio", IconGallery.Categories);
        Assert.Contains("Browsers & Web", IconGallery.Categories);

        Assert.NotNull(IconGallery.Icons);
        Assert.True(IconGallery.Icons.Count >= 100, $"Expected >= 100 icons, but found {IconGallery.Icons.Count}");

        foreach (var icon in IconGallery.Icons)
        {
            Assert.False(string.IsNullOrWhiteSpace(icon.Glyph), "Icon glyph must not be empty");
            Assert.False(string.IsNullOrWhiteSpace(icon.Name), "Icon name must not be empty");
            Assert.False(string.IsNullOrWhiteSpace(icon.Category), "Icon category must not be empty");
            Assert.Contains(icon.Category, IconGallery.Categories);
        }
    }

    [Fact]
    public async Task InstalledAppsService_ScanReturnsValidDetectedApps()
    {
        var apps = await TheHub.Core.Services.InstalledAppsService.GetInstalledApplicationsAsync(forceRefresh: true);
        Assert.NotNull(apps);
        // On any Windows machine, Start Menu or standard Windows utilities (cmd, notepad, etc.) will be found
        Assert.True(apps.Count > 0, "InstalledAppsService should detect at least 1 application on Windows");

        foreach (var app in apps)
        {
            Assert.False(string.IsNullOrWhiteSpace(app.Name), "Detected app must have a name");
            Assert.False(string.IsNullOrWhiteSpace(app.ExecutablePath), "Detected app must have an executable path");
            Assert.False(string.IsNullOrWhiteSpace(app.IconGlyph), "Detected app must have an icon glyph");
            Assert.DoesNotContain("spotify_cli", app.Name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("spotify_cli", app.ExecutablePath, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Administrative Tools", app.Name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Application Verifier", app.Name, StringComparison.OrdinalIgnoreCase);
        }

        // On this machine, Spotify should be detected as a clean application
        var spotify = apps.FirstOrDefault(a => a.Name.Equals("Spotify", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(spotify);
        Assert.Equal("🎵", spotify.IconGlyph);
        Assert.Equal("Media", spotify.Category);
    }

    [Fact]
    public void AppSettings_IslandHideOnFullscreen_DefaultsToTrue()
    {
        var settings = AppSettings.CreateDefault();
        Assert.True(settings.IslandHideOnFullscreen);
        Assert.True(settings.IslandShowClipboard);
    }

    [Fact]
    public void FullscreenDetector_DetermineFullscreenState_RunsSafely()
    {
        using var detector = new TheHub.Core.Monitors.FullscreenDetector();
        // Should evaluate without throwing any Win32 or COM exceptions
        bool isFs = detector.DetermineFullscreenState();
        // Since test runner is a background process, typically false
        Assert.False(detector.IsFullscreen);
    }

    [Fact]
    public void ClipboardMonitor_CanInstantiateAndDisposeSafely()
    {
        using var monitor = new TheHub.Core.Monitors.ClipboardMonitor();
        Assert.NotNull(monitor);
        // Start with IntPtr.Zero should safely no-op
        monitor.Start(IntPtr.Zero);
        monitor.Stop();
    }

    [Fact]
    public void AppSettings_IslandMonitorIndex_DefaultsToZero()
    {
        var settings = AppSettings.CreateDefault();
        Assert.Equal(0, settings.IslandMonitorIndex);
        Assert.True(settings.IslandShowVolume);
        Assert.True(settings.IslandShowUsb);
    }

    [Fact]
    public void DisplayMonitorHelper_GetAllMonitors_ReturnsValidMonitors()
    {
        var monitors = TheHub.Core.Native.DisplayMonitorHelper.GetAllMonitors();
        Assert.NotNull(monitors);
        Assert.True(monitors.Count >= 1, "Should detect at least one display monitor");
        Assert.Equal(0, monitors[0].Index);
        Assert.True(monitors[0].IsPrimary);
    }

    [Fact]
    public void VolumeMonitor_CanInstantiateAndDisposeSafely()
    {
        using var monitor = new TheHub.Core.Monitors.VolumeMonitor();
        Assert.NotNull(monitor);
        monitor.Start();
        monitor.Stop();
    }

    [Fact]
    public void AppSettings_NewIslandCustomSettings_PersistAndValidateDefaults()
    {
        var settings = AppSettings.CreateDefault();
        Assert.Equal(1.0, settings.IslandScale);
        Assert.False(settings.IslandLockPosition);
        Assert.False(settings.IslandAlwaysShowOnFullscreen);
        Assert.True(settings.IslandShowHoverQuickControls);
        Assert.True(settings.IslandShowMicMute);
        Assert.True(settings.IslandShowBrightnessControl);
        Assert.True(settings.IslandShowActiveApp);
        Assert.Null(settings.IslandCustomX);
        Assert.Null(settings.IslandCustomY);
    }

    [Fact]
    public void ActiveAppMonitor_CanInstantiateAndDispose()
    {
        using var monitor = new TheHub.Core.Monitors.ActiveAppMonitor();
        Assert.NotNull(monitor);
        monitor.Start();
        Assert.NotNull(monitor.CurrentStatus);
        monitor.Stop();
    }

    [Fact]
    public void SystemControlHelper_AudioAndBrightness_RunWithoutException()
    {
        // Safe query without altering state
        var (vol, isMuted) = TheHub.Core.Native.SystemControlHelper.GetMasterAudioState();
        Assert.InRange(vol, 0, 100);

        bool micMuted = TheHub.Core.Native.SystemControlHelper.GetMicrophoneMuteState();
        // Should evaluate without throwing

        int brightness = TheHub.Core.Native.SystemControlHelper.GetScreenBrightness();
        Assert.InRange(brightness, 0, 100);
    }

    [Fact]
    public void Test_Diagnostics_Brightness_And_AudioDevices()
    {
        var playDevices = TheHub.Core.Native.AudioDeviceManager.GetPlaybackDevices();
        var recDevices = TheHub.Core.Native.AudioDeviceManager.GetRecordingDevices();

        foreach (var p in playDevices)
        {
            Console.WriteLine($"[PLAYBACK] {p.Name} | {p.Id} | Default: {p.IsDefault}");
        }
        foreach (var r in recDevices)
        {
            Console.WriteLine($"[RECORDING] {r.Name} | {r.Id} | Default: {r.IsDefault}");
        }

        if (recDevices.Count > 1)
        {
            // Switch back to original default
            var origDefault = recDevices.FirstOrDefault(d => d.IsDefault) ?? recDevices[1];
            TheHub.Core.Native.AudioDeviceManager.SetDefaultDevice(origDefault.Id);
        }

        Assert.NotNull(playDevices);
        Assert.NotNull(recDevices);
    }

    [Fact]
    public void RadialWheelProfile_IsCompletelySeparatedFromWorkspaces()
    {
        var settings = AppSettings.CreateDefault();

        Assert.NotNull(settings.RadialProfiles);
        Assert.NotEmpty(settings.RadialProfiles);
        Assert.NotNull(settings.Workspaces);

        // Ensure distinct instances and types
        var radialProfile = settings.RadialProfiles[0];
        var workspace = settings.Workspaces[0];

        // Modifying workspace does not affect radial profile
        workspace.Apps.Clear();
        Assert.Empty(workspace.Apps);
        Assert.NotEmpty(radialProfile.Slices);

        // Modifying radial profile does not affect workspace
        radialProfile.Slices.Clear();
        Assert.Empty(radialProfile.Slices);
        Assert.Empty(radialProfile.Apps);
        Assert.Equal("Current · 0 shortcuts", radialProfile.ShortcutCountText);
    }

    [Fact]
    public void EmptyRadialProfile_AllowsZeroShortcutsWithoutGhostFallback()
    {
        var profile = new RadialWheelProfile
        {
            Name = "Empty Test Profile",
            IsActive = true,
            Slices = new()
        };

        Assert.Empty(profile.Slices);
        Assert.Empty(profile.Apps);
        Assert.Equal("Current · 0 shortcuts", profile.ShortcutCountText);
    }

    [Fact]
    public void WallpaperService_DetectsTypesCorrectly()
    {
        Assert.Equal(WallpaperType.Animated, WallpaperService.DetectWallpaperType("video.mp4"));
        Assert.Equal(WallpaperType.Animated, WallpaperService.DetectWallpaperType("animation.webm"));
        Assert.Equal(WallpaperType.Animated, WallpaperService.DetectWallpaperType("loop.gif"));
        Assert.Equal(WallpaperType.Animated, WallpaperService.DetectWallpaperType("clip.mkv"));

        Assert.Equal(WallpaperType.Static, WallpaperService.DetectWallpaperType("scenery.jpg"));
        Assert.Equal(WallpaperType.Static, WallpaperService.DetectWallpaperType("scenery.png"));
        Assert.Equal(WallpaperType.Static, WallpaperService.DetectWallpaperType("art.bmp"));
    }

    [Fact]
    public void WallpaperItem_PropertyChangesAndBadges_WorkCorrectly()
    {
        var animatedItem = new WallpaperItem
        {
            Title = "Neon City",
            FilePath = "C:\\wallpapers\\neon.mp4",
            Type = WallpaperType.Animated,
            IsActive = true
        };

        Assert.Equal("🎬 LIVE", animatedItem.TypeBadgeText);
        Assert.Equal("#7C3AED", animatedItem.TypeBadgeBackground);
        Assert.Equal("#10B981", animatedItem.CardBorderBrush);
        Assert.Equal("Active", animatedItem.StatusText);

        var staticItem = new WallpaperItem
        {
            Title = "Mountains",
            FilePath = "C:\\wallpapers\\mountains.png",
            Type = WallpaperType.Static,
            IsActive = false
        };

        Assert.Equal("🖼️ STATIC", staticItem.TypeBadgeText);
        Assert.Equal("#2563EB", staticItem.TypeBadgeBackground);
        Assert.Equal("#27272A", staticItem.CardBorderBrush);
        Assert.Equal("Apply", staticItem.StatusText);
    }

    [Fact]
    public void AppSettings_WallpaperPowerAndFit_DefaultValuesAndSerialization()
    {
        var settings = AppSettings.CreateDefault();
        Assert.True(settings.WallpaperPauseOnBattery);
        Assert.Equal("Fill", settings.WallpaperFitMode);

        settings.WallpaperPauseOnBattery = false;
        settings.WallpaperFitMode = "Fit";

        string json = JsonSerializer.Serialize(settings);
        var deserialized = JsonSerializer.Deserialize<AppSettings>(json);

        Assert.NotNull(deserialized);
        Assert.False(deserialized.WallpaperPauseOnBattery);
        Assert.Equal("Fit", deserialized.WallpaperFitMode);
    }

    [Fact]
    public void Win32Api_SystemPowerStatus_CanQueryWithoutError()
    {
        bool success = TheHub.Core.Native.Win32Api.GetSystemPowerStatus(out var status);
        Assert.True(success);
        Assert.True(status.ACLineStatus == 0 || status.ACLineStatus == 1 || status.ACLineStatus == 255);
    }
}

