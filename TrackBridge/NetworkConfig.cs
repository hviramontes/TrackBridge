// NetworkConfig.cs
namespace TrackBridge
{
    public static class NetworkConfig
    {
        // default values — adjust as you like
        public static string DisIp { get; set; } = "224.0.0.1";
        public static int DisPort { get; set; } = 3000;
        public static string CotIp { get; set; } = "224.0.0.2";
        public static int CotPort { get; set; } = 4242;
    }
}
