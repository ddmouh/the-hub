using System.Windows;
using System.Windows.Controls;

namespace TheHub.Design.Controls;

/// <summary>
/// A standardized two-column settings row matching DESIGN_SYSTEM.md §5.
/// Hosts arbitrary controls (toggles, sliders, dropdowns) on the right
/// while displaying Title and Description on the left.
/// </summary>
public class SettingRow : ContentControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(SettingRow),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.Register(nameof(Description), typeof(string), typeof(SettingRow),
            new PropertyMetadata(string.Empty));

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    static SettingRow()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(SettingRow),
            new FrameworkPropertyMetadata(typeof(SettingRow)));
    }
}
