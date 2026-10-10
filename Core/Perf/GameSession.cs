using System;
using System.Collections.Generic;
using System.Linq;

namespace WinNotch.Core.Perf
{
    /// <summary>
    /// P61: one game session while it is happening. Takes the samples the monitor already produces (it does not
    /// measure anything itself) and turns them into a <see cref="GameReport"/> at the end. Pure: no Windows, no WPF,
    /// no clock — the caller passes every time, so a two-hour session is tested instantly.
    /// <para>Bounded by design: whatever the session's length, this holds a handful of running totals plus one entry
    /// per background process name (at most <see cref="MaxNames"/>). Nothing grows with the number of samples, so a
    /// six-hour evening costs exactly what a five-minute match costs.</para>
    /// <para>Thread-safe, because it has to be: the samples arrive on <see cref="PerfMonitor"/>'s timer thread while
    /// the session can be closed from the context thread or the UI thread. Without the lock, <see cref="Report"/>
    /// could enumerate the background totals while <see cref="Add"/> is writing them — which throws, loses the report,
    /// and counts as an error against the feature.</para>
    /// </summary>
    public sealed class GameSession
    {
        /// <summary>Background names tracked for the blame list. Past this, new names are ignored.</summary>
        public const int MaxNames = 200;
        /// <summary>A background program must have averaged at least this much to be named.</summary>
        public const double BlameMinPercent = 3;

        private readonly object _gate = new object();
        private readonly DateTime _startUtc;

        private string _game;
        private int _samples;
        /// <summary>
        /// Samples that carried a process list. The expensive pass runs every
        /// <see cref="PerfRules.ProcessSeconds"/>, so this is a quarter of <see cref="_samples"/> during a game — and
        /// it, not the total, is the right denominator for the background averages.
        /// </summary>
        private int _procSamples;
        private double _cpuSum, _cpuMax, _gpuSum, _gpuMax, _ramSum, _ramPeak, _vramPeak = -1;
        private int _gpuSamples;
        private double _cpuTempMax = -1, _gpuTempMax = -1;
        private readonly Dictionary<string, (double Cpu, double Gpu, int N)> _others =
            new Dictionary<string, (double, double, int)>(StringComparer.Ordinal);

        public GameSession(string gameProcess, DateTime startUtc)
        {
            _game = RawProc.Normalize(gameProcess);
            _startUtc = startUtc;
        }

        public string Game { get { lock (_gate) return _game; } }
        public DateTime StartedUtc => _startUtc;
        public int Samples { get { lock (_gate) return _samples; } }

        /// <summary>
        /// The game's name, once it is known. An exclusive-fullscreen game is sometimes reported by Windows before its
        /// process name can be read, and a session that never learned its name would be saved as "" — which breaks the
        /// comparison the whole report file exists for, and makes <see cref="Remember"/> fail to recognise the game and
        /// list it under "what was stealing your machine". Named only once: a second game is a second session.
        /// </summary>
        public void Rename(string gameProcess)
        {
            string name = RawProc.Normalize(gameProcess);
            if (name.Length == 0) return;
            lock (_gate)
            {
                if (_game.Length > 0) return;
                _game = name;
                // Anything already blamed under this name was the game itself, mistaken for background noise.
                _others.Remove(name);
            }
        }

        /// <summary>
        /// One sample. Temperatures come from the caller (the app reads them through the existing sensor service, or
        /// the SYSTEM helper) — this class never reads a sensor, which is the rule that came out of P51c.
        /// </summary>
        public void Add(PerfSample sample, double? cpuTempC = null, double? gpuTempC = null)
        {
            if (sample == null) return;
            lock (_gate) AddLocked(sample, cpuTempC, gpuTempC);
        }

        private void AddLocked(PerfSample sample, double? cpuTempC, double? gpuTempC)
        {
            _samples++;

            _cpuSum += sample.CpuPercent;
            if (sample.CpuPercent > _cpuMax) _cpuMax = sample.CpuPercent;

            if (sample.HasGpu)
            {
                // The 3D engine is the game's load, and it is what the report must show. The busiest engine is only a
                // fallback: it is the maximum over every engine type, so it can be a video encoder (a recording in the
                // background) rather than the game — taking the larger of the two, as this did at first, meant the 3D
                // figure was never actually used.
                double gpu = sample.Gpu3dPercent >= 0 ? sample.Gpu3dPercent : sample.GpuPercent;
                _gpuSum += gpu;
                _gpuSamples++;
                if (gpu > _gpuMax) _gpuMax = gpu;
            }
            if (sample.VramUsedMb > _vramPeak) _vramPeak = sample.VramUsedMb;

            _ramSum += sample.RamUsedGb;
            if (sample.RamUsedGb > _ramPeak) _ramPeak = sample.RamUsedGb;

            if (cpuTempC is double ct && ct > _cpuTempMax) _cpuTempMax = ct;
            if (gpuTempC is double gt && gt > _gpuTempMax) _gpuTempMax = gt;

            if (sample.HasProcesses) { _procSamples++; Remember(sample); }
        }

        /// <summary>
        /// Running totals for the background processes, so the report can say who was eating the machine. The game
        /// itself and WinNotch are left out here, not at the end: there is no point averaging our own noise.
        /// </summary>
        private void Remember(PerfSample sample)
        {
            foreach (var u in sample.Processes)
            {
                if (u.Name.Length == 0) continue;
                if (string.Equals(u.Name, _game, StringComparison.Ordinal)) continue;
                if (string.Equals(u.Name, "winnotch", StringComparison.Ordinal)) continue;
                if (u.CpuPercent <= 0 && u.GpuPercent <= 0) continue;
                if (!_others.ContainsKey(u.Name) && _others.Count >= MaxNames) continue;
                _others.TryGetValue(u.Name, out var acc);
                _others[u.Name] = (acc.Cpu + u.CpuPercent, acc.Gpu + u.GpuPercent, acc.N + 1);
            }
        }

        /// <summary>
        /// The report. <paramref name="endUtc"/> is the caller's clock. Averages for the background programs are over
        /// the <em>whole</em> session, not over the samples they appeared in: a downloader that ran for two minutes of
        /// a two-hour evening did not take 40% of the machine, and saying so would be a lie with a number on it.
        /// </summary>
        public GameReport Report(DateTime endUtc)
        {
            lock (_gate) return ReportLocked(endUtc);
        }

        private GameReport ReportLocked(DateTime endUtc)
        {
            return new GameReport
            {
                Process = _game,
                StartedUtc = _startUtc,
                Duration = endUtc > _startUtc ? endUtc - _startUtc : TimeSpan.Zero,
                Samples = _samples,
                AvgCpu = _samples > 0 ? _cpuSum / _samples : 0,
                MaxCpu = _cpuMax,
                AvgGpu = _gpuSamples > 0 ? _gpuSum / _gpuSamples : -1,
                MaxGpu = _gpuSamples > 0 ? _gpuMax : -1,
                MaxCpuTempC = _cpuTempMax,
                MaxGpuTempC = _gpuTempMax,
                AvgRamGb = _samples > 0 ? _ramSum / _samples : 0,
                PeakRamGb = _ramPeak,
                PeakVramMb = _vramPeak,
                // Frames come with P62 (ETW in the SYSTEM helper), which will also decide how to keep them without
                // holding megabytes of raw frame times. Until then these stay zero and HasFps is false, so the UI
                // shows "—" rather than a made-up number.
                Blame = Blame(),
            };
        }

        /// <summary>
        /// Divided by the number of samples that <b>carried a process list</b>, not by every sample. Getting this
        /// wrong is not a rounding error: the expensive pass runs once every four samples during a game, so dividing
        /// by the total made every background average four times too small, pushed everything under
        /// <see cref="BlameMinPercent"/>, and turned the real answer into "nothing was stealing your machine".
        /// </summary>
        private IReadOnlyList<GameBlame> Blame()
        {
            if (_procSamples == 0) return Array.Empty<GameBlame>();
            return _others
                .Select(kv => new GameBlame(kv.Key, kv.Value.Cpu / _procSamples, kv.Value.Gpu / _procSamples))
                .Where(b => b.CpuPercent >= BlameMinPercent || b.GpuPercent >= BlameMinPercent)
                .OrderByDescending(b => Math.Max(b.CpuPercent, b.GpuPercent))
                .ThenBy(b => b.Name, StringComparer.Ordinal)
                .ToList();
        }
    }
}
