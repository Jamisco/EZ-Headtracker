using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EZ_HeadTracker
{
    public class OpenTrackLauncher
    {
        public static string openTrackDir = @"C:\Program Files (x86)\opentrack";
        public static void SetUDPSettings(int port, string host)
        {
            string iniPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "opentrack", "opentrack.ini");

            if (!File.Exists(iniPath))
            {
                Console.WriteLine("❌ OpenTrack config not found at: " + iniPath);
                return;
            }

            var lines = File.ReadAllLines(iniPath).ToList();
            int startIndex = lines.FindIndex(line => line.Trim() == "[tracker-udp]");

            if (startIndex == -1)
            {
                // Add new section at end if it doesn't exist
                lines.Add("");
                lines.Add("[tracker-udp]");
                lines.Add($"port={port}");
                lines.Add($"host={host}");
            }
            else
            {
                // Remove all lines in the [tracker-udp] section until the next section or end of file
                int endIndex = lines.FindIndex(startIndex + 1, line => line.StartsWith("[") && !line.StartsWith("[tracker-udp]"));
                if (endIndex == -1) endIndex = lines.Count;

                lines.RemoveRange(startIndex + 1, endIndex - (startIndex + 1));

                // Insert new clean lines
                lines.InsertRange(startIndex + 1, new[]
                {
            $"port={port}",
            $"host={host}"
        });
            }

            File.WriteAllLines(iniPath, lines);
            Console.WriteLine($"✅ Updated OpenTrack UDP settings: host={host}, port={port}");
        }

        public static bool Launched { get; private set; } = false;

        public static Process OpenTrackProcess { get; private set; } = null;
        public static void LaunchOpenTrack()
        {
            string exePath = Path.Combine(openTrackDir, "opentrack.exe");

            if (!File.Exists(exePath))
                return;

            // Try to find an existing OpenTrack process
            var existingProcesses = Process.GetProcessesByName("opentrack");
            if (existingProcesses.Length > 0)
            {
                OpenTrackProcess = existingProcesses[0];

                // Ensure it has a valid window handle
                if (OpenTrackProcess.MainWindowHandle == IntPtr.Zero)
                {
                    OpenTrackProcess.WaitForInputIdle();
                    while (OpenTrackProcess.MainWindowHandle == IntPtr.Zero)
                    {
                        OpenTrackProcess.Refresh();
                        Thread.Sleep(100);
                    }
                }
                Launched = true;

                return; // ✅ Already running
            }

            // Check if we're already elevated
            bool isAdmin = new WindowsPrincipal(WindowsIdentity.GetCurrent())
                .IsInRole(WindowsBuiltInRole.Administrator);

            if (!isAdmin)
            {
                // Relaunch the app as admin
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    WorkingDirectory = openTrackDir,
                    UseShellExecute = true,
                    Verb = "runas", // 🛡️ triggers UAC
                };

                try
                {
                    OpenTrackProcess = Process.Start(psi);
                }
                catch
                {
                    // Optional: notify user
                }
            }
            else
            {
                // Already admin, launch normally
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    WorkingDirectory = openTrackDir,
                    UseShellExecute = false,
                    CreateNoWindow = false
                };

                OpenTrackProcess = Process.Start(psi);
                OpenTrackProcess.WaitForInputIdle();

                while (OpenTrackProcess.MainWindowHandle == IntPtr.Zero)
                {
                    OpenTrackProcess.Refresh();
                    Thread.Sleep(100);
                }

                Launched = true;
            }
        }

    }
}
