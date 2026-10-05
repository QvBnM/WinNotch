using System;
using System.Collections.Generic;
using System.Linq;

namespace WinNotch.Core.Update
{
    /// <summary>What startup.json keeps between runs.</summary>
    public sealed class StartupState
    {
        /// <summary>The version the per-version fields below belong to.</summary>
        public string Version { get; set; } = "";
        /// <summary>Starts (UTC) of this version that haven't ended cleanly (yet).</summary>
        public List<DateTime> Starts { get; set; } = new List<DateTime>();
        /// <summary>The last run started and hasn't marked a clean exit.</summary>
        public bool Running { get; set; }
        /// <summary>The last (or current) run is in safe mode.</summary>
        public bool SafeMode { get; set; }
        /// <summary>That safe mode was started by the guard after repeated crashes (not asked with --safe-mode by hand).</summary>
        public bool AutoSafe { get; set; }
        /// <summary>When that safe-mode run started (UTC): only a crash soon after it leads to a rollback.</summary>
        public DateTime SafeStartedAt { get; set; }
        /// <summary>The guard just asked for a restart in safe mode; the next start (with --safe-mode) takes it over.</summary>
        public bool SafeRestartPending { get; set; }
        /// <summary>This version ran 10 minutes without unhandled errors: the previous exe may go.</summary>
        public bool Healthy { get; set; }
        /// <summary>Versions this PC rolled back from: never offered again (a newer one is).</summary>
        public List<string> Refused { get; set; } = new List<string>();
        /// <summary>Last automatic rollback (UTC): at most one per <see cref="StartupGuard.RollbackCooldown"/>.</summary>
        public DateTime LastRollback { get; set; }
    }

    /// <summary>rollback.json: written by the version that gave up, read once by the one it restored.</summary>
    public sealed class RollbackNote
    {
        public string Refused { get; set; } = "";
        public string Reason { get; set; } = "";
        public DateTime At { get; set; }
    }

    /// <summary>Where the state lives (a file in the app; memory in the tests).</summary>
    public interface IStartupStore
    {
        /// <summary>The saved state; null if missing or unreadable.</summary>
        StartupState Load();
        void Save(StartupState state);
        /// <summary>The rollback note, if any; it is removed (shown once).</summary>
        RollbackNote TakeRollbackNote();
        void WriteRollbackNote(RollbackNote note);
        void DeleteRollbackNote();
    }

    public enum StartupAction
    {
        /// <summary>Start normally.</summary>
        Normal,
        /// <summary>Run in safe mode (asked with --safe-mode, or a rollback isn't possible).</summary>
        SafeMode,
        /// <summary>Crashed 3 times in 5 minutes: restart once with --safe-mode.</summary>
        RestartInSafeMode,
        /// <summary>Crashed again soon after the automatic safe mode: put the previous version back.</summary>
        Rollback,
    }

    /// <summary>
    /// Watches the starts of a new version. Pure logic: the clock and the store are given, so every branch is tested.
    /// <list type="bullet">
    /// <item>3 starts without a clean exit within 5 minutes → restart once in safe mode;</item>
    /// <item>that automatic safe-mode run ends abruptly within 5 minutes too → roll back to the previous version (if a
    /// valid WinNotch.old.exe exists and there was no rollback in the last 30 minutes; otherwise stay in safe mode). A
    /// safe-mode run that worked for longer, or one asked for by hand, only counts as an ordinary crash;</item>
    /// <item>10 minutes of running (counted in steady minute ticks, so sleep or a clock change can't shorten it) without
    /// unhandled errors, not in safe mode → healthy: the counter is emptied and only now the previous exe may go.</item>
    /// </list>
    /// A clean exit ("Ieșire", Windows shutting down, the restart for an update) is not counted.
    /// </summary>
    public sealed class StartupGuard
    {
        public const int CrashLimit = 3;
        public static readonly TimeSpan CrashWindow = TimeSpan.FromMinutes(5);
        public static readonly TimeSpan HealthyAfter = TimeSpan.FromMinutes(10);
        public static readonly TimeSpan RollbackCooldown = TimeSpan.FromMinutes(30);
        /// <summary><see cref="CheckHealthy"/> runs every minute; a longer gap (sleep, clock change) starts the 10 minutes over.</summary>
        public static readonly TimeSpan MaxTickGap = TimeSpan.FromMinutes(2);
        /// <summary>After Windows announced the end of the session: still running this much later = the shutdown was cancelled.</summary>
        public static readonly TimeSpan SessionEndGrace = TimeSpan.FromMinutes(2);
        private const int MaxRefused = 20;

        private readonly IStartupStore _store;
        private readonly AppVersion _current;
        private readonly Func<DateTime> _now;
        private readonly Action<string> _log;
        private readonly object _lock = new object();
        private DateTime? _myStart;
        private DateTime _healthyFrom, _lastCheck;
        private DateTime? _sessionEndingAt;
        private bool _safe, _begun, _rollbackPending;

        public StartupGuard(IStartupStore store, AppVersion current, Func<DateTime> now = null, Action<string> log = null)
        {
            _store = store;
            _current = current ?? AppVersion.Zero;
            _now = now ?? (() => DateTime.UtcNow);
            _log = log;
        }

        public StartupState State { get; private set; } = new StartupState();

        /// <summary>After a rollback to this version: "Am revenit la 0.6.9: 0.7.0 se închidea" (shown once), else null.</summary>
        public string RollbackMessage { get; private set; }

        /// <summary>This run is in safe mode.</summary>
        public bool InSafeMode { get { lock (_lock) return _safe; } }

        /// <summary>
        /// Called first thing at startup: decides how this run starts and records it.
        /// </summary>
        /// <param name="safeModeArg">Started with --safe-mode.</param>
        /// <param name="previousExeValid">WinNotch.old.exe is there and is an older WinNotch, to roll back to.</param>
        public StartupAction Begin(bool safeModeArg, bool previousExeValid)
        {
            lock (_lock)
            {
                var s = Load();
                var now = _now();
                string me = _current.ToString();
                if (s.Version != me)
                {
                    s.Version = me; s.Starts.Clear(); s.Running = false; s.SafeMode = false; s.AutoSafe = false;
                    s.SafeRestartPending = false; s.Healthy = false;
                }
                ReadRollbackNote(s);
                if (s.Refused.Count > 0) _log?.Invoke("Versiuni refuzate pe acest PC (nu mai sunt propuse): " + string.Join(", ", s.Refused) + ".");

                bool previousUnclean = s.Running, previousSafe = s.SafeMode;
                bool recentAutoSafe = previousSafe && s.AutoSafe && (now - s.SafeStartedAt).Duration() <= CrashWindow;
                bool safeRestart = s.SafeRestartPending && safeModeArg;
                s.SafeRestartPending = false;
                s.Starts.RemoveAll(t => (now - t).Duration() > CrashWindow);
                int crashes = s.Starts.Count;       // runs of this version that ended abruptly in the last 5 minutes

                StartupAction action;
                if (previousUnclean && recentAutoSafe)
                {
                    if (!previousExeValid) { _log?.Invoke("Pornire: s-a închis brusc și în modul sigur, dar nu există o versiune anterioară validă; rămân în modul sigur."); action = StartupAction.SafeMode; }
                    else if (s.LastRollback != default && (now - s.LastRollback).Duration() < RollbackCooldown)     // Duration: a clock set back can't unlock it
                    { _log?.Invoke("Pornire: a fost deja o revenire în ultimele 30 de minute; rămân în modul sigur."); action = StartupAction.SafeMode; }
                    else action = StartupAction.Rollback;
                }
                else if (safeModeArg) action = StartupAction.SafeMode;
                else if (crashes >= CrashLimit) action = StartupAction.RestartInSafeMode;
                else action = StartupAction.Normal;

                if (action == StartupAction.Normal || action == StartupAction.SafeMode)
                {
                    bool auto = action == StartupAction.SafeMode && (safeRestart || (previousUnclean && recentAutoSafe));
                    Record(s, now, action == StartupAction.SafeMode, auto);
                }
                else
                {
                    // handing over (to the safe-mode run or the previous version): this one isn't a crash
                    s.Running = false;
                    s.SafeMode = false;
                    if (action == StartupAction.RestartInSafeMode)
                    {
                        s.Starts.Clear();               // these crashes led to safe mode; a clean exit from it starts the count over
                        s.SafeRestartPending = true;
                        _log?.Invoke("Pornire: " + crashes + " închideri bruște în 5 minute; repornesc în modul sigur.");
                    }
                }
                _begun = true;
                Save(s);
                return action;
            }
        }

        private void Record(StartupState s, DateTime now, bool safe, bool autoSafe)
        {
            s.Starts.Add(now);
            _myStart = now;
            s.Running = true;
            s.SafeMode = _safe = safe;
            s.AutoSafe = safe && autoSafe;
            if (safe) s.SafeStartedAt = now;
            _healthyFrom = _lastCheck = now;
        }

        /// <summary>The rollback or the restart failed: this run continues in safe mode and is counted as such.</summary>
        public void ContinueInSafeMode()
        {
            lock (_lock)
            {
                Record(State, _now(), true, false);     // a later crash is an ordinary one, not a new rollback
                Save(State);
            }
        }

        /// <summary>
        /// About to put the previous version back: remembers the refused version and the time and writes rollback.json
        /// first, so a process killed in the middle of the swap still leaves the note. Undo with <see cref="RollbackFailed"/>.
        /// </summary>
        public void RollingBack(string reason)
        {
            lock (_lock)
            {
                var now = _now();
                string me = _current.ToString();
                if (!State.Refused.Contains(me)) State.Refused.Add(me);
                while (State.Refused.Count > MaxRefused) State.Refused.RemoveAt(0);
                State.LastRollback = now;
                State.Running = false;
                State.SafeMode = State.AutoSafe = State.Healthy = false;
                State.Starts.Clear();
                State.Version = "";                    // if this version is installed again, it starts with a clean count
                _rollbackPending = true;
                Save(State);
                try { _store.WriteRollbackNote(new RollbackNote { Refused = me, Reason = reason ?? "", At = now }); }
                catch (Exception ex) { _log?.Invoke("Revenire: nota nu a putut fi scrisă: " + ex.GetType().Name); }
                _log?.Invoke("Revenire la versiunea anterioară: " + me + " " + reason + ".");
            }
        }

        /// <summary>The files couldn't be swapped: nothing was refused after all; this run continues in safe mode.</summary>
        public void RollbackFailed()
        {
            lock (_lock)
            {
                if (!_rollbackPending) return;
                _rollbackPending = false;
                State.Refused.Remove(_current.ToString());
                State.Version = _current.ToString();
                try { _store.DeleteRollbackNote(); } catch { }
            }
            ContinueInSafeMode();
        }

        /// <summary>
        /// "Ieșire", Windows shutting down or the restart for an update: this run doesn't count as a crash.
        /// <paramref name="sessionEnding"/>: Windows announced the end of the session, which another program can still
        /// cancel; if this run is still going later, <see cref="CheckHealthy"/> takes the protection up again.
        /// </summary>
        public void MarkCleanExit(bool sessionEnding = false)
        {
            lock (_lock)
            {
                if (_myStart == null) return;
                State.Starts.Remove(_myStart.Value);
                _myStart = null;
                State.Running = false;
                _sessionEndingAt = sessionEnding ? _now() : null;
                Save(State);
            }
        }

        /// <summary>A clean exit was marked but the app keeps running (the restart for an update failed): protected again.</summary>
        public void Resume()
        {
            lock (_lock)
            {
                if (_myStart != null || !_begun || _rollbackPending) return;
                _sessionEndingAt = null;
                var now = _now();
                State.Starts.Add(now);
                _myStart = now;
                State.Running = true;
                State.SafeMode = _safe;
                _healthyFrom = _lastCheck = now;
                Save(State);
            }
        }

        /// <summary>An unhandled error was caught (the app kept running): the 10 healthy minutes start again.</summary>
        public void NoteError()
        {
            lock (_lock) _healthyFrom = _now();
        }

        /// <summary>
        /// Called every minute. True once, when this version becomes healthy (10 minutes in steady ticks without
        /// unhandled errors, not in safe mode): the crash counter is emptied and the previous exe may be deleted.
        /// </summary>
        public bool CheckHealthy()
        {
            bool resume;
            lock (_lock)
            {
                var now = _now();
                resume = _sessionEndingAt != null && (now - _sessionEndingAt.Value).Duration() > SessionEndGrace;
            }
            if (resume) Resume();
            lock (_lock)
            {
                var now = _now();
                var gap = now - _lastCheck;
                _lastCheck = now;
                if (gap < TimeSpan.Zero || gap > MaxTickGap) { _healthyFrom = now; return false; }     // sleep or clock change
                if (_safe || _myStart == null || State.Healthy) return false;
                if (now - _healthyFrom < HealthyAfter) return false;
                State.Healthy = true;
                State.Starts.Clear();
                State.Starts.Add(_myStart.Value);       // this run itself still has to end cleanly
                Save(State);
                return true;
            }
        }

        /// <summary>The previous exe may be deleted (this version proved itself).</summary>
        public bool OldExeMayBeDeleted { get { lock (_lock) return State.Healthy; } }

        /// <summary>A version this PC rolled back from: not offered again.</summary>
        public bool IsRefused(AppVersion v)
        {
            if (v is null) return false;
            lock (_lock) return State.Refused.Any(r => AppVersion.TryParse(r, out var x) && x == v);
        }

        /// <summary>WinNotch.old.exe can be rolled back to only if it is a WinNotch older than this one.</summary>
        public static bool IsValidPrevious(AppVersion previous, AppVersion current) => previous is not null && current is not null && previous < current;

        private void ReadRollbackNote(StartupState s)
        {
            RollbackNote note = null;
            try { note = _store.TakeRollbackNote(); } catch (Exception ex) { _log?.Invoke("Revenire: nota nu a putut fi citită: " + ex.GetType().Name); }
            if (note == null || !AppVersion.TryParse(note.Refused, out var refused)) return;
            // only meaningful in an older version; if the refused one was installed again since, the note is stale
            if (!(refused > _current)) return;
            string r = refused.ToString();
            if (!s.Refused.Contains(r)) s.Refused.Add(r);
            while (s.Refused.Count > MaxRefused) s.Refused.RemoveAt(0);
            RollbackMessage = "Am revenit la " + _current + ": " + r + " se închidea";
        }

        private StartupState Load()
        {
            StartupState s = null;
            try { s = _store.Load(); } catch (Exception ex) { _log?.Invoke("Pornire: starea nu a putut fi citită: " + ex.GetType().Name); }
            s ??= new StartupState();
            s.Version ??= "";
            s.Starts ??= new List<DateTime>();
            s.Refused = (s.Refused ?? new List<string>()).Where(r => r != null && AppVersion.TryParse(r, out _)).Distinct().Take(MaxRefused).ToList();
            State = s;
            return s;
        }

        private void Save(StartupState s)
        {
            try { _store.Save(s); } catch (Exception ex) { _log?.Invoke("Pornire: starea nu a putut fi salvată: " + ex.GetType().Name); }
        }
    }
}
