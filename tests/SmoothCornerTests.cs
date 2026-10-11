using System;
using System.Linq;
using WinNotch.Core.Flags;
using WinNotch.Core.Ui;
using WinNotch.Features.NotchAnchored;

namespace WinNotch
{
    public static partial class T
    {
        /// <summary>
        /// P72: the bottom corners curve gradually instead of entering a circular arc all at once (reported by the
        /// author: "s-ar putea face marginile mai smooth"). The maths, the outline with the switch on and the
        /// non-regression with it off — the shape must come out exactly as before.
        /// </summary>
        static void SmoothCornerTests()
        {
            SmoothCornerMath();
            SmoothOutline();
            SmoothSourcePins();
        }

        static void SmoothCornerMath()
        {
            Check("SC1", "Cât ține curba pe o latură: de 1,6 ori raza, dar niciodată peste spațiul laturii și niciodată sub rază",
                  Math.Abs(SmoothCorner.Extent(16, 100) - 25.6) < 0.001 &&
                  Math.Abs(SmoothCorner.Extent(16, 20) - 20) < 0.001 &&
                  Math.Abs(SmoothCorner.Extent(16, 12) - 12) < 0.001 &&
                  SmoothCorner.Extent(0, 50) == 0 && SmoothCorner.Extent(16, 0) == 0 && SmoothCorner.Extent(-5, 50) == 0);

            var (cx, cy) = SmoothCorner.Control(0, 12, 0, 30);
            Check("SC2", "Punctul de control stă între capătul curbei și colț, la 0,63 din drum",
                  Math.Abs(cx) < 0.001 && Math.Abs(cy - 23.34) < 0.001 &&
                  SmoothCorner.Pull > 0.5 && SmoothCorner.Pull < 0.75 && SmoothCorner.Reach > 1);

            Check("SC3", "Id-ul comutatorului e cel din catalog, funcția e experimentală și oprită implicit",
                  SmoothCorner.FeatureId == FeatureCatalog.SmoothCorners &&
                  FeatureCatalog.Find(SmoothCorner.FeatureId) is FeatureInfo f &&
                  f.Stage == FeatureStage.Experimental && !f.DefaultOn);
        }

        static void SmoothOutline()
        {
            // oprit: exact conturul de până acum (arce), inclusiv prin supraîncărcarea veche
            var old4 = AnchoredGeometry.Outline(300, 40, 10, 6).Segments;
            var off = AnchoredGeometry.Outline(300, 40, 10, 6, false).Segments;
            Check("SC4", "Cu comutatorul oprit, conturul e neschimbat: aceleași șapte bucăți, colțurile de jos arce",
                  off.Count == 7 && old4.Count == 7 && !off.Any(s => s.IsCurve) &&
                  off[2].IsArc && off[2].To.Near(10, 40) && off[4].IsArc && off[4].To.Near(300, 30) &&
                  off.Select((s, i) => s.To.Near(old4[i].To.X, old4[i].To.Y) && s.IsArc == old4[i].IsArc).All(ok => ok));

            var (start, on) = AnchoredGeometry.Outline(300, 40, 10, 6, true);
            Check("SC5", "Cu el pornit, colțurile de jos sunt curbe cubice care pornesc din 16 px pe fiecare latură (1,6 × 10)",
                  on.Count == 7 && on[1].To.Near(0, 24) &&
                  on[2].IsCurve && !on[2].IsArc && on[2].To.Near(16, 40) &&
                  on[3].To.Near(284, 40) &&
                  on[4].IsCurve && !on[4].IsArc && on[4].To.Near(300, 24) &&
                  on[0].IsArc && on[6].IsArc);                       // urechile rămân arce

            Check("SC6", "Punctele de control trag spre colț, nu în afara lui: curba rămâne în interiorul colțului",
                  on[2].C1.Near(0, 34.08) && on[2].C2.Near(5.92, 40) &&
                  on[4].C1.Near(294.08, 40) && on[4].C2.Near(300, 34.08));

            Check("SC7", "Conturul se închide pe marginea de sus și cu colțuri continue",
                  Math.Abs(start.Y) < 0.001 && Math.Abs(on[6].To.Y) < 0.001 && on[6].To.X > start.X);

            // Pe forma mică (22 px) curba nu are voie să urce peste capătul urechii: latura ar merge înapoi și
            // conturul s-ar îndoi peste el însuși — aceeași regresie pe care o prinde NA18 pentru arce.
            double eLow = AnchoredGeometry.Ear(28, 300, 820, 22);
            var low = AnchoredGeometry.Outline(300, 22, 28, eLow, true).Segments;
            Check("SC8", "Pe pastila mică, curba se oprește la capătul urechii: latura nu merge niciodată înapoi",
                  low[1].To.Y >= eLow - 0.001 && low[1].To.Y >= low[0].To.Y - 0.001,
                  "ureche = " + eLow + ", latura se termină la y = " + low[1].To.Y);

            var pill = AnchoredGeometry.PillOnly(300, 40, 10, true);
            Check("SC9", "Decupajul conținutului folosește aceleași colțuri ca silueta pictată",
                  pill.Start.Near(0, 0) && pill.Segments.Count == 5 &&
                  pill.Segments[1].IsCurve && pill.Segments[3].IsCurve &&
                  pill.Segments[1].To.Near(16, 40) && AnchoredGeometry.PillOnly(300, 40, 10).Segments[1].IsArc);
        }

        static void SmoothSourcePins()
        {
            string shape = Src("Features/NotchAnchored/AnchoredShape.cs"),
                   part = Src("Features/NotchAnchored/NotchWindow.Anchored.cs"),
                   prev = Src("Features/WindowV2/NotchPreview.cs");

            Check("SC10", "Traducătorul spre WPF știe să deseneze curba, nu doar linii și arce",
                  shape.Contains("if (seg.IsCurve)") && shape.Contains("c.BezierTo("));

            Check("SC11", "Notch-ul citește comutatorul, îl ține în cheia de cache (altfel forma nu s-ar reface) și raportează erorile",
                  part.Contains("IsEnabled(SmoothCorner.FeatureId)") && part.Contains("smooth == _shapeSmooth") &&
                  part.Contains("_shapeSmooth = smooth;") && part.Contains("ReportError(SmoothCorner.FeatureId, ex)"));

            // Forma din fereastră e previzualizarea de pe „monitorul” din ramă (P70), nu un antet: acolo se verifică.
            Check("SC12", "Previzualizarea din fereastră urmează același comutator: silueta din margine și cea din fereastră nu se despart",
                  prev.Contains("SmoothCorner.FeatureId"));
        }
    }
}
