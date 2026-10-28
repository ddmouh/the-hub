using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace TheHub.Design.Controls;

public enum HoldButtonPhase
{
    Idle,
    Holding,
    Releasing,
    Committed
}

/// <summary>
/// High-performance Hold-to-Confirm button matching modern spring physics.
/// - Short click (< 250ms) triggers normal Click event.
/// - Press and hold smoothly charges progress bar with visual feedback.
/// - Releasing early rewinds smoothly at ReleaseRate (default 2.5x).
/// - Moving mouse beyond tolerance aborts the hold.
/// - Reaching 100% enters Committed phase and fires HoldConfirmed event.
/// </summary>
public partial class HoldToConfirmButton : UserControl
{
    public static readonly DependencyProperty HoldDurationMsProperty =
        DependencyProperty.Register(nameof(HoldDurationMs), typeof(double), typeof(HoldToConfirmButton), new PropertyMetadata(1500.0));

    public static readonly DependencyProperty ReleaseRateProperty =
        DependencyProperty.Register(nameof(ReleaseRate), typeof(double), typeof(HoldToConfirmButton), new PropertyMetadata(2.5));

    public static readonly DependencyProperty MoveToleranceProperty =
        DependencyProperty.Register(nameof(MoveTolerance), typeof(double), typeof(HoldToConfirmButton), new PropertyMetadata(10.0));

    public static readonly DependencyProperty TapThresholdMsProperty =
        DependencyProperty.Register(nameof(TapThresholdMs), typeof(double), typeof(HoldToConfirmButton), new PropertyMetadata(260.0));

    public static readonly DependencyProperty BaseBackgroundProperty =
        DependencyProperty.Register(nameof(BaseBackground), typeof(Brush), typeof(HoldToConfirmButton),
            new PropertyMetadata(new SolidColorBrush(Color.FromArgb(180, 24, 24, 27))));

    public static readonly DependencyProperty BaseBorderBrushProperty =
        DependencyProperty.Register(nameof(BaseBorderBrush), typeof(Brush), typeof(HoldToConfirmButton),
            new PropertyMetadata(new SolidColorBrush(Color.FromRgb(39, 39, 42))));

    public static readonly DependencyProperty HoldBrushProperty =
        DependencyProperty.Register(nameof(HoldBrush), typeof(Brush), typeof(HoldToConfirmButton),
            new PropertyMetadata(new SolidColorBrush(Color.FromRgb(239, 68, 68))));

    public static readonly DependencyProperty CornerRadiusProperty =
        DependencyProperty.Register(nameof(CornerRadius), typeof(CornerRadius), typeof(HoldToConfirmButton),
            new PropertyMetadata(new CornerRadius(6)));

    public static readonly DependencyProperty ConfirmedTextProperty =
        DependencyProperty.Register(nameof(ConfirmedText), typeof(string), typeof(HoldToConfirmButton),
            new PropertyMetadata("✓ CONFIRMED"));

    public static readonly DependencyProperty ProgressProperty =
        DependencyProperty.Register(nameof(Progress), typeof(double), typeof(HoldToConfirmButton), new PropertyMetadata(0.0, OnProgressChanged));

    public static readonly DependencyProperty PhaseProperty =
        DependencyProperty.Register(nameof(Phase), typeof(HoldButtonPhase), typeof(HoldToConfirmButton), new PropertyMetadata(HoldButtonPhase.Idle));

    public static readonly DependencyProperty CommandProperty =
        DependencyProperty.Register(nameof(Command), typeof(ICommand), typeof(HoldToConfirmButton), new PropertyMetadata(null));

    public static readonly DependencyProperty HoldConfirmedCommandProperty =
        DependencyProperty.Register(nameof(HoldConfirmedCommand), typeof(ICommand), typeof(HoldToConfirmButton), new PropertyMetadata(null));

    public double HoldDurationMs
    {
        get => (double)GetValue(HoldDurationMsProperty);
        set => SetValue(HoldDurationMsProperty, value);
    }

    public double ReleaseRate
    {
        get => (double)GetValue(ReleaseRateProperty);
        set => SetValue(ReleaseRateProperty, value);
    }

    public double MoveTolerance
    {
        get => (double)GetValue(MoveToleranceProperty);
        set => SetValue(MoveToleranceProperty, value);
    }

    public double TapThresholdMs
    {
        get => (double)GetValue(TapThresholdMsProperty);
        set => SetValue(TapThresholdMsProperty, value);
    }

    public Brush BaseBackground
    {
        get => (Brush)GetValue(BaseBackgroundProperty);
        set => SetValue(BaseBackgroundProperty, value);
    }

    public Brush BaseBorderBrush
    {
        get => (Brush)GetValue(BaseBorderBrushProperty);
        set => SetValue(BaseBorderBrushProperty, value);
    }

    public Brush HoldBrush
    {
        get => (Brush)GetValue(HoldBrushProperty);
        set => SetValue(HoldBrushProperty, value);
    }

    public CornerRadius CornerRadius
    {
        get => (CornerRadius)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    public string ConfirmedText
    {
        get => (string)GetValue(ConfirmedTextProperty);
        set => SetValue(ConfirmedTextProperty, value);
    }

    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    public HoldButtonPhase Phase
    {
        get => (HoldButtonPhase)GetValue(PhaseProperty);
        private set => SetValue(PhaseProperty, value);
    }

    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public ICommand? HoldConfirmedCommand
    {
        get => (ICommand?)GetValue(HoldConfirmedCommandProperty);
        set => SetValue(HoldConfirmedCommandProperty, value);
    }

    public event RoutedEventHandler? Click;
    public event RoutedEventHandler? HoldConfirmed;
    public event RoutedEventHandler? HoldAborted;

    private readonly Stopwatch _holdWatch = new();
    private TimeSpan _lastFrameTime;
    private Point _originPoint;
    private bool _isDown;
    private double _elapsedMs;
    private bool _isRenderHooked;

    public HoldToConfirmButton()
    {
        InitializeComponent();
        Loaded += (s, e) => UpdateProgressVisuals(Progress);
        Unloaded += (s, e) => DetachRendering();
    }

    private static void OnProgressChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is HoldToConfirmButton btn)
        {
            btn.UpdateProgressVisuals((double)e.NewValue);
        }
    }

    private void UpdateProgressVisuals(double p)
    {
        double width = ActualWidth > 0 ? ActualWidth : Width;
        if (double.IsNaN(width) || width <= 0) width = 36.0;

        ProgressFill.Width = Math.Clamp(p * width, 0, width);
        GlowBorder.Opacity = Math.Clamp(p * 1.2, 0.0, 1.0);
    }

    private void AttachRendering()
    {
        if (!_isRenderHooked)
        {
            _lastFrameTime = TimeSpan.Zero;
            CompositionTarget.Rendering += OnRenderTick;
            _isRenderHooked = true;
        }
    }

    private void DetachRendering()
    {
        if (_isRenderHooked)
        {
            CompositionTarget.Rendering -= OnRenderTick;
            _isRenderHooked = false;
        }
    }

    private void BeginHold(Point pt)
    {
        if (!IsEnabled || Phase == HoldButtonPhase.Committed) return;

        _originPoint = pt;
        _isDown = true;
        _elapsedMs = 0;
        Phase = HoldButtonPhase.Holding;
        _holdWatch.Restart();

        // Scale feedback
        ScaleTransform.ScaleX = 0.96;
        ScaleTransform.ScaleY = 0.96;

        AttachRendering();
    }

    private void ReleaseHold()
    {
        if (Phase != HoldButtonPhase.Holding) return;

        double holdTime = _holdWatch.ElapsedMilliseconds;
        _isDown = false;
        _holdWatch.Stop();

        // Check if single click / short tap
        if (holdTime < TapThresholdMs && Progress < 0.25)
        {
            ResetVisuals();
            Click?.Invoke(this, new RoutedEventArgs());
            Command?.Execute(null);
            return;
        }

        // Otherwise abort hold and smoothly rewind
        Phase = HoldButtonPhase.Releasing;
        HoldAborted?.Invoke(this, new RoutedEventArgs());
    }

    private void ResetVisuals()
    {
        _isDown = false;
        _elapsedMs = 0;
        Progress = 0;
        Phase = HoldButtonPhase.Idle;
        ScaleTransform.ScaleX = 1.0;
        ScaleTransform.ScaleY = 1.0;
        DefaultContentPresenter.Visibility = Visibility.Visible;
        TxtConfirmed.Visibility = Visibility.Collapsed;
        DetachRendering();
    }

    private void OnRenderTick(object? sender, EventArgs e)
    {
        if (e is RenderingEventArgs renderingArgs)
        {
            if (_lastFrameTime == TimeSpan.Zero)
            {
                _lastFrameTime = renderingArgs.RenderingTime;
                return;
            }

            double dt = (renderingArgs.RenderingTime - _lastFrameTime).TotalMilliseconds;
            _lastFrameTime = renderingArgs.RenderingTime;
            dt = Math.Min(dt, 50.0); // Cap frame delta

            if (_isDown && Phase == HoldButtonPhase.Holding)
            {
                _elapsedMs += dt;
                double p = Math.Clamp(_elapsedMs / HoldDurationMs, 0.0, 1.0);
                Progress = p;

                if (_elapsedMs >= HoldDurationMs)
                {
                    // Committed!
                    _isDown = false;
                    Progress = 1.0;
                    Phase = HoldButtonPhase.Committed;
                    DetachRendering();

                    ScaleTransform.ScaleX = 1.05;
                    ScaleTransform.ScaleY = 1.05;

                    DefaultContentPresenter.Visibility = Visibility.Collapsed;
                    TxtConfirmed.Visibility = Visibility.Visible;

                    HoldConfirmed?.Invoke(this, new RoutedEventArgs());
                    HoldConfirmedCommand?.Execute(null);
                }
            }
            else if (Phase == HoldButtonPhase.Releasing)
            {
                _elapsedMs -= dt * ReleaseRate;
                double p = Math.Clamp(_elapsedMs / HoldDurationMs, 0.0, 1.0);
                Progress = p;

                // Scale restore
                ScaleTransform.ScaleX = 1.0;
                ScaleTransform.ScaleY = 1.0;

                if (_elapsedMs <= 0)
                {
                    ResetVisuals();
                }
            }
            else
            {
                DetachRendering();
            }
        }
    }

    private void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        CaptureMouse();
        BeginHold(e.GetPosition(this));
        e.Handled = true;
    }

    private void OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (IsMouseCaptured) ReleaseMouseCapture();
        ReleaseHold();
        e.Handled = true;
    }

    private void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_isDown && Phase == HoldButtonPhase.Holding)
        {
            Point current = e.GetPosition(this);
            double dx = current.X - _originPoint.X;
            double dy = current.Y - _originPoint.Y;
            double dist = Math.Sqrt(dx * dx + dy * dy);
            if (dist > MoveTolerance)
            {
                if (IsMouseCaptured) ReleaseMouseCapture();
                ReleaseHold();
            }
        }
    }

    private void OnMouseLeave(object sender, MouseEventArgs e)
    {
        if (_isDown && Phase == HoldButtonPhase.Holding)
        {
            if (IsMouseCaptured) ReleaseMouseCapture();
            ReleaseHold();
        }
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space || e.Key == Key.Enter)
        {
            if (!e.IsRepeat && Phase == HoldButtonPhase.Idle)
            {
                BeginHold(new Point(ActualWidth / 2, ActualHeight / 2));
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Escape)
        {
            if (Phase == HoldButtonPhase.Holding || Phase == HoldButtonPhase.Releasing)
            {
                ResetVisuals();
                e.Handled = true;
            }
        }
    }

    private void OnKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space || e.Key == Key.Enter)
        {
            ReleaseHold();
            e.Handled = true;
        }
    }
}
