using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace WinNotch.Features.NotchAnchored
{
    /// <summary>
    /// P50: the one translator from the pure outline (<see cref="AnchoredGeometry"/>) to a WPF geometry. Both the notch
    /// and the header of the WinNotch window draw through it, so the shape in the screen's edge and the shape at the top
    /// of the window can never drift apart — the brief's whole point.
    /// </summary>
    internal static class AnchoredShape
    {
        /// <summary>The outline as a frozen geometry, shifted right by <paramref name="shiftX"/> (the left ear's room).</summary>
        internal static StreamGeometry Build(Pt start, IReadOnlyList<Seg> segments, double shiftX)
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(start.X + shiftX, start.Y), true, true);
                foreach (var seg in segments)
                {
                    var to = new Point(seg.To.X + shiftX, seg.To.Y);
                    if (seg.IsArc && seg.Radius > 0)
                        c.ArcTo(to, new Size(seg.Radius, seg.Radius), 0, false,
                                seg.Clockwise ? SweepDirection.Clockwise : SweepDirection.Counterclockwise, false, false);
                    else c.LineTo(to, false, false);
                }
            }
            g.Freeze();
            return g;
        }

        /// <summary>The whole silhouette (pill plus both fillets), its left ear starting at x = 0.</summary>
        internal static StreamGeometry Silhouette(double w, double h, double r, double e)
        {
            if (e <= 0)
            {
                var only = AnchoredGeometry.PillOnly(w, h, r);
                return Build(only.Start, only.Segments, 0);
            }
            var (start, segs) = AnchoredGeometry.Outline(w, h, r, e);
            return Build(start, segs, e);
        }
    }
}
