using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;

namespace WinNotch.Core.Diagnostics
{
    /// <summary>
    /// Every 6 hours one line in log.txt: WinNotch's own memory, its average CPU since the last line and the number of
    /// errors caught per feature. Nothing personal: no titles, paths, addresses or error messages.
    /// </summary>
    public static class HealthLog
    {
        public static readonly TimeSpan Interval = TimeSpan.FromHours(6);

        private static readonly ConcurrentDictionary<string, int> Errors = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);
        private static Timer _timer;
        private static Action<string> _log;
        private static TimeSpan _lastCpu;
        private static DateTime _lastAt;

        /// <summary>One more error caught in a feature (by <see cref="Flags.FeatureFlags.ReportError"/>).</summary>
        public static void CountError(string featureId) => Errors.AddOrUpdate(featureId, 1, (_, n) => n + 1);

        public static void Start(Action<string> log)
        {
            if (_timer != null) return;
            _log = log;
            (_lastCpu, _lastAt) = (CpuTime(), DateTime.UtcNow);
            _timer = new Timer(_ => WriteNow(), null, Interval, Interval);
        }

        public static void Stop()
        {
            _timer?.Dispose();
            _timer = null;
        }

        /// <summary>Writes the summary and starts counting again (also used by the timer).</summary>
        public static string WriteNow()
        {
            try
            {
                var cpu = CpuTime();
                var at = DateTime.UtcNow;
                double cpuPct = Percent(cpu - _lastCpu, at - _lastAt, Environment.ProcessorCount);
                (_lastCpu, _lastAt) = (cpu, at);
                long ramMb;
                using (var p = Process.GetCurrentProcess()) ramMb = p.PrivateMemorySize64 / (1024 * 1024);
                string line = Line(ramMb, cpuPct, TakeErrors());
                _log?.Invoke(line);
                return line;
            }
            catch (Exception ex) { _log?.Invoke("Rezumat de sănătate: " + ex.GetType().Name); return null; }
        }

        /// <summary>The errors counted so far (feature id → count), emptied.</summary>
        public static (string Id, int Count)[] TakeErrors()
        {
            var taken = new System.Collections.Generic.List<(string, int)>();
            foreach (var id in Errors.Keys.ToArray())
                if (Errors.TryRemove(id, out int n) && n > 0) taken.Add((id, n));
            return taken.OrderBy(t => t.Item1, StringComparer.Ordinal).ToArray();
        }

        /// <summary>Average CPU of this process over a period, as % of the whole machine.</summary>
        public static double Percent(TimeSpan cpu, TimeSpan wall, int cores) =>
            wall <= TimeSpan.Zero || cores <= 0 ? 0 : Math.Clamp(cpu.TotalMilliseconds / (wall.TotalMilliseconds * cores) * 100, 0, 100);

        public static string Line(long ramMb, double cpuPct, (string Id, int Count)[] errors)
        {
            string errs = errors == null || errors.Length == 0 ? "fără erori" : string.Join(", ", errors.Select(e => e.Id + "=" + e.Count));
            return "Sănătate (6 h): RAM " + ramMb + " MB · CPU mediu " + cpuPct.ToString("0.0", CultureInfo.InvariantCulture) + "% · erori pe funcții: " + errs;
        }

        private static TimeSpan CpuTime()
        {
            try { using var p = Process.GetCurrentProcess(); return p.TotalProcessorTime; } catch { return TimeSpan.Zero; }
        }
    }
}
