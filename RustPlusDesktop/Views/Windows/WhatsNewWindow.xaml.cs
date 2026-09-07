using System.Windows;

namespace RustPlusDesk.Views.Windows
{
    public partial class WhatsNewWindow : Wpf.Ui.Controls.FluentWindow
    {
        public bool DontShowAgain => ChkDontShowAgain.IsChecked == true;

        public WhatsNewWindow()
        {
            InitializeComponent();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
