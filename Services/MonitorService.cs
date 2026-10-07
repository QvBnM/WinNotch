using System;
using System.Collections.Generic;
using System.Linq;

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
        private static readonly HashSet<string> IgnoredClasses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "NotifyIconOverflowWindow",
            "XamlExplorerHostIslandWindow", "TopLevelWindowForOverflowXamlIsland", "Windows.UI.Core.CoreWindow",
            "ForegroundStaging", "MultitaskingViewFrame", "TaskListThumbnailWnd", "CEF-OSC-WIDGET",
            "Windows.UI.Composition.DesktopWindowContentBridge", "ThumbnailDeviceHelperWnd", "EdgeUiInputTopWndClass",
            "ApplicationManager_ImmersiveShellWindow", "Internet Explorer_Hidden", "IME", "MSCTFIME UI"
        };

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
            Native.EnumWindows((h, l) =>
            {
                if (decided.Count >= monitors.Count) return false;
                if (h == self || !Native.IsWindowVisible(h) || Native.IsIconic(h)) return true;

                long ex = Native.GetExStyle(h);
                if ((ex & Native.WS_EX_TOOLWINDOW) != 0 || (ex & Native.WS_EX_TRANSPARENT) != 0 || (ex & Native.WS_EX_NOACTIVATE) != 0) return true;
                if (Native.GetWindowTextLength(h) == 0) return true;          // overlays and helper windows have no title
                if (Native.IsCloaked(h)) return true;
                string cls = Native.ClassName(h);
                if (IgnoredClasses.Contains(cls)) return true;
                if (!Native.GetWindowRect(h, out var wr) || wr.Width <= 0 || wr.Height <= 0) return true;

                IntPtr mh = Native.MonitorFromWindow(h, Native.MONITOR_DEFAULTTONEAREST);
                var mon = monitors.FirstOrDefault(m => m.Handle == mh);
                if (mon == null || decided.Contains(mh)) return true;

                var b = mon.Bounds;
                var w = mon.Work;
                bool covers = wr.Left <= b.Left && wr.Top <= b.Top && wr.Right >= b.Right && wr.Bottom >= b.Bottom;
                // Real maximize, or an app that sizes itself to fill the work area (custom title bars, "fake" maximize).
                const int tol = 12;
                bool fillsWork = Math.Abs(wr.Left - w.Left) <= tol && Math.Abs(wr.Top - w.Top) <= tol &&
                                 Math.Abs(wr.Right - w.Right) <= tol && Math.Abs(wr.Bottom - w.Bottom) <= tol;
                bool zoomed = Native.IsZoomed(h) || (fillsWork && !covers);

                // Small windows (sticky notes, widgets, a calculator) don't decide the monitor's state: keep looking below them.
                double area = (double)Math.Max(0, Math.Min(wr.Right, b.Right) - Math.Max(wr.Left, b.Left)) *
                              Math.Max(0, Math.Min(wr.Bottom, b.Bottom) - Math.Max(wr.Top, b.Top));
                bool big = covers || zoomed || area >= 0.5 * b.Width * b.Height;
                if (!big) return true;

                mon.Busy = covers && !Native.IsZoomed(h);
                mon.Maximized = zoomed;
                // The window's title never goes in the log: it is the user's business (section 14 of DOCUMENTATIE.md,
                // and the same rule as ContextSnapshot.ToLogString). The class, the process and the geometry are enough
                // to tell which window decided a monitor's state, and are what the fullscreen rules are judged on.
                mon.Decider = Describe(h, cls, wr);
                decided.Add(mh);
                return true;
            }, IntPtr.Zero);

            return monitors;
        }

        /// <summary>
        /// How a window is named in the log: its class, the process' file name and its rectangle. Deliberately without
        /// the title — a title says what the user is reading or watching, and nothing of that goes in log.txt.
        /// </summary>
        private static string Describe(IntPtr h, string cls, Native.RECT wr) =>
            cls + " (" + ProcessName(h) + ") [" + wr.Left + "," + wr.Top + " " + wr.Width + "x" + wr.Height + "]";

        /// <summary>
        /// The window's process, lower-cased, without path or extension. Cached per window: this runs for every monitor
        /// on every scan, and opening the process each time would be work for nothing. Only ever used for the log line,
        /// so a handle Windows reused keeps the old name at worst.
        /// </summary>
        private static readonly Dictionary<IntPtr, string> ProcessNames = new Dictionary<IntPtr, string>();

        private static string ProcessName(IntPtr h)
        {
            if (ProcessNames.TryGetValue(h, out string cached)) return cached;
            string name = "?";
            try
            {
                string path = Native.ProcessPath(h);
                if (!string.IsNullOrEmpty(path)) name = System.IO.Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            }
            catch { /* a window of a process we may not query: the class alone still tells enough */ }
            if (ProcessNames.Count >= 64) ProcessNames.Clear();        // windows come and go; the cache never grows
            ProcessNames[h] = name;
            return name;
        }

        public static IntPtr MonitorUnderCursor()
        {
            Native.GetCursorPos(out var p);
            return Native.MonitorFromPoint(p, Native.MONITOR_DEFAULTTONEAREST);
        }
    }
}
