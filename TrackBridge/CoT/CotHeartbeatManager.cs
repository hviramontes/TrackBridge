using System;
using System.Timers;
using TrackBridge.DIS;

namespace TrackBridge.CoT
{
    public class CotHeartbeatManager
    {
        private readonly CotUdpSender _sender;
        private readonly Timer _timer;
        private string _lastUid = "TrackBridge-Heartbeat";

        public int IntervalSeconds
        {
            get => (int)(_timer.Interval / 1000);
            set => _timer.Interval = value * 1000;
        }

        public CotHeartbeatManager(CotUdpSender sender)
        {
            _sender = sender;
            _timer = new Timer(30000); // default 30 seconds
            _timer.Elapsed += OnHeartbeat;
            _timer.AutoReset = true;
        }

        public void Start()
        {
            _timer.Start();
        }

        public void Stop()
        {
            _timer.Stop();
        }

        private void OnHeartbeat(object sender, ElapsedEventArgs e)
        {
            var now = DateTime.UtcNow;
            var track = new EntityTrack
            {
                Id = _lastUid.GetHashCode(),
                EntityId = _lastUid,
                Lat = 0.0,
                Lon = 0.0,
                Altitude = 0.0,
                PlatformType = "Heartbeat",
                TrackType = "Friendly",
                CustomMarking = "HB",
                Publish = true,
                LastUpdate = now
            };

            var cotXml = CotBuilder.BuildCotXml(track);
            _sender.SendCot(cotXml);
        }
    }
}
