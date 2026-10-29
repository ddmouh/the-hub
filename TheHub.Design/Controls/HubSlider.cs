using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using TheHub.Design.Motion;

namespace TheHub.Design.Controls;

/// <summary>
/// Upgraded single-thumb slider featuring a 4px track, 16px thumb,
/// full keyboard navigation, and an animated value bubble over the thumb.
/// </summary>
[TemplatePart(Name = "PART_Track", Type = typeof(Track))]
[TemplatePart(Name = "PART_ValueBubble", Type = typeof(FrameworkElement))]
[TemplatePart(Name = "PART_BubbleText", Type = typeof(TextBlock))]
public class HubSlider : Slider
{
    private Track? _track;
    private FrameworkElement? _valueBubble;
    private TextBlock? _bubbleText;
    private TranslateTransform _bubbleTransform = new();

    #region Dependency Properties

    public static readonly DependencyProperty ValueFormatProperty =
        DependencyProperty.Register(
            nameof(ValueFormat),
            typeof(string),
            typeof(HubSlider),
            new PropertyMetadata("{0:0}"));

    public string ValueFormat
    {
        get => (string)GetValue(ValueFormatProperty);
        set => SetValue(ValueFormatProperty, value);
    }

    #endregion

    static HubSlider()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(HubSlider),
            new FrameworkPropertyMetadata(typeof(HubSlider)));
    }

    public HubSlider()
    {
        Loaded += (s, e) => UpdateBubblePosition();
        SizeChanged += (s, e) => UpdateBubblePosition();
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        _track = GetTemplateChild("PART_Track") as Track;
        _valueBubble = GetTemplateChild("PART_ValueBubble") as FrameworkElement;
        _bubbleText = GetTemplateChild("PART_BubbleText") as TextBlock;

        if (_valueBubble != null)
        {
            _valueBubble.RenderTransform = _bubbleTransform;
            _valueBubble.Opacity = 0.0;
        }

        if (_track?.Thumb != null)
        {
            _track.Thumb.DragStarted += OnThumbDragStarted;
            _track.Thumb.DragDelta += OnThumbDragDelta;
            _track.Thumb.DragCompleted += OnThumbDragCompleted;
        }

        UpdateBubblePosition();
    }

    protected override void OnValueChanged(double oldValue, double newValue)
    {
        base.OnValueChanged(oldValue, newValue);
        UpdateBubblePosition();
    }

    private void OnThumbDragStarted(object sender, DragStartedEventArgs e)
    {
        ShowBubble();
    }

    private void OnThumbDragDelta(object sender, DragDeltaEventArgs e)
    {
        ShowBubble();
    }

    private void OnThumbDragCompleted(object sender, DragCompletedEventArgs e)
    {
        HideBubble();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End or Key.PageUp or Key.PageDown)
        {
            ShowBubble();
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End or Key.PageUp or Key.PageDown)
        {
            // Delay hide slightly on keyboard adjust
            Dispatcher.InvokeAsync(async () =>
            {
                await Task.Delay(600);
                if (!IsKeyboardFocusWithin) HideBubble();
            });
        }
    }

    protected override void OnLostFocus(RoutedEventArgs e)
    {
        base.OnLostFocus(e);
        HideBubble();
    }

    private void ShowBubble()
    {
        UpdateBubblePosition();
        _valueBubble?.FadeTo(1.0, MotionTokens.FadeFastMs);
    }

    private void HideBubble()
    {
        _valueBubble?.FadeTo(0.0, MotionTokens.FadeFastMs);
    }

    private void UpdateBubblePosition()
    {
        if (_bubbleText != null)
        {
            _bubbleText.Text = string.Format(ValueFormat, Value);
        }

        if (_track?.Thumb == null || _valueBubble == null) return;

        try
        {
            Point thumbPos = _track.Thumb.TranslatePoint(new Point(0, 0), this);
            double thumbCenter = thumbPos.X + (_track.Thumb.ActualWidth / 2.0);
            double bubbleWidth = _valueBubble.ActualWidth > 0 ? _valueBubble.ActualWidth : 36.0;

            _bubbleTransform.X = thumbCenter - (bubbleWidth / 2.0);
        }
        catch
        {
            // Skip layout pass before element is connected
        }
    }
}
