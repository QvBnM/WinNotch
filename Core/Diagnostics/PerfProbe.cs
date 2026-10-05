using System;
using System.IO;

namespace WinNotch.Core.Diagnostics
{
    /// <summary>
    /// Opt-in timing for performance work: only if %AppData%\WinNotch\perf.flag exists (checked once at start), the
    /// time from the start of the hover to the first frame of the opening animation goes to the log.
    /// </summary>
    public static class PerfProbe
    {
        public static bool Enabled { get; private set; }

        public static void Init(string folder)
        {
            try { Enabled = File.Exists(Path.Combine(folder, "perf.flag")); } catch { Enabled = false; }
        }

        public static string OpenLine(double ms) => "Perf: hover → primul cadru al deschiderii: " + Math.Round(ms) + " ms";
    }
}
