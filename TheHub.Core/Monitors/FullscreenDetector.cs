using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using TheHub.Common.Config;
using TheHub.Core.Native;

namespace TheHub.Core.Monitors;

public class FullscreenDetector : IDisposable
{
    private readonly System.Threading.Timer _timer;
    private bool _isFullscreen;
    private readonly uint _currentProcessId;

    public event Action<bool>? FullscreenChanged;

    public bool IsFullscreen => _isFullscreen;

    public FullscreenDetector(int pollIntervalMs = 250)
    {
        _currentProcessId = (uint)Process.GetCurrentProcess().Id;
        _timer = new System.Threading.Timer(CheckState, null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start()
    {
        _timer.Change(0, 250);
    }

    public void Stop()
    {
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
    }

    private void CheckState(object? state)
    {
        try
        {
            bool isFs = DetermineFullscreenState();
            SetFullscreenState(isFs);
        }
        catch
        {
            // Ignore transient window checking errors
        }
    }

    public IntPtr TargetIslandHwnd { get; set; } = IntPtr.Zero;

    public bool DetermineFullscreenState()
    {
        var settings = SettingsManager.Current;
        if (!settings.IslandHideOnFullscreen || settings.IslandAlwaysShowOnFullscreen)
        {
            return false;
        }

        // 1. Check foreground window bounds against the monitor it resides on
        var foreground = Win32Api.GetForegroundWindow();
        if (foreground == IntPtr.Zero)
            return false;

        // Ignore if foreground window belongs to our own application process
        Win32Api.GetWindowThreadProcessId(foreground, out uint pid);
        if (pid == _currentProcessId)
        {
            // Retain previous state when interacting with TheHub itself
            return _isFullscreen;
        }

        // Ignore Desktop and Shell windows
        var desktop = Win32Api.GetDesktopWindow();
        var shell = Win32Api.GetShellWindow();
        if (foreground == desktop || foreground == shell)
        {
            return false;
        }

        // Check window class name for Explorer Desktop / Taskbar
        var classNameSb = new StringBuilder(256);
        Win32Api.GetClassName(foreground, classNameSb, classNameSb.Capacity);
        string className = classNameSb.ToString();
        if (className == "Progman" ||
            className == "WorkerW" ||
            className == "Shell_TrayWnd" ||
            className == "Shell_SecondaryTrayWnd")
        {
            return false;
        }

        // Check if window is visible
        if (!Win32Api.IsWindowVisible(foreground))
            return false;

        // Retrieve monitor for the foreground window
        IntPtr foregroundMonitor = Win32Api.MonitorFromWindow(foreground, Win32Api.MONITOR_DEFAULTTONEAREST);
        if (foregroundMonitor == IntPtr.Zero)
            return false;

        // MULTI-MONITOR ISOLATION:
        // If TargetIslandHwnd is configured, compare monitors.
        // If the fullscreen window is on a DIFFERENT monitor than the Dynamic Island,
        // do NOT hide the Dynamic Island!
        if (TargetIslandHwnd != IntPtr.Zero)
        {
            IntPtr islandMonitor = Win32Api.MonitorFromWindow(TargetIslandHwnd, Win32Api.MONITOR_DEFAULTTONEAREST);
            if (islandMonitor != IntPtr.Zero && foregroundMonitor != islandMonitor)
            {
                return false;
            }
        }

        // 2. Check if the foreground window covers its monitor (exclusive or borderless fullscreen)
        var mi = new Win32Api.MONITORINFO { cbSize = Marshal.SizeOf<Win32Api.MONITORINFO>() };
        if (Win32Api.GetMonitorInfo(foregroundMonitor, ref mi))
        {
            if (Win32Api.GetWindowRect(foreground, out var rect))
            {
                bool coversMonitor = (rect.Left <= mi.rcMonitor.Left + 1 &&
                                      rect.Top <= mi.rcMonitor.Top + 1 &&
                                      rect.Right >= mi.rcMonitor.Right - 1 &&
                                      rect.Bottom >= mi.rcMonitor.Bottom - 1);

                int style = Win32Api.GetWindowLong(foreground, Win32Api.GWL_STYLE);
                bool isChild = (style & Win32Api.WS_CHILD) != 0;

                if (coversMonitor && !isChild)
                {
                    return true;
                }
            }
        }

        // 3. Fallback check for native Windows Shell D3D exclusive fullscreen state
        try
        {
            int hr = Win32Api.SHQueryUserNotificationState(out var qstate);
            if (hr == 0 && (qstate == Win32Api.QUERY_USER_NOTIFICATION_STATE.QUNS_RUNNING_D3D_FULL_SCREEN ||
                            qstate == Win32Api.QUERY_USER_NOTIFICATION_STATE.QUNS_BUSY))
            {
                if (TargetIslandHwnd != IntPtr.Zero)
                {
                    IntPtr islandMon = Win32Api.MonitorFromWindow(TargetIslandHwnd, Win32Api.MONITOR_DEFAULTTONEAREST);
                    IntPtr primaryMon = Win32Api.MonitorFromWindow(IntPtr.Zero, Win32Api.MONITOR_DEFAULTTOPRIMARY);
                    if (islandMon == primaryMon)
                    {
                        return true;
                    }
                }
                else
                {
                    return true;
                }
            }
        }
        catch { }

        return false;
    }

    private void SetFullscreenState(bool isFullscreen)
    {
        if (_isFullscreen != isFullscreen)
        {
            _isFullscreen = isFullscreen;
            FullscreenChanged?.Invoke(isFullscreen);
        }
    }

    public void Dispose()
    {
        _timer.Dispose();
    }
}
