using CoordinateSharp;
using System;

namespace TrackBridge
{
    public static class MgrsConverter
    {
        /// <summary>
        /// Full-precision MGRS conversion using CoordinateSharp.
        /// </summary>
        public static string LatLonToMgrs(double lat, double lon)
        {
            var coord = new Coordinate(lat, lon);
            // coord.MGRS is a MilitaryGridReferenceSystem object—call ToString()
            return coord.MGRS.ToString();
        }

        /// <summary>
        /// Overload that allows specifying grid precision:
        /// digits = 2 → 1 km, 3 → 100 m, 4 → 10 m.
        /// </summary>
        public static string LatLonToMgrs(double lat, double lon, int digits)
        {
            string full = LatLonToMgrs(lat, lon);
            int keep = 5 + (digits * 2);  // zone+square (5 chars) + precision digits*2
            return full.Length >= keep
                ? full.Substring(0, keep)
                : full;
        }
    }
}
