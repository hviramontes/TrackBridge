using System;
using System.Diagnostics;
using System.Globalization;
using System.Xml.Linq;
using TrackBridge;


namespace TrackBridge.CoT
{
    public static class CotBuilder
    {
        public static string BuildCotXml(EntityTrack track)
        {
            try
            {
                if (track == null)
                {
                    Debug.WriteLine("Error: EntityTrack is null");
                    return null;
                }

                // Use provided geodetic coordinates (Lat, Lon, Altitude)
                double lat = track.Lat;
                double lon = track.Lon;
                double hae = track.Altitude;

                // Validate coordinates
                if (lat < -90.0 || lat > 90.0 || lon < -180.0 || lon > 180.0)
                {
                    Debug.WriteLine($"Invalid coordinates for track {track.Id}: lat={lat}, lon={lon}, hae={hae}");
                    return BuildPingCot();
                }

                string uid = $"TrackBridge-{track.EntityId}";
                string how = "m-g";
                string callsign = !string.IsNullOrWhiteSpace(track.CustomMarking)
                    ? track.CustomMarking
                    : $"Track-{track.EntityId}";

                string symbolId = !string.IsNullOrWhiteSpace(track.IconType) && IsValidMilStd2525(track.IconType)
                    ? track.IconType
                    : "SFGPUCI----K---";

                string cotType = GetCotType(track.TrackType, track.Domain.ToString());

                string ce = "10.0";
                string le = "10.0";

                Debug.WriteLine($"CoT Built: UID={uid}, Type={cotType}, Symbol={symbolId}, Lat={lat}, Lon={lon}, HAE={hae}");

                return new XElement("event",
                    new XAttribute("version", "2.0"),
                    new XAttribute("uid", uid),
                    new XAttribute("type", cotType),
                    new XAttribute("how", how),
                    new XAttribute("time", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)),
                    new XAttribute("start", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)),
                    new XAttribute("stale", DateTime.UtcNow.AddSeconds(30).ToString("o", CultureInfo.InvariantCulture)),
                    new XElement("point",
                        new XAttribute("lat", lat.ToString(CultureInfo.InvariantCulture)),
                        new XAttribute("lon", lon.ToString(CultureInfo.InvariantCulture)),
                        new XAttribute("hae", hae.ToString(CultureInfo.InvariantCulture)),
                        new XAttribute("ce", ce),
                        new XAttribute("le", le)
                    ),
                    new XElement("detail",
                        new XElement("symbol",
                            new XAttribute("symbol", symbolId)
                        ),
                        new XElement("contact",
                            new XAttribute("callsign", callsign)
                        ),
                        new XElement("group",
                            new XAttribute("role", GetGroupRole(track.Domain.ToString())),
                            new XAttribute("country", track.CountryCode ?? string.Empty),
                            new XAttribute("iconType", symbolId)
                        ),
                        new XElement("entity_id",
                            new XAttribute("value", track.EntityId)
                        )
                    )
                ).ToString(SaveOptions.DisableFormatting);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error building CoT for track {track?.Id}: {ex.Message}");
                return null;
            }
        }

        public static string BuildPingCot()
        {
            try
            {
                string uid = "TrackBridge-Heartbeat";
                string how = "m-g";
                string type = "a-f-G-U-C";

                return new XElement("event",
                    new XAttribute("version", "2.0"),
                    new XAttribute("uid", uid),
                    new XAttribute("type", type),
                    new XAttribute("how", how),
                    new XAttribute("time", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)),
                    new XAttribute("start", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)),
                    new XAttribute("stale", DateTime.UtcNow.AddSeconds(30).ToString("o", CultureInfo.InvariantCulture)),
                    new XElement("point",
                        new XAttribute("lat", "0.0"),
                        new XAttribute("lon", "0.0"),
                        new XAttribute("hae", "0.0"),
                        new XAttribute("ce", "9999999.0"),
                        new XAttribute("le", "9999999.0")
                    ),
                    new XElement("detail",
                        new XElement("contact",
                            new XAttribute("callsign", "TrackBridge")
                        )
                    )
                ).ToString(SaveOptions.DisableFormatting);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error building ping CoT: {ex.Message}");
                return null;
            }
        }

        private static string GetCotType(string trackType, string domain)
        {
            if (trackType == "Friendly")
            {
                if (domain == "1") return "a-f-G";
                if (domain == "2") return "a-f-A";
                if (domain == "3") return "a-f-S";
            }
            else if (trackType == "Enemy")
            {
                if (domain == "1") return "a-h-G";
                if (domain == "2") return "a-h-A";
                if (domain == "3") return "a-h-S";
            }
            return "a-u-U";
        }

        private static string GetGroupRole(string domain)
        {
            switch (domain)
            {
                case "1": return "ground";
                case "2": return "air";
                case "3": return "sea";
                default: return "unknown";
            }
        }

        private static bool IsValidMilStd2525(string symbolId)
        {
            return !string.IsNullOrWhiteSpace(symbolId) && symbolId.Length == 15;
        }

        private static double Clamp(double value, double min, double max)
        {
            return Math.Max(min, Math.Min(max, value));
        }
    }
}
