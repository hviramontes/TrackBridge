using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using TrackBridge;            // for EntityTrack and MgrsConverter
using TrackBridge.Utilities; // for EcefConverter

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

            IPAddress ipAddress = IPAddress.Parse(ip);
            udpClient = new UdpClient(port);

            if (ipAddress.IsMulticast())
                udpClient.JoinMulticastGroup(ipAddress);

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
                            EntityReceived?.Invoke(track);
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
            // Minimum PDU size / type / version check
            if (buffer.Length < 48 || buffer[2] != 1 || (buffer[0] != 6 && buffer[0] != 7))
            {
                Console.WriteLine($"Invalid PDU: Length={buffer.Length}, Type={buffer[2]}, Version={buffer[0]}");
                return null;
            }

            try
            {
                // Extract IDs
                int siteId = ToUInt16BigEndian(buffer, 12);
                int appId = ToUInt16BigEndian(buffer, 14);
                int entityId = ToUInt16BigEndian(buffer, 16);
                string entityKey = $"{siteId}:{appId}:{entityId}";

                // Extract ECEF
                double x = ToDoubleBigEndian(buffer, 48);
                double y = ToDoubleBigEndian(buffer, 56);
                double z = ToDoubleBigEndian(buffer, 64);

                // Convert to lat/lon/alt
                EcefConverter.ToLatLonAlt(x, y, z, out double lat, out double lon, out double hae);
                if (hae < -1000.0) hae = 0.0;
                if (double.IsNaN(lat) || double.IsNaN(lon) || double.IsNaN(hae))
                    lat = lon = hae = 0.0;

                // Other DIS fields
                byte forceId = buffer[11];
                byte entityKind = buffer[20];
                byte domain = buffer[21];
                int country = ToUInt16BigEndian(buffer, 22);
                byte category = buffer[24];

                string countryCode = MapCountryCode((ushort)country);

                // Determine track type
                string trackType = forceId == 1 ? "Friendly"
                                   : forceId == 2 ? "Enemy"
                                   : "Unknown";
                if (trackType == "Unknown")
                {
                    switch (countryCode)
                    {
                        case "USA":
                        case "UK":
                        case "AUS":
                        case "FRA":
                        case "CAN":
                            trackType = "Friendly"; break;
                        case "RUS":
                        case "CHN":
                        case "IRN":
                        case "PRK":
                            trackType = "Enemy"; break;
                    }
                }

                // Symbol and platform type
                string iconType = MapEntityTypeToSymbol(entityKind, domain, (byte)country, category);
                string platformType = GetPlatformType(entityKind, category);

                // Build the track
                var track = new EntityTrack
                {
                    EntityId = entityKey,
                    Id = entityKey.GetHashCode(),
                    PlatformType = platformType,
                    TrackType = trackType,
                    Lat = lat,
                    Lon = lon,
                    Altitude = hae,
                    LastUpdate = DateTime.UtcNow,
                    EntityKind = entityKind,
                    Domain = domain,
                    CountryCode = countryCode,
                    IconType = iconType,
                    Publish = true,

                    // ← Milestone 7: populate MGRS
                    Mgrs = MgrsConverter.LatLonToMgrs(lat, lon)
                };

                // Preserve custom markings
                if (trackCache.TryGetValue(entityKey, out var prev) && prev.IsCustomMarkingLocked)
                {
                    track.CustomMarking = prev.CustomMarking;
                    track.IsCustomMarkingLocked = true;
                }

                // Extract DIS marking if unlocked (Entity Marking = 1 byte charset + 11 bytes text)
                // In Entity State PDUs (v6/7), the marking block begins at byte 128 of the PDU.
                const int MARKING_OFFSET = 128;
                const int MARKING_LEN = 11;

                if (!track.IsCustomMarkingLocked && buffer.Length >= MARKING_OFFSET + 1 + MARKING_LEN)
                {
                    byte charset = buffer[MARKING_OFFSET]; // 1 = ASCII per DIS
                    if (charset == 1)
                    {
                        var mb = new byte[MARKING_LEN];
                        Buffer.BlockCopy(buffer, MARKING_OFFSET + 1, mb, 0, MARKING_LEN);

                        string marking = System.Text.Encoding.ASCII
                            .GetString(mb)
                            .TrimEnd('\0', ' ');

                        // must contain at least one letter or digit; otherwise ignore
                        if (!string.IsNullOrWhiteSpace(marking) &&
                            System.Text.RegularExpressions.Regex.IsMatch(marking, @"[A-Za-z0-9]"))
                        {
                            track.CustomMarking = marking;
                        }
                    }
                }


                // Fallback marking
                if (string.IsNullOrWhiteSpace(track.CustomMarking))
                    track.CustomMarking = entityKey;

                // Cache and return
                trackCache[entityKey] = track;
                return track;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing DIS PDU: {ex.Message}");
                return null;
            }
        }

        // Map Entity Type to MIL-STD-2525C symbol
        private string MapEntityTypeToSymbol(byte kind, byte domain, byte country, byte category)
        {
            if (kind == 1 && domain == 2 && country == 225 && category == 11) return "SHGPUCT----K---";
            if (kind == 1 && domain == 2 && country == 225 && category == 2) return "SFGPUCV----K---";
            if (kind == 1 && domain == 2 && country == 0 && category == 50) return "SFGPUCS----K---";
            return "SFGPUCI----K---";
        }

        // Map DIS country code to CoT country code
        private string MapCountryCode(ushort country)
        {
            switch (country)
            {
                case 225: return "USA";
                case 224: return "UK";
                case 13: return "AUS";
                case 71: return "FRA";
                case 38: return "CAN";
                case 222: return "RUS";
                case 45: return "CHN";
                case 97: return "IRN";
                case 115: return "PRK";
                default: return "Other";
            }
        }

        // Map to a simpler platform type
        private string GetPlatformType(byte kind, byte category)
        {
            if (kind == 1 && category == 11) return "Tank";
            if (kind == 1 && category == 2) return "Truck";
            if (kind == 1 && category == 50) return "Sensor";
            return "Unknown";
        }

        // Big-endian conversion helpers
        private ushort ToUInt16BigEndian(byte[] buffer, int offset)
        {
            if (BitConverter.IsLittleEndian)
                return BitConverter.ToUInt16(new[] { buffer[offset + 1], buffer[offset] }, 0);
            return BitConverter.ToUInt16(buffer, offset);
        }

        private float ToFloatBigEndian(byte[] buffer, int offset)
        {
            if (BitConverter.IsLittleEndian)
                return BitConverter.ToSingle(new[]
                {
                    buffer[offset + 3], buffer[offset + 2],
                    buffer[offset + 1], buffer[offset]
                }, 0);
            return BitConverter.ToSingle(buffer, offset);
        }

        private double ToDoubleBigEndian(byte[] buffer, int offset)
        {
            if (BitConverter.IsLittleEndian)
                return BitConverter.ToDouble(new[]
                {
                    buffer[offset + 7], buffer[offset + 6],
                    buffer[offset + 5], buffer[offset + 4],
                    buffer[offset + 3], buffer[offset + 2],
                    buffer[offset + 1], buffer[offset]
                }, 0);
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
