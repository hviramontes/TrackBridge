using System;
using System.Collections.Generic; // Added for EqualityComparer
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TrackBridge
{
    /// <summary>
    /// Represents a single DIS entity track, including location, identity, and metadata.
    /// </summary>
    public class EntityTrack : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(storage, value)) return false;
            storage = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        private string _entityId;
        public string EntityId
        {
            get => _entityId;
            set => SetProperty(ref _entityId, value);
        }

        private string _platformType;
        public string PlatformType
        {
            get => _platformType;
            set => SetProperty(ref _platformType, value);
        }

        private string _trackType;
        public string TrackType
        {
            get => _trackType;
            set => SetProperty(ref _trackType, value);
        }

        private int _id;
        public int Id
        {
            get => _id;
            set => SetProperty(ref _id, value);
        }

        private double _lat;
        public double Lat
        {
            get => _lat;
            set => SetProperty(ref _lat, value);
        }

        private double _lon;
        public double Lon
        {
            get => _lon;
            set => SetProperty(ref _lon, value);
        }

        private double _altitude;
        public double Altitude
        {
            get => _altitude;
            set => SetProperty(ref _altitude, value);
        }

        private DateTime _lastUpdate;
        public DateTime LastUpdate
        {
            get => _lastUpdate;
            set => SetProperty(ref _lastUpdate, value);
        }

        private int _entityKind;
        public int EntityKind
        {
            get => _entityKind;
            set => SetProperty(ref _entityKind, value);
        }

        private int _domain;
        public int Domain
        {
            get => _domain;
            set => SetProperty(ref _domain, value);
        }

        private string _customMarking;
        public string CustomMarking
        {
            get => _customMarking;
            set => SetProperty(ref _customMarking, value);
        }

        private bool _publish;
        public bool Publish
        {
            get => _publish;
            set => SetProperty(ref _publish, value);
        }

        private string _countryCode;
        public string CountryCode
        {
            get => _countryCode;
            set => SetProperty(ref _countryCode, value);
        }

        private string _iconType;
        public string IconType
        {
            get => _iconType;
            set => SetProperty(ref _iconType, value);
        }

        private double _latitude;
        public double Latitude
        {
            get => _latitude;
            set => SetProperty(ref _latitude, value);
        }

        private double _longitude;
        public double Longitude
        {
            get => _longitude;
            set => SetProperty(ref _longitude, value);
        }

        private double _alt;
        public double Alt
        {
            get => _alt;
            set => SetProperty(ref _alt, value);
        }

        private DateTime _timestamp;
        public DateTime Timestamp
        {
            get => _timestamp;
            set => SetProperty(ref _timestamp, value);
        }

        private string _mgrs;
        public string Mgrs
        {
            get => _mgrs;
            set => SetProperty(ref _mgrs, value);
        }

        private bool _isCustomMarkingLocked;
        public bool IsCustomMarkingLocked
        {
            get => _isCustomMarkingLocked;
            set => SetProperty(ref _isCustomMarkingLocked, value);
        }

        public EntityTrack()
        {
            CountryCode = string.Empty;
            IconType = string.Empty;
            Publish = true;
        }
    }
}