using System.ComponentModel;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using TheHub.Design.Motion;

namespace TheHub.Design.Controls;

/// <summary>
/// Upgraded toggle switch featuring spring-driven thumb slide,
/// track cross-fade, and a dynamic 1.15x press-stretch response.
/// </summary>
[TemplatePart(Name = "PART_Track", Type = typeof(FrameworkElement))]
[TemplatePart(Name = "PART_Thumb", Type = typeof(FrameworkElement))]
public class HubToggle : ToggleButton
{
    private FrameworkElement? _track;
    private FrameworkElement? _thumb;
    private readonly TranslateTransform _thumbTranslate = new();
    private readonly ScaleTransform _thumbScale = new(1.0, 1.0);
    private double _currentTranslateX = 0.0;
    private double _currentScaleX = 1.0;

    static HubToggle()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(HubToggle),
            new FrameworkPropertyMetadata(typeof(HubToggle)));
    }

    public HubToggle()
    {
        Loaded += (s, e) => UpdateVisualState(instant: true);

        // Observe IsPressed changes dynamically
        DependencyPropertyDescriptor.FromProperty(IsPressedProperty, typeof(HubToggle))
            ?.AddValueChanged(this, (s, e) => OnIsPressedChanged(IsPressed));
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _track = GetTemplateChild("PART_Track") as FrameworkElement;
        _thumb = GetTemplateChild("PART_Thumb") as FrameworkElement;

        if (_thumb != null)
        {
            var group = new TransformGroup();
            group.Children.Add(_thumbScale);
            group.Children.Add(_thumbTranslate);
            _thumb.RenderTransform = group;
            _thumb.RenderTransformOrigin = new Point(0.5, 0.5);
        }

        UpdateVisualState(instant: true);
    }

    protected override void OnChecked(RoutedEventArgs e)
    {
        base.OnChecked(e);
        UpdateVisualState(instant: false);
    }

    protected override void OnUnchecked(RoutedEventArgs e)
    {
        base.OnUnchecked(e);
        UpdateVisualState(instant: false);
    }

    private void OnIsPressedChanged(bool pressed)
    {
        double targetScaleX = pressed ? 1.15 : 1.0;

        string key = $"toggle_scale_{GetHashCode()}";
        SpringAnimator.AnimateValue(
            key,
            _currentScaleX,
            targetScaleX,
            SpringConfig.Snappy,
            val =>
            {
                _currentScaleX = val;
                _thumbScale.ScaleX = val;
            });
    }

    private void UpdateVisualState(bool instant)
    {
        if (_track == null || _thumb == null) return;

        double trackWidth = _track.ActualWidth > 0 ? _track.ActualWidth : 36.0;
        double thumbWidth = _thumb.ActualWidth > 0 ? _thumb.ActualWidth : 14.0;
        double margin = 3.0;
        double travelDistance = Math.Max(0.0, trackWidth - thumbWidth - (2 * margin));

        bool isChecked = IsChecked == true;
        double targetX = isChecked ? travelDistance : 0.0;

        if (FlowDirection == FlowDirection.RightToLeft)
        {
            targetX = -targetX;
        }

        if (instant)
        {
            _currentTranslateX = targetX;
            _thumbTranslate.X = targetX;
            return;
        }

        // Animate thumb slide with Spring.Snappy
        string key = $"toggle_slide_{GetHashCode()}";
        SpringAnimator.AnimateValue(
            key,
            _currentTranslateX,
            targetX,
            SpringConfig.Snappy,
            val =>
            {
                _currentTranslateX = val;
                _thumbTranslate.X = val;
            });
    }
}
