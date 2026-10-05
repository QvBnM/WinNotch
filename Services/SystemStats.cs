using System;
using System.Linq;
using System.Net.NetworkInformation;

namespace WinNotch.Services
{
    /// <summary>CPU load, RAM, network speed and battery, sampled once per second.</summary>
    public sealed class SystemStats
    {
        public double Cpu { get; private set; }
        public double RamUsedGb { get; private set; }
        public double RamTotalGb { get; private set; }
        public double NetDownMBps { get; private set; }
        public double NetUpMBps { get; private set; }
        public double NetDownMbps => NetDownMBps * 8.388608;     // megabits, like speed tests
        public double NetUpMbps => NetUpMBps * 8.388608;
        public bool HasBattery { get; private set; }
        public int BatteryPercent { get; private set; } = -1;
        public bool Charging { get; private set; }
        public int BatterySecondsLeft { get; private set; } = -1;

        private long _idle, _kernel, _user;
        private System.Collections.Generic.Dictionary<string, (long Rx, long Tx)> _nic = new System.Collections.Generic.Dictionary<string, (long, long)>();
        private DateTime _netTime;

        public void Sample()
        {
            try
            {
                if (Native.GetSystemTimes(out long idle, out long kernel, out long user))
                {
                    long di = idle - _idle, dk = kernel - _kernel, du = user - _user;
                    long total = dk + du;
                    if (_kernel != 0 && total > 0) Cpu = Math.Clamp((total - di) * 100.0 / total, 0, 100);
                    _idle = idle; _kernel = kernel; _user = user;
                }
            }
            catch { }

            try
            {
                var m = new Native.MEMORYSTATUSEX { dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.MEMORYSTATUSEX)) };
                if (Native.GlobalMemoryStatusEx(ref m))
                {
                    RamTotalGb = m.ullTotalPhys / 1073741824.0;
                    RamUsedGb = (m.ullTotalPhys - m.ullAvailPhys) / 1073741824.0;
                }
            }
            catch { }

            try
            {
                long rx = 0, tx = 0;
                var seen = new System.Collections.Generic.Dictionary<string, (long, long)>();
                foreach (var n in NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                                n.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                                n.NetworkInterfaceType != NetworkInterfaceType.Tunnel))
                {
                    try
                    {
                        var st = n.GetIPStatistics();
                        seen[n.Id] = (st.BytesReceived, st.BytesSent);
                        // Only adapters that were already there last second count: a new one (Wi-Fi reconnect, VPN)
                        // would otherwise add everything it transferred since boot as one second of traffic.
                        if (_nic.TryGetValue(n.Id, out var prev))
                        {
                            rx += Math.Max(0, st.BytesReceived - prev.Rx);
                            tx += Math.Max(0, st.BytesSent - prev.Tx);
                        }
                    }
                    catch { }
                }
                var now = DateTime.UtcNow;
                double sec = (now - _netTime).TotalSeconds;
                if (_nic.Count > 0 && sec > 0.2 && sec < 10)
                {
                    NetDownMBps = rx / sec / 1048576.0;
                    NetUpMBps = tx / sec / 1048576.0;
                }
                _nic = seen;
                _netTime = now;
            }
            catch { }

            try
            {
                if (Native.GetSystemPowerStatus(out var ps))
                {
                    HasBattery = ps.BatteryFlag != 128 && ps.BatteryFlag != 255 && ps.BatteryLifePercent != 255;
                    BatteryPercent = ps.BatteryLifePercent == 255 ? -1 : ps.BatteryLifePercent;
                    Charging = ps.ACLineStatus == 1;
                    BatterySecondsLeft = ps.BatteryLifeTime;
                }
            }
            catch { }
        }
    }
}
