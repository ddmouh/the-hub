using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

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

using TheHub.Common.Config;
using TheHub.Common.Models;
using TheHub.Core;
using TheHub.Core.AI;
using TheHub.Core.Bridge;
using TheHub.Core.Bus;
using TheHub.Core.Modules;
using TheHub.Core.Native;
using TheHub.Core.Services;
using TheHub.App.Services;
using TheHub.Overlays.DynamicIsland;
using TheHub.Overlays.RadialLauncher;
using TheHub.Overlays.Wallpaper;
using TheHub.Overlays.Widgets;

namespace TheHub.App;

/// <summary>
/// Lean Shell Host Window for The Hub Desktop Companion.
/// Decoupled into modular Page UserControls (TheHub.App.Pages).
/// </summary>
public partial class MainWindow : Window
{
    private readonly CoreHubManager _core;
    private readonly IslandModule _islandModule;
    private readonly WallpaperManager _wallpaperManager;
    private readonly WallpaperModule _wallpaperModule;
    private readonly WorkspaceModule _workspaceModule;
    private readonly WidgetsModule _widgetsModule;
    private readonly AIAssistantModule _aiAssistantModule;
    private readonly TrayIconService _trayService;
    private readonly RadialWindow _radialWindow;
    private readonly OverviewViewModel _overviewViewModel;
    private readonly DispatcherTimer _ramTimer;

    // Modal target callbacks
    private Action<string>? _onIconSelectedTarget;
    private Action<InstalledApp>? _onAppSelectedTarget;
    private List<InstalledApp> _allInstalledApps = new();
    private List<IconItem> _allCuratedIcons = new();
    private string _selectedIconCategory = "All";
    private string _iconSearchQuery = "";
    private string _appSearchQuery = "";
    private bool _isShuttingDown;

    public WallpaperManager WallpaperManager => _wallpaperManager;

    public MainWindow()
    {
        var sw = Stopwatch.StartNew();
        AppLogger.Log($"[PERF] MainWindow.ctor started: 0ms");
        InitializeComponent();
        AppLogger.Log($"[PERF] InitializeComponent finished: {sw.ElapsedMilliseconds}ms");

        _core = new CoreHubManager();
        AppLogger.Log($"[PERF] CoreHubManager created: {sw.ElapsedMilliseconds}ms");

        _islandModule = new IslandModule(HubBus.Default, UiBridge.Instance);
        AppLogger.Log($"[PERF] IslandModule created: {sw.ElapsedMilliseconds}ms");

        _wallpaperManager = new WallpaperManager();
        AppLogger.Log($"[PERF] WallpaperManager created: {sw.ElapsedMilliseconds}ms");

        _wallpaperModule = new WallpaperModule(HubBus.Default, UiBridge.Instance, _wallpaperManager);
        _workspaceModule = new WorkspaceModule(HubBus.Default, UiBridge.Instance);
        _widgetsModule = new WidgetsModule(HubBus.Default, UiBridge.Instance);
        _aiAssistantModule = new AIAssistantModule(HubBus.Default, UiBridge.Instance);
        AppLogger.Log($"[PERF] Modules created: {sw.ElapsedMilliseconds}ms");

        _trayService = new TrayIconService();
        _trayService.OpenMainWindowRequested += () => Dispatcher.Invoke(() =>
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
            Focus();
        });
        _trayService.ExitRequested += () => Dispatcher.Invoke(ForceCompleteShutdown);

        _radialWindow = new RadialWindow();
        _radialWindow.WorkspaceSwitchRequested += index =>
        {
            var ws = SettingsManager.Current.Workspaces;
            if (index >= 0 && index < ws.Count)
            {
                HubBus.Default.Publish(new WorkspaceChanged(ws[index].Id, "RadialWheel"));
            }
        };
        _radialWindow.Dismissed += () => _core.ResetRadialMouseState();

        _core.RadialLauncherRequested += (x, y) => Dispatcher.BeginInvoke(() =>
        {
            if (_radialWindow.IsVisible) _radialWindow.Dismiss();
            else _radialWindow.ShowAt(x, y);
        });
        _core.RadialPointerMoved += (x, y) => Dispatcher.BeginInvoke(() => _radialWindow.UpdatePointer(x, y));
        _core.RadialReleaseRequested += () => Dispatcher.BeginInvoke(() => _radialWindow.TriggerSelectionAndClose());

        _overviewViewModel = new OverviewViewModel();

        _ramTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        _ramTimer.Tick += (s, e) => UpdateMemoryMetrics();
        _ramTimer.Start();

        Loaded += OnLoaded;
        AppLogger.Log("MainWindow.ctor finished");
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var sw = Stopwatch.StartNew();
        AppLogger.Log($"[PERF] MainWindow.OnLoaded started: 0ms");
        try
        {
            // 1. Initialize Pages with injected singletons
            ViewOverview.Initialize(_wallpaperManager, _wallpaperModule, _widgetsModule, _core, _overviewViewModel, _islandModule);
            AppLogger.Log($"[PERF] ViewOverview.Initialize: {sw.ElapsedMilliseconds}ms");

            ViewWorkspaces.Initialize(_workspaceModule, _core);
            AppLogger.Log($"[PERF] ViewWorkspaces.Initialize: {sw.ElapsedMilliseconds}ms");

            ViewRadial.Initialize(_radialWindow, _core);
            AppLogger.Log($"[PERF] ViewRadial.Initialize: {sw.ElapsedMilliseconds}ms");

            ViewIsland.Initialize(_core, _islandModule, _overviewViewModel);
            AppLogger.Log($"[PERF] ViewIsland.Initialize: {sw.ElapsedMilliseconds}ms");

            ViewAi.Initialize(_aiAssistantModule);
            AppLogger.Log($"[PERF] ViewAi.Initialize: {sw.ElapsedMilliseconds}ms");

            ViewOptions.Initialize(_overviewViewModel);
            AppLogger.Log($"[PERF] ViewOptions.Initialize: {sw.ElapsedMilliseconds}ms");

            // 2. Wire Modal Requests from pages
            ViewWorkspaces.RequestIconPicker = (currentGlyph, callback) => OpenIconPickerModal(callback);
            ViewWorkspaces.RequestInstalledAppPicker = callback => OpenInstalledAppsModal(callback);

            // 3. Modals initialized on-demand (lazy)

            // 4. Start Modules Asynchronously in background to keep UI startup instant
            _ = Task.Run(async () =>
            {
                try
                {
                    AppLogger.Log("Starting Dynamic Island Module");
                    await _islandModule.StartAsync();

                    AppLogger.Log("Starting Wallpaper Module");
                    await _wallpaperModule.StartAsync();

                    AppLogger.Log("Starting Workspace Module");
                    await _workspaceModule.StartAsync();

                    AppLogger.Log("Starting Widgets Module");
                    await _widgetsModule.StartAsync();

                    AppLogger.Log("Starting Core Hub Manager");
                    await _core.StartAsync();

                    Dispatcher.Invoke(UpdateMemoryMetrics);
                }
                catch (Exception ex)
                {
                    AppLogger.Log($"Background modules start error: {ex.Message}");
                }
            });

            UpdateMemoryMetrics();
            AppLogger.Log("MainWindow.OnLoaded finished successfully");
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Error in OnLoaded: {ex}");
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplyDarkWindowChrome();

        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            _core.AttachClipboardListener(hwnd);

            var source = HwndSource.FromHwnd(hwnd);
            source?.AddHook(WndProc);
            Win32Api.RegisterUsbDeviceNotification(hwnd);

            var islandHwnd = _islandModule.GetWindowHandle();
            _core.SetIslandWindowHandle(islandHwnd);
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Error in OnSourceInitialized: {ex.Message}");
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Win32Api.WM_DEVICECHANGE)
        {
            int changeType = wParam.ToInt32();
            if (changeType == Win32Api.DBT_DEVICEARRIVAL || changeType == Win32Api.DBT_DEVICEREMOVECOMPLETE)
            {
                bool isArrival = (changeType == Win32Api.DBT_DEVICEARRIVAL);
                if (SettingsManager.Current.IslandShowUsb)
                {
                    _core.TriggerIslandEvent(new DynamicIslandEvent
                    {
                        Type = IslandEventType.Usb,
                        Title = isArrival ? "USB Device Connected" : "USB Device Removed",
                        Subtitle = isArrival ? "Hardware ready" : "Hardware safely disconnected",
                        Glyph = isArrival ? "🔌" : "⏏️",
                        DisplayDurationMs = 2500
                    });
                }
            }
        }
        return IntPtr.Zero;
    }

    private void ApplyDarkWindowChrome()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            int useDarkMode = 1;
            DwmSetWindowAttribute(hwnd, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref useDarkMode, sizeof(int));
        }
        catch { }
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    #region Window Drag, Buttons & Navigation

    private void OnTitleBarMouseDown(object sender, MouseButtonEventArgs e) => OnHeaderMouseDown(sender, e);

    private void OnHeaderMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void OnMaximizeClick(object sender, RoutedEventArgs e)
    {
        WindowState = (WindowState == WindowState.Maximized) ? WindowState.Normal : WindowState.Maximized;
    }

    /// <summary>
    /// Single click on the Close button: hides dashboard to tray while keeping widgets & overlays active.
    /// </summary>
    private void OnCloseSingleClick(object sender, RoutedEventArgs e)
    {
        AppLogger.Log("Close button clicked (single click): hiding dashboard to tray while keeping widgets & overlays active");
        Hide();
        MemoryOptimizer.TrimMemory();
    }

    /// <summary>
    /// Long-press hold-to-confirm on the Close button: terminates the entire application and all overlays.
    /// </summary>
    private void OnCloseHoldConfirmed(object sender, RoutedEventArgs e)
    {
        AppLogger.Log("Close button hold-to-confirm completed: ForceCompleteShutdown initiated");
        ForceCompleteShutdown();
    }

    private void OnGlobalSaveSettingsClick(object sender, RoutedEventArgs e)
    {
        try
        {
            SettingsManager.SaveSettings();
            _core?.TriggerIslandEvent(new DynamicIslandEvent
            {
                Type = IslandEventType.SystemAlert,
                Title = "Settings Saved",
                Subtitle = "Configuration updated successfully",
                Glyph = "💾",
                DisplayDurationMs = 2000
            });
        }
        catch (Exception ex)
        {
            AppLogger.Log($"SaveSettings error: {ex.Message}");
        }
    }

    private void OnEmergencyKillClick(object sender, RoutedEventArgs e)
    {
        AppLogger.Log("Emergency Kill Switch invoked");
        ForceCompleteShutdown();
    }

    private void OnNavChecked(object sender, RoutedEventArgs e)
    {
        if (ViewOverview == null || ViewWorkspaces == null || ViewRadial == null ||
            ViewIsland == null || ViewAi == null || ViewOptions == null) return;

        if (sender is RadioButton rb && rb.Tag is string tagStr && int.TryParse(tagStr, out int index))
        {
            ViewOverview.Visibility = (index == 0) ? Visibility.Visible : Visibility.Collapsed;
            ViewWorkspaces.Visibility = (index == 1) ? Visibility.Visible : Visibility.Collapsed;
            ViewRadial.Visibility = (index == 2) ? Visibility.Visible : Visibility.Collapsed;
            ViewIsland.Visibility = (index == 3) ? Visibility.Visible : Visibility.Collapsed;
            ViewAi.Visibility = (index == 4) ? Visibility.Visible : Visibility.Collapsed;
            ViewOptions.Visibility = (index == 5) ? Visibility.Visible : Visibility.Collapsed;
            
        }
    }

    public void NavigateTo(int pageIndex, int subTabIndex = 0)
    {
        NavigateToSection(pageIndex, subTabIndex);
    }

    public void NavigateToSection(int pageIndex, int subTabIndex = 0)
    {
        Action act = () =>
        {
            Show();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();

            if (ViewOverview == null || ViewWorkspaces == null || ViewRadial == null ||
                ViewIsland == null || ViewAi == null || ViewOptions == null) return;

            ViewOverview.Visibility = (pageIndex == 0) ? Visibility.Visible : Visibility.Collapsed;
            ViewWorkspaces.Visibility = (pageIndex == 1) ? Visibility.Visible : Visibility.Collapsed;
            ViewRadial.Visibility = (pageIndex == 2) ? Visibility.Visible : Visibility.Collapsed;
            ViewIsland.Visibility = (pageIndex == 3) ? Visibility.Visible : Visibility.Collapsed;
            ViewAi.Visibility = (pageIndex == 4) ? Visibility.Visible : Visibility.Collapsed;
            ViewOptions.Visibility = (pageIndex == 5) ? Visibility.Visible : Visibility.Collapsed;
            

            var navRadios = new[] { NavBtnOverview, NavBtnWorkspaces, NavBtnRadial, NavBtnIsland, NavBtnAi, NavBtnOptions };
            if (pageIndex >= 0 && pageIndex < navRadios.Length && navRadios[pageIndex] != null)
            {
                navRadios[pageIndex].IsChecked = true;
            }
        };

        if (CheckAccess()) act();
        else Dispatcher.Invoke(act);
    }

    public System.Windows.Media.Imaging.BitmapSource? CaptureSectionSnapshot(int pageIndex, int subTabIndex = 0)
    {
        Func<System.Windows.Media.Imaging.BitmapSource?> func = () =>
        {
            try
            {
                NavigateToSection(pageIndex, subTabIndex);
                UpdateLayout();

                FrameworkElement? target = pageIndex switch
                {
                    0 => ViewOverview,
                    1 => ViewWorkspaces,
                    2 => ViewRadial,
                    3 => ViewIsland,
                    4 => ViewAi,
                    5 => ViewOptions,
                    _ => ViewOverview
                };

                if (target == null) return null;

                double w = target.ActualWidth > 0 ? target.ActualWidth : 800;
                double h = target.ActualHeight > 0 ? target.ActualHeight : 560;

                int pixelW = Math.Max(1, (int)Math.Ceiling(w));
                int pixelH = Math.Max(1, (int)Math.Ceiling(h));

                var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(pixelW, pixelH, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                rtb.Render(target);
                rtb.Freeze();
                return rtb;
            }
            catch { return null; }
        };

        if (CheckAccess()) return func();
        return Dispatcher.Invoke(func);
    }

    public System.Windows.Media.Imaging.BitmapSource? CaptureModalSnapshot(string modalName)
    {
        Func<System.Windows.Media.Imaging.BitmapSource?> func = () =>
        {
            try
            {
                FrameworkElement? modal = modalName switch
                {
                    "IconPicker" => IconPickerModal,
                    "InstalledApps" => InstalledAppsModal,
                    _ => null
                };

                if (modal == null) return null;

                var prevVis = modal.Visibility;
                modal.Visibility = Visibility.Visible;
                UpdateLayout();

                double w = modal.ActualWidth > 0 ? modal.ActualWidth : 940;
                double h = modal.ActualHeight > 0 ? modal.ActualHeight : 620;

                int pixelW = Math.Max(1, (int)Math.Ceiling(w));
                int pixelH = Math.Max(1, (int)Math.Ceiling(h));

                var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(pixelW, pixelH, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                rtb.Render(modal);
                rtb.Freeze();

                modal.Visibility = prevVis;
                return rtb;
            }
            catch { return null; }
        };

        if (CheckAccess()) return func();
        return Dispatcher.Invoke(func);
    }

    public void ShowCustomExitDialog()
    {
        try
        {
            var dlg = new Controls.ExitDialog { Owner = this };
            dlg.ShowDialog();
            if (dlg.Choice == Controls.ExitDialogChoice.ExitCompletely)
            {
                ForceCompleteShutdown();
            }
            else if (dlg.Choice == Controls.ExitDialogChoice.HideDashboardOnly)
            {
                Hide();
                MemoryOptimizer.TrimMemory();
            }
        }
        catch { }
    }

    public void ShowRadialWheel()
    {
        Action act = () =>
        {
            Win32Api.GetCursorPos(out var pt);
            _radialWindow.ShowAt(pt.x, pt.y);
        };
        if (CheckAccess()) act();
        else Dispatcher.Invoke(act);
    }

    public void ShowIconPickerModal() => OpenIconPickerModal(_ => { });
    public void ShowInstalledAppsModal() => OpenInstalledAppsModal(_ => { });

    private void UpdateMemoryMetrics()
    {
        try
        {
            var proc = Process.GetCurrentProcess();
            proc.Refresh();
            long mb = proc.WorkingSet64 / (1024 * 1024);
            if (RamUsageText != null)
            {
                RamUsageText.Text = $"RAM: ~{mb} MB";
            }
        }
        catch { }
    }

    #endregion

    #region Shared Modals (Icon Picker & Installed Apps Picker)

    private void InitCuratedIcons()
    {
        if (_allCuratedIcons == null || _allCuratedIcons.Count == 0)
        {
            _allCuratedIcons = IconGallery.Icons;
        }
    }

    public void OpenIconPickerModal(Action<string> onSelected)
    {
        InitCuratedIcons();
        _onIconSelectedTarget = onSelected;
        IconSearchBox.Text = "";
        _iconSearchQuery = "";
        _selectedIconCategory = "All";
        FilterAndDisplayIcons();
        IconPickerModal.Visibility = Visibility.Visible;
    }

    private void OnCloseIconPickerClick(object sender, RoutedEventArgs e)
    {
        IconPickerModal.Visibility = Visibility.Collapsed;
        _onIconSelectedTarget = null;
    }

    private void OnIconPickerBackdropClick(object sender, MouseButtonEventArgs e)
    {
        IconPickerModal.Visibility = Visibility.Collapsed;
        _onIconSelectedTarget = null;
    }

    private void OnIconCategoryChipClick(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Tag is string category)
        {
            _selectedIconCategory = category;
            FilterAndDisplayIcons();
        }
    }

    private void OnIconSearchChanged(object sender, TextChangedEventArgs e)
    {
        _iconSearchQuery = IconSearchBox.Text.Trim();
        FilterAndDisplayIcons();
    }

    private void FilterAndDisplayIcons()
    {
        if (IconsListControl == null) return;
        var filtered = _allCuratedIcons.AsEnumerable();

        if (!string.Equals(_selectedIconCategory, "All", StringComparison.OrdinalIgnoreCase))
        {
            filtered = filtered.Where(i => string.Equals(i.Category, _selectedIconCategory, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(_iconSearchQuery))
        {
            filtered = filtered.Where(i => i.Name.Contains(_iconSearchQuery, StringComparison.OrdinalIgnoreCase) ||
                                           i.Category.Contains(_iconSearchQuery, StringComparison.OrdinalIgnoreCase));
        }

        IconsListControl.ItemsSource = filtered.ToList();
    }

    private void OnIconTileClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string glyph)
        {
            _onIconSelectedTarget?.Invoke(glyph);
            IconPickerModal.Visibility = Visibility.Collapsed;
            _onIconSelectedTarget = null;
        }
    }

    public async void OpenInstalledAppsModal(Action<InstalledApp> onSelected)
    {
        _onAppSelectedTarget = onSelected;
        InstalledAppsModal.Visibility = Visibility.Visible;
        InstalledAppsSearchBox.Text = "";
        _appSearchQuery = "";

        if (_allInstalledApps.Count == 0)
        {
            InstalledAppsStatusBlock.Text = "Scanning installed applications...";
            InstalledAppsListControl.ItemsSource = null;
            _allInstalledApps = await InstalledAppsService.GetInstalledApplicationsAsync();
        }

        InstalledAppsStatusBlock.Text = $"Detected {_allInstalledApps.Count} applications";
        FilterAndDisplayInstalledApps();
    }

    private void OnCloseInstalledAppsClick(object sender, RoutedEventArgs e)
    {
        InstalledAppsModal.Visibility = Visibility.Collapsed;
        _onAppSelectedTarget = null;
    }

    private void OnInstalledAppsBackdropClick(object sender, MouseButtonEventArgs e)
    {
        InstalledAppsModal.Visibility = Visibility.Collapsed;
        _onAppSelectedTarget = null;
    }

    private async void OnRescanInstalledAppsClick(object sender, RoutedEventArgs e)
    {
        InstalledAppsStatusBlock.Text = "Rescanning system applications...";
        InstalledAppsListControl.ItemsSource = null;
        _allInstalledApps = await InstalledAppsService.GetInstalledApplicationsAsync(forceRefresh: true);
        InstalledAppsStatusBlock.Text = $"Detected {_allInstalledApps.Count} applications";
        FilterAndDisplayInstalledApps();
    }

    private void OnInstalledAppSearchChanged(object sender, TextChangedEventArgs e)
    {
        _appSearchQuery = InstalledAppsSearchBox.Text.Trim();
        FilterAndDisplayInstalledApps();
    }

    private void FilterAndDisplayInstalledApps()
    {
        if (InstalledAppsListControl == null) return;
        var filtered = _allInstalledApps.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(_appSearchQuery))
        {
            filtered = filtered.Where(a => a.Name.Contains(_appSearchQuery, StringComparison.OrdinalIgnoreCase) ||
                                           a.Category.Contains(_appSearchQuery, StringComparison.OrdinalIgnoreCase));
        }

        InstalledAppsListControl.ItemsSource = filtered.ToList();
    }

    private void OnSelectInstalledAppClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is InstalledApp app)
        {
            _onAppSelectedTarget?.Invoke(app);
            InstalledAppsModal.Visibility = Visibility.Collapsed;
            _onAppSelectedTarget = null;
        }
    }

    #endregion

    #region Shutdown & Lifecycle

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        ForceCompleteShutdown();
    }

    public void ForceCompleteShutdown()
    {
        if (_isShuttingDown) return;
        _isShuttingDown = true;

        AppLogger.Log("ForceCompleteShutdown: starting instant termination");

        try { Hide(); } catch { }
        try { _radialWindow?.Hide(); } catch { }
        try { _ramTimer?.Stop(); } catch { }
        try { _trayService?.Dispose(); } catch { }

        try { _radialWindow?.Close(); } catch { }
        try { _islandModule?.Dispose(); } catch { }
        try { _wallpaperModule?.Dispose(); } catch { }
        try { _workspaceModule?.Dispose(); } catch { }
        try { _widgetsModule?.Dispose(); } catch { }
        try { _wallpaperManager?.DisposeWallpapers(); } catch { }
        try { _core?.Dispose(); } catch { }

        AppLogger.Log("ForceCompleteShutdown: terminating process immediately.");

        try { Process.GetCurrentProcess().Kill(); }
        catch { Environment.Exit(0); }
    }

    #endregion
}
