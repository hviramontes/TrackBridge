using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace TrackBridge
{
    public partial class FilterSettingsWindow : Window
    {
        // Map of DIS domain codes → human-readable names
        private static readonly Dictionary<string, string> DomainNames = new Dictionary<string, string>
        {
            { "1", "Land"       },
            { "2", "Air"        },
            { "3", "Surface"    },
            { "4", "Subsurface" },
            { "5", "Space"      }
        };

        /// <summary>
        /// The settings object we will read from / write to.
        /// </summary>
        public FilterSettings Settings { get; }

        public FilterSettingsWindow(
            FilterSettings existing,
            IEnumerable<string> availableDomains,
            IEnumerable<string> availableKinds)
        {
            InitializeComponent();

            // 1) Clone all properties so Cancel won't mutate the original
            Settings = new FilterSettings
            {
                AllowedDomains = existing.AllowedDomains.ToList(),
                AllowedKinds = existing.AllowedKinds.ToList(),
                PublishOnly = existing.PublishOnly,
                StaleThresholdSeconds = existing.StaleThresholdSeconds  // ← include this
            };

            // 2) Populate the domain checkboxes
            foreach (var code in availableDomains)
            {
                string name = DomainNames.TryGetValue(code, out var n) ? n : code;
                var cb = new CheckBox
                {
                    Tag = code,
                    Content = $"{code} – {name}",
                    IsChecked = Settings.AllowedDomains.Contains(code),
                    Margin = new Thickness(0, 0, 0, 2)
                };
                DomainPanel.Children.Add(cb);
            }

            // 3) Populate the kind checkboxes
            foreach (var kind in availableKinds)
            {
                var cb = new CheckBox
                {
                    Tag = kind,
                    Content = kind,
                    IsChecked = Settings.AllowedKinds.Contains(kind),
                    Margin = new Thickness(0, 0, 0, 2)
                };
                KindPanel.Children.Add(cb);
            }

            // 4) Initialize the PublishOnly checkbox and the stale threshold textbox
            PublishOnlyCheckBox.IsChecked = Settings.PublishOnly;
            StaleThresholdTextBox.Text = Settings.StaleThresholdSeconds.ToString();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            // 1) Read back allowed domains
            Settings.AllowedDomains = DomainPanel.Children
                .OfType<CheckBox>()
                .Where(cb => cb.IsChecked == true)
                .Select(cb => cb.Tag.ToString())
                .ToList();

            // 2) Read back allowed kinds
            Settings.AllowedKinds = KindPanel.Children
                .OfType<CheckBox>()
                .Where(cb => cb.IsChecked == true)
                .Select(cb => cb.Tag.ToString())
                .ToList();

            // 3) Read back PublishOnly
            Settings.PublishOnly = PublishOnlyCheckBox.IsChecked == true;

            // 4) Read and save the stale threshold
            if (int.TryParse(StaleThresholdTextBox.Text, out var secs))
                Settings.StaleThresholdSeconds = secs;

            // 5) Close with OK
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            // Close with Cancel
            DialogResult = false;
            Close();
        }
    }
}
