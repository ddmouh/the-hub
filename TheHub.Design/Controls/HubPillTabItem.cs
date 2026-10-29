using System.Windows;
using System.Windows.Controls;

namespace TheHub.Design.Controls;

/// <summary>
/// Individual tab item inside HubPillTabs.
/// </summary>
public class HubPillTabItem : ListBoxItem
{
    static HubPillTabItem()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(HubPillTabItem),
            new FrameworkPropertyMetadata(typeof(HubPillTabItem)));
    }
}
