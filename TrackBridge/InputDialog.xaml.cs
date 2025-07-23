using System.Windows;

namespace TrackBridge
{
    public partial class InputDialog : Window
    {
        public string ResponseText => NameTextBox.Text.Trim();

        public InputDialog(string title = "Enter Profile Name")
        {
            InitializeComponent();
            Title = title;
            NameTextBox.Focus();
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(ResponseText))
                DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
