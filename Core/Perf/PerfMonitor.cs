using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using WinNotch.Core.Flags;

namespace WinNotch.Core.Perf
{
    /// <summary>
    /// P60: the one place that measures the machine for the performance section, and the only one allowed to.
    /// Everything that shows a number — the Performanță tab, a widget, a game session, a report — reads from here and
    /// never samples on its own, so there is exactly one timer and exactly one cost.
    /// <para>The timer only exists while somebody is looking (<see cref="AddViewer"/>) or a game is running
    /// (<see cref="SetGame"/>). With nothing on screen and no game, there is no timer at all — not a slow one. See
    /// <see cref="PerfRules"/> for the cadences and why.</para>
    /// <para><b>Two locks, in this order, never the other way round.</b> <c>_passGate</c> guards a pass from start to
    /// finish: whoever wants to retire the sampler waits for the pass to end, because the sampler owns native handles
    /// and pulling one out from under a call in progress is how P51c killed the app. <c>_gate</c> guards the small
    /// state (history, cadence, the counters) and is never held across a measurement.</para>
    /// <para>Samples are raised on the timer's thread. Anything that draws must come back to the Dispatcher itself.</para>
    /// </summary>
    public sealed class PerfMonitor : IDisposable
    {
        public const string FeatureId = PerfRules.FeatureId;

        /// <summary>The app's instance; null in the helper modes and in the tests that do not need one.</summary>
        public static PerfMonitor Current { get; set; }

        /// <summary>Held for a whole pass. Order: this one first, then <c>_gate</c>.</summary>
        private readonly object _passGate = new object();
        private readonly object _gate = new object();

        private readonly FeatureFlags _flags;
        private readonly Func<IPerfSampler> _makeSampler;
        private readonly Func<DateTime> _clock;
        private readonly SampleRing<PerfSample> _history = new SampleRing<PerfSample>(PerfRules.HistoryCapacity);
        /// <summary>
        /// Per process name, one point a minute: how much private memory it held, with the time it was read. The time
        /// is absolute on purpose — a cadence change must not shift the axis under a trend that is already building.
        /// </summary>
        private readonly Dictionary<string, SampleRing<(DateTime At, double Mb)>> _memory =
            new Dictionary<string, SampleRing<(DateTime, double)>>(StringComparer.Ordinal);
        private readonly Dictionary<string, DateTime> _lastSeen = new Dictionary<string, DateTime>(StringComparer.Ordinal);

        private PerfSample _latest = PerfSample.Empty;
        private IPerfSampler _sampler;
        private Timer _timer;
        private PerfCadence _cadence = PerfCadence.Off;
        private int _viewers, _ticks;
        private bool _game, _disposed;

        /// <summary>The trends, recomputed at most once a minute: the points move once a minute anyway.</summary>
        private List<(string Name, MemoryTrendResult Trend)> _leaks = new List<(string, MemoryTrendResult)>();
        private DateTime _leaksAt = DateTime.MinValue;

        /// <summary>One sample, just taken, on the timer's thread.</summary>
        public event Action<PerfSample> Sampled;

        /// <param name="flags">The feature switches; the monitor starts and stops with <see cref="FeatureId"/>.</param>
        /// <param name="makeSampler">Builds a sampler when one is needed. Called outside both locks.</param>
        /// <param name="clock">UTC now; injected so the tests can run an hour in a millisecond.</param>
        public PerfMonitor(FeatureFlags flags, Func<IPerfSampler> makeSampler, Func<DateTime> clock = null)
        {
            _flags = flags ?? throw new ArgumentNullException(nameof(flags));
            _makeSampler = makeSampler ?? throw new ArgumentNullException(nameof(makeSampler));
            _clock = clock ?? (() => DateTime.UtcNow);
            _flags.Changed += OnFlagChanged;
        }

        /// <summary>
        /// The newest sample, with its process list; <see cref="PerfSample.Empty"/> before the first pass. The samples
        /// kept in <see cref="History"/> have that list stripped — see <see cref="RunPass"/>.
        /// </summary>
        public PerfSample Last { get { lock (_gate) return _latest ?? PerfSample.Empty; } }

        public PerfCadence Cadence { get { lock (_gate) return _cadence; } }
        public bool Running => Cadence != PerfCadence.Off;
        /// <summary>How many things asked for the numbers (<see cref="AddViewer"/>). For the tests and the log.</summary>
        public int Viewers { get { lock (_gate) return _viewers; } }

        /// <summary>Oldest first. A copy, taken under the lock, so the caller can walk it while the timer adds more.</summary>
        public List<PerfSample> History() { lock (_gate) return _history.ToList(); }

        /// <summary>
        /// How long the kept history spans, in minutes. Cheap on purpose: the tab asks for it on every sample, and
        /// copying an hour of samples just to subtract two timestamps would be work for nothing.
        /// </summary>
        public double WatchedMinutes
        {
            get
            {
                lock (_gate)
                    return _history.Count > 1 ? (_history[_history.Count - 1].TimeUtc - _history[0].TimeUtc).TotalMinutes : 0;
            }
        }

        // ------------------------------------------------------------------ who needs it running

        /// <summary>
        /// Something started showing the numbers. Every call must be paired with <see cref="RemoveViewer"/>; the
        /// caller should only pair a <c>RemoveViewer</c> with an <c>AddViewer</c> that actually happened.
        /// </summary>
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
            // The direction is never assumed: two changes from two threads can arrive in any order, so the switch is
            // read inside Apply, under the lock that decides (CLAUDE.md, "Cum declari o funcție nouă").
            if (!string.Equals(id, FeatureId, StringComparison.Ordinal)) return;
            try { Apply(); }
            catch (Exception ex) { _flags.ReportError(FeatureId, ex); }
        }

        /// <summary>
        /// Starts, re-times or stops the timer to match the state. The only place that touches it, and the only place
        /// that retires a sampler — which is why it waits for a pass in flight first.
        /// </summary>
        private void Apply()
        {
            if (Volatile.Read(ref _disposed)) return;
            IPerfSampler drop = null, fresh = null;
            try
            {
                // The pass gate first: a measurement in progress finishes before its sampler can be taken away.
                lock (_passGate)
                {
                    bool need, haveSampler;
                    lock (_gate)
                    {
                        if (_disposed) return;
                        // Read under the deciding lock: a switch flipped twice from two threads must not leave a
                        // running timer for a feature that is off (or the other way round).
                        var want = PerfRules.Pick(_flags.IsEnabled(FeatureId), _viewers > 0, _game);
                        if (want == _cadence) return;
                        _cadence = want;
                        need = PerfRules.SecondsFor(want) > 0;
                        haveSampler = _sampler != null;
                        if (!need)
                        {
                            _timer?.Dispose();
                            _timer = null;
                            drop = _sampler;
                            _sampler = null;
                            _history.Clear();
                            _latest = PerfSample.Empty;
                            _memory.Clear();
                            _lastSeen.Clear();
                            _leaks = new List<(string, MemoryTrendResult)>();
                            _leaksAt = DateTime.MinValue;
                            _ticks = 0;
                        }
                    }

                    if (!need) return;
                    if (!haveSampler) fresh = _makeSampler();          // built outside _gate: it may touch Windows
                    lock (_gate)
                    {
                        if (_disposed) { drop = fresh; fresh = null; return; }
                        if (fresh != null) { _sampler = fresh; fresh = null; }
                        int ms = PerfRules.SecondsFor(_cadence) * 1000;
                        if (_timer == null) _timer = new Timer(Tick, null, 0, ms);
                        else _timer.Change(0, ms);
                    }
                }
            }
            catch (Exception ex) { _flags.ReportError(FeatureId, ex); }
            finally
            {
                Close(drop);
                Close(fresh);
            }
        }

        private void Close(IPerfSampler s)
        {
            if (s == null) return;
            try { s.Dispose(); } catch (Exception ex) { _flags.ReportError(FeatureId, ex); }
        }

        // ------------------------------------------------------------------ the pass

        /// <summary>
        /// One beat of the timer. <see cref="Monitor.TryEnter(object)"/> and not a plain lock: if a pass is still
        /// running (a crowded machine, 300 processes, one second of cadence), this beat is skipped rather than queued
        /// behind it — two passes on one sampler would scramble its deltas and its buffers.
        /// </summary>
        private void Tick(object _)
        {
            if (!Monitor.TryEnter(_passGate)) return;
            try { RunPass(); }
            catch (Exception ex) { try { _flags.ReportError(FeatureId, ex); } catch { } }
            finally { Monitor.Exit(_passGate); }
        }

        private void RunPass()
        {
            IPerfSampler sampler;
            bool withProcesses;
            lock (_gate)
            {
                if (_disposed || _sampler == null) return;
                sampler = _sampler;
                int every = PerfRules.ProcessEvery(_cadence);
                withProcesses = every > 0 && _ticks % every == 0;
                _ticks++;
            }

            var sample = sampler.Sample(withProcesses);
            if (sample == null) return;

            lock (_gate)
            {
                if (_disposed) return;
                _latest = sample;
                // The history keeps the totals only. Holding an hour of per-process lists would cost several
                // megabytes — an odd thing for the part of the app that watches memory. What the trend needs is kept
                // in Remember: one number a minute per process.
                _history.Add(sample.Processes.Count == 0 ? sample : sample with { Processes = Array.Empty<ProcUsage>() });
                if (sample.HasProcesses) Remember(sample);
            }

            try { Sampled?.Invoke(sample); }
            catch (Exception ex) { _flags.ReportError(FeatureId, ex); }
        }

        /// <summary>
        /// One point a minute per process name, for the leak trend. A point a minute is enough to see a line over an
        /// hour and keeps this bounded: at most <see cref="MaxTracked"/> names, each with an hour of numbers, and
        /// names not seen for <see cref="ForgetMinutes"/> are dropped on every expensive pass — not only once the
        /// table is full, or a machine that has seen 200 names would never track a new one again.
        /// </summary>
        private const int MaxTracked = 200, ForgetMinutes = 10;

        private void Remember(PerfSample s)
        {
            var old = _lastSeen.Where(kv => (s.TimeUtc - kv.Value).TotalMinutes > ForgetMinutes).Select(kv => kv.Key).ToList();
            foreach (var name in old) { _memory.Remove(name); _lastSeen.Remove(name); }

            foreach (var u in s.Processes)
            {
                if (!_memory.TryGetValue(u.Name, out var ring))
                {
                    if (_memory.Count >= MaxTracked) continue;
                    ring = new SampleRing<(DateTime, double)>(PerfRules.HistoryMinutes + 1);
                    _memory[u.Name] = ring;
                }
                _lastSeen[u.Name] = s.TimeUtc;
                if (ring.Count == 0 || (s.TimeUtc - ring.Last.At).TotalMinutes >= 0.9) ring.Add((s.TimeUtc, u.PrivateMb));
            }
        }

        /// <summary>Minutes-since-the-first-point, which is what <see cref="MemoryTrend"/> wants.</summary>
        private static List<(double Minutes, double Mb)> Relative(SampleRing<(DateTime At, double Mb)> ring)
        {
            var raw = ring.ToList();
            var list = new List<(double, double)>(raw.Count);
            if (raw.Count == 0) return list;
            var first = raw[0].At;
            foreach (var p in raw) list.Add(((p.At - first).TotalMinutes, p.Mb));
            return list;
        }

        /// <summary>What this process's memory has been doing. <c>Unknown</c> when it has not been watched long enough.</summary>
        public MemoryTrendResult TrendOf(string process)
        {
            string name = RawProc.Normalize(process);
            lock (_gate)
                return _memory.TryGetValue(name, out var ring)
                    ? MemoryTrend.Of(Relative(ring))
                    : MemoryTrend.Of(Array.Empty<(double, double)>());
        }

        /// <summary>
        /// Everything that is growing straight and fast enough to be worth saying out loud, biggest first. Recomputed
        /// at most once a minute: the points behind it only move once a minute, and the tab asks every two seconds.
        /// </summary>
        public List<(string Name, MemoryTrendResult Trend)> Leaks()
        {
            lock (_gate)
            {
                var now = _clock();
                if ((now - _leaksAt).TotalSeconds < 60) return _leaks;
                _leaksAt = now;
                _leaks = _memory
                    .Select(kv => (Name: kv.Key, Trend: MemoryTrend.Of(Relative(kv.Value))))
                    .Where(x => x.Trend.Verdict == MemoryVerdict.Growing)
                    .OrderByDescending(x => x.Trend.MbPerHour)
                    .ToList();
                return _leaks;
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
            }
            _flags.Changed -= OnFlagChanged;
            IPerfSampler drop;
            // Same order as Apply: wait for a pass in flight, then take the sampler's native handles away.
            lock (_passGate)
            {
                lock (_gate)
                {
                    _timer?.Dispose();
                    _timer = null;
                    drop = _sampler;
                    _sampler = null;
                    _cadence = PerfCadence.Off;
                }
            }
            if (drop != null) try { drop.Dispose(); } catch { }
            if (ReferenceEquals(Current, this)) Current = null;
        }
    }
}
