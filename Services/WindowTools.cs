using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace WinNotch.Services
{
    public sealed class WorkspaceWindow
    {
        public string Exe { get; set; }
        public int Left { get; set; }
        public int Top { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public bool Maximized { get; set; }
    }

    public sealed class Workspace
    {
        public string Name { get; set; } = "Spațiu";
        public List<WorkspaceWindow> Windows { get; set; } = new List<WorkspaceWindow>();
    }

    /// <summary>Workspaces (open a set of apps arranged as saved) and actions on the active window.</summary>
    public static class WindowTools
    {
        private static readonly HashSet<string> SkipExe = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "explorer", "ApplicationFrameHost", "SystemSettings", "TextInputHost", "ShellExperienceHost", "SearchHost",
            "StartMenuExperienceHost", "LockApp", "WinNotch", "dwm"
        };

        /// <summary>Top-level app windows the user would recognise (visible, titled, not tool windows).</summary>
        public static List<IntPtr> AppWindows(IntPtr self)
        {
            var list = new List<IntPtr>();
            Native.EnumWindows((h, l) =>
            {
                if (h == self || !Native.IsWindowVisible(h) || Native.GetWindowTextLength(h) == 0) return true;
                long ex = Native.GetExStyle(h);
                if ((ex & Native.WS_EX_TOOLWINDOW) != 0 || (ex & Native.WS_EX_NOACTIVATE) != 0 || Native.IsCloaked(h)) return true;
                string cls = Native.ClassName(h);
                if (cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd") return true;
                list.Add(h);
                return true;
            }, IntPtr.Zero);
            return list;
        }

        public static Workspace Capture(IntPtr self, string name)
        {
            var ws = new Workspace { Name = name };
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var h in AppWindows(self))
            {
                string exe = Native.ProcessPath(h);
                if (string.IsNullOrEmpty(exe) || SkipExe.Contains(Path.GetFileNameWithoutExtension(exe))) continue;
                if (!seen.Add(exe)) continue;                       // one window per app keeps restores predictable
                var wp = new Native.WINDOWPLACEMENT { length = System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.WINDOWPLACEMENT)) };
                Native.GetWindowPlacement(h, ref wp);
                Native.GetWindowRect(h, out var r);
                bool max = Native.IsZoomed(h);
                var rc = max || Native.IsIconic(h) ? wp.rcNormalPosition : r;
                ws.Windows.Add(new WorkspaceWindow { Exe = exe, Left = rc.Left, Top = rc.Top, Width = rc.Width, Height = rc.Height, Maximized = max });
            }
            return ws;
        }

        /// <summary>Opens what's missing and puts every window back where it was saved.</summary>
        public static async Task RestoreAsync(Workspace ws, IntPtr self)
        {
            foreach (var w in ws.Windows)
            {
                try
                {
                    IntPtr h = FindWindowFor(w.Exe, self);
                    if (h == IntPtr.Zero && File.Exists(w.Exe))
                    {
                        Shell.Open(w.Exe, Path.GetDirectoryName(w.Exe));
                        for (int i = 0; i < 40 && h == IntPtr.Zero; i++)            // wait up to ~10 s for its window
                        {
                            await Task.Delay(250);
                            h = FindWindowFor(w.Exe, self);
                        }
                    }
                    if (h == IntPtr.Zero) continue;
                    Native.ShowWindow(h, Native.SW_RESTORE);
                    Native.SetWindowPos(h, IntPtr.Zero, w.Left, w.Top, w.Width, w.Height, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
                    if (w.Maximized) Native.ShowWindow(h, Native.SW_MAXIMIZE);
                }
                catch (Exception ex) { App.Log("Spațiu de lucru: " + ex.Message); }
            }
        }

        private static IntPtr FindWindowFor(string exe, IntPtr self) =>
            AppWindows(self).FirstOrDefault(h => string.Equals(Native.ProcessPath(h), exe, StringComparison.OrdinalIgnoreCase));

        // ----------------------------------------------------------------- active window

        public static string AppName(IntPtr h)
        {
            string exe = Native.ProcessPath(h);
            return exe == null ? "" : AudioSessionsService.Friendly(Path.GetFileNameWithoutExtension(exe).ToLowerInvariant());
        }

        public static bool IsTopmost(IntPtr h) => (Native.GetExStyle(h) & Native.WS_EX_TOPMOST) != 0;

        public static void ToggleTopmost(IntPtr h)
        {
            bool on = IsTopmost(h);
            Native.SetWindowPos(h, on ? Native.HWND_NOTOPMOST : Native.HWND_TOPMOST, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }

        private static Native.RECT WorkArea(IntPtr mon)
        {
            var mi = new Native.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.MONITORINFO)) };
            Native.GetMonitorInfo(mon, ref mi);
            return mi.rcWork;
        }

        /// <summary>Moves the window to the next monitor, keeping its relative position and size.</summary>
        public static void MoveToNextMonitor(IntPtr h)
        {
            var mons = new List<IntPtr>();
            Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr m, IntPtr hdc, ref Native.RECT r, IntPtr d) => { mons.Add(m); return true; }, IntPtr.Zero);
            if (mons.Count < 2) return;
            IntPtr cur = Native.MonitorFromWindow(h, Native.MONITOR_DEFAULTTONEAREST);
            int idx = Math.Max(0, mons.IndexOf(cur));
            IntPtr next = mons[(idx + 1) % mons.Count];
            var a = WorkArea(cur);
            var b = WorkArea(next);
            bool max = Native.IsZoomed(h);
            if (max) Native.ShowWindow(h, Native.SW_RESTORE);
            Native.GetWindowRect(h, out var wr);
            double fx = (wr.Left - a.Left) / (double)Math.Max(1, a.Width), fy = (wr.Top - a.Top) / (double)Math.Max(1, a.Height);
            int w = Math.Min(wr.Width, b.Width), hh = Math.Min(wr.Height, b.Height);
            int x = b.Left + (int)(fx * b.Width), y = b.Top + (int)(fy * b.Height);
            x = Math.Min(Math.Max(x, b.Left), b.Right - w);
            y = Math.Min(Math.Max(y, b.Top), b.Bottom - hh);
            Native.SetWindowPos(h, IntPtr.Zero, x, y, w, hh, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
            if (max) Native.ShowWindow(h, Native.SW_MAXIMIZE);
        }

        /// <summary>Left half → right half → left half…</summary>
        public static void SnapHalf(IntPtr h)
        {
            var a = WorkArea(Native.MonitorFromWindow(h, Native.MONITOR_DEFAULTTONEAREST));
            if (Native.IsZoomed(h)) Native.ShowWindow(h, Native.SW_RESTORE);
            Native.GetWindowRect(h, out var wr);
            int half = a.Width / 2;
            bool onLeft = Math.Abs(wr.Left - a.Left) < 20 && Math.Abs(wr.Width - half) < 40;
            int x = onLeft ? a.Left + half : a.Left;
            Native.SetWindowPos(h, IntPtr.Zero, x, a.Top, half, a.Height, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
        }

        /// <summary>Small window in the bottom-right corner (about a third of the screen) — handy for a video or a call.</summary>
        public static void Mini(IntPtr h)
        {
            var a = WorkArea(Native.MonitorFromWindow(h, Native.MONITOR_DEFAULTTONEAREST));
            if (Native.IsZoomed(h)) Native.ShowWindow(h, Native.SW_RESTORE);
            int w = a.Width / 3, hh = a.Height / 3, m = 16;
            Native.SetWindowPos(h, IntPtr.Zero, a.Right - w - m, a.Bottom - hh - m, w, hh, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
        }
    }
}
