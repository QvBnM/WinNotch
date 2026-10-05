using System;
using System.Linq;
using WinNotch.Features.Smoke;

namespace WinNotch
{
    public static partial class T
    {
        /// <summary>The --smoke switch and its command file (P02): parsing only; the app side runs in CI (tests/WinNotch.Smoke).</summary>
        static void SmokeModeTests()
        {
            SmokeMode.Init(new[] { "--safe-mode" });
            bool offWithout = !SmokeMode.On;
            SmokeMode.Init(new[] { "--safe-mode", "--smoke" });
            bool onWith = SmokeMode.On;
            SmokeMode.Init(new[] { "--SMOKE", "smoke" });
            bool exactOnly = !SmokeMode.On;
            SmokeMode.Init(null);
            Check("SM1", "--smoke pornește modul de test doar când e dat exact (nu „--SMOKE”, nu „smoke”, nu alte argumente)", offWithout && onWith && exactOnly && !SmokeMode.On);

            Check("SM2", "Comenzi valide: alertă volum / piesă (ro și en), comutator de funcție; majusculele și spațiile nu contează",
                  SmokeMode.Parse("post-alert volume")?.Kind == SmokeCommandKind.VolumeAlert && SmokeMode.Parse("  POST-ALERT   volum ")?.Kind == SmokeCommandKind.VolumeAlert &&
                  SmokeMode.Parse("post-alert track")?.Kind == SmokeCommandKind.TrackAlert && SmokeMode.Parse("post-alert piesă")?.Kind == SmokeCommandKind.TrackAlert &&
                  SmokeMode.Parse("toggle feature context-engine") is { Kind: SmokeCommandKind.ToggleFeature, Argument: "context-engine" });
            Check("SM3", "Comenzi invalide ignorate: necunoscute, id-uri cu semne, căi, prea lungi, goale",
                  new[] { "", "   ", null, "post-alert", "post-alert boom", "toggle feature", "toggle feature ../x", "toggle feature Context Engine", "toggle feature a b",
                          "delete settings", "toggle feature " + new string('a', 70), "post-alert volume " + new string('x', 300) }.All(l => SmokeMode.Parse(l) == null));
            var many = Enumerable.Repeat("post-alert volume", 50).Concat(new[] { "toggle feature demo-flag" });
            var parsed = SmokeMode.ParseAll(new[] { "post-alert track", "gunoi", "toggle feature demo-flag" });
            Check("SM4", "Fișierul: doar liniile valide, în ordine; cel mult 20 de linii citite",
                  parsed.Count == 2 && parsed[0].Kind == SmokeCommandKind.TrackAlert && parsed[1].Argument == "demo-flag" &&
                  SmokeMode.ParseAll(many).Count == SmokeMode.MaxLines && SmokeMode.ParseAll(null).Count == 0);
            bool ok = SmokeMode.TryParseStatus(SmokeMode.Status("Live", 359.6, 54.2), out var mode, out int w, out int h);
            Check("SM5", "Starea pentru UI Automation: „mode=Live;pill=360x54” se scrie și se citește înapoi; altceva e refuzat",
                  ok && mode == "Live" && w == 360 && h == 54 && SmokeMode.Status("Idle", 180, 32) == "mode=Idle;pill=180x32" &&
                  !SmokeMode.TryParseStatus("", out _, out _, out _) && !SmokeMode.TryParseStatus("mode=Idle", out _, out _, out _));
            // R1 (P02): errors FeatureFlags catches from a Changed handler, an automatic switch-off and a failed test command
            // used to pass the smoke test's "no exception in log.txt" check
            Check("SM6", "Log-ul testului de fum: erorile de comutator, oprirea automată, comenzile eșuate și stivele pică testul; excepțiile tratate nu",
                  new[] { "12:00 Eroare la schimbarea funcției „context-engine”: NullReferenceException",
                          "12:00 Funcția „context-engine” a fost oprită automat: prea multe erori",
                          "12:00 Test de fum: comanda a dat eroare: InvalidOperationException",
                          "12:00 Eroare neprevăzută: System.InvalidOperationException: x", "   at WinNotch.App.OnStartup()",
                          "12:00 Eroare în funcția „context-engine”: COMException" }.All(SmokeMode.IsFatalLogLine) &&
                  !SmokeMode.IsFatalLogLine("12:00 Temperaturi: COMException") && !SmokeMode.IsFatalLogLine("12:00 Test de fum: alertă de volum.") &&
                  !SmokeMode.IsFatalLogLine("") && !SmokeMode.IsFatalLogLine(null));

            // P13: the Activity Manager's test commands and status fields
            Check("SM7", "Comenzi P13: activitate persistentă 1–3, rafală de 1–10 alerte, activitate Low, închide activitățile",
                  SmokeMode.Parse("post-activity persistent 1") is { Kind: SmokeCommandKind.PersistentActivity, Number: 1 } &&
                  SmokeMode.Parse(" POST-ACTIVITY  persistent 3") is { Kind: SmokeCommandKind.PersistentActivity, Number: 3 } &&
                  SmokeMode.Parse("post-activity burst 5") is { Kind: SmokeCommandKind.BurstActivity, Number: 5 } &&
                  SmokeMode.Parse("post-activity burst 10") is { Kind: SmokeCommandKind.BurstActivity, Number: 10 } &&
                  SmokeMode.Parse("post-activity low")?.Kind == SmokeCommandKind.LowActivity && SmokeMode.Parse("dismiss-activities")?.Kind == SmokeCommandKind.DismissActivities);
            Check("SM8", "Comenzi P13 invalide ignorate: număr lipsă, 0, prea mare, negativ, cu semne, cuvinte în plus",
                  new[] { "post-activity", "post-activity persistent", "post-activity persistent 0", "post-activity persistent 4", "post-activity burst 11",
                          "post-activity burst -1", "post-activity burst 5x", "post-activity burst ５", "post-activity burst 0005", "post-activity low 2",
                          "post-activity split 1", "dismiss-activities now", "dismiss activities" }.All(l => SmokeMode.Parse(l) == null));
            bool e1 = SmokeMode.TryParseStatus(SmokeMode.Status("Live", 360, 40, split: 1), out var m1, out int w1, out _, out var x1);
            bool e2 = SmokeMode.TryParseStatus(SmokeMode.Status("Live", 230, 40, group: 5), out _, out _, out _, out var x2);
            bool e3 = SmokeMode.TryParseStatus(SmokeMode.Status("Live", 240, 34, peek: 1), out _, out _, out _, out var x3);
            bool e4 = SmokeMode.TryParseStatus("mode=Idle;pill=180x32", out _, out _, out _, out var x4);
            bool ext = e1 && e2 && e3 && e4;
            Check("SM9", "Starea cu câmpurile P13: „;split=1”, „;group=5”, „;peek=1” se scriu și se citesc; fără ele, formatul vechi; câmpuri necunoscute → refuzat",
                  ext && m1 == "Live" && w1 == 360 && x1["split"] == 1 && x1["group"] == 0 && x2["group"] == 5 && x3["peek"] == 1 && x4["split"] == 0 &&
                  SmokeMode.Status("Live", 360, 40, 1, 0, 0) == "mode=Live;pill=360x40;split=1" && SmokeMode.Status("Live", 230, 40, group: 5) == "mode=Live;pill=230x40;group=5" &&
                  !SmokeMode.TryParseStatus("mode=Live;pill=360x40;evil=1", out _, out _, out _) && SmokeMode.TryParseStatus("mode=Live;pill=360x40;split=1", out _, out _, out _));
        }
    }
}
