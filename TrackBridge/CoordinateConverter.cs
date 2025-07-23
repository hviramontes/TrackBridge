using CoordinateSharp;
namespace TrackBridge
{
    public static class CoordinateConverter
    {
        private const double OriginLat = 34.0000;
        private const double OriginLon = -117.0000; public static (double lat, double lon, double alt) ToLatLon(double x, double y, double z)
        {
            double lat = OriginLat + (y / 111320.0);
            double lon = OriginLon + (x / (111320.0 * System.Math.Cos(OriginLat * System.Math.PI / 180.0)));
            double alt = z;
            return (lat, lon, alt);
        }

        public static string ToMgrs(double lat, double lon)
        {
            // Create a coordinate object
            Coordinate coord = new Coordinate(lat, lon);

            // Get the MGRS string with 5-digit precision
            return coord.MGRS.ToString();  // Defaults to full precision (10-digit)
        }
    }
}

