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
                  tiny[1].To.Near(0, 5) && tiny[2].To.Near(5, 10) && tiny.Last().To.Near(30, 0));

            // Raportat de autor: „racordările nu se continuă lin în colțurile de jos; se vede o îmbinare”.
            // Pe o pastilă joasă (forma mică, 22 px) urechea de 21 px ajungea mai jos decât începutul colțului de jos
            // (H − R = 11), deci latura verticală mergea înapoi și conturul se îndoia peste el însuși.
            double eLow = AnchoredGeometry.Ear(28, 300, 820, 22);
            var low = Outline(300, 22, 28, eLow);
            Check("NA18", "Urechea e limitată și de înălțime: latura nu mai merge înapoi, racordarea se continuă lin în colțul de jos",
                  eLow == 11 && low[0].To.Y <= low[1].To.Y && Math.Abs(low[0].To.Y - low[1].To.Y) < 0.001 &&
                  AnchoredGeometry.Ear(17, 300, 820, 34) == AnchoredGeometry.Ear(17, 300, 820) &&
                  AnchoredGeometry.Ear(17, 720, 820, 360) == AnchoredGeometry.Ear(17, 720, 820),
                  "urechea = " + eLow);
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
                  notch.Contains("top = AnchoredTop(top);") &&
                  notch.Contains("w = AnchoredIdleWidth(IdleWidth());") && notch.Contains("AnchoredRadius(r, mini || _mode == Mode.Live)") &&
                  notch.Contains("if (ApplyAnchoredShape()) return;") && notch.Contains("StartAnchored();") && notch.Contains("StopAnchored();"));

            Check("NA12", "Zona de hover rămâne dreptunghiul pastilei: racordările nu primesc mouse-ul",
                  part.Contains("IsHitTestVisible = false") && !part.Contains("PillScreenRect"));

            string shape = Src("Features/NotchAnchored/AnchoredShape.cs"), v2 = Src("Features/WindowV2/WindowV2.cs");
            Check("NA16", "Forma desenată vine din geometria pură printr-un singur traducător, folosit și de notch și de antetul ferestrei",
                  shape.Contains("AnchoredGeometry.PillOnly(") && shape.Contains("AnchoredGeometry.Outline(") &&
                  shape.Contains("internal static StreamGeometry Build(") && shape.Contains("g.Freeze();") &&
                  part.Contains("AnchoredShape.Build(") && part.Contains("AnchoredShape.Silhouette(") &&
                  v2.Contains("AnchoredShape.Silhouette(") &&
                  // nicio a doua copie scrisă de mână: un singur loc deschide un StreamGeometry
                  System.Text.RegularExpressions.Regex.Matches(shape, @"new StreamGeometry\(\)").Count == 1 &&
                  !part.Contains("new StreamGeometry()") && !v2.Contains("new StreamGeometry()") && !v2.Contains("c.ArcTo("));

            Check("NA17", "Forma se reconstruiește doar când s-a schimbat ceva (nu la fiecare cadru al animației)",
                  part.Contains("if (Near(w, _shapeW) && Near(h, _shapeH) && Near(r, _shapeR) && Near(e, _shapeE)) return true;") &&
                  part.Contains("ThemeManager.Apply(S);"));

            Check("NA13", "Fundalul citește comutatorul prin regula pură (nicio limită scrisă de două ori)",
                  themes.Contains("AnchoredGeometry.BgOpacity("));

            // Raportat de autor pe 0.6.21: pe tema luminoasă forma notch-ului și ce e desenat peste ea aveau nuanțe
            // diferite, iar racordările se vedeau îmbinate. Cauza: pastila (Border) și silueta (Path) pictau amândouă
            // NotchBrush, deci corpul primea două straturi de 0,92 (≈0,994) iar racordările unul, plus două contururi
            // antialiasate suprapuse exact în colțurile de jos.
            Check("NA19", "O singură suprafață pictată: cât e ancorat, pastila nu-și mai desenează fundalul, iar umbra e pe siluetă",
                  part.Contains("Pill.Background = Brushes.Transparent;") &&
                  part.Contains("Pill.Effect = null;") &&
                  part.Contains("Pill.SetResourceReference(Border.BackgroundProperty, \"NotchBrush\");") &&
                  System.Text.RegularExpressions.Regex.IsMatch(part, @"_anchoredShape = new Path[\s\S]{0,900}?new DropShadowEffect"),
                  "pastila încă pictează fundalul sub siluetă");

            Check("NA20", "Silueta urmează pastila (margine, vizibilitate, opacitate, deplasare) și nu e lipită la grila de pixeli",
                  part.Contains("new System.Windows.Data.Binding(\"Margin\") { Source = Pill }") &&
                  part.Contains("new System.Windows.Data.Binding(\"Visibility\") { Source = Pill }") &&
                  part.Contains("new System.Windows.Data.Binding(\"Opacity\") { Source = Pill }") &&
                  part.Contains("_anchoredShape.RenderTransform = PillShift;") &&
                  part.Contains("UseLayoutRounding = false, SnapsToDevicePixels = false,") &&
                  // fără urechi nu se mai desenează „nimic”: pastila nu mai are fundal propriu
                  !part.Contains("Data = null"));

            Check("NA21", "Pensulele se reconstruiesc la pornirea și la oprirea comutatorului (opacitatea minimă nu mai aștepta o salvare de setări)",
                  themes.Contains("+ \"|\" + anchored") && themes.Contains("bool anchored = Core.Flags.FeatureFlags.Current?.IsEnabled("));

            Check("NA14", "Fără culori scrise în cod; fără cronometre noi",
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
