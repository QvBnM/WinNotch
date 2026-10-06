using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using WinNotch.Features.Smoke;

namespace WinNotch.Smoke
{
    public static partial class SmokeProgram
    {
        // ------------------------------------------------------------------ B1: the notch opened empty, the pill showed only the time

        private const int B1Cycles = 20;
        /// <summary>Every switch added since 0.6.13 (the Activity Manager is set by the run itself: off on one, on on the other).</summary>
        private static readonly string[] B1Features = { "command-bar", "context-pages", "quick-actions", "smart-clipboard", "shelf", "audio-switch" };

        /// <summary>
        /// What is exercised, on both runs: (1) the safety net itself: the open notch's panel and then the standby pill are
        /// emptied on purpose ("smoke-empty-panel", "smoke-empty-pill") and must be repaired, with a "B1 recover" line;
        /// (2) with every new switch on, 20 openings and closings mixed with an alert (opened over it), the Command Bar opened
        /// and closed, the shelf opened over the page, the page chosen by context ("Dev" → "devices"); after each opening the
        /// panel, the tabs and the page are seen (";b1=" absent), after each closing the pill is, and the safety net never
        /// had to repair anything (";b1r=" unchanged).
        /// </summary>
        private static void B1EmptyNotch()
        {
            SettledIdle();

            // (1) the safety net repairs what it is meant to
            OpenNotchB1();
            int before = ReadStatus().B1r, logged = LogCount("B1 recover: "), seen = LogCount("conținutul se vede"), named = LogCount("B1 recover: PanelHidden, NoTabs");
            Command("smoke-empty-panel");
            WaitFor(() => LogCount("Test de fum: panoul golit intenționat."), n => n > 0, TimeSpan.FromSeconds(5), "panoul golit intenționat");
            WaitFor(ReadStatus, s => s.B1r > before && s.B1 == 0 && s.Mode == "Expanded", TimeSpan.FromSeconds(5), "plasa de siguranță a refăcut panoul (;b1r crește, fără ;b1=)");
            WaitFor(() => LogCount("conținutul se vede"), n => n > seen, TimeSpan.FromSeconds(5), "„B1 recover: … conținutul se vede” în log");
            if (LogCount("B1 recover: PanelHidden, NoTabs") <= named) Fail("Rândul „B1 recover” nu numește problemele panoului golit:\n" + LogTail(6));
            CloseNotchB1();
            SettledIdle();
            before = ReadStatus().B1r;
            Command("smoke-empty-pill");
            WaitFor(() => LogCount("Test de fum: pastila golită intenționat."), n => n > 0, TimeSpan.FromSeconds(5), "pastila golită intenționat");
            WaitFor(ReadStatus, s => s.B1r > before && s.B1 == 0 && s.Mode == "Idle", TimeSpan.FromSeconds(5), "plasa de siguranță a refăcut pastila");
            Console.WriteLine("      (plasa de siguranță: " + (LogCount("B1 recover: ") - logged) + " rânduri „B1 recover” pentru golirile intenționate)");

            // (2) 20 cycles with every new switch on: nothing may need repairing
            var on = new List<string>();
            try
            {
                foreach (var f in B1Features) { SwitchB1(f, true); on.Add(f); }
                int mapped = LogCount("Test de fum: pagina pentru „Dev”: devices.");
                Command("set-context-page dev devices");
                WaitFor(() => LogCount("Test de fum: pagina pentru „Dev”: devices."), n => n > mapped, TimeSpan.FromSeconds(10), "maparea Dev → devices");
                SettledIdle();
                int repairs = ReadStatus().B1r;
                for (int i = 1; i <= B1Cycles; i++)
                {
                    string what;
                    switch (i % 4)
                    {
                        case 1:
                            what = "peste o alertă";
                            SettledIdle();
                            Command("post-alert volume");
                            WaitFor(ReadStatus, s => s.Mode == "Live", TimeSpan.FromSeconds(8), "alerta de volum (ciclul " + i + ")");
                            OpenNotchB1();
                            break;
                        case 2:
                            what = "după Command Bar";
                            SettledIdle();
                            FocusTestWindow();
                            Command("open-command-bar");
                            WaitFor(ReadStatus, s => s.Cmd == 1 && s.B1 == 0, TimeSpan.FromSeconds(8), "Command Bar deschis și vizibil (ciclul " + i + ")");
                            Thread.Sleep(300);
                            if (ReadStatus().Cmd == 1) Command("open-command-bar");     // the shortcut again: closes it
                            WaitFor(ReadStatus, s => s.Cmd == 0 && s.Mode != "Expanded", TimeSpan.FromSeconds(8), "Command Bar închis (ciclul " + i + ")");
                            WaitFor(ReadStatus, s => s.B1 == 0, TimeSpan.FromSeconds(3), "pastila vizibilă după Command Bar (ciclul " + i + ")");
                            OpenNotchB1();
                            break;
                        case 3:
                            what = "cu raftul";
                            SettledIdle();
                            OpenNotchB1();
                            var toggle = WaitFor(() => _notch.FindFirstDescendant(cf => cf.ByAutomationId(SmokeMode.ShelfToggleAutomationId)), b => b != null, TimeSpan.FromSeconds(5), "butonul „Raft” (ciclul " + i + ")");
                            toggle.AsButton().Invoke();
                            WaitFor(() => _notch.FindFirstDescendant(cf => cf.ByAutomationId(SmokeMode.ShelfClearAutomationId)), b => b != null, TimeSpan.FromSeconds(5), "raftul deschis peste pagină (ciclul " + i + ")");
                            break;
                        default:
                            bool dev = i % 8 == 0;
                            what = dev ? "pagina după context (Dev)" : "fără context";
                            SettledIdle();
                            string fakeLog = "Test de fum: context fals „" + (dev ? "Dev" : "niciunul") + "”";
                            int fakes = LogCount(fakeLog);
                            Command(dev ? "fake-context dev" : "fake-context none");
                            WaitFor(() => LogCount(fakeLog), n => n > fakes, TimeSpan.FromSeconds(10), "contextul fals „" + (dev ? "Dev" : "niciunul") + "” (ciclul " + i + ")");
                            if (dev) WaitFor(ReadStatus, s => s.Ctx == "Dev", TimeSpan.FromSeconds(10), "contextul „Dev” în snapshot (ciclul " + i + ")");
                            var st = OpenNotchB1();
                            if (dev) WaitFor(ReadStatus, s => s.Page == "devices", TimeSpan.FromSeconds(5), "pagina „devices” după context (ciclul " + i + ", " + st + ")");
                            break;
                    }
                    var open = WaitFor(ReadStatus, s => s.Mode == "Expanded" && s.Cmd == 0 && s.B1 == 0, TimeSpan.FromSeconds(3), "conținutul notch-ului vizibil după deschidere (ciclul " + i + ", " + what + ")");
                    if (open.B1r != repairs) Fail("Plasa de siguranță a trebuit să repare notch-ul în ciclul " + i + " (" + what + "): " + open + "\n" + LogTail(8));
                    CloseNotchB1();
                    var closed = WaitFor(ReadStatus, s => s.Mode != "Expanded" && s.B1 == 0, TimeSpan.FromSeconds(3), "pastila vizibilă după închidere (ciclul " + i + ")");
                    if (closed.B1r != repairs) Fail("Plasa de siguranță a trebuit să repare pastila în ciclul " + i + " (" + what + "): " + closed + "\n" + LogTail(8));
                }
                Thread.Sleep(600);                                      // a check still due after the last closing
                var end = ReadStatus();
                if (end.B1r != repairs) Fail("Plasa de siguranță a reparat ceva după ultimul ciclu: " + end + "\n" + LogTail(8));
                Console.WriteLine("      (" + B1Cycles + " cicluri, „activity-manager” " + (_activityOn ? "pornit" : "oprit") + ", toate comutatoarele noi pornite: conținut vizibil de fiecare dată, nicio reparație)");
            }
            finally
            {
                // back as before for the checks after this one (the Command Bar's own checks start with it off)
                if (!_app.HasExited)
                {
                    try
                    {
                        Command("fake-context none");
                        Command("set-context-page dev none");
                        foreach (var f in on) SwitchB1(f, false);
                    }
                    catch (SmokeFailure) { } catch (IOException) { }
                }
            }
            SettledIdle();
        }

        /// <summary>One switch through FeatureFlags ("toggle feature" flips it), waiting for its log line.</summary>
        private static void SwitchB1(string feature, bool on)
        {
            string line = "Test de fum: funcția „" + feature + "” " + (on ? "pornită" : "oprită") + ".";
            int n = LogCount(line);
            Command("toggle feature " + feature);
            WaitFor(() => LogCount(line), c => c > n, TimeSpan.FromSeconds(10), "„" + feature + "” " + (on ? "pornit" : "oprit"));
        }

        /// <summary>Win+Alt+N from whatever the pill shows now (standby or an alert): the notch open, with its page.</summary>
        private static Status OpenNotchB1()
        {
            Keyboard.TypeSimultaneously(VirtualKeyShort.LWIN, VirtualKeyShort.ALT, VirtualKeyShort.KEY_N);
            return WaitFor(ReadStatus, s => s.Mode == "Expanded" && s.Cmd == 0 && s.Page.Length > 0, TimeSpan.FromSeconds(8), "notch-ul deschis (mode=Expanded, cu ;page=)");
        }

        private static void CloseNotchB1()
        {
            Thread.Sleep(400);
            Keyboard.TypeSimultaneously(VirtualKeyShort.LWIN, VirtualKeyShort.ALT, VirtualKeyShort.KEY_N);
            WaitFor(ReadStatus, s => s.Mode != "Expanded", TimeSpan.FromSeconds(8), "notch-ul închis");
        }
    }
}
