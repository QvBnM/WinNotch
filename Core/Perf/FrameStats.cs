using System;
using System.Collections.Generic;

namespace WinNotch.Core.Perf
{
    /// <summary>
    /// P60: the frame-time maths, on its own and testable, so the numbers mean the same thing wherever they are shown.
    /// It takes nothing but a list of frame times in milliseconds (where they come from — ETW, in a later step — is not this file's problem), and it refuses to invent a number it cannot support.
    /// <para><b>What "1% low" means here:</b> the average frame rate over the slowest 1% of frames — take the slowest
    /// <c>n/100</c> frames, add up the time they took, divide the count by it. That is the definition CapFrameX and
    /// the benchmarking crowd use, and it is the useful one: it answers "how bad are the worst moments", while a plain
    /// 99th-percentile value answers "how bad is one single frame". Both are reported, so neither can be mistaken for
    /// the other.</para>
    /// <para>Below a certain number of frames a low is meaningless (1% of 50 frames is half a frame), so the stats say
    /// <see cref="HasP1"/> / <see cref="HasP01"/> and the UI shows "—" instead of a made-up figure.</para>
    /// </summary>
    public sealed class FrameStats
    {
        /// <summary>At least this many frames must fall inside a low for it to be reported.</summary>
        public const int MinFramesInLow = 5;
        /// <summary>A frame longer than this is not a stutter, it is a pause (loading, alt-tab): counted apart.</summary>
        public const double PauseMs = 2000;
        /// <summary>A frame is a stutter when it takes this many times the median...</summary>
        public const double StutterFactor = 2.0;
        /// <summary>...and at least this many milliseconds more than it, so a spike at 500 fps is not called a stutter.</summary>
        public const double StutterFloorMs = 8;

        /// <summary>Under this many frames the median is not steady enough to call anything a stutter.</summary>
        public const int MinFramesForStutter = 20;

        public static readonly FrameStats Empty = new FrameStats(Array.Empty<double>(), 0, 0);

        private readonly double[] _sorted;

        private FrameStats(double[] sortedFrameTimes, int pauses, double pausedMs)
        {
            _sorted = sortedFrameTimes;
            Pauses = pauses;
            PausedMs = pausedMs;
            double total = 0;
            for (int i = 0; i < _sorted.Length; i++) total += _sorted[i];
            TotalMs = total;
        }

        /// <summary>Builds the stats from frame times in milliseconds, in any order.</summary>
        public static FrameStats From(IReadOnlyList<double> frameTimesMs)
        {
            if (frameTimesMs == null || frameTimesMs.Count == 0) return Empty;
            var kept = new List<double>(frameTimesMs.Count);
            int pauses = 0;
            double pausedMs = 0;
            for (int i = 0; i < frameTimesMs.Count; i++)
            {
                double ms = frameTimesMs[i];
                if (double.IsNaN(ms) || double.IsInfinity(ms) || ms <= 0) continue;
                if (ms > PauseMs) { pauses++; pausedMs += ms; continue; }
                kept.Add(ms);
            }
            var arr = kept.ToArray();
            Array.Sort(arr);
            return new FrameStats(arr, pauses, pausedMs);
        }

        /// <summary>Frames that counted (pauses left out).</summary>
        public int Frames => _sorted.Length;
        /// <summary>Frames longer than <see cref="PauseMs"/>: a loading screen or an alt-tab, not a stutter.</summary>
        public int Pauses { get; }
        /// <summary>How long the pauses took together, in milliseconds. Kept out of the frame rate, counted in the wall clock.</summary>
        public double PausedMs { get; }
        /// <summary>How long the frames that counted took together, in milliseconds (pauses left out).</summary>
        public double TotalMs { get; }
        /// <summary>Seconds of actual rendering — the denominator of the frame rate.</summary>
        public double Seconds => TotalMs / 1000.0;
        /// <summary>
        /// Seconds from the first frame to the last, pauses included. The denominator for anything "per minute":
        /// a session with two minutes of loading screens did last those two minutes.
        /// </summary>
        public double ElapsedSeconds => (TotalMs + PausedMs) / 1000.0;

        /// <summary>Frames divided by the time they took — the only honest average frame rate.</summary>
        public double AvgFps => TotalMs > 0 ? Frames * 1000.0 / TotalMs : 0;
        public double AvgFrameMs => Frames > 0 ? TotalMs / Frames : 0;
        public double MedianFrameMs => Frames == 0 ? 0 : _sorted[Frames / 2];
        public double MaxFrameMs => Frames == 0 ? 0 : _sorted[Frames - 1];
        public double MinFrameMs => Frames == 0 ? 0 : _sorted[0];

        /// <summary>Enough frames for a 1% low to mean something.</summary>
        public bool HasP1 => FramesIn(1) >= MinFramesInLow;
        /// <summary>Enough frames for a 0.1% low to mean something (tens of thousands: a whole session, not a second).</summary>
        public bool HasP01 => FramesIn(0.1) >= MinFramesInLow;

        /// <summary>The 1% low in frames per second, or 0 when there are too few frames (<see cref="HasP1"/>).</summary>
        public double P1Low => HasP1 ? LowFps(1) : 0;
        /// <summary>The 0.1% low in frames per second, or 0 when there are too few frames (<see cref="HasP01"/>).</summary>
        public double P01Low => HasP01 ? LowFps(0.1) : 0;

        /// <summary>How many frames fall inside the slowest <paramref name="percent"/>% (rounded down, so it never over-claims).</summary>
        public int FramesIn(double percent) => percent <= 0 ? 0 : (int)Math.Floor(Frames * percent / 100.0);

        /// <summary>
        /// The average frame rate over the slowest <paramref name="percent"/>% of frames. Returns 0 when that is fewer
        /// than one frame; callers that show a number must check <see cref="HasP1"/> / <see cref="HasP01"/> first.
        /// </summary>
        public double LowFps(double percent)
        {
            int k = FramesIn(percent);
            if (k < 1) return 0;
            double ms = 0;
            for (int i = Frames - k; i < Frames; i++) ms += _sorted[i];
            return ms > 0 ? k * 1000.0 / ms : 0;
        }

        /// <summary>The frame time this share of frames stayed under, in milliseconds (the plain percentile).</summary>
        public double FrameMsPercentile(double percent)
        {
            if (Frames == 0) return 0;
            double p = Math.Clamp(percent, 0, 100);
            int idx = (int)Math.Round((Frames - 1) * p / 100.0);
            return _sorted[Math.Clamp(idx, 0, Frames - 1)];
        }

        /// <summary>
        /// Frames that took over twice the median (and at least 8 ms more): the ones you actually feel. Under
        /// <see cref="MinFramesForStutter"/> frames this is 0, because the median of a handful of frames is not
        /// steady enough to measure anything against.
        /// </summary>
        public int Stutters
        {
            get
            {
                if (Frames < MinFramesForStutter) return 0;
                double limit = Math.Max(MedianFrameMs * StutterFactor, MedianFrameMs + StutterFloorMs);
                int n = 0;
                for (int i = Frames - 1; i >= 0 && _sorted[i] > limit; i--) n++;
                return n;
            }
        }

        /// <summary>
        /// Stutters per minute of play — comparable between a 5-minute and a 2-hour session. Measured against the
        /// wall clock (<see cref="ElapsedSeconds"/>), so loading screens do not inflate the rate.
        /// </summary>
        public double StuttersPerMinute => ElapsedSeconds > 0 ? Stutters / (ElapsedSeconds / 60.0) : 0;
    }
}
