using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FlaUI.Core.AutomationElements;
using WinNotch.Features.Smoke;

namespace WinNotch.Smoke
{
    public static partial class SmokeProgram
    {
        // ------------------------------------------------------------------ P30: headphones / speakers

        private const string AudioFeature = "audio-switch";
        private const string AudioAutoOff = "„" + AudioFeature + "” a fost oprită automat";

        private static AutomationElement AudioElement(string id) => _notch.FindFirstDescendant(cf => cf.ByAutomationId(id));

        private static List<AutomationElement> AudioItems() =>
            _notch.FindAllDescendants().Where(e => IdOf(e).StartsWith(SmokeMode.AudioOutputItemPrefix, StringComparison.Ordinal)).ToList();

        /// <summary>
        /// What is exercised: the switch through FeatureFlags, the list of outputs opened on the Home page
        /// ("smoke-audio-outputs", the same method as the button), the CI machine having no audio device (the list must then
        /// say „Nicio ieșire audio”; with devices, at most one marked as default), the real button beside the volume closing
        /// and opening it again (UI Automation), the process alive, the switch not turned off by itself, no error in the log.
        /// Nothing is chosen: a click would change the default output of the machine. Unit tests only: SetDefault, the
        /// automatic switch-off at the first failure, the ids, the debounce.
        /// </summary>
        private static void AudioOutputs()
        {
            SettledIdle();
            bool switchedOn = false;
            try
            {
                const string started = "Ieșire audio: pornit.", stopped = "Ieșire audio: oprit.", listed = "Test de fum: lista ieșirilor audio.";
                int on = LogCount(started), offBefore = LogCount(stopped);
                Command("toggle feature " + AudioFeature);
                switchedOn = true;
                WaitFor(() => LogCount(started), n => n > on, TimeSpan.FromSeconds(10), "„" + started + "” în log");

                OpenNotchByHotkey();
                int shown = LogCount(listed);
                Command("smoke-audio-outputs");
                WaitFor(() => LogCount(listed), n => n > shown, TimeSpan.FromSeconds(8), "„" + listed + "” în log");
                WaitFor(() => AudioElement(SmokeMode.AudioOutputsPanelAutomationId), p => p != null, TimeSpan.FromSeconds(5), "lista ieșirilor audio („" + SmokeMode.AudioOutputsPanelAutomationId + "”)");

                // no audio on the CI machine: „Nicio ieșire audio”; with devices, the rows (at most one default)
                var state = WaitFor(() => (Items: AudioItems(), Empty: AudioElement(SmokeMode.AudioOutputsEmptyAutomationId)),
                                    s => s.Items.Count > 0 || s.Empty != null, TimeSpan.FromSeconds(8), "ieșirile audio sau „Nicio ieșire audio”");
                if (state.Items.Count == 0 && (state.Empty.Properties.Name.ValueOrDefault ?? "") != "Nicio ieșire audio")
                    Fail("Lista goală nu spune „Nicio ieșire audio”.");
                int defaults = state.Items.Count(e => (e.Properties.ItemStatus.ValueOrDefault ?? "") == "default");
                if (defaults > 1) Fail("Mai multe ieșiri sunt marcate ca implicite: " + defaults + ".");
                Console.WriteLine("      (ieșiri active: " + state.Items.Count + ", implicite marcate: " + defaults + ")");      // never the names

                // the real UI path: the button beside the volume closes the list and opens it again
                var toggle = WaitFor(() => AudioElement(SmokeMode.AudioOutputsToggleAutomationId), b => b != null, TimeSpan.FromSeconds(5),
                                     "butonul „Ieșire audio” de lângă volum („" + SmokeMode.AudioOutputsToggleAutomationId + "”)");
                toggle.AsButton().Invoke();
                WaitFor(() => AudioElement(SmokeMode.AudioOutputsPanelAutomationId), p => p == null, TimeSpan.FromSeconds(5), "lista închisă de butonul de lângă volum");
                toggle.AsButton().Invoke();
                WaitFor(() => AudioElement(SmokeMode.AudioOutputsPanelAutomationId), p => p != null, TimeSpan.FromSeconds(5), "lista deschisă din nou de butonul de lângă volum");
                if (ReadStatus().Mode != "Expanded") Fail("Notch-ul s-a închis după butonul „Ieșire audio”: " + ReadStatus());
                CloseNotchByHotkey();

                Alive();
                if (LogCount(AudioAutoOff) > 0 || LogCount(stopped) != offBefore) Fail("Comutatorul „" + AudioFeature + "” s-a oprit singur.");

                // back as before: switch off
                Command("toggle feature " + AudioFeature);
                switchedOn = false;
                WaitFor(() => LogCount(stopped), n => n > offBefore, TimeSpan.FromSeconds(10), "„" + stopped + "” în log");
                SettledIdle();
            }
            finally
            {
                // a failure half-way doesn't leave the switch on for the checks after it
                if (switchedOn && !_app.HasExited) { try { Command("toggle feature " + AudioFeature); } catch (SmokeFailure) { } catch (IOException) { } }
            }
        }
    }
}
