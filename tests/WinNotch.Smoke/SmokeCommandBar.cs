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
    public static partial class SmokeProgram
    {
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
    }
}
