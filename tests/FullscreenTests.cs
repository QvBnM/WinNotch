using System;
using System.Linq;
using WinNotch.Core.Flags;
using WinNotch.Features.Activity;
using WinNotch.Features.Fullscreen;

namespace WinNotch
{
    public static partial class T
    {
        /// <summary>
        /// P53: over an app that fills the screen the notch must disappear completely. The reported bug (a video on
        /// YouTube left the notch perched there) came from <c>Busy = covers &amp;&amp; !IsZoomed</c>: a maximized browser
        /// keeps WS_MAXIMIZE when it goes fullscreen. FS1 is the test that failed before the repair, with the exact
        /// rectangles from the user's log.
        /// </summary>
        static void FullscreenTests()
        {
            FullscreenClassify();
            FullscreenAlerts();
            FullscreenDeferred();
            FullscreenSourcePins();
        }

        // The monitor from the log: 2560x1440, taskbar 40 px tall at the bottom.
        static readonly Box Mon = Box.At(-2560, 0, 2560, 1440);
        static readonly Box Work = new Box(-2560, 0, 0, 1400);

        static MonitorUse? Use(Box win, bool zoomed, bool caption = true, bool thick = true, bool strict = true, Box? work = null) =>
            FullscreenRules.Classify(win, Mon, work ?? Work, zoomed, caption, thick, strict);

        static void FullscreenClassify()
        {
            // The two lines from the user's log, as they were written there.
            var maximizedChrome = Box.At(-2568, -8, 2576, 1408);
            var fullscreenChrome = Box.At(-2560, 0, 2560, 1440);

            Check("FS1", "Chrome pe tot ecranul (acoperă monitorul, dar IsZoomed e tot true) → ocupat, nu maximizat",
                  Use(fullscreenChrome, zoomed: true) == MonitorUse.Busy);
            Check("FS2", "Chrome maximizat normal → maximizat (lasă bara de activități liberă)",
                  Use(maximizedChrome, zoomed: true) == MonitorUse.Maximized);
            Check("FS3", "Joc fără ramă pe tot ecranul (nu e zoomed) → ocupat",
                  Use(fullscreenChrome, zoomed: false, caption: false, thick: false) == MonitorUse.Busy);
            Check("FS4", "Fereastră mică → nu decide nimic (se caută mai jos)",
                  Use(Box.At(-1200, 300, 500, 400), zoomed: false) == null);
            Check("FS5", "Fereastră de 0 px sau monitor gol → nu decide nimic",
                  Use(Box.At(-100, 100, 0, 0), zoomed: false) == null &&
                  FullscreenRules.Classify(fullscreenChrome, default, default, true, true, true, true) == null);
            Check("FS6", "Fereastră care umple zona de lucru fără să fie zoomed (maximizare „falsă”) → maximizat",
                  Use(new Box(-2558, 2, -2, 1398), zoomed: false) == MonitorUse.Maximized);
            Check("FS7", "Bara de activități ascunsă (zona de lucru = monitorul): doar o fereastră fără ramă e ocupat",
                  Use(fullscreenChrome, zoomed: true, work: Mon) == MonitorUse.Maximized &&
                  Use(fullscreenChrome, zoomed: true, caption: false, thick: false, work: Mon) == MonitorUse.Busy);
            Check("FS8", "Comutator oprit → regula veche (acoperă și nu e zoomed): Chrome pe tot ecranul rămâne maximizat",
                  Use(fullscreenChrome, zoomed: true, strict: false) == MonitorUse.Maximized &&
                  Use(fullscreenChrome, zoomed: false, strict: false) == MonitorUse.Busy);
            Check("FS9", "Desktopul, shell-ul și fereastra proprie sunt sărite; o fereastră normală nu",
                  FullscreenRules.Ignored("Progman", false, true, false, Mon) &&
                  FullscreenRules.Ignored("Shell_TrayWnd", false, true, false, Mon) &&
                  FullscreenRules.Ignored("Chrome_WidgetWin_1", true, true, false, Mon) &&
                  FullscreenRules.Ignored("Chrome_WidgetWin_1", false, false, false, Mon) &&
                  FullscreenRules.Ignored("Chrome_WidgetWin_1", false, true, true, Mon) &&
                  FullscreenRules.Ignored("Chrome_WidgetWin_1", false, true, false, Box.At(0, 0, 0, 0)) &&
                  !FullscreenRules.Ignored("Chrome_WidgetWin_1", false, true, false, Mon));
            Check("FS10", "Comutatorul din catalog are id-ul regulilor, e Beta și pornit implicit",
                  FullscreenRules.FeatureId == FeatureCatalog.FullscreenHide &&
                  FeatureCatalog.Find(FullscreenRules.FeatureId) is FeatureInfo fi && fi.Stage == FeatureStage.Beta && fi.DefaultOn);
        }

        static void FullscreenAlerts()
        {
            HiddenAlert A(string id) => FullscreenRules.ForAlert(id, LegacyAlerts.Find(id)?.Important ?? false);

            bool shown = A(LegacyAlerts.BatteryLow) == HiddenAlert.Show && A(LegacyAlerts.TempHot) == HiddenAlert.Show &&
                         A(LegacyAlerts.RamProgress) == HiddenAlert.Show && A(LegacyAlerts.RamDone) == HiddenAlert.Show &&
                         A(LegacyAlerts.CaptureResult) == HiddenAlert.Show && A(LegacyAlerts.OcrDone) == HiddenAlert.Show;
            Check("FS11", "Trec peste fullscreen: baterie, temperatură, memorie, rezultatul unei unelte cerute de utilizator", shown);

            Check("FS12", "Se amână piesa nouă (fără butoane); cele cu butoane se sar, fiindcă porțile lor le oferă din nou mai târziu",
                  A(LegacyAlerts.Track) == HiddenAlert.Defer &&
                  A(LegacyAlerts.EyeBreak) == HiddenAlert.Drop && A(LegacyAlerts.UpdateOffer) == HiddenAlert.Drop &&
                  A(LegacyAlerts.WhatsNew) == HiddenAlert.Drop);

            Check("FS13", "Se sar: volum, alimentare, extensia veche, un id necunoscut neimportant",
                  A(LegacyAlerts.Volume) == HiddenAlert.Drop && A(LegacyAlerts.Power) == HiddenAlert.Drop &&
                  A(LegacyAlerts.OldExtension) == HiddenAlert.Drop && FullscreenRules.ForAlert("nu.exista", false) == HiddenAlert.Drop &&
                  FullscreenRules.ForAlert(null, false) == HiddenAlert.Drop && FullscreenRules.ForAlert("nu.exista", true) == HiddenAlert.Show);

            Check("FS14", "Alertele importante din tabel trec sau se amână; cele cu butoane care se amână sunt sărite, nu pierdute (au poarta lor)",
                  LegacyAlerts.All.Where(a => a.Important && !a.Interactive).All(a => A(a.Id) != HiddenAlert.Drop));

            Check("FS15", "Popup-ul peste fullscreen e scurt (2,5 s); butoanele și pașii unui flux își păstrează durata",
                  FullscreenRules.DurationWhileHidden(LegacyAlerts.BatteryLow, 5000) == FullscreenRules.PeekMs &&
                  FullscreenRules.DurationWhileHidden(LegacyAlerts.TempHot, 5000) == FullscreenRules.PeekMs &&
                  FullscreenRules.DurationWhileHidden(LegacyAlerts.OcrReading, 30000) == 30000 &&
                  FullscreenRules.DurationWhileHidden(LegacyAlerts.RamProgress, 60000) == 60000 &&
                  FullscreenRules.DurationWhileHidden(LegacyAlerts.UpdateDownload, 600000) == 600000 &&
                  FullscreenRules.DurationWhileHidden(LegacyAlerts.CaptureResult, 5000) == 5000 &&
                  FullscreenRules.DurationWhileHidden("nu.exista", 9000) == FullscreenRules.PeekMs &&
                  FullscreenRules.DurationWhileHidden(LegacyAlerts.Volume, 1600) == 1600 && FullscreenRules.PeekMs == 2500);

            Check("FS16", "O fereastră ocupată poate fi și maximizată: setarea „mereu vizibil” păstrează pastila mică",
                  FullscreenRules.LooksMaximized(Box.At(-2560, 0, 2560, 1440), Mon, Work, zoomed: true) &&
                  !FullscreenRules.LooksMaximized(Box.At(-2560, 0, 2560, 1440), Mon, Work, zoomed: false) &&
                  FullscreenRules.LooksMaximized(new Box(-2558, 2, -2, 1398), Mon, Work, zoomed: false) &&
                  !FullscreenRules.LooksMaximized(Box.At(-1200, 300, 500, 400), Mon, Work, zoomed: false) &&
                  !FullscreenRules.LooksMaximized(default, Mon, Work, zoomed: true));

            Check("FS17", "Cât e ascuns: hover-ul și tragerea nu deschid; scurtătura, Command Bar și tray-ul da",
                  !FullscreenRules.Opens(HiddenTrigger.Hover) && !FullscreenRules.Opens(HiddenTrigger.Drag) &&
                  FullscreenRules.Opens(HiddenTrigger.Shortcut) && FullscreenRules.Opens(HiddenTrigger.CommandBar) &&
                  FullscreenRules.Opens(HiddenTrigger.Tray));
        }

        static void FullscreenDeferred()
        {
            var now = new DateTime(2026, 1, 1, 20, 0, 0);
            var d = new DeferredAlerts();
            d.Note(LegacyAlerts.Track, now);
            d.Note(LegacyAlerts.EyeBreak, now.AddSeconds(5));
            d.Note(LegacyAlerts.Track, now.AddSeconds(30));          // a newer track replaces the old one
            Check("FS18", "Cel mult una de fiecare fel, cea mai recentă", d.Count == 2);

            Check("FS19", "Cât e ascuns nu se arată nimic", d.Release(now.AddMinutes(10)).Count == 0);

            d.Freed(now.AddMinutes(10));
            Check("FS20", "Nici imediat după ieșirea din fullscreen: se așteaptă 2 secunde",
                  d.Release(now.AddMinutes(10).AddSeconds(1)).Count == 0 && FullscreenRules.ReleaseDelay == TimeSpan.FromSeconds(2));

            var due = d.Release(now.AddMinutes(10).AddSeconds(2));
            Check("FS21", "La 2 secunde se dau, cea mai veche prima, și o singură dată",
                  due.Count == 2 && due[0] == LegacyAlerts.EyeBreak && due[1] == LegacyAlerts.Track &&
                  d.Release(now.AddMinutes(11)).Count == 0 && d.Count == 0);

            var d2 = new DeferredAlerts();
            d2.Note(LegacyAlerts.Track, now);
            d2.Freed(now);
            d2.Hidden();                                             // fullscreen again before the delay was over
            Check("FS22", "Dacă intră iar în fullscreen, numărătoarea începe de la capăt",
                  d2.Release(now.AddSeconds(30)).Count == 0);

            var d3 = new DeferredAlerts();
            for (int i = 0; i < DeferredAlerts.MaxKinds + 4; i++) d3.Note("alerta-" + i, now.AddSeconds(i));
            d3.Note(null, now); d3.Note("", now);
            Check("FS23", "Coada nu crește peste limită și ignoră id-urile goale", d3.Count == DeferredAlerts.MaxKinds);

            d3.Clear();
            Check("FS24", "Clear uită tot (notch-ul a fost deschis de utilizator)", d3.Count == 0);
        }

        /// <summary>The hooks in the old files: few lines, and the window's title stays out of the log.</summary>
        static void FullscreenSourcePins()
        {
            string notch = Src("NotchWindow.xaml.cs"), mons = Src("Services/MonitorService.cs"),
                   act = Src("Features/Activity/NotchWindow.Activity.cs");

            Check("FS25", "MonitorService decide prin FullscreenRules.Classify, cu comutatorul citit o dată pe scanare",
                  mons.Contains("FullscreenRules.Classify(") && mons.Contains("FeatureFlags.Current?.IsEnabled(FullscreenRules.FeatureId)") &&
                  mons.Contains("FullscreenRules.Ignored(") && !mons.Contains("covers && !Native.IsZoomed(h)") &&
                  mons.Contains("mon.Maximized = use == MonitorUse.Maximized || FullscreenRules.LooksMaximized("));

            // P51c scrie rândul prin Describe (clasa, procesul și dreptunghiul): oricare dintre ele, dar niciodată titlul
            Check("FS26", "Titlul ferestrei nu mai ajunge în log (doar clasa, procesul și dreptunghiul)",
                  !mons.Contains("Native.Title(") &&
                  (mons.Contains("mon.Decider = cls + \" \" + box;") || mons.Contains("mon.Decider = Describe(h, cls, wr);")));

            Check("FS27", "Legăturile din fișierele vechi: alertele, ascunderea și uitarea la deschiderea manuală",
                  act.Contains("if (!FullscreenAllows(id, content, w, h, ref ms, important)) return false;") &&
                  notch.Contains("if (_hidden && !FullscreenOpens(Features.Fullscreen.HiddenTrigger.Hover)) { ClearDwell(); return; }") &&
                  notch.Contains("FullscreenTick();") && notch.Contains("FullscreenForget();"));
        }
    }
}
