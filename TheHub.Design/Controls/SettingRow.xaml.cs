using System.Windows;
using System.Windows.Controls;

namespace TheHub.Design.Controls;

public partial class SettingRow : UserControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(SettingRow),
            new PropertyMetadata(string.Empty, (d, e) => ((SettingRow)d).TxtTitle.Text = (string)e.NewValue));

    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.Register(nameof(Description), typeof(string), typeof(SettingRow),
            new PropertyMetadata(string.Empty, (d, e) =>
            {
                var sr = (SettingRow)d;
                string val = (string)e.NewValue;
                sr.TxtDescription.Text = val;
                sr.TxtDescription.Visibility = string.IsNullOrWhiteSpace(val) ? Visibility.Collapsed : Visibility.Visible;
            }));

    public static readonly DependencyProperty ControlContentProperty =
        DependencyProperty.Register(nameof(ControlContent), typeof(object), typeof(SettingRow),
            new PropertyMetadata(null, (d, e) => ((SettingRow)d).ControlHost.Content = e.NewValue));

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

    public object ControlContent
    {
        get => GetValue(ControlContentProperty);
        set => SetValue(ControlContentProperty, value);
    }

    public SettingRow()
    {
        InitializeComponent();
    }
}
