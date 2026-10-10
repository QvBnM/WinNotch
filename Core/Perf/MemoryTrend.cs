using System;
using System.Collections.Generic;

namespace WinNotch.Core.Perf
{
    /// <summary>What the trend of one process's memory says about it.</summary>
    public enum MemoryVerdict
    {
        /// <summary>Not enough time watched, or the numbers move both ways: no claim.</summary>
        Unknown,
        /// <summary>Flat, or growing slowly enough to be normal use.</summary>
        Steady,
        /// <summary>Growing, straight and fast, for long enough that it is worth telling the user.</summary>
        Growing,
    }

    /// <summary>One process's memory over time: the slope, how straight it is, and the verdict.</summary>
    public readonly struct MemoryTrendResult
    {
        public MemoryTrendResult(MemoryVerdict verdict, double mbPerHour, double fit, double minutes, double growthMb, int points)
        {
            Verdict = verdict; MbPerHour = mbPerHour; Fit = fit; Minutes = minutes; GrowthMb = growthMb; Points = points;
        }

        public MemoryVerdict Verdict { get; }
        /// <summary>How fast it grows, MB per hour (negative when it shrinks).</summary>
        public double MbPerHour { get; }
        /// <summary>How well a straight line fits, 0..1 (R²). A leak is straight; normal use is not.</summary>
        public double Fit { get; }
        /// <summary>How long the watched stretch is, in minutes.</summary>
        public double Minutes { get; }
        /// <summary>Last value minus first, in MB.</summary>
        public double GrowthMb { get; }
        public int Points { get; }
    }

    /// <summary>
    /// P60: is something leaking? Pure maths over (minute, megabyte) points — a least-squares line plus how well it
    /// fits — and nothing else.
    /// <para>This is the one honest memory feature. "Freeing RAM" by trimming working sets moves pages to the pagefile
    /// and they come straight back; on a 32 GB machine it buys nothing and costs stutter. Finding the process that has
    /// grown 200 MB an hour for three hours, on the other hand, is a real answer with a real number behind it, and the
    /// user can act on it (restart that app) with an effect they can see here afterwards.</para>
    /// <para>The thresholds are deliberately shy: a straight line (<see cref="MinFit"/>), over at least
    /// <see cref="MinMinutes"/>, at over <see cref="MinMbPerHour"/>, having actually grown <see cref="MinGrowthMb"/>.
    /// A browser that opens ten tabs grows fast but not straight; a service that leaks grows straight and slow.</para>
    /// </summary>
    public static class MemoryTrend
    {
        /// <summary>R² over this means the growth is a line, not the shape of someone using an app.</summary>
        public const double MinFit = 0.80;
        /// <summary>Under this much watched time, no claim at all.</summary>
        public const double MinMinutes = 20;
        public const double MinMbPerHour = 50;
        /// <summary>It must also have grown this much in total, so 60 MB/h noticed over 20 minutes stays quiet.</summary>
        public const double MinGrowthMb = 150;
        public const int MinPoints = 8;

        /// <summary>
        /// The trend of one series. <paramref name="points"/> is (minutes since the first sample, megabytes), oldest
        /// first; the time axis is in minutes so the slope is readable in MB/h without a second conversion.
        /// </summary>
        public static MemoryTrendResult Of(IReadOnlyList<(double Minutes, double Mb)> points)
        {
            if (points == null || points.Count < 2) return new MemoryTrendResult(MemoryVerdict.Unknown, 0, 0, 0, 0, points?.Count ?? 0);

            int n = points.Count;
            double span = points[n - 1].Minutes - points[0].Minutes;
            double growth = points[n - 1].Mb - points[0].Mb;

            double sx = 0, sy = 0;
            for (int i = 0; i < n; i++) { sx += points[i].Minutes; sy += points[i].Mb; }
            double mx = sx / n, my = sy / n;

            double sxy = 0, sxx = 0, syy = 0;
            for (int i = 0; i < n; i++)
            {
                double dx = points[i].Minutes - mx, dy = points[i].Mb - my;
                sxy += dx * dy; sxx += dx * dx; syy += dy * dy;
            }
            if (sxx <= 0) return new MemoryTrendResult(MemoryVerdict.Unknown, 0, 0, span, growth, n);

            double slope = sxy / sxx;                       // MB per minute
            double mbPerHour = slope * 60;
            double fit = syy > 0 ? Math.Clamp(sxy * sxy / (sxx * syy), 0, 1) : 1;

            var verdict =
                n < MinPoints || span < MinMinutes ? MemoryVerdict.Unknown
                : mbPerHour >= MinMbPerHour && growth >= MinGrowthMb && fit >= MinFit ? MemoryVerdict.Growing
                : MemoryVerdict.Steady;

            return new MemoryTrendResult(verdict, mbPerHour, fit, span, growth, n);
        }

        /// <summary>How long until it has grown by <paramref name="mb"/> more, in minutes; -1 when it is not growing.</summary>
        public static double MinutesToGrow(MemoryTrendResult t, double mb) =>
            t.MbPerHour > 0 ? mb / t.MbPerHour * 60 : -1;
    }
}
