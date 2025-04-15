using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using static EZ_HeadTracker.Hardware.HeadTracker;

namespace EZ_HeadTracker
{
    public static class DataBridge
    {
        private static UdpClient udpClient;
        private static readonly string localhost = "127.0.0.1";
        private static readonly int openTrackPort = 4242;

        static DataBridge()
        {
            SetUDPSettings();
        }

        public static void SendData(Point3f r, Point3f t)
        {
            string pitch = r.X.ToString("F2");
            string yaw = r.Y.ToString("F2");
            string roll = r.Z.ToString("F2");

            string x = t.X.ToString("F2");
            string y = t.Y.ToString("F2");
            string z = t.Z.ToString("F2");

            if (udpClient == null)
            {
                udpClient = new UdpClient();
            }

            try
            {
                // Format: "pitch,yaw,roll,tx,ty,tz"
                string data = $"{pitch},{yaw},{roll},{x},{y},{z}";
                byte[] bytes = Encoding.UTF8.GetBytes(data);
                udpClient.Send(bytes, bytes.Length, localhost, openTrackPort);
            }
            catch (Exception e)
            {
                Console.WriteLine($"UDP Send Error: {e.Message}");
            }
        }
        public static void SendData2OpenTrack(Point3f r, Point3f t)
        {
            using (UdpClient client = new UdpClient())
            {
                client.Connect(localhost, openTrackPort);

                // Use double (not float) since OpenTrack requires 64-bit values
                double[] data = { t.X, t.Y, t.Z, r.Y, -r.X, r.Z };
                byte[] bytes = new byte[data.Length * sizeof(double)]; // Ensure correct size: 6 * 8 bytes = 48 bytes

                Buffer.BlockCopy(data, 0, bytes, 0, bytes.Length); // Copy the data correctly

                client.Send(bytes, bytes.Length); // Send the UDP packet

            }
        }

        public static void SendData2OpenTrack(TransformationData data)
        {
            using (UdpClient client = new UdpClient())
            {
                client.Connect(localhost, openTrackPort);

                // Use double (not float) since OpenTrack requires 64-bit values
                double[] transformedData =
                {
                    data.X,
                    data.Y,
                    data.Z,
                    data.Yaw,
                    -data.Pitch,
                    data.Roll
                };
                byte[] bytes = new byte[transformedData.Length * sizeof(double)]; // Ensure correct size: 6 * 8 bytes = 48 bytes

                Buffer.BlockCopy(transformedData, 0, bytes, 0, bytes.Length); // Copy the data correctly

                client.Send(bytes, bytes.Length); // Send the UDP packet
            }
        }

        public static void SetUDPSettings()
        {
            OpenTrackLauncher.SetUDPSettings(openTrackPort, localhost);
        }
    }
}
