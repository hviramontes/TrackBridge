using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using TrackBridge.DIS;

namespace TrackBridge
{
    public partial class MapWindow : Window
    {
        // Fallback zoom level when no local tiles
        private const int DefaultMaxZoom = 18;

        public MapWindow()
        {
            InitializeComponent();
            InitializeAsync();
        }

        private async void InitializeAsync()
        {
            var env = await CoreWebView2Environment.CreateAsync();
            await MapView.EnsureCoreWebView2Async(env);
            var core = MapView.CoreWebView2;

            // Enable DevTools
            core.Settings.AreDevToolsEnabled = true;

            // Map base directory (where map.html lives)
            string assetsFolder = AppDomain.CurrentDomain.BaseDirectory;
            core.SetVirtualHostNameToFolderMapping(
                "appassets",
                assetsFolder,
                CoreWebView2HostResourceAccessKind.Allow);

            // Once initialized, load the map
            LoadMapWithProperZoom();
        }

        private void LoadMapWithProperZoom()
        {
            // Compute localMaxZoom as before
            string tilesRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tiles");
            int localMaxZoom = 0;
            if (Directory.Exists(tilesRoot))
            {
                localMaxZoom = Directory
                    .GetDirectories(tilesRoot)
                    .Select(Path.GetFileName)
                    .Select(n => int.TryParse(n, out var z) ? z : -1)
                    .Where(z => z >= 0)
                    .DefaultIfEmpty(0)
                    .Max();
            }

            // If no local tiles, fall back to DefaultMaxZoom
            int useMaxZoom = localMaxZoom > 0 ? localMaxZoom : DefaultMaxZoom;

            RefreshMap(useMaxZoom);
        }

        /// <summary>
        /// Navigates to map.html with a maxZoom parameter.
        /// </summary>
        public void RefreshMap(int maxZoom)
        {
            string url = $"https://appassets/map.html?localMaxZoom={maxZoom}";
            var core = MapView.CoreWebView2;
            if (core != null)
            {
                core.Navigate(url);
            }
            else
            {
                MapView.CoreWebView2InitializationCompleted += (s, e) =>
                {
                    if (e.IsSuccess)
                        MapView.CoreWebView2.Navigate(url);
                };
            }
        }

        public void FitBounds(double swLat, double swLon, double neLat, double neLon)
        {
            string js = $"map.fitBounds([[{swLat},{swLon}],[{neLat},{neLon}]]);";
            var core = MapView.CoreWebView2;
            if (core != null)
            {
                core.ExecuteScriptAsync(js);
            }
            else
            {
                MapView.CoreWebView2InitializationCompleted += (s, e) =>
                {
                    if (e.IsSuccess)
                        MapView.CoreWebView2.ExecuteScriptAsync(js);
                };
            }
        }

        public async void AddOrUpdateMarker(EntityTrack track)
        {
            var core = MapView.CoreWebView2;
            if (core == null) return;

            // build a lightweight object to send to JS
            var marker = new
            {
                id = track.Id,
                lat = track.Lat,
                lon = track.Lon,
                label = string.IsNullOrEmpty(track.CustomMarking)
                           ? track.Id.ToString()
                           : track.CustomMarking,
                type = track.TrackType,                               // your Ground/Air/Sensor/Civilian
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
            string json = JsonSerializer.Serialize(marker);
            await core.ExecuteScriptAsync($"window.addOrUpdateMarker({json});");
        }

    }
}
