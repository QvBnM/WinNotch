using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace WinNotch.Features.Smoke
{
    public enum SmokeCommandKind { VolumeAlert, TrackAlert, ToggleFeature }

    /// <summary>One line of smoke-commands.txt, checked.</summary>
    public sealed class SmokeCommand
    {
        public SmokeCommandKind Kind { get; init; }
        /// <summary>Feature id for <see cref="SmokeCommandKind.ToggleFeature"/>.</summary>
        public string Argument { get; init; } = "";
    }

    /// <summary>
    /// The smoke-test mode (WinNotch.exe --smoke, used by tests/WinNotch.Smoke in CI): its own data folder
    /// (%AppData%\WinNotch\smoke, so the real settings, log and startup records are never touched), no update checks, no
    /// temperature service, no message boxes, and a few test commands read from smoke-commands.txt in that folder.
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

        /// <summary>
        /// "post-alert volume" (or "volum"), "post-alert track" (or "piesa", "piesă"), "toggle feature &lt;id&gt;". Case and
        /// extra spaces don't matter; anything else is null (ignored).
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

        /// <summary>"mode=Live;pill=360x54": what the smoke test reads from the notch window (UI Automation ItemStatus).</summary>
        public static string Status(string mode, double pillWidth, double pillHeight) =>
            "mode=" + mode + ";pill=" + Math.Round(pillWidth) + "x" + Math.Round(pillHeight);

        /// <summary>Reads <see cref="Status"/> back: false if it isn't one.</summary>
        public static bool TryParseStatus(string status, out string mode, out int width, out int height)
        {
            mode = ""; width = height = 0;
            var m = Regex.Match(status ?? "", @"^mode=(\w+);pill=(\d+)x(\d+)$");
            if (!m.Success) return false;
            mode = m.Groups[1].Value;
            return int.TryParse(m.Groups[2].Value, out width) && int.TryParse(m.Groups[3].Value, out height);
        }
    }
}
