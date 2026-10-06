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
        private readonly bool _watchDisplay;
        private readonly Func<int> _abrupt;
        /// <summary>
        /// When Windows last reported a display change (UTC ticks, 0 = none pending): the library's GPU handles may be
        /// dead until it has been reopened (P51c). Written by the SystemEvents thread, read by the refresh.
        /// </summary>
        private long _displayChangedTicks;
        private bool _watching;
        /// <summary>The "stopped reading sensors" line goes in the log once per run, not on every ApplySettings.</summary>
        private bool _blockedLogged;

        /// <param name="watchDisplay">
        /// Reopen the library when Windows reports a display change. True in the app; false in the SYSTEM helper, which
        /// has no desktop session to hear it and is a separate process anyway.
        /// </param>
        /// <param name="abruptCount">
        /// How many runs ended abruptly in a row (<see cref="Core.Update.StartupGuard"/>). From the second one the
        /// library is not touched at all: see <see cref="Core.Diagnostics.SensorGuard"/>.
        /// </param>
        public TempService(bool watchDisplay = false, Func<int> abruptCount = null)
        {
            _watchDisplay = watchDisplay;
            _abrupt = abruptCount ?? (() => 0);
        }

        public float? Cpu { get; private set; }
        public float? Gpu { get; private set; }
        public float? Ssd { get; private set; }
        public float? GpuLoad { get; private set; }
        public bool Started { get; private set; }

        /// <summary>
        /// P51c: the in-process reading was given up after repeated abrupt closures (the UI says so instead of showing an
        /// unexplained "—"). Temperatures from the SYSTEM helper, a separate process, are not affected.
        /// </summary>
        public bool BlockedBySafety => !Core.Diagnostics.SensorGuard.AllowInProcess(_abrupt());

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
            if (!Core.Diagnostics.SensorGuard.AllowInProcess(_abrupt()))
            {
                // P51c: the app went down abruptly twice in a row and this is the one place that can take it down
                // without a catchable exception. Started stays false on purpose, so ticking "Temperaturi" in Settings
                // again (which clears the count) opens the library through the next ApplySettings, with no restart.
                if (!_blockedLogged) { _blockedLogged = true; App.Log("Temperaturi: " + Core.Diagnostics.SensorGuard.BlockedReason + "."); }
                return;
            }
            Started = true;
            if (_watchDisplay && !_watching)
            {
                Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
                _watching = true;
            }
            Task.Run(() => Open());
        }

        /// <summary>Opens the library on a background thread; a failure leaves <see cref="_pc"/> null and is logged.</summary>
        private void Open()
        {
            try
            {
                var pc = new Computer { IsCpuEnabled = true, IsGpuEnabled = true, IsStorageEnabled = true };
                pc.Open();
                if (_disposed) { pc.Close(); return; }       // app closed while the driver was starting
                _pc = pc;
            }
            catch (Exception ex) { App.Log("Temperaturi indisponibile: " + ex.Message); }
        }

        private void OnDisplaySettingsChanged(object sender, EventArgs e) =>
            Interlocked.Exchange(ref _displayChangedTicks, DateTime.UtcNow.Ticks);

        private DateTime? DisplayChangedAt()
        {
            long t = Interlocked.Read(ref _displayChangedTicks);
            return t == 0 ? (DateTime?)null : new DateTime(t, DateTimeKind.Utc);
        }

        /// <summary>
        /// P51c: after a display change the graphics driver re-initialises and the GPU handles the library kept go dead;
        /// using one reads protected memory and the process dies with no exception anyone can catch. So the library is
        /// closed and opened again, and nothing is read this time round. Called under <see cref="_busy"/>, so no refresh
        /// is in flight.
        /// </summary>
        /// <param name="observed">
        /// The change this reopen answers. Cleared only if no newer one arrived meanwhile, so a display change during
        /// the reopen (the driver was still settling) is answered by another reopen instead of being swallowed.
        /// </param>
        private void ReopenAfterDisplayChange(DateTime observed)
        {
            var old = _pc;
            _pc = null;
            try { old?.Close(); } catch (Exception ex) { App.Log("Temperaturi, închiderea bibliotecii: " + ex.GetType().Name); }
            if (_disposed) return;
            App.Log("Temperaturi: monitoarele s-au schimbat, redeschid citirea senzorilor.");
            Open();
            Interlocked.CompareExchange(ref _displayChangedTicks, 0, observed.Ticks);
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
                    // P51c: everything below this line calls into LibreHardwareMonitor, which calls into the graphics
                    // driver. Ask first whether we are still allowed to, and whether the handles need renewing.
                    var changedAt = DisplayChangedAt();
                    switch (Core.Diagnostics.SensorGuard.Next(_abrupt(), changedAt, DateTime.UtcNow))
                    {
                        case Core.Diagnostics.SensorStep.Blocked:
                        case Core.Diagnostics.SensorStep.Wait: return;
                        case Core.Diagnostics.SensorStep.Reopen: ReopenAfterDisplayChange(changedAt.Value); return;
                    }
                    if (_pc == null) return;
                    float? cpu = null, gpu = null, ssd = null, gpuLoad = null;
                    foreach (var hw in _pc.Hardware)
                    {
                        // One pass takes hundreds of milliseconds; a display change arriving inside it would make the
                        // very next hw.Update() the one that reads a dead handle, so the pass is abandoned at once.
                        if (DisplayChangedAt() != null) return;
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
            if (_watching)
            {
                _watching = false;
                try { Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged; } catch { }
            }
            // Don't close the driver while a refresh is reading it.
            SpinWait.SpinUntil(() => Volatile.Read(ref _busy) == 0, 1000);
            try { _pc?.Close(); } catch { }
        }
    }
}
