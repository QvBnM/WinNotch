using System;
using System.Linq;
using WinNotch.Core.Context;
using WinNotch.Core.Flags;
using WinNotch.Features.StatusDot;

namespace WinNotch
{
    public static partial class T
    {
        /// <summary>
        /// P73: the dot beside the pill. What it shows, in what order, with what ring — and that a privacy state is
        /// never hidden behind music. Everything is read from the context snapshot: no second source.
        /// </summary>
        static void StatusDotTests()
        {
            StatusDotPriority();
            StatusDotRing();
            StatusDotLook();
            StatusDotSourcePins();
        }

        static ContextSnapshot Snap(bool mic = false, bool cam = false, bool media = false,
                                    bool battery = false, bool charging = false, int percent = -1) =>
            ContextSnapshot.Empty with
            {
                MicrophoneInUse = mic, CameraInUse = cam, MediaPlaying = media,
                HasBattery = battery, OnBattery = battery && !charging, BatteryPercent = percent,
            };

        static void StatusDotPriority()
        {
            Check("SD1", "Fără nimic pornit, nicio bulină",
                  StatusDotRules.Pick(ContextSnapshot.Empty, false) == DotKind.None &&
                  StatusDotRules.Pick(null, false) == DotKind.None &&
                  StatusDotRules.All(ContextSnapshot.Empty, false).Count == 0);

            Check("SD2", "Microfonul bate tot: o stare de confidențialitate nu stă ascunsă sub o piesă care cântă",
                  StatusDotRules.Pick(Snap(mic: true, media: true, battery: true, charging: true), true) == DotKind.Listening &&
                  StatusDotRules.Pick(Snap(cam: true, media: true), false) == DotKind.Listening);

            Check("SD3", "Ordinea restului: ce ai pornit tu, apoi ce doar cântă, apoi încărcătorul",
                  StatusDotRules.Pick(Snap(media: true, battery: true, charging: true), true) == DotKind.Working &&
                  StatusDotRules.Pick(Snap(media: true, battery: true, charging: true), false) == DotKind.Playing &&
                  StatusDotRules.Pick(Snap(battery: true, charging: true), false) == DotKind.Charging);

            Check("SD4", "Pe baterie nu e nicio bulină de alimentare (doar încărcarea e o stare, descărcarea nu)",
                  StatusDotRules.Pick(Snap(battery: true), false) == DotKind.None &&
                  StatusDotRules.Pick(Snap(battery: false, charging: true), false) == DotKind.None);

            var all = StatusDotRules.All(Snap(mic: true, media: true, battery: true, charging: true), true);
            Check("SD5", "La hover apar toate, în aceeași ordine, cu textele lor în română",
                  all.Count == 4 && all[0] == DotKind.Listening && all[1] == DotKind.Working &&
                  all[2] == DotKind.Playing && all[3] == DotKind.Charging &&
                  StatusDotRules.HoverText(Snap(mic: true, media: true), false) == "te ascultă · cântă" &&
                  StatusDotRules.HoverText(ContextSnapshot.Empty, false) == "");
        }

        static void StatusDotRing()
        {
            Check("SD6", "Inelul e progresul piesei, nivelul bateriei sau progresul treburii",
                  StatusDotRules.Ring(DotKind.Playing, ContextSnapshot.Empty, 0.34, -1) == 0.34 &&
                  StatusDotRules.Ring(DotKind.Working, ContextSnapshot.Empty, -1, 0.5) == 0.5 &&
                  StatusDotRules.Ring(DotKind.Charging, Snap(battery: true, charging: true, percent: 84), -1, -1) == 0.84);

            Check("SD7", "Ascultatul n-are inel: tocmai lipsa lui îl deosebește dintr-o privire",
                  StatusDotRules.Ring(DotKind.Listening, Snap(mic: true), 0.5, 0.5) < 0 &&
                  StatusDotRules.Ring(DotKind.None, ContextSnapshot.Empty, 0.5, 0.5) < 0);

            Check("SD8", "Fără o valoare bună nu se desenează niciun inel (nu unul gol, nu unul plin)",
                  StatusDotRules.Ring(DotKind.Playing, ContextSnapshot.Empty, -1, -1) < 0 &&
                  StatusDotRules.Ring(DotKind.Playing, ContextSnapshot.Empty, 1.5, -1) < 0 &&
                  StatusDotRules.Ring(DotKind.Charging, Snap(battery: true, charging: true), -1, -1) < 0);

            var top = StatusDotRules.RingEnd(0, 10);
            var half = StatusDotRules.RingEnd(0.5, 10);
            var full = StatusDotRules.RingEnd(1, 10);
            var quarter = StatusDotRules.RingEnd(0.25, 10);
            Check("SD9", "Inelul pleacă din vârf și merge în sensul acelor de ceasornic",
                  Math.Abs(top.X - 10) < 0.001 && Math.Abs(top.Y) < 0.001 && !top.Large &&
                  Math.Abs(quarter.X - 20) < 0.001 && Math.Abs(quarter.Y - 10) < 0.001 && !quarter.Large &&
                  Math.Abs(half.X - 10) < 0.001 && Math.Abs(half.Y - 20) < 0.001 &&
                  Math.Abs(full.X - 10) < 0.001 && Math.Abs(full.Y) < 0.001 && full.Large);
        }

        static void StatusDotLook()
        {
            Check("SD10", "Bulina ține de notch-ul în repaus: dispare deschis, peste o alertă și cât e ascuns",
                  StatusDotRules.Visible(DotKind.Playing, false, false, false) &&
                  !StatusDotRules.Visible(DotKind.Playing, true, false, false) &&
                  !StatusDotRules.Visible(DotKind.Playing, false, true, false) &&
                  !StatusDotRules.Visible(DotKind.Playing, false, false, true) &&
                  !StatusDotRules.Visible(DotKind.None, false, false, false));

            Check("SD11", "Lipită de ramă se contopește în sertar: un cerc plutitor ar fi singurul lucru care nu atinge marginea",
                  StatusDotRules.Fused(true) && !StatusDotRules.Fused(false));

            // Raportat de autor pe prima versiune: „bulina nu împrumută din comportamentul notch-ului și nu s-a lipit
            // de ecran, pare cam dezechilibrată”. Discul urmează acum înălțimea pastilei, iar lipit de ramă nu mai
            // plutește deloc.
            Check("SD21", "Discul se micșorează odată cu pastila: standby 34 → 20, forma mică 22 → 14, niciodată mai mic",
                  StatusDotRules.Size(34) == 20 && StatusDotRules.Size(22) == StatusDotRules.MinDiameter &&
                  StatusDotRules.Size(60) == StatusDotRules.Diameter && StatusDotRules.Size(0) == StatusDotRules.Diameter &&
                  StatusDotRules.MinDiameter < StatusDotRules.Diameter);

            Check("SD12", "Fiecare stare are glifa ei din fontul existent și o pensulă din temă, nu o culoare scrisă în cod",
                  new[] { DotKind.Listening, DotKind.Working, DotKind.Playing, DotKind.Charging }
                      .All(k => StatusDotRules.Glyph(k).Length == 1 && StatusDotRules.BrushKey(k).EndsWith("Brush", StringComparison.Ordinal) &&
                                StatusDotRules.Label(k).Length > 0) &&
                  StatusDotRules.Glyph(DotKind.None) == "" &&
                  new[] { DotKind.Listening, DotKind.Working, DotKind.Playing, DotKind.Charging }
                      .Select(StatusDotRules.Glyph).Distinct().Count() == 4 &&
                  StatusDotRules.BrushKey(DotKind.Listening) != StatusDotRules.BrushKey(DotKind.Charging));

            Check("SD13", "Mărimile: discul de 20, la 8 px de pastilă, cu glifa de 11 și un inel subțire",
                  StatusDotRules.Diameter == 20 && StatusDotRules.Gap == 8 && StatusDotRules.GlyphSize == 11 &&
                  StatusDotRules.RingThickness < 3 && StatusDotRules.GlyphSize < StatusDotRules.Diameter);

            Check("SD14", "Id-ul comutatorului e cel din catalog, funcția e Beta și pornită implicit (anunțată în 0.6.30)",
                  StatusDotRules.FeatureId == FeatureCatalog.StatusDot &&
                  FeatureCatalog.Find(StatusDotRules.FeatureId) is FeatureInfo f &&
                  f.Stage == FeatureStage.Beta && f.DefaultOn &&
                  f.Description.Contains("Motorul de context"));
        }

        static void StatusDotSourcePins()
        {
            string part = Src("Features/StatusDot/NotchWindow.StatusDot.cs"), notch = Src("NotchWindow.xaml.cs");

            Check("SD15", "Stările vin din motorul de context, nu dintr-o a doua citire a microfonului sau a media",
                  part.Contains("ContextEngine.Current?.Snapshot") &&
                  !part.Contains("PrivacyService") && !part.Contains("Microphone()"));

            Check("SD16", "Protocolul comutatorului și al contextului: abonare, dezabonare, UI doar pe Dispatcher, erorile prin ReportError",
                  part.Contains("FeatureFlags.Current.Changed += _dotFlagHandler;") &&
                  part.Contains("FeatureFlags.Current.Changed -= _dotFlagHandler;") &&
                  part.Contains("ContextEngine.Current.Changed += _dotContextHandler;") &&
                  part.Contains("ContextEngine.Current.Changed -= _dotContextHandler;") &&
                  part.Contains("Dispatcher.InvokeAsync(UpdateStatusDot)") &&
                  part.Contains("ReportError(StatusDotRules.FeatureId, ex)"));

            Check("SD17", "Niciun cronometru nou și nicio culoare scrisă în cod: se mișcă pe evenimente și pe pensulele temei",
                  !part.Contains("DispatcherTimer") && !part.Contains("CompositionTarget.Rendering") &&
                  !System.Text.RegularExpressions.Regex.IsMatch(part, @"Color\.From|#[0-9A-Fa-f]{6}|new SolidColorBrush") &&
                  part.Contains("SetResourceReference"));

            Check("SD18", "Legăturile din notch sunt trei rânduri: pornire, oprire și poziția din ApplyMode",
                  notch.Contains("StartStatusDot();") && notch.Contains("StopStatusDot();") &&
                  notch.Contains("StatusDotLayout(w, h, top, dur);"));

            Check("SD19", "Bulina pleacă de pe ecran odată cu pastila (aceeași deplasare), nu rămâne singură peste un joc",
                  part.Contains("new TransformGroup { Children = { PillShift, _dotShift } }") &&
                  part.Contains("IsHitTestVisible = false"));

            // Raportat de autor (0.6.30, cu două capturi): mergând spre notch, pastila se estompează, dar glifa de
            // muzică rămânea singură pe tapet, la putere întreagă. Pastila se estompează la hover (0,18) și dispare cu
            // totul cât se dă la o parte peste o fereastră maximizată (Fade(Pill, 0, 90)); bulina nu lua parte la
            // niciuna. Acum gazda poartă opacitatea pastilei, iar stratul dinăuntru ține aprinderea ei — WPF le
            // înmulțește, deci cele două animații nu se bat pe aceeași proprietate.
            Check("SD24", "Bulina se estompează exact cât pastila: gazda e legată de opacitatea ei, iar aprinderea bulinei stă pe stratul dinăuntru",
                  part.Contains("_dotHost.SetBinding(OpacityProperty, new System.Windows.Data.Binding(\"Opacity\") { Source = Pill });") &&
                  part.Contains("FadeLayer(_dotFade, visible, 140);") && !part.Contains("FadeLayer(_dotHost") &&
                  part.Contains("_dotHost.Children.Add(_dotFade);"));

            Check("SD22", "Lipit de ramă chiar se contopește: fără disc, fără inel, glifa intră în pastilă după o linie",
                  part.Contains("if (StatusDotRules.Fused(Anchored))") &&
                  part.Contains("_dotDisc.Visibility = Visibility.Collapsed;") &&
                  part.Contains("_dotDivider.Visibility = Visibility.Visible;") &&
                  part.Contains("StatusDotRules.Fused(Anchored)") &&
                  part.Contains("StatusDotRules.FusedInset"));

            Check("SD23", "Plutitoare, împrumută forma pastilei: capsulă cu raza jumătate din înălțime, aceeași umbră",
                  part.Contains("new CornerRadius(d / 2)") && part.Contains("_dotHost.Effect = Pill.Effect;") &&
                  part.Contains("StatusDotRules.Size(pillHeight / scale)"));

            Check("SD20", "Progresul piesei e citit din aceeași sursă ca linia de pe pastila mică, nu calculat a doua oară",
                  part.Contains("mi.LivePosition.TotalSeconds / mi.Duration.TotalSeconds"));
        }
    }
}
