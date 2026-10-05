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
    /// Usage: WinNotch.Smoke [path\to\WinNotch.exe] [artifacts folder]. Exit code 0 = all passed. On a failure: a screenshot
    /// and log.txt in the artifacts folder.
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

        private sealed class SmokeFailure : Exception { public SmokeFailure(string m) : base(m) { } }

        public static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            string exe = Path.GetFullPath(args.Length > 0 ? args[0] : Path.Combine("publish", "WinNotch.exe"));
            string outDir = Path.GetFullPath(args.Length > 1 ? args[1] : "smoke-artifacts");
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
                Run(step = "Procesul trăiește 30 s", AliveFor30s);
                Run(step = "Alertă de volum: pastila își schimbă mărimea și revine", () => AlertChangesPill("post-alert volume"));
                Run(step = "Alertă de piesă: pastila își schimbă mărimea și revine", () => AlertChangesPill("post-alert track"));
                Run(step = "Comutator de funcție: „context-engine” oprit și pornit din nou", ToggleFeature);
                Run(step = "Win+Alt+N deschide și închide notch-ul", Hotkey);
                Run(step = "Fereastra WinNotch se deschide din meniul iconiței", EditorFromTray);
                Run(step = "„Ieșire” din meniul iconiței închide curat (cod 0)", ExitFromTray);
                Run(step = "Nicio excepție în log.txt", LogClean);
                Console.WriteLine($"\nTEST DE FUM: {_pass} PASS, 0 FAIL ({_clock.Elapsed.TotalSeconds:0} s)");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL  {step}: {(ex is SmokeFailure ? ex.Message : ex.ToString())}");
                SaveArtifacts(outDir);
                Console.WriteLine($"\nTEST DE FUM: {_pass} PASS, 1 FAIL. Artefacte: {outDir}");
                return 1;
            }
            finally
            {
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
            public override string ToString() => Raw;
            public bool SameSize(Status o) => Math.Abs(W - o.W) <= 2 && Math.Abs(H - o.H) <= 2;
        }

        private static Status ReadStatus()
        {
            string raw = _notch.Properties.ItemStatus.ValueOrDefault ?? "";
            return SmokeMode.TryParseStatus(raw, out var m, out int w, out int h) ? new Status { Mode = m, W = w, H = h, Raw = raw } : new Status { Mode = "", Raw = raw };
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

        private static void ClickMenuItem(string name)
        {
            for (int attempt = 1; ; attempt++)
            {
                OpenTrayMenu();
                AutomationElement item = null;
                try
                {
                    item = WaitFor(() => FindMenu()?.FindFirstDescendant(cf => cf.ByControlType(ControlType.MenuItem).And(cf.ByName(name))),
                                   i => i != null, TimeSpan.FromSeconds(5), "„" + name + "” în meniul iconiței");
                }
                catch (SmokeFailure) when (attempt < 3) { Keyboard.Type(VirtualKeyShort.ESCAPE); continue; }
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
            if (icon != null) { icon.RightClick(); Console.WriteLine("      (meniul iconiței: click dreapta pe iconiță)"); return; }
            PostTrayRightClick();
            Console.WriteLine("      (meniul iconiței: iconița nu e vizibilă în bara de activități; mesajul ei de click dreapta)");
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
