using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using WinNotch.Core.Flags;
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
                        _anchoredEars.SetBinding(OpacityProperty, new System.Windows.Data.Binding("Opacity") { Source = Pill });
                        root.Children.Insert(Math.Max(0, root.Children.IndexOf(Pill)), _anchoredEars);
                    }
                }
                else
                {
                    Pill.Effect = _anchoredOldShadow;
                    if (_anchoredEars != null && Pill.Parent is Panel root) { root.Children.Remove(_anchoredEars); _anchoredEars = null; }
                }
                ApplyRadius();
                ApplyMode();
            }
            catch (Exception ex) { FeatureFlags.Current?.ReportError(AnchoredGeometry.FeatureId, ex); }
        }

        /// <summary>The top margin of the pill: 0 while anchored (it touches the bezel), the old value otherwise.</summary>
        private double AnchoredTop(double top) => _anchoredOn ? AnchoredGeometry.TopMargin : top;

        /// <summary>The bottom radius while anchored (the user's setting, within 12–28).</summary>
        private double AnchoredRadius(double r) => _anchoredOn ? AnchoredGeometry.Radius(S.CornerRadius) * UiScale : r;

        /// <summary>Standby width while anchored: 240–520. The small form keeps its own width.</summary>
        private double AnchoredIdleWidth(double w) => AnchoredGeometry.IdleWidth(w, _anchoredOn);

        /// <summary>
        /// Hook in ApplyRadius / UpdateClip: the pill is rounded only at the bottom and the content is clipped with the
        /// same shape, so a coloured cover inside it is cut correctly. The ears are redrawn here too (not per frame).
        /// </summary>
        private bool AnchoredShape()
        {
            if (!_anchoredOn) return false;
            double w = Pill.ActualWidth, h = Pill.ActualHeight;
            double r = Math.Min(AnchoredGeometry.Radius(S.CornerRadius) * UiScale, Math.Max(0, Math.Min(w / 2, h)));
            Pill.CornerRadius = new CornerRadius(0, 0, r, r);
            if (w <= 0 || h <= 0) return true;
            var clip = new StreamGeometry();
            using (var c = clip.Open())
            {
                c.BeginFigure(new Point(0, 0), true, true);
                c.LineTo(new Point(0, h - r), false, false);
                if (r > 0) c.ArcTo(new Point(r, h), new Size(r, r), 0, false, SweepDirection.Counterclockwise, false, false);
                c.LineTo(new Point(w - r, h), false, false);
                if (r > 0) c.ArcTo(new Point(w, h - r), new Size(r, r), 0, false, SweepDirection.Counterclockwise, false, false);
                c.LineTo(new Point(w, 0), false, false);
            }
            clip.Freeze();
            Inner.Clip = clip;
            AnchoredDrawEars(w, h, r);
            return true;
        }

        private void AnchoredDrawEars(double w, double h, double r)
        {
            if (_anchoredEars == null) return;
            double e = AnchoredGeometry.Ear(S.CornerRadius, w, Width) * UiScale;
            if (e <= 0) { _anchoredEars.Data = null; return; }
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                // left fillet: from the frame down to the pill's side, curving inwards
                c.BeginFigure(new Point(0, 0), true, true);
                c.ArcTo(new Point(e, e), new Size(e, e), 0, false, SweepDirection.Clockwise, false, false);
                c.LineTo(new Point(e, 0), false, false);
                // right fillet, mirrored
                c.BeginFigure(new Point(w + 2 * e, 0), true, true);
                c.ArcTo(new Point(w + e, e), new Size(e, e), 0, false, SweepDirection.Counterclockwise, false, false);
                c.LineTo(new Point(w + e, 0), false, false);
            }
            g.Freeze();
            _anchoredEars.Width = w + 2 * e;
            _anchoredEars.Height = e;
            _anchoredEars.Margin = new Thickness(0);
            _anchoredEars.Data = g;
            _anchoredEars.Visibility = Pill.Visibility;
        }
    }
}
