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

        public FilterSettings Settings { get; }

        public FilterSettingsWindow(
            FilterSettings existing,
            IEnumerable<string> availableDomains,
            IEnumerable<string> availableKinds)
        {
            InitializeComponent();

            // Clone existing so Cancel won't mutate it
            Settings = new FilterSettings
            {
                AllowedDomains = existing.AllowedDomains.ToList(),
                AllowedKinds = existing.AllowedKinds.ToList(),
                PublishOnly = existing.PublishOnly
            };

            // Populate the Domains checkboxes
            foreach (var code in availableDomains)
            {
                // Look up a friendly name, or fall back to the code itself
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

            // Populate the Kinds checkboxes (just the string values)
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

            PublishOnlyCheckBox.IsChecked = Settings.PublishOnly;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            // Read back only the Tag values (the numeric codes) for domains
            Settings.AllowedDomains = DomainPanel.Children
                .OfType<CheckBox>()
                .Where(cb => cb.IsChecked == true)
                .Select(cb => cb.Tag.ToString())
                .ToList();

            // Read back kinds
            Settings.AllowedKinds = KindPanel.Children
                .OfType<CheckBox>()
                .Where(cb => cb.IsChecked == true)
                .Select(cb => cb.Tag.ToString())
                .ToList();

            Settings.PublishOnly = PublishOnlyCheckBox.IsChecked == true;

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
