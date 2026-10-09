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
        /// <summary>Features/Diagnostics: same id as ShutdownJournal.FeatureId (the tests check they match).</summary>
        public const string ShutdownReport = "shutdown-report";
        /// <summary>Features/Fullscreen: same id as FullscreenRules.FeatureId (the tests check they match).</summary>
        public const string FullscreenHide = "fullscreen-hide";
        /// <summary>Core/Ui + Features/Overlays: same id as OverlayStack.FeatureId (the tests check they match).</summary>
        public const string OverlayDismiss = "overlay-dismiss";
        /// <summary>Core/Ui + Features/AlertInterrupt: same id as InterruptRules.FeatureId (the tests check they match).</summary>
        public const string AlertInterrupt = "alert-interrupt";
        /// <summary>Features/NotchAnchored: same id as AnchoredGeometry.FeatureId (the tests check they match).</summary>
        public const string NotchAnchored = "notch-anchored";
        /// <summary>Features/WindowV2: same id as LayoutRules.FeatureId (the tests check they match).</summary>
        public const string WindowV2 = "window-v2";

        public static readonly IReadOnlyList<FeatureInfo> All = new[]
        {
            new FeatureInfo(DemoFlag, "Funcție de test", "Doar pentru teste: nu face nimic vizibil, nici pornită, nici oprită. Verifică doar că pornirea și oprirea funcțiilor merg.",
                            FeatureStage.Experimental, false),
            // no UI of its own: on by default (Beta, so --safe-mode turns it off); the features built on it have their own switches
            new FeatureInfo(ContextEngine, "Motorul de context", "Urmărește ce faci acum (aplicația din față, ecran complet, întâlniri, media) pentru funcțiile care se adaptează. Pornită implicit. Nu are ecran propriu: o vezi doar prin „Pagina după context” și „Quick Actions”, care fără ea nu pornesc.",
                            FeatureStage.Beta, true),
            // P13: off by default until 0.7.0; off = the alerts go the old way, untouched
            new FeatureInfo(ActivityManager, "Manager de activități", "Alertele trec printr-o coadă cu priorități: „N noutăți” la multe deodată, pastilă împărțită, alerte discrete. Se vede doar când vin două sau mai multe alerte aproape una de alta; cu una singură arată ca înainte.",
                            FeatureStage.Experimental, false),
            // P14: off by default (no 0.7.0 yet, see docs/PLAN.md); off = no shortcut registered, nothing changes
            new FeatureInfo(CommandBar, "Command Bar", "Win+Alt+Space (sau Win+Alt+K): pastila devine un câmp de căutare pentru orice acțiune („volum 30”, „captură”, „setări poziție”). Se vede apăsând scurtătura; dacă e luată de altă aplicație, alege-o pe cealaltă mai sus.",
                            FeatureStage.Experimental, false),
            // P27: off by default; off (or the context engine off) = the notch opens on the page it was on, as before
            new FeatureInfo(ContextPages, "Pagina după context", "La deschidere, notch-ul alege pagina după ce faci (programare, browser, întâlnire, joc…), cum alegi în Setări. Merge doar cu „Motorul de context” pornit și cu o alegere făcută mai sus la „Pagina după context”; o pagină aleasă de tine în notch o oprește 10 minute.",
                            FeatureStage.Experimental, false),
            // P20: off by default; off = no buttons under the pill and no suggestions, nothing subscribed
            new FeatureInfo(QuickActions, "Quick Actions", "Câteva butoane sub pastilă, la deschidere, după ce faci: în întâlnire cu căști mută microfonul, la muzică pauză, la stick deschide-l. Merge doar cu „Motorul de context” pornit, iar butoanele apar numai când contextul le cere (întâlnire, muzică, stick) — pe un desktop liniștit nu apare nimic.",
                            FeatureStage.Experimental, false),
            // P21: off by default; off = the Clipboard widget exactly as before, nothing recognized, no actions available
            new FeatureInfo(SmartClipboard, "Smart Clipboard", "Recunoaște ce copiezi (JSON, link, culoare, e-mail, IP, cale, JWT, telefon) și pune butoane mici în widget-ul Clipboard: formatează, curăță link-ul, decodează. Se vede doar în widget-ul Clipboard (pe o pagină care îl are) și doar după ce copiezi ceva recunoscut. Nimic din ce copiezi nu ajunge în log.",
                            FeatureStage.Experimental, false),
            // P23: off by default; off = no drop on the notch, no "Raft" button, the hover exactly as before
            new FeatureInfo(Shelf, "Raft", "Tragi fișiere peste notch (se deschide singur cât tragi) și le ții la îndemână: copiază calea, deschide folderul, zip, text din imagine (OCR), PNG ↔ JPG. Se vede doar trăgând fișiere peste notch. Doar căi locale, cel mult 20.",
                            FeatureStage.Experimental, false),
            // P30: off by default; off = no button beside the volume, no list, no "audio.output-*" actions, nothing subscribed
            new FeatureInfo(AudioSwitch, "Căști/boxe", "Un buton lângă volum (pagina Acasă) și în Command Bar: alegi ieșirea audio implicită (căști, boxe, Bluetooth). Butonul apare pe pagina Acasă din notch, lângă volum, deci doar dacă pagina Acasă e vizibilă (nu e ascunsă în Setări → Pagini). Folosește o interfață Windows nedocumentată; la prima eroare se oprește singur.",
                            FeatureStage.Experimental, false),
            // B1 (0.6.18): a repair, announced with it, so Stable and on (also in --safe-mode); off = no checks, nothing repaired
            new FeatureInfo(NotchGuard, "Plasa de siguranță a notch-ului", "Dacă notch-ul se deschide gol sau pastila rămâne fără conținut, îl reface singur (pagina curentă, apoi Acasă) și scrie în log un rând „B1 recover”. Pornită implicit; nu se vede decât atunci când ceva s-a stricat.",
                            FeatureStage.Stable, true),
            // P51c: a repair, so Beta and on; the switch is the net. Off = the log still names every closure, but the
            // notch says nothing about the ones it cannot explain.
            new FeatureInfo(ShutdownReport, "Raportul închiderilor", "Dacă WinNotch s-a închis singur de două ori la rând, notch-ul te anunță și îți deschide log-ul. Pornită implicit; alerta apare doar după două închideri neexplicate la rând, deci de obicei niciodată. Fiecare ieșire scrie în log motivul ei.",
                            FeatureStage.Beta, true),
            // P53: a repair of a behaviour, on by default (Beta, so --safe-mode turns it off); off = "busy" read the old way,
            // so a browser in fullscreen only makes the pill small, and every alert gets through as before
            new FeatureInfo(FullscreenHide, "Ascuns pe tot ecranul", "Peste un joc sau un film pe tot ecranul notch-ul dispare complet; coboară scurt doar pentru ceva important (baterie, temperatură, memorie, rezultatul unei unelte). Pornită implicit, deci bifarea ei nu schimbă nimic. Ca s-o vezi: „Peste jocuri / fullscreen” (mai sus) pe „ascuns”, aplicația să acopere tot monitorul fără ramă, și să nu ai un al doilea monitor liber — atunci notch-ul se mută acolo în loc să dispară.",
                            FeatureStage.Beta, true),
            // P51: a repair of a behaviour, on by default (Beta, so --safe-mode turns it off); off = the panels close only
            // through their own buttons, as before, and no key or click outside is read
            new FeatureInfo(OverlayDismiss, "Închiderea panourilor", "Un panou deschis (ieșire audio, raft, galerie, mărimi) se închide și la click în afara lui și la Esc, nu doar cu butonul lui. Pornită implicit; o oprești doar dacă îți încurcă ceva.",
                            FeatureStage.Beta, true),
            // P51b: a repair of a behaviour, on by default (Beta, so --safe-mode turns it off); off = an alert holds the
            // pill until it ends by itself, as before
            new FeatureInfo(AlertInterrupt, "Alertele nu stau în cale", "O alertă se dă la o parte imediat ce faci ceva: tragi fișiere peste notch, apeși scurtătura sau deschizi Command Bar-ul. Pornită implicit; se vede doar cât e o alertă pe ecran.",
                            FeatureStage.Beta, true),
            // P50: off by default until it is announced; off = the floating pill exactly as before (margin 8, full corners)
            new FeatureInfo(NotchAnchored, "Notch lipit de ramă", "Notch-ul crește din marginea de sus: lipit de ea, rotunjit doar jos, cu racordări în stânga și dreapta, ca o prelungire a ramei monitorului. Se vede în standby și cu notch-ul deschis; peste o fereastră maximizată pastila trece oricum la forma mică.",
                            FeatureStage.Experimental, false),
            // P52: off by default until it is finished; off = the window you know opens, untouched
            new FeatureInfo(WindowV2, "Fereastra WinNotch v2", "Fereastra nouă: antetul e notch-ul desfăcut, o coloană cu categorii, carduri pentru acțiunile care există și o coloană cu clipboard și confidențialitate. Se vede la următoarea deschidere a ferestrei WinNotch (din notch sau din meniul iconiței).",
                            FeatureStage.Experimental, false),
        };

        public static FeatureInfo Find(string id) => All.FirstOrDefault(f => string.Equals(f.Id, id, StringComparison.Ordinal));

        /// <summary>
        /// The short note beside a feature's name in Settings, or null. A feature that is already on when the user never
        /// touched it has to say so: otherwise ticking its box changes nothing and the switch looks dead (reported by the
        /// author about "Ascuns pe tot ecranul", which was on all along).
        /// </summary>
        public static string RowNote(FeatureInfo f) => f != null && f.DefaultOn ? "pornită implicit" : null;

        /// <summary>Romanian label for a stage, as shown in Settings.</summary>
        public static string StageName(FeatureStage s) => s switch
        {
            FeatureStage.Experimental => "Experimental",
            FeatureStage.Beta => "Beta",
            _ => "Stabil",
        };
    }
}
