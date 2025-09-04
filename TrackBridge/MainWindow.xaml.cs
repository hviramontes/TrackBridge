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
        private readonly ObservableCollection<EntityTrack> _entityTracks = new ObservableCollection<EntityTrack>();
        private readonly DisReceiver _disReceiver;
        private CotUdpSender _cotSender;
        private readonly Timer _heartbeatTimer;
        private readonly DispatcherTimer _statusTimer;
        private System.Windows.Threading.DispatcherTimer _refreshTimer;
        private readonly Queue<string> _logBuffer = new Queue<string>();
        private System.Windows.Threading.DispatcherTimer _logTimer;
        private bool _pendingViewRefresh = false;
        private FilterSettings _currentFilters;
        private bool _isReceiverRunning = false;
        private const string FilterFileName = "filters.json";
        private const string ProfilesFile = "filter_profiles.json";
        private const string WindowSettingsFile = "window_settings.json";
        private const string DataGridLayoutFile = "datagrid_layout.json";
        private readonly CotHeartbeatManager _heartbeatManager;
        private MapWindow _mapWindow;
        private const bool MAP_DISABLED_FOR_TEST = true;
        public static CotHeartbeatManager CotHeartbeatManager { get; private set; }
        private static readonly string[] DefaultDomains = new[]
        {
            "1", // e.g. Land
            "2", // Air
            "3", // Surface
            "4", // Subsurface
            "5" // Space
        };
        private static readonly string[] DefaultKinds = new[]
        {
            "Neutral",
            "Friendly",
            "Hostile",
            "Unknown"
        };
        private DateTime _lastEntityReceived = DateTime.MinValue;
        private static readonly TimeSpan DisTimeout = TimeSpan.FromSeconds(5);
        private Dictionary<string, FilterSettings> _profiles;
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
            _cotSender = new CotUdpSender(NetworkConfig.CotIp, NetworkConfig.CotPort);
            _heartbeatManager = new CotHeartbeatManager(_cotSender);
            CotHeartbeatManager = _heartbeatManager;
            EntityGrid.ItemsSource = _entityTracks;
            Loaded += Window_Loaded;
            Closing += Window_Closing;
            DisPortTextBox.Text = NetworkConfig.DisPort.ToString();
            CotIpTextBox.Text = NetworkConfig.CotIp;
            CotPortTextBox.Text = NetworkConfig.CotPort.ToString();
            HeartbeatIntervalTextBox.Text = _heartbeatManager.IntervalSeconds.ToString();
            TakIndicator.Fill = Brushes.Gray;
            _disReceiver = new DisReceiver();
            _heartbeatTimer = new Timer(5000) { AutoReset = true };
            _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _statusTimer.Tick += OnStatusTimerTick;
            _statusTimer.Start();
            // Throttle grid refreshes so high-rate DIS updates don't freeze the UI
            _refreshTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(150)
            };
            _refreshTimer.Tick += (s, e) =>
            {
                if (!_pendingViewRefresh) return;
                _pendingViewRefresh = false;

                // Refresh whatever view the grid is currently using (search filter or default collection)
                var view = CollectionViewSource.GetDefaultView(EntityGrid.ItemsSource ?? _entityTracks);
                view?.Refresh();
            };
            _refreshTimer.Start();
            // Buffer log writes and flush ~5 times/sec to avoid UI stalls
            _logTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(200)
            };
            _logTimer.Tick += (s, e) =>
            {
                if (_logBuffer.Count == 0) return;

                // Drain current buffer in one pass
                var count = _logBuffer.Count;
                var lines = new string[count];
                for (int i = 0; i < count; i++)
                    lines[i] = _logBuffer.Dequeue();

                LogOutput.AppendText(string.Join(Environment.NewLine, lines) + Environment.NewLine);
                LogOutput.ScrollToEnd();
            };
            _logTimer.Start();

            _disReceiver.EntityReceived += OnEntityReceived;
            _heartbeatTimer.Elapsed += OnHeartbeatTimerElapsed;
            _statusTimer.Tick += OnStatusTimerTick;
            _currentFilters = File.Exists(FilterFileName)
                ? JsonSerializer.Deserialize<FilterSettings>(File.ReadAllText(FilterFileName)) ?? new FilterSettings()
                : new FilterSettings();
            _profiles = File.Exists(ProfilesFile)
                ? JsonSerializer.Deserialize<Dictionary<string, FilterSettings>>(File.ReadAllText(ProfilesFile))
                  ?? new Dictionary<string, FilterSettings>()
                : new Dictionary<string, FilterSettings>();
            _replayTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _replayTimer.Tick += ReplayTimer_Tick;
            ApplyFilters();
            LoadDataGridLayout();
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
            var saved = NetworkConfig.DisIp;
            var selectedItem = DisIpComboBox.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(i => (string)i.Tag == saved) ?? DisIpComboBox.Items.OfType<ComboBoxItem>().FirstOrDefault();
            if (selectedItem != null)
            {
                DisIpComboBox.SelectedItem = selectedItem;
                NetworkConfig.DisIp = (string)selectedItem.Tag;
                Log($"[INFO] DIS IP initialized to {NetworkConfig.DisIp}");
            }
            else
            {
                NetworkConfig.DisIp = "0.0.0.0";
                Log($"[INFO] No DIS IP selected, defaulting to {NetworkConfig.DisIp}");
            }
            DisIpComboBox.SelectionChanged += (s, e) =>
            {
                if (DisIpComboBox.SelectedItem is ComboBoxItem item && item.Tag is string disIp)
                {
                    NetworkConfig.DisIp = disIp;
                    Log($"[INFO] DIS IP changed to {disIp}");
                }
            };
            EntityGrid.ColumnReordered += (s, e) => SaveDataGridLayout();

            _heartbeatTimer.Start();
            _heartbeatManager.Start();
        }

        private void ToggleReceiverButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_isReceiverRunning)
                {
                    _disReceiver.StopListening();
                    _isReceiverRunning = false;
                    DisIndicator.Fill = Brushes.Gray;
                    Log($"[INFO] DIS receiver stopped");
                    ToggleReceiverButton.Content = "Start Receiver";
                }
                else
                {
                    if (DisIpComboBox.SelectedItem is ComboBoxItem selectedItem && selectedItem.Tag is string disIp)
                    {
                        NetworkConfig.DisIp = disIp;
                    }
                    else
                    {
                        NetworkConfig.DisIp = "0.0.0.0";
                        Log($"[WARNING] No DIS IP selected, using {NetworkConfig.DisIp}");
                    }
                    if (!int.TryParse(DisPortTextBox.Text, out var disPort))
                    {
                        disPort = NetworkConfig.DisPort;
                        Log($"[WARNING] Invalid DIS port, using {disPort}");
                    }
                    _disReceiver.StartListening(NetworkConfig.DisIp, disPort);
                    _isReceiverRunning = true;
                    DisIndicator.Fill = Brushes.Green;
                    Log($"[INFO] DIS receiver started on {NetworkConfig.DisIp}:{disPort}");
                    ToggleReceiverButton.Content = "Stop Receiver";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to toggle receiver: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Log($"[ERROR] Failed to toggle receiver: {ex.Message}");
                _isReceiverRunning = false;
                DisIndicator.Fill = Brushes.Red;
                ToggleReceiverButton.Content = "Start Receiver";
            }
        }

        private void ApplySettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Console.WriteLine("ApplySettings_Click started at " + DateTime.Now);
                // Store old values
                string oldDisIp = NetworkConfig.DisIp;
                int oldDisPort = NetworkConfig.DisPort;
                string oldCotIp = NetworkConfig.CotIp;
                int oldCotPort = NetworkConfig.CotPort;

                // Update DIS IP
                if (DisIpComboBox.SelectedItem is ComboBoxItem selectedItem && selectedItem.Tag is string disIp)
                {
                    NetworkConfig.DisIp = disIp;
                    Console.WriteLine("DIS IP set to " + disIp);
                }
                else
                {
                    NetworkConfig.DisIp = "0.0.0.0";
                    Console.WriteLine("DIS IP defaulted to 0.0.0.0");
                }
                // Update DIS Port
                if (int.TryParse(DisPortTextBox.Text, out var disPort) && disPort > 0 && disPort <= 65535)
                {
                    NetworkConfig.DisPort = disPort;
                    Console.WriteLine("DIS Port set to " + disPort);
                }
                else
                {
                    NetworkConfig.DisPort = 3000;
                    Console.WriteLine("DIS Port defaulted to 3000");
                }
                // Log DIS changes
                if (NetworkConfig.DisPort != oldDisPort)
                    Log($"[INFO] DIS Port changed to {NetworkConfig.DisPort}");

                // Restart DIS receiver if running
                if (_isReceiverRunning)
                {
                    _disReceiver.StopListening();
                    _disReceiver.StartListening(NetworkConfig.DisIp, NetworkConfig.DisPort);
                    Console.WriteLine("DIS restarted at " + NetworkConfig.DisIp + ":" + NetworkConfig.DisPort);
                }
                // Update CoT IP
                if (!string.IsNullOrWhiteSpace(CotIpTextBox.Text) && IsValidIp(CotIpTextBox.Text))
                {
                    NetworkConfig.CotIp = CotIpTextBox.Text;
                    Console.WriteLine("CoT IP set to " + NetworkConfig.CotIp);
                }
                else
                {
                    NetworkConfig.CotIp = "224.0.0.2";
                    Console.WriteLine("CoT IP defaulted to 224.0.0.2");
                }
                // Update CoT Port
                if (int.TryParse(CotPortTextBox.Text, out var cotPort) && cotPort > 0 && cotPort <= 65535)
                {
                    NetworkConfig.CotPort = cotPort;
                    Console.WriteLine("CoT Port set to " + cotPort);
                }
                else
                {
                    NetworkConfig.CotPort = 4242;
                    Console.WriteLine("CoT Port defaulted to 4242");
                }
                // Log CoT changes
                if (NetworkConfig.CotIp != oldCotIp)
                    Log($"[INFO] CoT IP changed to {NetworkConfig.CotIp}");
                if (NetworkConfig.CotPort != oldCotPort)
                    Log($"[INFO] CoT Port changed to {NetworkConfig.CotPort}");

                // Reinitialize CoT sender
                try
                {
                    _cotSender = new CotUdpSender(NetworkConfig.CotIp, NetworkConfig.CotPort);
                    Console.WriteLine("CoT Sender reinitialized with " + NetworkConfig.CotIp + ":" + NetworkConfig.CotPort);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("CoT Sender init failed: " + ex.Message);
                    throw;
                }
                _heartbeatManager.Stop();
                _heartbeatManager.Start();
                Console.WriteLine("Heartbeat restarted");
                ErrorText.Text = "";
                MessageBox.Show("Settings applied and heartbeat restarted.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Exception in ApplySettings_Click: " + ex.Message + " at " + DateTime.Now);
                ErrorText.Text = "Apply failed: " + ex.Message;
                Log($"[ERROR] Apply failed: {ex.Message}");
            }
        }

        private void ApplyCotSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Store old values
                string oldCotIp = NetworkConfig.CotIp;
                int oldCotPort = NetworkConfig.CotPort;

                // Update CoT IP
                if (!string.IsNullOrWhiteSpace(CotIpTextBox.Text) && IsValidIp(CotIpTextBox.Text))
                {
                    NetworkConfig.CotIp = CotIpTextBox.Text;
                }
                else
                {
                    NetworkConfig.CotIp = "224.0.0.2";
                }
                // Update CoT Port
                if (int.TryParse(CotPortTextBox.Text, out int cotPort) && cotPort > 0 && cotPort <= 65535)
                {
                    NetworkConfig.CotPort = cotPort;
                }
                else
                {
                    NetworkConfig.CotPort = 4242;
                    MessageBox.Show("Invalid CoT port number.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                // Log changes
                if (NetworkConfig.CotIp != oldCotIp)
                    Log($"[INFO] CoT IP changed to {NetworkConfig.CotIp}");
                if (NetworkConfig.CotPort != oldCotPort)
                    Log($"[INFO] CoT Port changed to {NetworkConfig.CotPort}");

                _cotSender = new CotUdpSender(NetworkConfig.CotIp, NetworkConfig.CotPort);
                _heartbeatManager.Stop();
                _heartbeatManager.Start();
                MessageBox.Show("CoT settings applied and heartbeat restarted.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Log($"[ERROR] CoT settings apply failed: {ex.Message}");
                MessageBox.Show($"Failed to apply CoT settings: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ApplyHeartbeat_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(HeartbeatIntervalTextBox.Text, out int intervalSeconds))
            {
                _heartbeatManager.IntervalSeconds = intervalSeconds;
                LogOutput.AppendText($"[INFO] Updated heartbeat interval to {intervalSeconds} seconds.\n");
            }
            else
            {
                LogOutput.AppendText("[ERROR] Invalid heartbeat interval. Please enter a valid number.\n");
            }
        }

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
            HeartbeatIntervalTextBox.Text = _heartbeatManager.IntervalSeconds.ToString();
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

        private void OnHeartbeatTimerElapsed(object s, ElapsedEventArgs e)
        {
            var xml = CotBuilder.BuildPingCot();
            try
            {
                _cotSender.Send(xml);

                // Non-blocking UI update so heartbeat doesn't stall input/render
                Dispatcher.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.Background,
                    new Action(() =>
                    {
                        ErrorText.Text = "";
                        LastPingText.Text = "Last Ping: " + DateTime.Now.ToString("HH:mm:ss");
                        UpdateTakIndicator(true);
                    }));
            }
            catch (Exception ex)
            {
                Dispatcher.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.Background,
                    new Action(() =>
                    {
                        ErrorText.Text = "Send error: " + ex.Message;
                        UpdateTakIndicator(false);
                    }));
            }

            // Keep file I/O off the UI thread; this is already running on a timer thread.
            SaveCotToDailyLog(xml);
        }


        private void OnEntityReceived(EntityTrack track)
        {
            if (track == null || string.IsNullOrWhiteSpace(track.EntityId))
            {
                Dispatcher.BeginInvoke(new Action(() => Log("[ERROR] Received null track or invalid EntityId")));
                return;
            }

            _lastEntityReceived = DateTime.Now;

            // Build CoT now; file I/O stays off the UI thread
            var xml = CotBuilder.BuildCotXml(track);
            if (!track.Publish)
            {
                Dispatcher.BeginInvoke(new Action(() => Log($"[INFO] Skipping CoT for non-published track: EntityId={track.EntityId}")));
                return;
            }
            SaveCotToDailyLog(xml);

            // Non-blocking UI update. Let input/process/render stay responsive.
            Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Background,
                new Action(() =>
                {
                    var existingTrack = _entityTracks.FirstOrDefault(t =>
                        string.Equals(t.EntityId?.Trim(), track.EntityId?.Trim(), StringComparison.Ordinal));

                    if (existingTrack != null)
                    {
                        existingTrack.Lat = track.Lat;
                        existingTrack.Lon = track.Lon;
                        existingTrack.Altitude = track.Altitude;
                        existingTrack.LastUpdate = track.LastUpdate;

                        if (!existingTrack.IsCustomMarkingLocked)
                            existingTrack.CustomMarking = track.CustomMarking;

                        existingTrack.TrackType = track.TrackType;
                        existingTrack.PlatformType = track.PlatformType;
                        existingTrack.CountryCode = track.CountryCode;
                        existingTrack.IconType = track.IconType;
                        existingTrack.Mgrs = track.Mgrs;
                    }
                    else
                    {
                        // Ensure a visible name: use DIS marking if present, else the DIS Entity ID triple
                        if (string.IsNullOrWhiteSpace(track.CustomMarking))
                            track.CustomMarking = track.EntityId;

                        _entityTracks.Add(track);
                    }



                    ApplyFilters();

                    // Map updates temporarily disabled for perf test
                    if (!MAP_DISABLED_FOR_TEST && _mapWindow != null && _mapWindow.IsVisible)
                    {
                        _mapWindow.AddOrUpdateMarker(track);
                    }

                }));

            // Fire-and-forget CoT send. No per-entity Ping storm.
            Task.Run(() =>
            {
                try
                {
                    _cotSender.Send(xml);
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        ErrorText.Text = "";
                        LastPingText.Text = "Last Send: " + DateTime.Now.ToString("HH:mm:ss");
                        UpdateTakIndicator(true);
                    }));
                }
                catch (Exception ex)
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        ErrorText.Text = "Send error: " + ex.Message;
                        UpdateTakIndicator(false);
                    }));
                }
            });
        }


        private void OnStatusTimerTick(object s, EventArgs e)
        {
            var displayed = EntityGrid.Items.OfType<EntityTrack>().ToList();
            int total = displayed.Count;
            var groups = displayed
                .GroupBy(t => t.TrackType ?? "Unknown")
                .Select(g => $"{g.Key}:{g.Count()}")
                .ToArray();
            string text = $"Total: {total}";
            if (groups.Length > 0)
                text += " | " + string.Join(", ", groups);
            TrackCountText.Text = text;
            if (DateTime.Now - _lastEntityReceived > DisTimeout)
                DisIndicator.Fill = Brushes.Red;
            else
                DisIndicator.Fill = Brushes.Green;
            var pruneThreshold = TimeSpan.FromMinutes(5);
            var toRemove = _entityTracks
                .Where(t => DateTime.Now - t.LastUpdate > pruneThreshold)
                .ToList();
            foreach (var old in toRemove)
                _entityTracks.Remove(old);
        }

        public void Log(string msg)
        {
            // Enqueue and trim the buffer if someone goes wild
            _logBuffer.Enqueue(msg);
            const int maxQueued = 1000;   // safety cap
            if (_logBuffer.Count > maxQueued)
            {
                while (_logBuffer.Count > maxQueued * 0.8)
                    _logBuffer.Dequeue();
            }
        }


        private void SaveCotToDailyLog(string xml)
        {
            try
            {
                var folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
                Directory.CreateDirectory(folder);
                File.AppendAllText(Path.Combine(folder, $"CoT_{DateTime.Now:yyyy-MM-dd}.log"), xml + "\n");
            }
            catch (Exception ex)
            {
                Log($"[ERROR] Log write failed: {ex.Message}");
            }
        }

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
                        var mark = t.CustomMarking?.Replace(",", "_") ?? "";
                        sw.WriteLine($"{t.Id},{mark},{t.Lat},{t.Lon},{t.Altitude},{t.LastUpdate:O},{t.EntityKind},{t.Domain},{t.TrackType},{t.CountryCode},{t.IconType},{t.Mgrs},{t.Publish}");
                    }
                }
                Log($"[INFO] CSV saved to {dlg.FileName}");
            }
            catch (Exception ex)
            {
                Log($"[ERROR] CSV save failed: {ex.Message}");
            }
        }

        private void PreviewCot_Click(object s, RoutedEventArgs e)
        {
            var track = EntityGrid.SelectedItem as EntityTrack;
            var xml = track != null
                ? CotBuilder.BuildCotXml(track)
                : CotBuilder.BuildPingCot();
            new CotPreviewWindow(xml) { Owner = this }.ShowDialog();
        }

        private void LoadFilters_Click(object s, RoutedEventArgs e)
        {
            var seenDomains = _entityTracks.Select(t => t.Domain.ToString());
            var seenKinds = _entityTracks.Select(t => t.TrackType);
            var allDomains = DefaultDomains.Union(seenDomains).Distinct().OrderBy(x => x);
            var allKinds = DefaultKinds.Union(seenKinds).Distinct().OrderBy(x => x);
            var loaded = File.Exists(FilterFileName)
                ? JsonSerializer.Deserialize<FilterSettings>(File.ReadAllText(FilterFileName))
                  ?? new FilterSettings()
                : new FilterSettings();
            var dlg = new FilterSettingsWindow(loaded, allDomains, allKinds) { Owner = this };
            if (dlg.ShowDialog() != true)
                return;
            _currentFilters = dlg.Settings;
            File.WriteAllText(FilterFileName,
                JsonSerializer.Serialize(_currentFilters, new JsonSerializerOptions { WriteIndented = true }));
            ApplyFilters();
            MessageBox.Show("Filters loaded.", "Load Filters", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void SaveFilters_Click(object s, RoutedEventArgs e)
        {
            var seenDomains = _entityTracks.Select(t => t.Domain.ToString());
            var seenKinds = _entityTracks.Select(t => t.TrackType);
            var allDomains = DefaultDomains.Union(seenDomains).Distinct().OrderBy(x => x);
            var allKinds = DefaultKinds.Union(seenKinds).Distinct().OrderBy(x => x);
            var dlg = new FilterSettingsWindow(_currentFilters, allDomains, allKinds) { Owner = this };
            if (dlg.ShowDialog() != true)
                return;
            _currentFilters = dlg.Settings;
            File.WriteAllText(FilterFileName,
                JsonSerializer.Serialize(_currentFilters, new JsonSerializerOptions { WriteIndented = true }));
            ApplyFilters();
            MessageBox.Show("Filters saved.", "Save Filters", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void SaveProfile_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new InputDialog("Enter a name for this filter profile:") { Owner = this };
            if (dlg.ShowDialog() != true) return;
            var name = dlg.ResponseText.Trim();
            if (string.IsNullOrEmpty(name)) return;
            if (_profiles.ContainsKey(name) &&
                MessageBox.Show($"Profile '{name}' already exists. Overwrite?",
                                "Confirm Overwrite",
                                MessageBoxButton.YesNo,
                                MessageBoxImage.Question)
                != MessageBoxResult.Yes)
            {
                return;
            }
            var copy = JsonSerializer.Deserialize<FilterSettings>(
                JsonSerializer.Serialize(_currentFilters));
            _profiles[name] = copy;
            File.WriteAllText(ProfilesFile,
                JsonSerializer.Serialize(_profiles, new JsonSerializerOptions { WriteIndented = true }));
            MessageBox.Show($"Profile '{name}' saved.", "Save Profile", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void LoadProfile_Click(object sender, RoutedEventArgs e)
        {
            if (_profiles.Count == 0)
            {
                MessageBox.Show("No profiles to load.", "Load Profile", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            string list = string.Join(Environment.NewLine, _profiles.Keys);
            var dlg = new InputDialog("Available profiles:\n" + list + "\n\nName to load:") { Owner = this };
            if (dlg.ShowDialog() != true) return;
            var name = dlg.ResponseText.Trim();
            if (!_profiles.ContainsKey(name)) return;
            _currentFilters = JsonSerializer.Deserialize<FilterSettings>(
                JsonSerializer.Serialize(_profiles[name]));
            File.WriteAllText(FilterFileName,
                JsonSerializer.Serialize(_currentFilters, new JsonSerializerOptions { WriteIndented = true }));
            ApplyFilters();
            MessageBox.Show($"Profile '{name}' loaded.", "Load Profile", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void DeleteProfile_Click(object sender, RoutedEventArgs e)
        {
            if (_profiles.Count == 0)
            {
                MessageBox.Show("No profiles to delete.", "Delete Profile", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            string list = string.Join(Environment.NewLine, _profiles.Keys);
            var dlg = new InputDialog("Available profiles:\n" + list + "\n\nName to delete:") { Owner = this };
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
            File.WriteAllText(ProfilesFile,
                JsonSerializer.Serialize(_profiles, new JsonSerializerOptions { WriteIndented = true }));
            MessageBox.Show($"Profile '{name}' deleted.", "Delete Profile", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void OpenMapPreview_Click(object s, RoutedEventArgs e)
        {
            if (_mapWindow?.IsVisible == true)
            {
                _mapWindow.Activate();
            }
            else
            {
                _mapWindow = new MapWindow();
                _mapWindow.Owner = this;
                _mapWindow.MapReady += () =>
                {
                    var testTrack = new EntityTrack
                    {
                        Id = 999,
                        Lat = 32.8364,
                        Lon = -118.5208,
                        CustomMarking = "TEST",
                        TrackType = "Ground",
                        LastUpdate = DateTime.Now
                    };
                    _mapWindow.AddOrUpdateMarker(testTrack);
                };
                _mapWindow.Show();
            }
        }

        private void OpenDownloadArea_Click(object s, RoutedEventArgs e)
            => new DownloadAreaWindow { Owner = this }.Show();

        private void ImportMbtiles_Click(object s, RoutedEventArgs e)
            => MessageBox.Show("Not implemented", "MBTiles", MessageBoxButton.OK, MessageBoxImage.Information);

        private void OpenNetworkSettings_Click(object s, RoutedEventArgs e)
        {
            var settingsWindow = new NetworkSettingsWindow
            {
                Owner = this
            };
            settingsWindow.ShowDialog();
        }
        private void OpenLogsFolder_Click(object s, RoutedEventArgs e)
        {
            var f = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
            Directory.CreateDirectory(f);
            System.Diagnostics.Process.Start("explorer.exe", f);
        }

        private void LoadLogForReplay_Click(object sender, RoutedEventArgs e)
        {
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
                var entries = File.ReadAllLines(dlg.FileName)
                                  .Select(ParseLogLine)
                                  .Where(x => x.time != DateTime.MinValue)
                                  .OrderBy(x => x.time)
                                  .ToList();
                _replayEvents = entries;
                MessageBox.Show($"Loaded {_replayEvents.Count} events for replay.",
                                "Load Log",
                                MessageBoxButton.OK,
                                MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load log:\n{ex.Message}",
                                "Load Log Error",
                                MessageBoxButton.OK,
                                MessageBoxImage.Error);
            }
        }

        private (DateTime time, string xml) ParseLogLine(string line)
        {
            var m = Regex.Match(line, @"time=""(?<t>[^""]+)""");
            if (!m.Success || !DateTime.TryParse(m.Groups["t"].Value, out DateTime dt))
                return (DateTime.MinValue, null);
            return (dt.ToUniversalTime(), line);
        }

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
            return 1.0;
        }

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
            ReplaySpeedText.Text = $"Speed: {speed:0.0}x";
            while (_replayIndex < _replayEvents.Count &&
                   _replayEvents[_replayIndex].time <= targetTime)
            {
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
                    return;
                Dispatcher.Invoke(() =>
                {
                    _entityTracks.Add(track);
                    ApplyFilters();
                    // Map updates temporarily disabled for perf test
                    if (!MAP_DISABLED_FOR_TEST && _mapWindow?.IsVisible == true)
                    {
                        _mapWindow.AddOrUpdateMarker(track);
                    }

                    if (sendOk)
                    {
                        ErrorText.Text = "";
                        LastPingText.Text = "Last Ping: " + DateTime.Now.ToString("HH:mm:ss");
                        UpdateTakIndicator(true);
                    }
                });
                SaveCotToDailyLog(xml);
                _replayIndex++;
            }
        }

        private void TestTrack_Click(object sender, RoutedEventArgs e)
        {
            bool success = false;
            try
            {
                var ping = new Ping();
                var reply = ping.Send(NetworkConfig.CotIp, 1000);
                if (reply.Status != IPStatus.Success)
                {
                    ErrorText.Text = $"Ping failed: {reply.Status}";
                    UpdateTakIndicator(false);
                    return;
                }
                var testEntity = new EntityTrack
                {
                    Id = 999,
                    Lat = 32.8364,
                    Lon = -118.5208,
                    CustomMarking = "TEST",
                    TrackType = "Ground",
                    LastUpdate = DateTime.Now
                };
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

        private void Search_Click(object sender, RoutedEventArgs e)
        {
            string term = SearchTextBox.Text.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(term))
            {
                EntityGrid.ItemsSource = _entityTracks;
                return;
            }
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

        private void ClearSearch_Click(object sender, RoutedEventArgs e)
        {
            SearchTextBox.Clear();
            var view = CollectionViewSource.GetDefaultView(_entityTracks);
            view.Filter = null;
            view.Refresh();
        }

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
            catch { }
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
            catch { }
        }

        private class ColumnLayout
        {
            public string Header { get; set; }
            public int DisplayIndex { get; set; }
            public double Width { get; set; }
            public bool IsVisible { get; set; }
        }

        private void ApplyFilters()
        {
            var view = CollectionViewSource.GetDefaultView(_entityTracks);
            view.Filter = obj =>
            {
                var t = obj as EntityTrack;
                if (t == null) return false;

                bool domainOk = _currentFilters.AllowedDomains.Count == 0
                                || _currentFilters.AllowedDomains.Contains(t.Domain.ToString());

                bool kindOk = _currentFilters.AllowedKinds.Count == 0
                              || _currentFilters.AllowedKinds.Contains(t.TrackType);

                bool publishOk = !_currentFilters.PublishOnly || t.Publish;

                return domainOk && kindOk && publishOk;
            };

            // Keep ItemsSource pointing at the filtered view, but don't refresh immediately.
            EntityGrid.ItemsSource = view;

            // Ask the throttle to refresh on the next tick instead of right now.
            _pendingViewRefresh = true;
        }


        private void UpdateTakIndicator(bool success)
        {
            Dispatcher.Invoke(() =>
            {
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
                    var text = File.ReadAllText(dlg.FileName);
                    tracks = JsonSerializer.Deserialize<List<EntityTrack>>(text)
                             ?? new List<EntityTrack>();
                }
                else
                {
                    var lines = File.ReadAllLines(dlg.FileName);
                    var header = lines[0].Split(',');
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
                var detailWindow = new TrackDetailWindow(selectedTrack) { Owner = this };
                if (detailWindow.ShowDialog() == true)
                {
                    string oldMarking = selectedTrack.CustomMarking;
                    selectedTrack.CustomMarking = detailWindow.CustomMarking;
                    selectedTrack.IsCustomMarkingLocked = detailWindow.IsLocked;
                    if (oldMarking != selectedTrack.CustomMarking)
                    {
                        Log($"[INFO] CustomMarking for EntityId={selectedTrack.EntityId} changed to {selectedTrack.CustomMarking}");
                        if (selectedTrack.Publish)
                        {
                            string xml = CotBuilder.BuildCotXml(selectedTrack);
                            if (!string.IsNullOrEmpty(xml))
                            {
                                _cotSender.Send(xml);
                                SaveCotToDailyLog(xml);
                            }
                        }
                    }
                    EntityGrid.Items.Refresh();
                }
            }
        }

        private void CotPortTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            // No action; updates handled by ApplySettings_Click
        }

        private bool IsValidIp(string ip)
        {
            if (string.IsNullOrWhiteSpace(ip)) return false;
            return Regex.IsMatch(ip, @"^(\d{1,3}\.){3}\d{1,3}$") &&
                   ip.Split('.').All(x => int.Parse(x) >= 0 && int.Parse(x) <= 255);
        }
    }
}