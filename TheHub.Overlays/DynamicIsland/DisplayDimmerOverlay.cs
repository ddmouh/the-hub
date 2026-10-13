using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using TheHub.Core.Bridge;
using TheHub.Core.Native;

namespace TheHub.Overlays.DynamicIsland;

/// <summary>
/// A hardware-accelerated, transparent, click-through overlay window that provides
/// 100% guaranteed screen dimming across all displays without relying on DDC/CI or GPU drivers.
/// </summary>
public class DisplayDimmerOverlay : Window
{
    private static DisplayDimmerOverlay? _instance;
    public static DisplayDimmerOverlay Instance => _instance ??= new DisplayDimmerOverlay();

    private bool _isInitialized;

    public DisplayDimmerOverlay()
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

    public void Initialize()
    {
        if (_isInitialized) return;
        _isInitialized = true;
        UpdateBounds();
        Show();
        ApplyWindowStyles();
        Visibility = Visibility.Hidden;
    }

    private void ApplyWindowStyles()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        int ex = Win32Api.GetWindowLong(hwnd, Win32Api.GWL_EXSTYLE);
        Win32Api.SetWindowLong(hwnd, Win32Api.GWL_EXSTYLE,
            ex | Win32Api.WS_EX_TRANSPARENT | Win32Api.WS_EX_LAYERED | Win32Api.WS_EX_TOOLWINDOW | Win32Api.WS_EX_NOACTIVATE);
    }

    public void UpdateBounds(MonitorItem? targetMonitor = null, Window? referenceWindow = null)
    {
        var mon = targetMonitor ?? DisplayMonitorHelper.GetTargetMonitor(TheHub.Common.Config.SettingsManager.Current.IslandMonitorIndex);
        
        var dpi = VisualTreeHelper.GetDpi(referenceWindow ?? this);
        double dpiX = dpi.DpiScaleX > 0 ? dpi.DpiScaleX : 1.0;
        double dpiY = dpi.DpiScaleY > 0 ? dpi.DpiScaleY : 1.0;

        Left = mon.Bounds.Left / dpiX;
        Top = mon.Bounds.Top / dpiY;
        Width = (mon.Bounds.Right - mon.Bounds.Left) / dpiX;
        Height = (mon.Bounds.Bottom - mon.Bounds.Top) / dpiY;
    }

    public void SetBrightness(int brightnessPercent, MonitorItem? targetMonitor = null, Window? topWindowToPreserve = null)
    {
        _ = UiBridge.Instance.InvokeAsync(() =>
        {
            brightnessPercent = Math.Clamp(brightnessPercent, 10, 100);
            if (brightnessPercent >= 99)
            {
                Opacity = 0.0;
                Visibility = Visibility.Hidden;
            }
            else
            {
                UpdateBounds(targetMonitor, topWindowToPreserve);
                if (Visibility != Visibility.Visible)
                {
                    Visibility = Visibility.Visible;
                    ApplyWindowStyles();
                }

                // Smoothly dim from 100% (0.0 opacity) down to 10% (0.75 opacity)
                double maxDim = 0.75;
                double dim = (100 - brightnessPercent) / 90.0 * maxDim;
                Opacity = Math.Clamp(dim, 0.0, maxDim);

                // Ensure preserving the topWindow (like DynamicIsland) above dimmer
                if (topWindowToPreserve != null)
                {
                    var topHwnd = new WindowInteropHelper(topWindowToPreserve).Handle;
                    if (topHwnd != IntPtr.Zero)
                    {
                        Win32Api.SetWindowPos(topHwnd, Win32Api.HWND_TOPMOST, 0, 0, 0, 0,
                            Win32Api.SWP_NOMOVE | Win32Api.SWP_NOSIZE | Win32Api.SWP_NOACTIVATE);
                    }
                }
            }
        });
    }
}
