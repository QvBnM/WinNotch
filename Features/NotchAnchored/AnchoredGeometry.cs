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

    /// <summary>A piece of the outline: a straight line, or an arc with its radius and sweep.</summary>
    public readonly struct Seg
    {
        public Seg(Pt to, double radius, bool clockwise, bool arc) { To = to; Radius = radius; Clockwise = clockwise; IsArc = arc; }
        public Pt To { get; }
        public double Radius { get; }
        /// <summary>Sweep direction; the concave "ears" turn the other way from the convex bottom corners.</summary>
        public bool Clockwise { get; }
        public bool IsArc { get; }
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
        public static double Ear(double radius, double pillWidth, double windowWidth)
        {
            double e = Math.Clamp(Radius(radius) * 0.75, 10, 22);
            if (windowWidth <= 0 || pillWidth <= 0) return e;
            double room = (windowWidth - pillWidth) / 2;
            return room >= e ? e : Math.Max(0, room);
        }

        /// <summary>
        /// The outline, starting at the left ear's outer end and closing along the top edge (y = 0):
        /// concave ear → left side → convex bottom-left → bottom → convex bottom-right → right side → concave ear.
        /// </summary>
        public static (Pt Start, IReadOnlyList<Seg> Segments) Outline(double w, double h, double radius, double ear)
        {
            // The radius comes in already clamped by <see cref="Radius"/> and scaled: here only the geometry limits it.
            double r = Math.Max(0, Math.Min(radius, Math.Min(w / 2, h / 2)));
            double e = Math.Max(0, ear);
            var segs = new List<Seg>
            {
                new Seg(new Pt(0, e), e, true, e > 0),              // left ear, concave (centre at (-e, e))
                new Seg(new Pt(0, h - r), 0, false, false),
                new Seg(new Pt(r, h), r, false, r > 0),             // bottom-left, convex
                new Seg(new Pt(w - r, h), 0, false, false),
                new Seg(new Pt(w, h - r), r, false, r > 0),         // bottom-right, convex
                new Seg(new Pt(w, e), 0, false, false),
                new Seg(new Pt(w + e, 0), e, true, e > 0),          // right ear, concave (centre at (w + e, e))
            };
            return (new Pt(-e, 0), segs);
        }

        /// <summary>The outline is closed: the last point and the start sit on the top edge, so the figure shuts along it.</summary>
        public static bool IsClosed(double w, double h, double radius, double ear)
        {
            var (start, segs) = Outline(w, h, radius, ear);
            var last = segs[segs.Count - 1].To;
            return Math.Abs(start.Y) < 0.001 && Math.Abs(last.Y) < 0.001 && last.X > start.X;
        }

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
        public static (Pt Start, IReadOnlyList<Seg> Segments) PillOnly(double w, double h, double radius)
        {
            var (_, segs) = Outline(w, h, radius, 0);
            return (new Pt(0, 0), segs.Where(x => x.IsArc || x.To.X != 0 || x.To.Y != 0).ToList());
        }

        /// <summary>The shadow falls downwards only; upwards it would draw a line over the bezel.</summary>
        public const double ShadowDirection = 270, ShadowDepth = 6, ShadowBlur = 24, ShadowOpacity = 0.5;
    }
}
