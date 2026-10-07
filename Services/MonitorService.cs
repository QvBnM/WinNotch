using System;
using System.Collections.Generic;
using System.Linq;
using WinNotch.Core.Flags;
using WinNotch.Features.Fullscreen;

namespace WinNotch.Services
{
    public sealed class MonitorState
    {
        public IntPtr Handle;
        public Native.RECT Bounds;
        public Native.RECT Work;
        public double Scale = 1.0;
        /// <summary>A window covers the whole monitor (game, video, presentation; borderless or exclusive).</summary>
        public bool Busy;
        /// <summary>The top big window on this monitor is maximized (tabs/title bar sit under the notch).</summary>
        public bool Maximized;
        /// <summary>Which window decided the state, for the log.</summary>
        public string Decider = "desktop";
    }

    /// <summary>
    /// Looks at the top-level windows in z-order and decides, per monitor, whether it is busy
    /// (fullscreen, borderless included) or has a maximized window on top.
    /// </summary>
    public static class MonitorService
    {
        public static List<MonitorState> Scan(IntPtr self)
        {
            var monitors = new List<MonitorState>();
            Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr h, IntPtr hdc, ref Native.RECT r, IntPtr d) =>
            {
                var info = new Native.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.MONITORINFO)) };
                if (Native.GetMonitorInfo(h, ref info))
                    monitors.Add(new MonitorState { Handle = h, Bounds = info.rcMonitor, Work = info.rcWork, Scale = Native.ScaleForMonitor(h) });
                return true;
            }, IntPtr.Zero);

            var decided = new HashSet<IntPtr>();
            // P53: the switch decides how "busy" is read; off = the old rule (covers the monitor and not IsZoomed).
            bool strict = FeatureFlags.Current?.IsEnabled(FullscreenRules.FeatureId) ?? true;
            Native.EnumWindows((h, l) =>
            {
                if (decided.Count >= monitors.Count) return false;
                if (h == self || !Native.IsWindowVisible(h) || Native.IsIconic(h)) return true;
                long ex = Native.GetExStyle(h);
                if ((ex & Native.WS_EX_TOOLWINDOW) != 0 || (ex & Native.WS_EX_TRANSPARENT) != 0 || (ex & Native.WS_EX_NOACTIVATE) != 0) return true;
                if (!Native.GetWindowRect(h, out var wr)) return true;
                var box = Rect(wr);
                string cls = Native.ClassName(h);
                if (FullscreenRules.Ignored(cls, h == self, Native.GetWindowTextLength(h) > 0, false, box)) return true;
                if (Native.IsCloaked(h)) return true;              // after the cheap filters: this one asks DWM

                IntPtr mh = Native.MonitorFromWindow(h, Native.MONITOR_DEFAULTTONEAREST);
                var mon = monitors.FirstOrDefault(m => m.Handle == mh);
                if (mon == null || decided.Contains(mh)) return true;

                long st = Native.GetStyle(h);
                bool zoomed = Native.IsZoomed(h);
                var monBounds = Rect(mon.Bounds);
                var monWork = Rect(mon.Work);
                var use = FullscreenRules.Classify(box, monBounds, monWork, zoomed,
                                                  (st & Native.WS_CAPTION) != 0, (st & Native.WS_THICKFRAME) != 0, strict);
                if (use == null) return true;            // too small to decide: keep looking below it

                mon.Busy = use == MonitorUse.Busy;
                // A busy window is often maximized too (a browser in fullscreen keeps WS_MAXIMIZE): the small-pill rules
                // need that for the "always visible" setting, where the notch stays on screen over it.
                mon.Maximized = use == MonitorUse.Maximized || FullscreenRules.LooksMaximized(box, monBounds, monWork, zoomed);
                // The window's title is personal: only its class and its rectangle go in the log.
                mon.Decider = cls + " " + box;
                decided.Add(mh);
                return true;
            }, IntPtr.Zero);

            return monitors;
        }

        private static Box Rect(Native.RECT r) => new Box(r.Left, r.Top, r.Right, r.Bottom);

        public static IntPtr MonitorUnderCursor()
        {
            Native.GetCursorPos(out var p);
            return Native.MonitorFromPoint(p, Native.MONITOR_DEFAULTTONEAREST);
        }
    }
}
