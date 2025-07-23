using System;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace TrackBridge
{
    public static class CotSender
    {
        public static void Send(string xml, string destIp, int destPort)
        {
            try
            {
                byte[] data = Encoding.UTF8.GetBytes(xml);
                using (UdpClient client = new UdpClient())
                {
                    IPEndPoint endpoint = new IPEndPoint(IPAddress.Parse(destIp), destPort);
                    client.Send(data, data.Length, endpoint);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CoT send error: {ex.Message}");
            }
        }
    }
}
