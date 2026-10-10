using System;
using WinNotch.Core.Context;
using WinNotch.Core.Flags;
using WinNotch.Core.Perf;

namespace WinNotch.Features.GameMode
{
    /// <summary>What the watcher asks the app to do. Everything here is ours and reversible — see <see cref="Quiet"/>.</summary>
    public interface IGameHost
    {
        /// <summary>
        /// A game started (true) or finished (false). This is the whole of P61's "do something automatically": it only
        /// ever changes WinNotch's own behaviour, never the system's. Power plans and other programs' priorities are
        /// P64, behind their own switch, because those are changes to the machine and need a way back.
        /// </summary>
        void Quiet(bool on);

        /// <summary>A session finished and is worth telling about (already saved). On the UI thread.</summary>
        void SessionEnded(GameReport report);
    }

    /// <summary>
    /// P61: the game session, from end to end. It owns nothing that measures and nothing that draws: the context
    /// engine says what is in front, <see cref="PerfMonitor"/> produces the samples, <see cref="GameSession"/> adds
    /// them up, <see cref="GameReportStore"/> saves the result, and the host shows it.
    /// <para><b>What it does not do.</b> It does not touch alerts. Silencing the notch over a fullscreen game, holding
    /// the unimportant alerts back and showing them once afterwards is already <c>fullscreen-hide</c> (P53) — a second
    /// gate would be a parallel system with its own bugs, which <c>CLAUDE.md</c> forbids for good reason. A game
    /// session inherits it, and that is why the brief's "go quiet, hold the alerts" needs no code here.</para>
    /// <para>With the switch off, or with the context engine off, nothing starts: no session, no faster sampling, no
    /// report. The feature degrades to exactly the behaviour of the version before it.</para>
    /// </summary>
    public sealed class GameWatcher : IDisposable
    {
        /// <summary>
        /// Serialises everything that decides: the detector (which has state and is not thread-safe), starting and
        /// ending a session, and subscribing or unsubscribing. Three threads reach these — the context engine's timer,
        /// the UI thread through <see cref="Tick"/>, and whichever thread flips the switch — and without this two of
        /// them could both see "no session", both start one, and the first would never be reported. Order: this one
        /// first, then <see cref="_gate"/>.
        /// </summary>
        private readonly object _turn = new object();
        /// <summary>Guards the small state only; never held across a call into the host, the store or the monitor.</summary>
        private readonly object _gate = new object();
        private readonly FeatureFlags _flags;
        private readonly PerfMonitor _monitor;
        private readonly ContextEngine _engine;
        private readonly IGameHost _host;
        private readonly GameReportStore _store;
        private readonly Func<(double? Cpu, double? Gpu)> _temps;
        private readonly Func<DateTime> _clock;
        private readonly Action<string> _log;

        private readonly GameDetect _detect = new GameDetect();
        private GameSession _session;
        private Action<string> _flagHandler;
        private EventHandler<ContextChangedEventArgs> _contextHandler;
        private Action<PerfSample> _sampleHandler;
        private bool _running, _disposed;

        /// <summary>How many sessions were reported this run.</summary>
        public int Reported { get; private set; }
        /// <summary>The game of the open session, "" when none.</summary>
        public string Playing { get { lock (_gate) return _session?.Game ?? ""; } }

        public GameWatcher(FeatureFlags flags, PerfMonitor monitor, ContextEngine engine, IGameHost host,
                           GameReportStore store, Func<(double? Cpu, double? Gpu)> temps = null,
                           Func<DateTime> clock = null, Action<string> log = null)
        {
            _flags = flags ?? throw new ArgumentNullException(nameof(flags));
            _monitor = monitor;
            _engine = engine;
            _host = host;
            _store = store;
            _temps = temps ?? (() => (null, null));
            _clock = clock ?? (() => DateTime.UtcNow);
            _log = log;
        }

        /// <summary>Subscribes to the switch and, if it is on, to the context and the samples. Never throws.</summary>
        public void Start()
        {
            _flagHandler = id =>
            {
                if (!string.Equals(id, GameDetect.FeatureId, StringComparison.Ordinal) &&
                    !string.Equals(id, PerfRules.FeatureId, StringComparison.Ordinal)) return;
                try { Apply(); }
                catch (Exception ex) { _flags.ReportError(GameDetect.FeatureId, ex); }
            };
            _flags.Changed += _flagHandler;
            Apply();
        }

        /// <summary>
        /// The game mode has two dependencies and hides neither: the context engine says when a game is in front, and
        /// <see cref="PerfMonitor"/> produces every number in the report. With either switched off there would be a
        /// session with nothing in it, so the feature simply does not start — and its description in Settings says so.
        /// A switch is never worked around (<c>CLAUDE.md</c>): the monitor refuses to run with its own switch off, and
        /// asking it anyway would be exactly that.
        /// </summary>
        private void Apply()
        {
            lock (_turn) ApplyLocked();
        }

        private void ApplyLocked()
        {
            bool want = _flags.IsEnabled(GameDetect.FeatureId) && _flags.IsEnabled(PerfRules.FeatureId) && _engine != null;
            lock (_gate)
            {
                if (_disposed || want == _running) return;
                _running = want;
            }
            // Under _turn, so two switch changes from two threads cannot leave the subscriptions and the flag
            // disagreeing — which once meant a stopped watcher still listening, and a second Hook leaking the first
            // handler for good.
            if (want) Hook(); else Unhook(abandon: true);
        }

        /// <summary>Idempotent: whatever was attached is detached first, so a handler can never be left unreachable.</summary>
        private void Hook()
        {
            Detach();
            _contextHandler = (s, e) => OnContext(e?.New);        // arrives on the engine's thread: takes _turn
            _engine.Changed += _contextHandler;
            _sampleHandler = OnSample;
            if (_monitor != null) _monitor.Sampled += _sampleHandler;
            OnContextLocked(_engine.Snapshot);           // a game already running when the switch goes on
        }

        private void Detach()
        {
            if (_contextHandler != null && _engine != null) _engine.Changed -= _contextHandler;
            _contextHandler = null;
            if (_sampleHandler != null && _monitor != null) _monitor.Sampled -= _sampleHandler;
            _sampleHandler = null;
        }

        /// <summary>
        /// Unsubscribes and, with <paramref name="abandon"/>, drops an open session <b>without</b> a report: the
        /// feature was switched off or the app is closing, and a half-session reported as if it had ended would be a
        /// number the user cannot trust.
        /// </summary>
        private void Unhook(bool abandon)
        {
            Detach();
            if (!abandon) return;
            bool had;
            lock (_gate)
            {
                had = _session != null;
                _session = null;
                _detect.Reset();
            }
            if (had) Stop();
        }

        // ------------------------------------------------------------------ the session

        /// <summary>
        /// One context change. Also called from <see cref="Tick"/>: a game that simply vanished produces no further
        /// change, so without a tick its session would sit in the grace period for ever.
        /// </summary>
        private void OnContext(ContextSnapshot snapshot)
        {
            lock (_turn) OnContextLocked(snapshot);
        }

        /// <summary>Caller holds <see cref="_turn"/>.</summary>
        private void OnContextLocked(ContextSnapshot snapshot)
        {
            GameChange change;
            try { change = _detect.Update(snapshot, _clock()); }
            catch (Exception ex) { _flags.ReportError(GameDetect.FeatureId, ex); return; }

            if (change.Ended) End(change.EndedProcess);
            if (change.Started) Begin(change.Process);
            else if (change.Process.Length > 0)
            {
                // An exclusive-fullscreen game is sometimes seen before its process name can be read: the session is
                // started nameless and is given the name as soon as the detector learns it. Without this the report
                // would be saved as "", the comparison over time would be impossible for that game, and the game
                // itself would end up in the "what was stealing your machine" list.
                GameSession open;
                lock (_gate) open = _session;
                open?.Rename(change.Process);
            }
        }

        /// <summary>One line from the app's existing per-second timer. Cheap: returns at once when nothing is open.</summary>
        public void Tick()
        {
            // TryEnter, not a plain lock: this runs on the UI thread, and ending a session touches the file and the
            // monitor's own gate. Rather than let the notch wait for that, the beat is skipped — another comes in a
            // second, and the grace period is ninety.
            if (!System.Threading.Monitor.TryEnter(_turn)) return;
            try
            {
                bool running;
                bool open;
                lock (_gate) { running = _running; open = _session != null; }
                // The detector's state is read under _turn, like everything else that touches it.
                if (!running || (!open && _detect.State == GameState.None)) return;
                OnContextLocked(_engine?.Snapshot);
            }
            finally { System.Threading.Monitor.Exit(_turn); }
        }

        private void Begin(string process)
        {
            lock (_gate)
            {
                if (_disposed || !_running) return;
                _session = new GameSession(process, _clock());
            }
            try
            {
                _monitor?.SetGame(true);                 // the one place a cadence under two seconds is allowed
                _host?.Quiet(true);
                _log?.Invoke("Mod de joc: pornit.");     // no name: a game is personal enough
            }
            catch (Exception ex)
            {
                // Nothing would be measuring this session, so a report from it would be an empty promise.
                _flags.ReportError(GameDetect.FeatureId, ex);
                lock (_gate) _session = null;
                _detect.Reset();
                Stop();
            }
        }

        private void End(string process)
        {
            GameSession session;
            lock (_gate)
            {
                session = _session;
                _session = null;
            }
            if (session == null) return;
            Stop();

            try
            {
                var report = session.Report(_clock());
                if (report.Duration < GameDetect.MinSession || !report.Measured)
                {
                    _log?.Invoke("Mod de joc: oprit, sesiune prea scurtă pentru un raport.");
                    return;
                }
                _store?.Append(report);
                Reported++;
                _log?.Invoke("Mod de joc: oprit, " + report.ToLogString() + ".");
                _host?.SessionEnded(report);
            }
            catch (Exception ex) { _flags.ReportError(GameDetect.FeatureId, ex); }
        }

        /// <summary>Back to normal: the fast sampling and our own quieting both go, whatever happened to the report.</summary>
        private void Stop()
        {
            try { _monitor?.SetGame(false); } catch (Exception ex) { _flags.ReportError(GameDetect.FeatureId, ex); }
            try { _host?.Quiet(false); } catch (Exception ex) { _flags.ReportError(GameDetect.FeatureId, ex); }
        }

        private void OnSample(PerfSample sample)
        {
            GameSession session;
            lock (_gate) session = _session;
            if (session == null) return;
            try
            {
                var (cpu, gpu) = _temps();
                session.Add(sample, cpu, gpu);
            }
            catch (Exception ex) { _flags.ReportError(GameDetect.FeatureId, ex); }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
            }
            if (_flagHandler != null) _flags.Changed -= _flagHandler;
            _flagHandler = null;
            lock (_turn)
            {
                Unhook(abandon: true);
                lock (_gate) _running = false;
            }
        }
    }
}
