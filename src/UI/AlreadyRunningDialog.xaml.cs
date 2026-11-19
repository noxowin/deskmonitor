using System.Windows;

namespace deskmonitor
{
    public partial class AlreadyRunningDialog : Window
    {
        public AlreadyRunningDialog(string message)
        {
            InitializeComponent();
            MessageText.Text = message;
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
