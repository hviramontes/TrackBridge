using System;
using System.Net;
using System.Net.Sockets;
using System.Windows;

namespace TrackBridge
{
    public partial class NetworkSettingsWindow : Window
    {
        public NetworkSettingsWindow()
        {
            InitializeComponent();
            Console.WriteLine($"Loading CoT IP: {NetworkConfig.CotIp}");

            // Load current NetworkConfig values
            DisIpTextBox.Text = NetworkConfig.DisIp;
            DisPortTextBox.Text = NetworkConfig.DisPort.ToString();
            CotIpTextBox.Text = NetworkConfig.CotIp;
            CotPortTextBox.Text = NetworkConfig.CotPort.ToString();
        }

        // Helper: get the first non-loopback IPv4, or fall back to existing config
        private static string GetLocalIPAddress()
        {
            try
            {
                // Look up the host's address entries
                var host = Dns.GetHostEntry(Dns.GetHostName());
                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork
                        && !IPAddress.IsLoopback(ip))
                    {
                        return ip.ToString();
                    }
                }
            }
            catch
            {
                // ignore any errors, fall back to the configured value
            }
            // If detection failed, use whatever is in NetworkConfig
            return NetworkConfig.DisIp;
        }

        // Save button handler
        private void Save_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Store old values
                string oldDisIp = NetworkConfig.DisIp;
                int oldDisPort = NetworkConfig.DisPort;
                string oldCotIp = NetworkConfig.CotIp;
                int oldCotPort = NetworkConfig.CotPort;

                // Update new values
                string newDisIp = DisIpTextBox.Text;
                int newDisPort = int.Parse(DisPortTextBox.Text);
                string newCotIp = CotIpTextBox.Text;
                int newCotPort = int.Parse(CotPortTextBox.Text);

                // Log changes
                if (Owner is MainWindow mainWindow)
                {
                    if (newDisIp != oldDisIp)
                        mainWindow.Log($"[INFO] DIS IP changed to {newDisIp}");
                    if (newDisPort != oldDisPort)
                        mainWindow.Log($"[INFO] DIS Port changed to {newDisPort}");
                    if (newCotIp != oldCotIp)
                        mainWindow.Log($"[INFO] CoT IP changed to {newCotIp}");
                    if (newCotPort != oldCotPort)
                        mainWindow.Log($"[INFO] CoT Port changed to {newCotPort}");
                }

                // Apply changes
                NetworkConfig.DisIp = newDisIp;
                NetworkConfig.DisPort = newDisPort;
                NetworkConfig.CotIp = newCotIp;
                NetworkConfig.CotPort = newCotPort;

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Invalid input: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Cancel button handler
        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}