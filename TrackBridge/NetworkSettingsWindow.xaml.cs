using System;
using System.Net;
using System.Net.Sockets;
using System.Windows;

namespace TrackBridge
{
    public partial class NetworkSettingsWindow : Window
    {
        // Parameterless ctor now uses the machine's IPv4 for DIS IP
        public NetworkSettingsWindow()
            : this(
                GetLocalIPAddress(),         // use detected IP here
                NetworkConfig.DisPort,
                NetworkConfig.CotIp,
                NetworkConfig.CotPort)
        {
        }

        // Original constructor: populates the UI with passed-in values
        public NetworkSettingsWindow(string disIp, int disPort, string cotIp, int cotPort)
        {
            InitializeComponent();

            DisIpTextBox.Text = disIp;
            DisPortTextBox.Text = disPort.ToString();
            CotIpTextBox.Text = cotIp;
            CotPortTextBox.Text = cotPort.ToString();
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
            NetworkConfig.DisIp = DisIpTextBox.Text;
            NetworkConfig.DisPort = int.Parse(DisPortTextBox.Text);
            NetworkConfig.CotIp = CotIpTextBox.Text;
            NetworkConfig.CotPort = int.Parse(CotPortTextBox.Text);

            DialogResult = true;
            Close();
        }

        // Cancel button handler
        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
