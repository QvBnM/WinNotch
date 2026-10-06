using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using WinNotch.Features.Smoke;

namespace WinNotch.Smoke
{
    public static partial class SmokeProgram
    {
        // ------------------------------------------------------------------ P23: the shelf

        private const string ShelfFeature = "shelf";
        /// <summary>A word that must never appear in log.txt (no path or file name of the shelf is ever logged).</summary>
        private const string ShelfMarker = "zq-fum-p23-marcaj";

        private static List<AutomationElement> ShelfItems() =>
            _notch.FindAllDescendants().Where(e => IdOf(e).StartsWith(SmokeMode.ShelfItemPrefix, StringComparison.Ordinal)).ToList();

        private static AutomationElement ShelfButton(string id) =>
            _notch.FindFirstDescendant(cf => cf.ByAutomationId(id));

        /// <summary>
        /// What is exercised: the switch through FeatureFlags, a real local file made by the test going through the same
        /// checks as a drop ("smoke-shelf-add", since UI Automation can't do an OLE drag; drive, existence, shortcuts),
        /// the "Raft" button and the item found through UI Automation, „Copiază calea” through the registry
        /// ("Acțiune shelf.copy-path (UI): reușită") with the clipboard holding exactly the path, „Golește” emptying it, and
        /// nothing of the path in the log. Unit tests only: network paths and shortcuts, the 21st item, zip, OCR, PNG ↔ JPG.
        /// </summary>
        private static void Shelf()
        {
            SettledIdle();
            string dir = Path.Combine(Path.GetTempPath(), "winnotch-raft-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            string name = ShelfMarker + " ăș.txt";
            string file = Path.Combine(dir, name);
            if (file.Length > SmokeMode.MaxShelfPath) Fail("Calea fișierului de test e prea lungă pentru comanda de fum (" + file.Length + " caractere).");
            Directory.CreateDirectory(dir);
            File.WriteAllText(file, "fișier de test pentru raft", new UTF8Encoding(false));
            try
            {
                int on = LogCount("Raft: pornit");
                Command("toggle feature " + ShelfFeature);
                WaitFor(() => LogCount("Raft: pornit"), n => n > on, TimeSpan.FromSeconds(10), "„Raft: pornit” în log");

                const string added = "Raft: adăugate 1, refuzate 0";
                int before = LogCount(added);
                Command("smoke-shelf-add " + file);
                WaitFor(() => LogCount(added), n => n > before, TimeSpan.FromSeconds(10), "„" + added + "” în log (fișierul local trecut prin verificări)");

                OpenNotchByHotkey();
                var toggle = WaitFor(() => ShelfButton(SmokeMode.ShelfToggleAutomationId), b => b != null, TimeSpan.FromSeconds(5), "butonul „Raft” în antetul notch-ului");
                toggle.AsButton().Invoke();
                var item = WaitFor(() => ShelfItems().FirstOrDefault(), e => e != null, TimeSpan.FromSeconds(8), "elementul în raft („" + SmokeMode.ShelfItemPrefix + "…”)");
                if (ShelfItems().Count != 1) Fail("Raftul ar trebui să aibă exact un element; are " + ShelfItems().Count + ".");
                if ((item.Properties.Name.ValueOrDefault ?? "") != name) Fail("Elementul din raft nu are numele fișierului de test.");
                string id = IdOf(item).Substring(SmokeMode.ShelfItemPrefix.Length);

                const string ran = "Acțiune shelf.copy-path (UI): reușită";
                int copied = LogCount(ran);
                var copy = WaitFor(() => ShelfButton(SmokeMode.ShelfCopyPrefix + id), b => b != null, TimeSpan.FromSeconds(3), "„Copiază calea” („" + SmokeMode.ShelfCopyPrefix + id + "”)");
                copy.AsButton().Invoke();
                WaitFor(() => LogCount(ran), n => n > copied, TimeSpan.FromSeconds(8), "„" + ran + "” în log");
                string expected = char.ToUpperInvariant(file[0]) + file.Substring(1);            // the shelf writes the drive letter in capitals
                WaitFor(GetClipboardText, t => t == expected, TimeSpan.FromSeconds(5), "exact calea fișierului în clipboard");
                var msg = WaitFor(() => _notch.FindFirstDescendant(cf => cf.ByAutomationId(SmokeMode.ShelfMessageAutomationId)), m => m != null && (m.Properties.Name.ValueOrDefault ?? "").Length > 0,
                                  TimeSpan.FromSeconds(3), "rezultatul clickului sub titlul raftului („" + SmokeMode.ShelfMessageAutomationId + "”)");
                Console.WriteLine("      (rezultat: " + msg.Properties.Name.ValueOrDefault + ")");
                if (ReadStatus().Mode != "Expanded") Fail("Notch-ul s-a închis după „Copiază calea”: " + ReadStatus());

                const string cleared = "Acțiune shelf.clear (UI): reușită";
                int clearedBefore = LogCount(cleared);
                ShelfButton(SmokeMode.ShelfClearAutomationId).AsButton().Invoke();
                WaitFor(() => LogCount(cleared), n => n > clearedBefore, TimeSpan.FromSeconds(8), "„" + cleared + "” în log");
                WaitFor(() => ShelfItems().Count, n => n == 0, TimeSpan.FromSeconds(5), "raftul gol după „Golește”");
                if (!File.Exists(file)) Fail("„Golește” a atins fișierul de pe disc (trebuia să scoată doar referința).");
                CloseNotchByHotkey();

                // back as before: switch off; and nothing of the path in the log
                int off = LogCount("Raft: oprit.");
                Command("toggle feature " + ShelfFeature);
                WaitFor(() => LogCount("Raft: oprit."), n => n > off, TimeSpan.FromSeconds(10), "„Raft: oprit.” în log");
                if (LogCount(ShelfMarker) > 0 || LogCount(dir) > 0) Fail("Calea sau numele fișierului din raft a ajuns în log.txt.");
                SettledIdle();
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
    }
}
