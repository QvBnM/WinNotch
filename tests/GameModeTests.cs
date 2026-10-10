using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

            Check("GM16", "Temperaturile raportate sunt maximele, nu ultima citire",
                  Math.Abs(rep.MaxCpuTempC - 69) < 0.01 && Math.Abs(rep.MaxGpuTempC - 79) < 0.01 && rep.HasTemps);

            Check("GM17", "Durata e cea cerută, nu suma măsurătorilor",
                  rep.Duration == TimeSpan.FromMinutes(84) && rep.Headline() == "cs2 · 1 h 24 min");

            Check("GM18", "Vina: nici jocul, nici WinNotch, nici zgomotul de fundal sub prag",
                  rep.Blame.Count == 1 && rep.Blame[0].Name == "chrome" &&
                  Math.Abs(rep.Blame[0].CpuPercent - 10) < 0.01 && Math.Abs(rep.Blame[0].GpuPercent - 6) < 0.01);

            // A downloader that ran for two of a hundred samples did not take 40% of the machine.
            var brief = new GameSession("cs2", t0);
            for (int i = 0; i < 100; i++)
                brief.Add(new PerfSample
                {
                    TimeUtc = t0.AddSeconds(i), CpuPercent = 50, RamUsedGb = 16, RamTotalGb = 32,
                    Processes = i < 2
                        ? new List<ProcUsage> { new ProcUsage("qbittorrent", 40, 100, 90, 0, 0, 1) }
                        : (IReadOnlyList<ProcUsage>)Array.Empty<ProcUsage>(),
                });
            Check("GM19", "Mediile din fundal sunt pe toată sesiunea, nu doar pe măsurătorile în care au apărut",
                  brief.Report(t0.AddHours(1)).Blame.Count == 0,
                  "blame=" + string.Join(",", brief.Report(t0.AddHours(1)).Blame.Select(b => b.Name + ":" + Math.Round(b.CpuPercent))));

            // ---------------------------------------------------------------- the words
            Check("GM20", "Durata în română, scurt și fără precizie falsă",
                  GameReport.Spell(TimeSpan.FromSeconds(48)) == "48 s" &&
                  GameReport.Spell(TimeSpan.FromMinutes(7)) == "7 min" &&
                  GameReport.Spell(TimeSpan.FromMinutes(84)) == "1 h 24 min" &&
                  GameReport.Spell(TimeSpan.FromHours(2)) == "2 h" &&
                  GameReport.Spell(TimeSpan.Zero) == "0 s");

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

            // ---------------------------------------------------------------- the watcher, through the real engine
            WatcherTests();

            Check("GM40", "Comutatorul din catalog e același id ca regulile, Experimental, oprit implicit",
                  FeatureCatalog.GameSession == GameDetect.FeatureId &&
                  GameWatcher.FeatureId == GameDetect.FeatureId &&
                  FeatureCatalog.Find(GameDetect.FeatureId) is { Stage: FeatureStage.Experimental, DefaultOn: false });
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

            // A session shorter than the minimum is not worth a report.
            rig.Fg.Set(Fg("rdr2", covers: true, exclusive: true));
            rig.Clock.Advance(TimeSpan.FromMilliseconds(400));
            rig.Clock.Advance(TimeSpan.FromSeconds(20));
            rig.Fg.Set(Fg("discord"));
            rig.Clock.Advance(TimeSpan.FromMilliseconds(400));
            rig.Clock.Advance(GameDetect.Grace);
            watcher.Tick();
            Check("GM36", "O sesiune mai scurtă decât minimul nu produce raport (dar se oprește curat)",
                  host.Reports.Count == 1 && store.Last().Count == 1 &&
                  watcher.Playing == "" && monitor.Cadence == PerfCadence.Off && host.QuietOff == 2);

            // Switching the feature off in the middle of a session abandons it: no half-report.
            rig.Fg.Set(Fg("cs2", covers: true, exclusive: true));
            rig.Clock.Advance(TimeSpan.FromMilliseconds(400));
            rig.Clock.Advance(TimeSpan.FromHours(1));
            bool had = watcher.Playing == "cs2";
            rig.Flags.Set(GameDetect.FeatureId, false);
            Check("GM37", "Funcția oprită la mijlocul unei sesiuni o abandonează, fără un raport pe jumătate",
                  had && watcher.Playing == "" && host.Reports.Count == 1 && store.Last().Count == 1 &&
                  monitor.Cadence == PerfCadence.Off && host.QuietOff == 3);

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

        static ContextSnapshot Snap(string process, AppCategory category, FullscreenKind fullscreen) =>
            ContextSnapshot.Empty with { ForegroundProcess = process ?? "", ForegroundCategory = category, Fullscreen = fullscreen };

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
