using System.Runtime.InteropServices;
using TheHub.Common.Config;

namespace TheHub.Core.Native;

public static class DesktopWorkerHelper
{
    private const uint SPI_SETDESKWALLPAPER = 0x0014;
    private const uint SPIF_UPDATEINIFILE = 0x01;
    private const uint SPIF_SENDCHANGE = 0x02;

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern int SystemParametersInfo(uint uAction, uint uParam, string? lpvParam, uint fuWinIni);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyWindow(IntPtr hWnd);

    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_SHOWWINDOW = 0x0040;
    public static readonly IntPtr HWND_BOTTOM = new IntPtr(1);

    /// <summary>
    /// Refreshes Windows Explorer desktop wallpaper and repaints the screen to remove any WorkerW artifacts.
    /// </summary>
    public static void RefreshDesktopWallpaper()
    {
        try
        {
            SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, null, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
        }
        catch (Exception ex)
        {
            AppLogger.Log($"RefreshDesktopWallpaper exception: {ex.Message}");
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindowEx(IntPtr parentHandle, IntPtr childAfter, string? className, string? windowTitle);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd,
        uint msg,
        IntPtr wParam,
        IntPtr lParam,
        uint fuFlags,
        uint uTimeout,
        out IntPtr lpdwResult);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int MapWindowPoints(IntPtr hWndFrom, IntPtr hWndTo, ref Win32Api.RECT lpPoints, uint cPoints);

    /// <summary>
    /// Accurately maps screen coordinates to WorkerW client coordinates and sets the window bounds.
    /// In multi-monitor setups where secondary monitors have negative coordinates (e.g. X = -1360),
    /// WorkerW's client coordinate origin (0,0) is at the top-left of the virtual desktop.
    /// MapWindowPoints translates absolute screen bounds to WorkerW's parent-relative client coordinates.
    /// </summary>
    public static void SetWallpaperBounds(IntPtr wallpaperHwnd, IntPtr workerwHwnd, System.Drawing.Rectangle screenBounds)
    {
        if (wallpaperHwnd == IntPtr.Zero) return;

        int relativeX = screenBounds.X;
        int relativeY = screenBounds.Y;
        int width = screenBounds.Width;
        int height = screenBounds.Height;

        if (workerwHwnd != IntPtr.Zero)
        {
            var rect = new Win32Api.RECT
            {
                Left = screenBounds.Left,
                Top = screenBounds.Top,
                Right = screenBounds.Right,
                Bottom = screenBounds.Bottom
            };

            // Map points from Screen space (IntPtr.Zero) to WorkerW client space (workerwHwnd)
            MapWindowPoints(IntPtr.Zero, workerwHwnd, ref rect, 2);

            relativeX = rect.Left;
            relativeY = rect.Top;
        }

        SetWindowPos(
            wallpaperHwnd,
            IntPtr.Zero,
            relativeX,
            relativeY,
            width,
            height,
            SWP_NOZORDER | SWP_NOACTIVATE | SWP_SHOWWINDOW
        );

        AppLogger.Log($"SetWallpaperBounds: Screen [{screenBounds.X},{screenBounds.Y} {width}x{height}] -> WorkerW Relative [{relativeX},{relativeY} {width}x{height}] for HWND {wallpaperHwnd}");
    }



    /// <summary>
    /// Applies a static image file as the Windows desktop wallpaper.
    /// </summary>
    public static bool SetStaticWallpaper(string filePath)
    {
        try
        {
            if (!System.IO.File.Exists(filePath)) return false;
            int result = SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, filePath, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
            return result != 0;
        }
        catch
        {
            return false;
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetShellWindow();

    /// <summary>
    /// Locates the WorkerW window handle positioned behind desktop icons.
    /// Spawns WorkerW behind desktop icons by sending 0x052C to Progman.
    /// </summary>
    public static IntPtr GetDesktopWorkerW()
    {
        IntPtr progman = FindWindow("Progman", null);
        if (progman == IntPtr.Zero)
        {
            progman = FindWindow("Progman", "Program Manager");
        }
        if (progman == IntPtr.Zero)
        {
            progman = GetShellWindow();
        }
        if (progman == IntPtr.Zero) return IntPtr.Zero;

        // Send 0x052C to Progman to split desktop icons from desktop wallpaper
        SendMessageTimeout(progman, 0x052C, new IntPtr(0xD), new IntPtr(0x1), 0, 1000, out _);

        IntPtr workerW = IntPtr.Zero;

        // Enumerate top-level windows to locate WorkerW with quick retry
        for (int attempt = 0; attempt < 5 && workerW == IntPtr.Zero; attempt++)
        {
            if (attempt > 0) System.Threading.Thread.Sleep(50);

            EnumWindows((tophandle, topparamhandle) =>
            {
                IntPtr p = FindWindowEx(tophandle, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (p != IntPtr.Zero)
                {
                    // Gets the WorkerW window immediately behind SHELLDLL_DefView
                    workerW = FindWindowEx(IntPtr.Zero, tophandle, "WorkerW", null);
                }
                return true;
            }, IntPtr.Zero);
        }

        // Fallback: If sibling WorkerW is zero, fallback to progman
        return workerW != IntPtr.Zero ? workerW : progman;
    }
}
