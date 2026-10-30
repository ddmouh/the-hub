using System;
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
using UserControl = System.Windows.Controls.UserControl;
using TheHub.Common.Config;

namespace TheHub.App.Pages;

public partial class OptionsPage : UserControl
{
    private OverviewViewModel? _overviewViewModel;
    public Action<string, bool>? OnWallpaperOptionChanged;
    public Action<string>? OnDashboardFontChangedCallback;

    public OptionsPage()
    {
        InitializeComponent();
    }

    public void Initialize(OverviewViewModel? overviewViewModel)
    {
        _overviewViewModel = overviewViewModel;
        LoadSettings();
    }

    public void LoadSettings()
    {
        var settings = SettingsManager.Current;
        if (ChkOptPauseFullscreen != null) ChkOptPauseFullscreen.IsChecked = settings.WallpaperPauseOnFullscreen;
        if (ChkOptPauseBattery != null) ChkOptPauseBattery.IsChecked = settings.WallpaperPauseOnBattery;
        if (ChkOptMuteAudio != null) ChkOptMuteAudio.IsChecked = settings.WallpaperMuted;
        RefreshFontsList();
    }

    public void RefreshFontsList()
    {
        if (_overviewViewModel != null && FontsListControl != null)
        {
            FontsListControl.ItemsSource = null;
            FontsListControl.ItemsSource = _overviewViewModel.InstalledCustomFonts;
            if (FontsEmptyPrompt != null)
            {
                FontsEmptyPrompt.Visibility = (_overviewViewModel.InstalledCustomFonts.Count == 0)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
        }
    }

    private void OnDashboardFontChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox cmb) return;
        if (cmb.SelectedItem is not ComboBoxItem item) return;
        string fontName = item.Tag?.ToString() ?? "Segoe UI";
        OnDashboardFontChangedCallback?.Invoke(fontName);
    }

    private void OnInstallFontClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var ofd = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select Font to Install (.ttf / .otf)",
                Filter = "Font Files (*.ttf;*.otf)|*.ttf;*.otf|All Files (*.*)|*.*",
                Multiselect = false
            };

            if (ofd.ShowDialog() == true)
            {
                var installed = _overviewViewModel?.InstallCustomFont(ofd.FileName);
                RefreshFontsList();
            }
        }
        catch (Exception ex)
        {
            AppLogger.Log($"OnInstallFontClick error: {ex.Message}");
        }
    }

    private void OnDeleteFontClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement btn && btn.Tag is string fileName)
        {
            var confirm = MessageBox.Show(
                $"Are you sure you want to delete the installed font '{fileName}'?",
                "Delete Font",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm == MessageBoxResult.Yes)
            {
                _overviewViewModel?.DeleteCustomFont(fileName);
                SettingsManager.Current.CustomFonts.RemoveAll(f => string.Equals(f, fileName, StringComparison.OrdinalIgnoreCase));
                SettingsManager.SaveSettings();
                RefreshFontsList();
            }
        }
    }

    private void OnOptionsWallpaperSettingChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox chk) return;
        bool isChecked = chk.IsChecked ?? false;
        string tag = chk.Tag?.ToString() ?? string.Empty;
        switch (tag)
        {
            case "PauseFullscreen":
                SettingsManager.Current.WallpaperPauseOnFullscreen = isChecked;
                break;
            case "PauseBattery":
                SettingsManager.Current.WallpaperPauseOnBattery = isChecked;
                break;
            case "MuteAudio":
                SettingsManager.Current.WallpaperMuted = isChecked;
                break;
        }
        SettingsManager.SaveSettings();
        OnWallpaperOptionChanged?.Invoke(tag, isChecked);
    }
}
