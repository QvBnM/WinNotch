using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace WinNotch.Services
{
    public sealed class ProcInfo
    {
        public string Name;
        public double Cpu;      // percent of the whole CPU
        public long RamBytes;
    }

    /// <summary>Which apps use the most CPU and memory right now (grouped by app, e.g. all Chrome processes together).</summary>
    public sealed class ProcessMonitor
    {
        private Dictionary<int, TimeSpan> _lastCpu = new Dictionary<int, TimeSpan>();
        private DateTime _lastAt;
        private int _busy;
        public List<ProcInfo> Top { get; private set; } = new List<ProcInfo>();
        /// <summary>The apps holding the most memory (all their processes together), biggest first.</summary>
        public List<ProcInfo> TopRam { get; private set; } = new List<ProcInfo>();

        public void RefreshAsync()
        {
            if (Interlocked.Exchange(ref _busy, 1) == 1) return;
            Task.Run(() =>
            {
                try
                {
                    var now = DateTime.UtcNow;
                    double elapsed = _lastAt == default ? 0 : (now - _lastAt).TotalMilliseconds;
                    var cpuNow = new Dictionary<int, TimeSpan>();
                    var groups = new Dictionary<string, ProcInfo>();
                    foreach (var p in Process.GetProcesses())
                    {
                        try
                        {
                            if (p.Id == 0 || p.ProcessName == "Idle" || p.ProcessName == "System") continue;
                            string name = p.ProcessName;
                            if (name.Equals("WinNotch", StringComparison.OrdinalIgnoreCase)) continue;
                            if (!groups.TryGetValue(name, out var g)) groups[name] = g = new ProcInfo { Name = AudioSessionsService.Friendly(name.ToLowerInvariant()) };
                            g.RamBytes += p.WorkingSet64;
                            var t = p.TotalProcessorTime;
                            cpuNow[p.Id] = t;
                            if (elapsed > 0 && _lastCpu.TryGetValue(p.Id, out var prev))
                                g.Cpu += (t - prev).TotalMilliseconds / elapsed / Environment.ProcessorCount * 100;
                        }
                        catch { /* access denied for some system processes */ }
                        finally { p.Dispose(); }
                    }
                    TopRam = groups.Values.Where(g => g.Name != "WinNotch").OrderByDescending(g => g.RamBytes).Take(5).ToList();
                    _lastCpu = cpuNow;
                    _lastAt = now;
                    if (elapsed > 0)
                        Top = groups.Values.Where(g => g.Name != "WinNotch")
                                    .OrderByDescending(g => g.Cpu + g.RamBytes / 1e9)    // CPU first, memory breaks ties
                                    .Take(4).ToList();
                }
                catch (Exception ex) { App.Log("Procese: " + ex.Message); }
                finally { Interlocked.Exchange(ref _busy, 0); }
            });
        }
    }
}
