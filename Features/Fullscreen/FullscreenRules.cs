using System;
using System.Collections.Generic;
using System.Linq;
using WinNotch.Features.Activity;

namespace WinNotch.Features.Fullscreen
{
    /// <summary>A screen rectangle, in physical pixels. Same meaning as Native.RECT, without the interop.</summary>
    public readonly struct Box
    {
        public Box(int left, int top, int right, int bottom) { Left = left; Top = top; Right = right; Bottom = bottom; }

        /// <summary>From a position and a size, the way a log line prints a window ("[-2560,0 2560x1440]").</summary>
        public static Box At(int left, int top, int width, int height) => new Box(left, top, left + width, top + height);

        public int Left { get; }
        public int Top { get; }
        public int Right { get; }
        public int Bottom { get; }
        public int Width => Right - Left;
        public int Height => Bottom - Top;
        public bool Empty => Width <= 0 || Height <= 0;
        public bool Same(Box o) => Left == o.Left && Top == o.Top && Right == o.Right && Bottom == o.Bottom;
        public override string ToString() => "[" + Left + "," + Top + " " + Width + "x" + Height + "]";
    }

    /// <summary>What the top big window on a monitor does to the notch.</summary>
    public enum MonitorUse
    {
        /// <summary>Nothing covers the notch: normal standby.</summary>
        Free,
        /// <summary>A maximized window: its tab strip / title bar sits under the notch, so the pill goes small.</summary>
        Maximized,
        /// <summary>A game, a video or a presentation fills the whole monitor: the notch hides.</summary>
        Busy,
    }

    /// <summary>Why the notch would open while it is hidden over a fullscreen app.</summary>
    public enum HiddenTrigger
    {
        /// <summary>The mouse rested on the invisible pill.</summary>
        Hover,
        /// <summary>Files dragged over the invisible pill.</summary>
        Drag,
        /// <summary>Win+Alt+N.</summary>
        Shortcut,
        /// <summary>The Command Bar shortcut.</summary>
        CommandBar,
        /// <summary>A menu item in the tray.</summary>
        Tray,
    }

    /// <summary>What an alert may do while the notch is hidden over a fullscreen app.</summary>
    public enum HiddenAlert
    {
        /// <summary>Shown now, short and without a sound (battery, temperature, memory, the result of a tool the user started).</summary>
        Show,
        /// <summary>Kept and shown once, a couple of seconds after the fullscreen app is gone (eye break, update, new track).</summary>
        Defer,
        /// <summary>Not shown and forgotten (volume, a device connected, the old extension).</summary>
        Drop,
    }

    /// <summary>
    /// P53: over an app that fills the screen the notch disappears completely and only something important brings it
    /// back, briefly. Pure rules (no WPF, no interop): the classification of a window, what gets through while hidden,
    /// and what opens the notch anyway. <see cref="Services.MonitorService"/> and
    /// <c>Features/Fullscreen/NotchWindow.Fullscreen.cs</c> only call into here.
    /// </summary>
    public static class FullscreenRules
    {
        /// <summary>Same id as the entry in FeatureCatalog (the tests check they match).</summary>
        public const string FeatureId = "fullscreen-hide";

        /// <summary>A window within this many pixels of the work area counts as filling it (custom title bars, "fake" maximize).</summary>
        public const int FillTolerance = 12;

        /// <summary>An alert that gets through over a fullscreen app stays at most this long, with no sound and no buttons to press.</summary>
        public const int PeekMs = 2500;

        /// <summary>Deferred alerts are shown this long after the fullscreen app is gone.</summary>
        public static readonly TimeSpan ReleaseDelay = TimeSpan.FromSeconds(2);

        /// <summary>Windows that never decide a monitor's state: the desktop, the shell, overlays, helper windows.</summary>
        private static readonly HashSet<string> IgnoredClassNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "NotifyIconOverflowWindow",
            "XamlExplorerHostIslandWindow", "TopLevelWindowForOverflowXamlIsland", "Windows.UI.Core.CoreWindow",
            "ForegroundStaging", "MultitaskingViewFrame", "TaskListThumbnailWnd", "CEF-OSC-WIDGET",
            "Windows.UI.Composition.DesktopWindowContentBridge", "ThumbnailDeviceHelperWnd", "EdgeUiInputTopWndClass",
            "ApplicationManager_ImmersiveShellWindow", "Internet Explorer_Hidden", "IME", "MSCTFIME UI"
        };

        /// <summary>A window the scan skips: our own, the desktop, the shell, an overlay, a titleless or empty one.</summary>
        public static bool Ignored(string className, bool own, bool hasTitle, bool cloaked, Box rect) =>
            own || cloaked || !hasTitle || rect.Empty || className == null || IgnoredClassNames.Contains(className);

        /// <summary>
        /// What the window does to the notch, or null when it is too small to decide the monitor's state (keep looking
        /// below it). <paramref name="strict"/> is the feature switch: with it off, "busy" is decided the old way
        /// (covers the monitor and <c>IsZoomed</c> is false), which a browser in fullscreen fools because it keeps
        /// WS_MAXIMIZE — exactly the reported bug.
        /// </summary>
        public static MonitorUse? Classify(Box win, Box bounds, Box work, bool zoomed, bool caption, bool thickFrame, bool strict)
        {
            if (win.Empty || bounds.Empty) return null;

            bool covers = win.Left <= bounds.Left && win.Top <= bounds.Top && win.Right >= bounds.Right && win.Bottom >= bounds.Bottom;
            bool fillsWork = Math.Abs(win.Left - work.Left) <= FillTolerance && Math.Abs(win.Top - work.Top) <= FillTolerance &&
                             Math.Abs(win.Right - work.Right) <= FillTolerance && Math.Abs(win.Bottom - work.Bottom) <= FillTolerance;
            bool maximized = zoomed || (fillsWork && !covers);

            // Small windows (a calculator, sticky notes) don't decide the monitor's state.
            double area = (double)Math.Max(0, Math.Min(win.Right, bounds.Right) - Math.Max(win.Left, bounds.Left)) *
                          Math.Max(0, Math.Min(win.Bottom, bounds.Bottom) - Math.Max(win.Top, bounds.Top));
            if (!covers && !maximized && area < 0.5 * (double)bounds.Width * bounds.Height) return null;

            bool busy;
            if (strict)
            {
                // Geometry and style, not IsZoomed: the window covers the whole monitor AND has no frame of its own,
                // or it covers the taskbar's strip too (a normal maximized window leaves the work area's edge free).
                // WS_CAPTION is WS_BORDER | WS_DLGFRAME: any of those bits means the window still draws a frame of its own.
                bool frameless = !caption && !thickFrame;
                // A normal maximized window leaves the taskbar's strip free, so covering it is the second signal.
                // Known limit: on a monitor without a taskbar (or with an auto-hiding one) work == bounds, so only
                // "frameless" is left — a borderless window that is merely maximized is read as busy there, and an app
                // that goes fullscreen while keeping its frame is not. Both need a signal Windows does not give us.
                bool coversTaskbar = !work.Same(bounds);
                busy = covers && (frameless || coversTaskbar);
            }
            else busy = covers && !zoomed;

            return busy ? MonitorUse.Busy : maximized ? MonitorUse.Maximized : MonitorUse.Free;
        }

        /// <summary>
        /// Is the window maximized (or filling the work area) as well? A busy window can be both — a browser in
        /// fullscreen keeps WS_MAXIMIZE — and the small-pill rules still need to know ("always visible" setting).
        /// </summary>
        public static bool LooksMaximized(Box win, Box bounds, Box work, bool zoomed)
        {
            if (win.Empty || bounds.Empty) return false;
            bool covers = win.Left <= bounds.Left && win.Top <= bounds.Top && win.Right >= bounds.Right && win.Bottom >= bounds.Bottom;
            bool fillsWork = Math.Abs(win.Left - work.Left) <= FillTolerance && Math.Abs(win.Top - work.Top) <= FillTolerance &&
                             Math.Abs(win.Right - work.Right) <= FillTolerance && Math.Abs(win.Bottom - work.Bottom) <= FillTolerance;
            return zoomed || (fillsWork && !covers);
        }

        /// <summary>Alerts kept for later instead of being shown over a fullscreen app (a film is not interrupted).</summary>
        private static readonly HashSet<string> DeferredIds = new HashSet<string>(StringComparer.Ordinal)
        {
            LegacyAlerts.EyeBreak, LegacyAlerts.UpdateOffer, LegacyAlerts.WhatsNew, LegacyAlerts.Track,
        };

        /// <summary>
        /// What happens to an alert while the notch is hidden. The decision follows the alert table (important = battery,
        /// temperature, memory, the result of a tool the user started) minus the ones that wait for a quieter moment.
        /// </summary>
        public static HiddenAlert ForAlert(string id, bool important)
        {
            var known = id == null ? null : LegacyAlerts.Find(id);
            // An alert with buttons cannot be kept and replayed: its caller wires the buttons after it is shown. Those
            // have a gate of their own (eye break, update offer, what's new), which offers them again once we are back.
            if (id != null && DeferredIds.Contains(id)) return known?.Interactive == true ? HiddenAlert.Drop : HiddenAlert.Defer;
            return (known?.Important ?? important) ? HiddenAlert.Show : HiddenAlert.Drop;
        }

        /// <summary>
        /// How long a passing alert stays while hidden: short, unless it has buttons the user is meant to press or it is
        /// one step of a flow (a spinner replaced by its result: OCR, memory, download) — those keep their own duration.
        /// </summary>
        public static int DurationWhileHidden(string id, int ms)
        {
            var known = id == null ? null : LegacyAlerts.Find(id);
            bool keep = known != null && (known.Interactive || !string.Equals(known.Key, known.Id, StringComparison.Ordinal));
            return keep ? ms : Math.Min(ms, PeekMs);
        }

        /// <summary>While the notch is hidden only an explicit request of the user opens it; hover and drag do not.</summary>
        public static bool Opens(HiddenTrigger trigger) =>
            trigger == HiddenTrigger.Shortcut || trigger == HiddenTrigger.CommandBar || trigger == HiddenTrigger.Tray;
    }

    /// <summary>
    /// The alerts kept while the notch was hidden: at most one of each kind (the most recent), shown once,
    /// <see cref="FullscreenRules.ReleaseDelay"/> after the fullscreen app is gone. Pure: no timers, no WPF.
    /// </summary>
    public sealed class DeferredAlerts
    {
        /// <summary>More kinds than this and the oldest is forgotten: the queue never grows.</summary>
        public const int MaxKinds = 8;

        private readonly List<string> _order = new List<string>();
        private readonly Dictionary<string, DateTime> _at = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        private DateTime? _freeSince;

        public int Count => _order.Count;

        /// <summary>Keeps one alert of this kind for later (a newer one of the same kind replaces it).</summary>
        public void Note(string id, DateTime now)
        {
            if (string.IsNullOrEmpty(id)) return;
            if (!_at.ContainsKey(id)) _order.Add(id);
            _at[id] = now;
            if (_order.Count > MaxKinds) { _at.Remove(_order[0]); _order.RemoveAt(0); }
        }

        /// <summary>The notch is hidden again: the delay starts over.</summary>
        public void Hidden() => _freeSince = null;

        /// <summary>The fullscreen app is gone, from this moment on.</summary>
        public void Freed(DateTime now) => _freeSince ??= now;

        /// <summary>
        /// The kinds to show now, oldest first, once: only after <see cref="FullscreenRules.ReleaseDelay"/> has passed
        /// since <see cref="Freed"/>. An empty list while still hidden or while the delay is not over.
        /// </summary>
        public IReadOnlyList<string> Release(DateTime now)
        {
            if (_order.Count == 0 || _freeSince == null || now - _freeSince.Value < FullscreenRules.ReleaseDelay)
                return Array.Empty<string>();
            var ids = _order.OrderBy(id => _at[id]).ToList();
            _order.Clear();
            _at.Clear();
            return ids;
        }

        /// <summary>Forgets everything (the notch was opened by hand, edit mode, shutdown).</summary>
        public void Clear() { _order.Clear(); _at.Clear(); }
    }
}
