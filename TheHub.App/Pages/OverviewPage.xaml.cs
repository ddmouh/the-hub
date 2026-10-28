using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using ComboBoxItem = System.Windows.Controls.ComboBoxItem;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using MessageBoxResult = System.Windows.MessageBoxResult;
using RadioButton = System.Windows.Controls.RadioButton;
using UserControl = System.Windows.Controls.UserControl;
using TheHub.App.Services;
using TheHub.Common.Config;
using TheHub.Common.Models;
using TheHub.Core;
using TheHub.Core.Modules;
using TheHub.Core.Native;
using TheHub.Core.Services;
using TheHub.Overlays.DynamicIsland;
using TheHub.Overlays.Wallpaper;
using TheHub.Overlays.Widgets;

namespace TheHub.App.Pages;

public partial class OverviewPage : UserControl
{
    private WallpaperManager? _wallpaperManager;
    private WallpaperModule? _wallpaperModule;
    private WidgetsModule? _widgetsModule;
    private CoreHubManager? _core;
    private OverviewViewModel? _overviewViewModel;
    private DesktopClockWidget? _clockWidget;
    private DesktopCpuWidget? _cpuWidget;
    private DesktopGpuWidget? _gpuWidget;
    private DesktopRamWidget? _ramWidget;
    private IslandModule? _islandModule;
    private string _selectedMonitorTarget = "ALL";
    private bool _isInitializing = false;

    public Action<string, bool>? OnWallpaperSettingChangedCallback;

    public OverviewPage()
    {
        InitializeComponent();
    }

    public void Initialize(
        WallpaperManager wallpaperManager,
        WallpaperModule wallpaperModule,
        WidgetsModule widgetsModule,
        CoreHubManager core,
        OverviewViewModel overviewViewModel,
        IslandModule? islandModule = null,
        DesktopClockWidget? clockWidget = null,
        DesktopCpuWidget? cpuWidget = null,
        DesktopGpuWidget? gpuWidget = null,
        DesktopRamWidget? ramWidget = null)
    {
        _wallpaperManager = wallpaperManager;
        _wallpaperModule = wallpaperModule;
        _widgetsModule = widgetsModule;
        _core = core;
        _overviewViewModel = overviewViewModel;
        _clockWidget = clockWidget;
        _cpuWidget = cpuWidget;
        _gpuWidget = gpuWidget;
        _ramWidget = ramWidget;
        _islandModule = islandModule;

        // Wire OverviewWidgetsControl
        if (OverviewWidgetsControl != null && _overviewViewModel != null)
        {
            OverviewWidgetsControl.DataContext = _overviewViewModel;
            _overviewViewModel.WidgetConfigChanged += OnWidgetConfigChanged;
            _overviewViewModel.ClockLockedChanged += SetClockWidgetLocked;
            _overviewViewModel.GaugesLockedChanged += SetResourceWidgetLocked;
        }

        LoadSettings();
        RestoreActiveWallpapers();
    }

    public void LoadSettings()
    {
        _isInitializing = true;
        try
        {
            var settings = SettingsManager.Current;

            ChkPauseWallpaperFullscreen.IsChecked = settings.WallpaperPauseOnFullscreen;
            ChkPauseWallpaperBattery.IsChecked = settings.WallpaperPauseOnBattery;
            ChkMuteWallpaperAudio.IsChecked = settings.WallpaperMuted;

            string currentFit = settings.WallpaperFitMode ?? "Fill";
            foreach (ComboBoxItem item in CmbWallpaperFit.Items)
            {
                if (string.Equals(item.Content?.ToString(), currentFit, StringComparison.OrdinalIgnoreCase))
                {
                    CmbWallpaperFit.SelectedItem = item;
                    break;
                }
            }

            _selectedMonitorTarget = string.IsNullOrWhiteSpace(settings.SelectedTargetMonitor) ? "ALL" : settings.SelectedTargetMonitor;

            PopulateMonitorSelectorUI();
            RefreshWallpapersGallery();
        }
        finally
        {
            _isInitializing = false;
        }
    }

    private void OnOverviewSubTabChanged(object sender, RoutedEventArgs e)
    {
        if (SubPanelOverviewWallpapers == null || SubPanelOverviewWidgets == null ||
            SubPanelOverviewTaskbar == null) return;

        if (sender is RadioButton rb && rb.Tag is string tagStr && int.TryParse(tagStr, out int tabIndex))
        {
            SubPanelOverviewWallpapers.Visibility = (tabIndex == 0) ? Visibility.Visible : Visibility.Collapsed;
            SubPanelOverviewWidgets.Visibility = (tabIndex == 1) ? Visibility.Visible : Visibility.Collapsed;
            SubPanelOverviewTaskbar.Visibility = (tabIndex == 2) ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    public void RefreshWallpapersGallery()
    {
        var settings = SettingsManager.Current;
        if (WallpapersGalleryControl != null)
        {
            WallpapersGalleryControl.ItemsSource = null;
            WallpapersGalleryControl.ItemsSource = settings.Wallpapers;
        }
        if (WallpaperEmptyPrompt != null)
        {
            WallpaperEmptyPrompt.Visibility = (settings.Wallpapers.Count == 0) ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    public void PopulateMonitorSelectorUI()
    {
        if (MonitorSelectorPanel == null) return;
        MonitorSelectorPanel.Children.Clear();

        var settings = SettingsManager.Current;
        var monitors = DisplayMonitorHelper.GetAllMonitors();

        bool isAllSelected = string.Equals(_selectedMonitorTarget, "ALL", StringComparison.OrdinalIgnoreCase);
        string allWpTitle = "None";
        if (settings.MonitorWallpapers.TryGetValue("ALL", out var allId))
        {
            var found = settings.Wallpapers.FirstOrDefault(w => w.Id == allId);
            if (found != null) allWpTitle = found.Title;
        }

        var allCard = CreateMonitorCard(
            title: "🖥️🖥️ All Displays",
            subtitle: $"Duplicate Mode • Active: {allWpTitle}",
            isSelected: isAllSelected,
            onClick: () =>
            {
                _selectedMonitorTarget = "ALL";
                settings.SelectedTargetMonitor = "ALL";
                SettingsManager.SaveSettings();
                PopulateMonitorSelectorUI();
            });
        MonitorSelectorPanel.Children.Add(allCard);

        foreach (var monitor in monitors)
        {
            bool isSelected = string.Equals(_selectedMonitorTarget, monitor.DeviceName, StringComparison.OrdinalIgnoreCase);
            string assignedWp = "None";
            if (settings.MonitorWallpapers.TryGetValue(monitor.DeviceName, out var wpId))
            {
                var found = settings.Wallpapers.FirstOrDefault(w => w.Id == wpId);
                if (found != null) assignedWp = found.Title;
            }

            string primaryTag = monitor.IsPrimary ? " [Primary]" : "";
            string devName = monitor.DeviceName;
            var monCard = CreateMonitorCard(
                title: $"🖥️ Display {monitor.Index + 1} ({monitor.ResolutionText}){primaryTag}",
                subtitle: $"Individual • Active: {assignedWp}",
                isSelected: isSelected,
                onClick: () =>
                {
                    _selectedMonitorTarget = devName;
                    settings.SelectedTargetMonitor = devName;
                    SettingsManager.SaveSettings();
                    PopulateMonitorSelectorUI();
                });
            MonitorSelectorPanel.Children.Add(monCard);
        }

        if (TxtSelectedTargetDisplay != null)
        {
            if (isAllSelected)
            {
                TxtSelectedTargetDisplay.Text = $"All Displays (Duplicate Mode - {monitors.Count} Screen{(monitors.Count > 1 ? "s" : "")})";
            }
            else
            {
                var targetMon = monitors.FirstOrDefault(m => string.Equals(m.DeviceName, _selectedMonitorTarget, StringComparison.OrdinalIgnoreCase));
                TxtSelectedTargetDisplay.Text = targetMon != null
                    ? $"{targetMon.DisplayTitle} ({targetMon.DeviceName})"
                    : $"Display {_selectedMonitorTarget}";
            }
        }

        if (TxtCurrentTargetWallpaperStatus != null)
        {
            string currentWp = "None";
            if (settings.MonitorWallpapers.TryGetValue(_selectedMonitorTarget, out var currentId))
            {
                var found = settings.Wallpapers.FirstOrDefault(w => w.Id == currentId);
                if (found != null) currentWp = $"{found.Title} ({found.TypeBadgeText})";
            }
            TxtCurrentTargetWallpaperStatus.Text = $"Wallpaper: {currentWp}";
        }
    }

    private Border CreateMonitorCard(string title, string subtitle, bool isSelected, Action onClick)
    {
        var border = new Border
        {
            Background = new System.Windows.Media.SolidColorBrush(isSelected
                ? System.Windows.Media.Color.FromRgb(0x1E, 0x29, 0x3B)
                : System.Windows.Media.Color.FromRgb(0x16, 0x16, 0x1D)),
            BorderBrush = new System.Windows.Media.SolidColorBrush(isSelected
                ? System.Windows.Media.Color.FromRgb(0x3B, 0x82, 0xF6)
                : System.Windows.Media.Color.FromRgb(0x2A, 0x2A, 0x36)),
            BorderThickness = new Thickness(isSelected ? 1.5 : 1.0),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 8, 14, 8),
            Margin = new Thickness(0, 0, 10, 0),
            Cursor = System.Windows.Input.Cursors.Hand
        };

        var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var t1 = new TextBlock
        {
            Text = title,
            FontSize = 12,
            FontWeight = isSelected ? FontWeights.Bold : FontWeights.SemiBold,
            Foreground = isSelected
                ? System.Windows.Media.Brushes.White
                : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xD4, 0xD4, 0xD8))
        };
        var t2 = new TextBlock
        {
            Text = subtitle,
            FontSize = 10.5,
            Foreground = isSelected
                ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x93, 0xC5, 0xFD))
                : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x71, 0x71, 0x7A)),
            Margin = new Thickness(0, 2, 0, 0)
        };

        sp.Children.Add(t1);
        sp.Children.Add(t2);
        border.Child = sp;

        border.MouseLeftButtonDown += (s, e) => onClick();
        return border;
    }

    private void OnRefreshDisplaysClick(object sender, RoutedEventArgs e)
    {
        var monitors = DisplayMonitorHelper.GetAllMonitors();
        PopulateMonitorSelectorUI();

        _core?.TriggerIslandEvent(new DynamicIslandEvent
        {
            Type = IslandEventType.SystemAlert,
            Title = "Displays Refreshed",
            Subtitle = $"Found {monitors.Count} connected monitor(s)",
            Glyph = "🖥️",
            DisplayDurationMs = 2500
        });
    }

    private void OnClearCurrentDisplayWallpaperClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var settings = SettingsManager.Current;

            if (string.Equals(_selectedMonitorTarget, "ALL", StringComparison.OrdinalIgnoreCase))
            {
                _wallpaperManager?.DisposeWallpapers();
                settings.MonitorWallpapers.Clear();
                settings.ActiveWallpaperId = null;
                foreach (var wp in settings.Wallpapers)
                {
                    wp.IsActive = false;
                    wp.NotifyStateChanged();
                }
            }
            else
            {
                _wallpaperManager?.StopMonitor(_selectedMonitorTarget);
                settings.MonitorWallpapers.Remove(_selectedMonitorTarget);

                foreach (var wp in settings.Wallpapers)
                {
                    wp.IsActive = settings.MonitorWallpapers.Values.Contains(wp.Id);
                    wp.NotifyStateChanged();
                }
            }

            SettingsManager.SaveSettings();
            RefreshWallpapersGallery();
            PopulateMonitorSelectorUI();

            _core?.TriggerIslandEvent(new DynamicIslandEvent
            {
                Type = IslandEventType.SystemAlert,
                Title = "Display Cleared",
                Subtitle = $"Wallpaper cleared for {_selectedMonitorTarget}",
                Glyph = "🛑",
                DisplayDurationMs = 2200
            });
        }
        catch (Exception ex)
        {
            AppLogger.Log($"OnClearCurrentDisplayWallpaperClick error: {ex.Message}");
        }
    }

    public void RestoreActiveWallpapers()
    {
        try
        {
            var settings = SettingsManager.Current;

            string? ResolveExistingPath(string? path)
            {
                if (string.IsNullOrWhiteSpace(path)) return null;
                string abs = Path.GetFullPath(path);
                if (File.Exists(abs)) return abs;

                string fallback = Path.Combine(WallpaperService.GetWallpapersStorageDirectory(), Path.GetFileName(path));
                if (File.Exists(fallback)) return fallback;

                return null;
            }

            if (settings.MonitorWallpapers.TryGetValue("ALL", out var allWpId))
            {
                var wp = settings.Wallpapers.FirstOrDefault(w => w.Id == allWpId);
                if (wp != null)
                {
                    string? resolved = ResolveExistingPath(wp.FilePath);
                    if (resolved != null)
                    {
                        wp.FilePath = resolved;
                        if (wp.Type == WallpaperType.Animated)
                            _wallpaperManager?.PlayVideoOnAll(resolved, settings.WallpaperMuted, 0.0);
                        else
                            WallpaperService.ApplyStaticWallpaper(resolved);
                        return;
                    }
                }
            }

            if (settings.MonitorWallpapers.Count > 0)
            {
                foreach (var kvp in settings.MonitorWallpapers)
                {
                    var wp = settings.Wallpapers.FirstOrDefault(w => w.Id == kvp.Value);
                    if (wp != null)
                    {
                        string? resolved = ResolveExistingPath(wp.FilePath);
                        if (resolved != null)
                        {
                            wp.FilePath = resolved;
                            if (wp.Type == WallpaperType.Animated)
                                _wallpaperManager?.PlayVideoOnMonitor(kvp.Key, resolved, settings.WallpaperMuted, 0.0);
                            else
                                WallpaperService.ApplyStaticWallpaper(resolved);
                        }
                    }
                }
                return;
            }

            if (!string.IsNullOrEmpty(settings.ActiveWallpaperId))
            {
                var wp = settings.Wallpapers.FirstOrDefault(w => w.Id == settings.ActiveWallpaperId && w.IsActive);
                if (wp != null)
                {
                    string? resolved = ResolveExistingPath(wp.FilePath);
                    if (resolved != null)
                    {
                        wp.FilePath = resolved;
                        ApplyWallpaperItem(wp);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Log($"RestoreActiveWallpapers error: {ex.Message}");
        }
    }

    private void OnAddWallpaperClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var ofd = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select Wallpaper (Animated Video or Static Image)",
                Filter = "All Supported Wallpapers (*.mp4;*.webm;*.gif;*.png;*.jpg;*.jpeg;*.bmp)|*.mp4;*.webm;*.gif;*.png;*.jpg;*.jpeg;*.bmp|Video Wallpapers (*.mp4;*.webm;*.gif)|*.mp4;*.webm;*.gif|Static Images (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|All Files (*.*)|*.*",
                Multiselect = false
            };

            if (ofd.ShowDialog() == true)
            {
                string rawPath = ofd.FileName;
                string fullPath = Path.GetFullPath(rawPath);

                string persistentPath;
                try
                {
                    persistentPath = WallpaperService.ImportWallpaperFile(fullPath);
                }
                catch (Exception ex)
                {
                    AppLogger.Log($"[WallpaperService] Import failed ({ex.Message}), using source full path: '{fullPath}'");
                    persistentPath = fullPath;
                }

                string fileName = Path.GetFileNameWithoutExtension(persistentPath);
                var wpType = WallpaperService.DetectWallpaperType(persistentPath);

                var settings = SettingsManager.Current;
                var newItem = new WallpaperItem
                {
                    Title = fileName,
                    FilePath = persistentPath,
                    Type = wpType,
                    IsActive = true
                };

                settings.Wallpapers.Insert(0, newItem);
                SettingsManager.SaveSettings();

                if (wpType == WallpaperType.Animated)
                {
                    var codecInfo = VideoCodecHelper.DetectCodec(persistentPath);
                    if (!codecInfo.IsUniversallySupported)
                    {
                        MessageBox.Show(
                            $"Video Codec Notice: {codecInfo.FriendlyName}\n\n" +
                            $"{codecInfo.WarningMessage}\n\n" +
                            $"Recommendation:\n{codecInfo.Recommendation}",
                            "The Hub - Video Codec Notice",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    }
                }

                ApplyWallpaperItem(newItem);

                _core?.TriggerIslandEvent(new DynamicIslandEvent
                {
                    Type = IslandEventType.SystemAlert,
                    Title = "Wallpaper Added & Applied",
                    Subtitle = $"{newItem.Title} ({newItem.TypeBadgeText})",
                    Glyph = newItem.Type == WallpaperType.Animated ? "🎬" : "🖼️",
                    DisplayDurationMs = 3000
                });
            }
        }
        catch (Exception ex)
        {
            AppLogger.Log($"OnAddWallpaperClick error: {ex.Message}");
            MessageBox.Show($"Failed to add wallpaper: {ex.Message}", "The Hub", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public void ApplyWallpaperItem(WallpaperItem item)
    {
        try
        {
            var settings = SettingsManager.Current;

            string resolvedPath = Path.GetFullPath(item.FilePath);
            if (!File.Exists(resolvedPath))
            {
                string fallback = Path.Combine(WallpaperService.GetWallpapersStorageDirectory(), Path.GetFileName(item.FilePath));
                if (File.Exists(fallback))
                {
                    resolvedPath = fallback;
                    item.FilePath = fallback;
                    SettingsManager.SaveSettings();
                }
                else
                {
                    item.NotifyStateChanged();
                    _core?.TriggerIslandEvent(new DynamicIslandEvent
                    {
                        Type = IslandEventType.SystemAlert,
                        Title = "Wallpaper File Missing",
                        Subtitle = $"Cannot find file: '{Path.GetFileName(item.FilePath)}'",
                        Glyph = "⚠️",
                        DisplayDurationMs = 3500
                    });
                    RefreshWallpapersGallery();
                    return;
                }
            }

            if (string.Equals(_selectedMonitorTarget, "ALL", StringComparison.OrdinalIgnoreCase))
            {
                settings.MonitorWallpapers.Clear();
                settings.MonitorWallpapers["ALL"] = item.Id;
                settings.ActiveWallpaperId = item.Id;

                foreach (var wp in settings.Wallpapers)
                {
                    wp.IsActive = (wp.Id == item.Id);
                    wp.NotifyStateChanged();
                }

                if (item.Type == WallpaperType.Animated)
                {
                    var codec = VideoCodecHelper.DetectCodec(resolvedPath);
                    if (!codec.IsUniversallySupported)
                    {
                        _core?.TriggerIslandEvent(new DynamicIslandEvent
                        {
                            Type = IslandEventType.SystemAlert,
                            Title = $"Notice: {codec.FriendlyName}",
                            Subtitle = "AV1 / 4K codec may require Windows AV1 Extension.",
                            Glyph = "ℹ️",
                            DisplayDurationMs = 3500
                        });
                    }
                    _wallpaperManager?.PlayVideoOnAll(resolvedPath, settings.WallpaperMuted, 0.0);
                }
                else
                {
                    _wallpaperManager?.DisposeWallpapers();
                    WallpaperService.ApplyStaticWallpaper(resolvedPath);
                }
            }
            else
            {
                if (settings.MonitorWallpapers.ContainsKey("ALL"))
                    settings.MonitorWallpapers.Remove("ALL");
                settings.MonitorWallpapers[_selectedMonitorTarget] = item.Id;
                settings.ActiveWallpaperId = item.Id;

                foreach (var wp in settings.Wallpapers)
                {
                    wp.IsActive = settings.MonitorWallpapers.Values.Contains(wp.Id);
                    wp.NotifyStateChanged();
                }

                if (item.Type == WallpaperType.Animated)
                {
                    var codec = VideoCodecHelper.DetectCodec(resolvedPath);
                    if (!codec.IsUniversallySupported)
                    {
                        _core?.TriggerIslandEvent(new DynamicIslandEvent
                        {
                            Type = IslandEventType.SystemAlert,
                            Title = $"Notice: {codec.FriendlyName}",
                            Subtitle = "AV1 / 4K codec may require Windows AV1 Extension.",
                            Glyph = "ℹ️",
                            DisplayDurationMs = 3500
                        });
                    }
                    _wallpaperManager?.PlayVideoOnMonitor(_selectedMonitorTarget, resolvedPath, settings.WallpaperMuted, 0.0);
                }
                else
                {
                    _wallpaperManager?.StopMonitor(_selectedMonitorTarget);
                    WallpaperService.ApplyStaticWallpaper(resolvedPath);
                }
            }

            SettingsManager.SaveSettings();
            RefreshWallpapersGallery();
            PopulateMonitorSelectorUI();
        }
        catch (Exception ex)
        {
            AppLogger.Log($"ApplyWallpaperItem error: {ex.Message}");
        }
    }

    private void OnApplyWallpaperCardClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is WallpaperItem item)
        {
            ApplyWallpaperItem(item);
            string targetDesc = string.Equals(_selectedMonitorTarget, "ALL", StringComparison.OrdinalIgnoreCase)
                ? "All Displays"
                : _selectedMonitorTarget;

            _core?.TriggerIslandEvent(new DynamicIslandEvent
            {
                Type = IslandEventType.SystemAlert,
                Title = "Wallpaper Applied",
                Subtitle = $"{item.Title} ➔ {targetDesc}",
                Glyph = item.Type == WallpaperType.Animated ? "🎬" : "🖼️",
                DisplayDurationMs = 2500
            });
        }
    }

    private void OnDeleteWallpaperCardClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is WallpaperItem item)
        {
            var confirm = MessageBox.Show(
                $"Are you sure you want to remove '{item.Title}'?",
                "Remove Wallpaper",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm == MessageBoxResult.Yes)
            {
                var settings = SettingsManager.Current;

                if (settings.MonitorWallpapers.TryGetValue("ALL", out var allId) && allId == item.Id)
                {
                    _wallpaperManager?.DisposeWallpapers();
                    settings.MonitorWallpapers.Remove("ALL");
                }
                else
                {
                    var matchingDisplays = settings.MonitorWallpapers.Where(kvp => kvp.Value == item.Id).Select(kvp => kvp.Key).ToList();
                    foreach (var disp in matchingDisplays)
                    {
                        _wallpaperManager?.StopMonitor(disp);
                        settings.MonitorWallpapers.Remove(disp);
                    }
                }

                if (settings.ActiveWallpaperId == item.Id)
                {
                    settings.ActiveWallpaperId = null;
                }

                settings.Wallpapers.Remove(item);
                SettingsManager.SaveSettings();
                RefreshWallpapersGallery();
                PopulateMonitorSelectorUI();

                _core?.TriggerIslandEvent(new DynamicIslandEvent
                {
                    Type = IslandEventType.SystemAlert,
                    Title = "Wallpaper Removed",
                    Subtitle = item.Title,
                    Glyph = "🗑️",
                    DisplayDurationMs = 2000
                });
            }
        }
    }

    private void OnWallpaperSettingChanged(object sender, RoutedEventArgs e)
    {
        if (_isInitializing || ChkPauseWallpaperFullscreen == null || ChkPauseWallpaperBattery == null) return;
        SettingsManager.Current.WallpaperPauseOnFullscreen = ChkPauseWallpaperFullscreen.IsChecked ?? true;
        SettingsManager.Current.WallpaperPauseOnBattery = ChkPauseWallpaperBattery.IsChecked ?? true;
        SettingsManager.SaveSettings();
        _wallpaperManager?.CheckPowerState();
        OnWallpaperSettingChangedCallback?.Invoke("Pause", true);
    }

    private void OnWallpaperFitSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        if (CmbWallpaperFit?.SelectedItem is ComboBoxItem selectedItem)
        {
            string fitMode = selectedItem.Content?.ToString() ?? "Fill";
            SettingsManager.Current.WallpaperFitMode = fitMode;
            SettingsManager.SaveSettings();
            _wallpaperManager?.SetFitMode(fitMode);
            AppLogger.Log($"[UI] Wallpaper fit mode changed to: {fitMode}");
        }
    }

    private void OnWallpaperMuteChanged(object sender, RoutedEventArgs e)
    {
        if (_isInitializing || ChkMuteWallpaperAudio == null) return;
        bool isMuted = ChkMuteWallpaperAudio.IsChecked ?? true;
        SettingsManager.Current.WallpaperMuted = isMuted;
        _wallpaperManager?.SetMuted(isMuted);
        SettingsManager.SaveSettings();
        OnWallpaperSettingChangedCallback?.Invoke("MuteAudio", isMuted);
    }

    private void SetClockWidgetLocked(bool locked)
    {
        _clockWidget?.SetLockState(locked, clickThrough: locked);
    }

    private void SetResourceWidgetLocked(bool locked)
    {
        _cpuWidget?.SetLockState(locked, clickThrough: false);
        _gpuWidget?.SetLockState(locked, clickThrough: false);
        _ramWidget?.SetLockState(locked, clickThrough: false);
    }

    private void OnWidgetConfigChanged(WidgetsConfig config)
    {
        _clockWidget?.ApplyConfig(config);
        ApplyDynamicIslandFont(config);
    }

    private void ApplyDynamicIslandFont(WidgetsConfig config)
    {
        try
        {
            if (_islandModule != null && config != null && !string.IsNullOrWhiteSpace(config.DynamicIslandFontFamily))
            {
                _islandModule.SetFontFamily(CustomFontService.ResolveFontFamily(config.DynamicIslandFontFamily));
            }
        }
        catch (Exception ex)
        {
            AppLogger.Log($"ApplyDynamicIslandFont error: {ex.Message}");
        }
    }
}
