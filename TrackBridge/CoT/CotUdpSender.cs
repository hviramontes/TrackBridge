using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace TrackBridge.CoT
{
    /// <summary>
    /// Sends Cursor on Target (CoT) messages over UDP, with a mutable target,
    /// and records every message to a history log.
    /// </summary>
    public class CotUdpSender
    {
        private const string HistoryFileName = "cot_history.log";

        private UdpClient udpClient;
        private string targetIp;
        private int targetPort;

        public CotUdpSender(string ip, int port)
        {
            udpClient = new UdpClient();
            targetIp = ip;
            targetPort = port;
        }

        /// <summary>
        /// Sends the given CoT XML to the current target, and appends it to cot_history.log.
        /// </summary>
        public void Send(string cotXml)
        {
            if (string.IsNullOrEmpty(cotXml))
                return;

            try
            {
                // Send over UDP
                byte[] data = Encoding.UTF8.GetBytes(cotXml);
                udpClient.Send(data, data.Length, targetIp, targetPort);

                // Append to history log with ISO timestamp
                var entry = $"{DateTime.UtcNow:O}\n{cotXml}\n\n";
                File.AppendAllText(HistoryFileName, entry);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"CoT send error: {ex.Message}");
            }
        }

        /// <summary>
        /// Change the destination IP and port for subsequent sends.
        /// </summary>
        public void SetTarget(string newIp, int newPort)
        {
            targetIp = newIp;
            targetPort = newPort;
        }

        /// <summary>
        /// Clean up the UDP client.
        /// </summary>
        public void Close()
        {
            udpClient?.Dispose();
        }

        public void SendCot(string cotXml)
        {
            try
            {
                byte[] data = Encoding.UTF8.GetBytes(cotXml);
                udpClient.Send(data, data.Length, targetIp, targetPort);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to send CoT heartbeat: {ex.Message}");
            }
        }


    }
}
