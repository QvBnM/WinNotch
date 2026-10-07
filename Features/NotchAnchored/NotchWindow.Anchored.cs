using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using WinNotch.Core.Flags;
using System.Collections.Generic;
using WinNotch.Features.NotchAnchored;

namespace WinNotch
{
    /// <summary>
    /// P50: the notch grows out of the monitor's bezel — stuck to the top edge, rounded only at the bottom, with a
    /// concave fillet on each side. The geometry and the limits are in <see cref="AnchoredGeometry"/> (pure, tested);
    /// here we only draw them and animate nothing new. With the switch off the pill floats exactly as before.
    /// </summary>
    public partial class NotchWindow
    {
        private bool _anchoredOn;
        private Action<string> _anchoredFlagHandler;
        /// <summary>The two concave fillets, drawn behind the pill in the window's root grid (null while the switch is off).</summary>
        private Path _anchoredEars;
        private Effect _anchoredOldShadow;

        private static bool AnchoredEnabled() => FeatureFlags.Current?.IsEnabled(AnchoredGeometry.FeatureId) ?? false;

        /// <summary>True while the anchored look is on (read by ApplyMode, ApplyRadius and IdleWidth).</summary>
        internal bool Anchored => _anchoredOn;

        private void StartAnchored()
        {
            ApplyAnchoredSwitch();
            if (_anchoredFlagHandler != null) return;
            _anchoredFlagHandler = id =>
            {
                if (!string.Equals(id, AnchoredGeometry.FeatureId, StringComparison.Ordinal)) return;
                Dispatcher.InvokeAsync(ApplyAnchoredSwitch);
            };
            if (FeatureFlags.Current != null) FeatureFlags.Current.Changed += _anchoredFlagHandler;
        }

        private void StopAnchored()
        {
            if (_anchoredFlagHandler != null && FeatureFlags.Current != null) FeatureFlags.Current.Changed -= _anchoredFlagHandler;
            _anchoredFlagHandler = null;
        }

        private void ApplyAnchoredSwitch()
        {
            try
            {
                bool on = AnchoredEnabled();
                if (on == _anchoredOn && (_anchoredEars != null) == on) return;
                _anchoredOn = on;
                if (on)
                {
                    _anchoredOldShadow ??= Pill.Effect;
                    Pill.Effect = new DropShadowEffect
                    {
                        Direction = AnchoredGeometry.ShadowDirection, ShadowDepth = AnchoredGeometry.ShadowDepth,
                        BlurRadius = AnchoredGeometry.ShadowBlur, Opacity = AnchoredGeometry.ShadowOpacity,
                        Color = Colors.Black, RenderingBias = RenderingBias.Performance,
                    };
                    if (_anchoredEars == null && Pill.Parent is Panel root)
                    {
                        _anchoredEars = new Path { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false };
                        _anchoredEars.SetResourceReference(Shape.FillProperty, "NotchBrush");
                        // the fillets follow the pill: same shift (hidden over a fullscreen app) and same opacity (hover, dodge)
                        _anchoredEars.RenderTransform = PillShift;
                        // No shadow of its own: the shape it draws contains the pill, so a second shadow would double
                        // the one under the pill's bottom edge. The fillets sit against the bezel, where no shadow shows.
                        _anchoredEars.SetBinding(OpacityProperty, new System.Windows.Data.Binding("Opacity") { Source = Pill });
                        root.Children.Insert(Math.Max(0, root.Children.IndexOf(Pill)), _anchoredEars);
                    }
                }
                else
                {
                    Pill.Effect = _anchoredOldShadow;
                    if (_anchoredEars != null && Pill.Parent is Panel root) { root.Children.Remove(_anchoredEars); _anchoredEars = null; }
                }
                _shapeW = _shapeH = _shapeR = _shapeE = -1;      // the shape is rebuilt whatever its size
                ThemeManager.Apply(S);                            // the minimum opacity lives in the brushes
                ApplyRadius();
                ApplyMode();
            }
            catch (Exception ex) { FeatureFlags.Current?.ReportError(AnchoredGeometry.FeatureId, ex); }
        }

        /// <summary>The top margin of the pill: 0 while anchored (it touches the bezel), the old value otherwise.</summary>
        private double AnchoredTop(double top) => _anchoredOn ? AnchoredGeometry.TopMargin : top;

        /// <summary>
        /// The bottom radius while anchored (the user's setting, within 12–28). The small form and the alerts keep their
        /// own radius: the brief's shape is the standby pill and the open panel.
        /// </summary>
        private double AnchoredRadius(double r, bool ownRadius) =>
            _anchoredOn && !ownRadius ? AnchoredGeometry.Radius(S.CornerRadius) * UiScale : r;

        /// <summary>Standby width while anchored: 240–520. The small form keeps its own width.</summary>
        private double AnchoredIdleWidth(double w) => AnchoredGeometry.IdleWidth(w, _anchoredOn);

        private double _shapeW = -1, _shapeH = -1, _shapeR = -1, _shapeE = -1;

        /// <summary>
        /// Hook in ApplyRadius / UpdateClip: the pill is rounded only at the bottom and its content is clipped with the
        /// same shape, so a coloured cover inside it is cut correctly. Both the clip and the fillets are built from the
        /// pure outline (<see cref="AnchoredGeometry"/>), and only when something actually changed — not per frame.
        /// </summary>
        private bool AnchoredShape()
        {
            if (!_anchoredOn) return false;
            double w = Pill.ActualWidth, h = Pill.ActualHeight;
            if (w <= 0 || h <= 0) return true;
            // Radius is the animated value (already scaled), so the shape follows the animation instead of jumping.
            double scale = Math.Max(0.01, UiScale);
            double r = Math.Min(Radius, Math.Min(w / 2, h / 2));
            double e = AnchoredGeometry.Ear(Radius / scale, w / scale, Width / scale) * scale;
            if (Near(w, _shapeW) && Near(h, _shapeH) && Near(r, _shapeR) && Near(e, _shapeE)) return true;
            _shapeW = w; _shapeH = h; _shapeR = r; _shapeE = e;

            Pill.CornerRadius = new CornerRadius(0, 0, r, r);
            var pill = AnchoredGeometry.PillOnly(w, h, r);
            Inner.Clip = Build(pill.Start, pill.Segments, 0);
            AnchoredDrawEars(w, h, r, e);
            return true;
        }

        private static bool Near(double a, double b) => Math.Abs(a - b) < 0.25;

        /// <summary>Turns the pure outline into a frozen <see cref="StreamGeometry"/> (one translator, one shape).</summary>
        private static StreamGeometry Build(Pt start, IReadOnlyList<Seg> segments, double shiftX)
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

        /// <summary>The whole silhouette (pill plus both fillets) behind the pill: the fillets show beside it, seamlessly.</summary>
        private void AnchoredDrawEars(double w, double h, double r, double e)
        {
            if (_anchoredEars == null) return;
            if (e <= 0) { _anchoredEars.Data = null; return; }
            var (start, segs) = AnchoredGeometry.Outline(w, h, r, e);
            _anchoredEars.Width = w + 2 * e;
            _anchoredEars.Height = h;
            _anchoredEars.Data = Build(start, segs, e);          // shifted so the left ear starts at x = 0
            _anchoredEars.Visibility = Pill.Visibility;
        }
    }
}
