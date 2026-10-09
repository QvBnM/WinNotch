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
        /// <summary>
        /// The whole anchored silhouette (pill plus both fillets), one <see cref="Path"/> in the window's root grid.
        /// It is the <b>only</b> thing painted while anchored: the pill's own background is switched off, otherwise the
        /// two surfaces stack and the body comes out a shade darker than the fillets (plainly visible on a light theme)
        /// with a seam where the bottom corners of the two shapes meet. Null while the switch is off.
        /// </summary>
        private Path _anchoredShape;
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
                if (on == _anchoredOn && (_anchoredShape != null) == on) return;
                _anchoredOn = on;
                if (on)
                {
                    _anchoredOldShadow ??= Pill.Effect;
                    // The pill paints nothing while anchored, so its shadow would fall from its content (the text), not
                    // from the silhouette: the shadow moves to the shape, which is the silhouette.
                    Pill.Effect = null;
                    Pill.Background = Brushes.Transparent;
                    if (_anchoredShape == null && Pill.Parent is Panel root)
                    {
                        _anchoredShape = new Path
                        {
                            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
                            IsHitTestVisible = false,
                            // not snapped to the pixel grid: the shape must stay centred on the same axis as the pill,
                            // and a rounded width would move its own centre half a pixel off the pill's
                            UseLayoutRounding = false, SnapsToDevicePixels = false,
                            Effect = new DropShadowEffect
                            {
                                Direction = AnchoredGeometry.ShadowDirection, ShadowDepth = AnchoredGeometry.ShadowDepth,
                                BlurRadius = AnchoredGeometry.ShadowBlur, Opacity = AnchoredGeometry.ShadowOpacity,
                                Color = Colors.Black, RenderingBias = RenderingBias.Performance,
                            },
                        };
                        _anchoredShape.SetResourceReference(Shape.FillProperty, "NotchBrush");
                        // the shape follows the pill: same shift (hidden over a fullscreen app), same opacity (hover,
                        // dodge), same top margin (animated from 8 to 0 when the switch goes on) and same visibility
                        _anchoredShape.RenderTransform = PillShift;
                        _anchoredShape.SetBinding(OpacityProperty, new System.Windows.Data.Binding("Opacity") { Source = Pill });
                        _anchoredShape.SetBinding(MarginProperty, new System.Windows.Data.Binding("Margin") { Source = Pill });
                        _anchoredShape.SetBinding(VisibilityProperty, new System.Windows.Data.Binding("Visibility") { Source = Pill });
                        root.Children.Insert(Math.Max(0, root.Children.IndexOf(Pill)), _anchoredShape);
                    }
                }
                else
                {
                    Pill.Effect = _anchoredOldShadow;
                    Pill.SetResourceReference(Border.BackgroundProperty, "NotchBrush");
                    if (_anchoredShape != null && Pill.Parent is Panel root) { root.Children.Remove(_anchoredShape); _anchoredShape = null; }
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
        private bool ApplyAnchoredShape()
        {
            if (!_anchoredOn) return false;
            double w = Pill.ActualWidth, h = Pill.ActualHeight;
            if (w <= 0 || h <= 0) return true;
            // Radius is the animated value (already scaled), so the shape follows the animation instead of jumping.
            double scale = Math.Max(0.01, UiScale);
            double r = Math.Min(Radius, Math.Min(w / 2, h / 2));
            double e = AnchoredGeometry.Ear(Radius / scale, w / scale, Width / scale, h / scale) * scale;
            if (Near(w, _shapeW) && Near(h, _shapeH) && Near(r, _shapeR) && Near(e, _shapeE)) return true;
            _shapeW = w; _shapeH = h; _shapeR = r; _shapeE = e;

            Pill.CornerRadius = new CornerRadius(0, 0, r, r);
            var pill = AnchoredGeometry.PillOnly(w, h, r);
            Inner.Clip = AnchoredShape.Build(pill.Start, pill.Segments, 0);     // the same outline that is painted
            AnchoredDrawShape(w, h, r, e);
            return true;
        }

        private static bool Near(double a, double b) => Math.Abs(a - b) < 0.25;

        /// <summary>
        /// The whole silhouette — one painted shape, the pill included. Without ears (a window too narrow for them) it
        /// is the pill's own outline, never nothing: the pill itself no longer paints a background while anchored.
        /// </summary>
        private void AnchoredDrawShape(double w, double h, double r, double e)
        {
            if (_anchoredShape == null) return;
            _anchoredShape.Width = w + 2 * Math.Max(0, e);
            _anchoredShape.Height = h;
            _anchoredShape.Data = AnchoredShape.Silhouette(w, h, r, e);
        }
    }
}
