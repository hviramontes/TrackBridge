using System;

namespace TrackBridge
{
    /// <summary>
    /// Represents a single DIS entity track, including location, identity, and metadata.
    /// </summary>
    public class EntityTrack
    {
        public string EntityId { get; set; }
        public string PlatformType { get; set; }
        public string TrackType { get; set; }
        public int Id { get; set; }
        public double Lat { get; set; }
        public double Lon { get; set; }
        public double Altitude { get; set; }
        public DateTime LastUpdate { get; set; }
        public int EntityKind { get; set; }
        public int Domain { get; set; }
        public string CustomMarking { get; set; }
        public bool Publish { get; set; }
        public string CountryCode { get; set; }
        public string IconType { get; set; }

        public double Latitude
        {
            get { return Lat; }
            set { Lat = value; }
        }

        public double Longitude
        {
            get { return Lon; }
            set { Lon = value; }
        }

        public double Alt
        {
            get { return Altitude; }
            set { Altitude = value; }
        }

        public DateTime Timestamp
        {
            get { return LastUpdate; }
            set { LastUpdate = value; }
        }

        public EntityTrack()
        {
            CountryCode = string.Empty; // Avoid hardcoding to allow DIS-derived values
            IconType = string.Empty; // Avoid default to allow DIS-derived symbols
            Publish = true; // Default to publishing tracks
        }

        /// <summary>
        /// Computed MGRS grid reference from current Lat/Lon.
        /// Now read/write so we can assign different precisions.
        /// </summary>
        public string Mgrs { get; set; }

        /// <summary>
        /// If true, the CustomMarking is locked and will not be overwritten by new DIS data.
        /// </summary>
        public bool IsCustomMarkingLocked { get; set; } = false;

    }
}