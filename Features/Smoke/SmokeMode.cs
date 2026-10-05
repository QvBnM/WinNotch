using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace WinNotch.Features.Smoke
{
    public enum SmokeCommandKind { VolumeAlert, TrackAlert, ToggleFeature, PersistentActivity, BurstActivity, LowActivity, DismissActivities }

    /// <summary>One line of smoke-commands.txt, checked.</summary>
    public sealed class SmokeCommand
    {
        public SmokeCommandKind Kind { get; init; }
        /// <summary>Feature id for <see cref="SmokeCommandKind.ToggleFeature"/>.</summary>
        public string Argument { get; init; } = "";
        /// <summary>Which persistent activity (1–3) or how many alerts in the burst (1–10).</summary>
        public int Number { get; init; }
    }

    /// <summary>
    /// The smoke-test mode (WinNotch.exe --smoke, used by tests/WinNotch.Smoke in CI): its own data folder
    /// (%AppData%\WinNotch\smoke, so the real settings, log and startup records are never touched), no update checks, no
    /// temperature service, no "already running" message box (exit code 3 instead), and a few test commands read from smoke-commands.txt in that folder.
    /// Without --smoke none of this exists: the commands file is never read.
    /// </summary>
    public static class SmokeMode
    {
        public const string Arg = "--smoke";
        /// <summary>Sub-folder of %AppData%\WinNotch used instead of it in smoke mode.</summary>
        public const string FolderName = "smoke";
        public const string CommandsFile = "smoke-commands.txt";
        /// <summary>UI Automation id of the notch window (the smoke test finds it by this).</summary>
        public const string NotchAutomationId = "WinNotchNotch";
        /// <summary>Bigger files are ignored (and deleted): the commands are a few short lines.</summary>
        public const int MaxFileBytes = 4096;
        public const int MaxLines = 20;

        /// <summary>Set once, first thing at startup, from the command line.</summary>
        public static bool On { get; private set; }

        public static void Init(IEnumerable<string> args) => On = args?.Contains(Arg, StringComparer.Ordinal) ?? false;

        private static readonly Regex FeatureId = new Regex("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.CultureInvariant);

        public const int MaxPersistent = 3, MaxBurst = 10;

        /// <summary>
        /// "post-alert volume" (or "volum"), "post-alert track" (or "piesa", "piesă"), "toggle feature &lt;id&gt;", and for the
        /// Activity Manager (P13): "post-activity persistent &lt;1–3&gt;", "post-activity burst &lt;1–10&gt;", "post-activity low",
        /// "dismiss-activities". Case and extra spaces don't matter; anything else is null (ignored).
        /// </summary>
        public static SmokeCommand Parse(string line)
        {
            if (string.IsNullOrWhiteSpace(line) || line.Length > 200) return null;
            var w = line.Trim().ToLowerInvariant().Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (w.Length == 2 && w[0] == "post-alert")
            {
                if (w[1] is "volume" or "volum") return new SmokeCommand { Kind = SmokeCommandKind.VolumeAlert };
                if (w[1] is "track" or "piesa" or "piesă") return new SmokeCommand { Kind = SmokeCommandKind.TrackAlert };
                return null;
            }
            if (w.Length == 3 && w[0] == "toggle" && w[1] == "feature" && w[2].Length <= 60 && FeatureId.IsMatch(w[2]))
                return new SmokeCommand { Kind = SmokeCommandKind.ToggleFeature, Argument = w[2] };
            if (w.Length == 3 && w[0] == "post-activity" && w[2].Length <= 2 && w[2].All(char.IsAsciiDigit) && int.TryParse(w[2], out int n))
            {
                if (w[1] == "persistent" && n >= 1 && n <= MaxPersistent) return new SmokeCommand { Kind = SmokeCommandKind.PersistentActivity, Number = n };
                if (w[1] == "burst" && n >= 1 && n <= MaxBurst) return new SmokeCommand { Kind = SmokeCommandKind.BurstActivity, Number = n };
                return null;
            }
            if (w.Length == 2 && w[0] == "post-activity" && w[1] == "low") return new SmokeCommand { Kind = SmokeCommandKind.LowActivity };
            if (w.Length == 1 && w[0] == "dismiss-activities") return new SmokeCommand { Kind = SmokeCommandKind.DismissActivities };
            return null;
        }

        /// <summary>The valid commands of a file's lines, in order (at most <see cref="MaxLines"/> lines are looked at).</summary>
        public static List<SmokeCommand> ParseAll(IEnumerable<string> lines)
        {
            var list = new List<SmokeCommand>();
            int n = 0;
            foreach (var l in lines ?? Array.Empty<string>())
            {
                if (++n > MaxLines) break;
                var c = Parse(l);
                if (c != null) list.Add(c);
            }
            return list;
        }

        /// <summary>
        /// Log lines that fail the smoke test: unhandled errors, failed starts, errors reported by features (also the ones
        /// FeatureFlags catches from a Changed handler, and an automatic switch-off), a failed test command, stack traces.
        /// Other lines naming a handled exception (a service missing on the CI machine) don't.
        /// </summary>
        public static bool IsFatalLogLine(string line)
        {
            if (string.IsNullOrEmpty(line)) return false;
            if (line.StartsWith("   at ", StringComparison.Ordinal)) return true;
            foreach (var f in FatalLogTexts) if (line.Contains(f, StringComparison.Ordinal)) return true;
            return false;
        }

        private static readonly string[] FatalLogTexts =
        {
            "Eroare neprevăzută", "Eroare fatală", "Pornirea a eșuat", "Eroare în funcția", "Eroare la schimbarea funcției",
            "a fost oprită automat", "Test de fum: comanda a dat eroare", "Exception:", "Unhandled",
        };

        /// <summary>
        /// "mode=Live;pill=360x54": what the smoke test reads from the notch window (UI Automation ItemStatus). With the
        /// Activity Manager (P13) it can go on with what the pill shows: ";split=1" (two persistent activities),
        /// ";group=5" („5 noutăți”), ";peek=1" (a Low activity).
        /// </summary>
        public static string Status(string mode, double pillWidth, double pillHeight, int split = 0, int group = 0, int peek = 0) =>
            "mode=" + mode + ";pill=" + Math.Round(pillWidth) + "x" + Math.Round(pillHeight) +
            (split > 0 ? ";split=" + split : "") + (group > 0 ? ";group=" + group : "") + (peek > 0 ? ";peek=" + peek : "");

        /// <summary>Reads <see cref="Status"/> back: false if it isn't one.</summary>
        public static bool TryParseStatus(string status, out string mode, out int width, out int height) =>
            TryParseStatus(status, out mode, out width, out height, out _);

        /// <summary>Reads <see cref="Status"/> back, with the activity fields (missing ones are 0).</summary>
        public static bool TryParseStatus(string status, out string mode, out int width, out int height, out IReadOnlyDictionary<string, int> extra)
        {
            mode = ""; width = height = 0;
            var fields = new Dictionary<string, int>(StringComparer.Ordinal) { ["split"] = 0, ["group"] = 0, ["peek"] = 0 };
            extra = fields;
            var m = Regex.Match(status ?? "", @"^mode=(\w+);pill=(\d+)x(\d+)((?:;(?:split|group|peek)=\d{1,4})*)$");
            if (!m.Success) return false;
            mode = m.Groups[1].Value;
            foreach (Match f in Regex.Matches(m.Groups[4].Value, @";(\w+)=(\d+)")) fields[f.Groups[1].Value] = int.Parse(f.Groups[2].Value);
            return int.TryParse(m.Groups[2].Value, out width) && int.TryParse(m.Groups[3].Value, out height);
        }
    }
}
