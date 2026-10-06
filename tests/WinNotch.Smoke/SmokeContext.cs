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
        // ------------------------------------------------------------------ P27: the page by context

        private const string ContextPagesFeature = "context-pages";
        private const string ContextTargetPage = "devices";


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
    }
}
