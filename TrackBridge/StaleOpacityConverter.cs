using System;
using System.Globalization;
using System.Windows.Data;

namespace TrackBridge
{
    /// <summary>
    /// Converts a timestamp (DateTime) to an opacity: 
    /// 0.5 if (now - timestamp) &gt; ThresholdSeconds, else 1.
    /// </summary>
    public class StaleOpacityConverter : IValueConverter
    {
        // seconds before a row is considered stale
        public int ThresholdSeconds { get; set; } = 30;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is DateTime dt)
            {
                var age = DateTime.UtcNow - dt;
                return age.TotalSeconds > ThresholdSeconds ? 0.5 : 1.0;
            }
            return 1.0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
