using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace TrackBridge
{
    public partial class DownloadAreaWindow : Window
    {
        // Holds the drawn rectangle bounds
        private double _south, _west, _north, _east;

        public DownloadAreaWindow()
        {
            InitializeComponent();
            InitializeAsync();

            // Initialize sliders and display values
            MinZoomSlider.Value = 0;
            MaxZoomSlider.Value = 5;
            MinZoomValue.Text = "0";
            MaxZoomValue.Text = "5";

            MinZoomSlider.ValueChanged += (s, e) => MinZoomValue.Text = ((int)e.NewValue).ToString();
            MaxZoomSlider.ValueChanged += (s, e) => MaxZoomValue.Text = ((int)e.NewValue).ToString();
        }

        /// <summary>
        /// Sets up WebView2, maps the correct assets folder, and loads draw.html.
        /// </summary>
        private async void InitializeAsync()
        {
            var env = await CoreWebView2Environment.CreateAsync();
            await DownloadMapView.EnsureCoreWebView2Async(env);

            var core = DownloadMapView.CoreWebView2;

            // Determine where your HTML files live
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string htmlDir = Path.Combine(baseDir, "html");
            string assetsDir;

            if (Directory.Exists(htmlDir))
            {
                assetsDir = htmlDir;
            }
            else if (Directory.Exists(baseDir))
            {
                assetsDir = baseDir;
            }
            else
            {
                MessageBox.Show(
                    $"Cannot find HTML assets in either:\n  {htmlDir}\n  {baseDir}",
                    "Asset Folder Missing",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            // Map the chosen directory under the virtual host "appassets"
            core.SetVirtualHostNameToFolderMapping(
                "appassets",
                assetsDir,
                CoreWebView2HostResourceAccessKind.Allow);

            // Listen for bounds messages from the page
            core.WebMessageReceived += OnWebMessageReceived;

            // Finally, navigate to draw.html
            core.Navigate("https://appassets/draw.html");
        }

        /// <summary>
        /// Receives the rectangle bounds posted from draw.html.
        /// </summary>
        private void OnWebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                var msg = JsonSerializer.Deserialize<BoundsMessage>(e.WebMessageAsJson);
                _south = msg.south;
                _west = msg.west;
                _north = msg.north;
                _east = msg.east;
            }
            catch
            {
                // Ignore if the message is not the expected format
            }
        }

        /// <summary>
        /// Triggered when the user clicks Download Tiles.
        /// </summary>
        private void DownloadTilesButton_Click(object sender, RoutedEventArgs e)
        {
            int minZ = (int)MinZoomSlider.Value;
            int maxZ = (int)MaxZoomSlider.Value;

            if (_north == 0 && _south == 0 && _east == 0 && _west == 0)
            {
                MessageBox.Show(
                    "Please draw a rectangle on the map first.",
                    "No Area Selected",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            // TODO: Kick off your tile download using (_south,_west) to (_north,_east) at zooms minZ–maxZ
            MessageBox.Show(
                $"Downloading tiles for area:\n" +
                $"SW({_south:F4}, {_west:F4}) to NE({_north:F4}, {_east:F4})\n" +
                $"Zoom levels {minZ} to {maxZ}.",
                "Download Started",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            // Optionally close the window:
            // this.Close();
        }

        // DTO for JSON message from the page
        private class BoundsMessage
        {
            public double south { get; set; }
            public double west { get; set; }
            public double north { get; set; }
            public double east { get; set; }
        }
    }
}
