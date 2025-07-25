using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using TrackBridge;  // for EntityTrack
using TrackBridge.Utilities;  // Added for EcefConverter

namespace TrackBridge.DIS
{
    public class DisReceiver
    {
        private UdpClient udpClient;
        private CancellationTokenSource cts;
        private readonly Dictionary<string, EntityTrack> trackCache = new Dictionary<string, EntityTrack>();


        // Event raised when an Entity PDU is received and parsed
        public event Action<EntityTrack> EntityReceived;

        public void StartListening(string ip, int port)
        {
            StopListening(); // stop if already running

            cts = new CancellationTokenSource();

            // Create appropriate socket for Unicast, Broadcast, or Multicast
            IPAddress ipAddress = IPAddress.Parse(ip);
            udpClient = new UdpClient(port);

            if (ipAddress.IsMulticast())
            {
                udpClient.JoinMulticastGroup(ipAddress);
            }

            Task.Run(() => ListenLoop(udpClient, cts.Token));
        }

        public void StopListening()
        {
            cts?.Cancel();
            udpClient?.Close();
            udpClient = null;
        }

        private async Task ListenLoop(UdpClient client, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var result = await client.ReceiveAsync();

                    if (result.Buffer.Length > 0)
                    {
                        var track = ParseEntityPdu(result.Buffer, result.RemoteEndPoint);
                        if (track != null)
                        {
                            EntityReceived?.Invoke(track);
                        }
                    }
                }
                catch (ObjectDisposedException)
                {
                    // Socket was closed
                }
                catch (Exception ex)
                {
                    Console.WriteLine("DIS receive error: " + ex.Message);
                }
            }
        }

        private EntityTrack ParseEntityPdu(byte[] buffer, IPEndPoint _sender)
        {
            // Minimum PDU size check, Entity State PDU type (1), and Protocol Version (6 or 7)
            if (buffer.Length < 48 || buffer[2] != 1 || (buffer[0] != 6 && buffer[0] != 7))
            {
                Console.WriteLine($"Invalid PDU: Length={buffer.Length}, Type={buffer[2]}, Version={buffer[0]}");
                return null;
            }

            try
            {
                // Extract Entity ID
                int siteId = ToUInt16BigEndian(buffer, 12);
                int appId = ToUInt16BigEndian(buffer, 14);
                int entityId = ToUInt16BigEndian(buffer, 16);

                // Extract ECEF coordinates (in meters)
                double x = ToDoubleBigEndian(buffer, 48);
                double y = ToDoubleBigEndian(buffer, 56);
                double z = ToDoubleBigEndian(buffer, 64);

                // Log raw ECEF for debugging
                Console.WriteLine($"Raw ECEF: X={x}, Y={y}, Z={z}");

                // Convert ECEF to LLA (WGS84) using precise utility
                double lat, lon, hae;
                EcefConverter.ToLatLonAlt(x, y, z, out lat, out lon, out hae);

                // Adjust HAE for simulation ground level if needed
                if (hae < -1000.0) hae = 0.0; // Example adjustment for ground entities

                // Optional validation for edge cases
                if (double.IsNaN(lat) || double.IsNaN(lon) || double.IsNaN(hae))
                {
                    Console.WriteLine($"Invalid LLA conversion for ECEF={x},{y},{z}");
                    lat = 0.0;
                    lon = 0.0;
                    hae = 0.0;
                }

                // Log converted LLA for debugging
                Console.WriteLine($"Converted LLA: Lat={lat}, Lon={lon}, HAE={hae}");

                // Extract Force ID and Entity Type
                byte forceId = buffer[11]; // Byte 12 (0-based index)
                byte entityKind = buffer[20];
                byte domain = buffer[21];
                int country = ToUInt16BigEndian(buffer, 22);
                byte category = buffer[24];

                // Map country code
                string countryCode = MapCountryCode((ushort)country); // Cast to ushort for mapping

                // Map Force ID to TrackType, with fallback to country-based affiliation
                string trackType = forceId == 1 ? "Friendly" : forceId == 2 ? "Enemy" : "Unknown";
                if (trackType == "Unknown")
                {
                    switch (countryCode)
                    {
                        case "USA":
                        case "UK":
                        case "AUS":
                        case "FRA":
                        case "CAN":
                            trackType = "Friendly";
                            break;
                        case "RUS":
                        case "CHN":
                        case "IRN":
                        case "PRK":
                            trackType = "Enemy";
                            break;
                        default:
                            trackType = "Unknown";
                            break;
                    }
                }

                // Map Entity Type to MIL-STD-2525C symbol
                string iconType = MapEntityTypeToSymbol(entityKind, domain, (byte)country, category);

                // Log for debugging
                Console.WriteLine($"DIS Parsed: EntityID={siteId}:{appId}:{entityId}, ForceID={forceId}, Type={entityKind}:{domain}:{country}:{category}, ECEF={x},{y},{z}, LLA={lat},{lon},{hae}, CountryCode={countryCode}, TrackType={trackType}");
                string entityKey = $"{siteId}:{appId}:{entityId}";

                var track = new EntityTrack
                {
                    EntityId = entityKey,
                    Id = entityKey.GetHashCode(),
                    PlatformType = GetPlatformType(entityKind, category),
                    TrackType = trackType,
                    Lat = lat,
                    Lon = lon,
                    Altitude = hae,
                    LastUpdate = DateTime.UtcNow,
                    EntityKind = entityKind,
                    Domain = domain,
                    CountryCode = countryCode,
                    IconType = iconType,
                    Publish = true
                };

                // Check cache for previous version
                if (trackCache.TryGetValue(entityKey, out var previous))
                {
                    if (previous.IsCustomMarkingLocked)
                    {
                        track.CustomMarking = previous.CustomMarking;
                        track.IsCustomMarkingLocked = true;
                    }
                }

                // If not locked, extract marking from DIS PDU (bytes 72–82, null-terminated)
                if (!track.IsCustomMarkingLocked && buffer.Length >= 83)
                {
                    byte[] markingBytes = new byte[11];
                    Array.Copy(buffer, 72, markingBytes, 0, 11);
                    string marking = System.Text.Encoding.ASCII.GetString(markingBytes).TrimEnd('\0');

                    if (!string.IsNullOrWhiteSpace(marking))
                        track.CustomMarking = marking;
                }

                // Fallback marking if nothing else
                if (string.IsNullOrWhiteSpace(track.CustomMarking))
                {
                    track.CustomMarking = entityKey;
                }


                // Update cache
                trackCache[entityKey] = track;

                return track;

            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing DIS PDU: {ex.Message}");
                return null;
            }
        }

        // Map DIS Entity Type to MIL-STD-2525C symbol
        private string MapEntityTypeToSymbol(byte kind, byte domain, byte country, byte category)
        {
            if (kind == 1 && domain == 2 && country == 225 && category == 11) return "SHGPUCT----K---"; // M1A1 Abrams (Enemy)
            if (kind == 1 && domain == 2 && country == 225 && category == 2) return "SFGPUCV----K---"; // Utility Truck (Friendly)
            if (kind == 1 && domain == 2 && country == 0 && category == 50) return "SFGPUCS----K---"; // Sensor (Friendly)
            return "SFGPUCI----K---"; // Default friendly ground unit
        }

        // Map DIS country code to CoT country code
        private string MapCountryCode(ushort country)  // Changed to ushort for full country code
        {
            switch (country)
            {
                case 225: return "USA";
                case 224: return "UK";  // Britain/UK
                case 13: return "AUS";  // Australia
                case 71: return "FRA";  // France
                case 38: return "CAN";  // Canada
                case 222: return "RUS";  // Russia
                case 45: return "CHN";  // China
                case 97: return "IRN";  // Iran
                case 115: return "PRK";  // North Korea (assuming DPRK)
                case 0: return "Unknown";
                default: return "Other";
            }
        }

        // Map DIS entity type to platform type
        private string GetPlatformType(byte kind, byte category)
        {
            if (kind == 1 && category == 11) return "Tank";
            if (kind == 1 && category == 2) return "Truck";
            if (kind == 1 && category == 50) return "Sensor";
            return "Unknown";
        }

        // Helper methods for big-endian conversion
        private ushort ToUInt16BigEndian(byte[] buffer, int offset)
        {
            if (BitConverter.IsLittleEndian)
            {
                return BitConverter.ToUInt16(new[] { buffer[offset + 1], buffer[offset] }, 0);
            }
            return BitConverter.ToUInt16(buffer, offset);
        }

        private float ToFloatBigEndian(byte[] buffer, int offset)
        {
            if (BitConverter.IsLittleEndian)
            {
                return BitConverter.ToSingle(new[]
                {
                    buffer[offset + 3],
                    buffer[offset + 2],
                    buffer[offset + 1],
                    buffer[offset]
                }, 0);
            }
            return BitConverter.ToSingle(buffer, offset);
        }

        private double ToDoubleBigEndian(byte[] buffer, int offset)
        {
            if (BitConverter.IsLittleEndian)
            {
                return BitConverter.ToDouble(new[]
                {
                    buffer[offset + 7],
                    buffer[offset + 6],
                    buffer[offset + 5],
                    buffer[offset + 4],
                    buffer[offset + 3],
                    buffer[offset + 2],
                    buffer[offset + 1],
                    buffer[offset]
                }, 0);
            }
            return BitConverter.ToDouble(buffer, offset);
        }
    }

    public static class IpAddressExtensions
    {
        public static bool IsMulticast(this IPAddress ip)
        {
            byte first = ip.GetAddressBytes()[0];
            return first >= 224 && first <= 239;
        }
    }
}