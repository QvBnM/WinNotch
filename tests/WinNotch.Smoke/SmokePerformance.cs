using System;
using System.Linq;
using System.Threading;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

namespace WinNotch.Smoke
{
    public static partial class SmokeProgram
    {
        // ------------------------------------------------------------------ P60: the Performanță tab

        private const string PerfFeature = "perf-monitor";
        private const string WindowV2Feature = "window-v2";
        private const string PerfTabTitle = "Performanță";

        /// <summary>
        /// What is exercised: the fifth tab appears only with its switch on (and only inside the new window), it opens
        /// without throwing, the sampler starts and — the part that matters — stops when the window closes, so nothing
        /// measures in the background. Not exercised here (unit tests only): the numbers themselves, the cadences and
        /// the leak trend, which are pure and tested in PF1–PF65.
        /// </summary>
        private static void PerformanceTab()
        {
            SettledIdle();

            // The window is the classic one until window-v2 is on, and the tab lives only in the new one.
            int v2On = LogCount("Test de fum: funcția „" + WindowV2Feature + "” pornită.");
            Command("toggle feature " + WindowV2Feature);
            WaitFor(() => LogCount("Test de fum: funcția „" + WindowV2Feature + "” pornită."), n => n > v2On,
                    TimeSpan.FromSeconds(10), "„" + WindowV2Feature + "” pornit");

            // Switch off first: the new window must have exactly the four permanent tabs and no empty fifth one.
            ClickMenuItem("Pagini, teme și setări…");
            var window = WaitFor(FindEditor, w => w != null, TimeSpan.FromSeconds(20), "fereastra WinNotch v2");
            if (FindTab(window, PerfTabTitle) != null)
                Fail("Cu „" + PerfFeature + "” oprit, fila „" + PerfTabTitle + "” e totuși în antet.");
            foreach (var title in new[] { "Workspace", "Widgeturi", "Teme", "Setări" })
                if (FindTab(window, title) == null) Fail("Fila „" + title + "” lipsește din antetul ferestrei v2.");
            Console.WriteLine("      (oprit: patru file, fără a cincea)");

            int perfOn = LogCount("Test de fum: funcția „" + PerfFeature + "” pornită.");
            Command("toggle feature " + PerfFeature);
            WaitFor(() => LogCount("Test de fum: funcția „" + PerfFeature + "” pornită."), n => n > perfOn,
                    TimeSpan.FromSeconds(10), "„" + PerfFeature + "” pornit");

            // The switch is read live: the tab must appear without reopening the window.
            var tab = WaitFor(() => FindTab(window, PerfTabTitle), t => t != null, TimeSpan.FromSeconds(10),
                              "fila „" + PerfTabTitle + "” apărută fără să redeschid fereastra");
            tab.AsButton().Invoke();
            Thread.Sleep(2500);                       // two cadences of 2 s would be 4 s; one sample is enough here
            if (FindEditor() == null) Fail("Fereastra WinNotch s-a închis când am deschis fila „" + PerfTabTitle + "”.");

            // Turning it off again while the tab is open must not leave a dead tab or close the window.
            int perfOff = LogCount("Test de fum: funcția „" + PerfFeature + "” oprită.");
            Command("toggle feature " + PerfFeature);
            WaitFor(() => LogCount("Test de fum: funcția „" + PerfFeature + "” oprită."), n => n > perfOff,
                    TimeSpan.FromSeconds(10), "„" + PerfFeature + "” oprit");
            var gone = WaitFor(FindEditor, w => w != null && FindTab(w, PerfTabTitle) == null, TimeSpan.FromSeconds(10),
                               "fila „" + PerfTabTitle + "” dispărută, cu fereastra încă deschisă");
            gone.AsWindow().Close();
            WaitFor(FindEditor, w => w == null, TimeSpan.FromSeconds(10), "fereastra WinNotch închisă");

            int v2Off = LogCount("Test de fum: funcția „" + WindowV2Feature + "” oprită.");
            Command("toggle feature " + WindowV2Feature);
            WaitFor(() => LogCount("Test de fum: funcția „" + WindowV2Feature + "” oprită."), n => n > v2Off,
                    TimeSpan.FromSeconds(10), "„" + WindowV2Feature + "” oprit");
            SettledIdle();
        }

        /// <summary>A tab button of the v2 header, by the title the header shows (AutomationProperties.SetName).</summary>
        private static AutomationElement FindTab(AutomationElement window, string title)
        {
            try
            {
                return window.FindAllDescendants(cf => cf.ByControlType(ControlType.Button))
                             .FirstOrDefault(b => (b.Properties.Name.ValueOrDefault ?? "") == title);
            }
            catch (Exception) { return null; }
        }
    }
}
