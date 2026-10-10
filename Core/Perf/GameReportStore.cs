using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace WinNotch.Core.Perf
{
    /// <summary>
    /// P61: the game reports on disk, one JSON object per line, in <c>game-sessions.jsonl</c> next to
    /// <c>settings.json</c>.
    /// <para><b>Why a file, and why with the game's name in it.</b> One report answers "how did that session go". The
    /// file answers the question that actually finds problems: "CS2 ran at 240 fps last month and 190 now, and the
    /// card is 9°C hotter". Without the name there is nothing to compare, so the name is here — and nowhere else:
    /// <c>log.txt</c> never gets it (<see cref="GameReport.ToLogString"/> leaves it out), and nothing is sent anywhere.
    /// The author agreed to exactly this trade.</para>
    /// <para>One line per session, appended; the file is trimmed to <see cref="MaxLines"/> the moment it grows past
    /// it, so it cannot creep. A line that cannot be parsed (a half-written line after a power cut, a hand edit) is
    /// skipped, never thrown.</para>
    /// </summary>
    public sealed class GameReportStore
    {
        /// <summary>Sessions kept. At one evening a day this is years of history in a few hundred kilobytes.</summary>
        public const int MaxLines = 500;
        /// <summary>A file bigger than this is not read: something else wrote it.</summary>
        public const long MaxFile = 2 * 1024 * 1024;
        public const string FileName = "game-sessions.jsonl";

        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { WriteIndented = false };

        private readonly string _folder;
        private readonly Action<string> _log;

        public GameReportStore(string folder, Action<string> log = null)
        {
            _folder = folder;
            _log = log;
        }

        public string Path => System.IO.Path.Combine(_folder, FileName);

        /// <summary>Appends one session. Never throws: a report that cannot be saved is a line in the log, not a crash.</summary>
        public bool Append(GameReport report)
        {
            if (report == null || !report.Measured) return false;
            try
            {
                Directory.CreateDirectory(_folder);
                using var hold = AppSettings.HoldFolder();
                if (!AppSettings.SafeToWrite(Path)) throw new IOException("scriere refuzată");
                File.AppendAllText(Path, JsonSerializer.Serialize(Row.From(report), Json) + Environment.NewLine, Encoding.UTF8);
                Trim();
                return true;
            }
            catch (Exception ex) { _log?.Invoke("Raportul de joc, salvarea: " + ex.GetType().Name); return false; }
        }

        /// <summary>The last <paramref name="count"/> sessions, newest first. Empty when there is no file yet.</summary>
        public List<GameReport> Last(int count = 20)
        {
            var list = new List<GameReport>();
            try
            {
                if (!File.Exists(Path) || new FileInfo(Path).Length > MaxFile) return list;
                var lines = File.ReadAllLines(Path);
                // Walked backwards by index: newest first, and nothing is allocated to reverse it.
                for (int i = lines.Length - 1; i >= 0 && list.Count < Math.Max(1, count); i--)
                {
                    var row = Parse(lines[i]);
                    if (row != null) list.Add(row);
                }
            }
            catch (Exception ex) { _log?.Invoke("Raportul de joc, citirea: " + ex.GetType().Name); }
            return list;
        }

        /// <summary>The newest session, or null when there is none.</summary>
        public GameReport Latest() => Last(1).FirstOrDefault();

        /// <summary>
        /// Earlier sessions of the same game, newest first — what a comparison over time is built on.
        /// </summary>
        public List<GameReport> For(string process, int count = 10)
        {
            string name = RawProc.Normalize(process);
            return Last(MaxLines).Where(r => string.Equals(r.Process, name, StringComparison.Ordinal)).Take(Math.Max(1, count)).ToList();
        }

        private static GameReport Parse(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return null;
            try { return JsonSerializer.Deserialize<Row>(line)?.ToReport(); }
            catch { return null; }                      // a half-written or hand-edited line is skipped, not fatal
        }

        private void Trim()
        {
            try
            {
                // Same guard as Last(): a file this big was not written by us, and loading it whole to trim it would
                // be the one place that reads what the reader itself refuses.
                if (new FileInfo(Path).Length > MaxFile) return;
                var lines = File.ReadAllLines(Path);
                if (lines.Length <= MaxLines) return;
                string tmp = Path + ".tmp";
                if (!AppSettings.SafeToWrite(tmp)) return;
                File.WriteAllLines(tmp, lines.Skip(lines.Length - MaxLines), Encoding.UTF8);
                File.Move(tmp, Path, true);
            }
            catch (Exception ex) { _log?.Invoke("Raportul de joc, scurtarea: " + ex.GetType().Name); }
        }

        /// <summary>
        /// The shape on disk. Written out by hand rather than serialising <see cref="GameReport"/> itself, so the
        /// file format is a decision and not a side effect of a refactor: a field renamed in the record must be
        /// renamed here on purpose, and an old file still reads.
        /// </summary>
        internal sealed class Row
        {
            public string game { get; set; } = "";
            public string start { get; set; } = "";
            public int seconds { get; set; }
            public int samples { get; set; }
            public double cpuAvg { get; set; }
            public double cpuMax { get; set; }
            public double gpuAvg { get; set; } = -1;
            public double gpuMax { get; set; } = -1;
            public double cpuTempMax { get; set; } = -1;
            public double gpuTempMax { get; set; } = -1;
            public double ramAvg { get; set; }
            public double ramPeak { get; set; }
            public double vramPeak { get; set; } = -1;
            public double fpsAvg { get; set; }
            public double fpsLow1 { get; set; }
            public int stutters { get; set; }
            public List<string> blame { get; set; } = new List<string>();

            public static Row From(GameReport r) => new Row
            {
                game = r.Process,
                start = r.StartedUtc.ToString("o", System.Globalization.CultureInfo.InvariantCulture),
                seconds = (int)Math.Round(r.Duration.TotalSeconds),
                samples = r.Samples,
                cpuAvg = Round(r.AvgCpu), cpuMax = Round(r.MaxCpu),
                gpuAvg = Round(r.AvgGpu), gpuMax = Round(r.MaxGpu),
                cpuTempMax = Round(r.MaxCpuTempC), gpuTempMax = Round(r.MaxGpuTempC),
                ramAvg = Round(r.AvgRamGb, 2), ramPeak = Round(r.PeakRamGb, 2),
                vramPeak = Round(r.PeakVramMb),
                fpsAvg = Round(r.AvgFps), fpsLow1 = Round(r.P1LowFps), stutters = r.Stutters,
                // "name cpu gpu", so a person opening the file can read it without a tool
                blame = r.Blame.Take(5).Select(b => b.Name + " " + Round(b.CpuPercent) + " " + Round(b.GpuPercent)).ToList(),
            };

            public GameReport ToReport() => new GameReport
            {
                Process = RawProc.Normalize(game),
                StartedUtc = DateTime.TryParse(start, System.Globalization.CultureInfo.InvariantCulture,
                                               System.Globalization.DateTimeStyles.RoundtripKind, out var at) ? at : default,
                Duration = TimeSpan.FromSeconds(Math.Max(0, seconds)),
                Samples = Math.Max(0, samples),
                AvgCpu = cpuAvg, MaxCpu = cpuMax, AvgGpu = gpuAvg, MaxGpu = gpuMax,
                MaxCpuTempC = cpuTempMax, MaxGpuTempC = gpuTempMax,
                AvgRamGb = ramAvg, PeakRamGb = ramPeak, PeakVramMb = vramPeak,
                AvgFps = fpsAvg, P1LowFps = fpsLow1, Stutters = Math.Max(0, stutters),
                Blame = (blame ?? new List<string>()).Select(ParseBlame).Where(b => b.Name.Length > 0).ToList(),
            };

            private static GameBlame ParseBlame(string text)
            {
                var parts = (text ?? "").Split(' ');
                if (parts.Length < 3) return default;
                double.TryParse(parts[parts.Length - 2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double cpu);
                double.TryParse(parts[parts.Length - 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double gpu);
                return new GameBlame(string.Join(" ", parts.Take(parts.Length - 2)), cpu, gpu);
            }

            private static double Round(double v, int digits = 1) => Math.Round(v, digits, MidpointRounding.AwayFromZero);
        }
    }
}
