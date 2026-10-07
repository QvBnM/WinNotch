using System;
using System.Linq;
using WinNotch.Core.Flags;
using WinNotch.Features.NotchAnchored;

namespace WinNotch
{
    public static partial class T
    {
        /// <summary>
        /// P50: the notch is a continuation of the monitor's bezel — stuck to the top edge, rounded only at the bottom,
        /// with a concave fillet on each side. The pure geometry, the limits and the non-regression with the switch off.
        /// </summary>
        static void NotchAnchoredTests()
        {
            AnchoredOutline();
            AnchoredLimits();
            AnchoredSourcePins();
        }

        static void AnchoredOutline()
        {
            // the standby pill
            double r = AnchoredGeometry.Radius(17), e = AnchoredGeometry.Ear(17, 300, 820);
            var (start, segs) = AnchoredGeometry.Outline(300, 34, 17, e);
            Check("NA1", "Pastila de standby (300×34): începe în afara lățimii cu urechea, apoi coboară, colțurile de jos sunt convexe, iar urechea din dreapta iese simetric",
                  start.Near(-e, 0) && segs.Count == 7 &&
                  segs[0].To.Near(0, e) && segs[0].IsArc && segs[0].Clockwise &&
                  segs[1].To.Near(0, 34 - r) && !segs[1].IsArc &&
                  segs[2].To.Near(r, 34) && segs[2].IsArc && !segs[2].Clockwise &&
                  segs[3].To.Near(300 - r, 34) &&
                  segs[4].To.Near(300, 34 - r) && segs[4].IsArc && !segs[4].Clockwise &&
                  segs[5].To.Near(300, e) &&
                  segs[6].To.Near(300 + e, 0) && segs[6].IsArc && segs[6].Clockwise);

            var (s2, g2) = AnchoredGeometry.Outline(720, 360, 17, AnchoredGeometry.Ear(17, 720, 820));
            Check("NA2", "Panoul deschis (720×360): aceeași formă, închisă pe marginea de sus",
                  g2.Count == 7 && g2[3].To.Near(720 - r, 360) && s2.Y == 0 && g2[6].To.Y == 0 &&
                  AnchoredGeometry.IsClosed(720, 360, 17, 12) && AnchoredGeometry.IsClosed(300, 34, 17, 12));

            Check("NA3", "Urechea se reduce când pastila plus urechile n-ar încăpea în fereastră",
                  AnchoredGeometry.Ear(17, 720, 820) == Math.Clamp(17 * 0.75, 10, 22) &&
                  Math.Abs(AnchoredGeometry.Ear(17, 810, 820) - 5) < 0.001 &&
                  AnchoredGeometry.Ear(17, 820, 820) == 0 && AnchoredGeometry.Ear(17, 900, 820) == 0);

            Check("NA4", "Raza de jos respectă setarea, între 12 și 28; urechea e trei sferturi din ea, între 10 și 22",
                  AnchoredGeometry.Radius(17) == 17 && AnchoredGeometry.Radius(4) == 12 && AnchoredGeometry.Radius(40) == 28 &&
                  AnchoredGeometry.Ear(28, 300, 820) == 21 && AnchoredGeometry.Ear(12, 300, 820) == 10 &&
                  AnchoredGeometry.Ear(40, 300, 820) == 21);

            var tiny = Outline(20, 10, 28, 10);
            Check("NA5", "Raza nu depășește jumătatea pastilei, nici înălțimea (o pastilă foarte mică nu se strâmbă)",
                  tiny[1].To.Y >= 0 && tiny[1].To.Y <= 10 && tiny[2].To.Near(10, 10) && tiny.Last().To.Near(30, 0));
        }

        /// <summary>Convenience: the segments of an outline, for the short assertions above.</summary>
        static System.Collections.Generic.IReadOnlyList<Seg> Outline(double w, double h, double r, double e) =>
            AnchoredGeometry.Outline(w, h, r, e).Segments;

        static void AnchoredLimits()
        {
            Check("NA6", "Fundalul nu coboară sub 0,92 în modul ancorat, dar respectă setarea în modul vechi",
                  AnchoredGeometry.BgOpacity(0.7, true) == AnchoredGeometry.MinOpacity &&
                  AnchoredGeometry.BgOpacity(0.7, false) == 0.7 &&
                  AnchoredGeometry.BgOpacity(1, true) == 1 && AnchoredGeometry.BgOpacity(0.2, false) == 0.6 &&
                  AnchoredGeometry.MinOpacity == 0.92);

            Check("NA7", "Lățimea în standby e ținută între 240 și 520 cât e ancorat; neatinsă altfel",
                  AnchoredGeometry.IdleWidth(150, true) == 240 && AnchoredGeometry.IdleWidth(640, true) == 520 &&
                  AnchoredGeometry.IdleWidth(300, true) == 300 &&
                  AnchoredGeometry.IdleWidth(150, false) == 150 && AnchoredGeometry.IdleWidth(640, false) == 640);

            Check("NA8", "Conturul pastilei (pentru tăierea conținutului) e același, fără urechi, și începe în colțul din stânga-sus",
                  AnchoredGeometry.PillOnly(300, 34, 17).Start.Near(0, 0) &&
                  AnchoredGeometry.PillOnly(300, 34, 17).Segments.Count == 5 &&
                  AnchoredGeometry.PillOnly(300, 34, 17).Segments.Last().To.Near(300, 0) &&
                  AnchoredGeometry.TopMargin == 0);

            Check("NA9", "Umbra cade doar în jos (nicio linie peste ramă)",
                  AnchoredGeometry.ShadowDirection == 270 && AnchoredGeometry.ShadowDepth == 6 &&
                  AnchoredGeometry.ShadowBlur == 24 && AnchoredGeometry.ShadowOpacity == 0.5);

            Check("NA10", "Comutatorul din catalog are id-ul geometriei, e Experimental și oprit implicit (se anunță mai târziu)",
                  AnchoredGeometry.FeatureId == FeatureCatalog.NotchAnchored &&
                  FeatureCatalog.Find(AnchoredGeometry.FeatureId) is FeatureInfo fi &&
                  fi.Stage == FeatureStage.Experimental && !fi.DefaultOn);
        }

        /// <summary>The hooks in the old files: a few lines, and the hover area stays the pill's rectangle (without the fillets).</summary>
        static void AnchoredSourcePins()
        {
            string notch = Src("NotchWindow.xaml.cs"), part = Src("Features/NotchAnchored/NotchWindow.Anchored.cs"),
                   themes = Src("Themes.cs");

            Check("NA11", "Legăturile sunt câte un rând: marginea și raza din ApplyMode, lățimea în standby, forma din ApplyRadius și UpdateClip, pornire și oprire",
                  notch.Contains("top = AnchoredTop(top);") && notch.Contains("r = AnchoredRadius(r);") &&
                  notch.Contains("w = AnchoredIdleWidth(IdleWidth());") && notch.Contains("AnchoredRadius(r, mini || _mode == Mode.Live)") &&
                  notch.Contains("if (AnchoredShape()) return;") && notch.Contains("StartAnchored();") && notch.Contains("StopAnchored();"));

            Check("NA12", "Zona de hover rămâne dreptunghiul pastilei: racordările nu primesc mouse-ul",
                  part.Contains("IsHitTestVisible = false") && !part.Contains("PillScreenRect"));

            Check("NA16", "Forma desenată vine din geometria pură (un singur traducător), nu scrisă a doua oară de mână",
                  part.Contains("AnchoredGeometry.PillOnly(") && part.Contains("AnchoredGeometry.Outline(") &&
                  part.Contains("private static StreamGeometry Build(") &&
                  System.Text.RegularExpressions.Regex.Matches(part, @"new StreamGeometry\(\)").Count == 1);

            Check("NA17", "Forma se reconstruiește doar când s-a schimbat ceva (nu la fiecare cadru al animației)",
                  part.Contains("if (Near(w, _shapeW) && Near(h, _shapeH) && Near(r, _shapeR) && Near(e, _shapeE)) return true;") &&
                  part.Contains("ThemeManager.Apply(S);"));

            Check("NA13", "Fundalul citește comutatorul prin regula pură (nicio limită scrisă de două ori)",
                  themes.Contains("AnchoredGeometry.BgOpacity("));

            Check("NA14", "Geometria se reconstruiește la schimbarea mărimii și e înghețată; fără culori scrise în cod; fără cronometre noi",
                  part.Contains("clip.Freeze();") && part.Contains("g.Freeze();") &&
                  part.Contains("SetResourceReference(Shape.FillProperty, \"NotchBrush\")") &&
                  !part.Contains("DispatcherTimer") && !part.Contains("CompositionTarget.Rendering") &&
                  !System.Text.RegularExpressions.Regex.IsMatch(part, @"Color\.From|#[0-9A-Fa-f]{6}|new SolidColorBrush"));

            Check("NA15", "Protocolul comutatorului: citit la pornire, abonare la Changed cu Dispatcher și dezabonare, erorile prin ReportError",
                  part.Contains("FeatureFlags.Current.Changed += _anchoredFlagHandler;") &&
                  part.Contains("FeatureFlags.Current.Changed -= _anchoredFlagHandler;") &&
                  part.Contains("Dispatcher.InvokeAsync(ApplyAnchoredSwitch)") &&
                  part.Contains("ReportError(AnchoredGeometry.FeatureId, ex)"));
        }
    }
}
