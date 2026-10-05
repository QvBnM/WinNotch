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
    }

    public enum StartupAction
    {
        /// <summary>Start normally.</summary>
        Normal,
        /// <summary>Run in safe mode (asked with --safe-mode, or a rollback isn't possible).</summary>
        SafeMode,
        /// <summary>Crashed 3 times in 5 minutes: restart once with --safe-mode.</summary>
        RestartInSafeMode,
        /// <summary>Crashed again in safe mode: put the previous version back.</summary>
        Rollback,
    }

    /// <summary>
    /// Watches the starts of a new version. Pure logic: the clock and the store are given, so every branch is tested.
    /// <list type="bullet">
    /// <item>3 starts without a clean exit within 5 minutes → restart once in safe mode;</item>
    /// <item>one more abrupt end while in safe mode → roll back to the previous version (if WinNotch.old.exe exists and
    /// there was no rollback in the last 30 minutes; otherwise stay in safe mode);</item>
    /// <item>10 minutes without unhandled errors (not in safe mode) → healthy: the counter is emptied and only now the
    /// previous exe may be deleted.</item>
    /// </list>
    /// A clean exit ("Ieșire", Windows shutting down, the restart for an update) is not counted.
    /// </summary>
    public sealed class StartupGuard
    {
        public const int CrashLimit = 3;
        public static readonly TimeSpan CrashWindow = TimeSpan.FromMinutes(5);
        public static readonly TimeSpan HealthyAfter = TimeSpan.FromMinutes(10);
        public static readonly TimeSpan RollbackCooldown = TimeSpan.FromMinutes(30);
        private const int MaxRefused = 20;

        private readonly IStartupStore _store;
        private readonly AppVersion _current;
        private readonly Func<DateTime> _now;
        private readonly Action<string> _log;
        private readonly object _lock = new object();
        private DateTime? _myStart;
        private DateTime _healthyFrom;
        private bool _safe;

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
        /// <param name="previousExeExists">WinNotch.old.exe is there to roll back to.</param>
        public StartupAction Begin(bool safeModeArg, bool previousExeExists)
        {
            lock (_lock)
            {
                var s = Load();
                var now = _now();
                string me = _current.ToString();
                if (s.Version != me)
                {
                    s.Version = me; s.Starts.Clear(); s.Running = false; s.SafeMode = false; s.Healthy = false;
                }
                ReadRollbackNote(s);

                bool previousUnclean = s.Running, previousSafe = s.SafeMode;
                s.Starts.RemoveAll(t => now - t > CrashWindow || t > now);
                int crashes = s.Starts.Count;       // runs of this version that ended abruptly in the last 5 minutes

                StartupAction action;
                if (previousUnclean && previousSafe)
                {
                    if (!previousExeExists) { _log?.Invoke("Pornire: s-a închis brusc și în modul sigur, dar versiunea anterioară lipsește; rămân în modul sigur."); action = StartupAction.SafeMode; }
                    else if (s.LastRollback != default && (now - s.LastRollback).Duration() < RollbackCooldown)     // Duration: a clock set back can't unlock it
                    { _log?.Invoke("Pornire: a fost deja o revenire în ultimele 30 de minute; rămân în modul sigur."); action = StartupAction.SafeMode; }
                    else action = StartupAction.Rollback;
                }
                else if (safeModeArg) action = StartupAction.SafeMode;
                else if (crashes >= CrashLimit) action = StartupAction.RestartInSafeMode;
                else action = StartupAction.Normal;

                if (action == StartupAction.Normal || action == StartupAction.SafeMode)
                {
                    s.Starts.Add(now);
                    _myStart = now;
                    s.Running = true;
                    s.SafeMode = _safe = action == StartupAction.SafeMode;
                    _healthyFrom = now;
                }
                else
                {
                    // handing over (to the safe-mode run or the previous version): this one isn't a crash
                    s.Running = false;
                    s.SafeMode = false;
                }
                if (action == StartupAction.RestartInSafeMode) _log?.Invoke("Pornire: " + crashes + " închideri bruște în 5 minute; repornesc în modul sigur.");
                Save(s);
                return action;
            }
        }

        /// <summary>The rollback failed (or was refused): this run continues in safe mode and is counted as such.</summary>
        public void ContinueInSafeMode()
        {
            lock (_lock)
            {
                var now = _now();
                State.Starts.Add(now);
                _myStart = now;
                State.Running = true;
                State.SafeMode = _safe = true;
                _healthyFrom = now;
                Save(State);
            }
        }

        /// <summary>The previous version was put back: remembers the refused version, the time and writes rollback.json.</summary>
        public void RolledBack(string reason)
        {
            lock (_lock)
            {
                var now = _now();
                string me = _current.ToString();
                if (!State.Refused.Contains(me)) State.Refused.Add(me);
                while (State.Refused.Count > MaxRefused) State.Refused.RemoveAt(0);
                State.LastRollback = now;
                State.Running = false;
                State.SafeMode = false;
                Save(State);
                try { _store.WriteRollbackNote(new RollbackNote { Refused = me, Reason = reason ?? "", At = now }); }
                catch (Exception ex) { _log?.Invoke("Revenire: nota nu a putut fi scrisă: " + ex.GetType().Name); }
                _log?.Invoke("Revenire la versiunea anterioară: " + me + " " + reason + ".");
            }
        }

        /// <summary>"Ieșire", Windows shutting down or the restart for an update: this run doesn't count as a crash.</summary>
        public void MarkCleanExit()
        {
            lock (_lock)
            {
                if (_myStart == null) return;
                State.Starts.Remove(_myStart.Value);
                _myStart = null;
                State.Running = false;
                Save(State);
            }
        }

        /// <summary>An unhandled error was caught (the app kept running): the 10 healthy minutes start again.</summary>
        public void NoteError()
        {
            lock (_lock) _healthyFrom = _now();
        }

        /// <summary>
        /// Called now and then. True once, when this version becomes healthy (10 minutes without unhandled errors, not
        /// in safe mode): the crash counter is emptied and the previous exe may be deleted.
        /// </summary>
        public bool CheckHealthy()
        {
            lock (_lock)
            {
                if (_safe || _myStart == null || State.Healthy) return false;
                var now = _now();
                if (now - _healthyFrom < HealthyAfter) return false;
                State.Healthy = true;
                State.Starts.Clear();
                if (_myStart != null) State.Starts.Add(_myStart.Value);       // this run itself still has to end cleanly
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
            s.Refused = (s.Refused ?? new List<string>()).Where(r => r != null).ToList();
            State = s;
            return s;
        }

        private void Save(StartupState s)
        {
            try { _store.Save(s); } catch (Exception ex) { _log?.Invoke("Pornire: starea nu a putut fi salvată: " + ex.GetType().Name); }
        }
    }
}
