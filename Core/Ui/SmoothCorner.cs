using System;

namespace WinNotch.Core.Ui
{
    /// <summary>
    /// A corner whose curvature grows gradually instead of jumping from straight to a circular arc ("continuous
    /// curvature", the shape of an iOS icon). An arc of radius R starts curving at full strength the moment the
    /// straight edge ends, and the eye catches that break: the author described the arc corners as not smooth enough.
    /// Here the curve starts further out along each edge and reaches the same visual roundness without a seam.
    ///
    /// Pure (no WPF): one cubic Bézier per corner, described by its two control points. The callers turn it into a
    /// geometry. The numbers are fixed, so the same corner comes out the same everywhere it is drawn.
    /// </summary>
    public static class SmoothCorner
    {
        /// <summary>Same id as the entry in FeatureCatalog (the tests check they match).</summary>
        public const string FeatureId = "colturi-continue";

        /// <summary>
        /// How far the curve reaches along an edge, as a multiple of the radius. 1.6 is the roundness an arc of the
        /// same radius reads as, spread over a longer run so the start of the curve cannot be seen.
        /// </summary>
        public const double Reach = 1.6;

        /// <summary>
        /// How far each control point sits from its end point, towards the corner itself, as a fraction of that
        /// distance. Below ~0.5 the curve flattens into a chamfer, above ~0.75 it bulges back into an arc.
        /// </summary>
        public const double Pull = 0.63;

        /// <summary>
        /// How far the curve runs along one edge: <see cref="Reach"/> times the radius, but never past the room that
        /// edge has (<paramref name="room"/>, normally half its length, so two corners of the same edge cannot
        /// overlap) and never less than the radius itself — a corner squeezed below its radius is still a corner.
        /// </summary>
        public static double Extent(double radius, double room)
        {
            double r = Math.Max(0, radius);
            if (r <= 0) return 0;
            double max = Math.Max(0, room);
            if (max <= 0) return 0;
            return Math.Clamp(r * Reach, Math.Min(r, max), max);
        }

        /// <summary>
        /// The first control point of the corner's curve: between the point where the curve leaves the edge
        /// (<paramref name="fromX"/>, <paramref name="fromY"/>) and the corner itself.
        /// </summary>
        public static (double X, double Y) Control(double fromX, double fromY, double cornerX, double cornerY) =>
            (fromX + (cornerX - fromX) * Pull, fromY + (cornerY - fromY) * Pull);
    }
}
