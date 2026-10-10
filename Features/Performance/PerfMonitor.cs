using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using WinNotch.Core.Flags;
using WinNotch.Core.Perf;

namespace WinNotch.Features.Performance
{
    /// <summary>
    /// P60: the one place that measures the machine, and the only one allowed to. Everything that shows a performance
    /// number — the Performanță tab, a widget, a game session, a report — reads from here and never samples on its own,
    /// so there is exactly one timer and exactly one cost.
    /// <para>The timer only exists while somebody is looking (<see cref="AddViewer"/>) or a game is running
    /// (<see cref="SetGame"/>). With nothing on screen and no game, there is no timer at all — not a slow one. See
    /// <see cref="PerfRules"/> for the cadences and why.</para>
    /// <para>Samples are raised on the timer's thread. Anything that draws must come back to the Dispatcher itself.</para>
    /// </summary>
    internal sealed class PerfMonitor : IDisposable
    {
        public const string FeatureId = PerfRules.FeatureId;

        /// <summary>The app's instance; null in the helper modes and in the tests.</summary>
        public static PerfMonitor Current { get; set; }

        private readonly object _gate = new object();
        private readonly FeatureFlags _flags;
        private readonly SampleRing<PerfSample> _history = new SampleRing<PerfSample>(PerfRules.HistoryCapacity);
        /// <summary>Per process name, one point per minute: how much private memory it held. For the leak trend.</summary>
        private readonly Dictionary<string, SampleRing<(double Minutes, double Mb)>> _memory =
            new Dictionary<string, SampleRing<(double, double)>>(StringComparer.Ordinal);
        private readonly Dictionary<string, DateTime> _lastSeen = new Dictionary<string, DateTime>(StringComparer.Ordinal);

        private PerfSample _latest = PerfSample.Empty;
        private PerfSampler _sampler;
        private Timer _timer;
        private PerfCadence _cadence = PerfCadence.Off;
        private int _viewers, _ticks;
        private bool _game, _disposed;
        private DateTime _started;

        /// <summary>One sample, just taken, on the timer's thread.</summary>
        public event Action<PerfSample> Sampled;

        public PerfMonitor(FeatureFlags flags)
        {
            _flags = flags ?? throw new ArgumentNullException(nameof(flags));
            _flags.Changed += OnFlagChanged;
        }

        /// <summary>
        /// The newest sample, with its process list; <see cref="PerfSample.Empty"/> before the first pass. The samples
        /// kept in <see cref="History"/> have the list stripped — see <see cref="Tick"/>.
        /// </summary>
        public PerfSample Last { get { lock (_gate) return _latest ?? PerfSample.Empty; } }

        public PerfCadence Cadence { get { lock (_gate) return _cadence; } }
        public bool Running => Cadence != PerfCadence.Off;

        /// <summary>Oldest first. A copy, so the caller can walk it while the timer keeps adding.</summary>
        public List<PerfSample> History() { lock (_gate) return _history.ToList(); }

        /// <summary>The last <paramref name="minutes"/> of samples, oldest first.</summary>
        public List<PerfSample> Recent(int minutes)
        {
            var cut = DateTime.UtcNow.AddMinutes(-Math.Max(1, minutes));
            lock (_gate) return _history.ToList().Where(s => s.TimeUtc >= cut).ToList();
        }

        // ------------------------------------------------------------------ who needs it running

        /// <summary>Something started showing the numbers. Every call must be paired with <see cref="RemoveViewer"/>.</summary>
        public void AddViewer()
        {
            lock (_gate) _viewers++;
            Apply();
        }

        public void RemoveViewer()
        {
            lock (_gate) _viewers = Math.Max(0, _viewers - 1);
            Apply();
        }

        /// <summary>A game started or stopped (P61): the only reason for a rate under two seconds.</summary>
        public void SetGame(bool running)
        {
            lock (_gate) _game = running;
            Apply();
        }

        private void OnFlagChanged(string id)
        {
            // The direction is never assumed: two changes from two threads can arrive in any order, so the handler
            // reads the switch itself (CLAUDE.md, "Cum declari o funcție nouă").
            if (!string.Equals(id, FeatureId, StringComparison.Ordinal)) return;
            try { Apply(); }
            catch (Exception ex) { _flags.ReportError(FeatureId, ex); }
        }

        /// <summary>Starts, re-times or stops the timer to match the state. The only place that touches it.</summary>
        private void Apply()
        {
            if (_disposed) return;
            bool enabled = _flags.IsEnabled(FeatureId);          // asked outside the lock: it is somebody else's code
            try
            {
                lock (_gate)
                {
                    var want = PerfRules.Pick(enabled, _viewers > 0, _game);
                    if (want == _cadence) return;
                    _cadence = want;
                    int seconds = PerfRules.SecondsFor(want);
                    if (seconds <= 0)
                    {
                        _timer?.Dispose();
                        _timer = null;
                        _sampler?.Dispose();
                        _sampler = null;
                        _history.Clear();
                        _latest = PerfSample.Empty;
                        _memory.Clear();
                        _lastSeen.Clear();
                        _ticks = 0;
                        return;
                    }
                    _sampler ??= new PerfSampler();
                    _started = DateTime.UtcNow;
                    if (_timer == null) _timer = new Timer(Tick, null, 0, seconds * 1000);
                    else _timer.Change(0, seconds * 1000);
                }
            }
            catch (Exception ex) { _flags.ReportError(FeatureId, ex); }
        }

        // ------------------------------------------------------------------ the pass

        private void Tick(object _)
        {
            PerfSample sample = null;
            try
            {
                PerfSampler sampler;
                bool withProcesses;
                lock (_gate)
                {
                    if (_disposed || _sampler == null) return;
                    sampler = _sampler;
                    int every = PerfRules.ProcessEvery(_cadence);
                    withProcesses = every > 0 && _ticks % every == 0;
                    _ticks++;
                }

                sample = sampler.Sample(withProcesses);

                lock (_gate)
                {
                    if (_disposed || _sampler != sampler) return;       // stopped while we were reading
                    _latest = sample;
                    // The history keeps the totals only. Holding an hour of per-process lists would cost several
                    // megabytes — an odd thing for the part of the app that watches memory. What the trend needs is
                    // kept in Remember, one number a minute per process.
                    _history.Add(sample.Top.Count == 0 ? sample : sample with { Top = Array.Empty<ProcUsage>() });
                    if (sample.HasProcesses) Remember(sample);
                }
            }
            catch (Exception ex) { _flags.ReportError(FeatureId, ex); return; }

            try { if (sample != null) Sampled?.Invoke(sample); }
            catch (Exception ex) { _flags.ReportError(FeatureId, ex); }
        }

        /// <summary>
        /// One point a minute per process name, for the leak trend. A point a minute is enough to see a line over an
        /// hour and keeps this bounded: at most <see cref="MaxTracked"/> names, each with 60 numbers, and names not
        /// seen for a while are dropped.
        /// </summary>
        private const int MaxTracked = 200, ForgetMinutes = 10;

        private void Remember(PerfSample s)
        {
            double minutes = (s.TimeUtc - _started).TotalMinutes;
            foreach (var u in s.Top)
            {
                if (!_memory.TryGetValue(u.Name, out var ring))
                {
                    if (_memory.Count >= MaxTracked) continue;
                    ring = new SampleRing<(double, double)>(PerfRules.HistoryMinutes + 1);
                    _memory[u.Name] = ring;
                }
                _lastSeen[u.Name] = s.TimeUtc;
                if (ring.Count == 0 || minutes - ring.Last.Minutes >= 0.9) ring.Add((minutes, u.PrivateMb));
            }

            if (_memory.Count < MaxTracked) return;
            var old = _lastSeen.Where(kv => (s.TimeUtc - kv.Value).TotalMinutes > ForgetMinutes).Select(kv => kv.Key).ToList();
            foreach (var name in old) { _memory.Remove(name); _lastSeen.Remove(name); }
        }

        /// <summary>What this process's memory has been doing. <c>Unknown</c> when it has not been watched long enough.</summary>
        public MemoryTrendResult TrendOf(string process)
        {
            string name = RawProc.Normalize(process);
            lock (_gate)
                return _memory.TryGetValue(name, out var ring)
                    ? MemoryTrend.Of(ring.ToList())
                    : MemoryTrend.Of(Array.Empty<(double, double)>());
        }

        /// <summary>Everything that is growing straight and fast enough to be worth saying out loud, biggest first.</summary>
        public List<(string Name, MemoryTrendResult Trend)> Leaks()
        {
            List<(string, SampleRing<(double, double)>)> copy;
            lock (_gate) copy = _memory.Select(kv => (kv.Key, kv.Value)).ToList();
            return copy
                .Select(x => (Name: x.Item1, Trend: MemoryTrend.Of(x.Item2.ToList())))
                .Where(x => x.Trend.Verdict == MemoryVerdict.Growing)
                .OrderByDescending(x => x.Trend.MbPerHour)
                .ToList();
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
            }
            _flags.Changed -= OnFlagChanged;
            lock (_gate)
            {
                _timer?.Dispose();
                _timer = null;
                _sampler?.Dispose();
                _sampler = null;
                _cadence = PerfCadence.Off;
            }
        }
    }
}
