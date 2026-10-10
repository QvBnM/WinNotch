using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace WinNotch.Core.Perf
{
    /// <summary>One background program that took a real share of the machine while the game ran.</summary>
    public readonly struct GameBlame
    {
        public GameBlame(string name, double cpuPercent, double gpuPercent)
        {
            Name = name ?? ""; CpuPercent = cpuPercent; GpuPercent = gpuPercent;
        }

        public string Name { get; }
        /// <summary>Its average share of the CPU over the session, 0..100 of the whole machine.</summary>
        public double CpuPercent { get; }
        public double GpuPercent { get; }
    }

    /// <summary>
    /// P61: what one game session came to. Immutable, and the only thing the UI, the log and the file on disk ever
    /// read — so a number shown in the notch, a number in the report file and a number in the Performanță tab cannot
    /// disagree.
    /// <para>The frame-rate fields are here from the start, empty until P62 brings them (ETW, in the SYSTEM helper).
    /// A reader must check <see cref="HasFps"/>: a report without frames shows "—", never a zero.</para>
    /// </summary>
    public sealed record GameReport
    {
        public static readonly GameReport Empty = new GameReport();

        /// <summary>Process name of the game, lowercase, without ".exe". Written to the report file, never to log.txt.</summary>
        public string Process { get; init; } = "";
        public DateTime StartedUtc { get; init; }
        public TimeSpan Duration { get; init; }
        /// <summary>How many samples the averages rest on. Zero means nothing was measured.</summary>
        public int Samples { get; init; }

        public double AvgCpu { get; init; }
        public double MaxCpu { get; init; }
        /// <summary>Average GPU load, 0..100; -1 when the counters never answered.</summary>
        public double AvgGpu { get; init; } = -1;
        public double MaxGpu { get; init; } = -1;

        /// <summary>Hottest the CPU got, °C; -1 when temperatures were off or unavailable.</summary>
        public double MaxCpuTempC { get; init; } = -1;
        public double MaxGpuTempC { get; init; } = -1;

        public double AvgRamGb { get; init; }
        public double PeakRamGb { get; init; }
        /// <summary>Most dedicated video memory in use, MB; -1 when unknown.</summary>
        public double PeakVramMb { get; init; } = -1;

        /// <summary>Frames per second over the session; 0 until P62. Check <see cref="HasFps"/> first.</summary>
        public double AvgFps { get; init; }
        public double P1LowFps { get; init; }
        public int Stutters { get; init; }

        /// <summary>What else was using the machine, biggest first.</summary>
        public IReadOnlyList<GameBlame> Blame { get => _blame; init => _blame = value ?? Array.Empty<GameBlame>(); }
        private readonly IReadOnlyList<GameBlame> _blame = Array.Empty<GameBlame>();

        public bool HasFps => AvgFps > 0;
        public bool HasGpu => AvgGpu >= 0;
        public bool HasTemps => MaxCpuTempC > 0 || MaxGpuTempC > 0;
        public bool Measured => Samples > 0;

        // ------------------------------------------------------------------ the words the user reads

        /// <summary>"CS2 · 1 h 24 min". The one line that fits a pill.</summary>
        public string Headline() => (Process.Length > 0 ? Process : "Joc") + " · " + Spell(Duration);

        /// <summary>"1 h 24 min", "7 min", "48 s" — Romanian, short, no false precision.</summary>
        public static string Spell(TimeSpan d)
        {
            if (d.TotalSeconds < 60) return Math.Max(0, (int)Math.Round(d.TotalSeconds)) + " s";
            int hours = (int)d.TotalHours, minutes = d.Minutes;
            if (hours <= 0) return minutes + " min";
            return hours + " h" + (minutes > 0 ? " " + minutes + " min" : "");
        }

        /// <summary>
        /// The body of the report, one short line each, in the order that answers "how did it go": the load, then how
        /// hot it got, then memory, then who else was eating the machine. Lines with nothing to say are left out
        /// rather than filled with zeros.
        /// </summary>
        public IReadOnlyList<string> Lines()
        {
            var lines = new List<string>(5);
            if (!Measured) { lines.Add("Nu am măsurat nimic în sesiunea asta."); return lines; }

            if (HasFps)
                lines.Add("FPS " + R(AvgFps) + " mediu, " + R(P1LowFps) + " la 1% low" + (Stutters > 0 ? ", " + Stutters + " sacadări" : ""));

            lines.Add("Procesor " + R(AvgCpu) + "% mediu, " + R(MaxCpu) + "% maxim" +
                      (HasGpu ? " · placă video " + R(AvgGpu) + "% mediu, " + R(MaxGpu) + "% maxim" : ""));

            if (HasTemps)
            {
                var parts = new List<string>(2);
                if (MaxCpuTempC > 0) parts.Add("procesor " + R(MaxCpuTempC) + "°C");
                if (MaxGpuTempC > 0) parts.Add("placă video " + R(MaxGpuTempC) + "°C");
                lines.Add("Cel mai cald: " + string.Join(", ", parts));
            }

            lines.Add("Memorie " + G(AvgRamGb) + " GB mediu, " + G(PeakRamGb) + " GB la vârf" +
                      (PeakVramMb >= 0 ? " · video " + R(PeakVramMb) + " MB la vârf" : ""));

            if (_blame.Count > 0)
            {
                var who = _blame.Take(3).Select(b =>
                    b.Name + " (" + (b.GpuPercent > b.CpuPercent
                        ? R(b.GpuPercent) + "% placă video"
                        : R(b.CpuPercent) + "% procesor") + ")");
                lines.Add("Din fundal îți luau: " + string.Join(", ", who));
            }
            else lines.Add("Nimic din fundal nu ți-a luat resurse măsurabil.");

            return lines;
        }

        /// <summary>
        /// For the log: the game's name is <b>not</b> written. Numbers only, so a line can be traced without saying
        /// what the user played.
        /// </summary>
        public string ToLogString() =>
            "sesiune de joc " + Spell(Duration) + ", " + Samples + " măsurători, cpu " + R(AvgCpu) + "/" + R(MaxCpu) + "%" +
            (HasGpu ? ", gpu " + R(AvgGpu) + "/" + R(MaxGpu) + "%" : "") +
            ", ram " + G(PeakRamGb) + " GB vârf" +
            (HasTemps ? ", max " + R(Math.Max(MaxCpuTempC, MaxGpuTempC)) + "°C" : "");

        /// <summary>Away from zero, not to-even: 34,5% shown as 34% reads like a bug to the person looking at it.</summary>
        private static string R(double v) => Math.Round(v, 0, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture);
        private static string G(double v) => v.ToString("0.0", CultureInfo.InvariantCulture);
    }
}
