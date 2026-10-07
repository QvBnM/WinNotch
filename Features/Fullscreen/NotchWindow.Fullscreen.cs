using System;
using System.Collections.Generic;
using System.Windows;
using WinNotch.Core.Flags;

namespace WinNotch
{
    using WinNotch.Features.Fullscreen;

    /// <summary>
    /// P53: while an app fills the screen the notch is gone, and only something important comes down for a moment.
    /// Everything that decides is in <see cref="FullscreenRules"/> (pure, tested); this file only wires it to the
    /// window. With the switch off, every method here is a no-op and the notch behaves exactly as before.
    /// </summary>
    public partial class NotchWindow
    {
        private readonly DeferredAlerts _deferred = new DeferredAlerts();
        private readonly Dictionary<string, Action> _deferredShow = new Dictionary<string, Action>(StringComparer.Ordinal);
        private bool _fsReleasing;

        private static bool FullscreenOn => FeatureFlags.Current?.IsEnabled(FullscreenRules.FeatureId) ?? true;

        /// <summary>
        /// The gate of every alert while the notch is hidden: important ones come down briefly, a few are kept for a
        /// quieter moment, the rest are dropped. Returns false when the caller must not show anything now.
        /// </summary>
        private bool FullscreenAllows(string id, UIElement content, double w, double h, ref int ms, bool important)
        {
            if (!FullscreenOn || !_hidden || _fsReleasing) return true;
            switch (FullscreenRules.ForAlert(id, important))
            {
                case HiddenAlert.Show:
                    ms = FullscreenRules.DurationWhileHidden(id, ms);
                    return true;
                case HiddenAlert.Defer:
                    _deferred.Note(id, DateTime.Now);
                    int keep = ms;
                    _deferredShow[id] = () => Alert(id, content, w, h, keep, important);
                    return false;
                default:
                    return false;
            }
        }

        /// <summary>Called from MonitorTick (every 500 ms): counts the delay and shows what was kept, once.</summary>
        private void FullscreenTick()
        {
            if (!FullscreenOn) { FullscreenForget(); return; }      // switched off while something waited: nothing is kept
            if (_hidden) { _deferred.Hidden(); return; }
            var now = DateTime.Now;
            _deferred.Freed(now);
            if (_deferred.Count == 0 || _mode != Mode.Idle) return;
            var due = _deferred.Release(now);
            if (due.Count == 0) return;
            _fsReleasing = true;
            try
            {
                foreach (var id in due)
                {
                    if (!_deferredShow.TryGetValue(id, out var show)) continue;
                    _deferredShow.Remove(id);
                    try { show(); } catch (Exception ex) { FeatureFlags.Current?.ReportError(FullscreenRules.FeatureId, ex); }
                    break;                  // one alert at a time: the rest are forgotten, no queue builds up
                }
            }
            finally { _fsReleasing = false; _deferredShow.Clear(); }
        }

        /// <summary>The notch was opened by hand (shortcut, Command Bar, tray): nothing waits any more.</summary>
        private void FullscreenForget()
        {
            _deferred.Clear();
            _deferredShow.Clear();
        }

        /// <summary>Does this request open the notch while it is hidden? Hover and drag do not; the user's own shortcuts do.</summary>
        private bool FullscreenOpens(HiddenTrigger trigger) => !FullscreenOn || !_hidden || FullscreenRules.Opens(trigger);
    }
}
