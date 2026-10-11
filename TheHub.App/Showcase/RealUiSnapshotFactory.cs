using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using TheHub.Common.Config;
using TheHub.Common.Models;
using TheHub.Overlays.DynamicIsland;
using TheHub.Overlays.RadialLauncher;
using TheHub.Overlays.Widgets;
using TheHub.App.Controls;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using VerticalAlignment = System.Windows.VerticalAlignment;
using Orientation = System.Windows.Controls.Orientation;

namespace TheHub.App.Showcase;

public enum IslandSnapshotMode
{
    CompactIdle,
    CompactMusic,
    CompactVolume,
    CompactMicMute,
    ExpandedMedia,
    ExpandedAudioBrightness,
    ExpandedWorkspaces,
    EventUsb,
    EventDownload,
    EventClipboard,
    EventPrayer,
    EventPomodoro,
    EventBattery
}

/// <summary>
/// Factory creating 100% authentic visual UI elements directly from the application's actual windows, controls, and overlays.
/// </summary>
public static class RealUiSnapshotFactory
{
    public static FrameworkElement CreateDynamicIslandSnapshot(IslandSnapshotMode mode)
    {
        var island = new DynamicIslandWindow();
        
        // Copy window resources so detached IslandContainer maintains all custom styles and templates
        var host = new Border
        {
            Width = mode switch
            {
                IslandSnapshotMode.ExpandedMedia => 540,
                IslandSnapshotMode.ExpandedAudioBrightness => 540,
                IslandSnapshotMode.ExpandedWorkspaces => 540,
                _ => 480
            },
            Height = mode switch
            {
                IslandSnapshotMode.ExpandedWorkspaces => 360,
                IslandSnapshotMode.ExpandedMedia => 310,
                IslandSnapshotMode.ExpandedAudioBrightness => 310,
                _ => 150
            },
            Background = new SolidColorBrush(Color.FromRgb(0x0C, 0x0D, 0x11)),
            SnapsToDevicePixels = true,
            UseLayoutRounding = true
        };

        foreach (var key in island.Resources.Keys)
        {
            host.Resources[key] = island.Resources[key];
        }

        // Configure island state
        switch (mode)
        {
            case IslandSnapshotMode.CompactIdle:
                island.CompactView.Visibility = Visibility.Visible;
                island.ExpandedView.Visibility = Visibility.Collapsed;
                island.CompactGlyph.Text = "⚡";
                island.CompactTitle.Text = "The Hub";
                island.CompactDot.Fill = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81));
                island.IslandContainer.Width = 190;
                island.IslandContainer.Height = 38;
                break;

            case IslandSnapshotMode.CompactMusic:
                island.CompactView.Visibility = Visibility.Visible;
                island.ExpandedView.Visibility = Visibility.Collapsed;
                island.CompactGlyph.Text = "🎵";
                island.CompactTitle.Text = "Blinding Lights — The Weeknd";
                island.CompactDot.Fill = new SolidColorBrush(Color.FromRgb(0x8B, 0x5C, 0xF6));
                island.IslandContainer.Width = 270;
                island.IslandContainer.Height = 38;
                break;

            case IslandSnapshotMode.CompactVolume:
                island.CompactView.Visibility = Visibility.Visible;
                island.ExpandedView.Visibility = Visibility.Collapsed;
                island.CompactGlyph.Text = "🔊";
                island.CompactTitle.Text = "Master Volume: 65%";
                island.CompactDot.Fill = new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6));
                island.IslandContainer.Width = 230;
                island.IslandContainer.Height = 38;
                break;

            case IslandSnapshotMode.CompactMicMute:
                island.CompactView.Visibility = Visibility.Visible;
                island.ExpandedView.Visibility = Visibility.Collapsed;
                island.CompactGlyph.Text = "🎤";
                island.CompactTitle.Text = "Microphone Muted";
                island.CompactDot.Fill = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
                island.IslandContainer.Width = 210;
                island.IslandContainer.Height = 38;
                break;

            case IslandSnapshotMode.ExpandedMedia:
                island.CompactView.Visibility = Visibility.Collapsed;
                island.ExpandedView.Visibility = Visibility.Visible;
                island.ExpandedGlyph.Text = "🎵";
                island.ExpandedTitle.Text = "Blinding Lights";
                island.ExpandedSubtitle.Text = "The Weeknd — After Hours";
                island.MediaControlsPanel.Visibility = Visibility.Visible;
                island.PlayPauseButton.Content = "⏸";
                island.DashboardControlsPanel.Visibility = Visibility.Visible;
                island.SldVolume.Value = 65;
                island.TxtVolumePercent.Text = "65%";
                island.IslandContainer.Width = 430;
                island.IslandContainer.Height = 250;
                break;

            case IslandSnapshotMode.ExpandedAudioBrightness:
                island.CompactView.Visibility = Visibility.Collapsed;
                island.ExpandedView.Visibility = Visibility.Visible;
                island.ExpandedGlyph.Text = "⚡";
                island.ExpandedTitle.Text = "Audio Live";
                island.ExpandedSubtitle.Text = "Master: 65% • Display: 80%";
                island.MediaControlsPanel.Visibility = Visibility.Collapsed;
                island.DashboardControlsPanel.Visibility = Visibility.Visible;
                island.SldVolume.Value = 65;
                island.TxtVolumePercent.Text = "65%";
                island.IslandContainer.Width = 430;
                island.IslandContainer.Height = 240;
                break;

            case IslandSnapshotMode.ExpandedWorkspaces:
                island.CompactView.Visibility = Visibility.Collapsed;
                island.ExpandedView.Visibility = Visibility.Visible;
                island.ExpandedGlyph.Text = "🗂️";
                island.ExpandedTitle.Text = "Workspaces Drawer";
                island.ExpandedSubtitle.Text = "One-tap environment switcher";
                island.MediaControlsPanel.Visibility = Visibility.Collapsed;
                island.DashboardControlsPanel.Visibility = Visibility.Visible;
                island.SetWorkspaces(SettingsManager.Current.Workspaces ?? new List<WorkspaceConfig>
                {
                    new() { Name = "Gaming", IconGlyph = "🎮", AccentColor = "#EF4444", Description = "Auto-launch Steam, Discord, OBS" },
                    new() { Name = "Development", IconGlyph = "💻", AccentColor = "#3B82F6", Description = "VS Code, Terminal, Docker" },
                    new() { Name = "Creative", IconGlyph = "🎨", AccentColor = "#8B5CF6", Description = "DaVinci Resolve, Spotify" },
                    new() { Name = "Study & Focus", IconGlyph = "📚", AccentColor = "#10B981", Description = "Edge, Notion, Pomodoro" }
                });
                island.WorkspacePopover.Visibility = Visibility.Visible;
                island.IslandContainer.Width = 440;
                island.IslandContainer.Height = 310;
                break;

            case IslandSnapshotMode.EventUsb:
                island.CompactView.Visibility = Visibility.Collapsed;
                island.ExpandedView.Visibility = Visibility.Visible;
                island.ExpandedGlyph.Text = "🔌";
                island.ExpandedTitle.Text = "USB Device Connected";
                island.ExpandedSubtitle.Text = "Samsung T7 Shield SSD (1TB) ready";
                island.MediaControlsPanel.Visibility = Visibility.Collapsed;
                island.DashboardControlsPanel.Visibility = Visibility.Collapsed;
                island.IslandContainer.Width = 380;
                island.IslandContainer.Height = 85;
                break;

            case IslandSnapshotMode.EventDownload:
                island.CompactView.Visibility = Visibility.Collapsed;
                island.ExpandedView.Visibility = Visibility.Visible;
                island.ExpandedGlyph.Text = "⬇️";
                island.ExpandedTitle.Text = "Downloading Update";
                island.ExpandedSubtitle.Text = "TheHub_v0.2.0_Setup.exe (78% — 42 MB / 54 MB)";
                island.MediaControlsPanel.Visibility = Visibility.Collapsed;
                island.DashboardControlsPanel.Visibility = Visibility.Collapsed;
                island.IslandContainer.Width = 410;
                island.IslandContainer.Height = 85;
                break;

            case IslandSnapshotMode.EventClipboard:
                island.CompactView.Visibility = Visibility.Collapsed;
                island.ExpandedView.Visibility = Visibility.Visible;
                island.ExpandedGlyph.Text = "📋";
                island.ExpandedTitle.Text = "Copied to Clipboard";
                island.ExpandedSubtitle.Text = "https://github.com/ddmouh/the-hub";
                island.MediaControlsPanel.Visibility = Visibility.Collapsed;
                island.DashboardControlsPanel.Visibility = Visibility.Collapsed;
                island.IslandContainer.Width = 390;
                island.IslandContainer.Height = 85;
                break;

            case IslandSnapshotMode.EventPrayer:
                island.CompactView.Visibility = Visibility.Collapsed;
                island.ExpandedView.Visibility = Visibility.Visible;
                island.ExpandedGlyph.Text = "🕌";
                island.ExpandedTitle.Text = "صلاة العصر | Asr Prayer";
                island.ExpandedSubtitle.Text = "حان الآن موعد أذان العصر (03:45 PM)";
                island.MediaControlsPanel.Visibility = Visibility.Collapsed;
                island.DashboardControlsPanel.Visibility = Visibility.Collapsed;
                island.IslandContainer.Width = 400;
                island.IslandContainer.Height = 85;
                break;

            case IslandSnapshotMode.EventPomodoro:
                island.CompactView.Visibility = Visibility.Collapsed;
                island.ExpandedView.Visibility = Visibility.Visible;
                island.ExpandedGlyph.Text = "⏱️";
                island.ExpandedTitle.Text = "Pomodoro Focus Session";
                island.ExpandedSubtitle.Text = "Deep Work: 21:40 remaining (Session 2 of 4)";
                island.MediaControlsPanel.Visibility = Visibility.Collapsed;
                island.DashboardControlsPanel.Visibility = Visibility.Collapsed;
                island.IslandContainer.Width = 410;
                island.IslandContainer.Height = 85;
                break;

            case IslandSnapshotMode.EventBattery:
                island.CompactView.Visibility = Visibility.Collapsed;
                island.ExpandedView.Visibility = Visibility.Visible;
                island.ExpandedGlyph.Text = "🔋";
                island.ExpandedTitle.Text = "Battery Status";
                island.ExpandedSubtitle.Text = "94% — Plugged in, Fast Charging";
                island.MediaControlsPanel.Visibility = Visibility.Collapsed;
                island.DashboardControlsPanel.Visibility = Visibility.Collapsed;
                island.IslandContainer.Width = 380;
                island.IslandContainer.Height = 85;
                break;
        }

        // Unparent IslandContainer and insert into host
        if (island.Content is Grid rootGrid)
        {
            rootGrid.Children.Remove(island.IslandContainer);
        }

        island.IslandContainer.HorizontalAlignment = HorizontalAlignment.Center;
        island.IslandContainer.VerticalAlignment = VerticalAlignment.Center;
        host.Child = island.IslandContainer;

        return host;
    }

    public static FrameworkElement CreateRadialWheelSnapshot(int highlightedIndex = 1)
    {
        var shortcuts = SettingsManager.Current.LauncherShortcuts != null && SettingsManager.Current.LauncherShortcuts.Count >= 4
            ? SettingsManager.Current.LauncherShortcuts
            : new List<AppShortcut>
            {
                new() { Title = "Google Chrome", ExecutablePath = "chrome.exe", IconGlyph = "🌐" },
                new() { Title = "Discord", ExecutablePath = "discord.exe", IconGlyph = "💬" },
                new() { Title = "Visual Studio Code", ExecutablePath = "code.exe", IconGlyph = "💻" },
                new() { Title = "Spotify", ExecutablePath = "spotify.exe", IconGlyph = "🎵" },
                new() { Title = "Terminal", ExecutablePath = "wt.exe", IconGlyph = "⚡" },
                new() { Title = "Steam", ExecutablePath = "steam.exe", IconGlyph = "🎮" }
            };

        var wheel = new RadialWheelCanvas
        {
            Width = 520,
            Height = 520,
            OuterRadius = 155,
            InnerRadius = 60,
            IconSize = 64,
            Shortcuts = shortcuts,
            HighlightedIndex = highlightedIndex,
            SelectedLabel = highlightedIndex >= 0 && highlightedIndex < shortcuts.Count ? shortcuts[highlightedIndex].Title : "The Hub",
            BloomProgress = 1.0,
            WheelOpacityFactor = 0.92,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var host = new Border
        {
            Width = 540,
            Height = 540,
            Background = new SolidColorBrush(Color.FromRgb(0x09, 0x0A, 0x0E)),
            Child = wheel,
            SnapsToDevicePixels = true,
            UseLayoutRounding = true
        };

        return host;
    }

    public static FrameworkElement CreateDesktopClockSnapshot(bool analogMode)
    {
        var clock = new DesktopClockWidget();
        var cfg = WidgetsConfig.Load();
        cfg.DisplayMode = analogMode ? "Analog" : "Digital";
        clock.ApplyConfig(cfg);
        clock.UpdateClock();

        var border = (Border)clock.Content;
        clock.Content = null;

        var host = new Border
        {
            Width = analogMode ? 380 : 320,
            Height = analogMode ? 380 : 170,
            Background = new SolidColorBrush(Color.FromRgb(0x0A, 0x0B, 0x0F)),
            Padding = new Thickness(14),
            Child = border,
            SnapsToDevicePixels = true,
            UseLayoutRounding = true
        };

        foreach (var k in clock.Resources.Keys)
        {
            host.Resources[k] = clock.Resources[k];
        }

        border.HorizontalAlignment = HorizontalAlignment.Center;
        border.VerticalAlignment = VerticalAlignment.Center;

        return host;
    }

    public static FrameworkElement CreateDesktopResourceSnapshot(bool alertSpike = false)
    {
        var res = new DesktopResourceWidget();
        res.UpdateMetrics();

        var border = (Border)res.Content;
        res.Content = null;

        var host = new Border
        {
            Width = 350,
            Height = 250,
            Background = new SolidColorBrush(Color.FromRgb(0x0A, 0x0B, 0x0F)),
            Padding = new Thickness(12),
            Child = border,
            SnapsToDevicePixels = true,
            UseLayoutRounding = true
        };

        foreach (var k in res.Resources.Keys)
        {
            host.Resources[k] = res.Resources[k];
        }

        border.HorizontalAlignment = HorizontalAlignment.Center;
        border.VerticalAlignment = VerticalAlignment.Center;

        return host;
    }

    public static FrameworkElement CreateExitDialogSnapshot()
    {
        var dlg = new ExitDialog();
        var border = (Border)dlg.Content;
        dlg.Content = null;

        var host = new Border
        {
            Width = 500,
            Height = 400,
            Background = new SolidColorBrush(Color.FromRgb(0x09, 0x0A, 0x0E)),
            Padding = new Thickness(10),
            Child = border,
            SnapsToDevicePixels = true,
            UseLayoutRounding = true
        };

        foreach (var k in dlg.Resources.Keys)
        {
            host.Resources[k] = dlg.Resources[k];
        }

        border.HorizontalAlignment = HorizontalAlignment.Center;
        border.VerticalAlignment = VerticalAlignment.Center;

        return host;
    }

    public static FrameworkElement CreateColorPickerSnapshot()
    {
        var picker = new ColorPickerControl
        {
            SelectedColor = Color.FromRgb(0x3B, 0x82, 0xF6),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var host = new Border
        {
            Width = 440,
            Height = 420,
            Background = new SolidColorBrush(Color.FromRgb(0x09, 0x0A, 0x0E)),
            Padding = new Thickness(16),
            Child = picker,
            SnapsToDevicePixels = true,
            UseLayoutRounding = true
        };

        return host;
    }
}
