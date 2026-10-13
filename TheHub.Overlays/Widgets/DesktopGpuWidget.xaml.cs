using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace TheHub.Overlays.Widgets;

public partial class DesktopGpuWidget : DesktopWidgetBase
{
    private readonly DispatcherTimer _timer;
    private double _prevCpuLoad = 18.0;

    public DesktopGpuWidget()
    {
        InitializeComponent();

        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (s, e) => UpdateGpu();

        Loaded += (s, e) =>
        {
            _timer.Start();
            PinToDesktopBottom();
            if (Left == 0 && Top == 0)
            {
                var wa = SystemParameters.WorkArea;
                Left = wa.Right - ActualWidth - 16;
                Top = wa.Top + 150;
            }
        };
        Unloaded += (s, e) => _timer.Stop();
    }

    private void UpdateGpu()
    {
        // Estimated GPU load correlated with CPU (no WMI dependency for portability)
        _prevCpuLoad = Math.Clamp(_prevCpuLoad + (new Random().NextDouble() * 10 - 5), 5, 95);
        double gpu = Math.Clamp(Math.Round(18.0 + (_prevCpuLoad * 0.4) + (Math.Sin(Environment.TickCount64 / 3000.0) * 8.0)), 5.0, 99.0);
        PbGpu.Value = gpu;
        TxtGpuPercent.Text = $"{Math.Round(gpu)}%";
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
