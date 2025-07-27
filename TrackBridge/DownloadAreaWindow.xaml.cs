using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;

namespace TrackBridge
{
    public partial class DownloadAreaWindow : Window
    {
        // Holds the drawn rectangle bounds
        private double _south, _west, _north, _east;

        // Shared HTTP client for fetching tiles
        private static readonly HttpClient _httpClient = new HttpClient();

        // Cancellation support
        private CancellationTokenSource _downloadCts;

        // Stopwatch for ETA calculation
        private Stopwatch _downloadStopwatch;

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

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string htmlDir = Path.Combine(baseDir, "html");
            string assetsDir = Directory.Exists(htmlDir) ? htmlDir : baseDir;

            core.SetVirtualHostNameToFolderMapping(
                "appassets",
                assetsDir,
                CoreWebView2HostResourceAccessKind.Allow);

            core.WebMessageReceived += OnWebMessageReceived;
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

        /// <summary>Convert longitude to tile X at zoom z.</summary>
        private int LonToTileX(double lon, int z)
            => (int)Math.Floor((lon + 180.0) / 360.0 * (1 << z));

        /// <summary>Convert latitude to tile Y at zoom z.</summary>
        private int LatToTileY(double lat, int z)
        {
            double rad = lat * Math.PI / 180.0;
            double n = Math.PI - 2.0 * Math.Log(
                Math.Tan(Math.PI / 4.0 + rad / 2.0));
            return (int)Math.Floor(n / (2.0 * Math.PI) * (1 << z));
        }

        /// <summary>Triggered when the user clicks Download Tiles.</summary>
        private async void DownloadTilesButton_Click(object sender, RoutedEventArgs e)
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

            // Start ETA stopwatch
            _downloadStopwatch = Stopwatch.StartNew();

            // Prepare UI and cancellation
            _downloadCts?.Cancel();
            _downloadCts = new CancellationTokenSource();
            var token = _downloadCts.Token;

            DownloadTilesButton.IsEnabled = false;
            CancelDownloadButton.IsEnabled = true;
            MinZoomSlider.IsEnabled = false;
            MaxZoomSlider.IsEnabled = false;

            string tilesRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tiles");

            // Compute total number of tiles
            int totalTiles = 0;
            for (int z = minZ; z <= maxZ; z++)
            {
                int xMin = LonToTileX(_west, z), xMax = LonToTileX(_east, z);
                int yMin = LatToTileY(_north, z), yMax = LatToTileY(_south, z);
                totalTiles += (xMax - xMin + 1) * (yMax - yMin + 1);
            }

            DownloadProgressBar.Value = 0;
            DownloadProgressBar.Maximum = totalTiles;
            DownloadStatusText.Text = $"0 / {totalTiles} tiles";

            int completed = 0;
            try
            {
                for (int z = minZ; z <= maxZ; z++)
                {
                    token.ThrowIfCancellationRequested();

                    int xMin = LonToTileX(_west, z), xMax = LonToTileX(_east, z);
                    int yMin = LatToTileY(_north, z), yMax = LatToTileY(_south, z);

                    for (int x = xMin; x <= xMax; x++)
                    {
                        token.ThrowIfCancellationRequested();

                        string xDir = Path.Combine(tilesRoot, z.ToString(), x.ToString());
                        Directory.CreateDirectory(xDir);

                        for (int y = yMin; y <= yMax; y++)
                        {
                            token.ThrowIfCancellationRequested();

                            string tilePath = Path.Combine(xDir, $"{y}.png");
                            if (!File.Exists(tilePath))
                            {
                                try
                                {
                                    string url = $"https://a.tile.openstreetmap.org/{z}/{x}/{y}.png";
                                    var response = await _httpClient.GetAsync(url, token);
                                    response.EnsureSuccessStatusCode();
                                    var data = await response.Content.ReadAsByteArrayAsync();
                                    File.WriteAllBytes(tilePath, data);
                                }
                                catch (OperationCanceledException)
                                {
                                    throw;
                                }
                                catch
                                {
                                    // ignore missing tiles or network errors
                                }
                            }

                            // Update progress + ETA
                            completed++;
                            DownloadProgressBar.Value = completed;
                            var elapsed = _downloadStopwatch.Elapsed;
                            var remaining = TimeSpan.Zero;
                            if (completed > 0)
                                remaining = TimeSpan.FromTicks(elapsed.Ticks * (totalTiles - completed) / completed);
                            DownloadStatusText.Text = $"{completed} / {totalTiles} tiles (ETA: {remaining:mm\\:ss})";
                        }
                    }
                }

                MessageBox.Show(
                    $"Download complete!\nTiles saved under:\n{tilesRoot}",
                    "Download Finished",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (OperationCanceledException)
            {
                DownloadStatusText.Text = "Cancelled";
            }
            finally
            {
                DownloadTilesButton.IsEnabled = true;
                CancelDownloadButton.IsEnabled = false;
                MinZoomSlider.IsEnabled = true;
                MaxZoomSlider.IsEnabled = true;
                _downloadStopwatch?.Stop();
            }
        }

        /// <summary>Called when the user clicks Cancel.</summary>
        private void CancelDownloadButton_Click(object sender, RoutedEventArgs e)
        {
            CancelDownloadButton.IsEnabled = false;
            _downloadCts?.Cancel();
        }

        /// <summary>DTO for JSON message from the page</summary>
        private class BoundsMessage
        {
            public double south { get; set; }
            public double west { get; set; }
            public double north { get; set; }
            public double east { get; set; }
        }
    }
}
