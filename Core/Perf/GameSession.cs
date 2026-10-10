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
    /// </summary>
    public sealed class GameSession
    {
        /// <summary>Background names tracked for the blame list. Past this, new names are ignored.</summary>
        public const int MaxNames = 200;
        /// <summary>A background program must have averaged at least this much to be named.</summary>
        public const double BlameMinPercent = 3;

        private readonly string _game;
        private readonly DateTime _startUtc;

        private int _samples;
        private double _cpuSum, _cpuMax, _gpuSum, _gpuMax, _ramSum, _ramPeak, _vramPeak = -1;
        private int _gpuSamples;
        private double _cpuTempMax = -1, _gpuTempMax = -1;
        private readonly List<double> _frameTimesMs = new List<double>();
        private readonly Dictionary<string, (double Cpu, double Gpu, int N)> _others =
            new Dictionary<string, (double, double, int)>(StringComparer.Ordinal);

        public GameSession(string gameProcess, DateTime startUtc)
        {
            _game = RawProc.Normalize(gameProcess);
            _startUtc = startUtc;
        }

        public string Game => _game;
        public DateTime StartedUtc => _startUtc;
        public int Samples => _samples;

        /// <summary>
        /// One sample. Temperatures come from the caller (the app reads them through the existing sensor service, or
        /// the SYSTEM helper) — this class never reads a sensor, which is the rule that came out of P51c.
        /// </summary>
        public void Add(PerfSample sample, double? cpuTempC = null, double? gpuTempC = null)
        {
            if (sample == null) return;
            _samples++;

            _cpuSum += sample.CpuPercent;
            if (sample.CpuPercent > _cpuMax) _cpuMax = sample.CpuPercent;

            if (sample.HasGpu)
            {
                // The 3D engine is the game's load; the busiest engine is the fallback when 3D is not published.
                double gpu = sample.Gpu3dPercent >= 0 ? Math.Max(sample.Gpu3dPercent, sample.GpuPercent) : sample.GpuPercent;
                _gpuSum += gpu;
                _gpuSamples++;
                if (gpu > _gpuMax) _gpuMax = gpu;
            }
            if (sample.VramUsedMb > _vramPeak) _vramPeak = sample.VramUsedMb;

            _ramSum += sample.RamUsedGb;
            if (sample.RamUsedGb > _ramPeak) _ramPeak = sample.RamUsedGb;

            if (cpuTempC is double ct && ct > _cpuTempMax) _cpuTempMax = ct;
            if (gpuTempC is double gt && gt > _gpuTempMax) _gpuTempMax = gt;

            Remember(sample);
        }

        /// <summary>Frame times, in milliseconds, as they arrive (P62). Bounded: the oldest are dropped past an hour.</summary>
        public void AddFrames(IEnumerable<double> frameTimesMs)
        {
            if (frameTimesMs == null) return;
            foreach (double ms in frameTimesMs)
            {
                if (ms <= 0 || double.IsNaN(ms) || double.IsInfinity(ms)) continue;
                _frameTimesMs.Add(ms);
            }
            // An hour at 240 fps is ~864k frames; past that the oldest go, so a long evening stays bounded.
            const int max = 1_000_000;
            if (_frameTimesMs.Count > max) _frameTimesMs.RemoveRange(0, _frameTimesMs.Count - max);
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
            var frames = FrameStats.From(_frameTimesMs);
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
                AvgFps = frames.Frames > 0 ? frames.AvgFps : 0,
                P1LowFps = frames.HasP1 ? frames.P1Low : 0,
                Stutters = frames.Stutters,
                Blame = Blame(),
            };
        }

        private IReadOnlyList<GameBlame> Blame()
        {
            if (_samples == 0) return Array.Empty<GameBlame>();
            return _others
                .Select(kv => new GameBlame(kv.Key, kv.Value.Cpu / _samples, kv.Value.Gpu / _samples))
                .Where(b => b.CpuPercent >= BlameMinPercent || b.GpuPercent >= BlameMinPercent)
                .OrderByDescending(b => Math.Max(b.CpuPercent, b.GpuPercent))
                .ThenBy(b => b.Name, StringComparer.Ordinal)
                .ToList();
        }
    }
}
