using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using TheHub.Design.Motion;

namespace TheHub.Design.Controls;

/// <summary>
/// Destructive confirm button requiring a sustained press-and-hold.
/// Progressive fill with clip-based text inversion, spring rollback on early release,
/// and verified keyboard/mouse/touch accessibility.
/// </summary>
[TemplatePart(Name = "PART_FillTrack", Type = typeof(FrameworkElement))]
[TemplatePart(Name = "PART_InvertedContainer", Type = typeof(FrameworkElement))]
public class HubHoldButton : ButtonBase
{
    private readonly HoldButtonStateMachine _stateMachine = new();
    private FrameworkElement? _fillTrack;
    private FrameworkElement? _invertedContainer;
    private DateTime _lastTick;
    private bool _isTicking;

    #region Dependency Properties

    public static readonly DependencyProperty HoldDurationMsProperty =
        DependencyProperty.Register(
            nameof(HoldDurationMs),
            typeof(double),
            typeof(HubHoldButton),
            new PropertyMetadata(1200.0, (d, e) => ((HubHoldButton)d)._stateMachine.HoldDurationMs = (double)e.NewValue));

    public double HoldDurationMs
    {
        get => (double)GetValue(HoldDurationMsProperty);
        set => SetValue(HoldDurationMsProperty, value);
    }

    public static readonly DependencyProperty VariantProperty =
        DependencyProperty.Register(
            nameof(Variant),
            typeof(HubHoldButtonVariant),
            typeof(HubHoldButton),
            new PropertyMetadata(HubHoldButtonVariant.Danger));

    public HubHoldButtonVariant Variant
    {
        get => (HubHoldButtonVariant)GetValue(VariantProperty);
        set => SetValue(VariantProperty, value);
    }

    public static readonly DependencyProperty HoldingTextProperty =
        DependencyProperty.Register(
            nameof(HoldingText),
            typeof(string),
            typeof(HubHoldButton),
            new PropertyMetadata("Release to cancel"));

    public string HoldingText
    {
        get => (string)GetValue(HoldingTextProperty);
        set => SetValue(HoldingTextProperty, value);
    }

    public static readonly DependencyProperty ConfirmedTextProperty =
        DependencyProperty.Register(
            nameof(ConfirmedText),
            typeof(string),
            typeof(HubHoldButton),
            new PropertyMetadata("Confirmed"));

    public string ConfirmedText
    {
        get => (string)GetValue(ConfirmedTextProperty);
        set => SetValue(ConfirmedTextProperty, value);
    }

    private static readonly DependencyPropertyKey ProgressPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(Progress),
            typeof(double),
            typeof(HubHoldButton),
            new PropertyMetadata(0.0, OnProgressChanged));

    public static readonly DependencyProperty ProgressProperty = ProgressPropertyKey.DependencyProperty;

    public double Progress => (double)GetValue(ProgressProperty);

    private static readonly DependencyPropertyKey CurrentStatePropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(CurrentState),
            typeof(HoldButtonState),
            typeof(HubHoldButton),
            new PropertyMetadata(HoldButtonState.Idle));

    public static readonly DependencyProperty CurrentStateProperty = CurrentStatePropertyKey.DependencyProperty;

    public HoldButtonState CurrentState => (HoldButtonState)GetValue(CurrentStateProperty);

    public static readonly DependencyProperty CornerRadiusProperty =
        DependencyProperty.Register(
            nameof(CornerRadius),
            typeof(CornerRadius),
            typeof(HubHoldButton),
            new PropertyMetadata(new CornerRadius(6)));

    public CornerRadius CornerRadius
    {
        get => (CornerRadius)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    #endregion

    #region Routed Events

    public static readonly RoutedEvent ConfirmedEvent =
        EventManager.RegisterRoutedEvent(
            nameof(Confirmed),
            RoutingStrategy.Bubble,
            typeof(RoutedEventHandler),
            typeof(HubHoldButton));

    public event RoutedEventHandler Confirmed
    {
        add => AddHandler(ConfirmedEvent, value);
        remove => RemoveHandler(ConfirmedEvent, value);
    }

    #endregion

    static HubHoldButton()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(HubHoldButton),
            new FrameworkPropertyMetadata(typeof(HubHoldButton)));
    }

    public HubHoldButton()
    {
        _stateMachine.HoldDurationMs = HoldDurationMs;
        _stateMachine.Confirmed += OnStateMachineConfirmed;
        _stateMachine.ResetToIdle += OnStateMachineResetToIdle;

        Loaded += (s, e) => UpdateClip(ActualWidth, Progress);
        SizeChanged += (s, e) => UpdateClip(e.NewSize.Width, Progress);
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _fillTrack = GetTemplateChild("PART_FillTrack") as FrameworkElement;
        _invertedContainer = GetTemplateChild("PART_InvertedContainer") as FrameworkElement;
        UpdateClip(ActualWidth, Progress);
    }

    private void OnStateMachineConfirmed()
    {
        Dispatcher.InvokeAsync(() =>
        {
            SetValue(CurrentStatePropertyKey, HoldButtonState.Confirmed);
            SetValue(ProgressPropertyKey, 1.0);
            UpdateClip(ActualWidth, 1.0);

            // Fire RoutedEvent
            RaiseEvent(new RoutedEventArgs(ConfirmedEvent, this));

            // Execute Command
            if (Command != null && Command.CanExecute(CommandParameter))
            {
                Command.Execute(CommandParameter);
            }
        });
    }

    private void OnStateMachineResetToIdle()
    {
        Dispatcher.InvokeAsync(() =>
        {
            SetValue(CurrentStatePropertyKey, HoldButtonState.Idle);
            SetValue(ProgressPropertyKey, 0.0);
            UpdateClip(ActualWidth, 0.0);
            StopTicking();
        });
    }

    private void StartHolding()
    {
        if (!IsEnabled) return;
        if (_stateMachine.State == HoldButtonState.Confirmed) return;

        if (_stateMachine.Press())
        {
            SetValue(CurrentStatePropertyKey, HoldButtonState.Holding);
            StartTicking();
        }
    }

    private void StopHolding()
    {
        if (_stateMachine.State == HoldButtonState.Holding)
        {
            if (_stateMachine.Release())
            {
                SetValue(CurrentStatePropertyKey, HoldButtonState.Releasing);
            }
        }
    }

    private void StartTicking()
    {
        if (_isTicking) return;
        _isTicking = true;
        _lastTick = DateTime.UtcNow;
        CompositionTarget.Rendering += OnRenderingTick;
    }

    private void StopTicking()
    {
        if (!_isTicking) return;
        _isTicking = false;
        CompositionTarget.Rendering -= OnRenderingTick;
    }

    private void OnRenderingTick(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        double dtMs = (now - _lastTick).TotalMilliseconds;
        _lastTick = now;

        if (dtMs <= 0 || dtMs > 100) dtMs = 16.6;

        bool changed = _stateMachine.Step(dtMs);
        if (changed)
        {
            SetValue(ProgressPropertyKey, _stateMachine.Progress);
            SetValue(CurrentStatePropertyKey, _stateMachine.State);
            UpdateClip(ActualWidth, _stateMachine.Progress);
        }

        if (_stateMachine.State == HoldButtonState.Idle)
        {
            StopTicking();
        }
    }

    private static void OnProgressChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is HubHoldButton btn)
        {
            btn.UpdateClip(btn.ActualWidth, (double)e.NewValue);
        }
    }

    private void UpdateClip(double totalWidth, double progress)
    {
        if (totalWidth <= 0) return;

        double filledWidth = Math.Clamp(totalWidth * progress, 0.0, totalWidth);
        double actualHeight = Math.Max(32.0, ActualHeight);

        if (_fillTrack != null)
        {
            _fillTrack.Width = filledWidth;
        }

        if (_invertedContainer != null)
        {
            // Clip region for inverted text
            Rect clipRect;
            if (FlowDirection == FlowDirection.RightToLeft)
            {
                clipRect = new Rect(totalWidth - filledWidth, 0, filledWidth, actualHeight);
            }
            else
            {
                clipRect = new Rect(0, 0, filledWidth, actualHeight);
            }

            _invertedContainer.Clip = new RectangleGeometry(clipRect);
        }
    }

    #region Pointer & Keyboard Handlers

    protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            Focus();
            CaptureMouse();
            StartHolding();
            e.Handled = true;
        }
        base.OnPreviewMouseDown(e);
    }

    protected override void OnPreviewMouseUp(MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            ReleaseMouseCapture();
            StopHolding();
            e.Handled = true;
        }
        base.OnPreviewMouseUp(e);
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }
        StopHolding();
        base.OnMouseLeave(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if ((e.Key == Key.Space || e.Key == Key.Enter) && !e.IsRepeat)
        {
            StartHolding();
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (e.Key == Key.Space || e.Key == Key.Enter)
        {
            StopHolding();
            e.Handled = true;
            return;
        }
        base.OnKeyUp(e);
    }

    protected override void OnLostFocus(RoutedEventArgs e)
    {
        StopHolding();
        base.OnLostFocus(e);
    }

    #endregion
}
