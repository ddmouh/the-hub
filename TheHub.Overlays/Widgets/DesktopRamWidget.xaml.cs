using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace TheHub.Overlays.Widgets;

public partial class DesktopRamWidget : DesktopWidgetBase
{
    private readonly DispatcherTimer _timer;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private class MEMORYSTATUSEX
    {
        public uint dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

    public DesktopRamWidget()
    {
        InitializeComponent();

        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (s, e) => UpdateRam();

        Loaded += (s, e) =>
        {
            UpdateRam();
            _timer.Start();
            PinToDesktopBottom();
            if (Left == 0 && Top == 0)
            {
                var wa = SystemParameters.WorkArea;
                Left = wa.Right - ActualWidth - 16;
                Top = wa.Top + 240;
            }
        };
        Unloaded += (s, e) => _timer.Stop();
    }

    private void UpdateRam()
    {
        var ms = new MEMORYSTATUSEX();
        if (!GlobalMemoryStatusEx(ms)) return;

        double total = ms.ullTotalPhys / 1073741824.0;
        double used = (ms.ullTotalPhys - ms.ullAvailPhys) / 1073741824.0;
        double pct = ms.dwMemoryLoad;

        PbRam.Value = pct;
        TxtRamPercent.Text = $"{Math.Round(pct)}%";
        TxtRamText.Text = $"{used:F1} / {total:F1} GB";

        var alertBrush = pct >= 85
            ? Brushes.OrangeRed
            : (Brush)new SolidColorBrush(System.Windows.Media.Color.FromRgb(16, 185, 129));

        PbRam.Foreground = alertBrush;
        TxtRamPercent.Foreground = alertBrush;
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
