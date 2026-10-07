using System;
using System.Collections.Generic;
using System.Linq;

namespace WinNotch.Core.Flags
{
    /// <summary>How finished a feature is. Safe mode (--safe-mode) treats Experimental and Beta as off.</summary>
    public enum FeatureStage { Experimental, Beta, Stable }

    /// <summary>One switchable feature: what Settings shows and what it starts as when the user never touched it.</summary>
    public sealed class FeatureInfo
    {
        public FeatureInfo(string id, string name, string description, FeatureStage stage, bool defaultOn)
        {
            Id = id; Name = name; Description = description; Stage = stage; DefaultOn = defaultOn;
        }

        /// <summary>Stable key in settings.json ("Features"): lowercase words with dashes, never renamed once shipped.</summary>
        public string Id { get; }
        /// <summary>Shown in Settings (Romanian).</summary>
        public string Name { get; }
        /// <summary>One line, shown under the name (Romanian).</summary>
        public string Description { get; }
        public FeatureStage Stage { get; }
        /// <summary>Off until the version that announces the feature.</summary>
        public bool DefaultOn { get; }
    }

    /// <summary>
    /// Every switchable feature, declared in one place. A new feature adds one entry here (off by default), then checks
    /// <see cref="FeatureFlags.IsEnabled"/> and listens to <see cref="FeatureFlags.Changed"/> to start or stop itself.
    /// </summary>
    public static class FeatureCatalog
    {
        public const string DemoFlag = "demo-flag";
        /// <summary>Core/Context: same id as ContextEngine.FeatureId (the tests check they match).</summary>
        public const string ContextEngine = "context-engine";
        /// <summary>Core/Activity: same id as ActivityManager.FeatureId (the tests check they match).</summary>
        public const string ActivityManager = "activity-manager";
        /// <summary>Features/CommandBar: same id as CommandBarRules.FeatureId (the tests check they match).</summary>
        public const string CommandBar = "command-bar";
        /// <summary>Features/ContextPages: same id as ContextPageRules.FeatureId (the tests check they match).</summary>
        public const string ContextPages = "context-pages";
        /// <summary>Features/QuickActions: same id as QuickActionRules.FeatureId (the tests check they match).</summary>
        public const string QuickActions = "quick-actions";
        /// <summary>Features/SmartClipboard: same id as SmartClipboardActions.FeatureId (the tests check they match).</summary>
        public const string SmartClipboard = "smart-clipboard";
        /// <summary>Features/Shelf: same id as ShelfActions.FeatureId (the tests check they match).</summary>
        public const string Shelf = "shelf";
        /// <summary>Features/AudioSwitch: same id as AudioOutputRules.FeatureId (the tests check they match).</summary>
        public const string AudioSwitch = "audio-switch";
        /// <summary>Features/NotchGuard: same id as NotchGuardInfo.FeatureId (the tests check they match).</summary>
        public const string NotchGuard = "notch-guard";
        /// <summary>Features/NotchAnchored: same id as AnchoredGeometry.FeatureId (the tests check they match).</summary>
        public const string NotchAnchored = "notch-anchored";

        public static readonly IReadOnlyList<FeatureInfo> All = new[]
        {
            new FeatureInfo(DemoFlag, "Funcție de test", "Nu face nimic vizibil; verifică faptul că pornirea și oprirea funcțiilor noi merg.",
                            FeatureStage.Experimental, false),
            // no UI of its own: on by default (Beta, so --safe-mode turns it off); the features built on it have their own switches
            new FeatureInfo(ContextEngine, "Motorul de context", "Urmărește ce faci acum (aplicația din față, ecran complet, întâlniri, media) pentru funcțiile care se adaptează.",
                            FeatureStage.Beta, true),
            // P13: off by default until 0.7.0; off = the alerts go the old way, untouched
            new FeatureInfo(ActivityManager, "Manager de activități", "Alertele trec printr-o coadă cu priorități: „N noutăți” la multe deodată, pastilă împărțită, alerte discrete.",
                            FeatureStage.Experimental, false),
            // P14: off by default (no 0.7.0 yet, see docs/PLAN.md); off = no shortcut registered, nothing changes
            new FeatureInfo(CommandBar, "Command Bar", "Win+Alt+Space (sau Win+Alt+K): pastila devine un câmp de căutare pentru orice acțiune („volum 30”, „captură”, „setări poziție”).",
                            FeatureStage.Experimental, false),
            // P27: off by default; off (or the context engine off) = the notch opens on the page it was on, as before
            new FeatureInfo(ContextPages, "Pagina după context", "La deschidere, notch-ul alege pagina după ce faci (programare, browser, întâlnire, joc…), cum alegi în Setări. O pagină aleasă de tine rămâne 10 minute.",
                            FeatureStage.Experimental, false),
            // P20: off by default; off = no buttons under the pill and no suggestions, nothing subscribed
            new FeatureInfo(QuickActions, "Quick Actions", "Câteva butoane sub pastilă, la deschidere, după ce faci: în întâlnire cu căști mută microfonul, la muzică pauză, la stick deschide-l. Cu Activity Manager pornit, cel mult o sugestie la 10 minute.",
                            FeatureStage.Experimental, false),
            // P21: off by default; off = the Clipboard widget exactly as before, nothing recognized, no actions available
            new FeatureInfo(SmartClipboard, "Smart Clipboard", "Recunoaște ce copiezi (JSON, link, culoare, e-mail, IP, cale, JWT, telefon) și pune butoane mici în widget-ul Clipboard: formatează, curăță link-ul, decodează. Nimic din ce copiezi nu ajunge în log.",
                            FeatureStage.Experimental, false),
            // P23: off by default; off = no drop on the notch, no "Raft" button, the hover exactly as before
            new FeatureInfo(Shelf, "Raft", "Tragi fișiere peste notch (se deschide singur cât tragi) și le ții la îndemână: copiază calea, deschide folderul, zip, text din imagine (OCR), PNG ↔ JPG. Doar căi locale, cel mult 20.",
                            FeatureStage.Experimental, false),
            // P30: off by default; off = no button beside the volume, no list, no "audio.output-*" actions, nothing subscribed
            new FeatureInfo(AudioSwitch, "Căști/boxe", "Un buton lângă volum (pagina Acasă) și în Command Bar: alegi ieșirea audio implicită (căști, boxe, Bluetooth). Folosește o interfață Windows nedocumentată; la prima eroare se oprește singur.",
                            FeatureStage.Experimental, false),
            // B1 (0.6.18): a repair, announced with it, so Stable and on (also in --safe-mode); off = no checks, nothing repaired
            new FeatureInfo(NotchGuard, "Plasa de siguranță a notch-ului", "Dacă notch-ul se deschide gol sau pastila rămâne fără conținut, îl reface singur (pagina curentă, apoi Acasă) și scrie în log un rând „B1 recover”.",
                            FeatureStage.Stable, true),
            // P50: off by default until it is announced; off = the floating pill exactly as before (margin 8, full corners)
            new FeatureInfo(NotchAnchored, "Notch lipit de ramă", "Notch-ul crește din marginea de sus: lipit de ea, rotunjit doar jos, cu racordări în stânga și dreapta, ca o prelungire a ramei monitorului.",
                            FeatureStage.Experimental, false),
        };

        public static FeatureInfo Find(string id) => All.FirstOrDefault(f => string.Equals(f.Id, id, StringComparison.Ordinal));

        /// <summary>Romanian label for a stage, as shown in Settings.</summary>
        public static string StageName(FeatureStage s) => s switch
        {
            FeatureStage.Experimental => "Experimental",
            FeatureStage.Beta => "Beta",
            _ => "Stabil",
        };
    }
}
