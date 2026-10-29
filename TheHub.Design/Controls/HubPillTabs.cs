using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using TheHub.Design.Motion;

namespace TheHub.Design.Controls;

/// <summary>
/// Segmented pill navigation tabs with a sliding background indicator
/// animated via Spring.Smooth and optional collapse-when-idle behavior.
/// </summary>
[TemplatePart(Name = "PART_Indicator", Type = typeof(FrameworkElement))]
public class HubPillTabs : ListBox
{
    private FrameworkElement? _indicator;
    private TranslateTransform _indicatorTranslate = new();
    private ScaleTransform _indicatorScale = new(1.0, 1.0);
    private double _currentIndicatorX = 0.0;
    private double _currentIndicatorWidth = 0.0;
    private DispatcherTimer? _collapseTimer;

    #region Dependency Properties

    public static readonly DependencyProperty CollapseWhenIdleProperty =
        DependencyProperty.Register(
            nameof(CollapseWhenIdle),
            typeof(bool),
            typeof(HubPillTabs),
            new PropertyMetadata(false, OnCollapseWhenIdleChanged));

    public bool CollapseWhenIdle
    {
        get => (bool)GetValue(CollapseWhenIdleProperty);
        set => SetValue(CollapseWhenIdleProperty, value);
    }

    private static readonly DependencyPropertyKey IsCollapsedPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(IsCollapsed),
            typeof(bool),
            typeof(HubPillTabs),
            new PropertyMetadata(false));

    public static readonly DependencyProperty IsCollapsedProperty = IsCollapsedPropertyKey.DependencyProperty;

    public bool IsCollapsed => (bool)GetValue(IsCollapsedProperty);

    #endregion

    static HubPillTabs()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(HubPillTabs),
            new FrameworkPropertyMetadata(typeof(HubPillTabs)));
    }

    public HubPillTabs()
    {
        SelectionMode = SelectionMode.Single;
        Loaded += (s, e) => Dispatcher.InvokeAsync(UpdateIndicatorPosition, DispatcherPriority.Loaded);
        SizeChanged += (s, e) => Dispatcher.InvokeAsync(UpdateIndicatorPosition, DispatcherPriority.Render);

        MouseEnter += OnTabsMouseEnter;
        MouseLeave += OnTabsMouseLeave;
    }

    protected override bool IsItemItsOwnContainerOverride(object item) => item is HubPillTabItem;

    protected override DependencyObject GetContainerForItemOverride() => new HubPillTabItem();

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _indicator = GetTemplateChild("PART_Indicator") as FrameworkElement;

        if (_indicator != null)
        {
            var group = new TransformGroup();
            group.Children.Add(_indicatorScale);
            group.Children.Add(_indicatorTranslate);
            _indicator.RenderTransform = group;
        }

        Dispatcher.InvokeAsync(UpdateIndicatorPosition, DispatcherPriority.Loaded);
    }

    protected override void OnSelectionChanged(SelectionChangedEventArgs e)
    {
        base.OnSelectionChanged(e);
        Dispatcher.InvokeAsync(UpdateIndicatorPosition, DispatcherPriority.Render);
    }

    private static void OnCollapseWhenIdleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is HubPillTabs tabs && !(bool)e.NewValue)
        {
            tabs.SetValue(IsCollapsedPropertyKey, false);
            tabs.ApplyCollapsedState(false);
        }
    }

    private void OnTabsMouseEnter(object sender, MouseEventArgs e)
    {
        _collapseTimer?.Stop();
        if (CollapseWhenIdle && IsCollapsed)
        {
            SetValue(IsCollapsedPropertyKey, false);
            ApplyCollapsedState(false);
        }
    }

    private void OnTabsMouseLeave(object sender, MouseEventArgs e)
    {
        if (!CollapseWhenIdle) return;

        _collapseTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _collapseTimer.Tick -= OnCollapseTimerTick;
        _collapseTimer.Tick += OnCollapseTimerTick;
        _collapseTimer.Start();
    }

    private void OnCollapseTimerTick(object? sender, EventArgs e)
    {
        _collapseTimer?.Stop();
        if (CollapseWhenIdle && !IsMouseOver)
        {
            SetValue(IsCollapsedPropertyKey, true);
            ApplyCollapsedState(true);
        }
    }

    private void ApplyCollapsedState(bool collapsed)
    {
        for (int i = 0; i < Items.Count; i++)
        {
            if (ItemContainerGenerator.ContainerFromIndex(i) is FrameworkElement container)
            {
                if (collapsed && i != SelectedIndex)
                {
                    container.FadeTo(0.0, MotionTokens.FadeFastMs, () => container.Visibility = Visibility.Collapsed);
                }
                else
                {
                    container.Visibility = Visibility.Visible;
                    container.FadeTo(1.0, MotionTokens.FadeFastMs);
                }
            }
        }
        Dispatcher.InvokeAsync(UpdateIndicatorPosition, DispatcherPriority.Render);
    }

    public void UpdateIndicatorPosition()
    {
        if (_indicator == null || SelectedIndex < 0 || SelectedIndex >= Items.Count)
        {
            if (_indicator != null) _indicator.Opacity = 0.0;
            return;
        }

        if (ItemContainerGenerator.ContainerFromIndex(SelectedIndex) is not FrameworkElement container)
        {
            return;
        }

        try
        {
            GeneralTransform transform = container.TransformToAncestor(this);
            Point point = transform.Transform(new Point(0, 0));

            double targetX = point.X;
            double targetWidth = container.ActualWidth;

            if (targetWidth <= 0) return;

            _indicator.Opacity = 1.0;

            if (_currentIndicatorWidth <= 0)
            {
                // First initialization - instant placement
                _currentIndicatorX = targetX;
                _currentIndicatorWidth = targetWidth;
                _indicatorTranslate.X = targetX;
                _indicator.Width = targetWidth;
                return;
            }

            // Animate position using Spring.Smooth
            string animKeyX = $"pill_x_{GetHashCode()}";
            string animKeyW = $"pill_w_{GetHashCode()}";

            double startX = _currentIndicatorX;
            double startW = _currentIndicatorWidth;

            SpringAnimator.AnimateValue(
                animKeyX,
                startX,
                targetX,
                SpringConfig.Smooth,
                val =>
                {
                    _currentIndicatorX = val;
                    _indicatorTranslate.X = val;
                });

            SpringAnimator.AnimateValue(
                animKeyW,
                startW,
                targetWidth,
                SpringConfig.Smooth,
                val =>
                {
                    _currentIndicatorWidth = val;
                    _indicator.Width = Math.Max(0.0, val);
                });
        }
        catch
        {
            // Fallback during layout pass before tree attachment
        }
    }

    #region Keyboard Navigation

    protected override void OnKeyDown(KeyEventArgs e)
    {
        bool isRtl = FlowDirection == FlowDirection.RightToLeft;

        switch (e.Key)
        {
            case Key.Left:
                MoveSelection(isRtl ? 1 : -1);
                e.Handled = true;
                break;

            case Key.Right:
                MoveSelection(isRtl ? -1 : 1);
                e.Handled = true;
                break;

            case Key.Home:
                if (Items.Count > 0) SelectedIndex = 0;
                e.Handled = true;
                break;

            case Key.End:
                if (Items.Count > 0) SelectedIndex = Items.Count - 1;
                e.Handled = true;
                break;
        }

        base.OnKeyDown(e);
    }

    private void MoveSelection(int delta)
    {
        if (Items.Count == 0) return;

        int newIndex = Math.Clamp(SelectedIndex + delta, 0, Items.Count - 1);
        if (newIndex != SelectedIndex)
        {
            SelectedIndex = newIndex;
        }
    }

    #endregion
}
