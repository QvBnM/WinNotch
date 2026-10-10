using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using WinNotch.Core.Context;
using WinNotch.Core.Flags;
using WinNotch.Core.Perf;
using WinNotch.Features.GameMode;

namespace WinNotch
{
    public static partial class T
    {
        /// <summary>
        /// P61: the game session. The rules (what counts as playing, alt-tab, the grace period), the accumulator and
        /// its report, the file of past sessions, and the watcher that ties them together — all without Windows: the
        /// context engine runs on the same fake clock and fake sources as the P12 tests.
        /// </summary>
        static void GameModeTests()
        {
            // ---------------------------------------------------------------- what counts as playing
            Check("GM1", "Un joc pe tot ecranul e joc; în fereastră nu (limită asumată, nu omisiune)",
                  GameDetect.Playing(Snap("cs2", AppCategory.Game, FullscreenKind.Game)) &&
                  GameDetect.Playing(Snap("cs2", AppCategory.Game, FullscreenKind.Other)) &&
                  !GameDetect.Playing(Snap("cs2", AppCategory.Game, FullscreenKind.None)));

            Check("GM2", "Un joc exclusiv pe care tabelul nu-l știe tot e joc (Windows o spune)",
                  GameDetect.Playing(Snap("joc-nou-2026", AppCategory.Other, FullscreenKind.Game)) &&
                  !GameDetect.Playing(Snap("joc-nou-2026", AppCategory.Other, FullscreenKind.None)));

            Check("GM3", "Magazinele și lansatoarele nu sunt joc, nici pe tot ecranul",
                  !GameDetect.Playing(Snap("steam", AppCategory.Game, FullscreenKind.Other)) &&
                  !GameDetect.Playing(Snap("battle.net", AppCategory.Game, FullscreenKind.Other)) &&
                  !GameDetect.Playing(Snap("leagueclient", AppCategory.Game, FullscreenKind.Other)) &&
                  GameDetect.IsLauncher("Steam") && !GameDetect.IsLauncher("cs2") && !GameDetect.IsLauncher(null));

            Check("GM4", "Un browser pe tot ecranul (un film) nu e joc",
                  !GameDetect.Playing(Snap("chrome", AppCategory.Browser, FullscreenKind.Video)) &&
                  !GameDetect.Playing(Snap("chrome", AppCategory.Browser, FullscreenKind.Other)) &&
                  !GameDetect.Playing(null) && !GameDetect.Playing(ContextSnapshot.Empty));

            // ---------------------------------------------------------------- start, alt-tab, end
            var t0 = new DateTime(2026, 10, 10, 20, 0, 0, DateTimeKind.Utc);
            var d = new GameDetect();
            var started = d.Update(Snap("cs2", AppCategory.Game, FullscreenKind.Game), t0);
            Check("GM5", "Prima apariție pornește sesiunea, cu numele jocului",
                  started.Started && !started.Ended && started.State == GameState.Running &&
                  started.Process == "cs2" && d.Process == "cs2");

            var again = d.Update(Snap("cs2", AppCategory.Game, FullscreenKind.Game), t0.AddSeconds(30));
            Check("GM6", "Cât joci, nimic nu se mai întâmplă (nicio sesiune nouă la fiecare eveniment)",
                  !again.Started && !again.Ended && again.State == GameState.Running);

            var away = d.Update(Snap("discord", AppCategory.Other, FullscreenKind.None), t0.AddMinutes(1));
            Check("GM7", "Alt-tab nu închide sesiunea: intră în răgaz",
                  away.State == GameState.Grace && !away.Ended && d.Process == "cs2");

            var back = d.Update(Snap("cs2", AppCategory.Game, FullscreenKind.Game), t0.AddMinutes(2));
            Check("GM8", "Întors în joc înainte să treacă răgazul, e aceeași sesiune",
                  back.State == GameState.Running && !back.Started && !back.Ended && d.Process == "cs2");

            d.Update(Snap("discord", AppCategory.Other, FullscreenKind.None), t0.AddMinutes(3));
            var still = d.Update(Snap("discord", AppCategory.Other, FullscreenKind.None), t0.AddMinutes(3).Add(GameDetect.Grace).AddSeconds(-1));
            Check("GM9", "Cu o secundă înainte de capătul răgazului, sesiunea trăiește",
                  still.State == GameState.Grace && !still.Ended);

            var ended = d.Update(Snap("discord", AppCategory.Other, FullscreenKind.None), t0.AddMinutes(3).Add(GameDetect.Grace));
            Check("GM10", "La capătul răgazului se închide, și spune care joc s-a încheiat",
                  ended.Ended && ended.State == GameState.None && ended.EndedProcess == "cs2" &&
                  ended.Process == "" && d.Process == "");

            var d2 = new GameDetect();
            d2.Update(Snap("cs2", AppCategory.Game, FullscreenKind.Game), t0);
            var swapped = d2.Update(Snap("eldenring", AppCategory.Game, FullscreenKind.Game), t0.AddMinutes(40));
            Check("GM11", "Alt joc în locul primului: una se închide și alta se deschide în același pas",
                  swapped.Ended && swapped.Started && swapped.EndedProcess == "cs2" &&
                  swapped.Process == "eldenring" && d2.Process == "eldenring");

            var d3 = new GameDetect();
            d3.Update(Snap("cs2", AppCategory.Game, FullscreenKind.Game), t0);
            d3.Reset();
            Check("GM12", "Reset uită sesiunea fără s-o raporteze (funcția oprită, aplicația se închide)",
                  d3.State == GameState.None && d3.Process == "" &&
                  !d3.Update(Snap("discord", AppCategory.Other, FullscreenKind.None), t0.AddHours(1)).Ended);

            // Un joc exclusiv fără nume de proces nu trebuie să rupă numele sesiunii.
            var d4 = new GameDetect();
            d4.Update(Snap("", AppCategory.Other, FullscreenKind.Game), t0);
            var named = d4.Update(Snap("rdr2", AppCategory.Game, FullscreenKind.Game), t0.AddSeconds(5));
            Check("GM13", "Un joc exclusiv fără nume ia numele când se află, fără sesiune nouă",
                  !named.Started && !named.Ended && d4.Process == "rdr2");

            // Revizia R1, Mediu 1: un ceas dat în spate (corecție NTP) lăsa răgazul să nu se mai termine niciodată,
            // deci sesiunea rămânea deschisă — cu măsurare la 1 s și prioritatea coborâtă — până la oprirea funcției.
            var d5 = new GameDetect();
            d5.Update(Snap("cs2", AppCategory.Game, FullscreenKind.Game), t0);
            d5.Update(Snap("discord", AppCategory.Other, FullscreenKind.None), t0.AddMinutes(10));
            d5.Update(Snap("discord", AppCategory.Other, FullscreenKind.None), t0.AddMinutes(5));    // ceasul sare în spate
            var afterJump = d5.Update(Snap("discord", AppCategory.Other, FullscreenKind.None), t0.AddMinutes(5).Add(GameDetect.Grace));
            Check("GM13b", "Un ceas dat în spate nu blochează sesiunea deschisă pentru totdeauna",
                  afterJump.Ended && afterJump.State == GameState.None && afterJump.EndedProcess == "cs2");

            // ---------------------------------------------------------------- the accumulator
            var session = new GameSession("CS2.exe", t0);
            Check("GM14", "Numele jocului e normalizat; o sesiune fără măsurători nu raportează nimic",
                  session.Game == "cs2" && session.Samples == 0 &&
                  !session.Report(t0.AddMinutes(10)).Measured &&
                  session.Report(t0.AddMinutes(10)).Lines().Count == 1);

            for (int i = 0; i < 10; i++)
                session.Add(new PerfSample
                {
                    TimeUtc = t0.AddSeconds(i),
                    CpuPercent = 30 + i,                    // 30..39, max 39
                    RamUsedGb = 16 + i * 0.1,
                    RamTotalGb = 32,
                    GpuPercent = 80 + i, Gpu3dPercent = 80 + i,
                    VramUsedMb = 7000 + i * 10,
                    Processes = new List<ProcUsage>
                    {
                        new ProcUsage("cs2", 25, 4000, 3900, 95, 7000, 1),
                        new ProcUsage("chrome", 10, 900, 800, 6, 300, 20),
                        new ProcUsage("winnotch", 20, 80, 70, 20, 20, 1),
                        new ProcUsage("explorer", 0.5, 200, 150, 0, 0, 1),
                    },
                }, cpuTempC: 60 + i, gpuTempC: 70 + i);

            var rep = session.Report(t0.AddHours(1).AddMinutes(24));
            Check("GM15", "Mediile și maximele din zece măsurători",
                  rep.Samples == 10 && Math.Abs(rep.AvgCpu - 34.5) < 0.01 && Math.Abs(rep.MaxCpu - 39) < 0.01 &&
                  Math.Abs(rep.AvgGpu - 84.5) < 0.01 && Math.Abs(rep.MaxGpu - 89) < 0.01 &&
                  Math.Abs(rep.PeakVramMb - 7090) < 0.01 && Math.Abs(rep.PeakRamGb - 16.9) < 0.01);

            // Revizia R1, Mediu 6: „cel mai ocupat motor” e prin definiție ≥ motorul 3D, deci un Math.Max între ele
            // alegea mereu primul — adică putea raporta un encoder video (o înregistrare în fundal) în loc de joc.
            var engines = new GameSession("cs2", t0);
            engines.Add(new PerfSample { TimeUtc = t0, CpuPercent = 10, RamUsedGb = 16, RamTotalGb = 32, GpuPercent = 95, Gpu3dPercent = 60 });
            engines.Add(new PerfSample { TimeUtc = t0.AddSeconds(1), CpuPercent = 10, RamUsedGb = 16, RamTotalGb = 32, GpuPercent = 90, Gpu3dPercent = -1 });
            var enginesReport = engines.Report(t0.AddMinutes(30));
            Check("GM15b", "Încărcarea plăcii video e cea a motorului 3D când se știe, și cel mai ocupat doar ca rezervă",
                  Math.Abs(enginesReport.AvgGpu - 75) < 0.01 && Math.Abs(enginesReport.MaxGpu - 90) < 0.01,
                  "avg=" + enginesReport.AvgGpu + " max=" + enginesReport.MaxGpu);

            Check("GM16", "Temperaturile raportate sunt maximele, nu ultima citire",
                  Math.Abs(rep.MaxCpuTempC - 69) < 0.01 && Math.Abs(rep.MaxGpuTempC - 79) < 0.01 && rep.HasTemps);

            Check("GM17", "Durata e cea cerută, nu suma măsurătorilor",
                  rep.Duration == TimeSpan.FromMinutes(84) && rep.Headline() == "cs2 · 1 h 24 min");

            Check("GM18", "Vina: nici jocul, nici WinNotch, nici zgomotul de fundal sub prag",
                  rep.Blame.Count == 1 && rep.Blame[0].Name == "chrome" &&
                  Math.Abs(rep.Blame[0].CpuPercent - 10) < 0.01 && Math.Abs(rep.Blame[0].GpuPercent - 6) < 0.01);

            // Un descărcător care a mers scurt dintr-o sesiune lungă nu ți-a luat 40% din mașină.
            // Forma datelor e cea reală: trecerea scumpă vine la fiecare a patra măsurătoare și aduce **mereu** o
            // listă nevidă (procesele care rulează acum). Prima versiune a testului punea 98 de liste goale, ceea ce
            // monitorul nu produce niciodată — și de aceea trecea și cu numitorul greșit.
            var brief = new GameSession("cs2", t0);
            for (int i = 0; i < 400; i++)
            {
                var procs = new List<ProcUsage> { new ProcUsage("explorer", 0.5, 200, 150, 0, 0, 1) };
                if (i < 8) procs.Add(new ProcUsage("qbittorrent", 40, 100, 90, 0, 0, 1));   // 2 din 100 de treceri
                brief.Add(new PerfSample
                {
                    TimeUtc = t0.AddSeconds(i), CpuPercent = 50, RamUsedGb = 16, RamTotalGb = 32,
                    Processes = i % 4 == 0 ? procs : (IReadOnlyList<ProcUsage>)Array.Empty<ProcUsage>(),
                });
            }
            var briefReport = brief.Report(t0.AddHours(1));
            Check("GM19", "Mediile din fundal sunt pe toată sesiunea, nu doar pe trecerile în care au apărut",
                  briefReport.Blame.Count == 0,
                  "blame=" + string.Join(",", briefReport.Blame.Select(b => b.Name + ":" + Math.Round(b.CpuPercent, 2))));

            // Revizia R1, Major 4: monitorul umple lista de procese o dată la patru măsurători (trecerea scumpă stă la
            // 4 s și în joc). Împărțind la *toate* măsurătorile, un program care lua constant 10% ieșea 2,5% — sub
            // pragul de 3%, deci raportul scria „nimic din fundal nu ți-a luat resurse”. Testul de dinainte trecea
            // fiindcă hrănea procese la fiecare măsurătoare, lucru pe care monitorul real nu-l face niciodată.
            var sparse = new GameSession("cs2", t0);
            for (int i = 0; i < 120; i++)
                sparse.Add(new PerfSample
                {
                    TimeUtc = t0.AddSeconds(i), CpuPercent = 50, RamUsedGb = 16, RamTotalGb = 32,
                    Processes = i % 4 == 0
                        ? new List<ProcUsage> { new ProcUsage("chrome", 10, 900, 800, 4, 300, 20) }
                        : (IReadOnlyList<ProcUsage>)Array.Empty<ProcUsage>(),
                });
            var sparseReport = sparse.Report(t0.AddHours(1));
            Check("GM19b", "Numitorul e numărul de măsurători cu procese, nu totalul: 10% constant rămâne 10%",
                  sparseReport.Samples == 120 && sparseReport.Blame.Count == 1 &&
                  Math.Abs(sparseReport.Blame[0].CpuPercent - 10) < 0.01 &&
                  Math.Abs(sparseReport.Blame[0].GpuPercent - 4) < 0.01,
                  "blame=" + string.Join(",", sparseReport.Blame.Select(b => b.Name + ":" + Math.Round(b.CpuPercent, 2))));

            // ---------------------------------------------------------------- the words
            Check("GM20", "Durata în română, scurt și fără precizie falsă",
                  GameReport.Spell(TimeSpan.FromSeconds(48)) == "48 s" &&
                  GameReport.Spell(TimeSpan.FromMinutes(7)) == "7 min" &&
                  GameReport.Spell(TimeSpan.FromMinutes(84)) == "1 h 24 min" &&
                  GameReport.Spell(TimeSpan.FromHours(2)) == "2 h" &&
                  GameReport.Spell(TimeSpan.Zero) == "0 s");

            Check("GM20b", "59,6 s se citește ca un minut, nu ca „60 s”",
                  GameReport.Spell(TimeSpan.FromSeconds(59.6)) == "1 min" &&
                  GameReport.Spell(TimeSpan.FromSeconds(59.4)) == "59 s" &&
                  GameReport.Spell(TimeSpan.FromHours(1)) == "1 h" &&
                  GameReport.Spell(TimeSpan.FromSeconds(-5)) == "0 s");

            // Revizia R1, Mediu 5: o sesiune pornită fără nume (joc exclusiv văzut înainte să se poată citi procesul)
            // rămânea cu numele „”, deci linia din fișier nu se putea compara — și, mai rău, jocul însuși intra în
            // lista „din fundal îți luau”, fiindcă excluderea se face pe nume.
            var nameless = new GameSession("", t0);
            nameless.Add(new PerfSample
            {
                TimeUtc = t0, CpuPercent = 40, RamUsedGb = 16, RamTotalGb = 32,
                Processes = new List<ProcUsage> { new ProcUsage("rdr2", 90, 4000, 3900, 95, 7000, 1) },
            });
            nameless.Rename("RDR2.exe");
            nameless.Add(new PerfSample
            {
                TimeUtc = t0.AddSeconds(1), CpuPercent = 40, RamUsedGb = 16, RamTotalGb = 32,
                Processes = new List<ProcUsage> { new ProcUsage("rdr2", 90, 4000, 3900, 95, 7000, 1) },
            });
            var namelessReport = nameless.Report(t0.AddMinutes(30));
            Check("GM20c", "O sesiune care își află numele târziu îl primește, și jocul nu se mai blamează pe sine",
                  nameless.Game == "rdr2" && namelessReport.Process == "rdr2" &&
                  namelessReport.Headline().StartsWith("rdr2", StringComparison.Ordinal) &&
                  namelessReport.Blame.All(b => b.Name != "rdr2"),
                  "blame=" + string.Join(",", namelessReport.Blame.Select(b => b.Name)));

            Check("GM20d", "Numele se pune o singură dată: un alt joc e o altă sesiune",
                  Named("cs2", "eldenring") == "cs2" && Named("", "eldenring") == "eldenring" && Named("", "") == "");

            var lines = rep.Lines();
            Check("GM21", "Rezumatul spune încărcarea, căldura, memoria și cine fura — fără FPS, că nu-l avem încă",
                  lines.Count == 4 && !rep.HasFps &&
                  lines[0].Contains("Procesor 35% mediu") && lines[0].Contains("placă video 85% mediu") &&
                  lines[1].Contains("procesor 69°C") && lines[1].Contains("placă video 79°C") &&
                  lines[2].Contains("Memorie") && lines[3].Contains("chrome"),
                  string.Join(" | ", lines));

            var quiet = new GameSession("cs2", t0);
            quiet.Add(new PerfSample { TimeUtc = t0, CpuPercent = 20, RamUsedGb = 16, RamTotalGb = 32 });
            var quietLines = quiet.Report(t0.AddMinutes(30)).Lines();
            Check("GM22", "Fără placă video și fără temperaturi, rândurile lipsă nu se umplu cu zerouri",
                  quietLines.All(l => !l.Contains("°C")) && quietLines.All(l => !l.Contains("placă video 0")) &&
                  quietLines.Last().Contains("Nimic din fundal"),
                  string.Join(" | ", quietLines));

            Check("GM23", "Rândul pentru log n-are numele jocului",
                  !rep.ToLogString().Contains("cs2") && rep.ToLogString().Contains("sesiune de joc 1 h 24 min") &&
                  rep.ToLogString().Contains("cpu 35/39%"));

            // ---------------------------------------------------------------- the file of past sessions
            string dir = Path.Combine(TestFolder, "jocuri");
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            var store = new GameReportStore(dir);
            Check("GM24", "Fără fișier încă: nicio sesiune, niciun fir aruncat",
                  store.Last().Count == 0 && store.Latest() == null && store.For("cs2").Count == 0);

            Check("GM25", "O sesiune nemăsurată nu se salvează",
                  !store.Append(new GameSession("cs2", t0).Report(t0.AddHours(1))) && !store.Append(null) &&
                  store.Last().Count == 0);

            store.Append(rep);
            store.Append(new GameSession("eldenring", t0.AddDays(1)).Let(s =>
            {
                s.Add(new PerfSample { TimeUtc = t0, CpuPercent = 40, RamUsedGb = 20, RamTotalGb = 32, GpuPercent = 95, Gpu3dPercent = 95 });
                return s;
            }).Report(t0.AddDays(1).AddMinutes(50)));

            var back2 = store.Last();
            Check("GM26", "Sesiunile se citesc înapoi, cea nouă prima, cu numerele întregi",
                  back2.Count == 2 && back2[0].Process == "eldenring" && back2[1].Process == "cs2" &&
                  back2[1].Duration == TimeSpan.FromMinutes(84) &&
                  Math.Abs(back2[1].AvgCpu - 34.5) < 0.06 && Math.Abs(back2[1].MaxGpuTempC - 79) < 0.06 &&
                  back2[1].Blame.Count == 1 && back2[1].Blame[0].Name == "chrome" &&
                  Math.Abs(back2[1].Blame[0].CpuPercent - 10) < 0.06,
                  "count=" + back2.Count);

            Check("GM27", "Comparația în timp: doar sesiunile aceluiași joc",
                  store.For("CS2.exe").Count == 1 && store.For("cs2")[0].Process == "cs2" &&
                  store.For("nu-exista").Count == 0 && store.Latest().Process == "eldenring");

            File.AppendAllText(store.Path, "{ asta nu e json" + Environment.NewLine + Environment.NewLine);
            Check("GM28", "Un rând stricat sau gol e sărit, nu aruncat (o pană de curent la jumătatea scrierii)",
                  store.Last().Count == 2 && store.Latest() != null);

            for (int i = 0; i < GameReportStore.MaxLines + 20; i++) store.Append(rep);
            Check("GM29", "Fișierul se scurtează singur: nu crește la infinit",
                  File.ReadAllLines(store.Path).Length <= GameReportStore.MaxLines &&
                  store.Last(1000).Count <= GameReportStore.MaxLines,
                  "linii=" + File.ReadAllLines(store.Path).Length);

            string big = Path.Combine(TestFolder, "jocuri-mari");
            if (Directory.Exists(big)) Directory.Delete(big, true);
            var bigStore = new GameReportStore(big);
            bigStore.Append(rep);
            File.AppendAllText(bigStore.Path, new string('x', (int)GameReportStore.MaxFile + 16) + Environment.NewLine);
            long sizeBefore = new FileInfo(bigStore.Path).Length;
            bigStore.Append(rep);
            Check("GM29b", "Un fișier peste limită nu e nici citit, nici rescris la scurtare",
                  bigStore.Last().Count == 0 && new FileInfo(bigStore.Path).Length >= sizeBefore);

            // ---------------------------------------------------------------- the watcher, through the real engine
            WatcherTests();

            // ---------------------------------------------------------------- the action and the widget
            var reportHost = new FakeReportHost { Report = rep };
            var act = GameActions.CreateLastReport(reportHost);
            Check("GM41", "Acțiunea „game.last-report”: id, titlu, iconiță, comutator, fir UI, aliasuri în ambele limbi",
                  act.Id == "game.last-report" && act.Id == GameActions.LastReportId &&
                  act.FeatureId == GameDetect.FeatureId && act.RequiresUiThread && act.Icon.Length > 0 &&
                  act.Aliases.Contains("ultimul joc") && act.Aliases.Contains("game report") &&
                  act.Aliases.Distinct().Count() == act.Aliases.Count && act.UnavailableMessage.Length > 0);

            var ran = act.ExecuteAsync(Core.Actions.ActionArgs.Empty, default).GetAwaiter().GetResult();
            Check("GM42", "Pornită, arată ultima sesiune și spune care e",
                  ran.Success && reportHost.Shown == 1 && ran.Message == "cs2 · 1 h 24 min");

            reportHost.Report = null;
            var none = act.ExecuteAsync(Core.Actions.ActionArgs.Empty, default).GetAwaiter().GetResult();
            Check("GM43", "Fără nicio sesiune, spune asta în loc să arate un raport gol",
                  !none.Success && none.Message.Contains("nicio sesiune") && reportHost.Shown == 1);

            reportHost.Report = new GameSession("cs2", t0).Report(t0.AddHours(1));       // nemăsurată
            Check("GM44", "O sesiune nemăsurată e tratată ca inexistentă",
                  !act.ExecuteAsync(Core.Actions.ActionArgs.Empty, default).GetAwaiter().GetResult().Success &&
                  reportHost.Shown == 1);

            Check("GM45", "Widget-ul „Ultimul joc” e în catalog, la Sistem, cu mărimile lui",
                  Src("Widgets/Catalog.cs").Contains("D(\"lastgame\", \"Ultimul joc\", \"Sistem\"") &&
                  Src("Widgets/Catalog.cs").Contains("new Features.GameMode.GameWidget(w, s)"));

            Check("GM46", "Liniștea e doar a noastră: prioritatea proprie și cele două aduceri din rețea, nimic din sistem",
                  Src("Features/GameMode/NotchWindow.GameMode.cs").Contains("ProcessPriorityClass.BelowNormal") &&
                  Src("NotchWindow.xaml.cs").Contains("if (!GameQuiet && DateTime.Now - _weatherAt") &&
                  !Src("Features/GameMode/NotchWindow.GameMode.cs").Contains("PowerSetActiveScheme") &&
                  !Src("Features/GameMode/GameWatcher.cs").Contains("System.Diagnostics") &&
                  !Src("Features/GameMode/GameWatcher.cs").Contains("ProcessPriorityClass"));

            // Un singur loc declară id-ul. Două constante cu aceeași valoare fac auditul (FA1) să aleagă prin reflexie
            // oricare dintre tipuri, deci trecerea testului devine o chestiune de ordine a membrilor — exact așa a
            // scăpat „game-session” la prima rulare, și exact așa trecea „perf-monitor” doar din noroc.
            Check("GM40", "Comutatorul din catalog e același id ca regulile, Experimental, oprit implicit, declarat o singură dată",
                  FeatureCatalog.GameSession == GameDetect.FeatureId &&
                  FeatureCatalog.Find(GameDetect.FeatureId) is { Stage: FeatureStage.Experimental, DefaultOn: false } &&
                  OneDeclaration(GameDetect.FeatureId) && OneDeclaration(PerfRules.FeatureId));
        }

        static void WatcherTests()
        {
            string dir = Path.Combine(TestFolder, "jocuri-watcher");
            if (Directory.Exists(dir)) Directory.Delete(dir, true);

            var rig = new Rig().Started();
            var host = new FakeGameHost();
            var store = new GameReportStore(dir);
            var monitor = new PerfMonitor(rig.Flags, () => new FakeSampler());
            var watcher = new GameWatcher(rig.Flags, monitor, rig.Engine, host, store,
                                          temps: () => (75.0, 65.0), clock: () => rig.Clock.UtcNow);
            watcher.Start();

            // Both switches are still off here: a game must not start a session with nothing to measure.
            rig.Fg.Set(Fg("cs2", covers: true, exclusive: true));
            rig.Clock.Advance(TimeSpan.FromMilliseconds(400));
            Check("GM30", "Cu „Mod de joc” sau „Performanță” oprite, un joc nu pornește nimic",
                  watcher.Playing == "" && host.QuietOn == 0 && monitor.Cadence == PerfCadence.Off);

            rig.Flags.Set(GameDetect.FeatureId, true);
            Check("GM30b", "Doar „Mod de joc” pornit, fără „Performanță”: tot nu pornește (n-ar avea ce măsura)",
                  watcher.Playing == "" && host.QuietOn == 0 && monitor.Cadence == PerfCadence.Off);

            // Both on, and the game is already in front: the session has to start from the snapshot, not wait for a change.
            rig.Flags.Set(PerfRules.FeatureId, true);
            Check("GM31", "Cu ambele pornite și jocul deja în față, sesiunea pornește din snapshot, cere liniște și urcă ritmul la 1 s",
                  watcher.Playing == "cs2" && host.QuietOn == 1 && host.QuietOff == 0 &&
                  monitor.Cadence == PerfCadence.Game,
                  "playing=" + watcher.Playing + " quiet=" + host.QuietOn + " cadence=" + monitor.Cadence);

            var sampled = Await(() => monitor.Last, s => s.RamTotalGb > 0, 4000);
            Check("GM32", "Măsurătorile ajung în sesiune (monitorul chiar rulează pentru ea)",
                  sampled.RamTotalGb > 0);

            // Alt-tab, then back: still one session.
            rig.Fg.Set(Fg("discord"));
            rig.Clock.Advance(TimeSpan.FromMilliseconds(400));
            rig.Fg.Set(Fg("cs2", covers: true, exclusive: true));
            rig.Clock.Advance(TimeSpan.FromMilliseconds(400));
            Check("GM33", "Alt-tab și întors: tot o sesiune, fără raport între timp",
                  watcher.Playing == "cs2" && host.Reports.Count == 0 && host.QuietOn == 1);

            // Out for good: the grace period has to run out, and the tick is what notices.
            rig.Clock.Advance(TimeSpan.FromMinutes(30));          // a long session, on the fake clock
            rig.Fg.Set(Fg("discord"));
            rig.Clock.Advance(TimeSpan.FromMilliseconds(400));
            rig.Clock.Advance(GameDetect.Grace);
            watcher.Tick();
            Check("GM34", "La capătul răgazului sesiunea se închide, liniștea se ridică, ritmul coboară",
                  watcher.Playing == "" && host.QuietOff == 1 && monitor.Cadence == PerfCadence.Off,
                  "quietOff=" + host.QuietOff + " cadence=" + monitor.Cadence);

            Check("GM35", "Raportul ajunge la gazdă, cu temperaturile date de aplicație, și se salvează pe disc",
                  host.Reports.Count == 1 && host.Reports[0].Process == "cs2" &&
                  host.Reports[0].Duration >= TimeSpan.FromMinutes(30) &&
                  Math.Abs(host.Reports[0].MaxCpuTempC - 75) < 0.01 && Math.Abs(host.Reports[0].MaxGpuTempC - 65) < 0.01 &&
                  store.Last().Count == 1 && store.Latest().Process == "cs2",
                  "rapoarte=" + host.Reports.Count + " fișier=" + store.Last().Count);

            // An exclusive game seen before its process name can be read: the watcher must pass the name on.
            // Counted as deltas from here on: an absolute count would break every time a case is inserted above.
            int reportsBefore = host.Reports.Count, quietOffBefore = host.QuietOff, quietOnBefore = host.QuietOn;
            rig.Fg.Set(Fg("", covers: true, exclusive: true));
            rig.Clock.Advance(TimeSpan.FromMilliseconds(400));
            bool nameless = watcher.Playing == "" && monitor.Cadence == PerfCadence.Game;   // sesiune deschisă, fără nume
            rig.Fg.Set(Fg("rdr2", covers: true, exclusive: true));
            rig.Clock.Advance(TimeSpan.FromMilliseconds(400));
            // A real sample has to land, or the session has nothing measured and will (rightly) not be reported:
            // the fake clock moves the session's duration, but only the monitor's own timer produces samples.
            Await(() => monitor.Last, x => x.RamTotalGb > 0, 4000);
            Check("GM35b", "O sesiune pornită fără nume îl primește de la detector, fără o sesiune nouă",
                  nameless && watcher.Playing == "rdr2" &&
                  host.Reports.Count == reportsBefore && host.QuietOn == quietOnBefore + 1,
                  "playing=" + watcher.Playing + " rapoarte=+" + (host.Reports.Count - reportsBefore));

            int named = EndSession(rig, watcher, host, reportsBefore);
            Check("GM35c", "Raportul ei are numele, nu „”, deci se poate compara în timp",
                  named == reportsBefore + 1 && host.Reports[reportsBefore].Process == "rdr2" &&
                  store.For("rdr2").Count == 1 && host.QuietOff == quietOffBefore + 1,
                  "rapoarte=" + named + " proces=" + (host.Reports.Count > reportsBefore ? host.Reports[reportsBefore].Process : "-"));

            // A session shorter than the minimum is not worth a report.
            int shortBefore = host.Reports.Count, shortFile = store.Last().Count, shortQuiet = host.QuietOff;
            rig.Fg.Set(Fg("rdr2", covers: true, exclusive: true));
            rig.Clock.Advance(TimeSpan.FromMilliseconds(400));
            bool shortOpen = watcher.Playing == "rdr2";
            rig.Clock.Advance(TimeSpan.FromSeconds(20));
            EndSession(rig, watcher, host, shortBefore);
            Check("GM36", "O sesiune mai scurtă decât minimul nu produce raport (dar se oprește curat)",
                  shortOpen && host.Reports.Count == shortBefore && store.Last().Count == shortFile &&
                  watcher.Playing == "" && monitor.Cadence == PerfCadence.Off && host.QuietOff == shortQuiet + 1,
                  "rapoarte=+" + (host.Reports.Count - shortBefore) + " quietOff=+" + (host.QuietOff - shortQuiet));

            // Switching the feature off in the middle of a session abandons it: no half-report.
            int offBefore = host.Reports.Count, offFile = store.Last().Count, offQuiet = host.QuietOff;
            rig.Fg.Set(Fg("cs2", covers: true, exclusive: true));
            rig.Clock.Advance(TimeSpan.FromMilliseconds(400));
            Await(() => monitor.Last, x => x.RamTotalGb > 0, 4000);      // chiar măsurată, ca abandonul să conteze
            rig.Clock.Advance(TimeSpan.FromHours(1));
            bool had = watcher.Playing == "cs2";
            rig.Flags.Set(GameDetect.FeatureId, false);
            Check("GM37", "Funcția oprită la mijlocul unei sesiuni o abandonează, fără un raport pe jumătate",
                  had && watcher.Playing == "" &&
                  host.Reports.Count == offBefore && store.Last().Count == offFile &&
                  monitor.Cadence == PerfCadence.Off && host.QuietOff == offQuiet + 1,
                  "rapoarte=+" + (host.Reports.Count - offBefore) + " quietOff=+" + (host.QuietOff - offQuiet));

            rig.Fg.Set(Fg("eldenring", covers: true, exclusive: true));
            rig.Clock.Advance(TimeSpan.FromMilliseconds(400));
            Check("GM38", "Oprită, nu mai pornește nicio sesiune",
                  watcher.Playing == "" && monitor.Cadence == PerfCadence.Off);

            watcher.Dispose();
            int events = rig.Events.Count;
            rig.Fg.Set(Fg("cs2", covers: true, exclusive: true));
            rig.Clock.Advance(TimeSpan.FromMilliseconds(400));
            Check("GM39", "După Dispose nu mai e abonat la nimic (contextul merge, el nu reacționează)",
                  rig.Events.Count > events && watcher.Playing == "" && monitor.Cadence == PerfCadence.Off);
            monitor.Dispose();
        }

        /// <summary>Exactly one public static literal field in the app declares this switch id.</summary>
        static bool OneDeclaration(string id) => typeof(FeatureCatalog).Assembly.GetTypes()
            .SelectMany(t => t.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
            .Count(f => f.IsLiteral && f.FieldType == typeof(string) && f.Name == "FeatureId" && (string)f.GetRawConstantValue() == id) == 1;

        /// <summary>
        /// Leaves the game, lets the grace period run out and gives the watcher the tick that notices — then waits a
        /// moment for the report, since ending a session is not instant. Returns how many reports exist afterwards.
        /// </summary>
        static int EndSession(Rig rig, GameWatcher watcher, FakeGameHost host, int before)
        {
            rig.Fg.Set(Fg("discord"));
            rig.Clock.Advance(TimeSpan.FromMilliseconds(400));
            rig.Clock.Advance(GameDetect.Grace + TimeSpan.FromSeconds(5));
            for (int i = 0; i < 20 && watcher.Playing.Length > 0; i++)
            {
                watcher.Tick();                       // TryEnter: a beat can be skipped, so it is offered again
                Thread.Sleep(10);
            }
            return Await(() => host.Reports.Count, n => n > before, 1500);
        }

        /// <summary>The name a session ends up with when it is created as <paramref name="first"/> and renamed to <paramref name="then"/>.</summary>
        static string Named(string first, string then)
        {
            var s = new GameSession(first, new DateTime(2026, 10, 10, 20, 0, 0, DateTimeKind.Utc));
            s.Rename(then);
            return s.Game;
        }

        static ContextSnapshot Snap(string process, AppCategory category, FullscreenKind fullscreen) =>
            ContextSnapshot.Empty with { ForegroundProcess = process ?? "", ForegroundCategory = category, Fullscreen = fullscreen };

        sealed class FakeReportHost : IGameReportHost
        {
            public GameReport Report;
            public int Shown;
            public GameReport LastReport() => Report;
            public void ShowReport(GameReport report) => Shown++;
        }

        sealed class FakeGameHost : IGameHost
        {
            public int QuietOn, QuietOff;
            public readonly List<GameReport> Reports = new List<GameReport>();
            public void Quiet(bool on) { if (on) QuietOn++; else QuietOff++; }
            public void SessionEnded(GameReport report) => Reports.Add(report);
        }
    }

    /// <summary>Lets a test build and fill an object in one expression.</summary>
    internal static class TestChain
    {
        public static TValue Let<TValue>(this TValue value, Func<TValue, TValue> with) => with(value);
    }
}
