using System;
using System.Linq;
using WinNotch.Core.Flags;
using WinNotch.Core.Ui;
using WinNotch.Features.Activity;
using WinNotch.Features.Shelf;

namespace WinNotch
{
    public static partial class T
    {
        /// <summary>
        /// P51b: the eye-break alert was on the pill, the user started dragging files for the shelf and the notch stayed
        /// on the alert. An alert is information, not a state: any clear intention wins. The rules of
        /// <see cref="InterruptRules"/> and the anti-loop memory.
        /// </summary>
        static void InterruptTests()
        {
            InterruptRuleTable();
            InterruptAntiLoop();
            InterruptSourcePins();
        }

        static void InterruptRuleTable()
        {
            bool Inter(UserIntent i, bool interactive) => InterruptRules.Interrupts(i, interactive);

            Check("IR1", "Tragerea de fișiere întrerupe orice alertă, inclusiv una cu butoane (cazul raportat: pauza pentru ochi)",
                  Inter(UserIntent.FileDrag, true) && Inter(UserIntent.FileDrag, false));

            Check("IR2", "Scurtătura (și „Deschide notch-ul” din tray, aceeași cale) și Command Bar-ul întrerup orice alertă",
                  Inter(UserIntent.Shortcut, true) && Inter(UserIntent.CommandBar, true) &&
                  Inter(UserIntent.Shortcut, false) && Inter(UserIntent.CommandBar, false));

            Check("IR3", "Deschiderea unui panou întrerupe alerta",
                  Inter(UserIntent.OpenPanel, true) && Inter(UserIntent.OpenPanel, false));

            Check("IR4", "Hover-ul întrerupe o alertă de informare, dar nu una la care trebuie să apeși ceva",
                  Inter(UserIntent.Hover, false) && !Inter(UserIntent.Hover, true));

            Check("IR5", "Nu întrerup: mișcarea obișnuită a mouse-ului, tastatul în altă aplicație, o altă alertă",
                  !Inter(UserIntent.MouseMove, false) && !Inter(UserIntent.MouseMove, true) &&
                  !Inter(UserIntent.Typing, false) && !Inter(UserIntent.OtherAlert, false));

            // the alert table decides what "with buttons" means, so the rule and the alerts stay in step
            var eye = LegacyAlerts.Find(LegacyAlerts.EyeBreak);
            var track = LegacyAlerts.Find(LegacyAlerts.Track);
            Check("IR6", "Tabelul alertelor spune care are butoane: pauza pentru ochi da, piesa nouă nu",
                  eye != null && eye.Interactive && track != null && !track.Interactive &&
                  !Inter(UserIntent.Hover, eye.Interactive) && Inter(UserIntent.Hover, track.Interactive) &&
                  Inter(UserIntent.FileDrag, eye.Interactive));

            Check("IR7", "Comutatorul din catalog are id-ul regulilor, e Beta și pornit implicit",
                  InterruptRules.FeatureId == FeatureCatalog.AlertInterrupt &&
                  FeatureCatalog.Find(InterruptRules.FeatureId) is FeatureInfo fi && fi.Stage == FeatureStage.Beta && fi.DefaultOn);
        }

        static void InterruptAntiLoop()
        {
            var now = new DateTime(2026, 1, 1, 21, 0, 0);
            var m = new InterruptMemory();
            Check("IR8", "O alertă care n-a fost întreruptă nu e oprită",
                  !m.Suppressed(LegacyAlerts.EyeBreak, now) && !m.Suppressed(null, now) && !m.Suppressed("", now));

            m.Note(LegacyAlerts.EyeBreak, now);
            Check("IR9", "O alertă întreruptă nu se re-afișează imediat, dar răgazul e scurt (2 s): o alertă cerută de utilizator nu se pierde",
                  m.Suppressed(LegacyAlerts.EyeBreak, now) && m.Suppressed(LegacyAlerts.EyeBreak, now.AddSeconds(1)) &&
                  InterruptRules.Cooldown == TimeSpan.FromSeconds(2) && InterruptRules.Quiet == TimeSpan.FromMilliseconds(1500));

            Check("IR10", "După răgaz se poate afișa din nou și memoria se curăță; un ceas dat înapoi doar uită mai repede",
                  !m.Suppressed(LegacyAlerts.EyeBreak, now.AddSeconds(2)) && m.Count == 0 &&
                  !m.Suppressed(LegacyAlerts.EyeBreak, now.AddSeconds(3)));

            var mk = new InterruptMemory();
            mk.Note(LegacyAlerts.OcrKey, now);
            Check("IR10b", "Memoria lucrează pe cheia fluxului: pașii aceluiași flux împart răgazul, alt flux nu e atins",
                  mk.Suppressed(LegacyAlerts.OcrKey, now) && !mk.Suppressed(LegacyAlerts.Volume, now) &&
                  LegacyAlerts.Find(LegacyAlerts.OcrDone).Key == LegacyAlerts.OcrKey);

            var mb = new InterruptMemory();
            mb.Note(LegacyAlerts.EyeBreak, now);
            Check("IR10c", "Ceasul dat înapoi (ora de iarnă) nu blochează alerta o oră",
                  !mb.Suppressed(LegacyAlerts.EyeBreak, now.AddHours(-1)));

            m.Note(LegacyAlerts.EyeBreak, now);
            Check("IR11", "O altă alertă nu e afectată", !m.Suppressed(LegacyAlerts.Track, now));

            var m2 = new InterruptMemory();
            for (int i = 0; i < InterruptMemory.MaxKinds + 5; i++) m2.Note("alerta-" + i, now.AddSeconds(i));
            m2.Note(null, now); m2.Note("", now);
            Check("IR12", "Memoria nu crește peste limită și ignoră id-urile goale", m2.Count == InterruptMemory.MaxKinds);

            m2.Clear();
            Check("IR13", "Clear uită tot", m2.Count == 0);

            // the same pure detector the shelf uses: a button already down outside the pill is a drag carried in
            var drag = new ShelfDragHover();
            bool carried = !drag.Update(false, true) && drag.Update(true, true);
            bool pressedOnPill = !new ShelfDragHover().Update(true, true);
            Check("IR14", "Tragerea e recunoscută cu detectorul raftului (buton apăsat în afară, apoi adus pe pastilă); o apăsare pe pastilă nu e tragere",
                  carried && pressedOnPill && !drag.Update(true, false));
        }

        /// <summary>The hooks in the old files: few lines, and nothing personal in the log.</summary>
        static void InterruptSourcePins()
        {
            string notch = Src("NotchWindow.xaml.cs"), act = Src("Features/Activity/NotchWindow.Activity.cs"),
                   cmd = Src("Features/CommandBar/NotchWindow.CommandBar.cs"),
                   part = Src("Features/AlertInterrupt/NotchWindow.AlertInterrupt.cs");

            Check("IR15", "Legăturile sunt câte un rând: PollTick, scurtătura, Command Bar (doar când se deschide), panourile, poarta din Alert; EndLive rămâne neatins (pinul AC4)",
                  notch.Contains("AlertInterruptPoll(Inside(r, p, _miniApplied && _mode == Mode.Idle ? 10 : 4));") &&
                  notch.Contains("AlertInterrupt(Core.Ui.UserIntent.Shortcut);") && !notch.Contains("AlertInterruptEnded") &&
                  notch.Contains("StartAlertInterrupt();") && notch.Contains("StopAlertInterrupt();") &&
                  act.Contains("if (!AlertInterruptAllows(id)) return false;") &&
                  Norm(NoComments(Src("Features/CommandBar/NotchWindow.CommandBar.cs")))
                      .Contains("case ShortcutDecision.Open: AlertInterrupt(Core.Ui.UserIntent.CommandBar); OpenCommandBar(); break;") &&
                  Src("Features/AudioSwitch/NotchWindow.AudioSwitch.cs").Contains("AlertInterrupt(Core.Ui.UserIntent.OpenPanel);") &&
                  Src("Features/Shelf/NotchWindow.Shelf.cs").Contains("AlertInterrupt(Core.Ui.UserIntent.OpenPanel);"));

            Check("IR19", "Cazul raportat, în cod: o alertă cu butoane nu mai blochează tragerea, iar tragerea e singurul detector (nu se mai setează click-through)",
                  notch.Contains("if (_liveInteractive && AlertInterruptHoverBlocked() && !AlertInterruptDragging) return;") &&
                  notch.Contains("bool shelfDrag = ShelfDragHover(ins) || AlertInterruptDragging;") &&
                  notch.Contains("!atEdge && !AlertInterruptDragging"));

            Check("IR20", "Protocolul comutatorului: copie pe firul UI, Changed += / -=, Dispatcher, erorile prin ReportError; alerta întreruptă e uitată de Activity Manager (nu se re-desenează)",
                  part.Contains("FeatureFlags.Current.Changed += _aiFlagHandler;") &&
                  part.Contains("FeatureFlags.Current.Changed -= _aiFlagHandler;") &&
                  part.Contains("Dispatcher.InvokeAsync(") && part.Contains("ReportError(InterruptRules.FeatureId, ex)") &&
                  part.Contains("ShelfReadPrimaryButton();") &&
                  act.Contains("private void ActivityDismissShown()") && part.Contains("ActivityDismissShown();"));

            Check("IR16", "Alerta se încheie prin EndLive (rutina existentă), nu prin ascunderea straturilor, și doar cât e o alertă pe pastilă",
                  part.Contains("EndLive();") && !part.Contains("LiveLayer") && !part.Contains("Fade(") &&
                  part.Contains("if (!_aiOn || _mode != Mode.Live) return false;") &&
                  part.Contains("if (_mode == Mode.Live) { if (_aiPendingId != null) { _aiCurrentId = _aiPendingId; _aiPendingId = null; } }"));

            Check("IR17", "Nu se face un detector de tragere nou: se folosește ShelfDragHover (P23)",
                  part.Contains("new ShelfDragHover()") && !part.Contains("DispatcherTimer") &&
                  !part.Contains("bool _downOutside"));

            var logs = part.Split('\n').Where(l => l.Contains("App.Log(", StringComparison.Ordinal)).ToList();
            Check("IR18", "În log doar id-ul alertei și motivul, din cuvinte fixe: niciun titlu, nicio cale, niciun mesaj de excepție",
                  logs.Count == 1 && logs[0].Contains("Alertă întreruptă: ") && !logs.Any(l => l.Contains("Title") || l.Contains("ex.Message")));
        }
    }
}
