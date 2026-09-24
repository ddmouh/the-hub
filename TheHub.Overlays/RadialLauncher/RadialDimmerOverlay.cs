using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using TheHub.Core.Native;

namespace TheHub.Overlays.RadialLauncher;

/// <summary>
/// A hardware-accelerated, transparent, click-through overlay window that provides
/// per-monitor backdrop dimming when the Radial Wheel is invoked.
/// Restricted to the monitor where the cursor is, not the full virtual screen.
/// </summary>
public class RadialDimmerOverlay : Window
{
    private static RadialDimmerOverlay? _instance;
    public static RadialDimmerOverlay Instance => _instance ??= new RadialDimmerOverlay();

    private bool _isInitialized;

    public RadialDimmerOverlay()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Black;
        ShowInTaskbar = false;
        Focusable = false;
        IsHitTestVisible = false;
        Topmost = true;
        Opacity = 0.0;
        Visibility = Visibility.Hidden;
    }

    /// <summary>
    /// Shows the dimmer overlay covering only the monitor that contains (screenX, screenY) in physical pixels.
    /// </summary>
    public void ShowDimmer(double opacityPercent, double screenPhysX, double screenPhysY)
    {
        Dispatcher.Invoke(() =>
        {
            if (opacityPercent <= 0)
            {
                HideDimmer();
                return;
            }

            if (!_isInitialized)
            {
                _isInitialized = true;
                Show();
                ApplyWindowStyles();
            }

            // Determine which monitor contains the cursor (physical pixel coordinates)
            var pt = new Win32Api.POINT { x = (int)screenPhysX, y = (int)screenPhysY };
            IntPtr hMonitor = MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST);

            var mi = new Win32Api.MONITORINFO();
            mi.cbSize = Marshal.SizeOf(typeof(Win32Api.MONITORINFO));
            if (GetMonitorInfo(hMonitor, ref mi))
            {
                GetDpiForMonitor(hMonitor, 0, out uint dpiX, out uint dpiY);
                double dpiScaleX = dpiX > 0 ? dpiX / 96.0 : GetDpiScaleX();
                double dpiScaleY = dpiY > 0 ? dpiY / 96.0 : GetDpiScaleY();

                Left   = mi.rcMonitor.Left   / dpiScaleX;
                Top    = mi.rcMonitor.Top    / dpiScaleY;
                Width  = (mi.rcMonitor.Right  - mi.rcMonitor.Left) / dpiScaleX;
                Height = (mi.rcMonitor.Bottom - mi.rcMonitor.Top)  / dpiScaleY;
            }
            else
            {
                // Fallback: primary screen only
                Left   = 0;
                Top    = 0;
                Width  = SystemParameters.PrimaryScreenWidth;
                Height = SystemParameters.PrimaryScreenHeight;
            }

            Opacity = Math.Clamp(opacityPercent / 100.0, 0.0, 0.85);
            Visibility = Visibility.Visible;
            ApplyWindowStyles();
        });
    }

    public void HideDimmer()
    {
        Dispatcher.Invoke(() =>
        {
            Opacity = 0.0;
            Visibility = Visibility.Hidden;
        });
    }

    private void ApplyWindowStyles()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        int ex = Win32Api.GetWindowLong(hwnd, Win32Api.GWL_EXSTYLE);
        Win32Api.SetWindowLong(hwnd, Win32Api.GWL_EXSTYLE,
            ex | Win32Api.WS_EX_TRANSPARENT | Win32Api.WS_EX_LAYERED | Win32Api.WS_EX_TOOLWINDOW | Win32Api.WS_EX_NOACTIVATE);
    }

    // ── Native helpers (local, no changes to Win32Api.cs needed) ──────────────

    private const int MONITOR_DEFAULTTONEAREST = 2;

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(Win32Api.POINT pt, int dwFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref Win32Api.MONITORINFO lpmi);

    /// <summary>Returns the system DPI X scale (physical pixels per DIP).</summary>
    private static double GetDpiScaleX()
    {
        // SystemParameters.PrimaryScreenWidth is already in DIPs; GetSystemMetrics gives physical pixels.
        double physical = Win32Api.GetSystemMetrics(Win32Api.SM_CXSCREEN);
        double dip = SystemParameters.PrimaryScreenWidth;
        return dip > 0 ? physical / dip : 1.0;
    }

    private static double GetDpiScaleY()
    {
        double physical = Win32Api.GetSystemMetrics(Win32Api.SM_CYSCREEN);
        double dip = SystemParameters.PrimaryScreenHeight;
        return dip > 0 ? physical / dip : 1.0;
    }

    [DllImport("Shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hmonitor, uint dpiType, out uint dpiX, out uint dpiY);
}
