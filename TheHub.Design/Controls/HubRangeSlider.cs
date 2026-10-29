using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using TheHub.Design.Motion;

namespace TheHub.Design.Controls;

/// <summary>
/// Dual-thumb range slider supporting constrained boundaries,
/// keyboard navigation, and an animated value tooltip bubble.
/// </summary>
[TemplatePart(Name = "PART_TrackContainer", Type = typeof(FrameworkElement))]
[TemplatePart(Name = "PART_RangeHighlight", Type = typeof(FrameworkElement))]
[TemplatePart(Name = "PART_LowerThumb", Type = typeof(Thumb))]
[TemplatePart(Name = "PART_UpperThumb", Type = typeof(Thumb))]
[TemplatePart(Name = "PART_ValueBubble", Type = typeof(FrameworkElement))]
[TemplatePart(Name = "PART_BubbleText", Type = typeof(TextBlock))]
public class HubRangeSlider : Control
{
    private FrameworkElement? _trackContainer;
    private FrameworkElement? _rangeHighlight;
    private Thumb? _lowerThumb;
    private Thumb? _upperThumb;
    private FrameworkElement? _valueBubble;
    private TextBlock? _bubbleText;
    private TranslateTransform _bubbleTransform = new();

    private Thumb? _activeThumb;

    #region Dependency Properties

    public static readonly DependencyProperty MinimumProperty =
        DependencyProperty.Register(nameof(Minimum), typeof(double), typeof(HubRangeSlider),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsArrange, OnBoundsChanged));

    public double Minimum
    {
        get => (double)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public static readonly DependencyProperty MaximumProperty =
        DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(HubRangeSlider),
            new FrameworkPropertyMetadata(100.0, FrameworkPropertyMetadataOptions.AffectsArrange, OnBoundsChanged));

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public static readonly DependencyProperty LowerValueProperty =
        DependencyProperty.Register(nameof(LowerValue), typeof(double), typeof(HubRangeSlider),
            new FrameworkPropertyMetadata(20.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault | FrameworkPropertyMetadataOptions.AffectsArrange, OnLowerValueChanged));

    public double LowerValue
    {
        get => (double)GetValue(LowerValueProperty);
        set => SetValue(LowerValueProperty, value);
    }

    public static readonly DependencyProperty UpperValueProperty =
        DependencyProperty.Register(nameof(UpperValue), typeof(double), typeof(HubRangeSlider),
            new FrameworkPropertyMetadata(80.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault | FrameworkPropertyMetadataOptions.AffectsArrange, OnUpperValueChanged));

    public double UpperValue
    {
        get => (double)GetValue(UpperValueProperty);
        set => SetValue(UpperValueProperty, value);
    }

    public static readonly DependencyProperty MinimumDistanceProperty =
        DependencyProperty.Register(nameof(MinimumDistance), typeof(double), typeof(HubRangeSlider),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsArrange));

    public double MinimumDistance
    {
        get => (double)GetValue(MinimumDistanceProperty);
        set => SetValue(MinimumDistanceProperty, value);
    }

    public static readonly DependencyProperty StepProperty =
        DependencyProperty.Register(nameof(Step), typeof(double), typeof(HubRangeSlider),
            new FrameworkPropertyMetadata(1.0));

    public double Step
    {
        get => (double)GetValue(StepProperty);
        set => SetValue(StepProperty, value);
    }

    public static readonly DependencyProperty ValueFormatProperty =
        DependencyProperty.Register(nameof(ValueFormat), typeof(string), typeof(HubRangeSlider),
            new FrameworkPropertyMetadata("{0:0}"));

    public string ValueFormat
    {
        get => (string)GetValue(ValueFormatProperty);
        set => SetValue(ValueFormatProperty, value);
    }

    #endregion

    #region Routed Events

    public static readonly RoutedEvent LowerValueChangedEvent =
        EventManager.RegisterRoutedEvent(nameof(LowerValueChanged), RoutingStrategy.Bubble, typeof(RoutedPropertyChangedEventHandler<double>), typeof(HubRangeSlider));

    public event RoutedPropertyChangedEventHandler<double> LowerValueChanged
    {
        add => AddHandler(LowerValueChangedEvent, value);
        remove => RemoveHandler(LowerValueChangedEvent, value);
    }

    public static readonly RoutedEvent UpperValueChangedEvent =
        EventManager.RegisterRoutedEvent(nameof(UpperValueChanged), RoutingStrategy.Bubble, typeof(RoutedPropertyChangedEventHandler<double>), typeof(HubRangeSlider));

    public event RoutedPropertyChangedEventHandler<double> UpperValueChanged
    {
        add => AddHandler(UpperValueChangedEvent, value);
        remove => RemoveHandler(UpperValueChangedEvent, value);
    }

    #endregion

    static HubRangeSlider()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(HubRangeSlider), new FrameworkPropertyMetadata(typeof(HubRangeSlider)));
    }

    public HubRangeSlider()
    {
        Loaded += (s, e) => UpdateLayoutPositions();
        SizeChanged += (s, e) => UpdateLayoutPositions();
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        _trackContainer = GetTemplateChild("PART_TrackContainer") as FrameworkElement;
        _rangeHighlight = GetTemplateChild("PART_RangeHighlight") as FrameworkElement;
        _lowerThumb = GetTemplateChild("PART_LowerThumb") as Thumb;
        _upperThumb = GetTemplateChild("PART_UpperThumb") as Thumb;
        _valueBubble = GetTemplateChild("PART_ValueBubble") as FrameworkElement;
        _bubbleText = GetTemplateChild("PART_BubbleText") as TextBlock;

        if (_valueBubble != null)
        {
            _valueBubble.RenderTransform = _bubbleTransform;
            _valueBubble.Opacity = 0.0;
        }

        if (_lowerThumb != null)
        {
            _lowerThumb.DragStarted += OnLowerDragStarted;
            _lowerThumb.DragDelta += OnLowerDragDelta;
            _lowerThumb.DragCompleted += OnDragCompleted;
            _lowerThumb.KeyDown += OnThumbKeyDown;
            _lowerThumb.GotFocus += (s, e) => ShowBubbleForThumb(_lowerThumb, LowerValue);
            _lowerThumb.LostFocus += (s, e) => HideBubble();
        }

        if (_upperThumb != null)
        {
            _upperThumb.DragStarted += OnUpperDragStarted;
            _upperThumb.DragDelta += OnUpperDragDelta;
            _upperThumb.DragCompleted += OnDragCompleted;
            _upperThumb.KeyDown += OnThumbKeyDown;
            _upperThumb.GotFocus += (s, e) => ShowBubbleForThumb(_upperThumb, UpperValue);
            _upperThumb.LostFocus += (s, e) => HideBubble();
        }

        UpdateLayoutPositions();
    }

    private static void OnBoundsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is HubRangeSlider slider)
        {
            slider.UpdateLayoutPositions();
        }
    }

    private static void OnLowerValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is HubRangeSlider slider)
        {
            double oldVal = (double)e.OldValue;
            double newVal = (double)e.NewValue;
            slider.RaiseEvent(new RoutedPropertyChangedEventArgs<double>(oldVal, newVal, LowerValueChangedEvent));
            slider.UpdateLayoutPositions();
        }
    }

    private static void OnUpperValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is HubRangeSlider slider)
        {
            double oldVal = (double)e.OldValue;
            double newVal = (double)e.NewValue;
            slider.RaiseEvent(new RoutedPropertyChangedEventArgs<double>(oldVal, newVal, UpperValueChangedEvent));
            slider.UpdateLayoutPositions();
        }
    }

    private void OnLowerDragStarted(object sender, DragStartedEventArgs e)
    {
        _activeThumb = _lowerThumb;
        ShowBubbleForThumb(_lowerThumb, LowerValue);
    }

    private void OnUpperDragStarted(object sender, DragStartedEventArgs e)
    {
        _activeThumb = _upperThumb;
        ShowBubbleForThumb(_upperThumb, UpperValue);
    }

    private void OnDragCompleted(object sender, DragCompletedEventArgs e)
    {
        _activeThumb = null;
        HideBubble();
    }

    private void OnLowerDragDelta(object sender, DragDeltaEventArgs e)
    {
        double trackWidth = GetUsableTrackWidth();
        if (trackWidth <= 0) return;

        double deltaVal = (e.HorizontalChange / trackWidth) * (Maximum - Minimum);
        if (FlowDirection == FlowDirection.RightToLeft) deltaVal = -deltaVal;

        double newLower = RangeSliderMath.ClampLower(
            LowerValue + deltaVal,
            UpperValue,
            Minimum,
            Maximum,
            MinimumDistance,
            Step);

        LowerValue = newLower;
        ShowBubbleForThumb(_lowerThumb, LowerValue);
    }

    private void OnUpperDragDelta(object sender, DragDeltaEventArgs e)
    {
        double trackWidth = GetUsableTrackWidth();
        if (trackWidth <= 0) return;

        double deltaVal = (e.HorizontalChange / trackWidth) * (Maximum - Minimum);
        if (FlowDirection == FlowDirection.RightToLeft) deltaVal = -deltaVal;

        double newUpper = RangeSliderMath.ClampUpper(
            LowerValue,
            UpperValue + deltaVal,
            Minimum,
            Maximum,
            MinimumDistance,
            Step);

        UpperValue = newUpper;
        ShowBubbleForThumb(_upperThumb, UpperValue);
    }

    private void OnThumbKeyDown(object sender, KeyEventArgs e)
    {
        bool isLower = sender == _lowerThumb;
        double current = isLower ? LowerValue : UpperValue;
        double target = current;
        double effectiveStep = Step > 0 ? Step : 1.0;

        switch (e.Key)
        {
            case Key.Left:
            case Key.Down:
                target -= (FlowDirection == FlowDirection.RightToLeft && e.Key == Key.Left) ? -effectiveStep : effectiveStep;
                e.Handled = true;
                break;

            case Key.Right:
            case Key.Up:
                target += (FlowDirection == FlowDirection.RightToLeft && e.Key == Key.Right) ? -effectiveStep : effectiveStep;
                e.Handled = true;
                break;

            case Key.Home:
                target = Minimum;
                e.Handled = true;
                break;

            case Key.End:
                target = Maximum;
                e.Handled = true;
                break;
        }

        if (e.Handled)
        {
            if (isLower)
            {
                LowerValue = RangeSliderMath.ClampLower(target, UpperValue, Minimum, Maximum, MinimumDistance, Step);
                ShowBubbleForThumb(_lowerThumb, LowerValue);
            }
            else
            {
                UpperValue = RangeSliderMath.ClampUpper(LowerValue, target, Minimum, Maximum, MinimumDistance, Step);
                ShowBubbleForThumb(_upperThumb, UpperValue);
            }
        }
    }

    private double GetUsableTrackWidth()
    {
        double totalWidth = ActualWidth > 0 ? ActualWidth : Width;
        if (double.IsNaN(totalWidth) || totalWidth <= 0) totalWidth = 200.0;
        double thumbSize = _lowerThumb?.ActualWidth ?? 16.0;
        return Math.Max(0.0, totalWidth - thumbSize);
    }

    private void UpdateLayoutPositions()
    {
        double trackWidth = GetUsableTrackWidth();
        if (trackWidth <= 0) return;

        double thumbSize = _lowerThumb?.ActualWidth ?? 16.0;
        double lowerRatio = RangeSliderMath.ValueToRatio(LowerValue, Minimum, Maximum);
        double upperRatio = RangeSliderMath.ValueToRatio(UpperValue, Minimum, Maximum);

        double lowerX = lowerRatio * trackWidth;
        double upperX = upperRatio * trackWidth;

        if (FlowDirection == FlowDirection.RightToLeft)
        {
            lowerX = trackWidth - lowerX;
            upperX = trackWidth - upperX;
            if (lowerX > upperX) (lowerX, upperX) = (upperX, lowerX);
        }

        if (_lowerThumb != null)
        {
            Canvas.SetLeft(_lowerThumb, FlowDirection == FlowDirection.RightToLeft ? trackWidth - (lowerRatio * trackWidth) : lowerX);
        }

        if (_upperThumb != null)
        {
            Canvas.SetLeft(_upperThumb, FlowDirection == FlowDirection.RightToLeft ? trackWidth - (upperRatio * trackWidth) : upperX);
        }

        if (_rangeHighlight != null)
        {
            double highlightLeft = Math.Min(lowerX, upperX) + (thumbSize / 2.0);
            double highlightWidth = Math.Max(0.0, Math.Abs(upperX - lowerX));
            Canvas.SetLeft(_rangeHighlight, highlightLeft);
            _rangeHighlight.Width = highlightWidth;
        }
    }

    private void ShowBubbleForThumb(Thumb? thumb, double value)
    {
        if (thumb == null || _valueBubble == null) return;

        if (_bubbleText != null)
        {
            _bubbleText.Text = string.Format(ValueFormat, value);
        }

        double thumbCenter = Canvas.GetLeft(thumb) + (thumb.ActualWidth / 2.0);
        double bubbleWidth = _valueBubble.ActualWidth > 0 ? _valueBubble.ActualWidth : 36.0;
        _bubbleTransform.X = thumbCenter - (bubbleWidth / 2.0);

        _valueBubble.FadeTo(1.0, MotionTokens.FadeFastMs);
    }

    private void HideBubble()
    {
        _valueBubble?.FadeTo(0.0, MotionTokens.FadeFastMs);
    }
}
