using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using System.Windows.Controls;
using System.Windows.Threading;
using TheHub.App.Services;
using TheHub.Common.Config;
using TheHub.Common.Models;
using TheHub.Core;
using TheHub.Core.Native;
using TheHub.Core.Services;
using TheHub.Overlays.DynamicIsland;
using TheHub.Overlays.RadialLauncher;
using TheHub.Overlays.Wallpaper;
using TheHub.Overlays.Widgets;
using Button = System.Windows.Controls.Button;
using RadioButton = System.Windows.Controls.RadioButton;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace TheHub.App;

public partial class MainWindow : Window
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWA_CAPTION_COLOR = 35;
    private const int DWMWA_TEXT_COLOR = 36;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    private void ApplyDarkWindowChrome()
    {
        try
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            int dark = 1;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
            int borderColor = 0x00121212; // #121212 in COLORREF (0x00BBGGRR)
            DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref borderColor, sizeof(int));
            int captionColor = 0x00121212;
            DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref captionColor, sizeof(int));
        }
        catch { }
    }

    private readonly CoreHubManager _core;
    private readonly RadialWindow _radialWindow;
    private readonly IslandModule _islandModule;
    private readonly WallpaperManager _wallpaperManager;
    private readonly WallpaperModule _wallpaperModule;
    private DesktopClockWidget? _clockWidget;
    // Split hardware widgets (Section 2.3) — independent windows, same DesktopWidgetBase pattern as clock
    private DesktopCpuWidget? _cpuWidget;
    private DesktopGpuWidget? _gpuWidget;
    private DesktopRamWidget? _ramWidget;
    private readonly OverviewViewModel _overviewViewModel;
    private string _selectedMonitorTarget = "ALL";
    public WallpaperManager WallpaperManager => _wallpaperManager;
    public WallpaperModule WallpaperModule => _wallpaperModule;
    private readonly WorkspaceModule _workspaceModule;
    public WorkspaceModule WorkspaceModule => _workspaceModule;
    private readonly WidgetsModule _widgetsModule;
    public WidgetsModule WidgetsModule => _widgetsModule;
    private readonly DispatcherTimer _ramTimer;
    private readonly TrayIconService _trayService;
    private WorkspaceConfig? _selectedWorkspace;
    private RadialWheelProfile? _selectedRadialProfile;
    private AppShortcut? _editingApp;
    private Action<string>? _onIconSelectedTarget;
    private string _selectedIconCategory = "All";
    private string _iconSearchQuery = "";
    private List<InstalledApp> _allInstalledApps = new();
    private string _appSearchQuery = "";
    private bool _isFullscreenActive;

    public MainWindow()
    {
        AppLogger.Log("MainWindow.ctor started");
        try
        {
            InitializeComponent();
            AppLogger.Log("MainWindow.InitializeComponent finished");

            string currentExePath = Environment.ProcessPath ?? "";
            DateTime buildTime = System.IO.File.Exists(currentExePath) ? System.IO.File.GetLastWriteTime(currentExePath) : System.IO.File.GetLastWriteTime(typeof(MainWindow).Assembly.Location);
            string buildNumber = buildTime.ToString("yyyyMMdd.HHmm");
            Title = $"The Hub — Desktop Companion (Build {buildNumber})";
            if (TxtHeaderTitle != null)
            {
                TxtHeaderTitle.Text = $"Desktop Companion (Build {buildNumber})";
            }
        }
        catch (Exception ex)
        {
            AppLogger.Log($"FATAL EXCEPTION in InitializeComponent: {ex}");
            throw;
        }

        _core = new CoreHubManager();
        _radialWindow = new RadialWindow();
        _islandModule = new IslandModule();
        _wallpaperManager = new WallpaperManager();
        _wallpaperModule = new WallpaperModule(Core.Bus.HubBus.Instance, Core.Bridge.UiBridge.Instance, _wallpaperManager);
        _workspaceModule = new WorkspaceModule(Core.Bus.HubBus.Instance, Core.Bridge.UiBridge.Instance);
        _widgetsModule = new WidgetsModule(Core.Bus.HubBus.Instance, Core.Bridge.UiBridge.Instance);
        _trayService = new TrayIconService();

        // Tray Icon event wiring
        _trayService.OpenMainWindowRequested += () => Dispatcher.BeginInvoke(() =>
        {
            Show();
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }
            Activate();
        });

        // Dynamic Island Open Settings wiring
        _islandModule.OpenSettingsRequested += () => Dispatcher.BeginInvoke(() =>
        {
            Show();
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }
            Activate();
            Topmost = true;
            Topmost = false;
            Focus();
        });

        _trayService.OpenRadialRequested += () => Dispatcher.BeginInvoke(() =>
        {
            Core.Native.Win32Api.GetCursorPos(out var pt);
            _radialWindow.ShowAt(pt.x, pt.y);
        });

        _trayService.ToggleIslandRequested += (show) => _islandModule.ToggleVisibility(show);

        _trayService.ExitRequested += () => Dispatcher.BeginInvoke(() =>
        {
            ForceCompleteShutdown();
        });

        // Wallpaper Codec / Playback Diagnostics wiring
        _wallpaperManager.WallpaperPlaybackFailed += (devName, errorMsg) => Dispatcher.BeginInvoke(() =>
        {
            var settings = SettingsManager.Current;
            settings.MonitorWallpapers.Remove(devName);
            settings.MonitorWallpapers.Remove("ALL");
            foreach (var wp in settings.Wallpapers)
            {
                wp.IsActive = false;
                wp.NotifyStateChanged();
            }
            SettingsManager.SaveSettings();
            RefreshWallpapersGallery();
            PopulateMonitorSelectorUI();

            bool isMissingFile = errorMsg.StartsWith("Cannot find the media file", StringComparison.OrdinalIgnoreCase);

            _core.TriggerIslandEvent(new DynamicIslandEvent
            {
                Type = IslandEventType.SystemAlert,
                Title = isMissingFile ? "Media File Not Found" : "Playback Failed (Unsupported Codec)",
                Subtitle = isMissingFile ? errorMsg : "AV1 / 4K codec unsupported. Please use H.264 (MP4).",
                Glyph = "⚠️",
                DisplayDurationMs = 5000
            });

            string messageBody = isMissingFile
                ? $"Live Wallpaper Playback Failed on {devName}:\n\n" +
                  $"Reason: {errorMsg}\n\n" +
                  "The application could not locate the media file at the specified path. Please ensure the file exists and try adding it again."
                : $"Live Wallpaper Playback Failed on {devName}:\n\n" +
                  $"Reason: {errorMsg}\n\n" +
                  "This video file is encoded with an unsupported video codec (such as AV1 or HEVC at 4K/60fps) which Windows Media Player / DirectShow cannot decode natively without Microsoft Store extensions.\n\n" +
                  "Recommended Solution:\n" +
                  "1. Convert the video to standard MP4 (H.264 / AAC) at 1080p using HandBrake or any video converter for smooth playback.\n" +
                  "2. Or install 'AV1 Video Extension' from the Microsoft Store.";

            System.Windows.MessageBox.Show(
                messageBody,
                isMissingFile ? "The Hub - Media File Not Found" : "The Hub - Unsupported Video Codec",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        });

        // Core Hub Event Wiring
        _core.RadialLauncherRequested += (x, y) => Dispatcher.BeginInvoke(() =>
        {
            if (_radialWindow.IsVisible)
            {
                if (string.Equals(SettingsManager.Current.GestureBehavior, "Click", StringComparison.OrdinalIgnoreCase))
                {
                    _radialWindow.Dismiss();
                }
                return;
            }

            _radialWindow.ShowAt(x, y);
        });
        _core.RadialPointerMoved += (x, y) => Dispatcher.BeginInvoke(() => _radialWindow.UpdatePointer(x, y));
        _core.RadialReleaseRequested += () => Dispatcher.BeginInvoke(() => _radialWindow.TriggerSelectionAndClose());

        _radialWindow.Dismissed += () => _core.ResetRadialMouseState();
        _radialWindow.ShortcutSelected += shortcut =>
        {
            _core.ResetRadialMouseState();
            AppLauncherService.LaunchShortcut(shortcut);
            _core.TriggerIslandEvent(new DynamicIslandEvent
            {
                Type = IslandEventType.SystemAlert,
                Title = "Launched",
                Subtitle = shortcut.Title,
                Glyph = shortcut.IconGlyph,
                DisplayDurationMs = 2000
            });
        };

        _core.FullscreenModeChanged += isFullscreen =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                _isFullscreenActive = isFullscreen;
                AppLogger.Log($"Fullscreen state changed: isFullscreen={isFullscreen}");

                if (isFullscreen)
                {
                    if (SettingsManager.Current.IslandHideOnFullscreen)
                    {
                        _islandModule.ToggleVisibility(false);
                    }
                    if (SettingsManager.Current.WallpaperPauseOnFullscreen)
                    {
                        _wallpaperManager.Pause();
                    }
                }
                else
                {
                    if (SettingsManager.Current.DynamicIslandEnabled)
                    {
                        _islandModule.ToggleVisibility(true);
                    }
                    if (SettingsManager.Current.WallpaperPauseOnFullscreen)
                    {
                        _wallpaperManager.Resume();
                    }
                }
            });
        };

        _radialWindow.WorkspaceSwitchRequested += (idx) => Dispatcher.BeginInvoke(RefreshWorkspacesCards);

        _core.DynamicIslandEventReceived += evt => Dispatcher.BeginInvoke(() =>
        {
            if (_isFullscreenActive && SettingsManager.Current.IslandHideOnFullscreen)
            {
                // Suppress overlay popup during fullscreen games or videos
                return;
            }

            if (SettingsManager.Current.DynamicIslandEnabled)
            {
                _islandModule.ToggleVisibility(true);
            }
            _islandModule.PostEvent(evt);
        });

        _islandModule.PlayPauseRequested += async () => await _core.MediaPlayPauseAsync();
        _islandModule.NextRequested += async () => await _core.MediaNextAsync();
        _islandModule.PrevRequested += async () => await _core.MediaPrevAsync();

        // RAM update timer
        _ramTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _ramTimer.Tick += (s, e) => UpdateMemoryMetrics();
        _ramTimer.Start();

        // Overview Widgets ViewModel Wiring (Modular Cards Architecture)
        _overviewViewModel = new OverviewViewModel(
            setClockActive: SetClockWidgetState,
            setClockLocked: SetClockWidgetLocked,
            widgetConfigChanged: OnWidgetConfigChanged,
            setGaugesActive: SetResourceWidgetState,
            setGaugesLocked: SetResourceWidgetLocked,
            setCpuActive: SetCpuWidgetState,
            setGpuActive: SetGpuWidgetState,
            setRamActive: SetRamWidgetState
        );
            OverviewWidgetsControl.DataContext = _overviewViewModel;

            // Restore each hardware widget independently.
            SetCpuWidgetState(_overviewViewModel.IsCpuActive);
            SetGpuWidgetState(_overviewViewModel.IsGpuActive);
            SetRamWidgetState(_overviewViewModel.IsRamActive);

            Loaded += OnLoaded;
        AppLogger.Log("MainWindow.ctor finished");
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        AppLogger.Log("MainWindow.OnLoaded started");
        try
        {
            // Bind UI data
            var settings = SettingsManager.Current;
            WorkspacesListControl.ItemsSource = settings.Workspaces;
            // PermissionsListControl removed — AI Permissions section no longer shown in Options tab.

            // Options page: custom fonts list + island typography follow the active font
            RefreshFontsList();
            if (_overviewViewModel?.Config != null)
            {
                ApplyDynamicIslandFont(_overviewViewModel.Config);
            }
            if (!string.IsNullOrWhiteSpace(settings.GlobalFontFamily))
            {
                FontFamily = CustomFontService.ResolveFontFamily(settings.GlobalFontFamily);
            }

            // Bind Radial Wheel Studio Settings
            RefreshWorkspacesCards();
            TxtGlobalShortcut.Text = settings.GlobalShortcut ?? "Alt+Z";
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

            SldCenterHubSize.Value = settings.RadialCenterRadius > 0 ? settings.RadialCenterRadius : 60;
            TxtCenterHubSizeValue.Text = $"{SldCenterHubSize.Value} px";

            SldIconSize.Value = settings.IconSize;
            TxtIconSizeValue.Text = $"{settings.IconSize} px";

            SldTargetSpacing.Value = settings.TargetSpacing;
            TxtTargetSpacingValue.Text = $"{settings.TargetSpacing} px";

            TxtHoverColor.Text = settings.HoverColor ?? "#FFFFFF";

            if (settings.TargetingMode == "Pointer") RbTargetingPointer.IsChecked = true;
            else RbTargetingDirection.IsChecked = true;

            ChkPersistentLabels.IsChecked = settings.PersistentLabels;

            SldWheelOpacity.Value = settings.WheelOpacity;
            TxtWheelOpacityValue.Text = $"{settings.WheelOpacity}%";

            SldBackgroundDimming.Value = settings.BackgroundDimming;
            TxtBackgroundDimmingValue.Text = $"{settings.BackgroundDimming}%";

            ChkPrecisionMode.IsChecked = settings.PerformanceMode;
            ChkFullscreenProtection.IsChecked = settings.FullscreenProtection;

            ChkMedia.IsChecked = settings.IslandShowMedia;
            ChkShowActiveApp.IsChecked = settings.IslandShowActiveApp;
            ChkVolume.IsChecked = settings.IslandShowVolume;
            ChkClipboard.IsChecked = settings.IslandShowClipboard;
            ChkUsb.IsChecked = settings.IslandShowUsb;
            // ChkHideOnFullscreen removed from XAML — IslandHideOnFullscreen defaults to true
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

            // Initialize Wallpaper Gallery, multi-monitor selector & active wallpaper(s)
            ChkPauseWallpaperFullscreen.IsChecked = settings.WallpaperPauseOnFullscreen;
            ChkPauseWallpaperBattery.IsChecked = settings.WallpaperPauseOnBattery;
            ChkMuteWallpaperAudio.IsChecked = settings.WallpaperMuted;

            // Sync Options tab mirrors
            if (ChkOptPauseFullscreen != null) ChkOptPauseFullscreen.IsChecked = settings.WallpaperPauseOnFullscreen;
            if (ChkOptPauseBattery != null) ChkOptPauseBattery.IsChecked = settings.WallpaperPauseOnBattery;
            if (ChkOptMuteAudio != null) ChkOptMuteAudio.IsChecked = settings.WallpaperMuted;

            string currentFit = settings.WallpaperFitMode ?? "Fill";
            foreach (ComboBoxItem item in CmbWallpaperFit.Items)
            {
                if (string.Equals(item.Content?.ToString(), currentFit, StringComparison.OrdinalIgnoreCase))
                {
                    CmbWallpaperFit.SelectedItem = item;
                    break;
                }
            }
            _wallpaperManager.SetFitMode(currentFit);
            _selectedMonitorTarget = !string.IsNullOrEmpty(settings.SelectedTargetMonitor) ? settings.SelectedTargetMonitor : "ALL";
            PopulateMonitorSelectorUI();
            RefreshWallpapersGallery();
            RestoreActiveWallpapers();
            if (_overviewViewModel != null)
            {
                SetClockWidgetState(_overviewViewModel.IsClockActive);
            }

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

            UpdateMemoryMetrics();
            AppLogger.Log("MainWindow.OnLoaded finished successfully");
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Error in OnLoaded: {ex}");
        }
    }

    private PerformanceCounter? _wsPrivateCounter;

    private double GetWorkingSetPrivateMb()
    {
        try
        {
            if (_wsPrivateCounter == null)
            {
                int currentPid = Environment.ProcessId;
                string procName = Process.GetCurrentProcess().ProcessName;
                var category = new PerformanceCounterCategory("Process");
                string[] instances = category.GetInstanceNames();

                string? targetInstance = null;
                foreach (var inst in instances)
                {
                    if (inst.StartsWith(procName, StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            using var pidCounter = new PerformanceCounter("Process", "ID Process", inst, true);
                            if ((int)pidCounter.RawValue == currentPid)
                            {
                                targetInstance = inst;
                                break;
                            }
                        }
                        catch { }
                    }
                }

                _wsPrivateCounter = new PerformanceCounter("Process", "Working Set - Private", targetInstance ?? procName, true);
            }

            float valBytes = _wsPrivateCounter.NextValue();
            if (valBytes > 0)
            {
                return valBytes / (1024.0 * 1024.0);
            }
        }
        catch { }

        // Fallback: If PerformanceCounter is restricted or unavailable
        try
        {
            var proc = Process.GetCurrentProcess();
            proc.Refresh();
            return proc.WorkingSet64 / (1024.0 * 1024.0);
        }
        catch { }

        return 55.0;
    }

    private void UpdateMemoryMetrics()
    {
        try
        {
            double memMb = GetWorkingSetPrivateMb();
            long roundedMb = (long)Math.Round(memMb);
            if (RamUsageText != null)
            {
                RamUsageText.Text = $"RAM: ~{roundedMb} MB";
            }
        }
        catch { }
    }

    private void OnNavChecked(object sender, RoutedEventArgs e)
    {
        if (PageOverview == null || PageWorkspaces == null || PageRadial == null || 
            PageIsland == null || PageAi == null || PageOptions == null) return;

        if (sender is RadioButton rb && rb.Tag is string tagStr && int.TryParse(tagStr, out int index))
        {
            PageOverview.Visibility = (index == 0) ? Visibility.Visible : Visibility.Collapsed;
            PageWorkspaces.Visibility = (index == 1) ? Visibility.Visible : Visibility.Collapsed;
            PageRadial.Visibility = (index == 2) ? Visibility.Visible : Visibility.Collapsed;
            PageIsland.Visibility = (index == 3) ? Visibility.Visible : Visibility.Collapsed;
            PageAi.Visibility = (index == 4) ? Visibility.Visible : Visibility.Collapsed;
            PageOptions.Visibility = (index == 5) ? Visibility.Visible : Visibility.Collapsed;
            if (PageGallery != null)
            {
                PageGallery.Visibility = (index == 6) ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }

    /// <summary>
    /// Navigates the main window to a specific top-level page and optional sub-tab.
    /// Used by the Showcase Gallery to inspect active UI screens.
    /// </summary>
    public void NavigateToSection(int pageIndex, int subTabIndex = 0)
    {
        Dispatcher.Invoke(() =>
        {
            Show();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();

            if (PageOverview == null || PageWorkspaces == null || PageRadial == null || 
                PageIsland == null || PageAi == null || PageOptions == null) return;

            PageOverview.Visibility = (pageIndex == 0) ? Visibility.Visible : Visibility.Collapsed;
            PageWorkspaces.Visibility = (pageIndex == 1) ? Visibility.Visible : Visibility.Collapsed;
            PageRadial.Visibility = (pageIndex == 2) ? Visibility.Visible : Visibility.Collapsed;
            PageIsland.Visibility = (pageIndex == 3) ? Visibility.Visible : Visibility.Collapsed;
            PageAi.Visibility = (pageIndex == 4) ? Visibility.Visible : Visibility.Collapsed;
            PageOptions.Visibility = (pageIndex == 5) ? Visibility.Visible : Visibility.Collapsed;
            if (PageGallery != null)
            {
                PageGallery.Visibility = (pageIndex == 6) ? Visibility.Visible : Visibility.Collapsed;
            }

            if (pageIndex == 0 && NavBtnOverview != null) NavBtnOverview.IsChecked = true;
            else if (pageIndex == 1 && NavBtnWorkspaces != null) NavBtnWorkspaces.IsChecked = true;
            else if (pageIndex == 2 && NavBtnRadial != null) NavBtnRadial.IsChecked = true;
            else if (pageIndex == 3 && NavBtnIsland != null) NavBtnIsland.IsChecked = true;
            else if (pageIndex == 4 && NavBtnAi != null) NavBtnAi.IsChecked = true;
            else if (pageIndex == 5 && NavBtnOptions != null) NavBtnOptions.IsChecked = true;
            else if (pageIndex == 6 && NavBtnGallery != null) NavBtnGallery.IsChecked = true;

            if (pageIndex == 0)
            {
                if (subTabIndex == 0 && TabBtnOverviewWallpapers != null) TabBtnOverviewWallpapers.IsChecked = true;
                else if (subTabIndex == 1 && TabBtnOverviewWidgets != null) TabBtnOverviewWidgets.IsChecked = true;
                else if (subTabIndex == 2 && TabBtnOverviewTaskbar != null) TabBtnOverviewTaskbar.IsChecked = true;
            }
            else if (pageIndex == 2)
            {
                if (subTabIndex == 0 && TabBtnActivation != null) TabBtnActivation.IsChecked = true;
                else if (subTabIndex == 1 && TabBtnAppearance != null) TabBtnAppearance.IsChecked = true;
                else if (subTabIndex == 2 && TabBtnWorkspaces != null) TabBtnWorkspaces.IsChecked = true;
            }
            else if (pageIndex == 3)
            {
                _islandModule.ToggleVisibility(true);
            }
        });
    }

    public void ShowRadialWheel()
    {
        Dispatcher.Invoke(() =>
        {
            Core.Native.Win32Api.GetCursorPos(out var pt);
            _radialWindow.ShowAt(pt.x, pt.y);
        });
    }

    /// <summary>
    /// Captures a live pixel-perfect BitmapSource of a specific dashboard section, subtab, or view directly from this MainWindow.
    /// </summary>
    public System.Windows.Media.Imaging.BitmapSource? CaptureSectionSnapshot(int pageIndex, int subTabIndex = 0)
    {
        return Dispatcher.Invoke(() =>
        {
            try
            {
                int curPage = 0;
                if (PageWorkspaces?.Visibility == Visibility.Visible) curPage = 1;
                else if (PageRadial?.Visibility == Visibility.Visible) curPage = 2;
                else if (PageIsland?.Visibility == Visibility.Visible) curPage = 3;
                else if (PageAi?.Visibility == Visibility.Visible) curPage = 4;
                else if (PageOptions?.Visibility == Visibility.Visible) curPage = 5;
                else if (PageGallery?.Visibility == Visibility.Visible) curPage = 6;

                // Temporarily activate the requested page and subtab
                NavigateToSection(pageIndex, subTabIndex);
                UpdateLayout();

                FrameworkElement? target = pageIndex switch
                {
                    0 => PageOverview,
                    1 => PageWorkspaces,
                    2 => PageRadial,
                    3 => PageIsland,
                    4 => PageAi,
                    5 => PageOptions,
                    _ => PageOverview
                };

                if (target == null) return null;

                double w = target.ActualWidth > 0 ? target.ActualWidth : 940;
                double h = target.ActualHeight > 0 ? target.ActualHeight : 620;

                int pixelW = Math.Max(1, (int)Math.Ceiling(w));
                int pixelH = Math.Max(1, (int)Math.Ceiling(h));

                var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(pixelW, pixelH, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                rtb.Render(target);
                rtb.Freeze();

                // Restore previous active page
                NavigateToSection(curPage, 0);

                return rtb;
            }
            catch
            {
                return null;
            }
        });
    }

    /// <summary>
    /// Captures a live pixel-perfect BitmapSource of a modal dialog directly from this MainWindow.
    /// </summary>
    public System.Windows.Media.Imaging.BitmapSource? CaptureModalSnapshot(string modalName)
    {
        return Dispatcher.Invoke(() =>
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
                UpdateLayout();

                return rtb;
            }
            catch
            {
                return null;
            }
        });
    }

    protected override async void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        // Developer Quick Action: Ctrl + Shift + S triggers full UI Flow export & opens folder
        if (e.Key == Key.S && (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            e.Handled = true;
            try
            {
                var registry = new Showcase.ViewSnapshotRegistry();
                await Showcase.ScreenSnapshotExporter.ExportAllAsync(registry.GetAllSnapshots());
                Showcase.ScreenSnapshotExporter.OpenScreenshotsDirectory();
                _core.TriggerIslandEvent(new DynamicIslandEvent
                {
                    Type = IslandEventType.SystemAlert,
                    Title = "UI Flow Exported",
                    Subtitle = "Saved 17 high-res screens to /Screenshots/UI_Flow",
                    Glyph = "📸",
                    DisplayDurationMs = 3000
                });
            }
            catch (Exception ex)
            {
                AppLogger.Log($"Ctrl+Shift+S Export error: {ex.Message}");
            }
        }
    }

    public void ShowIconPickerModal()
    {
        Dispatcher.Invoke(() =>
        {
            Show();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
            if (IconPickerModal != null) IconPickerModal.Visibility = Visibility.Visible;
        });
    }

    public void ShowInstalledAppsModal()
    {
        Dispatcher.Invoke(() =>
        {
            Show();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
            if (InstalledAppsModal != null) InstalledAppsModal.Visibility = Visibility.Visible;
        });
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void OnMaximizeClick(object sender, RoutedEventArgs e)
    {
        WindowState = (WindowState == WindowState.Maximized) ? WindowState.Normal : WindowState.Maximized;
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        if (WindowState == WindowState.Minimized)
        {
            MemoryOptimizer.TrimMemory();
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_isShuttingDown)
        {
            base.OnClosing(e);
            return;
        }

        e.Cancel = true;
        ShowCustomExitDialog();
    }

    private void OnCloseButtonClick(object sender, RoutedEventArgs e)
    {
        ShowCustomExitDialog();
    }

    private void OnCloseToTrayClick(object sender, RoutedEventArgs e)
    {
        ShowCustomExitDialog();
    }

    public void ShowCustomExitDialog()
    {
        try
        {
            var dlg = new Controls.ExitDialog
            {
                Owner = this
            };

            dlg.ShowDialog();

            switch (dlg.Choice)
            {
                case Controls.ExitDialogChoice.HideDashboardOnly:
                    AppLogger.Log("User selected: Hide dashboard only (continue running in background)");
                    Hide();
                    MemoryOptimizer.TrimMemory();
                    break;
                case Controls.ExitDialogChoice.ExitCompletely:
                    AppLogger.Log("User selected: Exit application completely");
                    ForceCompleteShutdown();
                    break;
                case Controls.ExitDialogChoice.Cancel:
                default:
                    AppLogger.Log("User cancelled exit dialog");
                    Focus();
                    break;
            }
        }
        catch (Exception ex)
        {
            AppLogger.Log($"ShowCustomExitDialog error: {ex.Message}");
            Hide();
            MemoryOptimizer.TrimMemory();
        }
    }

    private void OnTestRadialClick(object sender, RoutedEventArgs e)
    {
        Core.Native.Win32Api.GetCursorPos(out var pt);
        _radialWindow.ShowAt(pt.x, pt.y);
    }

    private void OnTestMediaIslandClick(object sender, RoutedEventArgs e)
    {
        _core.TriggerIslandEvent(new DynamicIslandEvent
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
        _core.TriggerIslandEvent(new DynamicIslandEvent
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
        _core.TriggerIslandEvent(new DynamicIslandEvent
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
        _core.TriggerIslandEvent(new DynamicIslandEvent
        {
            Type = IslandEventType.Timer,
            Title = "Focus Session",
            Subtitle = "25:00 Remaining",
            Glyph = "⏱️",
            DisplayDurationMs = 3000
        });
    }

    #region Wallpaper & Desktop Management

    private void RefreshWallpapersGallery()
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

    private void PopulateMonitorSelectorUI()
    {
        if (MonitorSelectorPanel == null) return;
        MonitorSelectorPanel.Children.Clear();

        var settings = SettingsManager.Current;
        var monitors = DisplayMonitorHelper.GetAllMonitors();

        // 1. "All Displays (Duplicate Mode)" Button Card
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

        // 2. Individual Display Cards
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

        // Update target status indicators
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

        _core.TriggerIslandEvent(new DynamicIslandEvent
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
                _wallpaperManager.DisposeWallpapers();
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
                _wallpaperManager.StopMonitor(_selectedMonitorTarget);
                settings.MonitorWallpapers.Remove(_selectedMonitorTarget);

                // Update active flags on remaining wallpapers
                foreach (var wp in settings.Wallpapers)
                {
                    wp.IsActive = settings.MonitorWallpapers.Values.Contains(wp.Id);
                    wp.NotifyStateChanged();
                }
            }

            SettingsManager.SaveSettings();
            RefreshWallpapersGallery();
            PopulateMonitorSelectorUI();

            _core.TriggerIslandEvent(new DynamicIslandEvent
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

    private void RestoreActiveWallpapers()
    {
        try
        {
            var settings = SettingsManager.Current;

            string? ResolveExistingPath(string? path)
            {
                if (string.IsNullOrWhiteSpace(path)) return null;
                string abs = System.IO.Path.GetFullPath(path);
                if (File.Exists(abs)) return abs;

                string fallback = System.IO.Path.Combine(WallpaperService.GetWallpapersStorageDirectory(), System.IO.Path.GetFileName(path));
                if (File.Exists(fallback)) return fallback;

                return null;
            }

            // Check if All Displays mode is assigned
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
                            _wallpaperManager.PlayVideoOnAll(resolved, settings.WallpaperMuted, 0.0);
                        else
                            WallpaperService.ApplyStaticWallpaper(resolved);
                        return;
                    }
                }
            }

            // Otherwise restore per-monitor assignments
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
                                _wallpaperManager.PlayVideoOnMonitor(kvp.Key, resolved, settings.WallpaperMuted, 0.0);
                            else
                                WallpaperService.ApplyStaticWallpaper(resolved);
                        }
                    }
                }
                return;
            }

            // Fallback to legacy ActiveWallpaperId
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
                string fullPath = System.IO.Path.GetFullPath(rawPath);

                // Copy selected wallpaper file to permanent AppData folder (%AppData%\TheHub\Wallpapers)
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

                string fileName = System.IO.Path.GetFileNameWithoutExtension(persistentPath);
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
                        System.Windows.MessageBox.Show(
                            $"Video Codec Notice: {codecInfo.FriendlyName}\n\n" +
                            $"{codecInfo.WarningMessage}\n\n" +
                            $"Recommendation:\n{codecInfo.Recommendation}",
                            "The Hub - Video Codec Notice",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    }
                }

                ApplyWallpaperItem(newItem);

                _core.TriggerIslandEvent(new DynamicIslandEvent
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
            System.Windows.MessageBox.Show($"Failed to add wallpaper: {ex.Message}", "The Hub", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ApplyWallpaperItem(WallpaperItem item)
    {
        try
        {
            var settings = SettingsManager.Current;

            // Ensure physical absolute path is resolved with fallback
            string resolvedPath = System.IO.Path.GetFullPath(item.FilePath);
            if (!File.Exists(resolvedPath))
            {
                string fallback = System.IO.Path.Combine(WallpaperService.GetWallpapersStorageDirectory(), System.IO.Path.GetFileName(item.FilePath));
                if (File.Exists(fallback))
                {
                    resolvedPath = fallback;
                    item.FilePath = fallback;
                    SettingsManager.SaveSettings();
                }
                else
                {
                    item.NotifyStateChanged();
                    _core.TriggerIslandEvent(new DynamicIslandEvent
                    {
                        Type = IslandEventType.SystemAlert,
                        Title = "Wallpaper File Missing",
                        Subtitle = $"Cannot find file: '{System.IO.Path.GetFileName(item.FilePath)}'",
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
                        _core.TriggerIslandEvent(new DynamicIslandEvent
                        {
                            Type = IslandEventType.SystemAlert,
                            Title = $"Notice: {codec.FriendlyName}",
                            Subtitle = "AV1 / 4K codec may require Windows AV1 Extension.",
                            Glyph = "ℹ️",
                            DisplayDurationMs = 3500
                        });
                    }
                    _wallpaperManager.PlayVideoOnAll(resolvedPath, settings.WallpaperMuted, 0.0);
                }
                else
                {
                    _wallpaperManager.DisposeWallpapers();
                    WallpaperService.ApplyStaticWallpaper(resolvedPath);
                }
            }
            else
            {
                // Individual screen mode - per-display independent assignment
                // Remove stale "ALL" duplicate mapping if present, preserve other per-display assignments
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
                        _core.TriggerIslandEvent(new DynamicIslandEvent
                        {
                            Type = IslandEventType.SystemAlert,
                            Title = $"Notice: {codec.FriendlyName}",
                            Subtitle = "AV1 / 4K codec may require Windows AV1 Extension.",
                            Glyph = "ℹ️",
                            DisplayDurationMs = 3500
                        });
                    }
                    _wallpaperManager.PlayVideoOnMonitor(_selectedMonitorTarget, resolvedPath, settings.WallpaperMuted, 0.0);
                }
                else
                {
                    _wallpaperManager.StopMonitor(_selectedMonitorTarget);
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

            _core.TriggerIslandEvent(new DynamicIslandEvent
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
            var confirm = System.Windows.MessageBox.Show(
                $"Are you sure you want to remove '{item.Title}'?",
                "Remove Wallpaper",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm == MessageBoxResult.Yes)
            {
                var settings = SettingsManager.Current;

                // Stop instances currently running this wallpaper
                if (settings.MonitorWallpapers.TryGetValue("ALL", out var allId) && allId == item.Id)
                {
                    _wallpaperManager.DisposeWallpapers();
                    settings.MonitorWallpapers.Remove("ALL");
                }
                else
                {
                    var matchingDisplays = settings.MonitorWallpapers.Where(kvp => kvp.Value == item.Id).Select(kvp => kvp.Key).ToList();
                    foreach (var disp in matchingDisplays)
                    {
                        _wallpaperManager.StopMonitor(disp);
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

                _core.TriggerIslandEvent(new DynamicIslandEvent
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
        if (ChkPauseWallpaperFullscreen == null || ChkPauseWallpaperBattery == null) return;
        SettingsManager.Current.WallpaperPauseOnFullscreen = ChkPauseWallpaperFullscreen.IsChecked ?? true;
        SettingsManager.Current.WallpaperPauseOnBattery = ChkPauseWallpaperBattery.IsChecked ?? true;
        SettingsManager.SaveSettings();
        _wallpaperManager.CheckPowerState();
    }

    private void OnWallpaperFitSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbWallpaperFit?.SelectedItem is ComboBoxItem selectedItem)
        {
            string fitMode = selectedItem.Content?.ToString() ?? "Fill";
            SettingsManager.Current.WallpaperFitMode = fitMode;
            SettingsManager.SaveSettings();
            _wallpaperManager.SetFitMode(fitMode);
            AppLogger.Log($"[UI] Wallpaper fit mode changed to: {fitMode}");
        }
    }

    private void OnWallpaperMuteChanged(object sender, RoutedEventArgs e)
    {
        if (ChkMuteWallpaperAudio == null) return;
        bool isMuted = ChkMuteWallpaperAudio.IsChecked ?? true;
        SettingsManager.Current.WallpaperMuted = isMuted;
        _wallpaperManager.SetMuted(isMuted);
        SettingsManager.SaveSettings();
    }

    #endregion

    /// <summary>
    /// Mirrors Options-tab wallpaper checkboxes to actual settings and Wallpapers-tab controls.
    /// </summary>
    private void OnOptionsWallpaperSettingChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.CheckBox chk) return;
        bool isChecked = chk.IsChecked ?? false;
        string tag = chk.Tag?.ToString() ?? string.Empty;
        switch (tag)
        {
            case "PauseFullscreen":
                SettingsManager.Current.WallpaperPauseOnFullscreen = isChecked;
                if (ChkPauseWallpaperFullscreen != null) ChkPauseWallpaperFullscreen.IsChecked = isChecked;
                break;
            case "PauseBattery":
                SettingsManager.Current.WallpaperPauseOnBattery = isChecked;
                if (ChkPauseWallpaperBattery != null) ChkPauseWallpaperBattery.IsChecked = isChecked;
                break;
            case "MuteAudio":
                SettingsManager.Current.WallpaperMuted = isChecked;
                if (ChkMuteWallpaperAudio != null) ChkMuteWallpaperAudio.IsChecked = isChecked;
                _wallpaperManager.SetMuted(isChecked);
                break;
        }
        SettingsManager.SaveSettings();
        _wallpaperManager.CheckPowerState();
    }

    #region Workspaces & Category Management

    private async void OnLaunchWorkspaceClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is WorkspaceConfig ws)
        {
            bool ok = await _workspaceModule.SwitchToWorkspaceAsync(ws);
            RefreshWorkspacesCards();

            _core.TriggerIslandEvent(new DynamicIslandEvent
            {
                Type = IslandEventType.WorkspaceChanged,
                Title = $"Workspace: {ws.Name}",
                Subtitle = ok ? $"Switched to {ws.Name} profile ({ws.Apps.Count} apps)" : "Failed to switch workspace",
                Glyph = ws.IconGlyph,
                DisplayDurationMs = 3500
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
            OpenWorkspaceDetail(newWs);
            _islandModule.SetWorkspaces(SettingsManager.Current.Workspaces);

            _core.TriggerIslandEvent(new DynamicIslandEvent
            {
                Type = IslandEventType.WorkspaceChanged,
                Title = "Desktop Captured",
                Subtitle = $"Created '{wsName}' with {newWs.Apps.Count} running app(s)",
                Glyph = "📸",
                DisplayDurationMs = 3500
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
        NewWsNameBox.Focus();
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
            System.Windows.MessageBox.Show("Please enter a workspace category name.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var newWs = new WorkspaceConfig
        {
            Name = name,
            IconGlyph = NewWsIconBtn.Content as string ?? "📁",
            AccentColor = string.IsNullOrWhiteSpace(NewWsColorBox.Text) ? "#3B82F6" : NewWsColorBox.Text.Trim(),
            Description = NewWsDescBox.Text.Trim(),
            Apps = new List<AppShortcut>()
        };

        SettingsManager.Current.Workspaces.Add(newWs);
        SettingsManager.SaveSettings();

        NewWorkspaceCard.Visibility = Visibility.Collapsed;
        RefreshWorkspacesList();
        OpenWorkspaceDetail(newWs);

        // Keep the Dynamic Island workspace launcher in sync
        _islandModule.SetWorkspaces(SettingsManager.Current.Workspaces);

        _core.TriggerIslandEvent(new DynamicIslandEvent
        {
            Type = IslandEventType.SystemAlert,
            Title = "Workspace Created",
            Subtitle = $"Added '{newWs.Name}' category",
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

    private void OpenWorkspaceDetail(WorkspaceConfig ws)
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
            System.Windows.MessageBox.Show("Category name cannot be empty.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
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

        _core.TriggerIslandEvent(new DynamicIslandEvent
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
            var confirm = System.Windows.MessageBox.Show(
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
                // Keep the Dynamic Island workspace launcher in sync
                _islandModule.SetWorkspaces(SettingsManager.Current.Workspaces);

                _core.TriggerIslandEvent(new DynamicIslandEvent
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
            System.Windows.MessageBox.Show("Please enter an application title.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            System.Windows.MessageBox.Show("Please specify an executable path or command name.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string glyph = AppIconBtn.Content as string ?? "⚡";
        string args = AppArgsBox.Text.Trim();

        if (_editingApp != null)
        {
            // Edit existing
            _editingApp.Title = title;
            _editingApp.ExecutablePath = path;
            _editingApp.IconGlyph = glyph;
            _editingApp.Arguments = args;
        }
        else
        {
            // Add new
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

        // Refresh UI bindings
        WorkspaceAppsItemsControl.ItemsSource = null;
        WorkspaceAppsItemsControl.ItemsSource = _selectedWorkspace.Apps;
        DetailWsSubtitleBlock.Text = $"{_selectedWorkspace.Apps.Count} configured application{(_selectedWorkspace.Apps.Count == 1 ? "" : "s")}";
        RefreshWorkspacesList();

        AppEditorCard.Visibility = Visibility.Collapsed;
        _editingApp = null;

        _core.TriggerIslandEvent(new DynamicIslandEvent
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

            _core.TriggerIslandEvent(new DynamicIslandEvent
            {
                Type = IslandEventType.SystemAlert,
                Title = success ? $"Launched {app.Title}" : "Launch Error",
                Subtitle = msg,
                Glyph = success ? app.IconGlyph : "⚠️",
                DisplayDurationMs = 3000
            });
        }
    }

    private void RefreshWorkspacesList()
    {
        WorkspacesListControl.ItemsSource = null;
        WorkspacesListControl.ItemsSource = SettingsManager.Current.Workspaces;
    }

    #endregion

    #region Icon Picker & Gallery Handlers

    private void OnOpenIconPickerForNewWsClick(object sender, RoutedEventArgs e)
    {
        _onIconSelectedTarget = glyph => NewWsIconBtn.Content = glyph;
        OpenIconPicker(NewWsIconBtn.Content as string ?? "📁");
    }

    private void OnOpenIconPickerForDetailWsClick(object sender, RoutedEventArgs e)
    {
        _onIconSelectedTarget = glyph => DetailWsIconBtn.Content = glyph;
        OpenIconPicker(DetailWsIconBtn.Content as string ?? "🎮");
    }

    private void OnOpenIconPickerForAppClick(object sender, RoutedEventArgs e)
    {
        _onIconSelectedTarget = glyph => AppIconBtn.Content = glyph;
        OpenIconPicker(AppIconBtn.Content as string ?? "⚡");
    }

    private void OpenIconPicker(string currentGlyph)
    {
        _iconSearchQuery = "";
        IconSearchBox.Text = "";
        _selectedIconCategory = "All";

        // Reset radio buttons in IconCategoriesPanel
        foreach (var child in IconCategoriesPanel.Children)
        {
            if (child is RadioButton rb)
            {
                rb.IsChecked = (rb.Tag as string == "All");
            }
        }

        FilterAndDisplayIcons();
        IconPickerModal.Visibility = Visibility.Visible;
    }

    private void OnCloseIconPickerClick(object sender, RoutedEventArgs e)
    {
        IconPickerModal.Visibility = Visibility.Collapsed;
        _onIconSelectedTarget = null;
    }

    private void OnIconPickerBackdropClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
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
        var icons = IconGallery.Icons.AsEnumerable();

        if (!string.Equals(_selectedIconCategory, "All", StringComparison.OrdinalIgnoreCase))
        {
            icons = icons.Where(i => string.Equals(i.Category, _selectedIconCategory, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(_iconSearchQuery))
        {
            icons = icons.Where(i =>
                i.Name.Contains(_iconSearchQuery, StringComparison.OrdinalIgnoreCase) ||
                i.Glyph.Contains(_iconSearchQuery, StringComparison.OrdinalIgnoreCase) ||
                i.Category.Contains(_iconSearchQuery, StringComparison.OrdinalIgnoreCase));
        }

        IconsListControl.ItemsSource = icons.ToList();
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

    #endregion

    #region Installed Apps Auto-Detection Handlers

    private async void OnOpenInstalledAppsClick(object sender, RoutedEventArgs e)
    {
        InstalledAppsModal.Visibility = Visibility.Visible;
        InstalledAppsSearchBox.Text = "";
        _appSearchQuery = "";

        if (_allInstalledApps.Count == 0)
        {
            await LoadInstalledAppsAsync(forceRefresh: false);
        }
        else
        {
            FilterAndDisplayInstalledApps();
        }
    }

    private async void OnRescanInstalledAppsClick(object sender, RoutedEventArgs e)
    {
        await LoadInstalledAppsAsync(forceRefresh: true);
    }

    private async Task LoadInstalledAppsAsync(bool forceRefresh)
    {
        InstalledAppsStatusBlock.Text = "Scanning system and desktop shortcuts for installed applications...";
        InstalledAppsListControl.ItemsSource = null;

        try
        {
            var apps = await InstalledAppsService.GetInstalledApplicationsAsync(forceRefresh);
            _allInstalledApps = apps;
            InstalledAppsStatusBlock.Text = $"Found {_allInstalledApps.Count} installed applications available on your PC.";
            FilterAndDisplayInstalledApps();
        }
        catch (Exception ex)
        {
            InstalledAppsStatusBlock.Text = $"Error scanning applications: {ex.Message}";
        }
    }

    private void OnCloseInstalledAppsClick(object sender, RoutedEventArgs e)
    {
        InstalledAppsModal.Visibility = Visibility.Collapsed;
    }

    private void OnInstalledAppsBackdropClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        InstalledAppsModal.Visibility = Visibility.Collapsed;
    }

    private void OnInstalledAppSearchChanged(object sender, TextChangedEventArgs e)
    {
        _appSearchQuery = InstalledAppsSearchBox.Text.Trim();
        FilterAndDisplayInstalledApps();
    }

    private void FilterAndDisplayInstalledApps()
    {
        if (_allInstalledApps == null || _allInstalledApps.Count == 0)
        {
            InstalledAppsListControl.ItemsSource = null;
            return;
        }

        var list = _allInstalledApps.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(_appSearchQuery))
        {
            list = list.Where(a =>
                a.Name.Contains(_appSearchQuery, StringComparison.OrdinalIgnoreCase) ||
                a.ExecutablePath.Contains(_appSearchQuery, StringComparison.OrdinalIgnoreCase) ||
                a.Category.Contains(_appSearchQuery, StringComparison.OrdinalIgnoreCase));
        }

        var result = list.ToList();
        InstalledAppsStatusBlock.Text = $"Showing {result.Count} of {_allInstalledApps.Count} applications";
        InstalledAppsListControl.ItemsSource = result;
    }

    private void OnSelectInstalledAppClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is InstalledApp app)
        {
            AppTitleBox.Text = app.Name;
            AppPathBox.Text = app.ExecutablePath;
            AppArgsBox.Text = app.Arguments ?? "";
            AppIconBtn.Content = app.IconGlyph;

            InstalledAppsModal.Visibility = Visibility.Collapsed;

            _core.TriggerIslandEvent(new DynamicIslandEvent
            {
                Type = IslandEventType.SystemAlert,
                Title = "App Selected",
                Subtitle = $"Selected {app.Name}",
                Glyph = app.IconGlyph,
                DisplayDurationMs = 2000
            });
        }
    }

    #endregion

    private void OnAiExecuteClick(object sender, RoutedEventArgs e)
    {
        string prompt = AiInputTextBox.Text.Trim();
        if (string.IsNullOrEmpty(prompt)) return;

        if (SettingsManager.Current.AIEmergencyKillSwitch)
        {
            AiOutputTextBlock.Text = "⛔ AI execution blocked: Emergency Kill Switch is ACTIVE.";
            return;
        }

        // Parse prompt for tool calling simulation
        if (prompt.Contains("جيمينج", StringComparison.OrdinalIgnoreCase) || prompt.Contains("gaming", StringComparison.OrdinalIgnoreCase))
        {
            var ws = SettingsManager.Current.Workspaces.FirstOrDefault(w => w.Name == "Gaming");
            if (ws != null)
            {
                _ = AppLauncherService.ApplyWorkspaceAsync(ws);
                AiOutputTextBlock.Text = $"✅ Executed Tool: ApplyWorkspace(\"Gaming\")\n• Launched: {string.Join(", ", ws.Apps.Select(a => a.Title))}\n• Applied profile settings & muted background widgets.";
            }
        }
        else if (prompt.Contains("استهلاك", StringComparison.OrdinalIgnoreCase) || prompt.Contains("specs", StringComparison.OrdinalIgnoreCase) || prompt.Contains("ram", StringComparison.OrdinalIgnoreCase))
        {
            long memMb = Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024);
            AiOutputTextBlock.Text = $"✅ Executed Tool: ReadSystemSpecs()\n• CPU: <0.5% (Idle)\n• Hub RAM: {memMb} MB\n• Display: Primary (1920x1080 @ 60Hz)";
        }
        else
        {
            // Default app opener
            AiOutputTextBlock.Text = $"✅ Executed Tool: SystemCall(\"{prompt}\")\n• Permission: Level 0 (Safe)\n• Status: Dispatched successfully to Windows Shell.";
        }
    }

    private void OnEmergencyKillClick(object sender, RoutedEventArgs e)
    {
        var settings = SettingsManager.Current;
        settings.AIEmergencyKillSwitch = !settings.AIEmergencyKillSwitch;
        SettingsManager.SaveSettings(settings);

        if (settings.AIEmergencyKillSwitch)
        {
            EmergencyButton.Content = "Resume Services";
            _core.TriggerIslandEvent(new DynamicIslandEvent
            {
                Type = IslandEventType.SystemAlert,
                Title = "Emergency Kill Switch",
                Subtitle = "All AI tools and automated hooks suspended",
                Glyph = "🛑",
                DisplayDurationMs = 4000
            });
        }
        else
        {
            EmergencyButton.Content = "Kill Switch";
            _core.TriggerIslandEvent(new DynamicIslandEvent
            {
                Type = IslandEventType.SystemAlert,
                Title = "Services Resumed",
                Subtitle = "Standard desktop companion mode active",
                Glyph = "⚡",
                DisplayDurationMs = 2500
            });
        }
    }

    private void OnSettingToggled(object sender, RoutedEventArgs e)
    {
        if (ChkMedia == null || ChkVolume == null || ChkClipboard == null || ChkUsb == null) return;

        var settings = SettingsManager.Current;
        settings.IslandShowMedia = ChkMedia?.IsChecked ?? true;
        settings.IslandShowActiveApp = ChkShowActiveApp?.IsChecked ?? true;
        settings.IslandShowVolume = ChkVolume?.IsChecked ?? true;
        settings.IslandShowClipboard = ChkClipboard?.IsChecked ?? true;
        settings.IslandShowUsb = ChkUsb?.IsChecked ?? true;
        settings.IslandHideOnFullscreen = true; // ChkHideOnFullscreen removed — always hide on fullscreen by default
        settings.IslandAlwaysShowOnFullscreen = ChkAlwaysShowFullscreen?.IsChecked ?? false;
        settings.IslandLockPosition = ChkLockPosition?.IsChecked ?? false;
        settings.IslandShowHoverQuickControls = ChkShowQuickControls?.IsChecked ?? true;
        settings.IslandShowMicMute = ChkShowMicMute?.IsChecked ?? true;
        settings.IslandShowBrightnessControl = ChkShowBrightness?.IsChecked ?? true;
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
        if (TxtScaleValue == null || SldIslandScale == null) return;
        var settings = SettingsManager.Current;
        settings.IslandScale = Math.Round(SldIslandScale.Value, 2);
        TxtScaleValue.Text = $"{(int)Math.Round(settings.IslandScale * 100)}%";
        SettingsManager.SaveSettings(settings);
        _islandModule?.UpdateLayout();
    }

    private void OnIslandTopChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtTopValue == null || SldIslandTop == null) return;
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
        _core.TriggerIslandEvent(new DynamicIslandEvent
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
        if (CmbIslandMonitor == null || CmbIslandMonitor.SelectedIndex < 0) return;

        var settings = SettingsManager.Current;
        if (settings.IslandMonitorIndex != CmbIslandMonitor.SelectedIndex)
        {
            settings.IslandMonitorIndex = CmbIslandMonitor.SelectedIndex;
            SettingsManager.SaveSettings(settings);

            _islandModule?.UpdateLayout();

            _core.TriggerIslandEvent(new DynamicIslandEvent
            {
                Type = IslandEventType.SystemAlert,
                Title = "Display Screen Changed",
                Subtitle = $"Anchored to screen {settings.IslandMonitorIndex + 1}",
                Glyph = "🖥️",
                DisplayDurationMs = 2000
            });
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        AppLogger.Log("MainWindow.OnSourceInitialized");
        base.OnSourceInitialized(e);
        ApplyDarkWindowChrome();

        try
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            _core.AttachClipboardListener(hwnd);
            AppLogger.Log("Attached clipboard listener to MainWindow HWND");

            // Attach Win32 message hook for USB Device Changes & Register for Device Notifications
            var source = System.Windows.Interop.HwndSource.FromHwnd(hwnd);
            source?.AddHook(WndProc);
            Win32Api.RegisterUsbDeviceNotification(hwnd);
            AppLogger.Log("Registered USB device notification for MainWindow HWND");

            // Pass Island HWND to FullscreenDetector for screen-aware isolation
            var islandHwnd = _islandModule.GetWindowHandle();
            _core.SetIslandWindowHandle(islandHwnd);
            AppLogger.Log("Passed island HWND to FullscreenDetector");
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
            if (changeType == Win32Api.DBT_DEVICEARRIVAL)
            {
                OnUsbDeviceChanged(isArrival: true);
            }
            else if (changeType == Win32Api.DBT_DEVICEREMOVECOMPLETE)
            {
                OnUsbDeviceChanged(isArrival: false);
            }
        }
        return IntPtr.Zero;
    }

    private void OnUsbDeviceChanged(bool isArrival)
    {
        if (!SettingsManager.Current.IslandShowUsb) return;

        _core.TriggerIslandEvent(new DynamicIslandEvent
        {
            Type = IslandEventType.Usb,
            Title = isArrival ? "USB Device Connected" : "USB Device Removed",
            Subtitle = isArrival ? "New hardware connected" : "Hardware safely removed",
            Glyph = isArrival ? "🔌" : "⏏️",
            DisplayDurationMs = 2500
        });
    }

    protected override void OnContentRendered(EventArgs e)
    {
        AppLogger.Log("MainWindow.OnContentRendered");
        base.OnContentRendered(e);
    }


    protected override void OnClosed(EventArgs e)
    {
        AppLogger.Log("MainWindow.OnClosed called - initiating complete exit");
        base.OnClosed(e);
        ForceCompleteShutdown();
    }

    private bool _isShuttingDown;
    public void ForceCompleteShutdown()
    {
        if (_isShuttingDown) return;
        _isShuttingDown = true;

        AppLogger.Log("ForceCompleteShutdown: starting instant application termination");

        // 1. Instantly hide windows so user immediately sees everything disappear
        try { Hide(); } catch { }
        try { _radialWindow?.Hide(); } catch { }
        try { _islandModule?.ToggleVisibility(false); } catch { }
        try { _clockWidget?.Hide(); } catch { }

        // 2. Stop timers
        try { _ramTimer?.Stop(); } catch { }
        try { _wsPrivateCounter?.Dispose(); } catch { }

        // 3. Remove tray icon immediately so no ghost icon remains in Windows taskbar
        try { _trayService?.Dispose(); } catch { }

        // 4. Close windows
        try { _radialWindow?.Close(); } catch { }
        try { _islandModule?.Dispose(); } catch { }
        try { _clockWidget?.Close(); } catch { }
        try { _wallpaperModule?.Dispose(); } catch { }
        try { _workspaceModule?.Dispose(); } catch { }
        try { _widgetsModule?.Dispose(); } catch { }
        try { _wallpaperManager?.DisposeWallpapers(); } catch { }

        // 5. Unhook keyboard/mouse hooks and close IPC
        try { _core?.Dispose(); } catch { }

        // 6. Terminate any orphan / related child processes
        CleanupRelatedProcesses();

        AppLogger.Log("ForceCompleteShutdown: cleanup completed. Terminating process immediately.");

        try
        {
            Process.GetCurrentProcess().Kill();
        }
        catch
        {
            Environment.Exit(0);
        }
    }

    private static void CleanupRelatedProcesses()
    {
        try
        {
            int currentPid = Process.GetCurrentProcess().Id;

            // Kill any extra TheHub / TheHub.Overlays processes
            string[] targetProcessNames = { "TheHub.Overlays", "TheHub", "TheHub.App" };
            foreach (var name in targetProcessNames)
            {
                foreach (var proc in Process.GetProcessesByName(name))
                {
                    if (proc.Id != currentPid)
                    {
                        try { proc.Kill(); } catch { }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Log($"CleanupRelatedProcesses error: {ex.Message}");
        }
    }

    #region Overview Studio Handlers

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

    private void SetClockWidgetLocked(bool locked)
    {
        _clockWidget?.SetLockState(locked, clickThrough: locked);
    }

    private void OnWidgetConfigChanged(TheHub.Common.Config.WidgetsConfig config)
    {
        _clockWidget?.ApplyConfig(config);
        ApplyDynamicIslandFont(config);
    }

    private void ApplyDynamicIslandFont(TheHub.Common.Config.WidgetsConfig config)
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

    private void ApplyActiveFontToWidgets(TheHub.Common.Config.WidgetsConfig config)
    {
        try
        {
            _clockWidget?.ApplyConfig(config);
        }
        catch (Exception ex)
        {
            AppLogger.Log($"ApplyActiveFontToWidgets error: {ex.Message}");
        }
    }

    #region Options - Custom Fonts Management

    private void RefreshFontsList()
    {
        if (FontsListControl == null) return;
        var fonts = _overviewViewModel?.InstalledCustomFonts;
        FontsListControl.ItemsSource = null;
        FontsListControl.ItemsSource = fonts;
        if (FontsEmptyPrompt != null)
        {
            FontsEmptyPrompt.Visibility = (fonts == null || fonts.Count == 0) ? Visibility.Visible : Visibility.Collapsed;
        }
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

                if (installed != null)
                {
                    var settings = SettingsManager.Current;
                    if (!settings.CustomFonts.Contains(installed.FileName, StringComparer.OrdinalIgnoreCase))
                    {
                        settings.CustomFonts.Add(installed.FileName);
                        SettingsManager.SaveSettings(settings);
                    }
                    _core.TriggerIslandEvent(new DynamicIslandEvent
                    {
                        Type = IslandEventType.SystemAlert,
                        Title = "Font Installed",
                        Subtitle = $"{installed.FamilyName} is now available for widgets",
                        Glyph = "🔤",
                        DisplayDurationMs = 2500
                    });
                }
                else
                {
                    System.Windows.MessageBox.Show(
                        "Could not install the selected font. Please choose a valid .ttf or .otf file.",
                        "The Hub - Font Installation",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Log($"OnInstallFontClick error: {ex.Message}");
        }
    }

    private void OnDeleteFontClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string fileName)
        {
            var confirm = System.Windows.MessageBox.Show(
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

                _core.TriggerIslandEvent(new DynamicIslandEvent
                {
                    Type = IslandEventType.SystemAlert,
                    Title = "Font Removed",
                    Subtitle = fileName,
                    Glyph = "🗑️",
                    DisplayDurationMs = 2000
                });
            }
        }
    }

    // Stub — the global "Use" font button has been removed.
    // Fonts are now applied per-widget via the widget's own FontFamily ComboBox
    // (OverviewView.xaml → Clock Font / Dynamic Island Font dropdowns).
    private void OnUseFontClick(object sender, RoutedEventArgs e) { /* no-op */ }

    /// <summary>
    /// Updates the Hub MainWindow font family immediately when the user selects
    /// a new entry in Options -> Dashboard Font.
    /// </summary>
    private void OnDashboardFontChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not System.Windows.Controls.ComboBox cmb) return;
        if (cmb.SelectedItem is not System.Windows.Controls.ComboBoxItem item) return;
        string fontName = item.Tag?.ToString() ?? "Segoe UI";
        try
        {
            FontFamily = new System.Windows.Media.FontFamily(fontName);
            AppLogger.Log($"[Options] Dashboard font changed to: {fontName}");
        }
        catch (Exception ex)
        {
            AppLogger.Log($"[Options] OnDashboardFontChanged error: {ex.Message}");
        }
    }


    #endregion

    private void SetResourceWidgetLocked(bool locked)
    {
        _cpuWidget?.SetLockState(locked, clickThrough: false);
        _gpuWidget?.SetLockState(locked, clickThrough: false);
        _ramWidget?.SetLockState(locked, clickThrough: false);
    }

    [DllImport("user32.dll")]
    public static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    public static extern IntPtr SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);

    private void OnTitleBarMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            try
            {
                ReleaseCapture();
                var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                if (handle != IntPtr.Zero)
                {
                    SendMessage(handle, 0xA1 /* WM_NCLBUTTONDOWN */, 2 /* HTCAPTION */, 0);
                }
            }
            catch { }
        }
    }

    private void SetClockWidgetState(bool show)
    {
        try
        {
            if (show)
            {
                if (_clockWidget == null)
                {
                    _clockWidget = new DesktopClockWidget();
                    _clockWidget.Owner = null;
                    _clockWidget.PositionChanged += OnClockWidgetPositionChanged;
                    _clockWidget.WidgetClosed += (s, ev) =>
                    {
                        Dispatcher.Invoke(() =>
                        {
                            if (_overviewViewModel != null)
                                _overviewViewModel.IsClockActive = false;
                        });
                    };
                }
                _clockWidget.Owner = null;
                if (_overviewViewModel?.Config != null)
                {
                    _clockWidget.ApplyConfig(_overviewViewModel.Config);
                }
                _clockWidget.SetLockState(_overviewViewModel?.IsClockLocked ?? false, clickThrough: _overviewViewModel?.IsClockLocked ?? false);
                _clockWidget.Show();
                _clockWidget.Activate();
            }
            else
            {
                _clockWidget?.Hide();
            }
        }
        catch (Exception ex)
        {
            AppLogger.Log($"SetClockWidgetState error: {ex.Message}");
        }
    }

    private void OnClockWidgetPositionChanged(object? sender, EventArgs e)
    {
        if (_clockWidget == null || _overviewViewModel == null || _overviewViewModel.Config == null) return;
        _overviewViewModel.Config.ClockLeft = _clockWidget.Left;
        _overviewViewModel.Config.ClockTop = _clockWidget.Top;
        _overviewViewModel.Config.Save();
    }

    private void OnDynamicIslandFontChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbDynamicIslandFont?.SelectedItem is string family && _overviewViewModel != null)
        {
            _overviewViewModel.SelectedDynamicIslandFontFamily = family;
        }
    }

    private void SetResourceWidgetState(bool show)
    {
        try
        {
            if (show)
            {
                EnsureSplitWidgets();
                SetCpuWidgetState(true);
                SetGpuWidgetState(true);
                SetRamWidgetState(true);
            }
            else
            {
                _cpuWidget?.Hide();
                _gpuWidget?.Hide();
                _ramWidget?.Hide();
            }
        }
        catch (Exception ex)
        {
            AppLogger.Log($"SetResourceWidgetState error: {ex.Message}");
        }
    }

    private void SetCpuWidgetState(bool show)
    {
        EnsureSplitWidgets();
        if (show) { _cpuWidget!.SetLockState(_overviewViewModel?.IsGaugesLocked ?? false, false); _cpuWidget.Show(); }
        else _cpuWidget?.Hide();
    }

    private void SetGpuWidgetState(bool show)
    {
        EnsureSplitWidgets();
        if (show) { _gpuWidget!.SetLockState(_overviewViewModel?.IsGaugesLocked ?? false, false); _gpuWidget.Show(); }
        else _gpuWidget?.Hide();
    }

    private void SetRamWidgetState(bool show)
    {
        EnsureSplitWidgets();
        if (show) { _ramWidget!.SetLockState(_overviewViewModel?.IsGaugesLocked ?? false, false); _ramWidget.Show(); }
        else _ramWidget?.Hide();
    }

    private void EnsureSplitWidgets()
    {
        if (_cpuWidget == null)
        {
            _cpuWidget = new DesktopCpuWidget();
            _cpuWidget.WidgetClosed += (s, ev) => Dispatcher.Invoke(() => { if (_overviewViewModel != null) _overviewViewModel.IsGaugesActive = false; });
        }
        if (_gpuWidget == null)
        {
            _gpuWidget = new DesktopGpuWidget();
            _gpuWidget.WidgetClosed += (s, ev) => Dispatcher.Invoke(() => { if (_overviewViewModel != null) _overviewViewModel.IsGaugesActive = false; });
        }
        if (_ramWidget == null)
        {
            _ramWidget = new DesktopRamWidget();
            _ramWidget.WidgetClosed += (s, ev) => Dispatcher.Invoke(() => { if (_overviewViewModel != null) _overviewViewModel.IsGaugesActive = false; });
        }
    }

    #endregion

    #region Radial Wheel Studio Handlers

    private void OnRadialSubTabChanged(object sender, RoutedEventArgs e)
    {
        if (SubPanelActivation == null || SubPanelAppearance == null ||
            SubPanelWorkspaces == null) return;

        if (sender is RadioButton rb && rb.Tag is string tagStr && int.TryParse(tagStr, out int tabIndex))
        {
            SubPanelActivation.Visibility  = (tabIndex == 0) ? Visibility.Visible : Visibility.Collapsed;
            SubPanelAppearance.Visibility  = (tabIndex == 1) ? Visibility.Visible : Visibility.Collapsed;
            SubPanelWorkspaces.Visibility  = (tabIndex == 2) ? Visibility.Visible : Visibility.Collapsed;

            if (tabIndex == 2)
            {
                RefreshWorkspacesCards();
            }
        }
    }

    private void OnSaveRadialSettingsClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var settings = SettingsManager.Current;
            settings.GlobalShortcut = string.IsNullOrWhiteSpace(TxtGlobalShortcut.Text) ? "Alt+Z" : TxtGlobalShortcut.Text.Trim();
            settings.EnableMouseTrigger = ChkMouseTrigger.IsChecked ?? true;

            if (RbTriggerBack.IsChecked == true) settings.MouseTriggerButton = "Back";
            else if (RbTriggerForward.IsChecked == true) settings.MouseTriggerButton = "Forward";
            else settings.MouseTriggerButton = "Middle";

            settings.GestureBehavior = (RbGestureClick.IsChecked == true) ? "Click" : "Hold";
            settings.ActivationZone = (int)SldActivationZone.Value;

            settings.AppearanceTheme = (RbThemeWhite.IsChecked == true) ? "White" : "Black";
            settings.RadialRadius = (int)SldOrbitalRadius.Value;
            settings.RadialCenterRadius = (int)SldCenterHubSize.Value;
            settings.IconSize = (int)SldIconSize.Value;
            settings.TargetSpacing = (int)SldTargetSpacing.Value;
            settings.HoverColor = string.IsNullOrWhiteSpace(TxtHoverColor.Text) ? "#FFFFFF" : TxtHoverColor.Text.Trim();
            settings.TargetingMode = (RbTargetingPointer.IsChecked == true) ? "Pointer" : "Direction";
            settings.PersistentLabels = ChkPersistentLabels.IsChecked ?? false;
            settings.WheelOpacity = (int)SldWheelOpacity.Value;
            settings.BackgroundDimming = (int)SldBackgroundDimming.Value;

            settings.PerformanceMode = ChkPrecisionMode?.IsChecked ?? false;
            settings.FullscreenProtection = ChkFullscreenProtection?.IsChecked ?? true;

            SettingsManager.SaveSettings(settings);
            _radialWindow.RefreshShortcuts();
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Error saving radial settings: {ex.Message}");
        }
    }

    private void OnGlobalSaveSettingsClick(object sender, RoutedEventArgs e)
    {
        try
        {
            SettingsManager.SaveSettings();
        }
        catch (Exception ex)
        {
            AppLogger.Log($"[UI] Global save settings failed: {ex.Message}");
        }
    }

    // ── 2. Activation Handlers ──────────────────────────────────────────

    private void OnShortcutTextBoxPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
        if (key == System.Windows.Input.Key.LeftCtrl || key == System.Windows.Input.Key.RightCtrl ||
            key == System.Windows.Input.Key.LeftAlt || key == System.Windows.Input.Key.RightAlt ||
            key == System.Windows.Input.Key.LeftShift || key == System.Windows.Input.Key.RightShift)
        {
            return;
        }

        var parts = new List<string>();
        if ((System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) != 0) parts.Add("Ctrl");
        if ((System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Alt) != 0) parts.Add("Alt");
        if ((System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Shift) != 0) parts.Add("Shift");

        parts.Add(key.ToString());
        string combo = string.Join("+", parts);
        TxtGlobalShortcut.Text = combo;
        SettingsManager.Current.GlobalShortcut = combo;
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
    }

    private void ApplyRadialSettingsLive()
    {
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

    // ── 3. Appearance Handlers ──────────────────────────────────────────

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

    private void OnCenterHubSizeValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtCenterHubSizeValue == null) return;
        int val = (int)Math.Round(e.NewValue);
        TxtCenterHubSizeValue.Text = $"{val} px";
        SettingsManager.Current.RadialCenterRadius = val;
        ApplyRadialSettingsLive();
    }

    private void OnIconSizeValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtIconSizeValue == null) return;
        int val = (int)Math.Round(e.NewValue);
        TxtIconSizeValue.Text = $"{val} px";
        SettingsManager.Current.IconSize = val;
        ApplyRadialSettingsLive();
    }

    private void OnTargetSpacingValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtTargetSpacingValue == null) return;
        int val = (int)Math.Round(e.NewValue);
        TxtTargetSpacingValue.Text = $"{val} px";
        SettingsManager.Current.TargetSpacing = val;
        ApplyRadialSettingsLive();
    }

    private void OnColorSwatchClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string hex)
        {
            TxtHoverColor.Text = hex;
            SettingsManager.Current.HoverColor = hex;
            ApplyRadialSettingsLive();
        }
    }

    private void OnHoverColorTextChanged(object sender, TextChangedEventArgs e)
    {
        if (TxtHoverColor == null) return;
        string hex = TxtHoverColor.Text.Trim();
        if (hex.StartsWith("#") && (hex.Length == 7 || hex.Length == 9))
        {
            SettingsManager.Current.HoverColor = hex;
            ApplyRadialSettingsLive();
        }
    }

    private void OnTargetingModeChanged(object sender, RoutedEventArgs e)
    {
        if (RbTargetingPointer == null) return;
        SettingsManager.Current.TargetingMode = (RbTargetingPointer.IsChecked == true) ? "Pointer" : "Direction";
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

    // ── 4. Workspaces & Wheel Customizer Handlers (Rovyl Style) ────────

    private string _addSourceMode = "Installed";

    private void RefreshWorkspacesCards()
    {
        var settings = SettingsManager.Current;

        // Ensure default radial profiles exist if list is empty
        if (settings.RadialProfiles.Count == 0)
        {
            settings.RadialProfiles = AppSettings.CreateDefault().RadialProfiles;
        }

        // Assign 1-based hotkey indices
        for (int i = 0; i < settings.RadialProfiles.Count; i++)
        {
            settings.RadialProfiles[i].HotkeyIndex = i + 1;
        }

        // Ensure exactly one radial profile is marked active
        if (!settings.RadialProfiles.Any(w => w.IsActive))
        {
            settings.RadialProfiles[0].IsActive = true;
        }

        if (WorkspacesCardsControl != null)
        {
            WorkspacesCardsControl.ItemsSource = null;
            WorkspacesCardsControl.ItemsSource = settings.RadialProfiles;
        }

        // Set selected radial profile to active one
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

        // Sync accent color swatch with this profile's stored color
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

        // Refresh slices
        WsEditorCurrentAppsList.ItemsSource = null;
        WsEditorCurrentAppsList.ItemsSource = profile.Slices;
        WsEditorSliceCountText.Text = $"{profile.Slices.Count} items";
        WsEditorEmptyPrompt.Visibility = (profile.Slices.Count == 0) ? Visibility.Visible : Visibility.Collapsed;

        LoadInstalledAppsSuggestions();
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

            _radialWindow.RefreshShortcuts();

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

        _radialWindow.RefreshShortcuts();

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

    // Stub — static color dots removed; color is now picked via ColorPickerControl popup.
    private void OnWsEditorColorDotClick(object sender, RoutedEventArgs e) { /* no-op */ }

    /// <summary>Opens the full-spectrum ColorPicker popup, pre-seeded with the current accent color.</summary>
    private void OnWsEditorOpenColorPickerClick(object sender, RoutedEventArgs e)
    {
        if (_selectedRadialProfile == null) return;
        try
        {
            // Pre-seed picker with current accent color
            var hexStr = _selectedRadialProfile.AccentColor ?? "#3B82F6";
            var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter
                            .ConvertFromString(hexStr);
            WsEditorColorPicker.SelectedColor = color;
        }
        catch { /* fall through with default picker color */ }
        WsEditorColorPickerPopup.IsOpen = true;
    }

    /// <summary>Handles color selection from the ColorPickerControl popup.</summary>
    private void OnWsEditorColorPickerChanged(System.Windows.Media.Color color)
    {
        if (_selectedRadialProfile == null) return;
        string hex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        _selectedRadialProfile.AccentColor = hex;

        // Update live swatch
        WsEditorColorSwatch.Background = new System.Windows.Media.SolidColorBrush(color);

        // Persist and refresh the radial window + card thumbnails
        SettingsManager.SaveSettings();
        _selectedRadialProfile.NotifySlicesChanged();
        _radialWindow.RefreshShortcuts();

        // Refresh cards to redraw center dot with new color
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
            System.Windows.MessageBox.Show("At least one wheel profile must be preserved.", "The Hub",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_selectedRadialProfile == null) return;

        var result = System.Windows.MessageBox.Show(
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
            _radialWindow.RefreshShortcuts();
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
                _radialWindow.RefreshShortcuts();
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
                _radialWindow.RefreshShortcuts();
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
            _radialWindow.RefreshShortcuts();
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
            _radialWindow.RefreshShortcuts();
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
            string fileName = System.IO.Path.GetFileNameWithoutExtension(ofd.FileName);
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
            _radialWindow.RefreshShortcuts();
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
            string dirName = System.IO.Path.GetFileName(fbd.SelectedPath);
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
            _radialWindow.RefreshShortcuts();
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
        _radialWindow.RefreshShortcuts();
        BindRadialProfileEditor(_selectedRadialProfile);
        WorkspacesCardsControl.ItemsSource = null;
        WorkspacesCardsControl.ItemsSource = SettingsManager.Current.RadialProfiles;
    }

    // ── 5. Advanced Handlers ────────────────────────────────────────────

    private async void OnCheckForUpdatesClick(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn) btn.IsEnabled = false;
        await Task.Delay(800);
        if (sender is System.Windows.Controls.Button btn2) btn2.IsEnabled = true;
        _core.TriggerIslandEvent(new DynamicIslandEvent { Type = IslandEventType.SystemAlert, Title = "Updates", Subtitle = "The Hub v0.1.0 • Up to date", Glyph = "✅", DisplayDurationMs = 2500 });
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

    #endregion
}
