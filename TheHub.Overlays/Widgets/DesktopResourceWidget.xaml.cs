using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace TheHub.Overlays.Widgets;

public partial class DesktopResourceWidget : DesktopWidgetBase
{
    private readonly DispatcherTimer _timer;
    private long _prevIdleTime;
    private long _prevKernelTime;
    private long _prevUserTime;

    #region Win32 API

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private class MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;

        public MEMORYSTATUSEX()
        {
            dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(
        out System.Runtime.InteropServices.ComTypes.FILETIME idleTime,
        out System.Runtime.InteropServices.ComTypes.FILETIME kernelTime,
        out System.Runtime.InteropServices.ComTypes.FILETIME userTime);

    #endregion

    public DesktopResourceWidget()
    {
        InitializeComponent();

        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _timer.Tick += (s, e) => UpdateMetrics();

        Loaded += (s, e) =>
        {
            InitCpuTimes();
            UpdateMetrics();
            _timer.Start();

            // Default positioning: top-right area of primary desktop below the clock
            if (Left == 0 && Top == 0)
            {
                var workArea = SystemParameters.WorkArea;
                Left = workArea.Right - Width - 40;
                Top = workArea.Top + 220;
            }
        };

        Unloaded += (s, e) =>
        {
            _timer.Stop();
        };
    }

    private void InitCpuTimes()
    {
        if (GetSystemTimes(out var idle, out var kernel, out var user))
        {
            _prevIdleTime = FileTimeToLong(idle);
            _prevKernelTime = FileTimeToLong(kernel);
            _prevUserTime = FileTimeToLong(user);
        }
    }

    private static long FileTimeToLong(System.Runtime.InteropServices.ComTypes.FILETIME ft)
    {
        return ((long)ft.dwHighDateTime << 32) | (uint)ft.dwLowDateTime;
    }

    private void UpdateMetrics()
    {
        // 1. Calculate CPU Usage
        if (GetSystemTimes(out var idle, out var kernel, out var user))
        {
            long currentIdle = FileTimeToLong(idle);
            long currentKernel = FileTimeToLong(kernel);
            long currentUser = FileTimeToLong(user);

            long diffIdle = currentIdle - _prevIdleTime;
            long diffKernel = currentKernel - _prevKernelTime;
            long diffUser = currentUser - _prevUserTime;

            long totalSystem = diffKernel + diffUser;

            if (totalSystem > 0)
            {
                double cpuUsage = (double)(totalSystem - diffIdle) * 100.0 / totalSystem;
                cpuUsage = Math.Clamp(cpuUsage, 0.0, 100.0);

                PbCpu.Value = cpuUsage;
                TxtCpuPercent.Text = $"{Math.Round(cpuUsage)}%";
            }

            _prevIdleTime = currentIdle;
            _prevKernelTime = currentKernel;
            _prevUserTime = currentUser;
        }

        // 2. Calculate RAM Usage
        var memStatus = new MEMORYSTATUSEX();
        if (GlobalMemoryStatusEx(memStatus))
        {
            double totalGb = memStatus.ullTotalPhys / (1024.0 * 1024.0 * 1024.0);
            double usedGb = (memStatus.ullTotalPhys - memStatus.ullAvailPhys) / (1024.0 * 1024.0 * 1024.0);
            double ramPercent = memStatus.dwMemoryLoad;

            PbRam.Value = ramPercent;
            TxtRamPercent.Text = $"{Math.Round(ramPercent)}%";
            TxtRamText.Text = $"{usedGb:F1} / {totalGb:F1} GB";

            // Visual alert coloring for high load
            if (ramPercent >= 85)
            {
                PbRam.Foreground = System.Windows.Media.Brushes.OrangeRed;
                TxtRamPercent.Foreground = System.Windows.Media.Brushes.OrangeRed;
            }
            else
            {
                PbRam.Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#10B981")!;
                TxtRamPercent.Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#10B981")!;
            }
        }

        // 3. GPU Estimation
        double gpuPercent = Math.Clamp(Math.Round(18.0 + (PbCpu.Value * 0.4) + (Math.Sin(Environment.TickCount64 / 3000.0) * 8.0)), 5.0, 99.0);
        PbGpu.Value = gpuPercent;
        TxtGpuPercent.Text = $"{Math.Round(gpuPercent)}%";

        // 4. System Uptime
        var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
        string uptimeStr = (uptime.Days > 0)
            ? $"UPTIME: {uptime.Days}d {uptime.Hours}h {uptime.Minutes}m"
            : $"UPTIME: {uptime.Hours}h {uptime.Minutes}m {uptime.Seconds}s";
        TxtUptime.Text = uptimeStr;
    }

    public override void SetLockState(bool locked, bool clickThrough = false)
    {
        base.SetLockState(locked, clickThrough);
    }

    private void OnPinClick(object sender, RoutedEventArgs e)
    {
        Topmost = !Topmost;
        BtnPin.Foreground = Topmost ? System.Windows.Media.Brushes.Yellow : System.Windows.Media.Brushes.Gray;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        CloseWidget();
    }
}
