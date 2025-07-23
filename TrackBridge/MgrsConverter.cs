using System;

namespace TrackBridge
{
    public static class MgrsConverter
    {
        /// <summary>
        /// Original full‐precision conversion.
        /// </summary>
        public static string LatLonToMgrs(double lat, double lon)
        {
            // Your existing implementation here.
            // For example purposes only:
            throw new NotImplementedException("Implement your base MGRS conversion here");
        }

        /// <summary>
        /// Overload that allows specifying grid precision:
        /// digits = 2 → 1 km, 3 → 100 m, 4 → 10 m.
        /// </summary>
        public static string LatLonToMgrs(double lat, double lon, int digits)
        {
            // Call the full conversion first
            string full = LatLonToMgrs(lat, lon);
            // Determine how many characters to keep:
            // Typically zone + square is 5 chars, then digits*2 more.
            // e.g. "18SUJ23456789" → 5 + (digits*2)
            int keep = 5 + (digits * 2);
            if (full.Length >= keep)
                return full.Substring(0, keep);
            return full;
        }
    }
}
