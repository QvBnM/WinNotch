using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using WinNotch.Core.Flags;

namespace WinNotch.Core.Context
{
    /// <summary>Timers for the engine: real ones in the app, a hand-driven clock in the tests.</summary>
    public interface IContextScheduler
    {
        DateTime UtcNow { get; }
        /// <summary>Runs <paramref name="work"/> once after <paramref name="due"/> (any thread). Dispose cancels it.</summary>
        IDisposable Schedule(TimeSpan due, Action work);
        /// <summary>Runs <paramref name="work"/> every <paramref name="period"/> until disposed.</summary>
        IDisposable Every(TimeSpan period, Action work);
    }

    /// <summary>Thread-pool timers.</summary>
    public sealed class ThreadPoolContextScheduler : IContextScheduler
    {
        public DateTime UtcNow => DateTime.UtcNow;
        public IDisposable Schedule(TimeSpan due, Action work) => new Timer(_ => work(), null, due < TimeSpan.Zero ? TimeSpan.Zero : due, Timeout.InfiniteTimeSpan);
        public IDisposable Every(TimeSpan period, Action work) => new Timer(_ => work(), null, period, period);
    }

    /// <summary>
    /// The one place that knows what the user is doing now (ADR 0004). Combines its sources into an immutable
    /// <see cref="ContextSnapshot"/> and raises <see cref="Changed"/> with exactly the fields that changed, after a
    /// 300 ms debounce (a quick Alt+Tab through ten windows is one event), at most 1 s after the first change.
    /// <para>Runs only while the "context-engine" feature is on; when it is switched off every source is stopped (hooks
    /// and subscriptions removed), the snapshot goes back to <see cref="ContextSnapshot.Empty"/> and nothing is raised.
    /// A source that throws keeps its last good value, is retried later (30 s, doubling up to 10 min) and is reported
    /// once per run to <see cref="FeatureFlags.ReportError"/>; the engine itself keeps going.</para>
    /// <para><see cref="Changed"/> is raised on a timer thread: handlers that touch the UI go through the Dispatcher.
    /// The log gets only categories and process names, never window titles.</para>
    /// </summary>
    public sealed class ContextEngine : IDisposable
    {
        public const string FeatureId = "context-engine";

        public static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(300);
        /// <summary>Changes that never stop (a window title that ticks) are still published this often.</summary>
        public static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(1);
        /// <summary>Sources without a Windows event are read this often (the rule: never under 2 s).</summary>
        public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);
        public static readonly TimeSpan IdleAfter = TimeSpan.FromMinutes(2);
        public static readonly TimeSpan FirstRetry = TimeSpan.FromSeconds(30), MaxRetry = TimeSpan.FromMinutes(10);

        /// <summary>Changes worth a line in the log (rare ones; the app in front changes all the time).</summary>
        private const ContextField Logged = ContextField.Fullscreen | ContextField.Meeting | ContextField.AudioOutput |
                                            ContextField.Network | ContextField.Monitors | ContextField.UsbDrive;

        /// <summary>The app's engine (set once at startup); null in the helper modes.</summary>
        public static ContextEngine Current { get; set; }

        private sealed class Slot
        {
            public IContextSource Source;
            public Func<object> Read;
            public object Value;
            public Action Handler;
            public bool Dirty, Started, Reported;
            public int Failures;
            public DateTime RetryAt;
        }

        private readonly List<Slot> _slots = new List<Slot>();
        private readonly ContextSources _sources;
        private readonly FeatureFlags _flags;
        private readonly IContextScheduler _clock;
        private readonly AppCategories _categories;
        private readonly Action<string> _log;
        private readonly object _lock = new object();
        private readonly object _flushLock = new object();
        /// <summary>
        /// Starting and stopping the sources, one at a time (the switch can change from several threads at once). Taken
        /// before _lock, never while holding it; re-entered when a start failure switches the feature off.
        /// </summary>
        private readonly object _lifecycle = new object();
        private ContextSnapshot _snapshot = ContextSnapshot.Empty;
        private bool _running, _attached, _disposed;
        private int _generation;
        private DateTime? _firstPending;
        private IDisposable _debounce, _poll;
        /// <summary>Smoke tests only (<see cref="ForceCategoryForSmoke"/>); null in the app.</summary>
        private AppCategory? _smokeCategory;

        public ContextEngine(ContextSources sources, FeatureFlags flags = null, IContextScheduler scheduler = null,
                             AppCategories categories = null, Action<string> log = null)
        {
            _sources = sources ?? new ContextSources();
            _flags = flags;
            _clock = scheduler ?? new ThreadPoolContextScheduler();
            _categories = categories ?? AppCategories.Default;
            _log = log;
            Add(_sources.Foreground); Add(_sources.Media); Add(_sources.Privacy); Add(_sources.Audio); Add(_sources.Network);
            Add(_sources.Power); Add(_sources.Display); Add(_sources.UsbDrive); Add(_sources.Idle);
        }

        private void Add<T>(IContextSource<T> s)
        {
            if (s == null) return;
            var slot = new Slot { Source = s, Read = () => s.Read() };
            slot.Handler = () => OnSourceChanged(slot);
            _slots.Add(slot);
        }

        /// <summary>What the user is doing now (<see cref="ContextSnapshot.Empty"/> while the engine is off).</summary>
        public ContextSnapshot Snapshot { get { lock (_lock) return _snapshot; } }

        public bool Running { get { lock (_lock) return _running; } }

        /// <summary>
        /// Smoke tests only (WinNotch.exe --smoke, the "fake-context" test command; ADR 0008): from now on the app in front
        /// reads as <paramref name="category"/> (null = back to the real one). Goes through the normal flush, so the
        /// snapshot, <see cref="Changed"/> and everyone reading <see cref="Snapshot"/> see it exactly like a real change.
        /// False while the engine is off (it applies when the engine starts). The caller checks smoke mode.
        /// </summary>
        internal bool ForceCategoryForSmoke(AppCategory? category)
        {
            lock (_lock)
            {
                _smokeCategory = category;
                if (!_running || _disposed) return false;
                ScheduleFlush();
                return true;
            }
        }

        /// <summary>Old snapshot, new snapshot and the fields that changed. Raised on a timer thread.</summary>
        public event EventHandler<ContextChangedEventArgs> Changed;

        private bool Enabled => _flags == null || _flags.IsEnabled(FeatureId);

        /// <summary>Follows the feature switch from now on, and starts the sources if it is on.</summary>
        public void Start()
        {
            lock (_lock)
            {
                if (_disposed || _attached) return;
                _attached = true;
            }
            if (_flags != null) _flags.Changed += OnFlagChanged;
            Sync();
        }

        private void OnFlagChanged(string id)
        {
            if (id == FeatureId) Sync();
        }

        /// <summary>
        /// Brings the sources in line with the switch. The switch is read inside the lifecycle lock: of several changes
        /// arriving together, the last one to get the lock sees the final state, whatever order the handlers ran in.
        /// </summary>
        private void Sync()
        {
            lock (_lifecycle)
            {
                if (Enabled) StartSources(); else StopSources();
            }
        }

        /// <summary>Under _lifecycle.</summary>
        private void StartSources()
        {
            int gen;
            lock (_lock)
            {
                if (_running || _disposed) return;
                _running = true;
                gen = ++_generation;
            }
            foreach (var s in _slots)
            {
                s.Value = null; s.Failures = 0; s.Reported = false; s.RetryAt = default;
                s.Source.Changed += s.Handler;
                try { s.Source.Start(); s.Started = true; }
                catch (Exception ex) { s.Started = false; Fail(s, ex, "pornire"); }
            }
            bool switchedOff;
            lock (_lock)
            {
                switchedOff = !_running || gen != _generation;
                if (!switchedOff)
                {
                    foreach (var s in _slots) s.Dirty = s.Started;
                    ScheduleFlush();
                    if (_slots.Any(s => s.Source.Polled)) _poll = _clock.Every(PollInterval, () => PollTick(gen));
                }
            }
            if (switchedOff)
            {
                // switched off while starting (e.g. by the errors above): undo what this start did, unless a newer start owns it now
                lock (_lock) if (_running) return;
                foreach (var s in _slots) StopSlot(s);
                return;
            }
            _log?.Invoke("Context: pornit (" + _slots.Count(s => s.Started) + " surse).");
        }

        private void StopSlot(Slot s)
        {
            s.Source.Changed -= s.Handler;
            s.Dirty = false;
            if (!s.Started) return;
            s.Started = false;
            try { s.Source.Stop(); }
            catch (Exception ex) { _log?.Invoke("Context: oprirea sursei „" + s.Source.Name + "”: " + ex.GetType().Name); }
        }

        /// <summary>Under _lifecycle (Dispose and Sync take it).</summary>
        private void StopSources()
        {
            lock (_lock)
            {
                if (!_running) return;
                _running = false;
                _generation++;
                _debounce?.Dispose(); _debounce = null;
                _poll?.Dispose(); _poll = null;
                _firstPending = null;
                _snapshot = ContextSnapshot.Empty;
            }
            foreach (var s in _slots) StopSlot(s);
            _log?.Invoke("Context: oprit.");
        }

        private void OnSourceChanged(Slot s)
        {
            lock (_lock)
            {
                if (!_running || !s.Started) return;
                s.Dirty = true;
                ScheduleFlush();
            }
        }

        private void PollTick(int gen)
        {
            try
            {
                lock (_lock)
                {
                    if (!_running || gen != _generation) return;
                    var now = _clock.UtcNow;
                    bool any = false;
                    foreach (var s in _slots)
                        if (s.Started && (s.Source.Polled || (s.Failures > 0 && now >= s.RetryAt))) { s.Dirty = true; any = true; }
                    if (any) ScheduleFlush();
                }
            }
            catch (Exception ex) { _log?.Invoke("Context: verificarea periodică: " + ex.GetType().Name); }
        }

        /// <summary>Trailing debounce: every change pushes the flush 300 ms later, but never past 1 s after the first one. Under _lock.</summary>
        private void ScheduleFlush()
        {
            var now = _clock.UtcNow;
            _firstPending ??= now;
            var due = Debounce;
            var left = MaxDelay - (now - _firstPending.Value);
            if (left < due) due = left < TimeSpan.Zero ? TimeSpan.Zero : left;
            _debounce?.Dispose();
            int gen = _generation;
            _debounce = _clock.Schedule(due, () => Flush(gen));
        }

        private void Flush(int gen)
        {
            try
            {
                lock (_flushLock)
                {
                    List<Slot> dirty;
                    lock (_lock)
                    {
                        if (!_running || gen != _generation) return;
                        _debounce?.Dispose();          // this one has fired; a timer may be disposed from its own callback
                        _debounce = null;
                        _firstPending = null;
                        dirty = _slots.Where(s => s.Dirty).ToList();
                        foreach (var s in dirty) s.Dirty = false;
                    }
                    var now = _clock.UtcNow;
                    foreach (var s in dirty)
                    {
                        if (s.Failures > 0 && now < s.RetryAt) continue;          // waiting before trying a broken source again
                        try
                        {
                            s.Value = s.Read();
                            if (s.Failures > 0) _log?.Invoke("Context: sursa „" + s.Source.Name + "” merge din nou.");
                            s.Failures = 0;
                        }
                        catch (Exception ex) { Fail(s, ex, "citire"); }
                    }

                    var next = Compose();
                    ContextSnapshot old;
                    ContextField fields;
                    lock (_lock)
                    {
                        if (!_running || gen != _generation) return;           // switched off meanwhile: nothing is raised
                        old = _snapshot;
                        fields = ContextSnapshot.Diff(old, next);
                        if (fields == ContextField.None) return;
                        _snapshot = next;
                    }
                    if ((fields & Logged) != 0) _log?.Invoke("Context: " + next.ToLogString());
                    Raise(new ContextChangedEventArgs(old, next, fields));
                }
            }
            catch (Exception ex) { _log?.Invoke("Context: actualizarea a eșuat: " + ex.GetType().Name); }
        }

        /// <summary>A source failed: keep its last value, try again later, report it once per run. Only the type is logged.</summary>
        private void Fail(Slot s, Exception ex, string what)
        {
            s.Failures++;
            var wait = TimeSpan.FromTicks(Math.Min(MaxRetry.Ticks, FirstRetry.Ticks * (1L << Math.Min(10, s.Failures - 1))));
            s.RetryAt = _clock.UtcNow + wait;
            if (s.Failures == 1) _log?.Invoke("Context: sursa „" + s.Source.Name + "” (" + what + "): " + ex.GetType().Name);
            if (s.Reported) return;
            s.Reported = true;
            try { _flags?.ReportError(FeatureId, ex); }
            catch (Exception e2) { _log?.Invoke("Context: raportarea erorii: " + e2.GetType().Name); }
        }

        private T Value<T>(IContextSource<T> source, T fallback)
        {
            if (source == null) return fallback;
            var slot = _slots.FirstOrDefault(s => ReferenceEquals(s.Source, source));
            return slot?.Value is T v ? v : fallback;
        }

        private ContextSnapshot Compose()
        {
            var fg = Value(_sources.Foreground, ForegroundInfo.None) ?? ForegroundInfo.None;
            var media = Value(_sources.Media, MediaState.None) ?? MediaState.None;
            var cap = Value(_sources.Privacy, CaptureState.None) ?? CaptureState.None;
            var net = Value(_sources.Network, NetworkState.Unknown) ?? NetworkState.Unknown;
            var power = Value(_sources.Power, PowerState.Unknown) ?? PowerState.Unknown;
            string proc = AppCategories.Normalize(fg.Process);
            AppCategory? forced;
            lock (_lock) forced = _smokeCategory;
            var cat = forced ?? _categories.Categorize(proc);
            string meeting = ContextRules.Meeting(fg, cap, _categories);
            return new ContextSnapshot
            {
                ForegroundProcess = proc,
                ForegroundTitle = fg.Title ?? "",
                ForegroundCategory = cat,
                Fullscreen = ContextRules.Fullscreen(fg, cat, media),
                MediaPlaying = media.Playing,
                MediaApp = media.Playing ? media.App ?? "" : "",
                MicrophoneInUse = cap.MicrophoneInUse,
                MicrophoneApps = cap.MicrophoneInUse ? (cap.MicrophoneApps ?? Array.Empty<string>()).ToList() : Array.Empty<string>(),
                CameraInUse = cap.CameraInUse,
                CameraApps = cap.CameraInUse ? (cap.CameraApps ?? Array.Empty<string>()).ToList() : Array.Empty<string>(),
                AudioOutput = Value(_sources.Audio, AudioOutputKind.Unknown),
                MeetingActive = meeting != null,
                MeetingApp = meeting ?? "",
                Online = net.Online,
                Network = net.Kind,
                HasBattery = power.HasBattery,
                OnBattery = power.HasBattery && power.OnBattery,
                BatteryPercent = power.HasBattery ? power.Percent : -1,
                MonitorCount = Value(_sources.Display, 0),
                UsbDriveConnected = Value(_sources.UsbDrive, false),
                Idle = Value(_sources.Idle, TimeSpan.Zero) >= IdleAfter,
            };
        }

        /// <summary>Each handler on its own: one that throws can't stop the others or the engine. Only the type is logged.</summary>
        private void Raise(ContextChangedEventArgs e)
        {
            var handlers = Changed;
            if (handlers == null) return;
            foreach (EventHandler<ContextChangedEventArgs> h in handlers.GetInvocationList())
            {
                try { h(this, e); }
                catch (Exception ex) { _log?.Invoke("Context: un abonat a dat eroare: " + ex.GetType().Name); }
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_disposed) return;
                _disposed = true;
            }
            if (_flags != null) _flags.Changed -= OnFlagChanged;
            lock (_lifecycle) StopSources();
        }
    }
}
