using System.Windows;
using System.Windows.Controls;

namespace TheHub.Design.Controls;

public partial class SectionHeader : UserControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(SectionHeader),
            new PropertyMetadata(string.Empty, (d, e) => ((SectionHeader)d).TxtTitle.Text = (string)e.NewValue));

    public static readonly DependencyProperty SubtitleProperty =
        DependencyProperty.Register(nameof(Subtitle), typeof(string), typeof(SectionHeader),
            new PropertyMetadata(string.Empty, (d, e) =>
            {
                var sh = (SectionHeader)d;
                string val = (string)e.NewValue;
                sh.TxtSubtitle.Text = val;
                sh.TxtSubtitle.Visibility = string.IsNullOrWhiteSpace(val) ? Visibility.Collapsed : Visibility.Visible;
            }));

    public static readonly DependencyProperty ActionsProperty =
        DependencyProperty.Register(nameof(Actions), typeof(object), typeof(SectionHeader),
            new PropertyMetadata(null, (d, e) => ((SectionHeader)d).ActionsHost.Content = e.NewValue));

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Subtitle
    {
        get => (string)GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    public object Actions
    {
        get => GetValue(ActionsProperty);
        set => SetValue(ActionsProperty, value);
    }

    public SectionHeader()
    {
        InitializeComponent();
    }
}
