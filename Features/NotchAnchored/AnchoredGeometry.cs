using System;
using System.Collections.Generic;
using System.Linq;

namespace WinNotch.Features.NotchAnchored
{
    /// <summary>One point of the pill's outline, in the pill's own coordinates (origin: its top-left corner).</summary>
    public readonly struct Pt
    {
        public Pt(double x, double y) { X = x; Y = y; }
        public double X { get; }
        public double Y { get; }
        public bool Near(double x, double y, double tol = 0.001) => Math.Abs(X - x) <= tol && Math.Abs(Y - y) <= tol;
        public override string ToString() => "(" + Math.Round(X, 2) + "," + Math.Round(Y, 2) + ")";
    }

    /// <summary>A piece of the outline: a straight line, an arc with its radius and sweep, or a cubic curve.</summary>
    public readonly struct Seg
    {
        public Seg(Pt to, double radius, bool clockwise, bool arc)
        {
            To = to; Radius = radius; Clockwise = clockwise; IsArc = arc;
            C1 = default; C2 = default; IsCurve = false;
        }

        /// <summary>A cubic Bézier: the two control points, then the end point (see <see cref="Core.Ui.SmoothCorner"/>).</summary>
        public Seg(Pt c1, Pt c2, Pt to)
        {
            To = to; Radius = 0; Clockwise = false; IsArc = false;
            C1 = c1; C2 = c2; IsCurve = true;
        }

        public Pt To { get; }
        public double Radius { get; }
        /// <summary>Sweep direction; the concave "ears" turn the other way from the convex bottom corners.</summary>
        public bool Clockwise { get; }
        public bool IsArc { get; }
        /// <summary>The control points of a cubic corner; meaningless unless <see cref="IsCurve"/>.</summary>
        public Pt C1 { get; }
        public Pt C2 { get; }
        public bool IsCurve { get; }
    }

    /// <summary>
    /// P50: the notch is not a pill floating over Windows, it grows out of the monitor's bezel — stuck to the top edge,
    /// rounded only at the bottom, with a concave fillet ("ear") on each side where it meets the frame. Pure geometry
    /// and limits (no WPF): the window only turns this into a <c>StreamGeometry</c>.
    /// </summary>
    public static class AnchoredGeometry
    {
        /// <summary>Same id as the entry in FeatureCatalog (the tests check they match).</summary>
        public const string FeatureId = "notch-anchored";

        /// <summary>The pill sits on the top edge: no margin above it in any mode.</summary>
        public const double TopMargin = 0;

        /// <summary>Bottom corner radius, from the user's setting.</summary>
        public static double Radius(double setting) => Math.Clamp(setting, 12, 28);

        /// <summary>
        /// The ear's radius: three quarters of the bottom radius, within 10–22, and reduced when the pill plus both ears
        /// would not fit the window (the window is 820 wide, the pill at most 720, so normally there is room).
        /// </summary>
        public static double Ear(double radius, double pillWidth, double windowWidth) =>
            Ear(radius, pillWidth, windowWidth, double.PositiveInfinity);

        /// <summary>
        /// The same, also limited by the pill's height: the ear ends at y = E and the bottom corner starts at y = H − R,
        /// so an ear taller than H − R would send the left side backwards and fold the outline over itself (a visible
        /// kink). The small form (22 px tall) is exactly where that happens.
        /// </summary>
        public static double Ear(double radius, double pillWidth, double windowWidth, double pillHeight)
        {
            double r = Radius(radius);
            double e = Math.Clamp(r * 0.75, 10, 22);
            if (pillHeight > 0 && !double.IsInfinity(pillHeight))
            {
                double side = pillHeight - Math.Min(r, Math.Min(pillWidth > 0 ? pillWidth / 2 : r, pillHeight / 2));
                e = Math.Min(e, Math.Max(0, side));
            }
            if (windowWidth <= 0 || pillWidth <= 0) return e;
            double room = (windowWidth - pillWidth) / 2;
            return room >= e ? e : Math.Max(0, room);
        }

        /// <summary>
        /// The outline, starting at the left ear's outer end and closing along the top edge (y = 0):
        /// concave ear → left side → convex bottom-left → bottom → convex bottom-right → right side → concave ear.
        /// </summary>
        public static (Pt Start, IReadOnlyList<Seg> Segments) Outline(double w, double h, double radius, double ear) =>
            Outline(w, h, radius, ear, false);

        /// <summary>
        /// The same outline, with the two bottom corners drawn either as arcs (<paramref name="smooth"/> false, the
        /// shape up to 0.6.26) or with continuous curvature (<see cref="Core.Ui.SmoothCorner"/>). The piece count and
        /// the order never change, so everything that walks the outline keeps working either way.
        /// </summary>
        public static (Pt Start, IReadOnlyList<Seg> Segments) Outline(double w, double h, double radius, double ear, bool smooth)
        {
            // The radius comes in already clamped by <see cref="Radius"/> and scaled: here only the geometry limits it.
            double r = Math.Max(0, Math.Min(radius, Math.Min(w / 2, h / 2)));
            double e = Math.Max(0, ear);
            // How far a smooth corner reaches: sideways at most half the width (the two corners share the bottom edge),
            // upwards at most the straight side left under the ear — past it the outline would fold over itself.
            double ex = smooth ? Core.Ui.SmoothCorner.Extent(r, w / 2) : r;
            double ey = smooth ? Core.Ui.SmoothCorner.Extent(r, Math.Max(0, h - e)) : r;
            var segs = new List<Seg>
            {
                new Seg(new Pt(0, e), e, true, e > 0),              // left ear, concave (centre at (-e, e))
                new Seg(new Pt(0, h - ey), 0, false, false),
                smooth ? Corner(0, h - ey, ex, h, 0, h)             // bottom-left, continuous
                       : new Seg(new Pt(r, h), r, false, r > 0),    // bottom-left, convex arc
                new Seg(new Pt(w - ex, h), 0, false, false),
                smooth ? Corner(w - ex, h, w, h - ey, w, h)         // bottom-right, continuous
                       : new Seg(new Pt(w, h - r), r, false, r > 0),// bottom-right, convex arc
                new Seg(new Pt(w, e), 0, false, false),
                new Seg(new Pt(w + e, 0), e, true, e > 0),          // right ear, concave (centre at (w + e, e))
            };
            return (new Pt(-e, 0), segs);
        }

        /// <summary>One corner as a cubic: from (fx, fy) to (tx, ty), bending around the corner point (cx, cy).</summary>
        private static Seg Corner(double fx, double fy, double tx, double ty, double cx, double cy)
        {
            var (c1x, c1y) = Core.Ui.SmoothCorner.Control(fx, fy, cx, cy);
            var (c2x, c2y) = Core.Ui.SmoothCorner.Control(tx, ty, cx, cy);
            return new Seg(new Pt(c1x, c1y), new Pt(c2x, c2y), new Pt(tx, ty));
        }

        /// <summary>The outline is closed: the last point and the start sit on the top edge, so the figure shuts along it.</summary>
        public static bool IsClosed(double w, double h, double radius, double ear)
        {
            var (start, segs) = Outline(w, h, radius, ear);
            var last = segs[segs.Count - 1].To;
            return Math.Abs(start.Y) < 0.001 && Math.Abs(last.Y) < 0.001 && last.X > start.X;
        }

        /// <summary>
        /// Whether the concave fillets are drawn. False since 0.6.27: on a MacBook the image really does flow around a
        /// hole in the panel, which is what a fillet promises — Windows has no such hole, so the promise is false and
        /// the shape reads as sitting unnaturally on the edge (the author's words). What is drawn instead is a drawer:
        /// square shoulders, rounded only at the bottom, with a hairline of light along the top edge, so it reads as an
        /// object hanging from the bezel rather than a cut into it. The outline keeps supporting ears (<see cref="Ear"/>
        /// and <see cref="Outline"/> are unchanged and still tested) in case the shape is ever wanted back.
        /// </summary>
        public const bool DrawEars = false;

        /// <summary>The hairline along the top edge: white at this opacity, one pixel, inside the pill's own clip.</summary>
        public const double SeamOpacity = 0.14, SeamThickness = 1;

        /// <summary>
        /// How much shorter the standby pill is while anchored. Floating, it reads as 34 px plus the 8 px of screen
        /// above it; stuck to the edge that gap is gone, so the same 34 px look heavier than before. The small form
        /// (22 px), the alerts and the open panel keep their heights.
        /// </summary>
        public const double StandbyDrop = 4;

        /// <summary>The standby pill's height: shorter while anchored, untouched otherwise and in every other mode.</summary>
        public static double StandbyHeight(double height, bool anchored, bool standby, double scale)
        {
            if (!anchored || !standby) return height;
            double drop = StandbyDrop * (scale > 0 ? scale : 1);
            return Math.Max(MiniHeight, height - drop);
        }

        /// <summary>The standby pill never shrinks under the small form's height, whatever the scale.</summary>
        private const double MiniHeight = 22;

        /// <summary>In the anchored look the background is never see-through enough to break the illusion.</summary>
        public const double MinOpacity = 0.92;

        /// <summary>The background's opacity: the user's setting, but at least <see cref="MinOpacity"/> while anchored.</summary>
        public static double BgOpacity(double setting, bool anchored)
        {
            double o = Math.Clamp(setting, 0.6, 1);
            return anchored ? Math.Max(MinOpacity, o) : o;
        }

        /// <summary>Standby width while anchored: visible enough, never in the way (240–520). The small form is untouched.</summary>
        public static double IdleWidth(double width, bool anchored) => anchored ? Math.Clamp(width, 240, 520) : width;

        /// <summary>
        /// The same outline without the ears (the pill's own shape, used to clip its content): starts at the top-left
        /// corner and closes along the top edge. Same rules, so the shape and its clip can never drift apart.
        /// </summary>
        public static (Pt Start, IReadOnlyList<Seg> Segments) PillOnly(double w, double h, double radius) =>
            PillOnly(w, h, radius, false);

        /// <summary>The same, with continuous corners when <paramref name="smooth"/> is on.</summary>
        public static (Pt Start, IReadOnlyList<Seg> Segments) PillOnly(double w, double h, double radius, bool smooth)
        {
            var (_, segs) = Outline(w, h, radius, 0, smooth);
            // the first and the last segment are the ears: without them the figure closes along the top edge
            return (new Pt(0, 0), segs.Skip(1).Take(segs.Count - 2).ToList());
        }

        /// <summary>The shadow falls downwards only; upwards it would draw a line over the bezel.</summary>
        public const double ShadowDirection = 270, ShadowDepth = 6, ShadowBlur = 24, ShadowOpacity = 0.5;
    }
}
