using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Runtime.InteropServices;
using TheHub.Common.Config;
using TheHub.Common.Models;
using TheHub.Core.Native;

namespace TheHub.Overlays.RadialLauncher;

public partial class RadialWindow : Window
{
    private const int WM_NCHITTEST = 0x0084;
    private const int HTTRANSPARENT = -1;
    private const int HTCLIENT = 1;

    public event Action<AppShortcut>? ShortcutSelected;
    /// <summary>Fired when user presses a number key 1-9 to switch workspace.</summary>
    public event Action<int>? WorkspaceSwitchRequested;
    /// <summary>Fired whenever the radial wheel is closed or dismissed.</summary>
    public event Action? Dismissed;

    private IntPtr _prevForegroundWindow = IntPtr.Zero;
    private DateTime _openTime = DateTime.MinValue;

    /// <summary>Gesture behavior: "Click" keeps wheel open, "Hold" launches on release.</summary>
    private string _gestureBehavior = "Hold";

    public RadialWindow()
    {
        InitializeComponent();
        RefreshShortcuts();

        Deactivated += (s, e) => Dismiss();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;

        int exStyle = Win32Api.GetWindowLong(hwnd, Win32Api.GWL_EXSTYLE);
        Win32Api.SetWindowLong(hwnd, Win32Api.GWL_EXSTYLE,
            exStyle | Win32Api.WS_EX_TOOLWINDOW | Win32Api.WS_EX_NOACTIVATE);

        var source = HwndSource.FromHwnd(hwnd);
        source?.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Win32Api.WM_MOUSEACTIVATE)
        {
            handled = true;
            return (IntPtr)Win32Api.MA_NOACTIVATE;
        }

        if (msg == WM_NCHITTEST)
        {
            try
            {
                int screenX = unchecked((short)(lParam.ToInt64() & 0xFFFF));
                int screenY = unchecked((short)((lParam.ToInt64() >> 16) & 0xFFFF));

                Point screenPt = new Point(screenX, screenY);
                Point windowPt = PointFromScreen(screenPt);

                double centerX = ActualWidth / 2.0;
                double centerY = ActualHeight / 2.0;
                double dx = windowPt.X - centerX;
                double dy = windowPt.Y - centerY;
                double dist = Math.Sqrt(dx * dx + dy * dy);

                // Accept clicks within the outer radius + generous padding for tiles & pill
                double maxRadius = (WheelCanvas != null && WheelCanvas.OuterRadius > 0)
                    ? WheelCanvas.OuterRadius + 90
                    : ActualWidth / 2.0;

                if (dist <= maxRadius || (WheelCanvas != null && windowPt.Y > ActualHeight / 2.0 + WheelCanvas.OuterRadius + 30))
                {
                    handled = true;
                    return (IntPtr)HTCLIENT;
                }

                handled = true;
                return (IntPtr)HTTRANSPARENT;
            }
            catch
            {
                handled = true;
                return (IntPtr)HTTRANSPARENT;
            }
        }

        return IntPtr.Zero;
    }

    public void UpdateAppearance(AppSettings settings)
    {
        WheelCanvas.OuterRadius = settings.RadialRadius;
        // User configurable center disc radius (default 60px -> 120px diameter)
        WheelCanvas.InnerRadius = settings.RadialCenterRadius > 0 ? settings.RadialCenterRadius : 60.0;
        WheelCanvas.IconSize = settings.IconSize;
        WheelCanvas.TargetSpacing = settings.TargetSpacing;
        WheelCanvas.HoverColorHex = string.IsNullOrWhiteSpace(settings.HoverColor) ? "#FFFFFF" : settings.HoverColor;
        WheelCanvas.AppearanceTheme = settings.AppearanceTheme ?? "Black";
        WheelCanvas.PersistentLabels = settings.PersistentLabels;
        WheelCanvas.WheelOpacityFactor = Math.Clamp(settings.WheelOpacity / 100.0, 0.35, 1.0);
        _gestureBehavior = settings.GestureBehavior ?? "Hold";

        WheelCanvas.InvalidateVisual();
    }

    public void RefreshShortcuts()
    {
        var settings = SettingsManager.Current;

        // 1. Resolve Active Radial Wheel Profile (Dedicated to Radial Launcher, independent of Workspaces)
        var activeProfile = settings.RadialProfiles.FirstOrDefault(p => p.IsActive)
                            ?? settings.RadialProfiles.FirstOrDefault();

        if (activeProfile != null)
        {
            // CRITICAL: Slices are 1:1. If 0 shortcuts configured, it shows 0 shortcuts (no ghost/fake fallback).
            WheelCanvas.Shortcuts = activeProfile.Slices?.ToList() ?? new List<AppShortcut>();

            if (!string.IsNullOrWhiteSpace(activeProfile.AccentColor))
            {
                try
                {
                    var c = (Color)ColorConverter.ConvertFromString(activeProfile.AccentColor);
                    WheelCanvas.CenterColor = new SolidColorBrush(c);
                }
                catch
                {
                    WheelCanvas.CenterColor = Brushes.White;
                }
            }
            else
            {
                WheelCanvas.CenterColor = Brushes.White;
            }
        }
        else
        {
            WheelCanvas.Shortcuts = new List<AppShortcut>();
            WheelCanvas.CenterColor = Brushes.White;
        }

        UpdateAppearance(settings);
    }

    public void ShowAt(int screenX, int screenY)
    {
        // Prevent duplicate animation or re-trigger flicker while shortcut is held down
        if (IsVisible) return;

        IntPtr currentFg = Win32Api.GetForegroundWindow();
        var myHwnd = new WindowInteropHelper(this).Handle;
        if (currentFg != IntPtr.Zero && currentFg != myHwnd)
        {
            _prevForegroundWindow = currentFg;
        }

        RefreshShortcuts();

        // Use monitor-aware DIP conversion for correct multi-monitor positioning
        var dipPt = PhysicalToDip(screenX, screenY);
        double dipX = dipPt.X;
        double dipY = dipPt.Y;

        Left = dipX - (Width / 2.0);
        Top = dipY - (Height / 2.0);
        WheelCanvas.HighlightedIndex = -1;
        WheelCanvas.SelectedLabel = "Center";
        _openTime = DateTime.UtcNow;

        // Background Dimming — pass physical coords so dimmer can find the correct monitor
        var settings = SettingsManager.Current;
        if (settings.BackgroundDimming > 0)
        {
            RadialDimmerOverlay.Instance.ShowDimmer(settings.BackgroundDimming, screenX, screenY);
        }

        // Start bloom animation
        WheelCanvas.BloomProgress = 0.0;
        Opacity = 0.0;
        Show();
        AnimateBloom();
    }

    private void AnimateBloom()
    {
        var animation = new DoubleAnimation
        {
            From = 0.0,
            To = 1.0,
            Duration = TimeSpan.FromMilliseconds(220),
            EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
        };

        WheelCanvas.BeginAnimation(RadialWheelCanvas.BloomProgressProperty, animation);

        var opacityAnim = new DoubleAnimation
        {
            From = 0.0,
            To = 1.0,
            Duration = TimeSpan.FromMilliseconds(160),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        BeginAnimation(UIElement.OpacityProperty, opacityAnim);
    }

    public void UpdatePointer(int screenX, int screenY)
    {
        if (!IsVisible) return;

        var dipPt = PhysicalToDip(screenX, screenY);
        double relX = dipPt.X - Left;
        double relY = dipPt.Y - Top;
        UpdateHighlight(relX, relY);
    }

    /// <summary>
    /// Converts physical screen coordinates (from low-level mouse hook) to WPF DIPs
    /// using the DPI of the monitor that contains the given point.
    /// </summary>
    private static Point PhysicalToDip(int physX, int physY)
    {
        // Query which monitor the point falls on (physical coords)
        var pt = new Win32Api.POINT { x = physX, y = physY };
        IntPtr hMonitor = Win32Api.MonitorFromPoint(pt, Win32Api.MONITOR_DEFAULTTONEAREST);

        var mi = new Win32Api.MONITORINFO();
        mi.cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(Win32Api.MONITORINFO));
        if (Win32Api.GetMonitorInfo(hMonitor, ref mi))
        {
            GetDpiForMonitor(hMonitor, 0, out uint dpiX, out uint dpiY);
            double scaleX = dpiX > 0 ? dpiX / 96.0 : 1.0;
            double scaleY = dpiY > 0 ? dpiY / 96.0 : scaleX;
            return new Point(mi.rcMonitor.Left / scaleX + (physX - mi.rcMonitor.Left) / scaleX,
                             mi.rcMonitor.Top / scaleY + (physY - mi.rcMonitor.Top) / scaleY);
        }

        // Fallback: use primary screen DPI
        double cx = Win32Api.GetSystemMetrics(Win32Api.SM_CXSCREEN);
        double scale = cx > 0 ? cx / SystemParameters.PrimaryScreenWidth : 1.0;
        return new Point(physX / scale, physY / scale);
    }

    [DllImport("Shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hmonitor, uint dpiType, out uint dpiX, out uint dpiY);

    public void Dismiss()
    {
        if (!IsVisible) return;

        RadialDimmerOverlay.Instance.HideDimmer();

        WheelCanvas.BeginAnimation(RadialWheelCanvas.BloomProgressProperty, null);
        BeginAnimation(UIElement.OpacityProperty, null);
        Opacity = 1.0;
        Hide();

        if (_prevForegroundWindow != IntPtr.Zero)
        {
            Win32Api.SetForegroundWindow(_prevForegroundWindow);
            _prevForegroundWindow = IntPtr.Zero;
        }

        try { Dismissed?.Invoke(); } catch { }
    }

    public void TriggerSelectionAndClose()
    {
        if (!IsVisible) return;

        int index = WheelCanvas.HighlightedIndex;
        var list = WheelCanvas.Shortcuts;

        RadialDimmerOverlay.Instance.HideDimmer();

        WheelCanvas.BeginAnimation(RadialWheelCanvas.BloomProgressProperty, null);
        BeginAnimation(UIElement.OpacityProperty, null);
        Opacity = 1.0;
        Hide();

        if (_prevForegroundWindow != IntPtr.Zero)
        {
            Win32Api.SetForegroundWindow(_prevForegroundWindow);
            _prevForegroundWindow = IntPtr.Zero;
        }

        try { Dismissed?.Invoke(); } catch { }

        if (index >= 0 && index < list.Count)
        {
            var selected = list[index];
            try { ShortcutSelected?.Invoke(selected); } catch { }
        }
    }

    private void UpdateHighlight(double relX, double relY)
    {
        double centerX = ActualWidth / 2.0;
        double centerY = ActualHeight / 2.0;

        double dx = relX - centerX;
        double dy = relY - centerY;
        double dist = Math.Sqrt(dx * dx + dy * dy);

        int count = WheelCanvas.Shortcuts?.Count ?? 0;
        var settings = SettingsManager.Current;

        // Accurate Deadzone:
        // When ActivationZone slider is 120, deadzone matches the center hub/logo (InnerRadius) 100%!
        double hubRadius = WheelCanvas.InnerRadius > 0 ? WheelCanvas.InnerRadius : 60.0;
        double activationZone = settings.ActivationZone > 0 ? (settings.ActivationZone / 120.0) * hubRadius : hubRadius;

        if (count == 0 || dist < activationZone)
        {
            WheelCanvas.HighlightedIndex = -1;
            WheelCanvas.SelectedLabel = "Center";
            return;
        }

        // Pointer mode: Direct hitbox collision detection
        if (string.Equals(settings.TargetingMode, "Pointer", StringComparison.OrdinalIgnoreCase))
        {
            double angleStep = 360.0 / count;
            double effectiveRadius = WheelCanvas.OuterRadius + (WheelCanvas.TargetSpacing - 10.0);
            double tileDim = Math.Clamp(WheelCanvas.IconSize > 0 ? WheelCanvas.IconSize : 64.0, 36.0, 92.0);
            double halfTile = (tileDim * 1.1) / 2.0;

            int foundIndex = -1;
            for (int i = 0; i < count; i++)
            {
                double aDeg = (i * angleStep) - 90.0;
                double aRad = aDeg * Math.PI / 180.0;
                double tx = centerX + effectiveRadius * Math.Cos(aRad);
                double ty = centerY + effectiveRadius * Math.Sin(aRad);

                if (relX >= tx - halfTile && relX <= tx + halfTile &&
                    relY >= ty - halfTile && relY <= ty + halfTile)
                {
                    foundIndex = i;
                    break;
                }
            }

            WheelCanvas.HighlightedIndex = foundIndex;
            if (foundIndex >= 0 && foundIndex < count)
            {
                WheelCanvas.SelectedLabel = WheelCanvas.Shortcuts![foundIndex].Title;
            }
            else
            {
                WheelCanvas.SelectedLabel = "Center";
            }
            return;
        }

        // Direction mode: Polar angle vector
        double angleRad = Math.Atan2(dy, dx);
        double angleDeg = angleRad * (180.0 / Math.PI) + 90.0;
        if (angleDeg < 0) angleDeg += 360.0;

        double step = 360.0 / count;
        int index = (int)Math.Round(angleDeg / step) % count;

        WheelCanvas.HighlightedIndex = index;

        // Update bottom pill label
        if (index >= 0 && index < (WheelCanvas.Shortcuts?.Count ?? 0))
        {
            WheelCanvas.SelectedLabel = WheelCanvas.Shortcuts![index].Title;
        }
    }

    private void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        var pos = e.GetPosition(this);
        UpdateHighlight(pos.X, pos.Y);
    }

    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if ((DateTime.UtcNow - _openTime).TotalMilliseconds < 150) return;

        if (string.Equals(_gestureBehavior, "Click", StringComparison.OrdinalIgnoreCase))
        {
            if (WheelCanvas.HighlightedIndex >= 0)
            {
                TriggerSelectionAndClose();
                e.Handled = true;
            }
            else
            {
                Dismiss();
                e.Handled = true;
            }
        }
    }

    private void OnPreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (string.Equals(_gestureBehavior, "Click", StringComparison.OrdinalIgnoreCase))
        {
            // In Click mode, releasing the initial opening trigger button (< 300ms) MUST be ignored!
            if ((DateTime.UtcNow - _openTime).TotalMilliseconds < 300)
            {
                return;
            }

            if (WheelCanvas.HighlightedIndex >= 0)
            {
                TriggerSelectionAndClose();
                e.Handled = true;
            }
            else
            {
                Dismiss();
                e.Handled = true;
            }
            return;
        }

        // In "Hold" mode, releasing the mouse trigger executes the selected item (or dismisses if in deadzone)
        if (WheelCanvas.HighlightedIndex >= 0)
        {
            TriggerSelectionAndClose();
            e.Handled = true;
        }
        else
        {
            Dismiss();
            e.Handled = true;
        }
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Dismiss();
            return;
        }

        // Number keys 1-9 for workspace switching
        int wsIndex = e.Key switch
        {
            Key.D1 or Key.NumPad1 => 0,
            Key.D2 or Key.NumPad2 => 1,
            Key.D3 or Key.NumPad3 => 2,
            Key.D4 or Key.NumPad4 => 3,
            Key.D5 or Key.NumPad5 => 4,
            Key.D6 or Key.NumPad6 => 5,
            Key.D7 or Key.NumPad7 => 6,
            Key.D8 or Key.NumPad8 => 7,
            Key.D9 or Key.NumPad9 => 8,
            _ => -1
        };

        if (wsIndex >= 0 && wsIndex < SettingsManager.Current.RadialProfiles.Count)
        {
            for (int i = 0; i < SettingsManager.Current.RadialProfiles.Count; i++)
            {
                SettingsManager.Current.RadialProfiles[i].IsActive = (i == wsIndex);
            }
            SettingsManager.SaveSettings();
            RefreshShortcuts();
            WheelCanvas.BloomProgress = 0.0;
            AnimateBloom();
            try { WorkspaceSwitchRequested?.Invoke(wsIndex); } catch { }
        }
    }
}
