using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace TheHub.App;

/// <summary>
/// Interaction logic for OverviewView.xaml
/// Hosts the JaxCore-inspired Modular Cards Grid for Desktop Widgets &amp; Dynamic Island.
/// </summary>
public partial class OverviewView : System.Windows.Controls.UserControl
{
    public OverviewView()
    {
        InitializeComponent();
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is ScrollViewer scroller)
        {
            // Bubble inner PreviewMouseWheel to outer ScrollViewer when inner controls consume it
            if (!e.Handled)
            {
                scroller.ScrollToVerticalOffset(scroller.VerticalOffset - e.Delta);
                e.Handled = true;
            }
        }
    }

    private void OnAdvancedColorChanged(System.Windows.Media.Color color)
    {
        try
        {
             string hex = color.A == 255
                 ? $"#{color.R:X2}{color.G:X2}{color.B:X2}"
                 : $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
            if (DataContext is OverviewViewModel vm)
            {
                vm.HexColor = hex;
            }
        }
        catch { }
    }

    // ── Accordion toggle: CyberClock card ────────────────────────────────────
    private void OnToggleClockExpand(object sender, RoutedEventArgs e)
    {
        bool isExpanded = ClockExpandedSection.Visibility == Visibility.Visible;
        ClockExpandedSection.Visibility = isExpanded ? Visibility.Collapsed : Visibility.Visible;
        BtnToggleClockExpand.Content = isExpanded ? "▼  Customize" : "▲  Collapse";
    }

    // ── Accordion toggle: System Gauges card ─────────────────────────────────
    private void OnToggleGaugesExpand(object sender, RoutedEventArgs e)
    {
        bool isExpanded = GaugesExpandedSection.Visibility == Visibility.Visible;
        GaugesExpandedSection.Visibility = isExpanded ? Visibility.Collapsed : Visibility.Visible;
        BtnToggleGaugesExpand.Content = isExpanded ? "▼  Customize" : "▲  Collapse";
    }
}
