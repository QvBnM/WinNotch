using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LibreHardwareMonitor.Hardware;

namespace WinNotch.Services
{
    /// <summary>
    /// Reads CPU / GPU / SSD temperatures through LibreHardwareMonitor on a background thread.
    /// CPU temperatures need the app to run as administrator; GPU and SSD usually work without it.
    /// </summary>
    public sealed class TempService : IDisposable
    {
        private Computer _pc;
        private int _busy;
        private volatile bool _disposed;
        public float? Cpu { get; private set; }
        public float? Gpu { get; private set; }
        public float? Ssd { get; private set; }
        public float? GpuLoad { get; private set; }
        public bool Started { get; private set; }

        /// <summary>
        /// LibreHardwareMonitor reads CPU temperatures through the PawnIO driver (installed separately, from pawnio.eu).
        /// </summary>
        public static bool PawnIOInstalled
        {
            get
            {
                try
                {
                    using var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\PawnIO");
                    return k != null;
                }
                catch { return false; }
            }
        }

        public void Start()
        {
            if (Started) return;
            Started = true;
            Task.Run(() =>
            {
                try
                {
                    var pc = new Computer { IsCpuEnabled = true, IsGpuEnabled = true, IsStorageEnabled = true };
                    pc.Open();
                    if (_disposed) { pc.Close(); return; }       // app closed while the driver was starting
                    _pc = pc;
                }
                catch (Exception ex) { App.Log("Temperaturi indisponibile: " + ex.Message); }
            });
        }

        /// <summary>Refresh in the background; skipped if the previous refresh hasn't finished.</summary>
        public void RefreshAsync()
        {
            if (_disposed || Interlocked.Exchange(ref _busy, 1) == 1) return;
            if (_pc == null && (App.IsAdmin || !TempHelper.Installed)) { Interlocked.Exchange(ref _busy, 0); return; }
            Task.Run(() =>
            {
                try
                {
                    // with normal rights the CPU sensors come from the SYSTEM helper (GPU / SSD can be read locally too)
                    if (!App.IsAdmin && TempHelper.TryRead(out var hc, out var hg, out var hs, out var hl))
                    {
                        Cpu = hc; Gpu = hg ?? Gpu; Ssd = hs ?? Ssd; GpuLoad = hl ?? GpuLoad;
                        return;
                    }
                    if (_pc == null) return;
                    float? cpu = null, gpu = null, ssd = null, gpuLoad = null;
                    foreach (var hw in _pc.Hardware)
                    {
                        hw.Update();
                        foreach (var sub in hw.SubHardware) sub.Update();
                        var temps = hw.Sensors.Concat(hw.SubHardware.SelectMany(s => s.Sensors))
                            .Where(s => s.SensorType == SensorType.Temperature && s.Value.HasValue && s.Value.Value > 0 && s.Value.Value < 125)
                            .ToList();
                        if (temps.Count == 0) continue;

                        string type = hw.HardwareType.ToString();
                        if (hw.HardwareType == HardwareType.Cpu)
                            cpu = Pick(temps, "Package", "Tctl", "Tdie", "Core Max", "Core Average") ?? cpu;
                        else if (type.StartsWith("Gpu", StringComparison.OrdinalIgnoreCase))
                        {
                            gpu = Max(gpu, Pick(temps, "GPU Core", "Core", "Edge"));
                            var load = hw.Sensors.FirstOrDefault(x => x.SensorType == SensorType.Load && x.Name.IndexOf("Core", StringComparison.OrdinalIgnoreCase) >= 0 && x.Value.HasValue);
                            if (load != null) gpuLoad = Max(gpuLoad, load.Value);
                        }
                        else if (hw.HardwareType == HardwareType.Storage)
                            ssd = Max(ssd, temps.First().Value);
                    }
                    Cpu = cpu; Gpu = gpu; Ssd = ssd; GpuLoad = gpuLoad;
                }
                catch (Exception ex) { App.Log("Citire temperaturi: " + ex.Message); }
                finally { Interlocked.Exchange(ref _busy, 0); }
            });
        }

        private static float? Pick(System.Collections.Generic.List<ISensor> temps, params string[] names)
        {
            foreach (var n in names)
            {
                var s = temps.FirstOrDefault(t => t.Name.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0);
                if (s != null) return s.Value;
            }
            return temps.Max(t => t.Value);
        }

        private static float? Max(float? a, float? b) => a == null ? b : b == null ? a : Math.Max(a.Value, b.Value);

        public void Dispose()
        {
            _disposed = true;
            // Don't close the driver while a refresh is reading it.
            SpinWait.SpinUntil(() => Volatile.Read(ref _busy) == 0, 1000);
            try { _pc?.Close(); } catch { }
        }
    }
}
