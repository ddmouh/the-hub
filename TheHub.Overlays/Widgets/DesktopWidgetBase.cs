using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace TheHub.Overlays.Widgets;

/// \u003csummary\u003e
/// Production-ready base window for all draggable desktop widgets.
/// Enforces transparent, non-taskbar, borderless window metrics with smooth dragging.
/// \u003c/summary\u003e
public class DesktopWidgetBase : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_LAYERED = 0x00080000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    public bool IsPositionLocked { get; set; } = false;
    public bool IsClickThroughEnabled { get; private set; } = false;
    public event EventHandler? WidgetClosed;

    public void SetClickThrough(bool enable)
    {
        IsClickThroughEnabled = enable;
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        int currentExStyle = GetWindowLong(handle, GWL_EXSTYLE);
        if (enable)
        {
            SetWindowLong(handle, GWL_EXSTYLE, currentExStyle | WS_EX_TRANSPARENT | WS_EX_LAYERED);
        }
        else
        {
            SetWindowLong(handle, GWL_EXSTYLE, currentExStyle & ~WS_EX_TRANSPARENT);
        }
    }

    public virtual void SetLockState(bool locked, bool clickThrough = true)
    {
        IsPositionLocked = locked;
        SetClickThrough(locked && clickThrough);
        if (locked)
        {
            Topmost = false;
            PinToDesktopBottom();
        }
    }

    private const int WM_NCLBUTTONDOWN = 0xA1;
    private const int HTCAPTION = 2;

    [DllImport("user32.dll")]
    public static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    public static extern IntPtr SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    public static readonly IntPtr HWND_BOTTOM = new IntPtr(1);
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOACTIVATE = 0x0010;

    public void PinToDesktopBottom()
    {
        Topmost = false;
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero)
        {
            SetWindowPos(handle, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE);
        }
    }

    public DesktopWidgetBase()
    {
        Owner = null;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = false;
        ResizeMode = ResizeMode.NoResize;
        ShowActivated = false;
        WindowStartupLocation = WindowStartupLocation.Manual;

        Loaded += (s, e) => PinToDesktopBottom();
        Deactivated += (s, e) => PinToDesktopBottom();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero)
        {
            int currentExStyle = GetWindowLong(handle, GWL_EXSTYLE);
            // WS_EX_TOOLWINDOW: Hide from Alt+Tab
            SetWindowLong(handle, GWL_EXSTYLE, currentExStyle | WS_EX_TOOLWINDOW);
        }
        PinToDesktopBottom();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);

        // Allow dragging unless locked or clicking interactive sub-controls
        if (!IsPositionLocked && e.ButtonState == MouseButtonState.Pressed)
        {
            // If user clicked directly on an interactive button or slider, don't initiate drag
            if (e.OriginalSource is DependencyObject dep)
            {
                var parent = FindInteractiveAncestor(dep);
                if (parent != null)
                {
                    return;
                }
            }

            try
            {
                ReleaseCapture();
                var handle = new WindowInteropHelper(this).Handle;
                if (handle != IntPtr.Zero)
                {
                    SendMessage(handle, WM_NCLBUTTONDOWN, HTCAPTION, 0);
                    PinToDesktopBottom();
                }
            }
            catch { }
        }
    }

    private static DependencyObject? FindInteractiveAncestor(DependencyObject current)
    {
        while (current != null)
        {
            if (current is System.Windows.Controls.Primitives.ButtonBase ||
                current is System.Windows.Controls.TextBox ||
                current is System.Windows.Controls.Slider ||
                current is System.Windows.Controls.ComboBox)
            {
                return current;
            }
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    public virtual void CloseWidget()
    {
        WidgetClosed?.Invoke(this, EventArgs.Empty);
        Hide();
    }

    public virtual void PausePolling()
    {
    }

    public virtual void ResumePolling()
    {
    }
}
