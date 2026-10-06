using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using WinNotch.Core.Diagnostics;
using WinNotch.Core.Flags;
using WinNotch.Core.Update;

namespace WinNotch
{
    public static partial class T
    {
        /// <summary>
        /// P51c: why the app closed by itself, and why the next time it will say so.
        /// <para>The cause found on 0.6.18 (Event Viewer, 6 Oct 2026): an AccessViolationException inside NVIDIA's NVML,
        /// reached from <c>TempService.RefreshAsync</c> → <c>NvidiaGpu.Update</c>, moments after a monitor was unplugged
        /// and plugged back in. The handle LibreHardwareMonitor kept from <c>Computer.Open()</c> died with the driver's
        /// re-initialisation. Nothing caught it: a corrupted-state exception runs no <c>catch</c> and no
        /// <c>AppDomain.UnhandledException</c> handler, which is why log.txt held not one word about it — the last line
        /// was an ordinary one, 10 seconds earlier.</para>
        /// <para>So the tests here cover the two halves: the closure is named at the <em>next</em> start (it can never be
        /// named at the moment it happens), and the sensor library is not touched with a stale handle, nor at all once
        /// the app has gone down twice in a row.</para>
        /// </summary>
        static void ShutdownTests()
        {
            ShutdownJournalRules();
            SensorGuardRules();
            ShutdownGuardCounting();
            ShutdownSourcePins();
        }

        // ------------------------------------------------------------------ the fixed reasons

        static void ShutdownJournalRules()
        {
            var kinds = Enum.GetValues(typeof(ShutdownKind)).Cast<ShutdownKind>().ToList();
            var texts = kinds.Select(ShutdownJournal.Text).ToList();
            Check("SD1", "Fiecare motiv de închidere are un text fix, nevid, în română, și toate sunt diferite",
                  kinds.Count == 11 && texts.All(t => !string.IsNullOrWhiteSpace(t)) && texts.Distinct().Count() == kinds.Count,
                  kinds.Count + " motive: " + string.Join(" · ", texts));

            Check("SD2", "Rândul din log: „Închidere: cerere utilizator (tray)” și „Închidere anterioară: neexplicată”",
                  ShutdownJournal.Line(ShutdownKind.UserTray) == "Închidere: cerere utilizator (tray)" &&
                  ShutdownJournal.PreviousLine(ShutdownKind.Unexplained) == "Închidere anterioară: neexplicată",
                  ShutdownJournal.Line(ShutdownKind.UserTray) + " / " + ShutdownJournal.PreviousLine(ShutdownKind.Unexplained));

            Check("SD3", "Alerta cerută doar de la a doua închidere neexplicată la rând",
                  !ShutdownJournal.ShouldAlert(0) && !ShutdownJournal.ShouldAlert(1) &&
                  ShutdownJournal.ShouldAlert(2) && ShutdownJournal.ShouldAlert(3));

            Check("SD4", "Textele alertei sunt scrise pentru utilizator, nu pentru programator",
                  ShutdownJournal.AlertTitle == "WinNotch s-a închis singur" && ShutdownJournal.AlertButton == "Deschide log-ul" &&
                  ShutdownJournal.AlertBody.Length > 0);
        }

        // ------------------------------------------------------------------ the sensor library

        static void SensorGuardRules()
        {
            Check("SD5", "Citirea în proces e permisă până la 2 închideri bruște la rând, apoi nu",
                  SensorGuard.AllowInProcess(0) && SensorGuard.AllowInProcess(1) &&
                  !SensorGuard.AllowInProcess(2) && !SensorGuard.AllowInProcess(3));

            Check("SD6", "Fără schimbare de monitoare se citește; după una se redeschide biblioteca și nu se citește",
                  SensorGuard.Next(0, false) == SensorStep.Read && SensorGuard.Next(0, true) == SensorStep.Reopen &&
                  SensorGuard.Next(1, true) == SensorStep.Reopen);

            // the crash of 0.6.18: the library must not be entered at all once it has taken the app down twice
            Check("SD7", "Oprit după închideri bruște: nici schimbarea de monitoare nu mai redeschide biblioteca",
                  SensorGuard.Next(2, false) == SensorStep.Blocked && SensorGuard.Next(2, true) == SensorStep.Blocked &&
                  SensorGuard.Next(9, true) == SensorStep.Blocked);

            Check("SD8", "Motivul opririi e un text fix, de cel mult 120 de caractere, fără mesaj de excepție",
                  SensorGuard.BlockedReason.Length > 0 && SensorGuard.BlockedReason.Length <= 120 &&
                  !SensorGuard.BlockedReason.Contains("Exception"), SensorGuard.BlockedReason.Length + " caractere");
        }

        // ------------------------------------------------------------------ naming the previous closure

        /// <summary>
        /// The half that fails on 0.6.19: a run that died told the next one nothing. <see cref="StartupGuard"/> already
        /// knew (<c>Running</c> was still true in startup.json) but wrote no line and counted nothing.
        /// </summary>
        static void ShutdownGuardCounting()
        {
            var now = new DateTime(2026, 10, 6, 21, 36, 0, DateTimeKind.Utc);
            var me = AppVersion.ParseOrZero("0.6.19");
            var log = new List<string>();
            StartupGuard G(MemStore st) => new StartupGuard(st, me, () => now, log.Add);
            StartupState Saved(MemStore st) => JsonSerializer.Deserialize<StartupState>(st.Json);

            // run 1 starts and dies the way 0.6.18 died: no reason, nothing in the log
            var st1 = new MemStore();
            G(st1).Begin(false, true);
            Check("SD9", "O rulare care moare lasă startup.json cu Running=true și fără motiv",
                  Saved(st1).Running && Saved(st1).LastReason == "" && Saved(st1).UnexplainedInARow == 0);

            // run 2 is the one that must name it
            now = now.AddMinutes(60);
            log.Clear();
            var run2 = G(st1);
            run2.Begin(false, true);
            Check("SD10", "Pornirea următoare scrie „Închidere anterioară: neexplicată” și o numără (testul care pică pe 0.6.19)",
                  log.Any(l => l.StartsWith(ShutdownJournal.PreviousLine(ShutdownKind.Unexplained), StringComparison.Ordinal)) &&
                  run2.UnexplainedInARow == 1 && Saved(st1).UnexplainedInARow == 1,
                  run2.UnexplainedInARow + " · " + string.Join(" | ", log));

            // run 2 dies too: the second one in a row asks for the alert and shuts the sensors
            now = now.AddMinutes(60);
            log.Clear();
            var run3 = G(st1);
            run3.Begin(false, true);
            Check("SD11", "A doua închidere neexplicată la rând: se cere alerta și se oprește citirea senzorilor",
                  run3.UnexplainedInARow == 2 && ShutdownJournal.ShouldAlert(run3.UnexplainedInARow) &&
                  !SensorGuard.AllowInProcess(run3.UnexplainedInARow) &&
                  log.Any(l => l.Contains("a 2-a la rând", StringComparison.Ordinal)),
                  string.Join(" | ", log));

            // a clean exit ends the streak
            run3.MarkReason(ShutdownKind.UserTray);
            run3.MarkCleanExit();
            now = now.AddMinutes(1);
            log.Clear();
            var run4 = G(st1);
            run4.Begin(false, true);
            Check("SD12", "O ieșire curată șterge seria: pornirea următoare nu raportează nimic",
                  run4.UnexplainedInARow == 0 && SensorGuard.AllowInProcess(run4.UnexplainedInARow) &&
                  !log.Any(l => l.StartsWith(ShutdownJournal.PreviousPrefix, StringComparison.Ordinal)),
                  string.Join(" | ", log));

            // a run that died for a reason it wrote is named by that reason, and is not "unexplained"
            var st2 = new MemStore();
            var r1 = G(st2);
            r1.Begin(false, true);
            r1.MarkReason(ShutdownKind.StartupFailed);          // writes a reason but still counts as a crash
            now = now.AddMinutes(1);
            log.Clear();
            var r2 = G(st2);
            r2.Begin(false, true);
            Check("SD13", "O închidere cu motiv scris e numită cu motivul ei, nu „neexplicată”",
                  log.Any(l => l == ShutdownJournal.PreviousLine(ShutdownKind.StartupFailed)) &&
                  r2.UnexplainedInARow == 0 && Saved(st2).LastReason == "",
                  string.Join(" | ", log));

            // the user asks for temperatures again
            var st3 = new MemStore();
            G(st3).Begin(false, true);
            now = now.AddMinutes(1);
            var r3 = G(st3); r3.Begin(false, true);
            now = now.AddMinutes(1);
            var r4 = G(st3); r4.Begin(false, true);
            bool blocked = !SensorGuard.AllowInProcess(r4.UnexplainedInARow);
            r4.ClearUnexplained();
            Check("SD14", "„Temperaturi” bifat din nou în Setări repornește numărătoarea și citirea senzorilor",
                  blocked && r4.UnexplainedInARow == 0 && SensorGuard.AllowInProcess(r4.UnexplainedInARow) &&
                  Saved(st3).UnexplainedInARow == 0);

            // a cancelled shutdown must not leave a reason behind for the next start
            var st4 = new MemStore();
            var r5 = G(st4);
            r5.Begin(false, true);
            r5.MarkReason(ShutdownKind.Update);
            r5.MarkCleanExit();
            r5.Resume();                                        // the restart for the update failed: this run goes on
            Check("SD15", "O ieșire anulată (actualizarea a eșuat) nu lasă motivul ei pentru pornirea următoare",
                  Saved(st4).LastReason == "" && Saved(st4).Running);

            // startup.json edited by hand must not make the count nonsense
            var st5 = new MemStore { Json = JsonSerializer.Serialize(new StartupState { Version = "0.6.19", Running = true, UnexplainedInARow = -5 }) };
            var r6 = G(st5);
            r6.Begin(false, true);
            Check("SD16", "Un startup.json cu o numărătoare negativă e citit ca 0, apoi numărat normal",
                  r6.UnexplainedInARow == 1, r6.UnexplainedInARow.ToString());
        }

        // ------------------------------------------------------------------ the source, where the rules live in WPF code

        /// <summary>
        /// The parts that cannot be run without WPF or a driver, pinned to the source: every exit writes its reason, the
        /// callbacks that run outside the UI thread are guarded, and no window title reaches the log.
        /// </summary>
        static void ShutdownSourcePins()
        {
            string app = Src("App.xaml.cs"), mon = Src("Services/MonitorService.cs");

            // ---- the journal: every exit of ours names itself, on its line or in the two statements before it
            var appLines = app.Split('\n');
            var exits = new (string Name, string Anchor)[]
            {
                ("mod ajutător, fără drepturi", "Shutdown(5)"),
                ("mod ajutător, instalare/dezinstalare", "Shutdown(rc)"),
                ("a doua instanță, test de fum", "Shutdown(3)"),
                ("a doua instanță, mesaj", "MessageBox.Show(\"WinNotch rulează deja."),
                ("pornirea a eșuat", "Environment.Exit(1)"),
            };
            var missing = new List<string>();
            foreach (var e in exits)
            {
                int i = Array.FindIndex(appLines, l => l.Contains(e.Anchor, StringComparison.Ordinal));
                string near = i < 0 ? "" : string.Join(" ", appLines.Skip(Math.Max(0, i - 3)).Take(7));
                if (i < 0 || !Norm(NoComments(near)).Contains("LogShutdown(", StringComparison.Ordinal)) missing.Add(e.Name);
            }
            Check("SD17", "Fiecare ieșire din App.xaml.cs scrie motivul înainte să închidă", missing.Count == 0, string.Join(", ", missing));

            string exitApp = Norm(NoComments(MethodBody(app, "public void ExitApp(")));
            Check("SD18", "ExitApp scrie motivul ȘI apelează MarkCleanExit (test de non-regresie)",
                  exitApp != null && exitApp.Contains("LogShutdown(kind)", StringComparison.Ordinal) &&
                  exitApp.Contains("MarkCleanExit()", StringComparison.Ordinal) &&
                  exitApp.IndexOf("LogShutdown(", StringComparison.Ordinal) < exitApp.IndexOf("MarkCleanExit()", StringComparison.Ordinal),
                  exitApp ?? "ExitApp nu a fost găsit");

            Check("SD19", "Oprirea sesiunii Windows și eroarea fatală își scriu motivul",
                  Norm(NoComments(app)).Contains("LogShutdown(Core.Diagnostics.ShutdownKind.WindowsShutdown)", StringComparison.Ordinal) &&
                  Norm(NoComments(app)).Contains("LogShutdown(Core.Diagnostics.ShutdownKind.FatalError)", StringComparison.Ordinal));

            Check("SD20", "Predarea către alt proces (revenire, mod sigur, a doua instanță) își scrie motivul în coordonator",
                  Count(Norm(NoComments(Src("Core/Update/StartupCoordinator.cs"))), "Ending(guard, host,") == 3);

            Check("SD21", "Actualizarea iese cu motivul „actualizare”, nu cu cel implicit",
                  Norm(NoComments(Src("NotchWindow.Updates.cs"))).Contains("ExitApp(Core.Diagnostics.ShutdownKind.Update)", StringComparison.Ordinal));

            // ---- exceptions from any thread
            Check("SD22", "Sarcinile de fundal pe care nu le așteaptă nimeni ajung în log (azi nu ajungeau)",
                  Norm(NoComments(app)).Contains("TaskScheduler.UnobservedTaskException +=", StringComparison.Ordinal));

            string onData = Norm(NoComments(MethodBody(Src("Services/Visualizer.cs"), "private void OnData(")));
            string onNotify = Norm(NoComments(MethodBody(Src("Services/AudioService.cs"), "private void OnNotify(")));
            string expire = Norm(NoComments(MethodBody(Src("Core/Activity/ActivityManager.cs"), "private void Expire(")));
            Check("SD23", "Callback-urile de pe fire care nu sunt cel de UI (NAudio, COM, timer) sunt prinse: o excepție acolo omoară procesul",
                  onData != null && onData.Contains("try", StringComparison.Ordinal) && onData.Contains("catch", StringComparison.Ordinal) &&
                  onNotify != null && onNotify.Contains("try", StringComparison.Ordinal) && onNotify.Contains("catch", StringComparison.Ordinal) &&
                  expire != null && expire.Contains("try", StringComparison.Ordinal) && expire.Contains("catch", StringComparison.Ordinal),
                  "OnData: " + (onData?.Contains("catch") ?? false) + " OnNotify: " + (onNotify?.Contains("catch") ?? false) + " Expire: " + (expire?.Contains("catch") ?? false));

            Check("SD24", "Vizualizatorul nu împarte la zero dacă dispozitivul raportează 0 canale",
                  onData != null && onData.Contains("channels <= 0", StringComparison.Ordinal));

            // ---- the sensor library is asked before it is entered
            string temp = Norm(NoComments(Src("Services/TempService.cs")));
            Check("SD25", "TempService întreabă SensorGuard înainte de orice citire și se abonează la schimbarea monitoarelor",
                  temp.Contains("SensorGuard.Next(", StringComparison.Ordinal) &&
                  temp.Contains("SensorGuard.AllowInProcess(", StringComparison.Ordinal) &&
                  temp.Contains("SystemEvents.DisplaySettingsChanged +=", StringComparison.Ordinal) &&
                  temp.Contains("SystemEvents.DisplaySettingsChanged -=", StringComparison.Ordinal));

            // ---- nothing personal in the log (section 14 of DOCUMENTATIE.md): the leak this step closed
            Check("SD26", "Titlul ferestrei nu mai ajunge în log: MonitorService nu îl citește deloc",
                  !mon.Contains("Native.Title(", StringComparison.Ordinal), "Services/MonitorService.cs citește încă titlul");

            var monLine = LinesWith("NotchWindow.xaml.cs", "App.Log(\"Monitoare: \" + log)").FirstOrDefault() ?? "";
            string monBuild = LinesWith("NotchWindow.xaml.cs", "string log = string.Join(\" | \", mons").FirstOrDefault() ?? "";
            Check("SD27", "Rândul „Monitoare:” nu mai marchează monitorul țintă, deci mutarea mouse-ului nu mai scrie în log",
                  monLine.Length > 0 && monBuild.Length > 0 && !monBuild.Contains("t.Handle ? \"*\"", StringComparison.Ordinal),
                  Norm(monBuild));

            // ---- the switch
            var info = FeatureCatalog.Find(ShutdownJournal.FeatureId);
            Check("SD28", "Comutatorul „shutdown-report” e în catalog, Beta și pornit implicit (reparație)",
                  info != null && info.Id == FeatureCatalog.ShutdownReport && info.Stage == FeatureStage.Beta && info.DefaultOn &&
                  info.Name.Length > 0 && info.Description.EndsWith(".", StringComparison.Ordinal),
                  info == null ? "lipsește" : info.Stage + " " + info.DefaultOn);

            string report = Norm(NoComments(Src("Features/Diagnostics/NotchWindow.ShutdownReport.cs")));
            Check("SD29", "Alerta se arată o singură dată, doar în standby, doar cu comutatorul pornit",
                  report.Contains("_shutdownReportShown", StringComparison.Ordinal) &&
                  report.Contains("_mode != Mode.Idle", StringComparison.Ordinal) &&
                  report.Contains("if (!ShutdownReportEnabled()) return false;", StringComparison.Ordinal));

            Check("SD30", "Cârligul din UpdateTick e un singur rând",
                  LinesWith("NotchWindow.Updates.cs", "ShutdownReportTick()").Count == 1);
        }
    }
}
