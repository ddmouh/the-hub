using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace TheHub.Overlays.Widgets;

public partial class DesktopCpuWidget : DesktopWidgetBase
{
    private readonly DispatcherTimer _timer;
    private long _prevIdleTime, _prevKernelTime, _prevUserTime;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(
        out System.Runtime.InteropServices.ComTypes.FILETIME idleTime,
        out System.Runtime.InteropServices.ComTypes.FILETIME kernelTime,
        out System.Runtime.InteropServices.ComTypes.FILETIME userTime);

    private static long FileTimeToLong(System.Runtime.InteropServices.ComTypes.FILETIME ft)
        => ((long)ft.dwHighDateTime << 32) | (uint)ft.dwLowDateTime;

    public DesktopCpuWidget()
    {
        InitializeComponent();

        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (s, e) => UpdateCpu();

        Loaded += (s, e) =>
        {
            InitTimes();
            _timer.Start();
            PinToDesktopBottom();
            if (Left == 0 && Top == 0)
            {
                var wa = SystemParameters.WorkArea;
                Left = wa.Right - ActualWidth - 16;
                Top = wa.Top + 60;
            }
        };
        Unloaded += (s, e) => _timer.Stop();
    }

    private void InitTimes()
    {
        if (GetSystemTimes(out var idle, out var kernel, out var user))
        {
            _prevIdleTime = FileTimeToLong(idle);
            _prevKernelTime = FileTimeToLong(kernel);
            _prevUserTime = FileTimeToLong(user);
        }
    }

    private void UpdateCpu()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user)) return;

        long curIdle = FileTimeToLong(idle);
        long curKernel = FileTimeToLong(kernel);
        long curUser = FileTimeToLong(user);

        long diffIdle = curIdle - _prevIdleTime;
        long diffKernel = curKernel - _prevKernelTime;
        long diffUser = curUser - _prevUserTime;
        long total = diffKernel + diffUser;

        _prevIdleTime = curIdle; _prevKernelTime = curKernel; _prevUserTime = curUser;

        if (total <= 0) return;
        double cpu = Math.Clamp((double)(total - diffIdle) * 100.0 / total, 0.0, 100.0);

        PbCpu.Value = cpu;
        TxtCpuPercent.Text = $"{Math.Round(cpu)}%";
    }

    private void OnDragBarMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!IsPositionLocked && e.LeftButton == MouseButtonState.Pressed)
        {
            try { DragMove(); } catch { }
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => CloseWidget();

    public override void PausePolling()
    {
        _timer?.Stop();
    }

    public override void ResumePolling()
    {
        if (IsVisible)
        {
            _timer?.Start();
        }
    }
}
