using System;
using System.Threading;

namespace WinNotch.Smoke
{
    public static partial class SmokeProgram
    {
        // ------------------------------------------------------------------ P61: the game mode

        private const string GameFeature = "game-session";

        /// <summary>
        /// What is exercised: switching the game mode on and off, repeatedly, against the real feature flags, the real
        /// context engine and the real performance monitor — which is the path that subscribes and unsubscribes from
        /// both. The R1 review pointed out that this is the one automated test that could have caught the subscription
        /// race it found (a stopped watcher still listening, and a second start leaking the first handler), so it
        /// toggles more than once on purpose.
        /// <para>Not exercised here: a real game. There is no fullscreen game in a CI container, so the session itself
        /// is covered by GM1–GM53 against the engine's fake sources. What this proves is that the feature can be
        /// started and stopped on a live app without throwing and without leaving anything behind.</para>
        /// </summary>
        private static void GameMode()
        {
            SettledIdle();

            // The game mode needs the performance monitor too (every number in its report comes from there).
            int perfOn = LogCount("Test de fum: funcția „" + PerfFeature + "” pornită.");
            Command("toggle feature " + PerfFeature);
            WaitFor(() => LogCount("Test de fum: funcția „" + PerfFeature + "” pornită."), n => n > perfOn,
                    TimeSpan.FromSeconds(10), "„" + PerfFeature + "” pornit");

            for (int round = 1; round <= 3; round++)
            {
                int on = LogCount("Test de fum: funcția „" + GameFeature + "” pornită.");
                Command("toggle feature " + GameFeature);
                WaitFor(() => LogCount("Test de fum: funcția „" + GameFeature + "” pornită."), n => n > on,
                        TimeSpan.FromSeconds(10), "„" + GameFeature + "” pornit (runda " + round + ")");

                int off = LogCount("Test de fum: funcția „" + GameFeature + "” oprită.");
                Command("toggle feature " + GameFeature);
                WaitFor(() => LogCount("Test de fum: funcția „" + GameFeature + "” oprită."), n => n > off,
                        TimeSpan.FromSeconds(10), "„" + GameFeature + "” oprit (runda " + round + ")");
            }

            // No game ever ran, so nothing may have started or ended a session.
            if (LogCount("Mod de joc: pornit.") > 0) Fail("Fără niciun joc, modul de joc a pornit totuși o sesiune.");
            if (LogCount("Mod de joc: oprit") > 0) Fail("Fără niciun joc, modul de joc a încheiat o sesiune.");

            // The pill must still be alive and the shortcut must still work: a leaked subscription usually shows up
            // as a dead notch rather than as an exception.
            var status = SettledIdle();
            if (status.Mode != "Idle") Fail("După pornirea și oprirea modului de joc, notch-ul nu e în standby: " + status);
            OpenNotchByHotkey();
            CloseNotchByHotkey();

            int perfOff = LogCount("Test de fum: funcția „" + PerfFeature + "” oprită.");
            Command("toggle feature " + PerfFeature);
            WaitFor(() => LogCount("Test de fum: funcția „" + PerfFeature + "” oprită."), n => n > perfOff,
                    TimeSpan.FromSeconds(10), "„" + PerfFeature + "” oprit");
            SettledIdle();
        }
    }
}
