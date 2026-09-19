using System.Windows;
using System.Windows.Input;

namespace TheHub.App.Controls;

public enum ExitDialogChoice
{
    Cancel,
    HideDashboardOnly,
    ExitCompletely
}

public partial class ExitDialog : Window
{
    public ExitDialogChoice Choice { get; private set; } = ExitDialogChoice.Cancel;

    public ExitDialog()
    {
        InitializeComponent();
    }

    private void OnHeaderMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void OnHideDashboardClick(object sender, MouseButtonEventArgs e)
    {
        Choice = ExitDialogChoice.HideDashboardOnly;
        DialogResult = true;
        Close();
    }

    private void OnExitCompletelyClick(object sender, MouseButtonEventArgs e)
    {
        Choice = ExitDialogChoice.ExitCompletely;
        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        Choice = ExitDialogChoice.Cancel;
        DialogResult = false;
        Close();
    }
}
