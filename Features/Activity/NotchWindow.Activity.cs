using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using WinNotch.Core.Activity;
using WinNotch.Core.Flags;
using WinNotch.Features.Activity;

namespace WinNotch
{
    /// <summary>
    /// The notch's side of the Activity Manager (P13, ADR 0006). With the "activity-manager" switch off nothing here
    /// runs: every alert goes straight to the legacy ShowLive, as before. With it on, <see cref="Alert"/> posts the alert
    /// to the manager and the presenter draws the manager's view through the same ShowLive (same UI, size, animation);
    /// the manager, not the live timer, decides when it ends. Only the split pill, the peek and „N noutăți” are new visuals.
    /// </summary>
    public partial class NotchWindow
    {
        private ActivityManager _activity;
        private Action<string> _activityFlagHandler;
        /// <summary>UI-thread copy of the switch (read by <see cref="Alert"/> on every alert).</summary>
        private bool _activityOn;
        private bool _activityRendering, _activityRenderAgain;
        private long _activityRendered = -1;
        /// <summary>What the presenter put on the pill (null: nothing of the manager's is on screen).</summary>
        private ActivityView _activityShown;

        private static bool ActivityEnabled() => FeatureFlags.Current?.IsEnabled(ActivityManager.FeatureId) ?? false;

        /// <summary>Called once at startup (App.StartApp), after the feature flags. UI thread.</summary>
        internal void StartActivities()
        {
            if (_activity != null) return;
            _activity = new ActivityManager(new ThreadPoolActivityScheduler(), new NotchActivityEnvironment(this), new NotchActivityPresenter(this));
            ActivityManager.Current = _activity;
            _activityOn = ActivityEnabled();
            _activityFlagHandler = id =>
            {
                if (id == ActivityManager.FeatureId) Dispatcher.InvokeAsync(ApplyActivitySwitch);       // any thread → UI
            };
            if (FeatureFlags.Current != null) FeatureFlags.Current.Changed += _activityFlagHandler;
            if (_activityOn) App.Log("Activity Manager: pornit.");
        }

        /// <summary>The switch changed: read it again (two changes can arrive in any order).</summary>
        private void ApplyActivitySwitch()
        {
            bool on = ActivityEnabled();
            if (on == _activityOn) return;
            _activityOn = on;
            if (!on)
            {
                _activity?.DismissAll();               // whatever it showed goes; the next alerts take the old path
                _activityShown = null;                 // also when it couldn't be drawn now (notch open): nothing of it is left
                _activityRendered = -1;
            }
            App.Log("Activity Manager: " + (on ? "pornit." : "oprit."));
        }

        private void StopActivities()
        {
            if (_activityFlagHandler != null && FeatureFlags.Current != null) FeatureFlags.Current.Changed -= _activityFlagHandler;
            _activityFlagHandler = null;
            if (_activity == null) return;
            _activity.Presenter = null;
            _activity.DismissAll();                    // stops its timer
            if (ActivityManager.Current == _activity) ActivityManager.Current = null;
            _activity = null;
            _activityOn = false;
        }

        /// <summary>
        /// Every alert of the notch goes through here (UI thread). Switch off: the legacy ShowLive, unchanged. Switch on:
        /// posted to the Activity Manager; true as ShowLive would have said it (for a button alert: it is on screen now).
        /// </summary>
        private bool Alert(string id, UIElement content, double w, double h, int ms, bool important = false)
        {
            // P51b hook (Features/AlertInterrupt): an alert pushed aside a moment ago waits; a no-op with the switch off
            if (!AlertInterruptAllows(id)) return false;
            if (!_activityOn || _activity == null) return ShowLive(content, w, h, ms, important);
            try
            {
                var a = ActivityRouting.FromAlert(id, w, h, ms, important, content);
                var r = _activity.Post(a);         // a visible change is drawn right away (we're on the UI thread)
                return ActivityRouting.Accepted(r, a.Interactive, _activityShown?.Primary?.Key == a.Key && _mode == Mode.Live);
            }
            catch (Exception ex)
            {
                FeatureFlags.Current?.ReportError(ActivityManager.FeatureId, ex);
                return ShowLive(content, w, h, ms, important);        // the alert still shows, the old way
            }
        }

        /// <summary>P13 hook in LiveVolume: the volume alert updated in place stays its whole duration again.</summary>
        private bool ActivityTouch(string key) => _activityOn && _activity != null && _activity.Touch(key);

        /// <summary>
        /// P13 hook, last line of EndLive: an alert the presenter showed was closed by the notch itself (one of its buttons,
        /// a flow's catch). The manager forgets it and shows what's next; a persistent pill is drawn again. No-op with the
        /// switch off (nothing was shown by the presenter) and while the presenter itself ends it.
        /// </summary>
        private void ActivityLiveEnded()
        {
            if (_activityRendering || _activityShown == null || _activity == null) return;
            var shown = _activityShown;
            _activityShown = null;
            _activityRendered = -1;
            if (shown.IsPersistent) RenderActivity();
            else _activity.Dismiss(shown.Primary?.Key);
        }

        /// <summary>P13 hook in MonitorTick: the fullscreen app is gone, persistent activities kept meanwhile are drawn.</summary>
        private void ActivityFullscreenChanged()
        {
            if (!_activityOn || _activity == null || _hidden) return;
            _activityRendered = -1;
            _activity.Refresh();
        }

        /// <summary>P13 hook, last line of Collapse: alerts covered by the open notch are gone; persistent ones come back.</summary>
        private void ActivityNotchClosed()
        {
            if (!_activityOn || _activity == null) return;
            _activityRendered = -1;
            _activity.NotchClosed();
        }

        /// <summary>Draws the manager's view (UI thread). Re-entrant calls are folded into one more pass.</summary>
        private void RenderActivity()
        {
            if (_activity == null) return;
            if (_activityRendering) { _activityRenderAgain = true; return; }
            _activityRendering = true;
            try
            {
                do
                {
                    _activityRenderAgain = false;
                    var v = _activity?.View ?? ActivityView.Empty;
                    if (v.Version == _activityRendered) continue;
                    if (_mode == Mode.Expanded) break;           // drawn after the notch closes (ActivityNotchClosed)
                    _activityRendered = v.Version;
                    RenderView(v);
                } while (_activityRenderAgain);
            }
            catch (Exception ex) { FeatureFlags.Current?.ReportError(ActivityManager.FeatureId, ex); }
            finally { _activityRendering = false; }
            UpdateSmokeStatusIfOn();
        }

        private void RenderView(ActivityView v)
        {
            if (v.Kind == ActivityViewKind.None || v.Primary == null)
            {
                if (_activityShown != null) { _activityShown = null; if (_mode == Mode.Live) EndLive(); }
                return;
            }
            UIElement content;
            double w, h;
            var a = v.Primary;
            switch (v.Kind)
            {
                case ActivityViewKind.Split: content = SplitContent(a, v.Secondary); w = 360; h = 40; break;
                case ActivityViewKind.Group: content = GroupContent(v.GroupCount); w = a.Width; h = a.Height; break;
                case ActivityViewKind.Peek:
                    // the standby pill, a little wider (the live layer is scaled with the notch, the standby pill isn't)
                    content = PeekContent(a);
                    w = (IdleWidth() + 48) / UiScale; h = 34 / UiScale;
                    break;
                default:
                    content = a.Payload as UIElement ?? TextContent(a);
                    w = a.Width > 0 ? a.Width : 320; h = a.Height > 0 ? a.Height : 40;
                    break;
            }
            // over a fullscreen app only High / Critical (a persistent Normal one waits for the next change)
            bool important = a.Priority >= ActivityPriority.High;
            if (!ShowLive(content, w, h, (int)Math.Min(int.MaxValue, Math.Max(100, a.Duration.TotalMilliseconds)), important))
            {
                _activityShown = null;
                if (!v.IsPersistent) _activity?.Dismiss(a.Key);      // can't be shown after all: the queue moves on
                return;
            }
            _liveTimer.Stop();                                        // the manager decides when it ends
            _activityShown = v;
            if (a.Interactive && v.Kind == ActivityViewKind.Single)
            {
                _liveInteractive = true;
                LiveLayer.IsHitTestVisible = true;
                SetClickThrough(false);
            }
        }

        // ------------------------------------------------------------------ the new visuals (theme brushes, radius 16–18)

        private static TextBlock ThemedText(string text, double size, string brush, bool bold = false)
        {
            var t = new TextBlock
            {
                Text = text ?? "", FontSize = size, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
            };
            t.SetResourceReference(TextBlock.ForegroundProperty, brush);
            if (bold) t.FontWeight = FontWeights.SemiBold;
            return t;
        }

        private static FrameworkElement ThemedIcon(string glyph, double size, string brush)
        {
            var icon = new TextBlock { Text = string.IsNullOrEmpty(glyph) ? "" : glyph, FontSize = size, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            icon.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
            icon.SetResourceReference(TextBlock.ForegroundProperty, brush);
            var dot = new Border { Width = 24, Height = 24, CornerRadius = new CornerRadius(12), Child = icon, VerticalAlignment = VerticalAlignment.Center };
            dot.SetResourceReference(Border.BackgroundProperty, "ChipHoverBrush");
            return dot;
        }

        /// <summary>A persistent activity without its own UI: icon and title.</summary>
        private static UIElement TextContent(Activity a)
        {
            var g = Ui.Cols(Ui.Auto, Ui.Star());
            g.Put(ThemedIcon(a.Glyph, 12, "InkBrush"));
            var t = ThemedText(a.Title, 12.5, "InkBrush", true);
            t.Margin = new Thickness(10, 0, 4, 0);
            g.Put(t, 1);
            return g;
        }

        /// <summary>Two persistent activities: left and right halves, a thin divider between them.</summary>
        private static UIElement SplitContent(Activity left, Activity right)
        {
            var g = Ui.Cols(Ui.Star(), Ui.Px(13), Ui.Star());
            g.Put(Half(left));
            var line = new Rectangle { Width = 1, Height = 18, RadiusX = 0.5, RadiusY = 0.5, HorizontalAlignment = HorizontalAlignment.Center };
            line.SetResourceReference(Shape.FillProperty, "TrackBrush");
            g.Put(line, 1);
            g.Put(Half(right), 2);
            return g;

            static UIElement Half(Activity a)
            {
                var h = Ui.Cols(Ui.Auto, Ui.Star());
                h.Put(ThemedIcon(a?.Glyph, 11, "InkBrush"));
                var t = ThemedText(a?.Title, 12, "InkBrush");
                t.Margin = new Thickness(8, 0, 2, 0);
                h.Put(t, 1);
                return h;
            }
        }

        /// <summary>„N noutăți”: a burst of alerts, counted.</summary>
        private static UIElement GroupContent(int count)
        {
            var g = Ui.Cols(Ui.Auto, Ui.Star());
            g.Put(ThemedIcon("", 12, "AccentBrush"));
            var t = ThemedText(ActivityManager.GroupTitle(count), 13, "InkBrush", true);
            t.Margin = new Thickness(10, 0, 4, 0);
            g.Put(t, 1);
            return g;
        }

        /// <summary>A Low activity: a small line in the standby pill's height.</summary>
        private static UIElement PeekContent(Activity a)
        {
            var row = Ui.H(7, ThemedText("•", 12, "AccentBrush", true), ThemedText(a.Title, 11.5, "MutedBrush"));
            row.HorizontalAlignment = HorizontalAlignment.Center;
            return row;
        }

        // ------------------------------------------------------------------ smoke tests (only with --smoke)

        private void UpdateSmokeStatusIfOn()
        {
            if (Features.Smoke.SmokeMode.On) UpdateSmokeStatus();
        }

        /// <summary>What the presenter shows, for the smoke status: split, group count, peek.</summary>
        private (int Split, int Group, int Peek) ActivitySmokeFields()
        {
            var v = _mode == Mode.Live ? _activityShown : null;
            if (v == null) return (0, 0, 0);
            return (v.Kind == ActivityViewKind.Split ? 1 : 0, v.Kind == ActivityViewKind.Group ? v.GroupCount : 0, v.Kind == ActivityViewKind.Peek ? 1 : 0);
        }

        // ------------------------------------------------------------------ the manager's view of the notch

        /// <summary>Plain reads of the notch's state (from any thread; the presenter checks again on the UI thread).</summary>
        private sealed class NotchActivityEnvironment : IActivityEnvironment
        {
            private readonly NotchWindow _n;
            public NotchActivityEnvironment(NotchWindow n) { _n = n; }
            public bool NotchOpen => _n._mode == Mode.Expanded;
            public bool Fullscreen => _n._hidden;
        }

        /// <summary>Draws on the UI thread: right away when already there (a button alert is on screen when Alert returns), else queued.</summary>
        private sealed class NotchActivityPresenter : IActivityPresenter
        {
            private readonly NotchWindow _n;
            public NotchActivityPresenter(NotchWindow n) { _n = n; }

            public void Invalidate()
            {
                var d = _n.Dispatcher;
                if (d.CheckAccess()) _n.RenderActivity();
                else d.InvokeAsync(_n.RenderActivity, DispatcherPriority.Normal);
            }
        }
    }

    /// <summary>"activity.dismiss-all" in the app: the notch's manager.</summary>
    internal sealed class NotchActivityHost : IActivityHost
    {
        public int DismissAll() => ActivityManager.Current?.DismissAll() ?? 0;
    }
}
