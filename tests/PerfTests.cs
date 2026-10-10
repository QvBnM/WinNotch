using System;
using System.Collections.Generic;
using System.Linq;
using WinNotch.Core.Flags;
using WinNotch.Core.Perf;
using WinNotch.Features.WindowV2;

namespace WinNotch
{
    public static partial class T
    {
        /// <summary>
        /// P60: the performance section's maths and rules. Everything the user will be shown as a number is decided
        /// here, away from WPF and away from the machine, so it can be checked: the cadences (and the standby rule),
        /// the frame-time percentiles, the leak trend, how processes are added up and blamed, and the GPU counters'
        /// instance names.
        /// </summary>
        static void PerfTests()
        {
            // ---------------------------------------------------------------- cadences and the standby rule
            Check("PF1", "Oprită, sau nimic de arătat și niciun joc: niciun cronometru",
                  PerfRules.Pick(false, true, true) == PerfCadence.Off &&
                  PerfRules.Pick(true, false, false) == PerfCadence.Off &&
                  PerfRules.SecondsFor(PerfCadence.Off) == 0);

            Check("PF2", "Cât se uită cineva: 2 s, nu mai des",
                  PerfRules.Pick(true, true, false) == PerfCadence.Visible &&
                  PerfRules.SecondsFor(PerfCadence.Visible) == 2);

            Check("PF3", "Jocul e singurul motiv pentru un ritm sub 2 s, și bate fereastra deschisă",
                  PerfRules.Pick(true, false, true) == PerfCadence.Game &&
                  PerfRules.Pick(true, true, true) == PerfCadence.Game &&
                  PerfRules.SecondsFor(PerfCadence.Game) == 1);

            Check("PF4", "Regula din CLAUDE.md: nicio cadență sub 2 s nu e permisă fără un joc pornit",
                  PerfRules.AllowedInStandby(PerfCadence.Off, false) &&
                  PerfRules.AllowedInStandby(PerfCadence.Visible, false) &&
                  !PerfRules.AllowedInStandby(PerfCadence.Game, false) &&
                  PerfRules.AllowedInStandby(PerfCadence.Game, true));

            Check("PF5", "Trecerea scumpă (toate procesele) rămâne la 4 s și în joc",
                  PerfRules.ProcessEvery(PerfCadence.Game) == 4 &&
                  PerfRules.ProcessEvery(PerfCadence.Visible) == 2 &&
                  PerfRules.ProcessEvery(PerfCadence.Off) == 0 &&
                  PerfRules.ProcessEvery(PerfCadence.Game) * PerfRules.SecondsFor(PerfCadence.Game) >= 4);

            Check("PF6", "Comutatorul din catalog e același id ca regulile",
                  FeatureCatalog.PerfMonitor == PerfRules.FeatureId &&
                  FeatureCatalog.Find(PerfRules.FeatureId) != null &&
                  FeatureCatalog.Find(PerfRules.FeatureId).DefaultOn == false);

            // ---------------------------------------------------------------- the ring
            var ring = new SampleRing<int>(3);
            Check("PF7", "Inelul ține doar ultimele N, în ordine, fără să crească",
                  ring.Count == 0 && ring.Capacity == 3 &&
                  Fill(ring, 1, 2) && ring.ToList().SequenceEqual(new[] { 1, 2 }) &&
                  Fill(ring, 3, 4, 5) && ring.Count == 3 &&
                  ring.ToList().SequenceEqual(new[] { 3, 4, 5 }) && ring[0] == 3 && ring.Last == 5 &&
                  ring.Tail(2).SequenceEqual(new[] { 4, 5 }) && ring.Tail(9).Count == 3);

            var cleared = new SampleRing<int>(2);
            cleared.Add(7); cleared.Clear();
            Check("PF8", "Golit, inelul uită tot și nu mai răspunde la indici",
                  cleared.Count == 0 && cleared.Last == 0 && Throws(() => { var _ = cleared[0]; }));

            // ---------------------------------------------------------------- frame times
            Check("PF9", "Fără cadre, zero peste tot (și niciun număr inventat)",
                  FrameStats.From(null).Frames == 0 && FrameStats.From(new double[0]).AvgFps == 0 &&
                  !FrameStats.Empty.HasP1 && FrameStats.Empty.P1Low == 0);

            var steady = FrameStats.From(Enumerable.Repeat(10.0, 1000).ToList());
            Check("PF10", "1000 de cadre de 10 ms = 100 FPS fix, și 1% low tot 100",
                  Math.Abs(steady.AvgFps - 100) < 0.01 && steady.Frames == 1000 &&
                  Math.Abs(steady.Seconds - 10) < 0.01 &&
                  steady.HasP1 && Math.Abs(steady.P1Low - 100) < 0.01 && !steady.HasP01);

            // 990 frames at 10 ms + 10 frames at 50 ms: the slowest 1% is exactly those ten.
            var spiky = FrameStats.From(Enumerable.Repeat(10.0, 990).Concat(Enumerable.Repeat(50.0, 10)).ToList());
            Check("PF11", "1% low ia exact cele mai lente 1% din cadre (10 cadre de 50 ms = 20 FPS)",
                  spiky.Frames == 1000 && spiky.FramesIn(1) == 10 &&
                  Math.Abs(spiky.P1Low - 20) < 0.01 &&
                  spiky.AvgFps > 60 && spiky.MaxFrameMs == 50);

            Check("PF12", "Media nu e media FPS-urilor pe cadru, ci cadre împărțite la timp",
                  Math.Abs(spiky.AvgFps - 1000 * 1000.0 / (990 * 10 + 10 * 50)) < 0.001);

            Check("PF13", "Prea puține cadre pentru un 1% low: spune că nu știe, nu dă zero ca număr",
                  FrameStats.From(Enumerable.Repeat(10.0, 100).ToList()).HasP1 == false);

            var withPause = FrameStats.From(new List<double> { 10, 10, 5000, 10, -3, double.NaN, 10 });
            Check("PF14", "O pauză (încărcare, alt-tab) e numărată separat, nu ca stutter; valorile imposibile sunt sărite",
                  withPause.Frames == 4 && withPause.Pauses == 1 && Math.Abs(withPause.AvgFps - 100) < 0.01);

            var stutters = FrameStats.From(Enumerable.Repeat(10.0, 100).Concat(new[] { 40.0, 45.0 }).ToList());
            Check("PF15", "Stutter = peste dublul medianei și cel puțin 8 ms peste ea",
                  stutters.Stutters == 2 && stutters.StuttersPerMinute > 0 &&
                  FrameStats.From(Enumerable.Repeat(2.0, 100).Concat(new[] { 4.5 }).ToList()).Stutters == 0);

            Check("PF16", "Percentila de frametime e separată de „1% low” și le spune pe nume",
                  Math.Abs(spiky.FrameMsPercentile(50) - 10) < 0.01 &&
                  Math.Abs(spiky.FrameMsPercentile(100) - 50) < 0.01);

            // ---------------------------------------------------------------- the leak trend
            Check("PF17", "Fără destule puncte sau destul timp, nicio concluzie",
                  MemoryTrend.Of(null).Verdict == MemoryVerdict.Unknown &&
                  MemoryTrend.Of(Line(0, 100, 5, 60)).Verdict == MemoryVerdict.Unknown &&
                  MemoryTrend.Of(Line(0, 100, 30, 10)).Verdict == MemoryVerdict.Unknown);

            var leak = MemoryTrend.Of(Line(0, 300, 61, 60, 4));          // 4 MB/min = 240 MB/h, an hour of it
            Check("PF18", "O creștere dreaptă și mare, pe o oră, e raportată ca scurgere",
                  leak.Verdict == MemoryVerdict.Growing &&
                  Math.Abs(leak.MbPerHour - 240) < 1 && leak.Fit > 0.99 &&
                  Math.Abs(leak.GrowthMb - 240) < 1);

            var flat = MemoryTrend.Of(Line(0, 500, 61, 60, 0));
            Check("PF19", "Memorie care stă pe loc: „stabil”, nu scurgere",
                  flat.Verdict == MemoryVerdict.Steady && Math.Abs(flat.MbPerHour) < 0.001);

            var slow = MemoryTrend.Of(Line(0, 500, 61, 60, 0.5));        // 30 MB/h: real, but under the threshold
            Check("PF20", "Creștere mică (sub 50 MB/h) nu deranjează utilizatorul",
                  slow.Verdict == MemoryVerdict.Steady);

            // a browser being used: up and down, ending higher — fast but not a line
            var noisy = new List<(double, double)>();
            for (int i = 0; i <= 60; i++) noisy.Add((i, 500 + (i % 2 == 0 ? 400 : 0) + i));
            Check("PF21", "Creștere în zig-zag (o aplicație folosită normal) nu e scurgere: linia nu se potrivește",
                  MemoryTrend.Of(noisy).Fit < MemoryTrend.MinFit &&
                  MemoryTrend.Of(noisy).Verdict == MemoryVerdict.Steady);

            var shrinking = MemoryTrend.Of(Line(0, 2000, 61, 60, -5));
            Check("PF22", "Memorie care scade are panta negativă și nicio estimare de creștere",
                  shrinking.MbPerHour < -200 && shrinking.Verdict == MemoryVerdict.Steady &&
                  MemoryTrend.MinutesToGrow(shrinking, 100) < 0 &&
                  Math.Abs(MemoryTrend.MinutesToGrow(leak, 240) - 60) < 1);

            // ---------------------------------------------------------------- processes
            Check("PF23", "Procentul de procesor e din toată mașina, nu dintr-un nucleu",
                  Math.Abs(ProcessRollup.CpuPercent(1000, 1000, 1) - 100) < 0.01 &&
                  Math.Abs(ProcessRollup.CpuPercent(1000, 1000, 8) - 12.5) < 0.01 &&
                  ProcessRollup.CpuPercent(1000, 0, 8) == 0 && ProcessRollup.CpuPercent(-5, 1000, 8) == 0);

            Check("PF24", "Numele proceselor: litere mici, fără „.exe”, fără cale",
                  RawProc.Normalize("C:\\Games\\CS2.exe") == "cs2" && RawProc.Normalize("Chrome") == "chrome" &&
                  RawProc.Normalize(null) == "" && RawProc.Normalize("  Discord.EXE ") == "discord");

            var before = new Dictionary<int, RawProc>
            {
                [10] = new RawProc(10, "chrome", 1000, 500, 400),
                [11] = new RawProc(11, "chrome", 1000, 300, 250),
                [12] = new RawProc(12, "cs2", 5000, 4000, 3800),
            };
            var now = new List<RawProc>
            {
                new RawProc(10, "chrome", 1400, 520, 420),               // +400 ms
                new RawProc(11, "chrome", 1200, 310, 260),               // +200 ms
                new RawProc(12, "cs2", 9000, 4100, 3900),                // +4000 ms
                new RawProc(13, "notepad", 9999, 20, 15),                // new: memory counts, CPU must not
            };
            var merged = ProcessRollup.Merge(before, now, 1000, 8, new Dictionary<int, double> { [12] = 60 }, new Dictionary<int, double> { [12] = 7000 });
            var chrome = merged.First(u => u.Name == "chrome");
            var cs2 = merged.First(u => u.Name == "cs2");
            var notepad = merged.First(u => u.Name == "notepad");
            Check("PF25", "Procesele cu același nume se adună într-un rând, cu câte au fost",
                  merged.Count == 3 && chrome.Processes == 2 &&
                  Math.Abs(chrome.CpuPercent - 7.5) < 0.01 && Math.Abs(chrome.PrivateMb - 680) < 0.01);

            Check("PF26", "Un proces nou nu intră cu tot CPU-ul lui de la pornire (ar arăta 100%)",
                  notepad.CpuPercent == 0 && Math.Abs(notepad.PrivateMb - 15) < 0.01);

            Check("PF27", "GPU-ul și memoria video merg pe procesul lor",
                  Math.Abs(cs2.GpuPercent - 60) < 0.01 && Math.Abs(cs2.VramMb - 7000) < 0.01 && chrome.GpuPercent == 0);

            Check("PF28", "Topul se sortează după ce ceri, cel mai mare primul",
                  ProcessRollup.Top(merged, 2, PerfMetric.Cpu)[0].Name == "cs2" &&
                  ProcessRollup.Top(merged, 2, PerfMetric.Gpu)[0].Name == "cs2" &&
                  ProcessRollup.Top(merged, 1, PerfMetric.Memory)[0].Name == "cs2" &&
                  ProcessRollup.Top(merged, 9, PerfMetric.Gpu).Count == 1 &&
                  ProcessRollup.Top(null, 3, PerfMetric.Cpu).Count == 0);

            var blame = ProcessRollup.Blame(new List<ProcUsage>
            {
                new ProcUsage("cs2", 40, 4000, 3900, 95, 7000, 1),
                new ProcUsage("chrome", 12, 900, 800, 8, 300, 20),
                new ProcUsage("winnotch", 9, 80, 70, 1, 20, 1),
                new ProcUsage("explorer", 0.4, 200, 150, 0, 0, 1),
            }, "CS2.exe");
            Check("PF29", "Vina pentru un joc: nici jocul, nici WinNotch, nici zgomotul de fundal",
                  blame.Count == 1 && blame[0].Name == "chrome");

            // ---------------------------------------------------------------- GPU counter instance names
            var engine = GpuInstance.Parse("pid_12345_luid_0x00000000_0x0000D5BE_phys_0_eng_0_engtype_3D");
            Check("PF30", "Din numele contorului ies id-ul procesului și motorul",
                  engine.IsValid && engine.Pid == 12345 && engine.EngineType == "3D" && engine.Is3D);

            var mem = GpuInstance.Parse("pid_777_luid_0x00000000_0x0000D5BE_phys_0");
            Check("PF31", "Linia de memorie video n-are motor, dar are proces",
                  mem.IsValid && mem.Pid == 777 && mem.EngineType == "" && !mem.Is3D);

            Check("PF32", "„_Total” și orice altceva nu se adună la procese",
                  !GpuInstance.Parse("_Total").IsValid && !GpuInstance.Parse("").IsValid &&
                  !GpuInstance.Parse(null).IsValid && !GpuInstance.Parse("pid_").IsValid &&
                  !GpuInstance.Parse("pid_abc_eng_0").IsValid && !GpuInstance.Parse("pid_12x_phys_0").IsValid &&
                  !GpuInstance.Parse("pid_12345678901_phys_0").IsValid);

            var decode = GpuInstance.Parse("pid_9_luid_0x0_0x1_phys_0_eng_1_engtype_VideoDecode");
            Check("PF33", "Un motor de decodare video e recunoscut, dar nu trece ca 3D",
                  decode.IsValid && decode.Pid == 9 && decode.EngineType == "VideoDecode" && !decode.Is3D);

            // ---------------------------------------------------------------- the sample itself
            var sample = new PerfSample
            {
                TimeUtc = new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc),
                CpuPercent = 42.4, RamUsedGb = 16, RamTotalGb = 32, CommitUsedGb = 20, CommitLimitGb = 40,
                GpuPercent = 88, Gpu3dPercent = 85, VramUsedMb = 7000,
                Top = new List<ProcUsage> { new ProcUsage("cs2", 40, 4000, 3900, 95, 7000, 1) },
            };
            Check("PF34", "Procentele derivate din eșantion sunt corecte",
                  Math.Abs(sample.RamPercent - 50) < 0.01 && Math.Abs(sample.CommitPercent - 50) < 0.01 &&
                  sample.HasGpu && sample.HasProcesses &&
                  !PerfSample.Empty.HasGpu && PerfSample.Empty.GpuPercent == -1 && PerfSample.Empty.RamPercent == 0);

            Check("PF35", "Rândul pentru log n-are nume de procese, doar numere rotunde",
                  sample.ToLogString().Contains("cpu 42%") && sample.ToLogString().Contains("gpu 88%") &&
                  !sample.ToLogString().Contains("cs2") &&
                  PerfSample.Empty.ToLogString().Contains("gpu -"));

            // ---------------------------------------------------------------- the window's fifth tab
            Check("PF36", "Fila „Performanță” există doar cu comutatorul pornit; cele patru rămân neatinse",
                  LayoutRules.Tabs.Count == 4 &&
                  LayoutRules.TabsFor(false).Count == 4 &&
                  LayoutRules.TabsFor(true).Count == 5 &&
                  LayoutRules.TabsFor(true)[4].Id == LayoutRules.Performance &&
                  LayoutRules.TabsFor(true)[4].Title == "Performanță");

            Check("PF37", "Fila se găsește după id și se deschide din cerere, fără coloană sau inspector",
                  LayoutRules.FindTab(LayoutRules.Performance) != null &&
                  LayoutRules.TabFor("performance") == LayoutRules.Performance &&
                  LayoutRules.TabFor(LayoutRules.Performance) == LayoutRules.Performance &&
                  !LayoutRules.HasLeftPanel(LayoutRules.Performance) &&
                  !LayoutRules.HasInspector(LayoutRules.Performance));
        }

        /// <summary>Points on a straight line: (0, mb0) … every minute, mbPerMinute apart.</summary>
        static List<(double Minutes, double Mb)> Line(double from, double mb0, int points, double overMinutes, double mbPerMinute = 1)
        {
            var list = new List<(double, double)>();
            double step = points > 1 ? overMinutes / (points - 1) : 0;
            for (int i = 0; i < points; i++) list.Add((from + i * step, mb0 + i * step * mbPerMinute));
            return list;
        }

        static bool Fill<TItem>(SampleRing<TItem> ring, params TItem[] items)
        {
            foreach (var i in items) ring.Add(i);
            return true;
        }

        static bool Throws(Action a)
        {
            try { a(); return false; } catch { return true; }
        }
    }
}
