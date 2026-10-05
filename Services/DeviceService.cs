using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace WinNotch.Services
{
    public enum DeviceKind { Headphones, Mouse, Keyboard, Gamepad, Phone, Monitor, Drive, Camera, Printer, Bluetooth, Usb }

    public sealed class ConnectedDevice
    {
        public DeviceKind Kind;
        public string Name;
        public string Detail;          // "Bluetooth", "USB · 41 GB liberi", "2560×1440 · 144 Hz"
        public string DriveRoot;       // for removable drives: "E:\"
    }

    /// <summary>Bluetooth devices that are connected, USB peripherals, monitors and removable drives.</summary>
    public static class DeviceService
    {
        public static async Task<List<ConnectedDevice>> ScanAsync()
        {
            var list = new List<ConnectedDevice>();

            // Bluetooth (classic + Low Energy) that are connected right now
            try
            {
                var seen = new HashSet<string>();
                foreach (var sel in new[]
                {
                    BluetoothDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected),
                    BluetoothLEDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected)
                })
                {
                    var found = await DeviceInformation.FindAllAsync(sel);
                    foreach (var d in found)
                    {
                        if (string.IsNullOrWhiteSpace(d.Name) || !seen.Add(d.Name)) continue;
                        list.Add(new ConnectedDevice { Kind = Guess(d.Name, DeviceKind.Bluetooth), Name = d.Name, Detail = "Bluetooth" });
                    }
                }
            }
            catch (Exception ex) { App.Log("Bluetooth: " + ex.Message); }

            // USB peripherals
            await Task.Run(() =>
            {
                try
                {
                    using var q = new ManagementObjectSearcher("SELECT Name, PNPClass, PNPDeviceID FROM Win32_PnPEntity WHERE PNPDeviceID LIKE 'USB%' AND Present = TRUE");
                    var names = new HashSet<string>();
                    using var coll = q.Get();                 // WMI objects hold COM resources: dispose them
                    foreach (ManagementObject o in coll)
                    {
                        using var owned = o;
                        string cls = (o["PNPClass"] as string) ?? "";
                        string name = (o["Name"] as string) ?? "";
                        DeviceKind kind;
                        switch (cls)
                        {
                            case "Keyboard": kind = DeviceKind.Keyboard; break;
                            case "Mouse": kind = DeviceKind.Mouse; break;
                            case "Camera": case "Image": kind = DeviceKind.Camera; break;
                            case "Printer": kind = DeviceKind.Printer; break;
                            case "WPD": kind = DeviceKind.Phone; break;
                            case "MEDIA": kind = DeviceKind.Headphones; break;
                            default: continue;
                        }
                        // Generic driver names say nothing; replace them with a readable label.
                        if (name.StartsWith("HID Keyboard", StringComparison.OrdinalIgnoreCase)) name = "Tastatură USB";
                        else if (name.StartsWith("HID-compliant mouse", StringComparison.OrdinalIgnoreCase)) name = "Mouse USB";
                        else if (name.StartsWith("USB Audio", StringComparison.OrdinalIgnoreCase)) name = "Dispozitiv audio USB";
                        if (!names.Add(name)) continue;
                        list.Add(new ConnectedDevice { Kind = kind, Name = name, Detail = "USB" });
                    }
                }
                catch (Exception ex) { App.Log("USB: " + ex.Message); }
            });

            // Monitor names (WMI) and drives (a sleeping USB stick can take seconds) are read off the UI thread.
            await Task.Run(() =>
            {
                // Monitors
                try
                {
                    var friendly = MonitorNames();
                    var screens = System.Windows.Forms.Screen.AllScreens;
                    for (int i = 0; i < screens.Length; i++)
                    {
                        var s = screens[i];
                        var dm = new Native.DEVMODE { dmSize = (short)System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.DEVMODE)) };
                        int hz = Native.EnumDisplaySettings(s.DeviceName, -1, ref dm) ? dm.dmDisplayFrequency : 0;
                        string name = friendly.Count == screens.Length && !string.IsNullOrWhiteSpace(friendly[i]) ? friendly[i] : "Monitor " + (i + 1);
                        string detail = (s.Primary ? "Principal" : "Monitor " + (i + 1)) + " · " + dm.dmPelsWidth + "×" + dm.dmPelsHeight + (hz > 1 ? " · " + hz + " Hz" : "");
                        if (dm.dmPelsWidth == 0) detail = (s.Primary ? "Principal" : "Monitor " + (i + 1)) + " · " + s.Bounds.Width + "×" + s.Bounds.Height;
                        list.Add(new ConnectedDevice { Kind = DeviceKind.Monitor, Name = name, Detail = detail });
                    }
                }
                catch (Exception ex) { App.Log("Monitoare (dispozitive): " + ex.Message); }

                // Removable drives
                try
                {
                    foreach (var d in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Removable && d.IsReady))
                    {
                        string label = string.IsNullOrWhiteSpace(d.VolumeLabel) ? "Stick USB" : d.VolumeLabel;
                        list.Add(new ConnectedDevice
                        {
                            Kind = DeviceKind.Drive,
                            Name = label + " (" + d.Name.TrimEnd('\\') + ")",
                            Detail = "USB · " + Gb(d.AvailableFreeSpace) + " liberi din " + Gb(d.TotalSize),
                            DriveRoot = d.Name
                        });
                    }
                }
                catch (Exception ex) { App.Log("Unități: " + ex.Message); }
            });

            return list;
        }

        private static string Gb(long b) => b >= 1L << 40 ? (b / (double)(1L << 40)).ToString("0.#") + " TB" : Math.Round(b / (double)(1L << 30)) + " GB";

        private static List<string> MonitorNames()
        {
            var names = new List<string>();
            try
            {
                using var q = new ManagementObjectSearcher(@"root\wmi", "SELECT UserFriendlyName FROM WmiMonitorID");
                using var coll = q.Get();
                foreach (ManagementObject o in coll)
                {
                    using var owned = o;
                    if (o["UserFriendlyName"] is ushort[] chars)
                        names.Add(new string(chars.TakeWhile(c => c != 0).Select(c => (char)c).ToArray()).Trim());
                    else names.Add("");
                }
            }
            catch { }
            return names;
        }

        private static DeviceKind Guess(string name, DeviceKind fallback)
        {
            string n = name.ToLowerInvariant();
            if (n.Contains("buds") || n.Contains("airpods") || n.Contains("headphone") || n.Contains("headset") || n.Contains("wh-") || n.Contains("wf-") || n.Contains("jbl") || n.Contains("bose") || n.Contains("căști")) return DeviceKind.Headphones;
            if (n.Contains("mouse") || n.Contains("mx master") || n.Contains("mx anywhere")) return DeviceKind.Mouse;
            if (n.Contains("keyboard") || n.Contains("keys") || n.Contains("keychron") || n.Contains("tastatur")) return DeviceKind.Keyboard;
            if (n.Contains("controller") || n.Contains("xbox") || n.Contains("dualsense") || n.Contains("gamepad")) return DeviceKind.Gamepad;
            if (n.Contains("phone") || n.Contains("galaxy") || n.Contains("iphone") || n.Contains("pixel") || n.Contains("redmi")) return DeviceKind.Phone;
            return fallback;
        }

        /// <summary>Safely removes a USB drive (same as "Eject" in Explorer). Must run on the UI thread.</summary>
        public static bool Eject(string root)
        {
            try
            {
                var t = Type.GetTypeFromProgID("Shell.Application");
                dynamic shell = Activator.CreateInstance(t);
                dynamic folder = shell.Namespace(17);          // "This PC"
                dynamic item = folder.ParseName(root);
                item.InvokeVerb("Eject");
                return true;
            }
            catch (Exception ex) { App.Log("Scoatere unitate: " + ex.Message); return false; }
        }
    }
}
