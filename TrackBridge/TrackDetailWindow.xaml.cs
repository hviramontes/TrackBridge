using System.Windows;

namespace TrackBridge
{
    public partial class TrackDetailWindow : Window
    {
        public string CustomMarking { get; private set; }
        public bool IsLocked { get; private set; }

        public TrackDetailWindow(EntityTrack track)
        {
            InitializeComponent();

            // Populate fields
            TxtEntityId.Text = track.EntityId;
            TxtMarking.Text = track.CustomMarking;
            ChkLock.IsChecked = track.IsCustomMarkingLocked;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            CustomMarking = TxtMarking.Text.Trim();
            IsLocked = true; // Lock by default to prevent DIS overwrites
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
