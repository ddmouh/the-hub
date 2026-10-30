using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using ComboBoxItem = System.Windows.Controls.ComboBoxItem;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using MessageBoxResult = System.Windows.MessageBoxResult;
using RadioButton = System.Windows.Controls.RadioButton;
using TextBox = System.Windows.Controls.TextBox;
using UserControl = System.Windows.Controls.UserControl;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using TheHub.App.Services;
using TheHub.Core.Services;
using TheHub.Common.Config;
using TheHub.Common.Models;
using TheHub.Core;
using TheHub.Overlays.RadialLauncher;

namespace TheHub.App.Pages;

public partial class RadialPage : UserControl
{
    private RadialWindow? _radialWindow;
    private CoreHubManager? _core;
    private RadialWheelProfile? _selectedRadialProfile;
    private List<InstalledApp> _allInstalledApps = new();
    private string _addSourceMode = "Installed";
    private bool _isInitializing = true;

    public RadialPage()
    {
        InitializeComponent();
    }

    public void Initialize(RadialWindow radialWindow, CoreHubManager core)
    {
        _radialWindow = radialWindow;
        _core = core;
        LoadSettings();
    }

    public void LoadSettings()
    {
        _isInitializing = true;
        try
        {
            var settings = SettingsManager.Current;

            TxtGlobalShortcut.Text = settings.GlobalShortcut;
            ChkMouseTrigger.IsChecked = settings.EnableMouseTrigger;

            if (settings.MouseTriggerButton == "Back") RbTriggerBack.IsChecked = true;
            else if (settings.MouseTriggerButton == "Forward") RbTriggerForward.IsChecked = true;
            else RbTriggerWheel.IsChecked = true;

            if (settings.GestureBehavior == "Click") RbGestureClick.IsChecked = true;
            else RbGestureHold.IsChecked = true;

            SldActivationZone.Value = settings.ActivationZone;
            TxtActivationZoneValue.Text = $"{settings.ActivationZone} px";

            if (settings.AppearanceTheme == "White") RbThemeWhite.IsChecked = true;
            else RbThemeBlack.IsChecked = true;

            SldOrbitalRadius.Value = settings.RadialRadius;
            TxtOrbitalRadiusValue.Text = $"{settings.RadialRadius} px";

            SldTileSize.Value = settings.IconSize;
            TxtTileSizeValue.Text = $"{settings.IconSize} px";

            ChkPersistentLabels.IsChecked = settings.PersistentLabels;

            SldWheelOpacity.Value = settings.WheelOpacity;
            TxtWheelOpacityValue.Text = $"{settings.WheelOpacity}%";

            SldBackgroundDimming.Value = settings.BackgroundDimming;
            TxtBackgroundDimmingValue.Text = $"{settings.BackgroundDimming}%";

            ChkPrecisionMode.IsChecked = settings.PerformanceMode;
            ChkFullscreenProtection.IsChecked = settings.FullscreenProtection;

            RefreshWorkspacesCards();
        }
        finally
        {
            _isInitializing = false;
        }
    }

    public void RefreshWorkspacesCards()
    {
        var settings = SettingsManager.Current;

        if (settings.RadialProfiles.Count == 0)
        {
            settings.RadialProfiles = AppSettings.CreateDefault().RadialProfiles;
        }

        for (int i = 0; i < settings.RadialProfiles.Count; i++)
        {
            settings.RadialProfiles[i].HotkeyIndex = i + 1;
        }

        if (!settings.RadialProfiles.Any(w => w.IsActive))
        {
            settings.RadialProfiles[0].IsActive = true;
        }

        if (WorkspacesCardsControl != null)
        {
            WorkspacesCardsControl.ItemsSource = null;
            WorkspacesCardsControl.ItemsSource = settings.RadialProfiles;
        }

        _selectedRadialProfile = settings.RadialProfiles.FirstOrDefault(w => w.IsActive) ?? settings.RadialProfiles[0];
        BindRadialProfileEditor(_selectedRadialProfile);
    }

    private void BindRadialProfileEditor(RadialWheelProfile? profile)
    {
        if (profile == null || WorkspaceEditorSection == null) return;

        _selectedRadialProfile = profile;
        WsEditorNameBox.Text = profile.Name;
        WsEditorIconText.Text = string.IsNullOrWhiteSpace(profile.IconGlyph) ? "🎯" : profile.IconGlyph;
        WsEditorSubtitle.Text = $"Configure wheel shortcuts for '{profile.Name}' (Active: {(profile.IsActive ? "Yes" : "No")})";
        WsEditorVisibilityBtn.Content = profile.IsVisibleInSwitcher ? "👁️ In Switcher" : "🙈 Hidden";

        try
        {
            var swatchColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter
                                  .ConvertFromString(profile.AccentColor ?? "#3B82F6");
            WsEditorColorSwatch.Background = new System.Windows.Media.SolidColorBrush(swatchColor);
        }
        catch
        {
            WsEditorColorSwatch.Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x3B, 0x82, 0xF6));
        }

        WsEditorCurrentAppsList.ItemsSource = null;
        WsEditorCurrentAppsList.ItemsSource = profile.Slices;
        WsEditorSliceCountText.Text = $"{profile.Slices.Count} items";
        WsEditorEmptyPrompt.Visibility = (profile.Slices.Count == 0) ? Visibility.Visible : Visibility.Collapsed;

        LoadInstalledAppsSuggestions();
    }

        private void OnRadialSubTabsSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SubPanelActivation == null || SubPanelAppearance == null || SubPanelWorkspaces == null) return;

        int tabIndex = RadialSubTabs?.SelectedIndex ?? 0;
        SubPanelActivation.Visibility = (tabIndex == 0) ? Visibility.Visible : Visibility.Collapsed;
        SubPanelAppearance.Visibility = (tabIndex == 1) ? Visibility.Visible : Visibility.Collapsed;
        SubPanelWorkspaces.Visibility = (tabIndex == 2) ? Visibility.Visible : Visibility.Collapsed;

        if (tabIndex == 2)
        {
            RefreshWorkspacesCards();
        }
    }

    private void OnRadialSubTabChanged(object sender, RoutedEventArgs e)
    {
        if (SubPanelActivation == null || SubPanelAppearance == null || SubPanelWorkspaces == null) return;

        if (sender is RadioButton rb && rb.Tag is string tagStr && int.TryParse(tagStr, out int tabIndex))
        {
            SubPanelActivation.Visibility = (tabIndex == 0) ? Visibility.Visible : Visibility.Collapsed;
            SubPanelAppearance.Visibility = (tabIndex == 1) ? Visibility.Visible : Visibility.Collapsed;
            SubPanelWorkspaces.Visibility = (tabIndex == 2) ? Visibility.Visible : Visibility.Collapsed;

            if (tabIndex == 2)
            {
                RefreshWorkspacesCards();
            }
        }
    }

    private void ApplyRadialSettingsLive()
    {
        if (_isInitializing) return;
        try
        {
            var settings = SettingsManager.Current;
            SettingsManager.SaveSettings(settings);
            _radialWindow?.UpdateAppearance(settings);
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Error applying radial settings live: {ex.Message}");
        }
    }

    private void OnShortcutTextBoxPreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.LeftCtrl || key == Key.RightCtrl ||
            key == Key.LeftAlt || key == Key.RightAlt ||
            key == Key.LeftShift || key == Key.RightShift)
        {
            return;
        }

        var parts = new List<string>();
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) parts.Add("Ctrl");
        if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0) parts.Add("Alt");
        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) parts.Add("Shift");

        parts.Add(key.ToString());
        string combo = string.Join("+", parts);
        TxtGlobalShortcut.Text = combo;
        SettingsManager.Current.GlobalShortcut = combo;
        ApplyRadialSettingsLive();
    }

    private void OnRecordShortcutClick(object sender, RoutedEventArgs e)
    {
        TxtGlobalShortcut.Text = "Press key...";
        TxtGlobalShortcut.Focus();
        TxtGlobalShortcut.SelectAll();
    }

    private void OnResetShortcutClick(object sender, RoutedEventArgs e)
    {
        TxtGlobalShortcut.Text = "Alt+Z";
        SettingsManager.Current.GlobalShortcut = "Alt+Z";
        ApplyRadialSettingsLive();
    }

    private void OnMouseTriggerToggled(object sender, RoutedEventArgs e)
    {
        if (ChkMouseTrigger == null) return;
        SettingsManager.Current.EnableMouseTrigger = ChkMouseTrigger.IsChecked ?? true;
        ApplyRadialSettingsLive();
    }

    private void OnTriggerButtonChanged(object sender, RoutedEventArgs e)
    {
        if (RbTriggerBack == null || RbTriggerForward == null) return;
        if (RbTriggerBack.IsChecked == true) SettingsManager.Current.MouseTriggerButton = "Back";
        else if (RbTriggerForward.IsChecked == true) SettingsManager.Current.MouseTriggerButton = "Forward";
        else SettingsManager.Current.MouseTriggerButton = "Middle";
        ApplyRadialSettingsLive();
    }

    private void OnGestureBehaviorChanged(object sender, RoutedEventArgs e)
    {
        if (RbGestureClick == null) return;
        SettingsManager.Current.GestureBehavior = (RbGestureClick.IsChecked == true) ? "Click" : "Hold";
        ApplyRadialSettingsLive();
    }

    private void OnActivationZoneValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtActivationZoneValue == null) return;
        int val = (int)Math.Round(e.NewValue);
        TxtActivationZoneValue.Text = $"{val} px";
        SettingsManager.Current.ActivationZone = val;
        ApplyRadialSettingsLive();
    }

    private void OnAppearanceThemeChanged(object sender, RoutedEventArgs e)
    {
        if (RbThemeWhite == null) return;
        SettingsManager.Current.AppearanceTheme = (RbThemeWhite.IsChecked == true) ? "White" : "Black";
        ApplyRadialSettingsLive();
    }

    private void OnOrbitalRadiusValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtOrbitalRadiusValue == null) return;
        int val = (int)Math.Round(e.NewValue);
        TxtOrbitalRadiusValue.Text = $"{val} px";
        SettingsManager.Current.RadialRadius = val;
        ApplyRadialSettingsLive();
    }

    private void OnTileSizeValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtTileSizeValue == null) return;
        int val = (int)Math.Round(e.NewValue);
        TxtTileSizeValue.Text = $"{val} px";
        SettingsManager.Current.IconSize = val;
        ApplyRadialSettingsLive();
    }

    private void OnPersistentLabelsToggled(object sender, RoutedEventArgs e)
    {
        if (ChkPersistentLabels == null) return;
        SettingsManager.Current.PersistentLabels = ChkPersistentLabels.IsChecked ?? false;
        ApplyRadialSettingsLive();
    }

    private void OnWheelOpacityValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtWheelOpacityValue == null) return;
        int val = (int)Math.Round(e.NewValue);
        TxtWheelOpacityValue.Text = $"{val}%";
        SettingsManager.Current.WheelOpacity = val;
        ApplyRadialSettingsLive();
    }

    private void OnBackgroundDimmingValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtBackgroundDimmingValue == null) return;
        int val = (int)Math.Round(e.NewValue);
        TxtBackgroundDimmingValue.Text = $"{val}%";
        SettingsManager.Current.BackgroundDimming = val;
        ApplyRadialSettingsLive();
    }

    private void OnPrecisionModeToggled(object sender, RoutedEventArgs e)
    {
        if (ChkPrecisionMode == null) return;
        SettingsManager.Current.PerformanceMode = ChkPrecisionMode.IsChecked ?? false;
    }

    private void OnFullscreenProtectionToggled(object sender, RoutedEventArgs e)
    {
        if (ChkFullscreenProtection == null) return;
        SettingsManager.Current.FullscreenProtection = ChkFullscreenProtection.IsChecked ?? true;
    }

    private void OnSelectWorkspaceCardClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement elem && elem.Tag is RadialWheelProfile clickedProfile)
        {
            var settings = SettingsManager.Current;
            foreach (var p in settings.RadialProfiles)
            {
                p.IsActive = (p == clickedProfile);
                p.NotifySlicesChanged();
            }
            SettingsManager.SaveSettings();

            _radialWindow?.RefreshShortcuts();

            WorkspacesCardsControl.ItemsSource = null;
            WorkspacesCardsControl.ItemsSource = settings.RadialProfiles;

            BindRadialProfileEditor(clickedProfile);
        }
    }

    private void OnCreateNewWorkspaceCardClick(object sender, MouseButtonEventArgs e)
    {
        var settings = SettingsManager.Current;
        int newIdx = settings.RadialProfiles.Count + 1;
        var newProfile = new RadialWheelProfile
        {
            Name = $"Wheel Profile {newIdx}",
            IconGlyph = "🎯",
            AccentColor = "#3B82F6",
            HotkeyIndex = newIdx,
            IsVisibleInSwitcher = true,
            IsActive = true,
            Slices = new()
        };

        foreach (var p in settings.RadialProfiles)
        {
            p.IsActive = false;
            p.NotifySlicesChanged();
        }
        settings.RadialProfiles.Add(newProfile);
        SettingsManager.SaveSettings();

        _radialWindow?.RefreshShortcuts();

        WorkspacesCardsControl.ItemsSource = null;
        WorkspacesCardsControl.ItemsSource = settings.RadialProfiles;

        BindRadialProfileEditor(newProfile);
    }

    private void OnWsEditorNameChanged(object sender, TextChangedEventArgs e)
    {
        if (_selectedRadialProfile == null || WsEditorNameBox == null) return;
        string newName = WsEditorNameBox.Text.Trim();
        if (!string.IsNullOrEmpty(newName) && _selectedRadialProfile.Name != newName)
        {
            _selectedRadialProfile.Name = newName;
            SettingsManager.SaveSettings();
            _selectedRadialProfile.NotifySlicesChanged();
        }
    }

    private void OnWsEditorOpenColorPickerClick(object sender, RoutedEventArgs e)
    {
        if (_selectedRadialProfile == null) return;
        try
        {
            var hexStr = _selectedRadialProfile.AccentColor ?? "#3B82F6";
            var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hexStr);
            WsEditorColorPicker.SelectedColor = color;
        }
        catch { }
        WsEditorColorPickerPopup.IsOpen = true;
    }

    private void OnWsEditorColorPickerChanged(System.Windows.Media.Color color)
    {
        if (_selectedRadialProfile == null) return;
        string hex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        _selectedRadialProfile.AccentColor = hex;

        WsEditorColorSwatch.Background = new System.Windows.Media.SolidColorBrush(color);

        SettingsManager.SaveSettings();
        _selectedRadialProfile.NotifySlicesChanged();
        _radialWindow?.RefreshShortcuts();

        WorkspacesCardsControl.ItemsSource = null;
        WorkspacesCardsControl.ItemsSource = SettingsManager.Current.RadialProfiles;
    }

    private void OnWsEditorToggleVisibilityClick(object sender, RoutedEventArgs e)
    {
        if (_selectedRadialProfile == null) return;
        _selectedRadialProfile.IsVisibleInSwitcher = !_selectedRadialProfile.IsVisibleInSwitcher;
        SettingsManager.SaveSettings();
        WsEditorVisibilityBtn.Content = _selectedRadialProfile.IsVisibleInSwitcher ? "👁️ In Switcher" : "🙈 Hidden";
    }

    private void OnWsEditorLaunchAllClick(object sender, RoutedEventArgs e)
    {
        if (_selectedRadialProfile == null) return;
        foreach (var app in _selectedRadialProfile.Slices)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(app.ExecutablePath))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = app.ExecutablePath,
                        UseShellExecute = true
                    });
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log($"Launch app failed ({app.Title}): {ex.Message}");
            }
        }
    }

    private void OnWsEditorDeleteClick(object sender, RoutedEventArgs e)
    {
        var settings = SettingsManager.Current;
        if (settings.RadialProfiles.Count <= 1)
        {
            MessageBox.Show("At least one wheel profile must be preserved.", "The Hub", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_selectedRadialProfile == null) return;

        var result = MessageBox.Show(
            $"Are you sure you want to delete '{_selectedRadialProfile.Name}'?",
            "Confirm Delete",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            settings.RadialProfiles.Remove(_selectedRadialProfile);
            _selectedRadialProfile = settings.RadialProfiles[0];
            _selectedRadialProfile.IsActive = true;
            SettingsManager.SaveSettings();
            _radialWindow?.RefreshShortcuts();
            RefreshWorkspacesCards();
        }
    }

    private void OnWsSliceMoveUpClick(object sender, RoutedEventArgs e)
    {
        if (_selectedRadialProfile == null) return;
        if (sender is Button btn && btn.Tag is AppShortcut shortcut)
        {
            int idx = _selectedRadialProfile.Slices.IndexOf(shortcut);
            if (idx > 0)
            {
                _selectedRadialProfile.Slices.RemoveAt(idx);
                _selectedRadialProfile.Slices.Insert(idx - 1, shortcut);
                for (int i = 0; i < _selectedRadialProfile.Slices.Count; i++) _selectedRadialProfile.Slices[i].Order = i;

                _selectedRadialProfile.NotifySlicesChanged();
                SettingsManager.SaveSettings();
                _radialWindow?.RefreshShortcuts();
                BindRadialProfileEditor(_selectedRadialProfile);
                WorkspacesCardsControl.ItemsSource = null;
                WorkspacesCardsControl.ItemsSource = SettingsManager.Current.RadialProfiles;
            }
        }
    }

    private void OnWsSliceMoveDownClick(object sender, RoutedEventArgs e)
    {
        if (_selectedRadialProfile == null) return;
        if (sender is Button btn && btn.Tag is AppShortcut shortcut)
        {
            int idx = _selectedRadialProfile.Slices.IndexOf(shortcut);
            if (idx >= 0 && idx < _selectedRadialProfile.Slices.Count - 1)
            {
                _selectedRadialProfile.Slices.RemoveAt(idx);
                _selectedRadialProfile.Slices.Insert(idx + 1, shortcut);
                for (int i = 0; i < _selectedRadialProfile.Slices.Count; i++) _selectedRadialProfile.Slices[i].Order = i;

                _selectedRadialProfile.NotifySlicesChanged();
                SettingsManager.SaveSettings();
                _radialWindow?.RefreshShortcuts();
                BindRadialProfileEditor(_selectedRadialProfile);
                WorkspacesCardsControl.ItemsSource = null;
                WorkspacesCardsControl.ItemsSource = SettingsManager.Current.RadialProfiles;
            }
        }
    }

    private void OnWsSliceRemoveClick(object sender, RoutedEventArgs e)
    {
        if (_selectedRadialProfile == null) return;
        if (sender is Button btn && btn.Tag is AppShortcut shortcut)
        {
            _selectedRadialProfile.Slices.Remove(shortcut);
            for (int i = 0; i < _selectedRadialProfile.Slices.Count; i++) _selectedRadialProfile.Slices[i].Order = i;

            _selectedRadialProfile.NotifySlicesChanged();
            SettingsManager.SaveSettings();
            _radialWindow?.RefreshShortcuts();
            BindRadialProfileEditor(_selectedRadialProfile);
            WorkspacesCardsControl.ItemsSource = null;
            WorkspacesCardsControl.ItemsSource = SettingsManager.Current.RadialProfiles;
        }
    }

    private void OnAddSourceTabChanged(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Tag is string tag)
        {
            _addSourceMode = tag;
            if (PanelAddInstalled != null) PanelAddInstalled.Visibility = (tag == "Installed") ? Visibility.Visible : Visibility.Collapsed;
            if (PanelAddFile != null) PanelAddFile.Visibility = (tag == "File") ? Visibility.Visible : Visibility.Collapsed;
            if (PanelAddUrl != null) PanelAddUrl.Visibility = (tag == "URL") ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private async void OnInstalledFilterChanged(object sender, TextChangedEventArgs e)
    {
        string query = TxtInstalledFilter.Text.Trim().ToLowerInvariant();
        if (_allInstalledApps.Count == 0)
        {
            _allInstalledApps = await InstalledAppsService.GetInstalledApplicationsAsync();
        }

        var filtered = string.IsNullOrEmpty(query)
            ? _allInstalledApps.Take(15).ToList()
            : _allInstalledApps.Where(a => a.Name.ToLowerInvariant().Contains(query)).Take(25).ToList();

        ListInstalledAppsSuggestions.ItemsSource = filtered;
    }

    private async void LoadInstalledAppsSuggestions()
    {
        try
        {
            if (_allInstalledApps.Count == 0)
            {
                _allInstalledApps = await InstalledAppsService.GetInstalledApplicationsAsync();
            }
            if (ListInstalledAppsSuggestions != null)
            {
                string query = TxtInstalledFilter?.Text?.Trim().ToLowerInvariant() ?? "";
                var filtered = string.IsNullOrEmpty(query)
                    ? _allInstalledApps.Take(15).ToList()
                    : _allInstalledApps.Where(a => a.Name.ToLowerInvariant().Contains(query)).Take(25).ToList();
                ListInstalledAppsSuggestions.ItemsSource = filtered;
            }
        }
        catch (Exception ex)
        {
            AppLogger.Log($"LoadInstalledAppsSuggestions error: {ex.Message}");
        }
    }

    private void OnAddInstalledAppToCurrentWsClick(object sender, RoutedEventArgs e)
    {
        if (_selectedRadialProfile == null) return;
        if (sender is Button btn && btn.Tag is InstalledApp app)
        {
            _selectedRadialProfile.Slices.Add(new AppShortcut
            {
                Title = app.Name,
                ExecutablePath = app.ExecutablePath,
                IconGlyph = "⚡",
                ShortcutType = "Application",
                Order = _selectedRadialProfile.Slices.Count
            });

            _selectedRadialProfile.NotifySlicesChanged();
            SettingsManager.SaveSettings();
            _radialWindow?.RefreshShortcuts();
            BindRadialProfileEditor(_selectedRadialProfile);
            WorkspacesCardsControl.ItemsSource = null;
            WorkspacesCardsControl.ItemsSource = SettingsManager.Current.RadialProfiles;
        }
    }

    private void OnBrowseExeForCurrentWsClick(object sender, RoutedEventArgs e)
    {
        if (_selectedRadialProfile == null) return;

        var ofd = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Executables (*.exe)|*.exe|All Files (*.*)|*.*",
            Title = "Select Executable Application"
        };

        if (ofd.ShowDialog() == true)
        {
            string fileName = Path.GetFileNameWithoutExtension(ofd.FileName);
            _selectedRadialProfile.Slices.Add(new AppShortcut
            {
                Title = fileName,
                ExecutablePath = ofd.FileName,
                IconGlyph = "⚡",
                ShortcutType = "Application",
                Order = _selectedRadialProfile.Slices.Count
            });

            _selectedRadialProfile.NotifySlicesChanged();
            SettingsManager.SaveSettings();
            _radialWindow?.RefreshShortcuts();
            BindRadialProfileEditor(_selectedRadialProfile);
            WorkspacesCardsControl.ItemsSource = null;
            WorkspacesCardsControl.ItemsSource = SettingsManager.Current.RadialProfiles;
        }
    }

    private void OnBrowseFolderForCurrentWsClick(object sender, RoutedEventArgs e)
    {
        if (_selectedRadialProfile == null) return;

        var fbd = new System.Windows.Forms.FolderBrowserDialog();
        if (fbd.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            string dirName = Path.GetFileName(fbd.SelectedPath);
            if (string.IsNullOrEmpty(dirName)) dirName = fbd.SelectedPath;

            _selectedRadialProfile.Slices.Add(new AppShortcut
            {
                Title = dirName,
                ExecutablePath = fbd.SelectedPath,
                IconGlyph = "📁",
                ShortcutType = "Folder",
                Order = _selectedRadialProfile.Slices.Count
            });

            _selectedRadialProfile.NotifySlicesChanged();
            SettingsManager.SaveSettings();
            _radialWindow?.RefreshShortcuts();
            BindRadialProfileEditor(_selectedRadialProfile);
            WorkspacesCardsControl.ItemsSource = null;
            WorkspacesCardsControl.ItemsSource = SettingsManager.Current.RadialProfiles;
        }
    }

    private void OnAddUrlForCurrentWsClick(object sender, RoutedEventArgs e)
    {
        if (_selectedRadialProfile == null) return;
        string url = TxtCustomUrl.Text.Trim();
        string title = TxtCustomUrlTitle.Text.Trim();
        if (string.IsNullOrEmpty(url)) url = "https://";
        if (string.IsNullOrEmpty(title)) title = "Web Shortcut";

        _selectedRadialProfile.Slices.Add(new AppShortcut
        {
            Title = title,
            ExecutablePath = url,
            IconGlyph = "🌐",
            ShortcutType = "URL",
            Order = _selectedRadialProfile.Slices.Count
        });

        _selectedRadialProfile.NotifySlicesChanged();
        SettingsManager.SaveSettings();
        _radialWindow?.RefreshShortcuts();
        BindRadialProfileEditor(_selectedRadialProfile);
        WorkspacesCardsControl.ItemsSource = null;
        WorkspacesCardsControl.ItemsSource = SettingsManager.Current.RadialProfiles;
    }
}
