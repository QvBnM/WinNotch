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
    }
}
