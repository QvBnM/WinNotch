using System;
using System.Collections.Generic;
using System.Linq;

namespace WinNotch.Core.Activity
{
    /// <summary>
    /// The one place that decides what the notch's pill shows (ADR 0006). Features post <see cref="Activity"/> objects;
    /// the manager applies the rules and publishes an immutable <see cref="View"/>, which the presenter draws.
    /// <para>Rules:
    /// <list type="bullet">
    /// <item>Same <see cref="Activity.Key"/> as one shown, queued or persistent → changed in place, no new slot.</item>
    /// <item>Notch open → dropped (as before P13); Critical and persistent ones wait until it closes.</item>
    /// <item>Fullscreen (pill hidden) → only High and Critical; Normal and Low are dropped (as before: "important").</item>
    /// <item>A new alert of the same or higher priority replaces the one shown (as before); a lower one waits in the queue
    /// (at most <see cref="QueueTtl"/>, <see cref="MaxQueue"/> entries, the least important and oldest dropped first).
    /// Critical interrupts anything.</item>
    /// <item>More than <see cref="BurstLimit"/> distinct Normal/Low alerts within <see cref="BurstWindow"/> → one „N noutăți”
    /// summary (the burst's alerts are dropped, only their count is kept; it expires like an alert).</item>
    /// <item>Low → a 2 s peek. Interactive (buttons) → shown now or dropped, never queued or grouped.</item>
    /// <item>Persistent ones stay until dismissed, behind the alerts; two share the pill (split).</item>
    /// </list></para>
    /// <para>Thread-safe: everything runs under one lock; the presenter is called after it is released. Timers: one
    /// one-shot timer while an alert is shown, nothing otherwise (no polling, nothing ticking in standby).</para>
    /// </summary>
    public sealed class ActivityManager
    {
        public const string FeatureId = "activity-manager";
        /// <summary>Id and key of the „N noutăți” summary.</summary>
        public const string GroupId = "noutati";

        public const int MaxQueue = 50, MaxPersistent = 8, BurstLimit = 3;
        public static readonly TimeSpan BurstWindow = TimeSpan.FromSeconds(5), GroupDuration = TimeSpan.FromSeconds(4),
            PeekDuration = TimeSpan.FromSeconds(2), QueueTtl = TimeSpan.FromSeconds(10);

        /// <summary>The app's manager (set at startup by the notch); null in the helper modes.</summary>
        public static ActivityManager Current { get; set; }

        private sealed class Entry
        {
            public Activity A;
            public DateTime PostedAt;
            public long Seq;
            public bool IsGroup;
            /// <summary>The summary's alerts (its count).</summary>
            public HashSet<string> Keys;
            public ActivityPriority Priority => A.Priority;
            public bool Groupable => !IsGroup && !A.Interactive && !A.Persistent && A.Priority <= ActivityPriority.Normal;
        }

        private readonly IActivityScheduler _clock;
        private readonly IActivityEnvironment _env;
        private readonly object _lock = new object();
        private readonly List<Entry> _queue = new List<Entry>();
        private readonly List<Entry> _persistent = new List<Entry>();
        private readonly List<(DateTime At, string Key)> _burst = new List<(DateTime, string)>();
        private Entry _current;
        private IDisposable _timer;
        private long _timerGen, _seq, _version;
        private volatile ActivityView _view = ActivityView.Empty;

        public ActivityManager(IActivityScheduler clock, IActivityEnvironment env = null, IActivityPresenter presenter = null)
        {
            _clock = clock ?? new ThreadPoolActivityScheduler();
            _env = env;
            Presenter = presenter;
        }

        /// <summary>Told after every visible change (outside the lock, from the thread that made it).</summary>
        public IActivityPresenter Presenter { get; set; }

        /// <summary>What to show now.</summary>
        public ActivityView View => _view;

        public int QueueCount { get { lock (_lock) return _queue.Count; } }
        public int PersistentCount { get { lock (_lock) return _persistent.Count; } }
        /// <summary>A timer is waiting (an alert is shown). False in standby: nothing ticks.</summary>
        public bool TimerActive { get { lock (_lock) return _timer != null; } }
        /// <summary>Something is shown, queued or persistent.</summary>
        public bool HasAny { get { lock (_lock) return _current != null || _queue.Count > 0 || _persistent.Count > 0; } }

        /// <summary>Asks for an activity to be shown; see the class rules. Never throws for a bad activity (Dropped).</summary>
        public PostResult Post(Activity a)
        {
            if (a == null || !a.IsValid) return PostResult.Dropped;
            PostResult r;
            bool changed;
            lock (_lock)
            {
                long before = _version;
                r = PostLocked(a, _clock.UtcNow);
                changed = _version != before;
            }
            if (changed) Notify();
            return r;
        }

        /// <summary>Changes the activity with <paramref name="a"/>'s key in place; false (and nothing posted) when there is none.</summary>
        public bool Update(Activity a)
        {
            if (a == null || !a.IsValid) return false;
            bool found, changed;
            lock (_lock)
            {
                long before = _version;
                found = UpdateLocked(a);
                changed = _version != before;
            }
            if (changed) Notify();
            return found;
        }

        /// <summary>The alert shown with this key stays for its whole duration again (a volume drag updated in place by its owner).</summary>
        public bool Touch(string key)
        {
            lock (_lock)
            {
                if (_current == null || _current.IsGroup || _current.A.Key != key) return false;
                StartTimer(_current);
                return true;
            }
        }

        /// <summary>Removes the activity with this key (shown, queued or persistent). True if there was one.</summary>
        public bool Dismiss(string key)
        {
            if (key == null) return false;
            bool found = false, changed;
            lock (_lock)
            {
                long before = _version;
                if (_current != null && _current.A.Key == key) { found = true; _current = null; Advance(_clock.UtcNow); }
                found |= _queue.RemoveAll(e => e.A.Key == key) > 0;
                if (_persistent.RemoveAll(e => e.A.Key == key) > 0) { found = true; Bump(); }
                changed = _version != before;
            }
            if (changed) Notify();
            return found;
        }

        /// <summary>Everything goes: alerts, queue, persistent ones, the burst count. Returns how many there were.</summary>
        public int DismissAll()
        {
            int n;
            lock (_lock)
            {
                n = (_current != null ? 1 : 0) + _queue.Count + _persistent.Count;
                _current = null;
                StopTimer();
                _queue.Clear();
                _persistent.Clear();
                _burst.Clear();
                Bump();
            }
            Notify();
            return n;
        }

        /// <summary>
        /// The notch was closed: the alert that was covered by it is gone (as before P13), queued ones too, except
        /// Critical; the persistent ones and a waiting Critical come back.
        /// </summary>
        public void NotchClosed()
        {
            lock (_lock)
            {
                if (_current != null && _current.Priority < ActivityPriority.Critical) { _current = null; StopTimer(); }
                _queue.RemoveAll(e => e.Priority < ActivityPriority.Critical);
                if (_current == null) Advance(_clock.UtcNow);
                Bump();
            }
            Notify();
        }

        // ------------------------------------------------------------------ the rules (under _lock)

        private PostResult PostLocked(Activity a, DateTime now)
        {
            if (UpdateLocked(a)) return PostResult.Updated;
            // the same key as a waiting alert (now a button alert) or a persistent one (now an alert): this one replaces it
            _queue.RemoveAll(q => !q.IsGroup && q.A.Key == a.Key);
            if (!a.Persistent && _persistent.RemoveAll(p => p.A.Key == a.Key) > 0 && _current == null) Bump();

            bool critical = a.Priority == ActivityPriority.Critical;
            if (_env?.NotchOpen == true)
            {
                if (a.Persistent) { AddPersistent(a, now); return PostResult.Queued; }
                if (critical) { Enqueue(NewEntry(a, now)); return PostResult.Queued; }
                return PostResult.Dropped;
            }
            if (_env?.Fullscreen == true && a.Priority < ActivityPriority.High) return PostResult.Dropped;

            if (a.Persistent)
            {
                AddPersistent(a, now);
                if (_current == null) { Bump(); return PostResult.Shown; }
                return PostResult.Queued;
            }

            var e = NewEntry(a, now);
            if (a.Interactive)
            {
                if (_current != null && _current.Priority > a.Priority) return PostResult.Dropped;
                SetCurrent(e);
                return PostResult.Shown;
            }

            if (e.Groupable)
            {
                _burst.RemoveAll(b => now - b.At >= BurstWindow);
                _burst.Add((now, a.Key));
                var group = GroupEntry();
                if (group != null)
                {
                    // one more alert of the burst (the same alert again is not counted twice)
                    if (group.Keys.Add(a.Key))
                    {
                        group.A = GroupActivity(group.Keys.Count);
                        if (group == _current) { StartTimer(group); Bump(); }
                    }
                    return PostResult.Grouped;
                }
                var keys = new HashSet<string>(_burst.Select(b => b.Key), StringComparer.Ordinal);
                if (keys.Count > BurstLimit)
                {
                    // the burst's alerts (shown or waiting) fold into one summary
                    _queue.RemoveAll(q => q.Groupable && keys.Contains(q.A.Key));
                    if (_current != null && _current.Groupable && keys.Contains(_current.A.Key)) _current = null;
                    var g = new Entry { IsGroup = true, Keys = keys, PostedAt = now, Seq = ++_seq, A = GroupActivity(keys.Count) };
                    Place(g);
                    return PostResult.Grouped;
                }
            }
            return Place(e);
        }

        /// <summary>Same key and same kind (alert / persistent) → changed in place. False when there is none.</summary>
        private bool UpdateLocked(Activity a)
        {
            if (a.Persistent)
            {
                var p = _persistent.FirstOrDefault(x => x.A.Key == a.Key);
                if (p == null) return false;
                p.A = a;
                SortPersistent();
                if (_current == null) Bump();
                return true;
            }
            if (_current != null && !_current.IsGroup && _current.A.Key == a.Key)
            {
                _current.A = a;
                StartTimer(_current);
                Bump();
                return true;
            }
            if (a.Interactive) return false;                                     // a button alert is never queued: posted anew
            var q = _queue.FirstOrDefault(x => !x.IsGroup && x.A.Key == a.Key);
            if (q == null) return false;
            q.A = a;
            _queue.Remove(q);
            Insert(q);                                                          // its priority may have changed
            return true;
        }

        private PostResult Place(Entry e)
        {
            if (_current == null || e.Priority >= _current.Priority) { SetCurrent(e); return PostResult.Shown; }
            Enqueue(e);
            return PostResult.Queued;
        }

        private Entry NewEntry(Activity a, DateTime now) => new Entry { A = a, PostedAt = now, Seq = ++_seq };

        private Entry GroupEntry() => _current?.IsGroup == true ? _current : _queue.FirstOrDefault(q => q.IsGroup);

        private void SetCurrent(Entry e)
        {
            _current = e;              // the one it replaces is gone (as before P13: the new alert replaces the old one)
            StartTimer(e);
            Bump();
        }

        private void Enqueue(Entry e)
        {
            Insert(e);
            while (_queue.Count > MaxQueue)
            {
                // the least important, then the oldest, goes first
                var drop = _queue.OrderBy(q => q.Priority).ThenBy(q => q.Seq).First();
                _queue.Remove(drop);
            }
        }

        /// <summary>Highest priority first, then in order of arrival.</summary>
        private void Insert(Entry e)
        {
            int i = _queue.FindIndex(q => q.Priority < e.Priority || (q.Priority == e.Priority && q.Seq > e.Seq));
            if (i < 0) _queue.Add(e); else _queue.Insert(i, e);
        }

        private void AddPersistent(Activity a, DateTime now)
        {
            _persistent.Add(NewEntry(a, now));
            SortPersistent();
            while (_persistent.Count > MaxPersistent) _persistent.Remove(_persistent.OrderBy(p => p.Priority).ThenBy(p => p.Seq).First());
        }

        private void SortPersistent()
        {
            var sorted = _persistent.OrderByDescending(p => p.Priority).ThenBy(p => p.Seq).ToList();
            _persistent.Clear();
            _persistent.AddRange(sorted);
        }

        /// <summary>The shown alert ended: the next waiting one that may still be shown, or the persistent ones.</summary>
        private void Advance(DateTime now)
        {
            StopTimer();
            bool open = _env?.NotchOpen == true, full = _env?.Fullscreen == true;
            for (int i = 0; i < _queue.Count;)
            {
                var e = _queue[i];
                bool critical = e.Priority == ActivityPriority.Critical;
                if (open) { if (critical) { i++; continue; } _queue.RemoveAt(i); continue; }   // Critical waits for the notch to close
                _queue.RemoveAt(i);
                if (!critical && now - e.PostedAt > QueueTtl) continue;                         // too old to be news
                if (full && e.Priority < ActivityPriority.High) continue;
                _current = e;
                StartTimer(e);
                break;
            }
            Bump();
        }

        private void StartTimer(Entry e)
        {
            StopTimer();
            var d = e.IsGroup ? GroupDuration : e.Priority == ActivityPriority.Low ? PeekDuration : e.A.Duration;
            long gen = ++_timerGen;
            _timer = _clock.Schedule(d, () => Expire(gen));
        }

        private void StopTimer()
        {
            _timerGen++;
            _timer?.Dispose();
            _timer = null;
        }

        private void Expire(long gen)
        {
            lock (_lock)
            {
                if (gen != _timerGen) return;                  // replaced or dismissed meanwhile
                _timer?.Dispose();
                _timer = null;
                _current = null;
                Advance(_clock.UtcNow);
            }
            Notify();
        }

        /// <summary>A visible change: a new view.</summary>
        private void Bump()
        {
            _version++;
            ActivityView v;
            if (_current != null)
            {
                var kind = _current.IsGroup ? ActivityViewKind.Group : _current.Priority == ActivityPriority.Low ? ActivityViewKind.Peek : ActivityViewKind.Single;
                v = new ActivityView(_version, kind, _current.A, null, _current.IsGroup ? _current.Keys.Count : 0);
            }
            else if (_persistent.Count >= 2) v = new ActivityView(_version, ActivityViewKind.Split, _persistent[0].A, _persistent[1].A, 0);
            else if (_persistent.Count == 1) v = new ActivityView(_version, ActivityViewKind.Single, _persistent[0].A, null, 0);
            else v = new ActivityView(_version, ActivityViewKind.None, null, null, 0);
            _view = v;
        }

        private void Notify()
        {
            try { Presenter?.Invalidate(); }
            catch { /* the presenter reports its own errors (FeatureFlags.ReportError); the rules go on */ }
        }

        // ------------------------------------------------------------------ the „N noutăți” summary

        /// <summary>„1 noutate”, „5 noutăți”, „20 de noutăți” (Romanian: „de” from 20, except 101–119, 201–219…).</summary>
        public static string GroupTitle(int n)
        {
            if (n == 1) return "1 noutate";
            int r = n % 100;
            return n + (n != 0 && (r == 0 || r >= 20) ? " de noutăți" : " noutăți");
        }

        private static Activity GroupActivity(int count) => new Activity
        {
            Id = GroupId, Priority = ActivityPriority.Normal, Duration = GroupDuration, Title = GroupTitle(count), Width = 230, Height = 40,
        };
    }
}
