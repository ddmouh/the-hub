using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using MessageBox = System.Windows.MessageBox;
using UserControl = System.Windows.Controls.UserControl;
using TheHub.Common.Config;
using TheHub.Common.Models;
using TheHub.Core;
using TheHub.Core.Native;
using TheHub.Overlays.DynamicIsland;

namespace TheHub.App.Pages;

public partial class IslandPage : UserControl
{
    private CoreHubManager? _core;
    private IslandModule? _islandModule;
    private OverviewViewModel? _overviewViewModel;
    private bool _isInitializing = true;

    public IslandPage()
    {
        InitializeComponent();
    }

    public void Initialize(CoreHubManager core, IslandModule islandModule, OverviewViewModel? overviewViewModel)
    {
        _core = core;
        _islandModule = islandModule;
        _overviewViewModel = overviewViewModel;
        LoadSettings();
    }

    public void LoadSettings()
    {
        _isInitializing = true;
        try
        {
            var settings = SettingsManager.Current;

            ChkMedia.IsChecked = settings.IslandShowMedia;
            ChkShowActiveApp.IsChecked = settings.IslandShowActiveApp;
            ChkVolume.IsChecked = settings.IslandShowVolume;
            ChkClipboard.IsChecked = settings.IslandShowClipboard;
            ChkUsb.IsChecked = settings.IslandShowUsb;
            ChkAlwaysShowFullscreen.IsChecked = settings.IslandAlwaysShowOnFullscreen;
            ChkLockPosition.IsChecked = settings.IslandLockPosition;
            ChkShowQuickControls.IsChecked = settings.IslandShowHoverQuickControls;
            ChkShowMicMute.IsChecked = settings.IslandShowMicMute;
            ChkShowBrightness.IsChecked = settings.IslandShowBrightnessControl;

            if (_overviewViewModel != null)
            {
                CmbDynamicIslandFont.ItemsSource = _overviewViewModel.AvailableFontFamilies;
                CmbDynamicIslandFont.SelectedItem = _overviewViewModel.SelectedDynamicIslandFontFamily;
            }

            SldIslandScale.Value = settings.IslandScale;
            TxtScaleValue.Text = $"{(int)Math.Round(settings.IslandScale * 100)}%";
            SldIslandTop.Value = settings.IslandTopOffset;
            TxtTopValue.Text = $"{(int)settings.IslandTopOffset} px";

            // Populate Target Display Screen ComboBox
            var monitors = DisplayMonitorHelper.GetAllMonitors();
            CmbIslandMonitor.ItemsSource = monitors.Select(m => m.Name).ToList();
            int selectedIndex = Math.Clamp(settings.IslandMonitorIndex, 0, Math.Max(0, monitors.Count - 1));
            CmbIslandMonitor.SelectedIndex = selectedIndex;
        }
        finally
        {
            _isInitializing = false;
        }
    }

    private void OnSettingToggled(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;
        if (ChkMedia == null || ChkVolume == null || ChkClipboard == null || ChkUsb == null || ChkAlwaysShowFullscreen == null || ChkLockPosition == null || ChkShowQuickControls == null || ChkShowMicMute == null || ChkShowBrightness == null || ChkShowActiveApp == null) return;

        var settings = SettingsManager.Current;
        settings.IslandShowMedia = ChkMedia.IsChecked ?? true;
        settings.IslandShowActiveApp = ChkShowActiveApp.IsChecked ?? true;
        settings.IslandShowVolume = ChkVolume.IsChecked ?? true;
        settings.IslandShowClipboard = ChkClipboard.IsChecked ?? true;
        settings.IslandShowUsb = ChkUsb.IsChecked ?? true;
        settings.IslandHideOnFullscreen = true;
        settings.IslandAlwaysShowOnFullscreen = ChkAlwaysShowFullscreen.IsChecked ?? false;
        settings.IslandLockPosition = ChkLockPosition.IsChecked ?? false;
        settings.IslandShowHoverQuickControls = ChkShowQuickControls.IsChecked ?? true;
        settings.IslandShowMicMute = ChkShowMicMute.IsChecked ?? true;
        settings.IslandShowBrightnessControl = ChkShowBrightness.IsChecked ?? true;
        SettingsManager.SaveSettings(settings);

        _islandModule?.UpdateLayout();

        if (sender == ChkShowQuickControls || sender == ChkShowMicMute || sender == ChkShowBrightness)
        {
            _islandModule?.ShowSettingsPreview();
        }
    }

    private void OnPreviewIslandClick(object sender, RoutedEventArgs e)
    {
        _islandModule?.ShowSettingsPreview();
    }

    private void OnIslandScaleChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isInitializing || TxtScaleValue == null || SldIslandScale == null) return;
        var settings = SettingsManager.Current;
        settings.IslandScale = Math.Round(SldIslandScale.Value, 2);
        TxtScaleValue.Text = $"{(int)Math.Round(settings.IslandScale * 100)}%";
        SettingsManager.SaveSettings(settings);
        _islandModule?.UpdateLayout();
    }

    private void OnIslandTopChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isInitializing || TxtTopValue == null || SldIslandTop == null) return;
        var settings = SettingsManager.Current;
        settings.IslandTopOffset = Math.Round(SldIslandTop.Value);
        TxtTopValue.Text = $"{(int)settings.IslandTopOffset} px";
        if (settings.IslandCustomY.HasValue)
        {
            settings.IslandCustomY = settings.IslandTopOffset;
        }
        SettingsManager.SaveSettings(settings);
        _islandModule?.UpdateLayout();
    }

    private void OnResetIslandPositionClick(object sender, RoutedEventArgs e)
    {
        _islandModule?.ResetPositionToCenter();
        _core?.TriggerIslandEvent(new DynamicIslandEvent
        {
            Type = IslandEventType.SystemAlert,
            Title = "Position Centered",
            Subtitle = "Island snapped to screen center",
            Glyph = "🎯",
            DisplayDurationMs = 1800
        });
    }

    private void OnIslandMonitorSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing || CmbIslandMonitor == null || CmbIslandMonitor.SelectedIndex < 0) return;

        var settings = SettingsManager.Current;
        if (settings.IslandMonitorIndex != CmbIslandMonitor.SelectedIndex)
        {
            settings.IslandMonitorIndex = CmbIslandMonitor.SelectedIndex;
            SettingsManager.SaveSettings(settings);

            _islandModule?.UpdateLayout();

            _core?.TriggerIslandEvent(new DynamicIslandEvent
            {
                Type = IslandEventType.SystemAlert,
                Title = "Display Screen Changed",
                Subtitle = $"Anchored to screen {settings.IslandMonitorIndex + 1}",
                Glyph = "🖥️",
                DisplayDurationMs = 2000
            });
        }
    }

    private void OnDynamicIslandFontChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing || _overviewViewModel == null || CmbDynamicIslandFont == null) return;
        if (CmbDynamicIslandFont.SelectedItem is string family)
        {
            _overviewViewModel.SelectedDynamicIslandFontFamily = family;
            _islandModule?.UpdateLayout();
        }
    }

    private void OnTestMediaIslandClick(object sender, RoutedEventArgs e)
    {
        _core?.TriggerIslandEvent(new DynamicIslandEvent
        {
            Type = IslandEventType.Media,
            Title = "Blinding Lights",
            Subtitle = "The Weeknd — Spotify",
            Glyph = "🎵",
            IsPlaying = true,
            IsPersistent = false,
            DisplayDurationMs = 3500
        });
    }

    private void OnTestVolumeIslandClick(object sender, RoutedEventArgs e)
    {
        _core?.TriggerIslandEvent(new DynamicIslandEvent
        {
            Type = IslandEventType.Volume,
            Title = "Volume",
            Subtitle = "75%",
            Glyph = "🔊",
            Value = 75,
            DisplayDurationMs = 2500
        });
    }

    private void OnTestClipboardIslandClick(object sender, RoutedEventArgs e)
    {
        _core?.TriggerIslandEvent(new DynamicIslandEvent
        {
            Type = IslandEventType.Clipboard,
            Title = "Copied to Clipboard",
            Subtitle = "https://github.com/the-hub",
            Glyph = "📋",
            DisplayDurationMs = 2500
        });
    }

    private void OnTestTimerIslandClick(object sender, RoutedEventArgs e)
    {
        _core?.TriggerIslandEvent(new DynamicIslandEvent
        {
            Type = IslandEventType.Timer,
            Title = "Focus Session",
            Subtitle = "25:00 Remaining",
            Glyph = "⏱️",
            DisplayDurationMs = 3000
        });
    }
}
