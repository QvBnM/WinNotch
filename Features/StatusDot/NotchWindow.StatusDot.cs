using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using WinNotch.Core.Context;
using WinNotch.Core.Flags;
using WinNotch.Features.StatusDot;

namespace WinNotch
{
    /// <summary>
    /// P73: the small disc beside the pill — what is going on right now, as one glyph. Everything it shows is read from
    /// the context engine's snapshot (never from the microphone or the media itself), and what to show is decided by
    /// <see cref="StatusDotRules"/> (pure, tested). Here we only draw it and move it with the pill.
    /// </summary>
    public partial class NotchWindow
    {
        private bool _dotOn, _dotWorking;
        private double _dotWorkProgress = -1;
        private DotKind _dotKind;
        private Action<string> _dotFlagHandler;
        private EventHandler<ContextChangedEventArgs> _dotContextHandler;

        /// <summary>The disc, its glyph and its ring, in the window's root grid. Null while the switch is off.</summary>
        private Grid _dotHost;
        /// <summary>
        /// Inside the host: everything that is drawn. Its opacity is the dot's own show/hide, while the host's opacity
        /// follows the pill's — WPF multiplies the two down the visual tree, so the dot fades exactly as much as the
        /// pill does and the two animations never fight over one property.
        /// </summary>
        private Grid _dotFade;
        private Border _dotDisc, _dotDivider;
        private TextBlock _dotGlyph;
        private Path _dotRing;
        private TranslateTransform _dotShift;

        private static bool StatusDotEnabled() => FeatureFlags.Current?.IsEnabled(StatusDotRules.FeatureId) ?? false;

        private void StartStatusDot()
        {
            ApplyStatusDotSwitch();
            if (_dotFlagHandler != null) return;
            _dotFlagHandler = id =>
            {
                if (!string.Equals(id, StatusDotRules.FeatureId, StringComparison.Ordinal)) return;
                Dispatcher.InvokeAsync(ApplyStatusDotSwitch);
            };
            if (FeatureFlags.Current != null) FeatureFlags.Current.Changed += _dotFlagHandler;
        }

        private void StopStatusDot()
        {
            if (_dotFlagHandler != null && FeatureFlags.Current != null) FeatureFlags.Current.Changed -= _dotFlagHandler;
            _dotFlagHandler = null;
            UnsubscribeStatusDotContext();
        }

        private void UnsubscribeStatusDotContext()
        {
            if (_dotContextHandler != null && ContextEngine.Current != null) ContextEngine.Current.Changed -= _dotContextHandler;
            _dotContextHandler = null;
        }

        /// <summary>
        /// The switch went on or off: build or drop the disc, and subscribe to the context only while it is on. With
        /// the context engine off the snapshot is empty, so nothing ever applies and no dot is shown — by itself.
        /// </summary>
        private void ApplyStatusDotSwitch()
        {
            try
            {
                bool on = StatusDotEnabled();
                if (on == _dotOn && (_dotHost != null) == on) return;
                _dotOn = on;
                if (on)
                {
                    BuildStatusDot();
                    if (_dotContextHandler == null && ContextEngine.Current != null)
                    {
                        _dotContextHandler = (s, e) =>
                        {
                            // the engine raises this on its own timer thread: the UI is only ever touched on the Dispatcher
                            if (!e.Has(ContextField.Microphone) && !e.Has(ContextField.Camera) &&
                                !e.Has(ContextField.Media) && !e.Has(ContextField.Power)) return;
                            Dispatcher.InvokeAsync(UpdateStatusDot);
                        };
                        ContextEngine.Current.Changed += _dotContextHandler;
                    }
                }
                else
                {
                    UnsubscribeStatusDotContext();
                    if (_dotHost != null && Pill.Parent is Panel root) root.Children.Remove(_dotHost);
                    _dotHost = null; _dotFade = null; _dotDisc = null; _dotDivider = null; _dotGlyph = null; _dotRing = null; _dotShift = null;
                    _dotKind = DotKind.None;
                }
                UpdateStatusDot();
                ApplyMode();
            }
            catch (Exception ex) { FeatureFlags.Current?.ReportError(StatusDotRules.FeatureId, ex); }
        }

        private void BuildStatusDot()
        {
            if (_dotHost != null || !(Pill.Parent is Panel root)) return;
            _dotShift = new TranslateTransform();
            // A capsule, like the pill: same brush, same corner rule (radius is half the height), same shadow. Fused
            // into the drawer it paints nothing at all — the pill behind it is the surface.
            _dotDisc = new Border();
            _dotDisc.SetResourceReference(Border.BackgroundProperty, "NotchBrush");
            _dotDivider = new Border { Width = 1, Height = 14, HorizontalAlignment = HorizontalAlignment.Left,
                                       VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
            _dotDivider.SetResourceReference(Border.BackgroundProperty, "TrackBrush");
            _dotRing = new Path { StrokeThickness = StatusDotRules.RingThickness, StrokeStartLineCap = PenLineCap.Round,
                                  StrokeEndLineCap = PenLineCap.Round, Visibility = Visibility.Collapsed };
            _dotGlyph = new TextBlock
            {
                Style = (Style)TryFindResource("Icon"),
                FontSize = StatusDotRules.GlyphSize,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
            _dotFade = new Grid { Opacity = 0 };
            _dotFade.Children.Add(_dotRing);
            _dotFade.Children.Add(_dotDisc);
            _dotFade.Children.Add(_dotDivider);
            _dotFade.Children.Add(_dotGlyph);
            _dotHost = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
                IsHitTestVisible = false,
                // the same shift as the pill, so it leaves the screen with it over a fullscreen app, plus its own offset
                RenderTransform = new TransformGroup { Children = { PillShift, _dotShift } },
                Effect = Pill.Effect,
            };
            _dotHost.Children.Add(_dotFade);
            _dotHost.SetBinding(VisibilityProperty, new System.Windows.Data.Binding("Visibility") { Source = Pill });
            // Reported by the author (0.6.30): walking towards the notch left the music glyph alone on the wallpaper.
            // The pill fades on hover (0.18) and vanishes entirely while it gets out of the way of a maximized window
            // (Fade(Pill, 0, 90)); the dot had no part in that, so it stayed at full strength beside a pill nobody
            // could see. The host simply wears the pill's opacity, so every one of those fades carries it along —
            // without naming any of them here, and without a second copy of the rule to keep in step.
            _dotHost.SetBinding(OpacityProperty, new System.Windows.Data.Binding("Opacity") { Source = Pill });
            root.Children.Add(_dotHost);
        }

        /// <summary>
        /// What the dot shows now. Called when the context changes, when the switch moves and from ApplyMode — never on
        /// a timer of its own.
        /// </summary>
        private void UpdateStatusDot()
        {
            if (!_dotOn || _dotHost == null) return;
            try
            {
                var snap = ContextEngine.Current?.Snapshot ?? ContextSnapshot.Empty;
                var kind = StatusDotRules.Pick(snap, _dotWorking);
                bool visible = StatusDotRules.Visible(kind, _mode == Mode.Expanded, _mode == Mode.Live, false);
                if (kind != _dotKind)
                {
                    _dotKind = kind;
                    if (kind != DotKind.None)
                    {
                        _dotGlyph.Text = StatusDotRules.Glyph(kind);
                        _dotGlyph.SetResourceReference(TextBlock.ForegroundProperty, StatusDotRules.BrushKey(kind));
                        _dotRing.SetResourceReference(Shape.StrokeProperty, StatusDotRules.BrushKey(kind));
                        _dotHost.ToolTip = StatusDotRules.HoverText(snap, _dotWorking);
                    }
                }
                DrawStatusDotRing(StatusDotRules.Ring(kind, snap, MediaProgress(), _dotWorkProgress));
                FadeLayer(_dotFade, visible, 140);     // the host carries the pill's opacity; this is the dot's own
            }
            catch (Exception ex) { FeatureFlags.Current?.ReportError(StatusDotRules.FeatureId, ex); }
        }

        /// <summary>
        /// How far the song is, as a fraction — the same reading the small pill's progress line uses, not a second
        /// source. Negative when there is no session, nothing playing or no duration to divide by.
        /// </summary>
        private double MediaProgress()
        {
            var mi = Now?.Info;
            if (mi == null || !mi.HasSession || !mi.Playing || mi.Duration <= TimeSpan.Zero) return -1;
            return Math.Clamp(mi.LivePosition.TotalSeconds / mi.Duration.TotalSeconds, 0, 1);
        }

        /// <summary>The ring around the disc: one arc from the top, clockwise. A negative fraction means no ring.</summary>
        private void DrawStatusDotRing(double fraction)
        {
            if (_dotRing == null) return;
            // Fused into the drawer the thread along the pill's bottom edge already carries the progress: a ring over
            // it would be the same thing said twice.
            if (fraction < 0 || fraction > 1 || StatusDotRules.Fused(Anchored))
            {
                _dotRing.Visibility = Visibility.Collapsed;
                return;
            }
            double scale = Math.Max(0.01, UiScale);
            double r = (_dotDisc != null && _dotDisc.Width > 0 ? _dotDisc.Width / 2 : StatusDotRules.Diameter / 2 * scale)
                       + StatusDotRules.RingGap * scale;
            var (x, y, large) = StatusDotRules.RingEnd(fraction, r);
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(r, 0), false, false);
                c.ArcTo(new Point(x, y), new Size(r, r), 0, large, SweepDirection.Clockwise, true, false);
            }
            g.Freeze();
            _dotRing.Data = g;
            _dotRing.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// P73 hook in ApplyMode: the dot sits to the right of the pill, on its middle line, and moves with the same
        /// animation — so it never lags behind while the pill grows or shrinks.
        /// </summary>
        private void StatusDotLayout(double pillWidth, double pillHeight, double top, TimeSpan dur)
        {
            if (!_dotOn || _dotHost == null) return;
            try
            {
                double scale = Math.Max(0.01, UiScale);
                // The disc borrows the pill's own size: when the pill shrinks to the small form, so does it.
                double d = StatusDotRules.Size(pillHeight / scale) * scale;
                double gap = StatusDotRules.Gap * scale;
                double x, y, box;

                if (StatusDotRules.Fused(Anchored))
                {
                    // Stuck to the bezel there is no room for a satellite: a circle floating beside a shape that grows
                    // out of the edge is the only thing in the scene not touching it, and the strip of wallpaper
                    // between them reads as a second notch. The glyph moves inside the pill's right end instead, after
                    // a hairline — the pill itself is the surface, so the disc paints nothing and the ring is gone.
                    box = d + 12 * scale;
                    _dotDisc.Visibility = Visibility.Collapsed;
                    _dotDivider.Visibility = Visibility.Visible;
                    _dotDivider.Height = Math.Max(8, pillHeight - 18 * scale);
                    _dotHost.Effect = null;
                    x = pillWidth / 2 - StatusDotRules.FusedInset * scale - box / 2;
                }
                else
                {
                    box = d + 2 * (StatusDotRules.RingGap + StatusDotRules.RingThickness) * scale;
                    _dotDisc.Visibility = Visibility.Visible;
                    _dotDivider.Visibility = Visibility.Collapsed;
                    _dotHost.Effect = Pill.Effect;
                    // the same corner rule as the pill: the radius is half the height, so it is a capsule, not a box
                    _dotDisc.CornerRadius = new CornerRadius(d / 2);
                    x = pillWidth / 2 + gap + box / 2;
                }

                _dotHost.Width = box; _dotHost.Height = box;
                _dotDisc.Width = d; _dotDisc.Height = d;
                _dotDisc.HorizontalAlignment = HorizontalAlignment.Center;
                _dotDisc.VerticalAlignment = VerticalAlignment.Center;
                _dotGlyph.FontSize = Math.Max(9, StatusDotRules.GlyphSize * (d / Math.Max(1, StatusDotRules.Diameter * scale)) * scale);
                y = top + (pillHeight - box) / 2;
                _dotShift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(x, dur) { EasingFunction = Spring });
                _dotHost.BeginAnimation(MarginProperty,
                    new ThicknessAnimation(new Thickness(0, Math.Max(0, y), 0, 0), TimeSpan.FromMilliseconds(300)));
                UpdateStatusDot();
            }
            catch (Exception ex) { FeatureFlags.Current?.ReportError(StatusDotRules.FeatureId, ex); }
        }

        /// <summary>
        /// A task of the user's own is running (an update, a speed test, a long action). Not wired to anything yet:
        /// the first caller will be the updater, when P72 moves its progress onto the thread.
        /// </summary>
        internal void StatusDotWork(bool running, double progress)
        {
            _dotWorking = running;
            _dotWorkProgress = progress;
            if (_dotOn) Dispatcher.InvokeAsync(UpdateStatusDot);
        }
    }
}
