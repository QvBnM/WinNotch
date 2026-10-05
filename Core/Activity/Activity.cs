using System;
using System.Text.RegularExpressions;
using System.Threading;

namespace WinNotch.Core.Activity
{
    /// <summary>
    /// How much an activity matters. Over a fullscreen app only High and Critical are shown; Critical also interrupts
    /// anything and waits (instead of being dropped) while the notch is open; Low only "peeks" (2 s, a slightly wider pill).
    /// </summary>
    public enum ActivityPriority { Low = 0, Normal = 1, High = 2, Critical = 3 }

    /// <summary>What <see cref="ActivityManager.Post"/> did with an activity.</summary>
    public enum PostResult
    {
        /// <summary>Visible now (or, for a persistent one, part of the visible pill as soon as no alert covers it).</summary>
        Shown,
        /// <summary>An activity with the same key existed: it was changed in place (no new slot).</summary>
        Updated,
        /// <summary>Waits behind a more important one (or, Critical / persistent, until the notch closes).</summary>
        Queued,
        /// <summary>Counted in the „N noutăți” summary of a burst.</summary>
        Grouped,
        /// <summary>Not shown and forgotten: notch open, fullscreen, invalid, or a button alert that can't be shown now.</summary>
        Dropped,
    }

    /// <summary>
    /// Something the notch shows for a while (an alert) or until it is dismissed (persistent). Immutable. The manager
    /// never looks inside <see cref="Payload"/> (the UI, built by the caller) and never logs <see cref="Title"/>
    /// (it can hold a song or a page title).
    /// </summary>
    public sealed class Activity
    {
        /// <summary>Longest <see cref="Title"/> kept (longer ones are cut, with "…").</summary>
        public const int MaxTitle = 120;
        public static readonly TimeSpan MinDuration = TimeSpan.FromMilliseconds(100), MaxDuration = TimeSpan.FromHours(1);
        public const double MaxSize = 2000;

        private static readonly Regex IdFormat = new Regex("^[a-z0-9]+([.-][a-z0-9]+)*$", RegexOptions.CultureInvariant);

        private string _key, _title = "";

        /// <summary>What it is ("temp-hot", "browser.glovo"): lowercase words, dashes or dots, at most 60 characters.</summary>
        public string Id { get; init; }
        /// <summary>Activities with the same key replace each other in place (a volume drag, the steps of a flow). Default: <see cref="Id"/>.</summary>
        public string Key { get => _key ?? Id; init => _key = value; }
        public ActivityPriority Priority { get; init; } = ActivityPriority.Normal;
        /// <summary>How long it stays (ignored for persistent ones; Low always peeks for <see cref="ActivityManager.PeekDuration"/>).</summary>
        public TimeSpan Duration { get; init; } = TimeSpan.FromSeconds(4);
        /// <summary>Stays until dismissed; two of them share the pill (split pill).</summary>
        public bool Persistent { get; init; }
        /// <summary>Has buttons: shown right away or not at all (never queued, never grouped).</summary>
        public bool Interactive { get; init; }
        /// <summary>Size of the pill for it (unscaled px); 0 = the presenter's default.</summary>
        public double Width { get; init; }
        public double Height { get; init; }
        /// <summary>Short text for the split pill, the peek and the log-free summaries. Personal: never logged.</summary>
        public string Title { get => _title; init => _title = Cut(value); }
        /// <summary>An icon-font glyph for the split pill and the peek.</summary>
        public string Glyph { get; init; } = "";
        /// <summary>The caller's UI for it (a WPF element in the app); opaque here.</summary>
        public object Payload { get; init; }

        /// <summary>Id, size and duration are usable (checked by <see cref="ActivityManager.Post"/>).</summary>
        public bool IsValid =>
            Id != null && Id.Length <= 60 && IdFormat.IsMatch(Id) && Key != null && Key.Length <= 80 &&
            Width >= 0 && Width <= MaxSize && Height >= 0 && Height <= MaxSize && !double.IsNaN(Width) && !double.IsNaN(Height) &&
            (Persistent || (Duration >= MinDuration && Duration <= MaxDuration));

        private static string Cut(string s)
        {
            s = (s ?? "").Replace('\r', ' ').Replace('\n', ' ');
            return s.Length <= MaxTitle ? s : s.Substring(0, MaxTitle - 1) + "…";
        }
    }

    public enum ActivityViewKind
    {
        /// <summary>Standby: nothing from the manager.</summary>
        None,
        /// <summary>One activity, with its own UI and size (an alert, or the only persistent one).</summary>
        Single,
        /// <summary>A Low activity: the standby pill a little wider for 2 s, with its title.</summary>
        Peek,
        /// <summary>Two persistent activities side by side (left: <see cref="ActivityView.Primary"/>).</summary>
        Split,
        /// <summary>„N noutăți”: a burst of alerts, counted.</summary>
        Group,
    }

    /// <summary>What the pill should show now. Immutable; a new one (higher <see cref="Version"/>) after every visible change.</summary>
    public sealed class ActivityView
    {
        public static readonly ActivityView Empty = new ActivityView(0, ActivityViewKind.None, null, null, 0);

        public ActivityView(long version, ActivityViewKind kind, Activity primary, Activity secondary, int groupCount)
        {
            Version = version; Kind = kind; Primary = primary; Secondary = secondary; GroupCount = groupCount;
        }

        public long Version { get; }
        public ActivityViewKind Kind { get; }
        /// <summary>The activity shown (Single, Peek, the group's summary), or the left half of a split.</summary>
        public Activity Primary { get; }
        /// <summary>The right half of a split.</summary>
        public Activity Secondary { get; }
        /// <summary>How many alerts the „N noutăți” summary stands for.</summary>
        public int GroupCount { get; }
        /// <summary>A persistent activity (or two) is on screen, not an alert.</summary>
        public bool IsPersistent => Kind == ActivityViewKind.Split || (Kind == ActivityViewKind.Single && Primary?.Persistent == true);
    }

    /// <summary>One-shot timers: thread-pool ones in the app, a hand-driven clock in the tests.</summary>
    public interface IActivityScheduler
    {
        DateTime UtcNow { get; }
        /// <summary>Runs <paramref name="work"/> once after <paramref name="due"/> (any thread). Dispose cancels it.</summary>
        IDisposable Schedule(TimeSpan due, Action work);
    }

    /// <summary>Thread-pool one-shot timers (nothing runs while nothing is scheduled).</summary>
    public sealed class ThreadPoolActivityScheduler : IActivityScheduler
    {
        public DateTime UtcNow => DateTime.UtcNow;
        public IDisposable Schedule(TimeSpan due, Action work) =>
            new Timer(_ => work(), null, due < TimeSpan.Zero ? TimeSpan.Zero : due, Timeout.InfiniteTimeSpan);
    }

    /// <summary>The notch's state the rules need. Read from any thread (plain reads of the notch's fields).</summary>
    public interface IActivityEnvironment
    {
        /// <summary>The notch is open: alerts are dropped (as before), Critical and persistent ones wait.</summary>
        bool NotchOpen { get; }
        /// <summary>The pill is hidden over a fullscreen app on its monitor: only High and Critical.</summary>
        bool Fullscreen { get; }
    }

    /// <summary>Draws <see cref="ActivityManager.View"/>. <see cref="Invalidate"/> comes from any thread; the UI work goes through the Dispatcher.</summary>
    public interface IActivityPresenter
    {
        void Invalidate();
    }
}
