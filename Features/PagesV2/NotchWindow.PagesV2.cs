using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WinNotch.Core.Flags;
using WinNotch.Features.PagesV2;

namespace WinNotch
{
    /// <summary>
    /// P74: the thread along the bottom edge of the open notch — the one thing that is running, wherever it is running
    /// from. The small pill has had it for the song; here it carries the same meaning on every page, so the line is a
    /// language and not a detail of one screen. The metrics live in <see cref="PageStyle"/> (pure, tested); the rest of
    /// the new look is in the builders in <c>Ui</c>, which every page is written with.
    /// </summary>
    public partial class NotchWindow
    {
        private bool _pagesOn;
        private Action<string> _pagesFlagHandler;
        private Grid _thread;
        private ScaleTransform _threadFill;

        private static bool NewPagesEnabled() => FeatureFlags.Current?.IsEnabled(PageStyle.FeatureId) ?? false;

        private void StartPagesV2()
        {
            ApplyPagesV2Switch();
            if (_pagesFlagHandler != null) return;
            _pagesFlagHandler = id =>
            {
                if (!string.Equals(id, PageStyle.FeatureId, StringComparison.Ordinal)) return;
                Dispatcher.InvokeAsync(ApplyPagesV2Switch);
            };
            if (FeatureFlags.Current != null) FeatureFlags.Current.Changed += _pagesFlagHandler;
        }

        private void StopPagesV2()
        {
            if (_pagesFlagHandler != null && FeatureFlags.Current != null) FeatureFlags.Current.Changed -= _pagesFlagHandler;
            _pagesFlagHandler = null;
        }

        /// <summary>
        /// The switch moved: build or drop the thread, then rebuild the pages so the new builders are used. Everything
        /// else about the look comes from <c>Ui</c>, so there is nothing else to turn on here.
        /// </summary>
        private void ApplyPagesV2Switch()
        {
            try
            {
                bool on = NewPagesEnabled();
                if (on == _pagesOn && (_thread != null) == on) return;
                _pagesOn = on;
                if (on) BuildThread();
                else if (_thread != null && ExpLayer.Children.Contains(_thread)) { ExpLayer.Children.Remove(_thread); _thread = null; _threadFill = null; }
                // The pages are built through Ui, so they have to be built again — but only once there is something to
                // rebuild: at startup this runs before the first page exists, and ApplySettings builds them anyway.
                if (_home != null) RebuildUi();
                PagesV2Tick();
            }
            catch (Exception ex) { FeatureFlags.Current?.ReportError(PageStyle.FeatureId, ex); }
        }

        private void BuildThread()
        {
            if (_thread != null) return;
            _thread = new Grid
            {
                Height = PageStyle.ThreadThickness, VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(PageStyle.ThreadInset, 0, PageStyle.ThreadInset, 3),
                IsHitTestVisible = false, Visibility = Visibility.Collapsed,
            };
            var track = new Border { CornerRadius = new CornerRadius(PageStyle.ThreadThickness / 2) };
            track.SetResourceReference(Border.BackgroundProperty, "TrackBrush");
            _threadFill = new ScaleTransform(0, 1);
            var fill = new Border
            {
                CornerRadius = new CornerRadius(PageStyle.ThreadThickness / 2),
                RenderTransform = _threadFill, RenderTransformOrigin = new Point(0, 0),
            };
            fill.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
            _thread.Children.Add(track);
            _thread.Children.Add(fill);
            Panel.SetZIndex(_thread, 25);
            ExpLayer.Children.Add(_thread);
        }

        /// <summary>
        /// P74 hook in SecondTick: the thread follows the one thing that is running. No timer of its own — it rides the
        /// tick the notch already has, and only while the notch is open.
        /// </summary>
        private void PagesV2Tick()
        {
            if (!_pagesOn || _thread == null) return;
            try
            {
                double p = _mode == Mode.Expanded ? MediaProgress() : -1;
                if (p < 0) { _thread.Visibility = Visibility.Collapsed; return; }
                _thread.Visibility = Visibility.Visible;
                _threadFill.ScaleX = p;
            }
            catch (Exception ex) { FeatureFlags.Current?.ReportError(PageStyle.FeatureId, ex); }
        }
    }
}
