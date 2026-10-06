using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.Core.Definitions;
using FlaUI.Core.Exceptions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;
using WinNotch.Features.Smoke;

namespace WinNotch.Smoke
{
    /// <summary>
    /// Smoke tests (P02): the real, published WinNotch.exe started with --smoke and driven through UI Automation, the way a
    /// user would: notch window visible, alive for 30 s, alerts change the pill's size, a feature switch, Win+Alt+N, the
    /// WinNotch window from the tray menu, a clean "Ieșire" (exit code 0) and a log without exceptions.
    /// Usage: WinNotch.Smoke [path\to\WinNotch.exe] [artifacts folder] [--activity-manager=on|off]. Exit code 0 = all passed.
    /// With --activity-manager=on (P13) the "activity-manager" switch is turned on first and every check runs with the alerts
    /// going through the Activity Manager, plus the split pill, „N noutăți” and the peek; off (the default), the same checks
    /// on the old path and the Activity Manager's test commands must be refused. P14, on both runs: with "command-bar" off
    /// Win+Alt+Space opens nothing; switched on, the real shortcut opens the Command Bar over a window of the test (or, if
    /// the shortcut is taken on the machine or doesn't arrive, the "open-command-bar" test command, the same code path; the
    /// run says which), "volum" finds a result, an alert doesn't take the pill while it is open, Esc closes it and the
    /// keyboard goes back to the test window, and Enter on "settings.position" opens the WinNotch window. P27, once (only
    /// on the run with the Activity Manager off; the other run prints SKIP): a fake "Dev" context goes into the context
    /// engine's snapshot ("fake-context dev"), "Dev" is mapped to the "devices" page; with "context-pages" off Win+Alt+N
    /// opens on the old page, switched on it opens on "devices" (read from the notch's status, ";page=").
    /// P20, on both runs: a fake meeting with headphones goes into the context engine ("fake-meeting headphones"); with
    /// "quick-actions" off Win+Alt+N opens without buttons, switched on the row has "Microfon" and "Volum 40%" (UI Automation,
    /// "qa-&lt;action id&gt;"), a click on "Microfon" runs through the registry (twice: the microphone ends as it was); with the
    /// Activity Manager on also one suggestion (a peek, ";qs=1"), none more within 10 minutes, and „Nu mai arăta”.
    /// P21, once (only on the run with the Activity Manager off; the other run prints SKIP): a page with the Clipboard
    /// widget ("smoke-clipboard-page on"), a JSON copied by the test; with "smart-clipboard" off no chips, switched on the
    /// chip "Formatează" ("sc-clipboard.format-json"), a click through the registry, and the clipboard then holds the same
    /// JSON indented (the chips follow it: "Compactează", no loop); the JSON never reaches the log.
    /// P23, once (only on the run with the Activity Manager off; the other run prints SKIP): with "shelf" on, a local file
    /// made by the test goes through the drop's checks ("smoke-shelf-add"), shows in the shelf (UI Automation,
    /// "shelf-item-&lt;id&gt;"), „Copiază calea” runs through the registry and the clipboard holds exactly its path, „Golește”
    /// empties the shelf (the file stays); the path never reaches the log.
    /// P30, once (only on the run with the Activity Manager off; the other run prints SKIP): with "audio-switch" on, the list
    /// of outputs opens on the Home page ("smoke-audio-outputs"), says „Nicio ieșire audio” on a machine without audio (or
    /// shows the rows, at most one default), the button beside the volume ("audio-outputs-toggle") closes and reopens it,
    /// and the switch doesn't turn itself off; nothing is chosen (that would change the machine's default output).
    /// B1, on both runs: the safety net repairs a panel and a pill emptied on purpose ("B1 recover" in the log); then, with every
    /// new switch on, 20 openings mixed with an alert, the Command Bar, the shelf and the page by context, and after each the
    /// content is seen (";b1=" absent from the status) without any repair (";b1r=" unchanged).
    /// log.txt is copied to the artifacts folder after every run; on a failure also a screenshot and the notch's state.
    /// This file: Main, the order of the checks, the shared helpers and the basic checks; the areas are in SmokeAlerts.cs,
    /// SmokeCommandBar.cs, SmokeContext.cs (P27, P20), SmokeClipboard.cs (P21), SmokeShelf.cs (P23), SmokeAudio.cs (P30) and SmokeB1.cs (B1), the same partial class.
    /// </summary>
    public static partial class SmokeProgram
    {
        private static Process _app;
        private static UIA3Automation _uia;
        private static AutomationElement _desktop;
        private static AutomationElement _notch;
        private static string _dataDir, _log;
        private static readonly Stopwatch _clock = new Stopwatch();
        private static int _pass;
        private static bool _activityOn;
        private const string ActivityFeature = "activity-manager";

        private sealed class SmokeFailure : Exception { public SmokeFailure(string m) : base(m) { } }

        public static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            var pos = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToArray();
            foreach (var a in args.Where(a => a.StartsWith("--", StringComparison.Ordinal)))
            {
                if (a == "--activity-manager=on") _activityOn = true;
                else if (a == "--activity-manager=off") _activityOn = false;
                else { Console.WriteLine("Argument necunoscut: " + a); return 2; }
            }
            string exe = Path.GetFullPath(pos.Length > 0 ? pos[0] : Path.Combine("publish", "WinNotch.exe"));
            string outDir = Path.GetFullPath(pos.Length > 1 ? pos[1] : "smoke-artifacts");
            Console.WriteLine("Test de fum, cu „" + ActivityFeature + "” " + (_activityOn ? "pornit" : "oprit") + ".");
            _dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinNotch", SmokeMode.FolderName);
            _log = Path.Combine(_dataDir, "log.txt");
            string step = "pregătire";
            try
            {
                if (!File.Exists(exe)) throw new SmokeFailure("Nu există " + exe + " (rulează întâi dotnet publish).");
                try { if (Directory.Exists(_dataDir)) Directory.Delete(_dataDir, true); } catch (IOException) { }
                _uia = new UIA3Automation();
                _desktop = _uia.GetDesktop();
                _clock.Start();
                _app = Process.Start(new ProcessStartInfo(exe, SmokeMode.Arg) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe) });

                Run(step = "Fereastra notch există și e vizibilă", NotchVisible);
                if (_activityOn) Run(step = "Comutatorul „activity-manager” pornit înainte de verificări", ActivityManagerOn);
                Run(step = "Procesul trăiește 30 s", AliveFor30s);
                Run(step = "Alertă de volum: pastila își schimbă mărimea și revine", () => AlertChangesPill("post-alert volume"));
                Run(step = "Alertă de piesă: pastila își schimbă mărimea și revine", () => AlertChangesPill("post-alert track"));
                Run(step = "Comutator de funcție: „context-engine” oprit și pornit din nou", ToggleFeature);
                Run(step = _activityOn ? "Pastila împărțită: două activități persistente, apoi „activity.dismiss-all”" : "Fără Activity Manager: activitățile persistente sunt refuzate", SplitPill);
                Run(step = _activityOn ? "„5 noutăți”: 5 alerte rapide se adună într-una" : "Fără Activity Manager: rafala de test e refuzată", Burst);
                Run(step = _activityOn ? "Activitate Low: pastila se lărgește puțin 2 s („peek”)" : "Fără Activity Manager: activitatea Low e refuzată", Peek);
                Run(step = "Win+Alt+N deschide și închide notch-ul", Hotkey);
                if (!_activityOn) Run(step = "Pagina după context: contextul „Dev” din motor deschide notch-ul pe pagina mapată, doar cu „context-pages” pornit", ContextPages);
                else Console.WriteLine("SKIP  Pagina după context (rulează o singură dată, în rularea cu activity-manager oprit)");
                Run(step = _activityOn
                    ? "Quick Actions: întâlnire cu căști → butoanele sub pastilă; click pe „Mută / pornește microfonul” prin registru, cu rezultatul în rând; o singură sugestie (peek) la 10 minute; „Nu mai arăta”"
                    : "Quick Actions: întâlnire cu căști → butoanele sub pastilă; click pe „Mută / pornește microfonul” prin registru, cu rezultatul în rând; fără sugestii nesolicitate", QuickActions);
                if (!_activityOn) Run(step = "Smart Clipboard: un JSON copiat → chip-ul „Formatează” în widget-ul Clipboard; click prin registru → JSON-ul formatat în clipboard", SmartClipboard);
                else Console.WriteLine("SKIP  Smart Clipboard (rulează o singură dată, în rularea cu activity-manager oprit)");
                if (!_activityOn) Run(step = "Raft: un fișier local adăugat ca la tragere → apare în raft; „Copiază calea” prin registru → exact calea în clipboard; „Golește” → raftul gol", Shelf);
                else Console.WriteLine("SKIP  Raft (rulează o singură dată, în rularea cu activity-manager oprit)");
                if (!_activityOn) Run(step = "Căști/boxe: lista ieșirilor audio de lângă volum se deschide și fără niciun dispozitiv („Nicio ieșire audio”), butonul o închide și o redeschide, comutatorul nu se oprește singur", AudioOutputs);
                else Console.WriteLine("SKIP  Căști/boxe (rulează o singură dată, în rularea cu activity-manager oprit)");
                Run(step = "B1: plasa de siguranță repară un panou și o pastilă golite intenționat; 20 de deschideri cu toate comutatoarele noi pornite (alertă, Command Bar, raft, pagina după context), conținutul vizibil de fiecare dată, fără reparații", B1EmptyNotch);
                Run(step = "Command Bar oprit: Win+Alt+Space nu deschide nimic", CommandBarOff);
                Run(step = "Command Bar: se deschide, „volum” găsește un rezultat, o alertă nu ia pastila, Esc închide și focusul revine", CommandBarSearchAndEscape);
                Run(step = "Command Bar: Enter pe „settings.position” (sigură) deschide Setări în fereastra WinNotch", CommandBarRunsAction);
                Run(step = "Fereastra WinNotch se deschide din meniul iconiței", EditorFromTray);
                Run(step = "„Ieșire” din meniul iconiței închide curat (cod 0)", ExitFromTray);
                Run(step = "Nicio excepție în log.txt", LogClean);
                Console.WriteLine($"\nTEST DE FUM ({ActivityFeature} {(_activityOn ? "pornit" : "oprit")}): {_pass} PASS, 0 FAIL ({_clock.Elapsed.TotalSeconds:0} s)");
                CopyLog(outDir);
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL  {step}: {(ex is SmokeFailure ? ex.Message : ex.ToString())}");
                SaveArtifacts(outDir);
                Console.WriteLine($"\nTEST DE FUM ({ActivityFeature} {(_activityOn ? "pornit" : "oprit")}): {_pass} PASS, 1 FAIL. Artefacte: {outDir}");
                return 1;
            }
            finally
            {
                CloseTestWindow();
                try { if (_app != null && !_app.HasExited) _app.Kill(); } catch { }
                _uia?.Dispose();
            }
        }

        private static void Run(string name, Action test)
        {
            var t = Stopwatch.StartNew();
            test();
            _pass++;
            Console.WriteLine($"PASS  {name} ({t.Elapsed.TotalSeconds:0.0} s)");
        }

        private static void Fail(string message) => throw new SmokeFailure(message);

        private static void Alive()
        {
            if (_app.HasExited) Fail("WinNotch s-a închis (cod " + _app.ExitCode + "). Ultimele rânduri din log:\n" + LogTail(15));
        }

        /// <summary>Asks until <paramref name="read"/> gives a value <paramref name="ok"/> accepts, or fails after <paramref name="timeout"/>.</summary>
        private static T WaitFor<T>(Func<T> read, Func<T, bool> ok, TimeSpan timeout, string what)
        {
            var until = DateTime.UtcNow + timeout;
            T last = default;
            while (true)
            {
                Alive();
                try { last = read(); if (ok(last)) return last; } catch (COMException) { } catch (TimeoutException) { } catch (ElementNotAvailableException) { }
                if (DateTime.UtcNow > until) Fail("Așteptat " + what + " în " + timeout.TotalSeconds + " s; ultima valoare: " + (last?.ToString() ?? "nimic"));
                Thread.Sleep(100);
            }
        }

        // ------------------------------------------------------------------ the notch

        private static AutomationElement FindNotch() =>
            _desktop.FindFirstChild(cf => cf.ByProcessId(_app.Id).And(cf.ByAutomationId(SmokeMode.NotchAutomationId)));

        private sealed class Status
        {
            public string Mode; public int W, H; public string Raw;
            /// <summary>P13: what the Activity Manager shows (0 when nothing of it).</summary>
            public int Split, Group, Peek;
            /// <summary>P14: the Command Bar is open.</summary>
            public int Cmd;
            /// <summary>P27: the page shown in the notch and the category in the context engine's snapshot ("" when not given).</summary>
            public string Page = "", Ctx = "";
            /// <summary>P20: Quick Actions buttons that ran with success, unasked suggestions posted.</summary>
            public int Qa, Qs;
            /// <summary>B1: what the safety net sees wrong on screen now (0 = the content is visible), its repairs since start.</summary>
            public int B1, B1r;
            public override string ToString() => Raw;
            public bool SameSize(Status o) => Math.Abs(W - o.W) <= 2 && Math.Abs(H - o.H) <= 2;
        }

        private static Status ReadStatus()
        {
            string raw = _notch.Properties.ItemStatus.ValueOrDefault ?? "";
            return SmokeMode.TryParseStatus(raw, out var m, out int w, out int h, out var x, out var page, out var ctx)
                ? new Status { Mode = m, W = w, H = h, Raw = raw, Split = x["split"], Group = x["group"], Peek = x["peek"], Cmd = x["cmd"], Page = page, Ctx = ctx, Qa = x["qa"], Qs = x["qs"], B1 = x["b1"], B1r = x["b1r"] }
                : new Status { Mode = "", Raw = raw };
        }

        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);

        private static void NotchVisible()
        {
            _notch = WaitFor(FindNotch, n => n != null, TimeSpan.FromSeconds(60), "fereastra notch-ului");
            var hwnd = _notch.Properties.NativeWindowHandle.ValueOrDefault;
            if (hwnd == IntPtr.Zero || !IsWindowVisible(hwnd)) Fail("Fereastra notch-ului nu e vizibilă.");
            WaitFor(ReadStatus, s => s.Mode.Length > 0 && s.W > 0 && s.H > 0, TimeSpan.FromSeconds(15), "pastila desenată (mode=…;pill=LxÎ)");
        }

        private static void AliveFor30s()
        {
            while (_clock.Elapsed < TimeSpan.FromSeconds(30)) { Alive(); Thread.Sleep(500); }
            Alive();
        }

        /// <summary>Waits for the previous command file to be consumed, then writes the new one in one move.</summary>
        private static void Command(string line)
        {
            string path = Path.Combine(_dataDir, SmokeMode.CommandsFile);
            WaitFor(() => File.Exists(path), exists => !exists, TimeSpan.FromSeconds(10), "comanda anterioară citită");
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, line + "\n", new UTF8Encoding(false));
            File.Move(tmp, path);
        }

        /// <summary>Waits until the notch is in standby and settled; returns its status.</summary>
        private static Status SettledIdle()
        {
            WaitFor(ReadStatus, s => s.Mode == "Idle", TimeSpan.FromSeconds(15), "notch-ul în standby");
            Thread.Sleep(800);
            return WaitFor(ReadStatus, s => s.Mode == "Idle", TimeSpan.FromSeconds(5), "notch-ul în standby");
        }

        private static Status OpenNotchByHotkey()
        {
            SettledIdle();
            Keyboard.TypeSimultaneously(VirtualKeyShort.LWIN, VirtualKeyShort.ALT, VirtualKeyShort.KEY_N);
            return WaitFor(ReadStatus, s => s.Mode == "Expanded" && s.Page.Length > 0, TimeSpan.FromSeconds(8), "notch-ul deschis (mode=Expanded, cu ;page=)");
        }

        private static void CloseNotchByHotkey()
        {
            Thread.Sleep(800);
            Keyboard.TypeSimultaneously(VirtualKeyShort.LWIN, VirtualKeyShort.ALT, VirtualKeyShort.KEY_N);
            WaitFor(ReadStatus, s => s.Mode != "Expanded", TimeSpan.FromSeconds(8), "notch-ul închis");
        }

        private static string IdOf(AutomationElement e) => e.Properties.AutomationId.ValueOrDefault ?? "";

        private static void Hotkey()
        {
            WaitFor(ReadStatus, s => s.Mode == "Idle", TimeSpan.FromSeconds(15), "notch-ul în standby");
            Keyboard.TypeSimultaneously(VirtualKeyShort.LWIN, VirtualKeyShort.ALT, VirtualKeyShort.KEY_N);
            WaitFor(ReadStatus, s => s.Mode == "Expanded", TimeSpan.FromSeconds(8), "notch-ul deschis (mode=Expanded)");
            Thread.Sleep(1000);
            Keyboard.TypeSimultaneously(VirtualKeyShort.LWIN, VirtualKeyShort.ALT, VirtualKeyShort.KEY_N);
            WaitFor(ReadStatus, s => s.Mode != "Expanded", TimeSpan.FromSeconds(8), "notch-ul închis la a doua apăsare");
        }

        // ------------------------------------------------------------------ the tray icon and its menu

        private static void EditorFromTray()
        {
            ClickMenuItem("Pagini, teme și setări…");
            var editor = WaitFor(FindEditor, w => w != null, TimeSpan.FromSeconds(20), "fereastra WinNotch");
            var hwnd = editor.Properties.NativeWindowHandle.ValueOrDefault;
            if (hwnd == IntPtr.Zero || !IsWindowVisible(hwnd)) Fail("Fereastra WinNotch nu e vizibilă.");
            editor.AsWindow().Close();
            WaitFor(FindEditor, w => w == null, TimeSpan.FromSeconds(10), "fereastra WinNotch închisă");
        }

        private static AutomationElement FindEditor() =>
            _desktop.FindAllChildren(cf => cf.ByProcessId(_app.Id).And(cf.ByControlType(ControlType.Window)))
                    .FirstOrDefault(w => w.Properties.AutomationId.ValueOrDefault != SmokeMode.NotchAutomationId && w.Properties.Name.ValueOrDefault == "WinNotch");

        private static void ExitFromTray()
        {
            ClickMenuItem("Ieșire");
            if (!_app.WaitForExit(20000)) Fail("WinNotch nu s-a închis în 20 s după „Ieșire”.");
            _app.WaitForExit();
            if (_app.ExitCode != 0) Fail("Cod de ieșire " + _app.ExitCode + " (așteptat 0).");
            // "Ieșire" must also be recorded as a clean exit (StartupGuard.MarkCleanExit), or every exit counts as a crash
            string state = Path.Combine(_dataDir, "startup.json");
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(state));
                if (!doc.RootElement.TryGetProperty("Running", out var running) || running.GetBoolean())
                    Fail("startup.json nu marchează ieșirea curată (Running ar trebui să fie false).");
            }
            catch (IOException) { Fail("startup.json lipsește după „Ieșire”: " + state); }
            catch (JsonException) { Fail("startup.json nu poate fi citit după „Ieșire”."); }
        }

        /// <summary>
        /// Opens the tray menu and clicks <paramref name="name"/>. Exactly one retry: on the CI runner the menu sometimes
        /// doesn't open on the second time (docs/PROGRESS.md); the reason is written to the console (and the run summary).
        /// </summary>
        private static void ClickMenuItem(string name)
        {
            const int attempts = 2;
            for (int attempt = 1; ; attempt++)
            {
                OpenTrayMenu();
                AutomationElement item;
                try
                {
                    item = WaitFor(() => FindMenu()?.FindFirstDescendant(cf => cf.ByControlType(ControlType.MenuItem).And(cf.ByName(name))),
                                   i => i != null, TimeSpan.FromSeconds(5), "„" + name + "” în meniul iconiței");
                }
                catch (SmokeFailure ex) when (attempt < attempts)
                {
                    string why = "meniul iconiței nu a arătat „" + name + "” (încercarea " + attempt + "): " + ex.Message + "; reîncerc o singură dată.";
                    Console.WriteLine("      (" + why + ")");
                    if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true") Console.WriteLine("::warning::Test de fum: " + why);
                    Keyboard.Type(VirtualKeyShort.ESCAPE);
                    Thread.Sleep(500);
                    continue;
                }
                item.Click();
                return;
            }
        }

        private static AutomationElement FindMenu() =>
            _desktop.FindFirstChild(cf => cf.ByProcessId(_app.Id).And(cf.ByControlType(ControlType.Menu)));

        /// <summary>Right-click on the icon next to the clock (also among the hidden icons); if the taskbar can't be read, the icon's own message.</summary>
        private static void OpenTrayMenu()
        {
            var icon = FindTrayIcon();
            string why = "iconița nu e vizibilă în bara de activități";
            if (icon != null)
            {
                // found but sometimes not clickable on the CI runner (NoClickablePointException): same fallback as "not found"
                try { icon.RightClick(); Console.WriteLine("      (meniul iconiței: click dreapta pe iconiță)"); return; }
                catch (NoClickablePointException) { why = "iconița găsită, dar fără punct de click"; }
            }
            PostTrayRightClick();
            Console.WriteLine("      (meniul iconiței: " + why + "; mesajul ei de click dreapta)");
            // visible in the run summary: the icon itself wasn't checked this time
            if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true")
                Console.WriteLine("::warning::Testul de fum nu a găsit iconița în bara de activități; meniul a fost deschis cu mesajul ei de click dreapta.");
        }

        private static AutomationElement FindTrayIcon()
        {
            bool IsOurs(AutomationElement e) => (e.Properties.Name.ValueOrDefault ?? "").StartsWith("WinNotch", StringComparison.Ordinal);
            try
            {
                var tray = _desktop.FindFirstChild(cf => cf.ByClassName("Shell_TrayWnd"));
                var icon = tray?.FindAllDescendants(cf => cf.ByControlType(ControlType.Button)).FirstOrDefault(IsOurs);
                if (icon != null) return icon;
                // hidden icons: the "^" button, then the overflow window (Windows 10: NotifyIconOverflowWindow, Windows 11: a XAML island)
                var chevron = tray?.FindAllDescendants(cf => cf.ByControlType(ControlType.Button)).FirstOrDefault(b =>
                {
                    string n = (b.Properties.Name.ValueOrDefault ?? "").ToLowerInvariant();
                    return n.Contains("hidden icons") || n.Contains("chevron") || n.Contains("pictograme ascunse");
                });
                if (chevron == null) return null;
                chevron.Click();
                Thread.Sleep(800);
                foreach (var cls in new[] { "NotifyIconOverflowWindow", "TopLevelWindowForOverflowXamlIsland" })
                {
                    var over = _desktop.FindFirstChild(cf => cf.ByClassName(cls));
                    var hidden = over?.FindAllDescendants(cf => cf.ByControlType(ControlType.Button)).FirstOrDefault(IsOurs);
                    if (hidden != null) return hidden;
                }
                Keyboard.Type(VirtualKeyShort.ESCAPE);
            }
            catch (COMException) { }
            return null;
        }

        private delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc proc, IntPtr lParam);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int max);
        [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

        /// <summary>
        /// What Windows sends to the app when its tray icon is right-clicked (WinForms NotifyIcon: WM_USER + 1024 with
        /// WM_RBUTTONUP), posted to the app's hidden WinForms windows. Used only when the taskbar's icons can't be found.
        /// </summary>
        private static void PostTrayRightClick()
        {
            const uint WM_TRAYMOUSEMESSAGE = 0x0400 + 1024, WM_RBUTTONUP = 0x0205;
            var targets = new List<IntPtr>();
            EnumWindows((h, l) =>
            {
                GetWindowThreadProcessId(h, out uint pid);
                if (pid != _app.Id) return true;
                var sb = new StringBuilder(256);
                GetClassName(h, sb, sb.Capacity);
                if (sb.ToString().StartsWith("WindowsForms10.Window", StringComparison.Ordinal)) targets.Add(h);
                return true;
            }, IntPtr.Zero);
            if (targets.Count == 0) Fail("Nu găsesc iconița WinNotch (nici în bara de activități, nici fereastra ei ascunsă).");
            foreach (var h in targets) PostMessage(h, WM_TRAYMOUSEMESSAGE, (IntPtr)1, (IntPtr)WM_RBUTTONUP);
        }

        // ------------------------------------------------------------------ the log

        private static string[] LogLines()
        {
            try
            {
                using var f = new FileStream(_log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var r = new StreamReader(f, Encoding.UTF8);
                return r.ReadToEnd().Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
            }
            catch (IOException) { return Array.Empty<string>(); }
            catch (UnauthorizedAccessException) { return Array.Empty<string>(); }
        }

        private static int LogCount(string text) => LogLines().Count(l => l.Contains(text, StringComparison.Ordinal));

        private static string LogTail(int n) => string.Join("\n", LogLines().Where(l => l.Length > 0).TakeLast(n));

        /// <summary>
        /// Unhandled errors, failed starts, feature errors and stack traces fail the test. Other lines naming an exception
        /// type (a service that is missing on the CI machine, handled and logged) are only listed.
        /// </summary>
        private static void LogClean()
        {
            var lines = LogLines();
            if (lines.Length == 0) Fail("log.txt lipsește sau e gol: " + _log);
            var bad = lines.Where(SmokeMode.IsFatalLogLine).ToList();
            foreach (var l in lines.Where(l => l.Contains("Exception", StringComparison.Ordinal) && !bad.Contains(l)))
                Console.WriteLine("      (în log, tratat: " + l + ")");
            if (bad.Count > 0) Fail("Excepții în log.txt:\n" + string.Join("\n", bad.Take(20)));
        }

        /// <summary>log.txt of this run, kept next to the other run's (each run has its own artifacts folder).</summary>
        private static void CopyLog(string outDir)
        {
            try
            {
                Directory.CreateDirectory(outDir);
                if (File.Exists(_log)) File.WriteAllLines(Path.Combine(outDir, "log.txt"), LogLines());
            }
            catch (Exception ex) { Console.WriteLine("log.txt nu a putut fi copiat: " + ex.GetType().Name); }
        }

        // ------------------------------------------------------------------ on failure

        private static void SaveArtifacts(string outDir)
        {
            try
            {
                Directory.CreateDirectory(outDir);
                try { Capture.Screen().ToFile(Path.Combine(outDir, "ecran.png")); } catch (Exception ex) { Console.WriteLine("Captura de ecran a eșuat: " + ex.GetType().Name); }
                if (File.Exists(_log)) File.WriteAllLines(Path.Combine(outDir, "log.txt"), LogLines());
                if (_notch != null) { try { File.WriteAllText(Path.Combine(outDir, "stare.txt"), ReadStatus().Raw); } catch { } }
            }
            catch (Exception ex) { Console.WriteLine("Artefactele nu au putut fi salvate: " + ex.GetType().Name); }
        }
    }
}
