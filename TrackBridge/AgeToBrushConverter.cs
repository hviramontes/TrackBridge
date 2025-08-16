using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace TrackBridge
{
    /// <summary>
    /// Converts a track’s LastUpdate timestamp into a background brush:
    /// • <1 min → Transparent  
    /// • 1–2 min → LightYellow  
    /// • 2–4 min → Orange  
    /// • ≥4 min → Red  
    /// </summary>
    public class AgeToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is DateTime last)
            {
                var age = DateTime.UtcNow - last.ToUniversalTime();
                if (age >= TimeSpan.FromMinutes(4)) return Brushes.Red;
                if (age >= TimeSpan.FromMinutes(2)) return Brushes.Orange;
                if (age >= TimeSpan.FromMinutes(1)) return Brushes.LightYellow;
            }
            return Brushes.Transparent;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
