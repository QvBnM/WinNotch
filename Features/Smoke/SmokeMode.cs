using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace WinNotch.Features.Smoke
{
    public enum SmokeCommandKind { VolumeAlert, TrackAlert, ToggleFeature, PersistentActivity, BurstActivity, LowActivity, DismissActivities, OpenCommandBar, FakeContext, SetContextPage, FakeMeeting }

    /// <summary>One line of smoke-commands.txt, checked.</summary>
    public sealed class SmokeCommand
    {
        public SmokeCommandKind Kind { get; init; }
        /// <summary>Feature id for <see cref="SmokeCommandKind.ToggleFeature"/>.</summary>
        public string Argument { get; init; } = "";
        /// <summary>Which persistent activity (1–3) or how many alerts in the burst (1–10).</summary>
        public int Number { get; init; }
        // P20: for FakeMeeting, Argument is the playback device ("Headphones"…; "" = back to the real context)
        /// <summary>P27: the page id for <see cref="SmokeCommandKind.SetContextPage"/> ("" = „—”).</summary>
        public string Page { get; init; } = "";
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
        /// <summary>P14: UI Automation id of the Command Bar's text box (exists only while the bar is open).</summary>
        public const string CommandBoxAutomationId = "WinNotchCommandBox";
        /// <summary>P14: UI Automation id of a result row = this prefix + the action id; the selected row's ItemStatus is "selected".</summary>
        public const string CommandResultAutomationPrefix = "WinNotchCommandResult:";
        /// <summary>P20: UI Automation id of a Quick Actions button = this prefix + the action id ("qa-audio.mute-mic").</summary>
        public const string QuickActionAutomationPrefix = "qa-";
        /// <summary>P20: „Nu mai arăta” for a rule = this prefix + the rule id ("qa-hide-meeting-headphones").</summary>
        public const string QuickActionHideAutomationPrefix = "qa-hide-";
        /// <summary>P20: the row of Quick Actions buttons under the open notch's content.</summary>
        public const string QuickActionsRowAutomationId = "qa-row";
        /// <summary>P20 (R1): the last click's result at the end of the row (its Name is the text; only while shown).</summary>
        public const string QuickActionMessageAutomationId = "qa-message";
        /// <summary>Bigger files are ignored (and deleted): the commands are a few short lines.</summary>
        public const int MaxFileBytes = 4096;
        public const int MaxLines = 20;

        /// <summary>Set once, first thing at startup, from the command line.</summary>
        public static bool On { get; private set; }

        public static void Init(IEnumerable<string> args) => On = args?.Contains(Arg, StringComparer.Ordinal) ?? false;

        private static readonly Regex FeatureId = new Regex("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.CultureInvariant);

        public const int MaxPersistent = 3, MaxBurst = 10;

        /// <summary>
        /// P27: the context categories the test commands accept, lowercase → the name of Core.Context.AppCategory (this file
        /// is also compiled into the smoke project, which doesn't have the engine; the tests check the names match).
        /// </summary>
        public static readonly IReadOnlyDictionary<string, string> ContextCategories = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["dev"] = "Dev", ["browser"] = "Browser", ["meeting"] = "Meeting", ["game"] = "Game", ["media"] = "Media", ["office"] = "Office", ["creator"] = "Creator",
        };

        /// <summary>
        /// P20: the playback devices "fake-meeting" accepts, lowercase → the name of Core.Context.AudioOutputKind (the tests
        /// check the names match).
        /// </summary>
        public static readonly IReadOnlyDictionary<string, string> MeetingOutputs = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["headphones"] = "Headphones", ["speakers"] = "Speakers", ["bluetooth"] = "Bluetooth",
        };

        /// <summary>A page id in a test command or in the status: "home", "devices" or a page of yours (32 hex digits).</summary>
        private static readonly Regex PageId = new Regex("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.CultureInvariant);
        private static readonly Regex StatusText = new Regex("^[A-Za-z0-9-]{1,40}$", RegexOptions.CultureInvariant);

        /// <summary>
        /// "post-alert volume" (or "volum"), "post-alert track" (or "piesa", "piesă"), "toggle feature &lt;id&gt;", and for the
        /// Activity Manager (P13): "post-activity persistent &lt;1–3&gt;", "post-activity burst &lt;1–10&gt;", "post-activity low",
        /// "dismiss-activities", for the Command Bar (P14): "open-command-bar" (the same code path as its shortcut), and for the
        /// page by context (P27): "fake-context &lt;category|none&gt;" (into the context engine's snapshot) and
        /// "set-context-page &lt;category&gt; &lt;page id|none&gt;", and for Quick Actions (P20): "fake-meeting
        /// &lt;headphones|speakers|bluetooth|none&gt;" (a meeting with that output, into the context engine's snapshot). Case and
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
            if (w.Length == 3 && w[0] == "post-activity" && w[2].Length <= 2 && w[2].All(char.IsAsciiDigit) && int.TryParse(w[2], out int n))
            {
                if (w[1] == "persistent" && n >= 1 && n <= MaxPersistent) return new SmokeCommand { Kind = SmokeCommandKind.PersistentActivity, Number = n };
                if (w[1] == "burst" && n >= 1 && n <= MaxBurst) return new SmokeCommand { Kind = SmokeCommandKind.BurstActivity, Number = n };
                return null;
            }
            if (w.Length == 2 && w[0] == "post-activity" && w[1] == "low") return new SmokeCommand { Kind = SmokeCommandKind.LowActivity };
            if (w.Length == 1 && w[0] == "dismiss-activities") return new SmokeCommand { Kind = SmokeCommandKind.DismissActivities };
            if (w.Length == 1 && w[0] == "open-command-bar") return new SmokeCommand { Kind = SmokeCommandKind.OpenCommandBar };
            if (w.Length == 2 && w[0] == "fake-context")
            {
                if (w[1] == "none") return new SmokeCommand { Kind = SmokeCommandKind.FakeContext };
                return ContextCategories.TryGetValue(w[1], out var cat) ? new SmokeCommand { Kind = SmokeCommandKind.FakeContext, Argument = cat } : null;
            }
            if (w.Length == 2 && w[0] == "fake-meeting")
            {
                if (w[1] == "none") return new SmokeCommand { Kind = SmokeCommandKind.FakeMeeting };
                return MeetingOutputs.TryGetValue(w[1], out var output) ? new SmokeCommand { Kind = SmokeCommandKind.FakeMeeting, Argument = output } : null;
            }
            if (w.Length == 3 && w[0] == "set-context-page" && ContextCategories.TryGetValue(w[1], out var category))
            {
                if (w[2] == "none") return new SmokeCommand { Kind = SmokeCommandKind.SetContextPage, Argument = category };
                return w[2].Length <= 40 && PageId.IsMatch(w[2]) ? new SmokeCommand { Kind = SmokeCommandKind.SetContextPage, Argument = category, Page = w[2] } : null;
            }
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
        /// ";group=5" („5 noutăți”), ";peek=1" (a Low activity); with the Command Bar (P14) open, ";cmd=1". P27 adds, at the
        /// end, the page shown in the notch (";page=home") and the category in the context engine's snapshot (";ctx=Dev");
        /// a value that isn't a plain id is left out. P20 appends <see cref="QuickActionsStatus"/>.
        /// </summary>
        public static string Status(string mode, double pillWidth, double pillHeight, int split = 0, int group = 0, int peek = 0, int cmd = 0,
                                    string page = null, string ctx = null) =>
            "mode=" + mode + ";pill=" + Math.Round(pillWidth) + "x" + Math.Round(pillHeight) +
            (split > 0 ? ";split=" + split : "") + (group > 0 ? ";group=" + group : "") + (peek > 0 ? ";peek=" + peek : "") + (cmd > 0 ? ";cmd=" + cmd : "") +
            (page != null && StatusText.IsMatch(page) ? ";page=" + page : "") + (ctx != null && StatusText.IsMatch(ctx) ? ";ctx=" + ctx : "");

        /// <summary>
        /// P20, at the very end of the status: how many Quick Actions buttons ran through the registry with success (";qa=2")
        /// and how many unasked suggestions were posted (";qs=1"); zero ones are left out.
        /// </summary>
        public static string QuickActionsStatus(int qa, int qs) =>
            (qa > 0 ? ";qa=" + Math.Min(qa, 9999) : "") + (qs > 0 ? ";qs=" + Math.Min(qs, 9999) : "");

        /// <summary>Reads <see cref="Status"/> back: false if it isn't one.</summary>
        public static bool TryParseStatus(string status, out string mode, out int width, out int height) =>
            TryParseStatus(status, out mode, out width, out height, out _);

        /// <summary>Reads <see cref="Status"/> back, with the activity fields (missing ones are 0).</summary>
        public static bool TryParseStatus(string status, out string mode, out int width, out int height, out IReadOnlyDictionary<string, int> extra) =>
            TryParseStatus(status, out mode, out width, out height, out extra, out _, out _);

        /// <summary>Reads <see cref="Status"/> back, with the P27 fields too (missing ones are "").</summary>
        public static bool TryParseStatus(string status, out string mode, out int width, out int height, out IReadOnlyDictionary<string, int> extra,
                                          out string page, out string ctx)
        {
            mode = ""; width = height = 0; page = ""; ctx = "";
            var fields = new Dictionary<string, int>(StringComparer.Ordinal) { ["split"] = 0, ["group"] = 0, ["peek"] = 0, ["cmd"] = 0, ["qa"] = 0, ["qs"] = 0 };
            extra = fields;
            var m = Regex.Match(status ?? "", @"^mode=(\w+);pill=(\d+)x(\d+)((?:;(?:split|group|peek|cmd)=\d{1,4})*)(;page=[A-Za-z0-9-]{1,40})?(;ctx=[A-Za-z0-9-]{1,40})?((?:;(?:qa|qs)=\d{1,4})*)$");
            if (!m.Success) return false;
            mode = m.Groups[1].Value;
            foreach (Match f in Regex.Matches(m.Groups[4].Value + m.Groups[7].Value, @";(\w+)=(\d+)")) fields[f.Groups[1].Value] = int.Parse(f.Groups[2].Value);
            if (m.Groups[5].Success) page = m.Groups[5].Value.Substring(";page=".Length);
            if (m.Groups[6].Success) ctx = m.Groups[6].Value.Substring(";ctx=".Length);
            return int.TryParse(m.Groups[2].Value, out width) && int.TryParse(m.Groups[3].Value, out height);
        }
    }
}
