using System;

namespace TrackBridge.Utilities
{
    public static class EcefConverter
    {
        // WGS84 ellipsoid constants
        private const double a = 6378137.0;           // semi-major axis
        private const double e2 = 6.69437999014e-3;   // first eccentricity squared

        public static void ToLatLonAlt(double x, double y, double z, out double lat, out double lon, out double alt)
        {
            double b = a * Math.Sqrt(1 - e2); // semi-minor axis
            double ep = Math.Sqrt((a * a - b * b) / (b * b));
            double p = Math.Sqrt(x * x + y * y);
            double theta = Math.Atan2(z * a, p * b);

            double sinTheta = Math.Sin(theta);
            double cosTheta = Math.Cos(theta);

            lat = Math.Atan2(z + ep * ep * b * sinTheta * sinTheta * sinTheta,
                             p - e2 * a * cosTheta * cosTheta * cosTheta);
            lon = Math.Atan2(y, x);

            double sinLat = Math.Sin(lat);
            double N = a / Math.Sqrt(1 - e2 * sinLat * sinLat);
            alt = p / Math.Cos(lat) - N;

            // Convert radians to degrees
            lat = lat * (180.0 / Math.PI);
            lon = lon * (180.0 / Math.PI);
        }
    }
}
