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
    /// log.txt is copied to the artifacts folder after every run; on a failure also a screenshot and the notch's state.
    /// </summary>
    public static class SmokeProgram
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
            public override string ToString() => Raw;
            public bool SameSize(Status o) => Math.Abs(W - o.W) <= 2 && Math.Abs(H - o.H) <= 2;
        }

        private static Status ReadStatus()
        {
            string raw = _notch.Properties.ItemStatus.ValueOrDefault ?? "";
            return SmokeMode.TryParseStatus(raw, out var m, out int w, out int h, out var x, out var page, out var ctx)
                ? new Status { Mode = m, W = w, H = h, Raw = raw, Split = x["split"], Group = x["group"], Peek = x["peek"], Cmd = x["cmd"], Page = page, Ctx = ctx, Qa = x["qa"], Qs = x["qs"] }
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

        private static void AlertChangesPill(string command)
        {
            var idle = WaitFor(ReadStatus, s => s.Mode == "Idle", TimeSpan.FromSeconds(15), "notch-ul în standby");
            Thread.Sleep(800);                                                    // let the standby animation settle
            idle = WaitFor(ReadStatus, s => s.Mode == "Idle", TimeSpan.FromSeconds(5), "notch-ul în standby");
            Command(command);
            WaitFor(ReadStatus, s => s.Mode == "Live" && !s.SameSize(idle), TimeSpan.FromSeconds(8), "alerta afișată (mode=Live, altă mărime decât " + idle + ")");
            WaitFor(ReadStatus, s => s.Mode == "Idle" && s.SameSize(idle), TimeSpan.FromSeconds(15), "pastila înapoi la " + idle);
        }

        private static void ToggleFeature()
        {
            int stops = LogCount("Context: oprit."), starts = LogCount("Context: pornit");
            Command("toggle feature context-engine");
            WaitFor(() => LogCount("Context: oprit."), n => n > stops, TimeSpan.FromSeconds(10), "„Context: oprit.” în log");
            Command("toggle feature context-engine");
            WaitFor(() => LogCount("Context: pornit"), n => n > starts, TimeSpan.FromSeconds(10), "„Context: pornit” în log");
            Command("toggle feature nu-exista");                                  // unknown: ignored, nothing breaks
            WaitFor(() => LogCount("funcție necunoscută: nu-exista"), n => n > 0, TimeSpan.FromSeconds(10), "funcția necunoscută ignorată");
        }

        // ------------------------------------------------------------------ P13: the Activity Manager

        private static void ActivityManagerOn()
        {
            Command("toggle feature " + ActivityFeature);
            WaitFor(() => LogCount("Activity Manager: pornit."), n => n > 0, TimeSpan.FromSeconds(10), "„Activity Manager: pornit.” în log");
        }

        /// <summary>Waits until the notch is in standby and settled; returns its status.</summary>
        private static Status SettledIdle()
        {
            WaitFor(ReadStatus, s => s.Mode == "Idle", TimeSpan.FromSeconds(15), "notch-ul în standby");
            Thread.Sleep(800);
            return WaitFor(ReadStatus, s => s.Mode == "Idle", TimeSpan.FromSeconds(5), "notch-ul în standby");
        }

        /// <summary>Off: the command is refused (a log line, no fatal one) and the pill stays in standby, without activity fields.</summary>
        private static void Refused(string command)
        {
            int before = LogCount("managerul de activități e oprit; comanda e ignorată");
            Command(command);
            WaitFor(() => LogCount("managerul de activități e oprit; comanda e ignorată"), n => n > before, TimeSpan.FromSeconds(10), "comanda „" + command + "” refuzată în log");
            Thread.Sleep(1000);
            var s = ReadStatus();
            if (s.Mode != "Idle" || s.Split + s.Group + s.Peek != 0) Fail("Cu „" + ActivityFeature + "” oprit, „" + command + "” a schimbat pastila: " + s);
        }

        private static void SplitPill()
        {
            SettledIdle();
            if (!_activityOn)
            {
                Refused("post-activity persistent 1");
                Refused("post-activity persistent 2");
                int before = LogCount("activity.dismiss-all → indisponibil");
                Command("dismiss-activities");
                WaitFor(() => LogCount("activity.dismiss-all → indisponibil"), n => n > before, TimeSpan.FromSeconds(10), "„activity.dismiss-all” indisponibilă în log");
                return;
            }
            Command("post-activity persistent 1");
            WaitFor(ReadStatus, s => s.Mode == "Live" && s.Split == 0, TimeSpan.FromSeconds(8), "o activitate persistentă (mode=Live, fără split)");
            Command("post-activity persistent 2");
            var split = WaitFor(ReadStatus, s => s.Mode == "Live" && s.Split == 1, TimeSpan.FromSeconds(8), "pastila împărțită (split=1)");
            Thread.Sleep(5000);                                                   // persistent: still there after an alert would have ended
            var still = ReadStatus();
            if (still.Split != 1) Fail("Pastila împărțită a dispărut singură: " + still);
            Command("dismiss-activities");
            WaitFor(() => LogCount("activity.dismiss-all → făcut"), n => n > 0, TimeSpan.FromSeconds(10), "„activity.dismiss-all → făcut” în log");
            WaitFor(ReadStatus, s => s.Mode == "Idle" && s.Split == 0, TimeSpan.FromSeconds(10), "standby după „activity.dismiss-all” (de la " + split + ")");
        }

        private static void Burst()
        {
            SettledIdle();
            if (!_activityOn) { Refused("post-activity burst 5"); return; }
            Command("post-activity burst 5");
            var g = WaitFor(ReadStatus, s => s.Mode == "Live" && s.Group == 5, TimeSpan.FromSeconds(8), "„5 noutăți” (group=5)");
            WaitFor(ReadStatus, s => s.Mode == "Idle" && s.Group == 0, TimeSpan.FromSeconds(15), "standby după „5 noutăți” (de la " + g + ")");
        }

        private static void Peek()
        {
            SettledIdle();
            // runs after the burst: wait out its 5 s window, so the Low activity can't be counted with it (timing-independent)
            if (_activityOn) Thread.Sleep(5500);
            if (!_activityOn) { Refused("post-activity low"); return; }
            Command("post-activity low");
            var p = WaitFor(ReadStatus, s => s.Mode == "Live" && s.Peek == 1, TimeSpan.FromSeconds(8), "„peek” (peek=1)");
            if (p.H > 40) Fail("„Peek” ar trebui să rămână cât pastila mică (înălțime ≤ 40): " + p);
            WaitFor(ReadStatus, s => s.Mode == "Idle" && s.Peek == 0, TimeSpan.FromSeconds(10), "standby după „peek”");
        }

        private static void Hotkey()
        {
            WaitFor(ReadStatus, s => s.Mode == "Idle", TimeSpan.FromSeconds(15), "notch-ul în standby");
            Keyboard.TypeSimultaneously(VirtualKeyShort.LWIN, VirtualKeyShort.ALT, VirtualKeyShort.KEY_N);
            WaitFor(ReadStatus, s => s.Mode == "Expanded", TimeSpan.FromSeconds(8), "notch-ul deschis (mode=Expanded)");
            Thread.Sleep(1000);
            Keyboard.TypeSimultaneously(VirtualKeyShort.LWIN, VirtualKeyShort.ALT, VirtualKeyShort.KEY_N);
            WaitFor(ReadStatus, s => s.Mode != "Expanded", TimeSpan.FromSeconds(8), "notch-ul închis la a doua apăsare");
        }

        // ------------------------------------------------------------------ P27: the page by context

        private const string ContextPagesFeature = "context-pages";
        private const string ContextTargetPage = "devices";

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

        /// <summary>
        /// What is exercised: the context category comes from the real ContextEngine (the test command forces the category
        /// of the app in front inside the engine, through its normal debounce; the status shows the snapshot's category),
        /// the mapping from the settings (as Settings saves it), the switch through FeatureFlags, and the open through the
        /// real Win+Alt+N → Expand → the P27 hook. Not exercised here (unit tests only): the 10-minute manual window and
        /// hidden pages.
        /// </summary>
        private static void ContextPages()
        {
            SettledIdle();
            Command("fake-context dev");
            WaitFor(ReadStatus, s => s.Ctx == "Dev", TimeSpan.FromSeconds(10), "categoria „Dev” în snapshot-ul motorului de context (;ctx=Dev)");
            int mapped = LogCount("Test de fum: pagina pentru „Dev”: " + ContextTargetPage + ".");
            Command("set-context-page dev " + ContextTargetPage);
            WaitFor(() => LogCount("Test de fum: pagina pentru „Dev”: " + ContextTargetPage + "."), n => n > mapped, TimeSpan.FromSeconds(10), "maparea Dev → " + ContextTargetPage);

            // switch off: the notch opens where it was
            var off = OpenNotchByHotkey();
            Thread.Sleep(500);
            off = ReadStatus();
            if (off.Page == ContextTargetPage) Fail("Cu „" + ContextPagesFeature + "” oprit, notch-ul s-a deschis pe pagina mapată (sau era deja pe ea): " + off);
            Console.WriteLine("      (oprit: deschis pe „" + off.Page + "”)");
            CloseNotchByHotkey();

            int on = LogCount("Test de fum: funcția „" + ContextPagesFeature + "” pornită.");
            int chosen = LogCount("Pagina după context: Dev.");
            Command("toggle feature " + ContextPagesFeature);
            WaitFor(() => LogCount("Test de fum: funcția „" + ContextPagesFeature + "” pornită."), n => n > on, TimeSpan.FromSeconds(10), "„" + ContextPagesFeature + "” pornit");
            OpenNotchByHotkey();
            WaitFor(ReadStatus, s => s.Mode == "Expanded" && s.Page == ContextTargetPage, TimeSpan.FromSeconds(5), "notch-ul deschis pe „" + ContextTargetPage + "” (;page=" + ContextTargetPage + ")");
            WaitFor(() => LogCount("Pagina după context: Dev."), n => n > chosen, TimeSpan.FromSeconds(5), "„Pagina după context: Dev.” în log");
            CloseNotchByHotkey();

            // back as before: switch off, no mapping, the real context
            int offAgain = LogCount("Test de fum: funcția „" + ContextPagesFeature + "” oprită.");
            Command("toggle feature " + ContextPagesFeature);
            WaitFor(() => LogCount("Test de fum: funcția „" + ContextPagesFeature + "” oprită."), n => n > offAgain, TimeSpan.FromSeconds(10), "„" + ContextPagesFeature + "” oprit");
            Command("set-context-page dev none");
            Command("fake-context none");
            WaitFor(() => LogCount("Test de fum: context fals „niciunul”"), n => n > 0, TimeSpan.FromSeconds(10), "contextul fals scos");
            SettledIdle();
        }

        // ------------------------------------------------------------------ P20: Quick Actions

        private const string QuickActionsFeature = "quick-actions";
        private const string QaMic = SmokeMode.QuickActionAutomationPrefix + "audio.mute-mic";
        private const string QaVolume = SmokeMode.QuickActionAutomationPrefix + "audio.volume-set";
        private const string QaHide = SmokeMode.QuickActionHideAutomationPrefix + "meeting-headphones";
        /// <summary>The context engine logs a meeting change (its log line names the meeting app, "Test" for the fake one).</summary>
        private const string MeetingOnLog = ", întâlnire Test,", MeetingOffLog = ", întâlnire nu,";

        private static List<AutomationElement> QuickButtons() =>
            _notch.FindAllDescendants(cf => cf.ByControlType(ControlType.Button))
                  .Where(e => IdOf(e).StartsWith(SmokeMode.QuickActionAutomationPrefix, StringComparison.Ordinal)).ToList();

        private static AutomationElement QuickButton(string id) => QuickButtons().FirstOrDefault(b => IdOf(b) == id);

        /// <summary>"fake-meeting …" and the engine's own log line for it (the snapshot changed, after its debounce).</summary>
        private static void FakeMeeting(string output)
        {
            string expect = output == "none" ? MeetingOffLog : MeetingOnLog;
            int before = LogCount(expect);
            Command("fake-meeting " + output);
            WaitFor(() => LogCount(expect), n => n > before, TimeSpan.FromSeconds(10), "„" + expect.Trim(',', ' ') + "” în log (motorul de context)");
        }

        /// <summary>
        /// What is exercised: the meeting comes from the real ContextEngine (the test command forces "a meeting with
        /// headphones" inside the engine, through its normal debounce and Changed), the switch through FeatureFlags, the open
        /// through the real Win+Alt+N → Expand → the P20 hook, the buttons through UI Automation and the click through the
        /// registry ("Acțiune audio.mute-mic (QuickAction): reușită"; pressed twice, so the microphone is as before). With
        /// the Activity Manager on: one suggestion (a Low peek, ";qs=1"), not a second one within 10 minutes, and
        /// „Nu mai arăta”. With it off: no suggestion and no „Nu mai arăta”. Unit tests only: the other rules, the 10:00 edge.
        /// </summary>
        private static void QuickActions()
        {
            SettledIdle();
            FakeMeeting("headphones");

            // switch off: nothing under the pill, nothing suggested
            OpenNotchByHotkey();
            Thread.Sleep(800);
            if (QuickButtons().Count > 0 || ReadStatus().Qs != 0) Fail("Cu „" + QuickActionsFeature + "” oprit au apărut Quick Actions: " + ReadStatus());
            CloseNotchByHotkey();
            FakeMeeting("none");

            int on = LogCount("Quick Actions: pornit.");
            Command("toggle feature " + QuickActionsFeature);
            WaitFor(() => LogCount("Quick Actions: pornit."), n => n > on, TimeSpan.FromSeconds(10), "„Quick Actions: pornit.” în log");
            SettledIdle();
            FakeMeeting("headphones");
            if (_activityOn)
            {
                WaitFor(ReadStatus, st => st.Qs == 1, TimeSpan.FromSeconds(8), "o sugestie nesolicitată (;qs=1)");
                WaitFor(() => LogCount("Quick Actions: sugestie meeting-headphones."), n => n == 1, TimeSpan.FromSeconds(5), "„Quick Actions: sugestie meeting-headphones.” în log");
                SettledIdle();                                                    // the 2 s peek is over
                // the same rule again within 10 minutes: no second suggestion
                int later = LogCount("Quick Actions: sugestie amânată (meeting-headphones");
                FakeMeeting("none");
                FakeMeeting("headphones");
                WaitFor(() => LogCount("Quick Actions: sugestie amânată (meeting-headphones"), n => n > later, TimeSpan.FromSeconds(8), "a doua sugestie amânată (limita de 10 minute)");
                if (ReadStatus().Qs != 1) Fail("A doua sugestie a apărut în mai puțin de 10 minute: " + ReadStatus());
            }
            else
            {
                Thread.Sleep(2500);
                if (ReadStatus().Qs != 0 || LogCount("Quick Actions: sugestie") > 0) Fail("Fără Activity Manager a apărut o sugestie nesolicitată: " + ReadStatus());
            }

            // open: the meeting rule's buttons
            SettledIdle();
            OpenNotchByHotkey();
            var mic = WaitFor(() => QuickButton(QaMic), b => b != null, TimeSpan.FromSeconds(8), "butonul „" + QaMic + "”");
            var buttons = QuickButtons().Where(b => !IdOf(b).StartsWith(SmokeMode.QuickActionHideAutomationPrefix, StringComparison.Ordinal)).ToList();
            if (QuickButton(QaVolume) == null) Fail("Lipsește butonul „" + QaVolume + "”.");
            if (buttons.Count < 2 || buttons.Count > 4) Fail("Quick Actions ar trebui să aibă 2–4 butoane, are " + buttons.Count + ": " + string.Join(", ", buttons.Select(IdOf)));
            Console.WriteLine("      (" + string.Join(", ", buttons.Select(IdOf)) + ")");
            bool hide = QuickButton(QaHide) != null;
            if (hide != _activityOn) Fail(_activityOn ? "Lipsește „Nu mai arăta”." : "„Nu mai arăta” apare deși fără Activity Manager nu vin sugestii.");

            // a click goes through the registry as QuickAction; twice, so the microphone is back as it was
            const string ran = "Acțiune audio.mute-mic (QuickAction): reușită";
            int before = LogCount(ran), qa = ReadStatus().Qa;
            mic.AsButton().Invoke();
            WaitFor(ReadStatus, st => st.Qa == qa + 1, TimeSpan.FromSeconds(8), "clickul pe „Microfon” executat (;qa=" + (qa + 1) + ")");
            var msg = WaitFor(() => _notch.FindFirstDescendant(cf => cf.ByAutomationId(SmokeMode.QuickActionMessageAutomationId)), m => m != null && (m.Properties.Name.ValueOrDefault ?? "").Length > 0,
                              TimeSpan.FromSeconds(3), "rezultatul clickului în rând („" + SmokeMode.QuickActionMessageAutomationId + "”)");
            Console.WriteLine("      (rezultat: " + msg.Properties.Name.ValueOrDefault + ")");
            QuickButton(QaMic)?.AsButton().Invoke();
            WaitFor(ReadStatus, st => st.Qa == qa + 2, TimeSpan.FromSeconds(8), "al doilea click pe „Microfon” executat (;qa=" + (qa + 2) + ")");
            WaitFor(() => LogCount(ran), n => n >= before + 2, TimeSpan.FromSeconds(5), "„" + ran + "” de două ori în log");
            if (ReadStatus().Mode != "Expanded") Fail("Notch-ul s-a închis după un click pe Quick Actions: " + ReadStatus());

            if (_activityOn)
            {
                int hidden = LogCount("Quick Actions: sugestiile pentru „meeting-headphones” nu mai apar.");
                QuickButton(QaHide).AsButton().Invoke();
                WaitFor(() => LogCount("Quick Actions: sugestiile pentru „meeting-headphones” nu mai apar."), n => n > hidden, TimeSpan.FromSeconds(8), "„Nu mai arăta” salvat (log)");
                WaitFor(() => QuickButton(QaHide), b => b == null, TimeSpan.FromSeconds(5), "„Nu mai arăta” dispărut din rând");
                if (QuickButton(QaMic) == null) Fail("După „Nu mai arăta”, butoanele trebuie să rămână (doar sugestiile dispar).");
            }
            CloseNotchByHotkey();
            WaitFor(() => QuickButtons().Count, n => n == 0, TimeSpan.FromSeconds(5), "rândul Quick Actions dispărut după închidere");

            // back as before: switch off, the real context
            int off = LogCount("Quick Actions: oprit.");
            Command("toggle feature " + QuickActionsFeature);
            WaitFor(() => LogCount("Quick Actions: oprit."), n => n > off, TimeSpan.FromSeconds(10), "„Quick Actions: oprit.” în log");
            FakeMeeting("none");
            SettledIdle();
        }

        // ------------------------------------------------------------------ P21: Smart Clipboard

        private const string SmartClipboardFeature = "smart-clipboard";
        private const string ScFormat = SmokeMode.SmartClipChipPrefix + "clipboard.format-json";
        private const string ScMinify = SmokeMode.SmartClipChipPrefix + "clipboard.minify-json";
        /// <summary>A word that must never appear in log.txt (the content of the clipboard is never logged).</summary>
        private const string SmokeJsonMarker = "zq-fum-p21-marcaj";
        /// <summary>Minified as WinNotch writes it (so "Compactează" isn't offered at first), with diacritics and a number written "2.50".</summary>
        private const string SmokeJson = "{\"marcaj\":\"" + SmokeJsonMarker + "\",\"diacritice\":\"ăâîșț\",\"lista\":[1,2.50,{\"gol\":null}],\"ok\":true}";

        private static List<AutomationElement> SmartChips() =>
            _notch.FindAllDescendants(cf => cf.ByControlType(ControlType.Button))
                  .Where(e => IdOf(e).StartsWith(SmokeMode.SmartClipChipPrefix, StringComparison.Ordinal)).ToList();

        private static AutomationElement SmartChip(string id) => SmartChips().FirstOrDefault(b => IdOf(b) == id);

        /// <summary>The clipboard needs an STA thread; one short-lived thread per call.</summary>
        private static T OnSta<T>(Func<T> work)
        {
            T result = default;
            Exception error = null;
            var t = new Thread(() => { try { result = work(); } catch (Exception ex) { error = ex; } }) { IsBackground = true, Name = "clipboard-ul testului" };
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            if (!t.Join(TimeSpan.FromSeconds(15))) Fail("Clipboard-ul nu a răspuns în 15 s.");
            if (error != null) throw error;
            return result;
        }

        private static void SetClipboardText(string text)
        {
            for (int i = 0; i < 5; i++)
            {
                try { OnSta(() => { System.Windows.Forms.Clipboard.SetText(text); return true; }); return; }
                catch (ExternalException) { Thread.Sleep(300); }               // busy for a moment (WinNotch reading it)
            }
            Fail("Testul nu a putut scrie în clipboard.");
        }

        private static string GetClipboardText()
        {
            try { return OnSta(() => System.Windows.Forms.Clipboard.ContainsText() ? System.Windows.Forms.Clipboard.GetText() : ""); }
            catch (ExternalException) { return ""; }
        }

        /// <summary>The same JSON value (numbers as written, keys in order), however it is indented.</summary>
        private static bool SameJson(string a, string b)
        {
            try
            {
                using var x = JsonDocument.Parse(a);
                using var y = JsonDocument.Parse(b);
                return JsonSerializer.Serialize(x.RootElement) == JsonSerializer.Serialize(y.RootElement);
            }
            catch (JsonException) { return false; }
        }

        /// <summary>
        /// What is exercised: the copy goes through the real clipboard notification (WM_CLIPBOARDUPDATE → the history →
        /// the P21 hook), the switch through FeatureFlags, the widget on a real page (the standard pages have no Clipboard
        /// widget: the test command adds one), the chip through UI Automation, the click through the registry
        /// ("Acțiune clipboard.format-json (UI): reușită") and the result read back from the clipboard. Unit tests only:
        /// the other kinds, the peek (it needs the Activity Manager, off on this run), the password managers' markers.
        /// </summary>
        private static void SmartClipboard()
        {
            SettledIdle();
            int added = LogCount("Test de fum: pagina cu widget-ul Clipboard adăugată");
            Command("smoke-clipboard-page on");
            WaitFor(() => LogCount("Test de fum: pagina cu widget-ul Clipboard adăugată"), n => n > added, TimeSpan.FromSeconds(10), "pagina de test cu widget-ul Clipboard");

            // switch off: the widget as before, without chips
            SetClipboardText(SmokeJson);
            Thread.Sleep(800);
            OpenNotchByHotkey();
            WaitFor(ReadStatus, s => s.Page == SmokeMode.SmokeClipboardPageId, TimeSpan.FromSeconds(5), "notch-ul deschis pe pagina de test (;page=" + SmokeMode.SmokeClipboardPageId + ")");
            Thread.Sleep(1500);                                                   // the widget refreshes about once a second
            if (SmartChips().Count > 0) Fail("Cu „" + SmartClipboardFeature + "” oprit au apărut chip-uri: " + string.Join(", ", SmartChips().Select(IdOf)));
            CloseNotchByHotkey();

            int on = LogCount("Smart Clipboard: pornit.");
            Command("toggle feature " + SmartClipboardFeature);
            WaitFor(() => LogCount("Smart Clipboard: pornit."), n => n > on, TimeSpan.FromSeconds(10), "„Smart Clipboard: pornit.” în log");
            SetClipboardText(SmokeJson);                                          // copied with the switch on: recorded, then recognized
            Thread.Sleep(800);
            OpenNotchByHotkey();
            WaitFor(ReadStatus, s => s.Page == SmokeMode.SmokeClipboardPageId, TimeSpan.FromSeconds(5), "notch-ul deschis pe pagina de test");
            var chip = WaitFor(() => SmartChip(ScFormat), b => b != null, TimeSpan.FromSeconds(8), "chip-ul „Formatează” („" + ScFormat + "”)");
            if (SmartChip(ScMinify) != null) Fail("JSON-ul copiat e deja compact: „Compactează” nu trebuia oferit.");

            const string ran = "Acțiune clipboard.format-json (UI): reușită";
            int before = LogCount(ran);
            chip.AsButton().Invoke();
            WaitFor(() => LogCount(ran), n => n > before, TimeSpan.FromSeconds(8), "„" + ran + "” în log");
            var msg = WaitFor(() => _notch.FindFirstDescendant(cf => cf.ByAutomationId(SmokeMode.SmartClipMessageAutomationId)), m => m != null && (m.Properties.Name.ValueOrDefault ?? "").Length > 0,
                              TimeSpan.FromSeconds(3), "rezultatul clickului lângă chip-uri („" + SmokeMode.SmartClipMessageAutomationId + "”)");
            Console.WriteLine("      (rezultat: " + msg.Properties.Name.ValueOrDefault + ")");
            var got = WaitFor(GetClipboardText, t => t != SmokeJson && t.Contains('\n'), TimeSpan.FromSeconds(5), "JSON-ul formatat în clipboard");
            if (!SameJson(got, SmokeJson)) Fail("JSON-ul din clipboard nu e echivalent cu cel copiat (" + got.Length + " caractere).");
            if (!got.Replace("\r\n", "\n").Contains("\n  \"marcaj\": \"" + SmokeJsonMarker + "\"")) Fail("JSON-ul din clipboard nu e indentat cu 2 spații.");
            if (!got.Contains("ăâîșț") || !got.Contains("2.50")) Fail("Formatarea a schimbat diacriticele sau numerele.");

            // WinNotch's own write: no loop; the chips follow it ("Compactează", no "Formatează")
            WaitFor(() => SmartChip(ScMinify) != null && SmartChip(ScFormat) == null, ok => ok, TimeSpan.FromSeconds(5), "chip-urile după formatare („Compactează”, fără „Formatează”)");
            Thread.Sleep(1500);
            if (GetClipboardText() != got) Fail("Clipboard-ul s-a schimbat singur după formatare (buclă?).");
            if (ReadStatus().Mode != "Expanded") Fail("Notch-ul s-a închis după click pe chip: " + ReadStatus());
            CloseNotchByHotkey();

            // back as before: switch off, the test page removed; and nothing of the content in the log
            int off = LogCount("Smart Clipboard: oprit");
            Command("toggle feature " + SmartClipboardFeature);
            WaitFor(() => LogCount("Smart Clipboard: oprit"), n => n > off, TimeSpan.FromSeconds(10), "„Smart Clipboard: oprit” în log");
            int removed = LogCount("Test de fum: pagina cu widget-ul Clipboard scoasă.");
            Command("smoke-clipboard-page off");
            WaitFor(() => LogCount("Test de fum: pagina cu widget-ul Clipboard scoasă."), n => n > removed, TimeSpan.FromSeconds(10), "pagina de test scoasă");
            if (LogCount(SmokeJsonMarker) > 0) Fail("Conținutul clipboard-ului a ajuns în log.txt.");
            SettledIdle();
        }

        // ------------------------------------------------------------------ P14: the Command Bar

        private const string CommandBarFeature = "command-bar";
        private const string TestWindowTitle = "WinNotch, test de fum: fereastra anterioară";
        private const string ShortcutTaken = "Command Bar: scurtătura Win+Alt+Space e folosită de altă aplicație.";
        private static System.Windows.Forms.Form _testForm;
        private static IntPtr _testHwnd;

        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);

        /// <summary>A small window of the test (its own thread): the app the user was in before the Command Bar.</summary>
        private static void StartTestWindow()
        {
            if (_testForm != null) return;
            var ready = new ManualResetEventSlim(false);
            Exception error = null;
            var t = new Thread(() =>
            {
                try
                {
                    var f = new System.Windows.Forms.Form
                    {
                        Text = TestWindowTitle, StartPosition = System.Windows.Forms.FormStartPosition.Manual,
                        Left = 60, Top = 440, Width = 420, Height = 240, ShowInTaskbar = true,
                    };
                    f.Controls.Add(new System.Windows.Forms.TextBox { Multiline = true, Dock = System.Windows.Forms.DockStyle.Fill });
                    f.Shown += (o, e) => { _testHwnd = f.Handle; ready.Set(); };
                    _testForm = f;
                    System.Windows.Forms.Application.Run(f);
                }
                catch (Exception ex) { error = ex; ready.Set(); }
            }) { IsBackground = true, Name = "fereastra de test" };
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            if (!ready.Wait(TimeSpan.FromSeconds(15)) || error != null || _testHwnd == IntPtr.Zero)
                Fail("Fereastra de test nu a pornit" + (error != null ? ": " + error.GetType().Name : "."));
        }

        private static void CloseTestWindow()
        {
            var f = _testForm;
            _testForm = null;
            _testHwnd = IntPtr.Zero;
            try { if (f != null && f.IsHandleCreated) f.BeginInvoke(new Action(f.Close)); } catch { }
        }

        /// <summary>The test window in front, with the keyboard (a real click on it, then SetForegroundWindow if needed).</summary>
        private static void FocusTestWindow()
        {
            StartTestWindow();
            var form = WaitFor(() => _desktop.FindFirstChild(cf => cf.ByProcessId(Environment.ProcessId).And(cf.ByName(TestWindowTitle))),
                               w => w != null, TimeSpan.FromSeconds(10), "fereastra de test în UI Automation");
            for (int i = 0; i < 3 && GetForegroundWindow() != _testHwnd; i++)
            {
                var r = form.BoundingRectangle;
                Mouse.Click(new System.Drawing.Point(r.X + r.Width / 2, r.Y + r.Height / 2));
                Thread.Sleep(400);
                if (GetForegroundWindow() != _testHwnd) SetForegroundWindow(_testHwnd);
                Thread.Sleep(300);
            }
            if (GetForegroundWindow() != _testHwnd) Fail("Fereastra de test nu a ajuns în față (trebuie pentru verificarea focusului).");
        }

        private static AutomationElement FindCommandBox() =>
            _notch.FindFirstDescendant(cf => cf.ByAutomationId(SmokeMode.CommandBoxAutomationId));

        private static string IdOf(AutomationElement e) => e.Properties.AutomationId.ValueOrDefault ?? "";

        private static List<AutomationElement> CommandResults() =>
            _notch.FindAllDescendants(cf => cf.ByControlType(ControlType.ListItem))
                  .Where(e => IdOf(e).StartsWith(SmokeMode.CommandResultAutomationPrefix, StringComparison.Ordinal)).ToList();

        /// <summary>Like <see cref="WaitFor{T}"/>, but null after the timeout instead of failing.</summary>
        private static T TryWait<T>(Func<T> read, TimeSpan timeout) where T : class
        {
            var until = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < until)
            {
                Alive();
                try { var v = read(); if (v != null) return v; } catch (COMException) { } catch (TimeoutException) { } catch (ElementNotAvailableException) { }
                Thread.Sleep(100);
            }
            return null;
        }

        private static void Warn(string why)
        {
            Console.WriteLine("      (" + why + ")");
            if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true") Console.WriteLine("::warning::Test de fum: " + why);
        }

        /// <summary>
        /// Opens the bar over the test window with the real Win+Alt+Space; if the shortcut is taken on this machine or doesn't
        /// arrive in 5 s, with the "open-command-bar" test command (the same code path as the shortcut), said in the output.
        /// Then checks that the box has the keyboard (via the command: after a click on it, the shortcut's own focus can't be
        /// checked there).
        /// </summary>
        private static AutomationElement OpenCommandBar()
        {
            FocusTestWindow();
            if (LogCount(ShortcutTaken) == 0)
            {
                Keyboard.TypeSimultaneously(VirtualKeyShort.LWIN, VirtualKeyShort.ALT, VirtualKeyShort.SPACE);
                var box = TryWait(FindCommandBox, TimeSpan.FromSeconds(5));
                if (box != null)
                {
                    Console.WriteLine("      (Command Bar deschis cu scurtătura reală Win+Alt+Space)");
                    WaitFor(() => box.Properties.HasKeyboardFocus.ValueOrDefault, f => f, TimeSpan.FromSeconds(5), "focusul tastaturii în Command Bar, după scurtătură");
                    return box;
                }
                Warn("Win+Alt+Space nu a deschis Command Bar în 5 s; îl deschid cu comanda de test „open-command-bar” (aceeași cale ca scurtătura)");
            }
            else Warn("Win+Alt+Space e ocupată pe mașina de test; Command Bar deschis cu comanda de test „open-command-bar” (aceeași cale ca scurtătura)");
            Command("open-command-bar");
            var b = WaitFor(FindCommandBox, x => x != null, TimeSpan.FromSeconds(10), "Command Bar deschis (comanda de test)");
            if (!b.Properties.HasKeyboardFocus.ValueOrDefault)
            {
                b.Click();                                                        // without the shortcut, the test gives it the keyboard
                var r = _testForm != null ? GetWindowRectCenter() : default;
                if (r != default) Mouse.Position = r;                             // off the pill again, so the notch doesn't open by hover later
            }
            WaitFor(() => b.Properties.HasKeyboardFocus.ValueOrDefault, f => f, TimeSpan.FromSeconds(5), "focusul tastaturii în Command Bar");
            return b;
        }

        private static System.Drawing.Point GetWindowRectCenter()
        {
            var form = _desktop.FindFirstChild(cf => cf.ByProcessId(Environment.ProcessId).And(cf.ByName(TestWindowTitle)));
            if (form == null) return default;
            var r = form.BoundingRectangle;
            return new System.Drawing.Point(r.X + r.Width / 2, r.Y + r.Height / 2);
        }

        private static void CommandBarOff()
        {
            SettledIdle();
            FocusTestWindow();
            int opened = LogCount("Command Bar: deschis.");
            Keyboard.TypeSimultaneously(VirtualKeyShort.LWIN, VirtualKeyShort.ALT, VirtualKeyShort.SPACE);
            Thread.Sleep(2000);
            Keyboard.Type(VirtualKeyShort.ESCAPE);                                // Alt+Space may have opened the test window's system menu
            var s = ReadStatus();
            if (FindCommandBox() != null || s.Cmd != 0 || s.Mode == "Expanded" || LogCount("Command Bar: deschis.") > opened)
                Fail("Cu „" + CommandBarFeature + "” oprit, Win+Alt+Space a deschis ceva: " + s);
            // the test command takes the shortcut's path: refused too, without an error
            int refused = LogCount("Command Bar: comutatorul e oprit; nu se deschide.");
            Command("open-command-bar");
            WaitFor(() => LogCount("Command Bar: comutatorul e oprit; nu se deschide."), n => n > refused, TimeSpan.FromSeconds(10), "„open-command-bar” refuzată cu comutatorul oprit");
            if (FindCommandBox() != null || ReadStatus().Cmd != 0) Fail("Cu comutatorul oprit, comanda de test a deschis Command Bar.");
        }

        private static void CommandBarSearchAndEscape()
        {
            SettledIdle();
            int on = LogCount("Command Bar: pornit.");
            Command("toggle feature " + CommandBarFeature);
            WaitFor(() => LogCount("Command Bar: pornit."), n => n > on, TimeSpan.FromSeconds(10), "„Command Bar: pornit.” în log");
            WaitFor(() => LogCount("Command Bar: scurtătura Win+Alt+Space e activă.") + LogCount(ShortcutTaken), n => n > 0, TimeSpan.FromSeconds(10), "scurtătura înregistrată sau ocupată (log)");
            if (LogCount(ShortcutTaken) > 0) SettledIdle();                      // the one conflict alert goes first

            OpenCommandBar();
            WaitFor(ReadStatus, st => st.Cmd == 1 && st.Mode == "Expanded", TimeSpan.FromSeconds(5), "starea „cmd=1” (pastila e bara)");
            Keyboard.Type("volum");
            var results = WaitFor(CommandResults, l => l.Any(r => IdOf(r) == SmokeMode.CommandResultAutomationPrefix + "audio.volume-set"), TimeSpan.FromSeconds(8),
                                  "rezultatul „Setează volumul” pentru „volum”");
            Console.WriteLine("      (" + results.Count + " rezultate pentru „volum”)");

            // while it is open, an alert doesn't take the pill (it is held like with the open notch, on both paths)
            int vol = LogCount("Test de fum: alertă de volum.");
            Command("post-alert volume");
            WaitFor(() => LogCount("Test de fum: alertă de volum."), n => n > vol, TimeSpan.FromSeconds(10), "alerta de volum trimisă");
            if (_activityOn)
            {
                int pers = LogCount("Test de fum: activitate de test (PersistentActivity 1)");
                Command("post-activity persistent 1");
                WaitFor(() => LogCount("Test de fum: activitate de test (PersistentActivity 1)"), n => n > pers, TimeSpan.FromSeconds(10), "activitatea persistentă trimisă");
            }
            Thread.Sleep(1200);
            var held = ReadStatus();
            if (held.Cmd != 1 || held.Mode != "Expanded" || FindCommandBox() == null) Fail("O alertă a luat pastila cât era deschis Command Bar: " + held);

            Keyboard.Type(VirtualKeyShort.ESCAPE);
            WaitFor(FindCommandBox, b => b == null, TimeSpan.FromSeconds(5), "Command Bar închis cu Esc");
            WaitFor(ReadStatus, st => st.Cmd == 0 && st.Mode != "Expanded", TimeSpan.FromSeconds(5), "pastila înapoi (fără „cmd=1”)");
            WaitFor(() => GetForegroundWindow(), h => h == _testHwnd, TimeSpan.FromSeconds(5), "focusul înapoi în fereastra de test (cea din față înainte)");
            if (_activityOn)
            {
                // the persistent activity waited and comes in now
                WaitFor(ReadStatus, st => st.Mode == "Live", TimeSpan.FromSeconds(8), "activitatea persistentă afișată după închiderea barei");
                int dismissed = LogCount("activity.dismiss-all → făcut");
                Command("dismiss-activities");
                WaitFor(() => LogCount("activity.dismiss-all → făcut"), n => n > dismissed, TimeSpan.FromSeconds(10), "„activity.dismiss-all → făcut” în log");
            }
            SettledIdle();
        }

        private static void CommandBarRunsAction()
        {
            SettledIdle();
            OpenCommandBar();
            Keyboard.Type("setari pozitie");
            const string id = SmokeMode.CommandResultAutomationPrefix + "settings.position";
            WaitFor(() => CommandResults().FirstOrDefault(r => IdOf(r) == id), r => r != null && r.Properties.ItemStatus.ValueOrDefault == "selected", TimeSpan.FromSeconds(8),
                    "„Setări: poziția notch-ului” găsit și selectat");
            const string ran = "Acțiune settings.position (CommandBar): reușită";
            int before = LogCount(ran);
            Keyboard.Type(VirtualKeyShort.RETURN);
            WaitFor(() => LogCount(ran), n => n > before, TimeSpan.FromSeconds(10), "„" + ran + "” în log");
            WaitFor(FindCommandBox, b => b == null, TimeSpan.FromSeconds(5), "Command Bar închis după Enter");
            var editor = WaitFor(FindEditor, w => w != null, TimeSpan.FromSeconds(20), "fereastra WinNotch deschisă de „settings.position”");
            editor.AsWindow().Close();
            WaitFor(FindEditor, w => w == null, TimeSpan.FromSeconds(10), "fereastra WinNotch închisă");

            // switched off again: the shortcut is given back
            int off = LogCount("Command Bar: oprit.");
            Command("toggle feature " + CommandBarFeature);
            WaitFor(() => LogCount("Command Bar: oprit."), n => n > off, TimeSpan.FromSeconds(10), "„Command Bar: oprit.” în log");
            if (LogCount(ShortcutTaken) == 0)
                WaitFor(() => LogCount("Command Bar: scurtătura Win+Alt+Space a fost eliberată."), n => n > 0, TimeSpan.FromSeconds(10), "scurtătura eliberată (log)");
            CloseTestWindow();
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
