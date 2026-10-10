using System;
using System.Collections.Generic;
using System.Linq;

namespace WinNotch.Core.Perf
{
    /// <summary>Which number to sort by.</summary>
    public enum PerfMetric { Cpu, Gpu, Memory }

    /// <summary>One process as the sampler read it: cumulative CPU time, memory now, nothing computed yet.</summary>
    public readonly struct RawProc
    {
        public RawProc(int pid, string name, double cpuMs, double workingSetMb, double privateMb)
        {
            Pid = pid; Name = Normalize(name); CpuMs = cpuMs; WorkingSetMb = workingSetMb; PrivateMb = privateMb;
        }

        public int Pid { get; }
        /// <summary>Lowercase, without ".exe" — the same shape as <c>ContextSnapshot.ForegroundProcess</c>.</summary>
        public string Name { get; }
        /// <summary>Total CPU time this process has used since it started, in milliseconds.</summary>
        public double CpuMs { get; }
        public double WorkingSetMb { get; }
        public double PrivateMb { get; }

        /// <summary>Process names in one shape everywhere: lowercase, no extension, no path.</summary>
        public static string Normalize(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            string s = name.Trim();
            int slash = s.LastIndexOfAny(new[] { '\\', '/' });
            if (slash >= 0) s = s.Substring(slash + 1);
            if (s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) s = s.Substring(0, s.Length - 4);
            return s.ToLowerInvariant();
        }
    }

    /// <summary>
    /// P60: turning raw readings into the one sentence that matters — "this is what used your machine". Pure, so the
    /// arithmetic that the user will trust can be tested without a machine under load.
    /// <para>Processes are added up <b>by name</b>, not listed one by one. A browser with forty renderer processes at
    /// 2% each is not forty small things, it is one program using 80% of a core, and that is what the user needs to
    /// see. The count of merged processes is kept, so the UI can say "chrome (41)".</para>
    /// </summary>
    public static class ProcessRollup
    {
        /// <summary>
        /// Share of the whole machine from two readings of a process's CPU time. Divided by the number of cores, so
        /// 100% means "the whole CPU", not "one core" — the same scale as Task Manager and as the overall figure.
        /// </summary>
        public static double CpuPercent(double cpuMsDelta, double wallMsDelta, int cores)
        {
            if (wallMsDelta <= 0 || cores <= 0 || cpuMsDelta <= 0) return 0;
            return Math.Clamp(cpuMsDelta / (wallMsDelta * cores) * 100.0, 0, 100);
        }

        /// <summary>
        /// Adds up two passes into per-name usage. <paramref name="before"/> and <paramref name="now"/> are keyed by
        /// pid; a process that was not there before contributes memory but no CPU (its whole lifetime of CPU time
        /// would otherwise land in one interval and read as 100%). <paramref name="gpuByPid"/> and
        /// <paramref name="vramByPid"/> may be empty when the GPU counters are not available.
        /// </summary>
        public static List<ProcUsage> Merge(
            IReadOnlyDictionary<int, RawProc> before,
            IReadOnlyList<RawProc> now,
            double wallMsDelta,
            int cores,
            IReadOnlyDictionary<int, double> gpuByPid = null,
            IReadOnlyDictionary<int, double> vramByPid = null)
        {
            var byName = new Dictionary<string, (double Cpu, double Ws, double Priv, double Gpu, double Vram, int N)>(StringComparer.Ordinal);
            if (now == null) return new List<ProcUsage>();

            foreach (var p in now)
            {
                if (p.Name.Length == 0) continue;
                double cpu = 0;
                if (before != null && before.TryGetValue(p.Pid, out var old) && string.Equals(old.Name, p.Name, StringComparison.Ordinal))
                    cpu = CpuPercent(p.CpuMs - old.CpuMs, wallMsDelta, cores);
                double gpu = gpuByPid != null && gpuByPid.TryGetValue(p.Pid, out var g) ? g : 0;
                double vram = vramByPid != null && vramByPid.TryGetValue(p.Pid, out var v) ? v : 0;

                byName.TryGetValue(p.Name, out var acc);
                byName[p.Name] = (acc.Cpu + cpu, acc.Ws + p.WorkingSetMb, acc.Priv + p.PrivateMb,
                                  acc.Gpu + gpu, acc.Vram + vram, acc.N + 1);
            }

            var list = new List<ProcUsage>(byName.Count);
            foreach (var kv in byName)
                list.Add(new ProcUsage(kv.Key, Math.Min(100, kv.Value.Cpu), kv.Value.Ws, kv.Value.Priv,
                                       Math.Min(100, kv.Value.Gpu), kv.Value.Vram, kv.Value.N));
            return list;
        }

        /// <summary>The <paramref name="n"/> biggest by one metric, biggest first; ties broken by name so the list never jitters.</summary>
        public static List<ProcUsage> Top(IReadOnlyList<ProcUsage> all, int n, PerfMetric by)
        {
            if (all == null || all.Count == 0 || n <= 0) return new List<ProcUsage>();
            Func<ProcUsage, double> key = by switch
            {
                PerfMetric.Gpu => u => u.GpuPercent,
                PerfMetric.Memory => u => u.PrivateMb,
                _ => u => u.CpuPercent,
            };
            return all.Where(u => key(u) > 0)
                      .OrderByDescending(key)
                      .ThenBy(u => u.Name, StringComparer.Ordinal)
                      .Take(n)
                      .ToList();
        }

        /// <summary>
        /// What took resources away from a game: everything that used a real share of the CPU or the GPU while it ran,
        /// except the game itself and WinNotch (blaming ourselves for watching would be a neat trick). Sorted by the
        /// bigger of its two shares, because a 20%-GPU downloader hurts a game more than a 5%-CPU one.
        /// </summary>
        public static List<ProcUsage> Blame(IReadOnlyList<ProcUsage> during, string gameProcess, double minPercent = 3)
        {
            if (during == null) return new List<ProcUsage>();
            string game = RawProc.Normalize(gameProcess);
            return during
                .Where(u => !string.Equals(u.Name, game, StringComparison.Ordinal))
                .Where(u => !string.Equals(u.Name, "winnotch", StringComparison.Ordinal))
                .Where(u => u.CpuPercent >= minPercent || u.GpuPercent >= minPercent)
                .OrderByDescending(u => Math.Max(u.CpuPercent, u.GpuPercent))
                .ThenBy(u => u.Name, StringComparer.Ordinal)
                .ToList();
        }
    }
}
