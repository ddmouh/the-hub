using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TheHub.Common.Models;
using Application = System.Windows.Application;
using Image = System.Windows.Controls.Image;
using Color = System.Windows.Media.Color;

namespace TheHub.App.Showcase;

/// <summary>
/// Central catalog maintaining 100% authentic visual snapshots, live page captures, and inspect actions
/// for every window, menu, state, overlay, widget, and modal in The Hub.
/// </summary>
public class ViewSnapshotRegistry
{
    private readonly List<ViewSnapshotItem> _items = new();

    public ViewSnapshotRegistry()
    {
        RegisterAllSnapshots();
    }

    public IReadOnlyList<ViewSnapshotItem> GetAllSnapshots() => _items;

    public IEnumerable<ViewSnapshotItem> GetByCategory(string category)
    {
        if (string.Equals(category, "All", StringComparison.OrdinalIgnoreCase))
            return _items;

        return _items.Where(x => string.Equals(x.Category, category, StringComparison.OrdinalIgnoreCase));
    }

    public IEnumerable<ViewSnapshotItem> Find(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return _items;

        var q = query.Trim().ToLowerInvariant();
        return _items.Where(x =>
            x.Title.ToLowerInvariant().Contains(q) ||
            x.Category.ToLowerInvariant().Contains(q) ||
            x.Description.ToLowerInvariant().Contains(q) ||
            x.StateTag.ToLowerInvariant().Contains(q) ||
            x.Tags.Any(t => t.ToLowerInvariant().Contains(q)));
    }

    private void RegisterAllSnapshots()
    {
        // =========================================================================
        // 0. DESIGN SYSTEM & CORE CONTROLS (DESIGN_SYSTEM.md)
        // =========================================================================

        _items.Add(new ViewSnapshotItem
        {
            Id = "design_core_controls",
            Title = "Design System: Core Controls & Tokens",
            Category = "Design System",
            BadgeColor = "#3B82F6",
            StateTag = "Tokens & Controls",
            Description = "Unified component catalog featuring SectionHeader, SettingRow, HubButtons, HubToggle, and HubCard matching DESIGN_SYSTEM.md.",
            Tags = new[] { "Design", "Tokens", "Controls", "Buttons", "Header", "Toggles" },
            TargetWidth = 900,
            TargetHeight = 600,
            PreviewElementFactory = () => new ControlsShowcaseView()
        });

        // =========================================================================
        // 1. DYNAMIC ISLAND (ALL STATES: COMPACT, EXPANDED, EVENTS, SLIDERS, DRAWER)
        // =========================================================================

        _items.Add(new ViewSnapshotItem
        {
            Id = "island_compact_idle",
            Title = "Dynamic Island: Compact (Idle / Ambient)",
            Category = "Dynamic Island",
            BadgeColor = "#8B5CF6",
            StateTag = "Compact Idle",
            Description = "OLED dark glass pill in quiet ambient status displaying The Hub logo and green ready indicator dot.",
            Tags = new[] { "Island", "Compact", "Pill", "Status", "Idle" },
            TargetWidth = 480,
            TargetHeight = 150,
            PreviewElementFactory = () => RealUiSnapshotFactory.CreateDynamicIslandSnapshot(IslandSnapshotMode.CompactIdle),
            InspectAction = mw => mw.NavigateToSection(3, 0)
        });

        _items.Add(new ViewSnapshotItem
        {
            Id = "island_compact_music",
            Title = "Dynamic Island: Compact (Active Music & Waves)",
            Category = "Dynamic Island",
            BadgeColor = "#8B5CF6",
            StateTag = "Music Waves",
            Description = "Compact pill playing music showing real track metadata, music glyph, and live pulsing equalizer waves.",
            Tags = new[] { "Island", "Compact", "Music", "Spotify", "Equalizer", "Waves" },
            TargetWidth = 480,
            TargetHeight = 150,
            PreviewElementFactory = () => RealUiSnapshotFactory.CreateDynamicIslandSnapshot(IslandSnapshotMode.CompactMusic),
            InspectAction = mw => mw.NavigateToSection(3, 0)
        });

        _items.Add(new ViewSnapshotItem
        {
            Id = "island_compact_volume",
            Title = "Dynamic Island: Compact (Volume Level Notification)",
            Category = "Dynamic Island",
            BadgeColor = "#3B82F6",
            StateTag = "Volume Level",
            Description = "Instant volume feedback notification pill showing current master speaker level in percentage.",
            Tags = new[] { "Island", "Compact", "Volume", "Audio", "Sound" },
            TargetWidth = 480,
            TargetHeight = 150,
            PreviewElementFactory = () => RealUiSnapshotFactory.CreateDynamicIslandSnapshot(IslandSnapshotMode.CompactVolume),
            InspectAction = mw => mw.NavigateToSection(3, 0)
        });

        _items.Add(new ViewSnapshotItem
        {
            Id = "island_compact_mic",
            Title = "Dynamic Island: Compact (Microphone Muted Alert)",
            Category = "Dynamic Island",
            BadgeColor = "#EF4444",
            StateTag = "Mic Mute",
            Description = "Real-time privacy indicator alerting the user when hardware microphone input is silenced.",
            Tags = new[] { "Island", "Compact", "Mic", "Privacy", "Mute" },
            TargetWidth = 480,
            TargetHeight = 150,
            PreviewElementFactory = () => RealUiSnapshotFactory.CreateDynamicIslandSnapshot(IslandSnapshotMode.CompactMicMute),
            InspectAction = mw => mw.NavigateToSection(3, 0)
        });

        _items.Add(new ViewSnapshotItem
        {
            Id = "island_expanded_media",
            Title = "Dynamic Island: Expanded (Media Player Studio)",
            Category = "Dynamic Island",
            BadgeColor = "#8B5CF6",
            StateTag = "Media Player",
            Description = "Expanded music pill featuring full playback controls (Prev/Play/Next), track seeker, album details, and volume level.",
            Tags = new[] { "Island", "Expanded", "Player", "Controls", "Music" },
            TargetWidth = 540,
            TargetHeight = 310,
            PreviewElementFactory = () => RealUiSnapshotFactory.CreateDynamicIslandSnapshot(IslandSnapshotMode.ExpandedMedia),
            InspectAction = mw => mw.NavigateToSection(3, 0)
        });

        _items.Add(new ViewSnapshotItem
        {
            Id = "island_expanded_sliders",
            Title = "Dynamic Island: Expanded (Live Sliders & Controls)",
            Category = "Dynamic Island",
            BadgeColor = "#8B5CF6",
            StateTag = "Live Sliders",
            Description = "Interactive dashboard controls with Shadcn master volume slider, hardware screen brightness, and quick mic toggles.",
            Tags = new[] { "Island", "Expanded", "Sliders", "Volume", "Brightness", "Controls" },
            TargetWidth = 540,
            TargetHeight = 310,
            PreviewElementFactory = () => RealUiSnapshotFactory.CreateDynamicIslandSnapshot(IslandSnapshotMode.ExpandedAudioBrightness),
            InspectAction = mw => mw.NavigateToSection(3, 0)
        });

        _items.Add(new ViewSnapshotItem
        {
            Id = "island_expanded_workspaces",
            Title = "Dynamic Island: Expanded (Workspaces Drawer Open)",
            Category = "Dynamic Island",
            BadgeColor = "#8B5CF6",
            StateTag = "Workspaces Drawer",
            Description = "Slide-down profile switcher drawer allowing one-tap launching of Gaming, Dev, Creative, and Study setups.",
            Tags = new[] { "Island", "Expanded", "Workspaces", "Drawer", "Profiles" },
            TargetWidth = 540,
            TargetHeight = 360,
            PreviewElementFactory = () => RealUiSnapshotFactory.CreateDynamicIslandSnapshot(IslandSnapshotMode.ExpandedWorkspaces),
            InspectAction = mw => mw.NavigateToSection(1, 0)
        });

        _items.Add(new ViewSnapshotItem
        {
            Id = "island_event_usb",
            Title = "Dynamic Island: Event (USB Hardware Connected)",
            Category = "Dynamic Island",
            BadgeColor = "#10B981",
            StateTag = "USB Event",
            Description = "Instant USB plug/unplug notification card presenting hardware label and safe connection status.",
            Tags = new[] { "Island", "Event", "USB", "Hardware", "Device" },
            TargetWidth = 480,
            TargetHeight = 150,
            PreviewElementFactory = () => RealUiSnapshotFactory.CreateDynamicIslandSnapshot(IslandSnapshotMode.EventUsb),
            InspectAction = mw => mw.NavigateToSection(3, 0)
        });

        _items.Add(new ViewSnapshotItem
        {
            Id = "island_event_download",
            Title = "Dynamic Island: Event (Download Progress Tracker)",
            Category = "Dynamic Island",
            BadgeColor = "#3B82F6",
            StateTag = "Download Progress",
            Description = "Compact animated progress indicator monitoring background file downloads and update packages.",
            Tags = new[] { "Island", "Event", "Download", "Progress", "File" },
            TargetWidth = 480,
            TargetHeight = 150,
            PreviewElementFactory = () => RealUiSnapshotFactory.CreateDynamicIslandSnapshot(IslandSnapshotMode.EventDownload),
            InspectAction = mw => mw.NavigateToSection(3, 0)
        });

        _items.Add(new ViewSnapshotItem
        {
            Id = "island_event_clipboard",
            Title = "Dynamic Island: Event (Clipboard Smart Capture)",
            Category = "Dynamic Island",
            BadgeColor = "#F59E0B",
            StateTag = "Clipboard Capture",
            Description = "Subtle notification appearing when copying text, codes, or URL links with inline preview snippet.",
            Tags = new[] { "Island", "Event", "Clipboard", "Copy", "Capture" },
            TargetWidth = 480,
            TargetHeight = 150,
            PreviewElementFactory = () => RealUiSnapshotFactory.CreateDynamicIslandSnapshot(IslandSnapshotMode.EventClipboard),
            InspectAction = mw => mw.NavigateToSection(3, 0)
        });

        _items.Add(new ViewSnapshotItem
        {
            Id = "island_event_prayer",
            Title = "Dynamic Island: Event (Islamic Prayer Time Alert)",
            Category = "Dynamic Island",
            BadgeColor = "#10B981",
            StateTag = "Prayer Alert",
            Description = "Timely Azan notification banner displaying current prayer name (Asr, Maghrib, etc.) and exact time.",
            Tags = new[] { "Island", "Event", "Prayer", "Islam", "Azan", "Time" },
            TargetWidth = 480,
            TargetHeight = 150,
            PreviewElementFactory = () => RealUiSnapshotFactory.CreateDynamicIslandSnapshot(IslandSnapshotMode.EventPrayer),
            InspectAction = mw => mw.NavigateToSection(3, 0)
        });

        _items.Add(new ViewSnapshotItem
        {
            Id = "island_event_pomodoro",
            Title = "Dynamic Island: Event (Pomodoro Focus Countdown)",
            Category = "Dynamic Island",
            BadgeColor = "#EC4899",
            StateTag = "Pomodoro Timer",
            Description = "Productivity timer status keeping track of work intervals, session numbers, and countdown minutes.",
            Tags = new[] { "Island", "Event", "Pomodoro", "Timer", "Focus", "Study" },
            TargetWidth = 480,
            TargetHeight = 150,
            PreviewElementFactory = () => RealUiSnapshotFactory.CreateDynamicIslandSnapshot(IslandSnapshotMode.EventPomodoro),
            InspectAction = mw => mw.NavigateToSection(3, 0)
        });

        // =========================================================================
        // 2. DASHBOARD PAGES & STUDIOS (LIVE ACTIVE VIEWS FROM MAINWINDOW)
        // =========================================================================

        _items.Add(new ViewSnapshotItem
        {
            Id = "dashboard_wallpapers",
            Title = "Dashboard: Live Wallpapers Studio",
            Category = "Dashboard",
            BadgeColor = "#3B82F6",
            StateTag = "Wallpapers Studio",
            Description = "Live video wallpaper gallery, playback controls, per-monitor assignment, sound mute, and battery pause switches.",
            Tags = new[] { "Dashboard", "Wallpapers", "Video", "Monitors", "Studio" },
            TargetWidth = 920,
            TargetHeight = 580,
            LiveCaptureFactory = () => (Application.Current?.MainWindow as MainWindow)?.CaptureSectionSnapshot(0, 0),
            PreviewElementFactory = () => CreateLiveImagePreview(0, 0),
            InspectAction = mw => mw.NavigateToSection(0, 0)
        });

        _items.Add(new ViewSnapshotItem
        {
            Id = "dashboard_widgets",
            Title = "Dashboard: Desktop Widgets Manager",
            Category = "Dashboard",
            BadgeColor = "#10B981",
            StateTag = "Widgets Manager",
            Description = "Catalog and configuration studio for desktop widgets: Clock styles, CPU/GPU telemetry, Islamic times, and Pomodoro.",
            Tags = new[] { "Dashboard", "Widgets", "Clock", "Telemetry", "HUD" },
            TargetWidth = 920,
            TargetHeight = 580,
            LiveCaptureFactory = () => (Application.Current?.MainWindow as MainWindow)?.CaptureSectionSnapshot(0, 1),
            PreviewElementFactory = () => CreateLiveImagePreview(0, 1),
            InspectAction = mw => mw.NavigateToSection(0, 1)
        });

        _items.Add(new ViewSnapshotItem
        {
            Id = "dashboard_taskbar",
            Title = "Dashboard: Taskbar & Dock Customizer",
            Category = "Dashboard",
            BadgeColor = "#3B82F6",
            StateTag = "Taskbar Dock",
            Description = "Windows taskbar customization studio: Acrylic blur, opacity slider, auto-hide in fullscreen games, and dock metrics.",
            Tags = new[] { "Dashboard", "Taskbar", "Dock", "Blur", "Acrylic", "Windows" },
            TargetWidth = 920,
            TargetHeight = 580,
            LiveCaptureFactory = () => (Application.Current?.MainWindow as MainWindow)?.CaptureSectionSnapshot(0, 2),
            PreviewElementFactory = () => CreateLiveImagePreview(0, 2),
            InspectAction = mw => mw.NavigateToSection(0, 2)
        });

        _items.Add(new ViewSnapshotItem
        {
            Id = "dashboard_workspaces",
            Title = "Dashboard: Workspaces & Environments (Batch Launcher)",
            Category = "Dashboard",
            BadgeColor = "#8B5CF6",
            StateTag = "Workspaces Studio",
            Description = "Environment batch launcher studio: Gaming, Dev, Creative, Study cards with app tags, launch triggers, and editor.",
            Tags = new[] { "Dashboard", "Workspaces", "Batch", "Environments", "Gaming", "Dev" },
            TargetWidth = 920,
            TargetHeight = 580,
            LiveCaptureFactory = () => (Application.Current?.MainWindow as MainWindow)?.CaptureSectionSnapshot(1, 0),
            PreviewElementFactory = () => CreateLiveImagePreview(1, 0),
            InspectAction = mw => mw.NavigateToSection(1, 0)
        });

        _items.Add(new ViewSnapshotItem
        {
            Id = "radial_activation",
            Title = "Radial Launcher: Activation & Triggers Studio",
            Category = "Radial Wheel",
            BadgeColor = "#F59E0B",
            StateTag = "Activation Rules",
            Description = "Middle mouse button hold & flick configuration, trigger delay sliders, deadzone boundaries, and multi-monitor rules.",
            Tags = new[] { "Radial", "Activation", "MiddleClick", "Flick", "Hotkeys" },
            TargetWidth = 920,
            TargetHeight = 580,
            LiveCaptureFactory = () => (Application.Current?.MainWindow as MainWindow)?.CaptureSectionSnapshot(2, 0),
            PreviewElementFactory = () => CreateLiveImagePreview(2, 0),
            InspectAction = mw => mw.NavigateToSection(2, 0)
        });

        _items.Add(new ViewSnapshotItem
        {
            Id = "radial_appearance",
            Title = "Radial Launcher: Appearance & Visual Metrics",
            Category = "Radial Wheel",
            BadgeColor = "#F59E0B",
            StateTag = "Appearance Studio",
            Description = "Customize Rovyl wheel visual parameters: Outer/Inner radius, icon size slider, glass opacity, and active bloom glow.",
            Tags = new[] { "Radial", "Appearance", "Radius", "Glass", "Bloom", "Styling" },
            TargetWidth = 920,
            TargetHeight = 580,
            LiveCaptureFactory = () => (Application.Current?.MainWindow as MainWindow)?.CaptureSectionSnapshot(2, 1),
            PreviewElementFactory = () => CreateLiveImagePreview(2, 1),
            InspectAction = mw => mw.NavigateToSection(2, 1)
        });

        _items.Add(new ViewSnapshotItem
        {
            Id = "radial_slices",
            Title = "Radial Launcher: Wheel Slices & Shortcuts Editor",
            Category = "Radial Wheel",
            BadgeColor = "#F59E0B",
            StateTag = "Slices Studio",
            Description = "6-slice wheel editor with app executable browsing, re-order buttons (▲/▼), custom glyphs, and web shortcuts.",
            Tags = new[] { "Radial", "Slices", "Shortcuts", "Apps", "Editor" },
            TargetWidth = 920,
            TargetHeight = 580,
            LiveCaptureFactory = () => (Application.Current?.MainWindow as MainWindow)?.CaptureSectionSnapshot(2, 2),
            PreviewElementFactory = () => CreateLiveImagePreview(2, 2),
            InspectAction = mw => mw.NavigateToSection(2, 2)
        });

        // =========================================================================
        // 3. RADIAL WHEEL OVERLAYS
        // =========================================================================

        _items.Add(new ViewSnapshotItem
        {
            Id = "radial_wheel_glass",
            Title = "Radial Launcher: Floating Glass Orbit (Rovyl Wheel)",
            Category = "Radial Wheel",
            BadgeColor = "#F59E0B",
            StateTag = "Active Bloom",
            Description = "Rovyl-style 6-slice glass wheel with active bloom glow, dashed orbit guide ring, center hub icon, and shortcut icons.",
            Tags = new[] { "Radial", "Wheel", "Glass", "Orbit", "Bloom", "Shortcuts" },
            TargetWidth = 540,
            TargetHeight = 540,
            PreviewElementFactory = () => RealUiSnapshotFactory.CreateRadialWheelSnapshot(1),
            InspectAction = mw => mw.ShowRadialWheel()
        });

        _items.Add(new ViewSnapshotItem
        {
            Id = "radial_wheel_hover",
            Title = "Radial Launcher: Slice Hover & Tooltip State",
            Category = "Radial Wheel",
            BadgeColor = "#F59E0B",
            StateTag = "Slice Selected",
            Description = "Interactive highlighted state with glowing slice border, hovered application label, and bloom halo.",
            Tags = new[] { "Radial", "Wheel", "Hover", "Selection", "Tooltip" },
            TargetWidth = 540,
            TargetHeight = 540,
            PreviewElementFactory = () => RealUiSnapshotFactory.CreateRadialWheelSnapshot(3),
            InspectAction = mw => mw.ShowRadialWheel()
        });

        // =========================================================================
        // 4. DESKTOP WIDGETS
        // =========================================================================

        _items.Add(new ViewSnapshotItem
        {
            Id = "widget_clock_analog",
            Title = "Desktop Clock: Analog Dial & Glowing Digital HUD",
            Category = "Widgets",
            BadgeColor = "#10B981",
            StateTag = "Analog & Digital",
            Description = "Full-featured desktop clock widget with smooth sweeping analog dial, glowing hour markers, 24h digital time, and date.",
            Tags = new[] { "Widgets", "Clock", "Analog", "Digital", "HUD", "Desktop" },
            TargetWidth = 400,
            TargetHeight = 400,
            PreviewElementFactory = () => RealUiSnapshotFactory.CreateDesktopClockSnapshot(true),
            InspectAction = mw => mw.NavigateToSection(0, 1)
        });

        _items.Add(new ViewSnapshotItem
        {
            Id = "widget_clock_compact",
            Title = "Desktop Clock: Compact Minimalist Capsule",
            Category = "Widgets",
            BadgeColor = "#10B981",
            StateTag = "Compact Capsule",
            Description = "Minimalist desktop pill clock designed for screen corners with frosted glass backdrop and clean digital typography.",
            Tags = new[] { "Widgets", "Clock", "Compact", "Capsule", "Minimal" },
            TargetWidth = 340,
            TargetHeight = 180,
            PreviewElementFactory = () => RealUiSnapshotFactory.CreateDesktopClockSnapshot(false),
            InspectAction = mw => mw.NavigateToSection(0, 1)
        });

        _items.Add(new ViewSnapshotItem
        {
            Id = "widget_stats_triple",
            Title = "Hardware Stats HUD: Triple Telemetry (CPU / GPU / RAM)",
            Category = "Widgets",
            BadgeColor = "#10B981",
            StateTag = "Telemetry Active",
            Description = "Triple system resource monitor displaying live CPU utilization, GPU temperature & clock, and RAM memory load.",
            Tags = new[] { "Widgets", "Hardware", "CPU", "GPU", "RAM", "Telemetry" },
            TargetWidth = 370,
            TargetHeight = 260,
            PreviewElementFactory = () => RealUiSnapshotFactory.CreateDesktopResourceSnapshot(false),
            InspectAction = mw => mw.NavigateToSection(0, 1)
        });

        _items.Add(new ViewSnapshotItem
        {
            Id = "widget_stats_alert",
            Title = "Hardware Stats: High Load Spike Alert (RAM >88%)",
            Category = "Widgets",
            BadgeColor = "#EF4444",
            StateTag = "High Load Spike",
            Description = "Dynamic alert state triggered when system RAM exceeds safe thresholds (>88%), highlighting consumption in red.",
            Tags = new[] { "Widgets", "Hardware", "Alert", "Spike", "Memory", "Red" },
            TargetWidth = 370,
            TargetHeight = 260,
            PreviewElementFactory = () => RealUiSnapshotFactory.CreateDesktopResourceSnapshot(true),
            InspectAction = mw => mw.NavigateToSection(0, 1)
        });

        // =========================================================================
        // 5. SETTINGS, OPTIONS & AI ASSISTANT
        // =========================================================================

        _items.Add(new ViewSnapshotItem
        {
            Id = "dashboard_island_settings",
            Title = "Settings: Dynamic Island Configuration Studio",
            Category = "Settings & Options",
            BadgeColor = "#8B5CF6",
            StateTag = "Island Studio",
            Description = "Adjust Dynamic Island pill size, top screen margin, anchor alignment (Center/Left/Right), and event duration timers.",
            Tags = new[] { "Settings", "Island", "Configuration", "Alignment", "Position" },
            TargetWidth = 920,
            TargetHeight = 580,
            LiveCaptureFactory = () => (Application.Current?.MainWindow as MainWindow)?.CaptureSectionSnapshot(3, 0),
            PreviewElementFactory = () => CreateLiveImagePreview(3, 0),
            InspectAction = mw => mw.NavigateToSection(3, 0)
        });

        _items.Add(new ViewSnapshotItem
        {
            Id = "dashboard_ai",
            Title = "AI System Assistant: Offline Models & Tool Permissions",
            Category = "Settings & Options",
            BadgeColor = "#6366F1",
            StateTag = "System AI",
            Description = "System AI companion studio: Ollama offline LLM selector, Tool Permissions Matrix (Levels 0-3), and audit log console.",
            Tags = new[] { "Settings", "AI", "Ollama", "Permissions", "ToolCalling", "Console" },
            TargetWidth = 920,
            TargetHeight = 580,
            LiveCaptureFactory = () => (Application.Current?.MainWindow as MainWindow)?.CaptureSectionSnapshot(4, 0),
            PreviewElementFactory = () => CreateLiveImagePreview(4, 0),
            InspectAction = mw => mw.NavigateToSection(4, 0)
        });

        _items.Add(new ViewSnapshotItem
        {
            Id = "options_typography",
            Title = "Settings: Typography & UI Scale Multiplier",
            Category = "Settings & Options",
            BadgeColor = "#06B6D4",
            StateTag = "Typography Studio",
            Description = "Configure Segoe UI Variable font family, UI scale multiplier (100% - 150%), header weights, and readability options.",
            Tags = new[] { "Settings", "Options", "Typography", "Fonts", "Scale", "Segoe" },
            TargetWidth = 920,
            TargetHeight = 580,
            LiveCaptureFactory = () => (Application.Current?.MainWindow as MainWindow)?.CaptureSectionSnapshot(5, 0),
            PreviewElementFactory = () => CreateLiveImagePreview(5, 0),
            InspectAction = mw => mw.NavigateToSection(5, 0)
        });

        _items.Add(new ViewSnapshotItem
        {
            Id = "options_custom_fonts",
            Title = "Settings: Custom Fonts Dropzone (.ttf / .otf)",
            Category = "Settings & Options",
            BadgeColor = "#06B6D4",
            StateTag = "Custom Fonts",
            Description = "Installed .ttf and .otf custom typeface cards with real-time preview glyphs, file sizes, and 1-click removal.",
            Tags = new[] { "Settings", "Fonts", "Custom", "TTF", "OTF", "Dropzone" },
            TargetWidth = 920,
            TargetHeight = 580,
            LiveCaptureFactory = () => (Application.Current?.MainWindow as MainWindow)?.CaptureSectionSnapshot(5, 0),
            PreviewElementFactory = () => CreateLiveImagePreview(5, 0),
            InspectAction = mw => mw.NavigateToSection(5, 0)
        });

        _items.Add(new ViewSnapshotItem
        {
            Id = "options_wallpapers",
            Title = "Settings: Multi-Monitor Wallpaper Rules & Display Hook",
            Category = "Settings & Options",
            BadgeColor = "#06B6D4",
            StateTag = "Display Rules",
            Description = "Multi-monitor display setup, auto-pause live video in fullscreen games, pause on battery, and sound mute switches.",
            Tags = new[] { "Settings", "Wallpapers", "Monitors", "Battery", "Gaming", "Rules" },
            TargetWidth = 920,
            TargetHeight = 580,
            LiveCaptureFactory = () => (Application.Current?.MainWindow as MainWindow)?.CaptureSectionSnapshot(0, 0),
            PreviewElementFactory = () => CreateLiveImagePreview(0, 0),
            InspectAction = mw => mw.NavigateToSection(0, 0)
        });

        // =========================================================================
        // 6. MODALS & CONTROLS
        // =========================================================================

        _items.Add(new ViewSnapshotItem
        {
            Id = "modal_icon_picker",
            Title = "Modal: Curated App Icon Picker (180+ Glyphs)",
            Category = "Modals & Dialogs",
            BadgeColor = "#EC4899",
            StateTag = "Modal Picker",
            Description = "Categorized icon picker dialog (Gaming, Media, Dev, Browsers, System) with instant search box and glyph grid.",
            Tags = new[] { "Modal", "IconPicker", "Glyphs", "Categories", "Icons" },
            TargetWidth = 920,
            TargetHeight = 580,
            LiveCaptureFactory = () => (Application.Current?.MainWindow as MainWindow)?.CaptureModalSnapshot("IconPicker"),
            PreviewElementFactory = () => CreateLiveModalImagePreview("IconPicker"),
            InspectAction = mw => mw.ShowIconPickerModal()
        });

        _items.Add(new ViewSnapshotItem
        {
            Id = "modal_installed_apps",
            Title = "Modal: Windows Installed Apps Auto-Detector",
            Category = "Modals & Dialogs",
            BadgeColor = "#EC4899",
            StateTag = "App Scanner",
            Description = "System scanner modal detecting installed Windows applications with executable path validation and 1-click add.",
            Tags = new[] { "Modal", "Apps", "AutoDetect", "Scanner", "Windows" },
            TargetWidth = 920,
            TargetHeight = 580,
            LiveCaptureFactory = () => (Application.Current?.MainWindow as MainWindow)?.CaptureModalSnapshot("InstalledApps"),
            PreviewElementFactory = () => CreateLiveModalImagePreview("InstalledApps"),
            InspectAction = mw => mw.ShowInstalledAppsModal()
        });

        _items.Add(new ViewSnapshotItem
        {
            Id = "modal_exit_dialog",
            Title = "Dialog: Bilingual Exit Action Prompt (Arabic / English)",
            Category = "Modals & Dialogs",
            BadgeColor = "#EC4899",
            StateTag = "Action Dialog",
            Description = "Bilingual exit prompt offering clear choices: Minimize to system tray, Suspend overlays, or Complete Quit.",
            Tags = new[] { "Dialog", "Exit", "Prompt", "Tray", "Arabic", "Quit" },
            TargetWidth = 520,
            TargetHeight = 420,
            PreviewElementFactory = () => RealUiSnapshotFactory.CreateExitDialogSnapshot(),
            InspectAction = mw => mw.ShowCustomExitDialog()
        });

        _items.Add(new ViewSnapshotItem
        {
            Id = "control_color_picker",
            Title = "Control: HSL & Hex Spectrum Color Picker",
            Category = "Modals & Dialogs",
            BadgeColor = "#6366F1",
            StateTag = "Color Picker",
            Description = "Precision 2D gradient saturation/brightness spectrum picker with vertical hue slider, alpha opacity, and hex input.",
            Tags = new[] { "Control", "ColorPicker", "HSL", "Spectrum", "Hex" },
            TargetWidth = 460,
            TargetHeight = 440,
            PreviewElementFactory = () => RealUiSnapshotFactory.CreateColorPickerSnapshot(),
            InspectAction = mw => mw.NavigateToSection(2, 1)
        });
    }

    private static FrameworkElement CreateLiveImagePreview(int pageIndex, int subTabIndex)
    {
        var mw = Application.Current?.MainWindow as MainWindow;
        if (mw != null)
        {
            var bmp = mw.CaptureSectionSnapshot(pageIndex, subTabIndex);
            if (bmp != null)
            {
                return new Image
                {
                    Source = bmp,
                    Stretch = Stretch.Uniform,
                    SnapsToDevicePixels = true
                };
            }
        }

        // Fallback: build a clean placeholder container
        return new Border
        {
            Width = 880,
            Height = 520,
            Background = new SolidColorBrush(Color.FromRgb(0x0E, 0x0E, 0x12)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x24, 0x24, 0x30)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10)
        };
    }

    private static FrameworkElement CreateLiveModalImagePreview(string modalName)
    {
        var mw = Application.Current?.MainWindow as MainWindow;
        if (mw != null)
        {
            var bmp = mw.CaptureModalSnapshot(modalName);
            if (bmp != null)
            {
                return new Image
                {
                    Source = bmp,
                    Stretch = Stretch.Uniform,
                    SnapsToDevicePixels = true
                };
            }
        }

        return new Border
        {
            Width = 880,
            Height = 520,
            Background = new SolidColorBrush(Color.FromRgb(0x0E, 0x0E, 0x12)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x24, 0x24, 0x30)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10)
        };
    }
}
