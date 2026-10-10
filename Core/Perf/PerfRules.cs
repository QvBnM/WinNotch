using System;

namespace WinNotch.Core.Perf
{
    /// <summary>How fast the sampler runs. Nothing here is a guess: each step has a reason, below.</summary>
    public enum PerfCadence
    {
        /// <summary>No timer at all: the feature is off, or nothing is looking and no game runs.</summary>
        Off,
        /// <summary>Something shows the numbers (the Performanță tab, a widget, the notch page).</summary>
        Visible,
        /// <summary>A game is running: the only case where a faster rate is allowed.</summary>
        Game,
    }

    /// <summary>
    /// P60: when and how often performance is measured — pure rules, no WPF, no Windows calls.
    /// <para>The rule from <c>CLAUDE.md</c> is "no polling under 2 seconds in standby", and this is where it is kept
    /// honest: with nothing on screen and no game running the cadence is <see cref="PerfCadence.Off"/>, which means no
    /// timer exists at all, not a slow one. Two seconds is the floor everywhere except inside a game, and even there
    /// the expensive half (walking every process) stays slow, because a game needs the CPU more than we do.</para>
    /// </summary>
    public static class PerfRules
    {
        /// <summary>Same id as the entry in FeatureCatalog (the tests check they match).</summary>
        public const string FeatureId = "perf-monitor";

        /// <summary>Cheap samples (CPU, RAM, commit, GPU totals) while someone is looking at them.</summary>
        public const int VisibleSeconds = 2;
        /// <summary>Cheap samples while a game runs. The only rate under two seconds in the whole app.</summary>
        public const int GameSeconds = 1;
        /// <summary>
        /// The expensive half — every process, its CPU time, its memory, its share of the GPU — never runs faster than
        /// this, whatever the cadence. Walking ~300 processes costs tens of milliseconds; inside a game that matters.
        /// </summary>
        public const int ProcessSeconds = 4;

        /// <summary>How long a sample stays interesting: an hour of history is enough to see a leak and cheap to keep.</summary>
        public const int HistoryMinutes = 60;

        /// <summary>The cadence for a given state. A game wins over a visible window: it is the stricter measurement.</summary>
        public static PerfCadence Pick(bool enabled, bool visible, bool gameRunning)
        {
            if (!enabled) return PerfCadence.Off;
            if (gameRunning) return PerfCadence.Game;
            return visible ? PerfCadence.Visible : PerfCadence.Off;
        }

        /// <summary>Seconds between cheap samples; 0 means "no timer" (see <see cref="PerfCadence.Off"/>).</summary>
        public static int SecondsFor(PerfCadence c) => c switch
        {
            PerfCadence.Game => GameSeconds,
            PerfCadence.Visible => VisibleSeconds,
            _ => 0,
        };

        /// <summary>
        /// Is this cadence allowed to exist while the user is doing nothing with us? Only a game earns a rate under two
        /// seconds, so a cadence of <see cref="PerfCadence.Game"/> without a game running is a bug, not a setting.
        /// </summary>
        public static bool AllowedInStandby(PerfCadence c, bool gameRunning) =>
            c == PerfCadence.Off || (SecondsFor(c) >= 2) || gameRunning;

        /// <summary>How many cheap samples fit in <see cref="HistoryMinutes"/> at the fastest rate (the ring's size).</summary>
        public static int HistoryCapacity => HistoryMinutes * 60 / GameSeconds;

        /// <summary>Every how many cheap samples the expensive per-process pass runs, at this cadence.</summary>
        public static int ProcessEvery(PerfCadence c)
        {
            int s = SecondsFor(c);
            if (s <= 0) return 0;
            return Math.Max(1, (int)Math.Ceiling(ProcessSeconds / (double)s));
        }
    }
}
