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

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    public static extern bool ReleaseCapture();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    public static extern System.IntPtr SendMessage(System.IntPtr hWnd, int Msg, int wParam, int lParam);

    private void OnHeaderMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            try
            {
                ReleaseCapture();
                var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                if (handle != System.IntPtr.Zero)
                {
                    SendMessage(handle, 0xA1, 2, 0);
                }
            }
            catch { }
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
