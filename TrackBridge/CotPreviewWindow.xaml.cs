using System.Windows;

namespace TrackBridge
{
    /// <summary>
    /// Interaction logic for CotPreviewWindow.xaml
    /// Displays CoT XML in a read-only text box.
    /// </summary>
    public partial class CotPreviewWindow : Window
    {
        /// <summary>
        /// Constructor that accepts a CoT XML string and displays it in the preview box.
        /// </summary>
        /// <param name="cotXml">The CoT message to display.</param>
        public CotPreviewWindow(string cotXml)
        {
            InitializeComponent();
            CotXmlTextBox.Text = cotXml;
        }

        /// <summary>
        /// Close the window when the Close button is clicked.
        /// </summary>
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
