using System.Drawing;
using System.Runtime.InteropServices;
using TheHub.Core.Native;

namespace TheHub.Core.Native;

public class MonitorItem
{
    public int Index { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public Win32Api.RECT Bounds { get; set; }
    public Win32Api.RECT WorkArea { get; set; }
    public bool IsPrimary { get; set; }
    public IntPtr Handle { get; set; }

    public int Width => Bounds.Right - Bounds.Left;
    public int Height => Bounds.Bottom - Bounds.Top;
    public string ResolutionText => $"{Width}x{Height}";
    public string DisplayTitle => IsPrimary ? $"Display {Index + 1} ({ResolutionText}) — Primary" : $"Display {Index + 1} ({ResolutionText})";
}

public static class DisplayMonitorHelper
{
    public const int ENUM_CURRENT_SETTINGS = -1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmDeviceName;
        public short dmSpecVersion;
        public short dmDriverVersion;
        public short dmSize;
        public short dmDriverExtra;
        public int dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public int dmDisplayOrientation;
        public int dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmFormName;
        public short dmLogPixels;
        public short dmBitsPerPel;
        public int dmPelsWidth;
        public int dmPelsHeight;
        public int dmDisplayFlags;
        public int dmDisplayFrequency;
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern bool EnumDisplaySettings(string? lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode);

    public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref Win32Api.RECT lprcMonitor, IntPtr dwData);

    [DllImport("user32.dll")]
    public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public struct MONITORINFOEX
    {
        public int cbSize;
        public Win32Api.RECT rcMonitor;
        public Win32Api.RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    public const int MONITORINFOF_PRIMARY = 1;

    /// <summary>
    /// Returns the exact physical pixel bounds for a given DeviceName by querying hardware DEVMODE.
    /// </summary>
    public static Rectangle GetPhysicalBounds(string deviceName, Rectangle fallbackBounds)
    {
        try
        {
            var dm = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
            if (EnumDisplaySettings(deviceName, ENUM_CURRENT_SETTINGS, ref dm) && dm.dmPelsWidth > 0 && dm.dmPelsHeight > 0)
            {
                return new Rectangle(dm.dmPositionX, dm.dmPositionY, dm.dmPelsWidth, dm.dmPelsHeight);
            }
        }
        catch
        {
        }
        return fallbackBounds;
    }

    /// <summary>
    /// Returns a list of all active physical monitors with precise physical dimensions.
    /// </summary>
    public static List<MonitorItem> GetAllMonitors()
    {
        var rawList = new List<MonitorItem>();
        int counter = 0;

        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMon, IntPtr hdc, ref Win32Api.RECT rc, IntPtr data) =>
        {
            var mi = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
            if (GetMonitorInfo(hMon, ref mi))
            {
                bool isPrimary = (mi.dwFlags & MONITORINFOF_PRIMARY) != 0;
                string device = !string.IsNullOrEmpty(mi.szDevice) ? mi.szDevice : $"\\\\.\\DISPLAY{counter + 1}";
                var fallback = new Rectangle(mi.rcMonitor.Left, mi.rcMonitor.Top, mi.rcMonitor.Right - mi.rcMonitor.Left, mi.rcMonitor.Bottom - mi.rcMonitor.Top);
                var phys = GetPhysicalBounds(device, fallback);

                rawList.Add(new MonitorItem
                {
                    Handle = hMon,
                    DeviceName = device,
                    Bounds = new Win32Api.RECT
                    {
                        Left = phys.Left,
                        Top = phys.Top,
                        Right = phys.Right,
                        Bottom = phys.Bottom
                    },
                    WorkArea = mi.rcWork,
                    IsPrimary = isPrimary,
                    Index = counter++
                });
            }
            return true;
        }, IntPtr.Zero);

        // Sort so Primary is always Index 0, and secondary monitors follow
        var sorted = rawList.OrderByDescending(m => m.IsPrimary).ToList();
        for (int i = 0; i < sorted.Count; i++)
        {
            sorted[i].Index = i;
            int width = sorted[i].Bounds.Right - sorted[i].Bounds.Left;
            int height = sorted[i].Bounds.Bottom - sorted[i].Bounds.Top;
            sorted[i].Name = sorted[i].IsPrimary
                ? $"🖥️ Primary Display ({width}x{height})"
                : $"🖥️ Secondary Display {i} ({width}x{height})";
        }

        if (sorted.Count == 0)
        {
            // Fallback for headless or mock
            sorted.Add(new MonitorItem
            {
                Index = 0,
                DeviceName = "\\\\.\\DISPLAY1",
                Name = "🖥️ Primary Display (1920x1080)",
                IsPrimary = true,
                Bounds = new Win32Api.RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1080 },
                WorkArea = new Win32Api.RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1040 }
            });
        }

        return sorted;
    }

    public static MonitorItem GetTargetMonitor(int targetIndex)
    {
        var list = GetAllMonitors();
        if (targetIndex >= 0 && targetIndex < list.Count)
        {
            return list[targetIndex];
        }

        return list[0]; // Primary fallback
    }
}
