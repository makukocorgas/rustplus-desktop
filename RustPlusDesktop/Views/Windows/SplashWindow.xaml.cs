using System.Windows;

namespace RustPlusDesk.Views.Windows;

public partial class SplashWindow : Window
{
    public SplashWindow()
    {
        InitializeComponent();
        VersionText.Text = "v" + RustPlusDesk.Helpers.VersionHelper.GetClientVersion();
    }

    public void SetStatus(string message)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => SetStatus(message));
            return;
        }
        StatusText.Text = message;
    }
}
