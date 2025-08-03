using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml.Linq;
using TrackBridge.CoT;
using TrackBridge.DIS;


namespace TrackBridge
{
    public partial class MainWindow : Window
    {
        // ─── Fields ─────────────────────────
        private readonly ObservableCollection<EntityTrack> _entityTracks
            = new ObservableCollection<EntityTrack>();

        private readonly DisReceiver _disReceiver;
        private CotUdpSender _cotSender;
        private readonly Timer _heartbeatTimer;
        private CotUdpSender cotSender;
        private readonly DispatcherTimer _statusTimer;
        private FilterSettings _currentFilters;
        private const string FilterFileName = "filters.json";
        private const string ProfilesFile = "filter_profiles.json";
        private const string WindowSettingsFile = "window_settings.json";
        private const string DataGridLayoutFile = "datagrid_layout.json";
        private readonly CotHeartbeatManager heartbeatManager;
        private MapWindow mapWindow;


        public static CotHeartbeatManager CotHeartbeatManager { get; private set; }

        // ─── Static default filter options ─────────────────
        // Since EntityTrack.Domain is an int, list those codes as strings:
        private static readonly string[] DefaultDomains = new[]
        {
            "1",  // e.g. Land
            "2",  // Air
            "3",  // Surface
            "4",  // Subsurface
            "5"   // Space
        };

                private static readonly string[] DefaultKinds = new[]
                {
            "Neutral",
            "Friendly",
            "Hostile",
            "Unknown"
        };
        // timestamp of the last DIS entity received
        private DateTime _lastEntityReceived = DateTime.MinValue;
        // how long without DIS before we consider it “down”
        private static readonly TimeSpan DisTimeout = TimeSpan.FromSeconds(5);

        private Dictionary<string, FilterSettings> _profiles;
        private MapWindow _mapWindow;

        // ─── Replay Fields ───────────────────
        private List<(DateTime time, string xml)> _replayEvents;
        private int _replayIndex;
        private DispatcherTimer _replayTimer;
        private DateTime _replayStartWallClock;
        private DateTime _replayStartLogTime;
        private bool _isReplayPaused;
        private TimeSpan _pausedOffset;

        public MainWindow()
        {
            InitializeComponent();

            ReplaySpeedComboBox.SelectionChanged += (s, e) =>
            {
                var speedText = ReplaySpeedComboBox.Text;
                ReplaySpeedText.Text = $"Speed: {speedText}";
            };


            cotSender = new CotUdpSender(NetworkConfig.CotIp, NetworkConfig.CotPort);

            CotHeartbeatManager = new CotHeartbeatManager(_cotSender);

            EntityGrid.ItemsSource = _entityTracks;

            // Persist window
            Loaded += Window_Loaded;
            Closing += Window_Closing;

            // Init UI
           
            DisPortTextBox.Text = NetworkConfig.DisPort.ToString();
            CotIpTextBox.Text = NetworkConfig.CotIp;
            CotPortTextBox.Text = NetworkConfig.CotPort.ToString();
            TakIndicator.Fill = Brushes.Gray;

            // Core services
            _disReceiver = new DisReceiver();
            _cotSender = new CotUdpSender(NetworkConfig.CotIp, NetworkConfig.CotPort);
            heartbeatManager = new CotHeartbeatManager(cotSender);
            _heartbeatTimer = new Timer(5000) { AutoReset = true };
            // status-bar update every second
            _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _statusTimer.Tick += OnStatusTimerTick;
            _statusTimer.Start();


            // Wire events
            _disReceiver.EntityReceived += OnEntityReceived;
            _heartbeatTimer.Elapsed += OnHeartbeatTimerElapsed;
            _statusTimer.Tick += OnStatusTimerTick;

            // Start
            _disReceiver.StartListening(NetworkConfig.DisIp, NetworkConfig.DisPort);
            _heartbeatTimer.Start();
            _statusTimer.Start();

            // Load filters & profiles
            _currentFilters = File.Exists(FilterFileName)
                ? JsonSerializer.Deserialize<FilterSettings>(File.ReadAllText(FilterFileName)) ?? new FilterSettings()
                : new FilterSettings();
            _profiles = File.Exists(ProfilesFile)
                ? JsonSerializer.Deserialize<Dictionary<string, FilterSettings>>(File.ReadAllText(ProfilesFile))
                  ?? new Dictionary<string, FilterSettings>()
                : new Dictionary<string, FilterSettings>();

            // Replay
            _replayTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _replayTimer.Tick += ReplayTimer_Tick;

            ApplyFilters();
            LoadDataGridLayout();

            // Populate DIS‐IP combo box with local IPv4 addresses
            DisIpComboBox.Items.Clear();
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces()
                       .Where(n => n.OperationalStatus == OperationalStatus.Up))
            {
                foreach (var addr in nic.GetIPProperties()
                                       .UnicastAddresses
                                       .Where(u => u.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork))
                {
                    var item = new ComboBoxItem
                    {
                        Content = $"{nic.Name} ({addr.Address})",
                        Tag = addr.Address.ToString()
                    };
                    DisIpComboBox.Items.Add(item);
                }
            }
            // Select saved IP or default to first
            var saved = NetworkConfig.DisIp;
            DisIpComboBox.SelectedItem = DisIpComboBox.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(i => (string)i.Tag == saved)
              ?? DisIpComboBox.Items.OfType<ComboBoxItem>().FirstOrDefault();


            // Whenever user reorders or resizes a column, persist layout immediately
            EntityGrid.ColumnReordered += (s, e) => SaveDataGridLayout();

            EntityGrid.MouseDoubleClick += EntityGrid_MouseDoubleClick;

        }
        private void ApplySettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // DIS IP
                if (DisIpComboBox.SelectedItem is ComboBoxItem selectedItem &&
                    selectedItem.Tag is string disIp)
                {
                    NetworkConfig.DisIp = disIp;
                }

                // DIS Port
                if (int.TryParse(DisPortTextBox.Text, out var disPort))
                {
                    NetworkConfig.DisPort = disPort;
                }

                // Apply DIS changes
                _disReceiver.StartListening(NetworkConfig.DisIp, NetworkConfig.DisPort);
                Log($"[INFO] DIS set to {NetworkConfig.DisIp}:{NetworkConfig.DisPort}");

                // CoT IP
                NetworkConfig.CotIp = CotIpTextBox.Text;

                // CoT Port
                if (int.TryParse(CotPortTextBox.Text, out var cotPort))
                {
                    NetworkConfig.CotPort = cotPort;
                }

                // Apply CoT changes
                _cotSender = new CotUdpSender(NetworkConfig.CotIp, NetworkConfig.CotPort);
                Log($"[INFO] CoT set to {NetworkConfig.CotIp}:{NetworkConfig.CotPort}");

                ErrorText.Text = ""; // clear any previous error
            }
            catch (Exception ex)
            {
                ErrorText.Text = "Apply failed: " + ex.Message;
                Log($"[ERROR] Apply failed: {ex.Message}");
            }
        }

        private void ApplyCotSettings_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(CotPortTextBox.Text, out int cotPort))
            {
                NetworkConfig.CotPort = cotPort;
                _cotSender = new CotUdpSender(NetworkConfig.CotIp, NetworkConfig.CotPort);

                CotHeartbeatManager.Stop();
                CotHeartbeatManager = new CotHeartbeatManager(_cotSender);
                CotHeartbeatManager.Start();

                MessageBox.Show("CoT settings applied and heartbeat restarted.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show("Invalid CoT port number.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ApplyHeartbeat_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(HeartbeatIntervalTextBox.Text, out int intervalSeconds))
            {
                heartbeatManager.IntervalSeconds = intervalSeconds;
                LogOutput.AppendText($"[INFO] Updated heartbeat interval to {intervalSeconds} seconds.\n");
            }
            else
            {
                LogOutput.AppendText("[ERROR] Invalid heartbeat interval. Please enter a valid number.\n");
            }
        }



        // ─── Window Persistence ─────────────────
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                if (File.Exists(WindowSettingsFile))
                {
                    var ws = JsonSerializer.Deserialize<WindowSettings>(File.ReadAllText(WindowSettingsFile));
                    if (ws != null && ws.Width > 0 && ws.Height > 0)
                    {
                        Left = ws.Left;
                        Top = ws.Top;
                        Width = ws.Width;
                        Height = ws.Height;
                        WindowState = ws.State;
                    }
                }
            }
            catch { }

            HeartbeatIntervalTextBox.Text = CotHeartbeatManager.IntervalSeconds.ToString();  // 👈 Add this line

        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            SaveDataGridLayout();
            try
            {
                var ws = new WindowSettings
                {
                    State = WindowState,
                    Left = (WindowState == WindowState.Normal) ? Left : RestoreBounds.Left,
                    Top = (WindowState == WindowState.Normal) ? Top : RestoreBounds.Top,
                    Width = (WindowState == WindowState.Normal) ? Width : RestoreBounds.Width,
                    Height = (WindowState == WindowState.Normal) ? Height : RestoreBounds.Height
                };
                File.WriteAllText(WindowSettingsFile,
                    JsonSerializer.Serialize(ws, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }

        // ─── LostFocus Handlers ─────────────────
      
    

        // ─── Heartbeat & Entity ─────────────────
        private void OnHeartbeatTimerElapsed(object s, ElapsedEventArgs e)
        {
            var xml = CotBuilder.BuildPingCot();

            try
            {
                _cotSender.Send(xml);
                Dispatcher.Invoke(() =>
                {
                    ErrorText.Text = "";  // clear any prior error
                    LastPingText.Text = "Last Ping: " + DateTime.Now.ToString("HH:mm:ss");
                    Log($"[HEARTBEAT] Ping at {DateTime.Now:HH:mm:ss}");
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    ErrorText.Text = "Send error: " + ex.Message;
                    Log($"[ERROR] Ping failed: {ex.Message}");
                });
            }

            SaveCotToDailyLog(xml);
        }


        private void OnEntityReceived(EntityTrack track)
        {
            _lastEntityReceived = DateTime.Now;
            var xml = CotBuilder.BuildCotXml(track);
            if (!track.Publish)
                return; // skip sending CoT for disabled tracks

            SaveCotToDailyLog(xml);

            // 3) Immediately add to grid *and* update the map
            Dispatcher.Invoke(() =>
            {
                // Add to the DataGrid
                _entityTracks.Add(track);
                Log($"[DEBUG] Collection count after Add: {_entityTracks.Count}");

                // Refresh filters (so your grid stays consistent)
                ApplyFilters();

                // If the map window is open, push this marker
                if (mapWindow != null && mapWindow.IsVisible)
                    mapWindow.AddOrUpdateMarker(track);
            });

            // 4) Ping & send in background so UI stays responsive
            Task.Run(() => {
                try
                {
                    var ping = new Ping();
                    var reply = ping.Send(NetworkConfig.CotIp, 1000);
                    if (reply.Status != IPStatus.Success)
                    {
                        Dispatcher.Invoke(() => {
                            ErrorText.Text = $"Ping failed: {reply.Status}";
                            UpdateTakIndicator(false);
                        });
                    }
                    else
                    {
                        _cotSender.Send(xml);
                        Dispatcher.Invoke(() => {
                            ErrorText.Text = "";
                            LastPingText.Text = "Last Ping: " + DateTime.Now.ToString("HH:mm:ss");
                            UpdateTakIndicator(true);
                        });
                    }
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() => {
                        ErrorText.Text = "Send error: " + ex.Message;
                        UpdateTakIndicator(false);
                    });
                }
            });
        }




        private void OnStatusTimerTick(object s, EventArgs e)
        {
            // 1) Gather all tracks currently shown in the grid
            var displayed = EntityGrid.Items
                              .OfType<EntityTrack>()
                              .ToList();

            // 2) Compute total count
            int total = displayed.Count;

            // 3) Compute breakdown by TrackType
            var groups = displayed
                .GroupBy(t => t.TrackType ?? "Unknown")
                .Select(g => $"{g.Key}:{g.Count()}")
                .ToArray();

            // 4) Build the display string
            string text = $"Total: {total}";
            if (groups.Length > 0)
                text += " | " + string.Join(", ", groups);

            // 5) Update the status bar TextBlock
            TrackCountText.Text = text;

            // flip DIS indicator red if no entity in the last DisTimeout
            if (DateTime.Now - _lastEntityReceived > DisTimeout)
                DisIndicator.Fill = Brushes.Red;
            else
                DisIndicator.Fill = Brushes.Green;

            // ─── Prune any tracks older than 30 s ─────────────────
            var staleThreshold = TimeSpan.FromSeconds(30);
            var toRemove = _entityTracks
                .Where(t => DateTime.Now - t.LastUpdate > staleThreshold)
                .ToList();
            foreach (var old in toRemove)
                _entityTracks.Remove(old);

        }


        // ─── Logging Helpers ───────────────────
        private void Log(string msg)
        {
            LogOutput.AppendText(msg + "\n");
            LogOutput.ScrollToEnd();
        }

        private void SaveCotToDailyLog(string xml)
        {
            try
            {
                var folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
                Directory.CreateDirectory(folder);
                File.AppendAllText(Path.Combine(folder, $"CoT_{DateTime.Now:yyyy-MM-dd}.log"), xml + "\n");
            }
            catch (Exception ex) { Log($"[ERROR] Log write failed: {ex.Message}"); }
        }

        // ─── CSV & Preview ─────────────────────
        private void SaveToCsv_Click(object s, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog { Title = "Save Entities as CSV", Filter = "CSV|*.csv", FileName = "entities.csv" };
            if (dlg.ShowDialog() != true) return;
            try
            {
                using (var sw = new StreamWriter(dlg.FileName, false, System.Text.Encoding.UTF8))
                {
                    sw.WriteLine("Id,CustomMarking,Lat,Lon,Altitude,LastUpdate,EntityKind,Domain,TrackType,CountryCode,IconType,Mgrs,Publish");

                    foreach (var t in _entityTracks)
                    {
                        // escape any commas in markings
                        var mark = t.CustomMarking?.Replace(",", "_") ?? "";
                        sw.WriteLine($"{t.Id}," +
                                     $"{mark}," +
                                     $"{t.Lat}," +
                                     $"{t.Lon}," +
                                     $"{t.Altitude}," +
                                     $"{t.LastUpdate:O}," +
                                     $"{t.EntityKind}," +
                                     $"{t.Domain}," +
                                     $"{t.TrackType}," +
                                     $"{t.CountryCode}," +
                                     $"{t.IconType}," +
                                     $"{t.Mgrs}," +
                                     $"{t.Publish}");
                    }

                }
                Log($"[INFO] CSV saved to {dlg.FileName}");
            }
            catch (Exception ex) { Log($"[ERROR] CSV save failed: {ex.Message}"); }
        }

        private void PreviewCot_Click(object s, RoutedEventArgs e)
        {
            var track = EntityGrid.SelectedItem as EntityTrack;
            var xml = track != null
                ? CotBuilder.BuildCotXml(track)
                : CotBuilder.BuildPingCot();
            new CotPreviewWindow(xml) { Owner = this }.ShowDialog();
        }

        // ─── Filters & Profiles ─────────────────
        private void LoadFilters_Click(object s, RoutedEventArgs e)
        {
            // 1) Dynamic values seen so far
            var seenDomains = _entityTracks.Select(t => t.Domain.ToString());
            var seenKinds = _entityTracks.Select(t => t.TrackType);

            // 2) Merge static defaults + dynamic, de-dup, sort
            var allDomains = DefaultDomains
                .Union(seenDomains)
                .Distinct()
                .OrderBy(x => x);
            var allKinds = DefaultKinds
                .Union(seenKinds)
                .Distinct()
                .OrderBy(x => x);

            // 3) Load previously saved ad-hoc filters
            var loaded = File.Exists(FilterFileName)
                ? JsonSerializer.Deserialize<FilterSettings>(File.ReadAllText(FilterFileName))
                  ?? new FilterSettings()
                : new FilterSettings();

            // 4) Show filter dialog seeded with combined lists
            var dlg = new FilterSettingsWindow(
                loaded,
                allDomains,
                allKinds)
            {
                Owner = this
            };
            if (dlg.ShowDialog() != true)
                return;

            // 5) Apply & save new settings
            _currentFilters = dlg.Settings;
            File.WriteAllText(
                FilterFileName,
                JsonSerializer.Serialize(_currentFilters, new JsonSerializerOptions { WriteIndented = true }));
            ApplyFilters();

            MessageBox.Show(
                "Filters loaded.",
                "Load Filters",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void SaveFilters_Click(object s, RoutedEventArgs e)
        {
            var seenDomains = _entityTracks.Select(t => t.Domain.ToString());
            var seenKinds = _entityTracks.Select(t => t.TrackType);

            var allDomains = DefaultDomains
                .Union(seenDomains)
                .Distinct()
                .OrderBy(x => x);
            var allKinds = DefaultKinds
                .Union(seenKinds)
                .Distinct()
                .OrderBy(x => x);

            var dlg = new FilterSettingsWindow(
                _currentFilters,
                allDomains,
                allKinds)
            {
                Owner = this
            };
            if (dlg.ShowDialog() != true)
                return;

            _currentFilters = dlg.Settings;
            File.WriteAllText(
                FilterFileName,
                JsonSerializer.Serialize(_currentFilters, new JsonSerializerOptions { WriteIndented = true }));
            ApplyFilters();

            MessageBox.Show(
                "Filters saved.",
                "Save Filters",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }


        // ─── Filter → Save Profile ─────────────────
        private void SaveProfile_Click(object sender, RoutedEventArgs e)
        {
            // Ask the user for a profile name
            var dlg = new InputDialog("Enter a name for this filter profile:")
            {
                Owner = this
            };
            if (dlg.ShowDialog() != true) return;

            var name = dlg.ResponseText.Trim();
            if (string.IsNullOrEmpty(name)) return;

            // Confirm overwrite if it exists
            if (_profiles.ContainsKey(name) &&
                MessageBox.Show($"Profile '{name}' already exists. Overwrite?",
                                "Confirm Overwrite",
                                MessageBoxButton.YesNo,
                                MessageBoxImage.Question)
                != MessageBoxResult.Yes)
            {
                return;
            }

            // Deep copy current filters and save
            var copy = JsonSerializer.Deserialize<FilterSettings>(
                JsonSerializer.Serialize(_currentFilters));
            _profiles[name] = copy;

            File.WriteAllText(
                ProfilesFile,
                JsonSerializer.Serialize(_profiles, new JsonSerializerOptions { WriteIndented = true }));

            MessageBox.Show(
                $"Profile '{name}' saved.",
                "Save Profile",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        // ─── Filter → Load Profile ─────────────────
        private void LoadProfile_Click(object sender, RoutedEventArgs e)
        {
            if (_profiles.Count == 0)
            {
                MessageBox.Show("No profiles to load.",
                                "Load Profile",
                                MessageBoxButton.OK,
                                MessageBoxImage.Information);
                return;
            }

            // Let the user pick one by name
            string list = string.Join(Environment.NewLine, _profiles.Keys);
            var dlg = new InputDialog("Available profiles:\n" + list + "\n\nName to load:")
            {
                Owner = this
            };
            if (dlg.ShowDialog() != true) return;

            var name = dlg.ResponseText.Trim();
            if (!_profiles.ContainsKey(name)) return;

            // Restore the selected profile
            _currentFilters = JsonSerializer.Deserialize<FilterSettings>(
                JsonSerializer.Serialize(_profiles[name]));

            // Also persist it as the “last used” ad-hoc filter
            File.WriteAllText(
                FilterFileName,
                JsonSerializer.Serialize(_currentFilters, new JsonSerializerOptions { WriteIndented = true }));

            ApplyFilters();

            MessageBox.Show(
                $"Profile '{name}' loaded.",
                "Load Profile",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        // ─── Filter → Delete Profile ─────────────────
        private void DeleteProfile_Click(object sender, RoutedEventArgs e)
        {
            if (_profiles.Count == 0)
            {
                MessageBox.Show("No profiles to delete.",
                                "Delete Profile",
                                MessageBoxButton.OK,
                                MessageBoxImage.Information);
                return;
            }

            // Let the user choose which profile to remove
            string list = string.Join(Environment.NewLine, _profiles.Keys);
            var dlg = new InputDialog("Available profiles:\n" + list + "\n\nName to delete:")
            {
                Owner = this
            };
            if (dlg.ShowDialog() != true) return;

            var name = dlg.ResponseText.Trim();
            if (!_profiles.ContainsKey(name)) return;

            if (MessageBox.Show($"Delete profile '{name}' permanently?",
                                "Confirm Delete",
                                MessageBoxButton.YesNo,
                                MessageBoxImage.Warning)
                != MessageBoxResult.Yes)
            {
                return;
            }

            _profiles.Remove(name);

            File.WriteAllText(
                ProfilesFile,
                JsonSerializer.Serialize(_profiles, new JsonSerializerOptions { WriteIndented = true }));

            MessageBox.Show(
                $"Profile '{name}' deleted.",
                "Delete Profile",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }


        // ─── Map, Settings, Logs, Replay, TestTrack ─────────────────
        private void OpenMapPreview_Click(object s, RoutedEventArgs e)
        {
            if (mapWindow?.IsVisible == true)
            {
                mapWindow.Activate();
            }
            else
            {
                mapWindow = new MapWindow();
                mapWindow.Owner = this;

                // ▶️ When the map is actually ready, drop in your test marker
                mapWindow.MapReady += () =>
                {
                    var testTrack = new EntityTrack
                    {
                        Id = 999,
                        Lat = 32.8364,      // San Clemente Island
                        Lon = -118.5208,
                        CustomMarking = "TEST",
                        TrackType = "Ground",
                        LastUpdate = DateTime.Now
                    };
                    mapWindow.AddOrUpdateMarker(testTrack);
                };

                mapWindow.Show();


                EntityGrid.ItemsSource = _entityTracks;
                // … the rest of your constructor …

            }
        }

        private void OpenDownloadArea_Click(object s, RoutedEventArgs e)
            => new DownloadAreaWindow { Owner = this }.Show();
        private void ImportMbtiles_Click(object s, RoutedEventArgs e)
            => MessageBox.Show("Not implemented", "MBTiles", MessageBoxButton.OK, MessageBoxImage.Information);
        private void OpenNetworkSettings_Click(object s, RoutedEventArgs e)
            => new NetworkSettingsWindow { Owner = this }.ShowDialog();
        private void OpenLogsFolder_Click(object s, RoutedEventArgs e)
        {
            var f = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
            Directory.CreateDirectory(f);
            System.Diagnostics.Process.Start("explorer.exe", f);
        }
        // ─── Logs Menu → Load Log for Replay ─────────────────
        private void LoadLogForReplay_Click(object sender, RoutedEventArgs e)
        {
            // 1) Ask the user to pick a CoT log file
            var dlg = new OpenFileDialog
            {
                Title = "Select CoT Log for Replay",
                InitialDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs"),
                Filter = "CoT Log (*.log)|*.log|All Files (*.*)|*.*"
            };
            if (dlg.ShowDialog() != true)
                return;

            try
            {
                // 2) Read all lines and parse out timestamp + XML
                var entries = File.ReadAllLines(dlg.FileName)
                                  .Select(ParseLogLine)
                                  .Where(x => x.time != DateTime.MinValue)
                                  .OrderBy(x => x.time)
                                  .ToList();

                // 3) Store in your replay buffer
                _replayEvents = entries;

                MessageBox.Show(
                    $"Loaded {_replayEvents.Count} events for replay.",
                    "Load Log",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to load log:\n{ex.Message}",
                    "Load Log Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        // ─── Helper: parse a CoT log line into (timestamp, xml) ─────────────────
        private (DateTime time, string xml) ParseLogLine(string line)
        {
            // Looks for time="2025-07-01T12:34:56.789Z" inside the XML
            var m = Regex.Match(line, @"time=""(?<t>[^""]+)""");
            if (!m.Success || !DateTime.TryParse(m.Groups["t"].Value, out DateTime dt))
                return (DateTime.MinValue, null);

            // Return UTC timestamp + the raw XML line
            return (dt.ToUniversalTime(), line);
        }



        /// <summary>Start or resume replaying loaded events.</summary>
        private void PlayReplay_Click(object sender, RoutedEventArgs e)
        {
            if (_replayEvents == null || !_replayEvents.Any())
            {
                MessageBox.Show("No log loaded for replay.",
                                "Replay",
                                MessageBoxButton.OK,
                                MessageBoxImage.Information);
                return;
            }

            if (_isReplayPaused)
            {
                _isReplayPaused = false;
                _replayStartWallClock = DateTime.Now - _pausedOffset;
            }
            else
            {
                _replayIndex = 0;
                _replayStartWallClock = DateTime.Now;
                _replayStartLogTime = _replayEvents[0].time;
            }
            double speed = GetReplaySpeedMultiplier();
            Log($"[REPLAY] Started at speed {speed:0.0}x");

            _replayTimer.Start();

            ReplayStatusText.Text = "Replay: ▶️ Playing";

        }

        /// <summary>Pause the ongoing replay.</summary>
        private void PauseReplay_Click(object sender, RoutedEventArgs e)
        {
            if (_replayTimer.IsEnabled)
            {
                _replayTimer.Stop();
                _isReplayPaused = true;
                _pausedOffset = DateTime.Now - _replayStartWallClock;
            }
            ReplayStatusText.Text = "Replay: ⏸️ Paused";

        }

        /// <summary>Stop and reset the replay.</summary>
        private void StopReplay_Click(object sender, RoutedEventArgs e)
        {
            _replayTimer.Stop();
            _replayIndex = 0;
            _isReplayPaused = false;
            _pausedOffset = TimeSpan.Zero;
            ReplayStatusText.Text = "Replay: ⏹️ Stopped";

        }

        private double GetReplaySpeedMultiplier()
        {
            if (ReplaySpeedComboBox?.SelectedItem is ComboBoxItem selectedItem)
            {
                string content = selectedItem.Content.ToString();
                if (content.EndsWith("x") && double.TryParse(content.TrimEnd('x'), out double multiplier))
                {
                    return multiplier;
                }
            }
            return 1.0; // fallback to normal speed
        }

        /// <summary>Fires on each tick to send due events.</summary>
        /// <summary>Fires on each tick to send due events.</summary>
        private void ReplayTimer_Tick(object sender, EventArgs e)
        {
            if (_replayIndex >= _replayEvents.Count)
            {
                StopReplay_Click(null, null);
                return;
            }

            double speed = GetReplaySpeedMultiplier();
            var elapsed = (DateTime.Now - _replayStartWallClock).TotalSeconds * speed;
            var targetTime = _replayStartLogTime.AddSeconds(elapsed);

            // Update the replay speed visual indicator (e.g., status bar or label)
            ReplaySpeedText.Text = $"Speed: {speed:0.0}x";

            while (_replayIndex < _replayEvents.Count &&
                   _replayEvents[_replayIndex].time <= targetTime)
            {
                // 1) Resend the XML over UDP
                string xml = _replayEvents[_replayIndex].xml;
                bool sendOk = false;
                try
                {
                    _cotSender.Send(xml);
                    sendOk = true;
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() => ErrorText.Text = "Replay send error: " + ex.Message);
                }

                // 2) Parse the XML into an EntityTrack
                var el = XElement.Parse(xml);
                var uidValue = el.Attribute("uid")?.Value;
                int id = int.TryParse(uidValue?.Split('-').Last(), out var tmp) ? tmp : 0;

                var pt = el.Element("point");
                double lat = double.Parse(pt.Attribute("lat").Value, CultureInfo.InvariantCulture);
                double lon = double.Parse(pt.Attribute("lon").Value, CultureInfo.InvariantCulture);
                double alt = double.Parse(pt.Attribute("hae").Value, CultureInfo.InvariantCulture);

                var contact = el.Element("detail")?.Element("contact");
                string callsign = contact?.Attribute("callsign")?.Value ?? id.ToString();

                var group = el.Element("detail")?.Element("group");
                string country = group?.Attribute("country")?.Value ?? string.Empty;
                string icon = group?.Attribute("iconType")?.Value ?? string.Empty;

                // ← Here’s the only change: add Mgrs
                var track = new EntityTrack
                {
                    Id = id,
                    Lat = lat,
                    Lon = lon,
                    Altitude = alt,
                    LastUpdate = DateTime.Now,
                    CustomMarking = callsign,
                    CountryCode = country,
                    IconType = icon,
                    Mgrs = MgrsConverter.LatLonToMgrs(lat, lon)
                };

                if (!track.Publish)
                    return; // skip sending for non-published tracks


                // 3) Update UI and map on the Dispatcher thread
                Dispatcher.Invoke(() =>
                {
                    _entityTracks.Add(track);
                    ApplyFilters();
                    if (_mapWindow?.IsVisible == true)
                        _mapWindow.AddOrUpdateMarker(track);

                    if (sendOk)
                    {
                        ErrorText.Text = "";
                        LastPingText.Text = "Last Ping: " + DateTime.Now.ToString("HH:mm:ss");
                        UpdateTakIndicator(true);
                    }
                });

                // 4) Log and advance
                SaveCotToDailyLog(xml);
                _replayIndex++;
            }
        }


        /// <summary>Send a dummy “TEST” entity to verify TAK connectivity.</summary>
        private void TestTrack_Click(object sender, RoutedEventArgs e)
        {
            bool success = false;
            try
            {
                // First, verify the CoT host is reachable
                var ping = new Ping();
                var reply = ping.Send(NetworkConfig.CotIp, 1000);
                if (reply.Status != IPStatus.Success)
                {
                    // Ping failed—show an error and bail out
                    ErrorText.Text = $"Ping failed: {reply.Status}";
                    UpdateTakIndicator(false);
                    return;
                }

                // If we get here, the host answered—build & send the test track
                var testEntity = new EntityTrack { /* … */ };

                string xml = CotBuilder.BuildCotXml(testEntity);
                _cotSender.Send(xml);
                success = true;

                Dispatcher.Invoke(() =>
                {
                    ErrorText.Text = "";
                    LastPingText.Text = "Last Ping: " + DateTime.Now.ToString("HH:mm:ss");
                });

                Log($"[TEST] Entity track sent: ID={testEntity.Id}");
                MessageBox.Show($"Test track sent to {NetworkConfig.CotIp}:{NetworkConfig.CotPort}",
                                "Test Track", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => ErrorText.Text = "Test send error: " + ex.Message);
                MessageBox.Show($"Failed to send test track:\n{ex.Message}",
                                "Test Track Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            UpdateTakIndicator(success);
        }


        /// <summary>Perform a simple ID/marking search.</summary>
        private void Search_Click(object sender, RoutedEventArgs e)
        {
            string term = SearchTextBox.Text.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(term))
            {
                EntityGrid.ItemsSource = _entityTracks;
                return;
            }

            // Apply view filtering instead of swapping the source
            var view = CollectionViewSource.GetDefaultView(_entityTracks);
            view.Filter = obj =>
            {
                var t = obj as EntityTrack;
                if (t == null) return false;
                return t.Id.ToString().Contains(term) ||
                       (t.CustomMarking?.ToLowerInvariant().Contains(term) ?? false);
            };
            view.Refresh();

        }


        /// <summary>Clear search box and show all tracks.</summary>
        private void ClearSearch_Click(object sender, RoutedEventArgs e)
        {
            SearchTextBox.Clear();
            var view = CollectionViewSource.GetDefaultView(_entityTracks);
            view.Filter = null;
            view.Refresh();
        }



        // ─── Helpers: DataGrid Layout ─────────────────
        private void LoadDataGridLayout()
        {
            try
            {
                if (!File.Exists(DataGridLayoutFile))
                    return;

                var json = File.ReadAllText(DataGridLayoutFile);
                var layouts = JsonSerializer.Deserialize<List<ColumnLayout>>(json)
                              ?? new List<ColumnLayout>();

                foreach (var col in EntityGrid.Columns)
                {
                    var layout = layouts.FirstOrDefault(l => l.Header == col.Header.ToString());
                    if (layout != null)
                    {
                        col.DisplayIndex = layout.DisplayIndex;
                        col.Width = layout.Width;
                        col.Visibility = layout.IsVisible
                                           ? Visibility.Visible
                                           : Visibility.Collapsed;
                    }
                }
            }
            catch
            {
                // ignore any errors
            }
        }

        private void SaveDataGridLayout()
        {
            try
            {
                var layouts = EntityGrid.Columns
                    .Select(col => new ColumnLayout
                    {
                        Header = col.Header.ToString(),
                        DisplayIndex = col.DisplayIndex,
                        Width = col.ActualWidth,
                        IsVisible = (col.Visibility == Visibility.Visible)
                    })
                    .ToList();

                var json = JsonSerializer.Serialize(
                    layouts,
                    new JsonSerializerOptions { WriteIndented = true });

                File.WriteAllText(DataGridLayoutFile, json);
            }
            catch
            {
                // ignore any errors
            }
        }

        // ─── Helper: DataGrid column layout for saving/restoring ───────────────────
        private class ColumnLayout
        {
            public string Header { get; set; }
            public int DisplayIndex { get; set; }
            public double Width { get; set; }
            public bool IsVisible { get; set; }
        }

        private void ApplyFilters()
            => EntityGrid.ItemsSource = _entityTracks;

        // ─── TAK Connectivity Indicator Helper ─────────────────
        private void UpdateTakIndicator(bool success)
        {
            // Run on UI thread
            Dispatcher.Invoke(() =>
            {
                // Green on success, red on failure
                TakIndicator.Fill = success ? Brushes.Green : Brushes.Red;
            });
        }


        private void Close_Click(object s, RoutedEventArgs e)
            => Close();

        private void ImportStubs_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Import Track Stubs",
                Filter = "JSON files (*.json)|*.json|CSV files (*.csv)|*.csv"
            };
            if (dlg.ShowDialog() != true)
                return;

            try
            {
                List<EntityTrack> tracks;
                var ext = Path.GetExtension(dlg.FileName).ToLowerInvariant();

                if (ext == ".json")
                {
                    // JSON array of EntityTrack
                    var text = File.ReadAllText(dlg.FileName);
                    tracks = JsonSerializer.Deserialize<List<EntityTrack>>(text)
                             ?? new List<EntityTrack>();
                }
                else // assume CSV
                {
                    var lines = File.ReadAllLines(dlg.FileName);
                    var header = lines[0].Split(',');

                    // Helper to find column index
                    int idx(string name) => Array.IndexOf(header, name);

                    tracks = new List<EntityTrack>();
                    for (int i = 1; i < lines.Length; i++)
                    {
                        var cols = lines[i].Split(',');
                        var t = new EntityTrack
                        {
                            Id = int.Parse(cols[idx("Id")]),
                            CustomMarking = cols[idx("CustomMarking")],
                            Lat = double.Parse(cols[idx("Lat")]),
                            Lon = double.Parse(cols[idx("Lon")]),
                            Altitude = double.Parse(cols[idx("Altitude")]),
                            LastUpdate = DateTime.Parse(cols[idx("LastUpdate")]),
                            EntityKind = int.Parse(cols[idx("EntityKind")]),
                            Domain = int.Parse(cols[idx("Domain")]),
                            TrackType = cols[idx("TrackType")],
                            CountryCode = cols[idx("CountryCode")],
                            IconType = cols[idx("IconType")],
                            Mgrs = cols[idx("Mgrs")],
                            Publish = bool.Parse(cols[idx("Publish")])
                        };
                        tracks.Add(t);
                    }
                }

                // Inject each stub as if received live
                foreach (var track in tracks)
                    OnEntityReceived(track);

                MessageBox.Show($"Imported {tracks.Count} stubs.",
                                "Import Complete",
                                MessageBoxButton.OK,
                                MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Import failed: {ex.Message}",
                                "Error",
                                MessageBoxButton.OK,
                                MessageBoxImage.Error);
            }
        }

      
        private void EntityGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (EntityGrid.SelectedItem is EntityTrack selectedTrack)
            {
                var detailWindow = new TrackDetailWindow(selectedTrack)
                {
                    Owner = this
                };

                if (detailWindow.ShowDialog() == true)
                {
                    // Update the track with user edits
                    selectedTrack.CustomMarking = detailWindow.CustomMarking;
                    selectedTrack.IsCustomMarkingLocked = detailWindow.IsLocked;

                    // Refresh the DataGrid
                    EntityGrid.Items.Refresh();
                }
            }
        }

        private void CotPortTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(CotPortTextBox.Text, out int newPort))
            {
                cotSender?.SetTarget(CotIpTextBox.Text, newPort);
                Log("Updated CoT port via LostFocus.");
            }
        }


    }

}
