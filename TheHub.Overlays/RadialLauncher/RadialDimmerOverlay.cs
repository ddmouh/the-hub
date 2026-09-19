using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using TheHub.Core.Native;

namespace TheHub.Overlays.RadialLauncher;

/// <summary>
/// A hardware-accelerated, transparent, click-through overlay window that provides
/// full screen backdrop dimming when the Radial Wheel is invoked.
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

    public void ShowDimmer(double opacityPercent, double centerX, double centerY)
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

            // Cover the virtual screen (all monitors or primary)
            Left = SystemParameters.VirtualScreenLeft;
            Top = SystemParameters.VirtualScreenTop;
            Width = SystemParameters.VirtualScreenWidth;
            Height = SystemParameters.VirtualScreenHeight;

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
}
