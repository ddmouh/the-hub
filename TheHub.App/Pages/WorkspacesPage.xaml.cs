using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Button = System.Windows.Controls.Button;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using MessageBoxResult = System.Windows.MessageBoxResult;
using UserControl = System.Windows.Controls.UserControl;
using TheHub.App.Services;
using TheHub.Common.Config;
using TheHub.Common.Models;
using TheHub.Core;
using TheHub.Core.Modules;
using TheHub.Core.Services;

namespace TheHub.App.Pages;

public partial class WorkspacesPage : UserControl
{
    private WorkspaceModule? _workspaceModule;
    private CoreHubManager? _core;
    private WorkspaceConfig? _selectedWorkspace;
    private AppShortcut? _editingApp;

    public Action<string, Action<string>>? RequestIconPicker;
    public Action<Action<InstalledApp>>? RequestInstalledAppPicker;
    public Action<System.Collections.Generic.List<WorkspaceConfig>>? OnWorkspacesChanged;

    public WorkspacesPage()
    {
        InitializeComponent();
    }

    public void Initialize(WorkspaceModule workspaceModule, CoreHubManager core)
    {
        _workspaceModule = workspaceModule;
        _core = core;
        RefreshWorkspacesList();
    }

    public void RefreshWorkspacesList()
    {
        if (WorkspacesListControl == null) return;
        WorkspacesListControl.ItemsSource = null;
        WorkspacesListControl.ItemsSource = SettingsManager.Current.Workspaces;
    }

    private async void OnLaunchWorkspaceClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is WorkspaceConfig ws && _workspaceModule != null)
        {
            bool ok = await _workspaceModule.SwitchToWorkspaceAsync(ws);
            RefreshWorkspacesList();

            _core?.TriggerIslandEvent(new DynamicIslandEvent
            {
                Type = IslandEventType.WorkspaceChanged,
                Title = $"Workspace: {ws.Name}",
                Subtitle = ok ? $"{ws.Apps.Count} apps launched" : "Workspace applied",
                Glyph = ws.IconGlyph,
                DisplayDurationMs = 3000
            });
        }
    }

    private void OnCaptureDesktopWorkspaceClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var snapshots = WorkspaceEngine.CaptureDesktopSnapshot();
            string wsName = $"Desktop {DateTime.Now:HH:mm}";
            var newWs = WorkspaceEngine.CreateWorkspaceFromSnapshot(wsName, "🖥️", "#10B981", snapshots);

            SettingsManager.Current.Workspaces.Add(newWs);
            SettingsManager.SaveSettings();

            RefreshWorkspacesList();
            OnWorkspacesChanged?.Invoke(SettingsManager.Current.Workspaces);

            _core?.TriggerIslandEvent(new DynamicIslandEvent
            {
                Type = IslandEventType.SystemAlert,
                Title = "Desktop Captured",
                Subtitle = $"Created '{wsName}' with {snapshots.Count} apps",
                Glyph = "📸",
                DisplayDurationMs = 3000
            });
        }
        catch (Exception ex)
        {
            AppLogger.Log($"OnCaptureDesktopWorkspaceClick error: {ex.Message}");
        }
    }

    private void OnOpenAddWorkspaceClick(object sender, RoutedEventArgs e)
    {
        NewWorkspaceCard.Visibility = Visibility.Visible;
        NewWsNameBox.Text = "";
        NewWsIconBtn.Content = "📁";
        NewWsColorBox.Text = "#3B82F6";
        NewWsDescBox.Text = "";
    }

    private void OnCancelNewWorkspaceClick(object sender, RoutedEventArgs e)
    {
        NewWorkspaceCard.Visibility = Visibility.Collapsed;
    }

    private void OnSaveNewWorkspaceClick(object sender, RoutedEventArgs e)
    {
        string name = NewWsNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show("Please enter a workspace category name.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var newWs = new WorkspaceConfig
        {
            Name = name,
            IconGlyph = NewWsIconBtn.Content as string ?? "📁",
            AccentColor = string.IsNullOrWhiteSpace(NewWsColorBox.Text) ? "#3B82F6" : NewWsColorBox.Text.Trim(),
            Description = NewWsDescBox.Text.Trim()
        };

        SettingsManager.Current.Workspaces.Add(newWs);
        SettingsManager.SaveSettings();

        NewWorkspaceCard.Visibility = Visibility.Collapsed;
        RefreshWorkspacesList();
        OnWorkspacesChanged?.Invoke(SettingsManager.Current.Workspaces);

        _core?.TriggerIslandEvent(new DynamicIslandEvent
        {
            Type = IslandEventType.SystemAlert,
            Title = "Category Created",
            Subtitle = $"Added '{name}' category",
            Glyph = newWs.IconGlyph,
            DisplayDurationMs = 2500
        });
    }

    private void OnManageWorkspaceClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is WorkspaceConfig ws)
        {
            OpenWorkspaceDetail(ws);
        }
    }

    public void OpenWorkspaceDetail(WorkspaceConfig ws)
    {
        _selectedWorkspace = ws;
        _editingApp = null;

        DetailWsTitleBlock.Text = ws.Name;
        DetailWsIconBlock.Text = ws.IconGlyph;
        DetailWsColorBadge.Text = string.IsNullOrWhiteSpace(ws.AccentColor) ? "Category" : ws.AccentColor;
        DetailWsSubtitleBlock.Text = $"{ws.Apps.Count} configured application{(ws.Apps.Count == 1 ? "" : "s")}";

        try
        {
            var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(ws.AccentColor);
            DetailWsBadgeBorder.Background = new System.Windows.Media.SolidColorBrush(color);
        }
        catch
        {
            DetailWsBadgeBorder.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(59, 130, 246));
        }

        DetailWsNameBox.Text = ws.Name;
        DetailWsIconBtn.Content = ws.IconGlyph;
        DetailWsColorBox.Text = ws.AccentColor;
        DetailWsDescBox.Text = ws.Description;
        DetailWsPauseWallpapers.IsChecked = ws.PauseWallpapers;
        DetailWsDoNotDisturb.IsChecked = ws.EnableDoNotDisturb;

        WorkspaceAppsItemsControl.ItemsSource = null;
        WorkspaceAppsItemsControl.ItemsSource = ws.Apps;

        AppEditorCard.Visibility = Visibility.Collapsed;
        WorkspaceDetailPanel.Visibility = Visibility.Visible;
    }

    private void OnCloseWorkspaceDetailClick(object sender, RoutedEventArgs e)
    {
        WorkspaceDetailPanel.Visibility = Visibility.Collapsed;
        AppEditorCard.Visibility = Visibility.Collapsed;
        _selectedWorkspace = null;
        _editingApp = null;
    }

    private void OnUpdateWorkspaceSettingsClick(object sender, RoutedEventArgs e)
    {
        if (_selectedWorkspace == null) return;

        string name = DetailWsNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show("Category name cannot be empty.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _selectedWorkspace.Name = name;
        _selectedWorkspace.IconGlyph = DetailWsIconBtn.Content as string ?? "📁";
        _selectedWorkspace.AccentColor = string.IsNullOrWhiteSpace(DetailWsColorBox.Text) ? "#3B82F6" : DetailWsColorBox.Text.Trim();
        _selectedWorkspace.Description = DetailWsDescBox.Text.Trim();
        _selectedWorkspace.PauseWallpapers = DetailWsPauseWallpapers.IsChecked ?? false;
        _selectedWorkspace.EnableDoNotDisturb = DetailWsDoNotDisturb.IsChecked ?? false;

        SettingsManager.SaveSettings();

        DetailWsTitleBlock.Text = _selectedWorkspace.Name;
        DetailWsIconBlock.Text = _selectedWorkspace.IconGlyph;
        DetailWsColorBadge.Text = _selectedWorkspace.AccentColor;

        try
        {
            var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(_selectedWorkspace.AccentColor);
            DetailWsBadgeBorder.Background = new System.Windows.Media.SolidColorBrush(color);
        }
        catch { }

        RefreshWorkspacesList();
        OnWorkspacesChanged?.Invoke(SettingsManager.Current.Workspaces);

        _core?.TriggerIslandEvent(new DynamicIslandEvent
        {
            Type = IslandEventType.SystemAlert,
            Title = "Settings Updated",
            Subtitle = $"Updated '{_selectedWorkspace.Name}' profile",
            Glyph = "✅",
            DisplayDurationMs = 2000
        });
    }

    private void OnDeleteWorkspaceClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is WorkspaceConfig ws)
        {
            var confirm = MessageBox.Show(
                $"Are you sure you want to delete the '{ws.Name}' workspace category?\nThis will remove all associated shortcut configurations.",
                "Delete Workspace",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm == MessageBoxResult.Yes)
            {
                SettingsManager.Current.Workspaces.Remove(ws);
                SettingsManager.SaveSettings();

                if (_selectedWorkspace == ws)
                {
                    WorkspaceDetailPanel.Visibility = Visibility.Collapsed;
                    _selectedWorkspace = null;
                }

                RefreshWorkspacesList();
                OnWorkspacesChanged?.Invoke(SettingsManager.Current.Workspaces);

                _core?.TriggerIslandEvent(new DynamicIslandEvent
                {
                    Type = IslandEventType.SystemAlert,
                    Title = "Category Deleted",
                    Subtitle = $"Removed '{ws.Name}'",
                    Glyph = "🗑️",
                    DisplayDurationMs = 2000
                });
            }
        }
    }

    private void OnOpenAddAppClick(object sender, RoutedEventArgs e)
    {
        if (_selectedWorkspace == null) return;

        _editingApp = null;
        AppEditorTitleBlock.Text = $"Add Application to '{_selectedWorkspace.Name}'";
        AppTitleBox.Text = "";
        AppIconBtn.Content = "⚡";
        AppPathBox.Text = "";
        AppArgsBox.Text = "";

        AppEditorCard.Visibility = Visibility.Visible;
        AppTitleBox.Focus();
    }

    private void OnEditSingleAppClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is AppShortcut app && _selectedWorkspace != null)
        {
            _editingApp = app;
            AppEditorTitleBlock.Text = $"Edit Application: {app.Title}";
            AppTitleBox.Text = app.Title;
            AppIconBtn.Content = app.IconGlyph;
            AppPathBox.Text = app.ExecutablePath;
            AppArgsBox.Text = app.Arguments;

            AppEditorCard.Visibility = Visibility.Visible;
            AppPathBox.Focus();
        }
    }

    private void OnBrowseExecutableClick(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select Application Executable or Shortcut",
            Filter = "Executables & Shortcuts (*.exe;*.lnk;*.bat;*.cmd)|*.exe;*.lnk;*.bat;*.cmd|All Files (*.*)|*.*",
            CheckFileExists = true
        };

        if (dlg.ShowDialog() == true)
        {
            AppPathBox.Text = dlg.FileName;
            if (string.IsNullOrWhiteSpace(AppTitleBox.Text))
            {
                string inferredName = Path.GetFileNameWithoutExtension(dlg.FileName);
                AppTitleBox.Text = inferredName;
            }
        }
    }

    private void OnSaveAppClick(object sender, RoutedEventArgs e)
    {
        if (_selectedWorkspace == null) return;

        string title = AppTitleBox.Text.Trim();
        string path = AppPathBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(title))
        {
            MessageBox.Show("Please enter an application title.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            MessageBox.Show("Please specify an executable path or command name.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string glyph = AppIconBtn.Content as string ?? "⚡";
        string args = AppArgsBox.Text.Trim();

        if (_editingApp != null)
        {
            _editingApp.Title = title;
            _editingApp.ExecutablePath = path;
            _editingApp.IconGlyph = glyph;
            _editingApp.Arguments = args;
        }
        else
        {
            var newApp = new AppShortcut
            {
                Title = title,
                ExecutablePath = path,
                IconGlyph = glyph,
                Arguments = args,
                Category = _selectedWorkspace.Name,
                Order = _selectedWorkspace.Apps.Count
            };
            _selectedWorkspace.Apps.Add(newApp);
        }

        SettingsManager.SaveSettings();

        WorkspaceAppsItemsControl.ItemsSource = null;
        WorkspaceAppsItemsControl.ItemsSource = _selectedWorkspace.Apps;
        DetailWsSubtitleBlock.Text = $"{_selectedWorkspace.Apps.Count} configured application{(_selectedWorkspace.Apps.Count == 1 ? "" : "s")}";
        RefreshWorkspacesList();

        AppEditorCard.Visibility = Visibility.Collapsed;
        _editingApp = null;

        _core?.TriggerIslandEvent(new DynamicIslandEvent
        {
            Type = IslandEventType.SystemAlert,
            Title = "App Saved",
            Subtitle = $"Updated '{title}' in {_selectedWorkspace.Name}",
            Glyph = glyph,
            DisplayDurationMs = 2000
        });
    }

    private void OnCancelAppEditorClick(object sender, RoutedEventArgs e)
    {
        AppEditorCard.Visibility = Visibility.Collapsed;
        _editingApp = null;
    }

    private void OnDeleteSingleAppClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is AppShortcut app && _selectedWorkspace != null)
        {
            _selectedWorkspace.Apps.Remove(app);
            SettingsManager.SaveSettings();

            WorkspaceAppsItemsControl.ItemsSource = null;
            WorkspaceAppsItemsControl.ItemsSource = _selectedWorkspace.Apps;
            DetailWsSubtitleBlock.Text = $"{_selectedWorkspace.Apps.Count} configured application{(_selectedWorkspace.Apps.Count == 1 ? "" : "s")}";
            RefreshWorkspacesList();

            if (_editingApp == app)
            {
                AppEditorCard.Visibility = Visibility.Collapsed;
                _editingApp = null;
            }
        }
    }

    private async void OnTestSingleAppClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is AppShortcut app)
        {
            var (success, msg) = await AppLauncherService.LaunchShortcutWithDetailsAsync(app);

            _core?.TriggerIslandEvent(new DynamicIslandEvent
            {
                Type = IslandEventType.SystemAlert,
                Title = success ? $"Launched {app.Title}" : "Launch Error",
                Subtitle = msg,
                Glyph = success ? app.IconGlyph : "⚠️",
                DisplayDurationMs = 3000
            });
        }
    }

    private void OnOpenIconPickerForNewWsClick(object sender, RoutedEventArgs e)
    {
        RequestIconPicker?.Invoke(NewWsIconBtn.Content as string ?? "📁", glyph => NewWsIconBtn.Content = glyph);
    }

    private void OnOpenIconPickerForDetailWsClick(object sender, RoutedEventArgs e)
    {
        RequestIconPicker?.Invoke(DetailWsIconBtn.Content as string ?? "🎮", glyph => DetailWsIconBtn.Content = glyph);
    }

    private void OnOpenIconPickerForAppClick(object sender, RoutedEventArgs e)
    {
        RequestIconPicker?.Invoke(AppIconBtn.Content as string ?? "⚡", glyph => AppIconBtn.Content = glyph);
    }

    private void OnOpenInstalledAppsClick(object sender, RoutedEventArgs e)
    {
        RequestInstalledAppPicker?.Invoke(app =>
        {
            AppTitleBox.Text = app.Name;
            AppPathBox.Text = app.ExecutablePath;
            if (!string.IsNullOrEmpty(app.IconGlyph))
            {
                AppIconBtn.Content = app.IconGlyph;
            }
        });
    }
}
