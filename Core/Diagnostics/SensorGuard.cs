using System;

namespace WinNotch.Core.Diagnostics
{
    /// <summary>What a refresh of the hardware sensors is allowed to do this time round.</summary>
    public enum SensorStep
    {
        /// <summary>Read the sensors as usual.</summary>
        Read,
        /// <summary>The monitors changed moments ago: read nothing, and don't reopen yet either.</summary>
        Wait,
        /// <summary>The change has settled: close and open the library again, read nothing this time.</summary>
        Reopen,
        /// <summary>Don't touch the library at all (it took the app down before).</summary>
        Blocked,
    }

    /// <summary>
    /// Whether WinNotch may read CPU / GPU / disk temperatures inside its own process.
    /// <para>The GPU part of LibreHardwareMonitor calls NVIDIA's NVML with a device handle it keeps from the moment the
    /// library was opened. When the display setup changes, the graphics driver re-initialises and that handle dies;
    /// using it reads memory the process no longer owns (AccessViolationException, 0xc0000005). That cannot be caught:
    /// the runtime ends the process without running a single handler, so a <c>try</c> around the read is no protection.
    /// Three rules keep it away instead:</para>
    /// <list type="number">
    /// <item>from the moment the monitors change, nothing is read — not even the CPU, because one pass of the library
    /// touches every device;</item>
    /// <item><see cref="Settle"/> after the last change (a replug is a burst of events, and the driver needs a moment),
    /// the library is closed and opened again, so the dead handle is never used;</item>
    /// <item>if the app still ended abruptly twice in a row, the in-process read stops for good (the user turns it back
    /// on in Settings). Temperatures go missing; the app stays up.</item>
    /// </list>
    /// <para>This narrows the crash to the window between a change Windows has not announced yet and the next read. It
    /// does not close it: only moving the reading out of this process does (P51d).</para>
    /// Pure rules: no WPF, no hardware, so every branch is tested.
    /// </summary>
    public static class SensorGuard
    {
        /// <summary>Abrupt closures in a row after which the in-process read is given up.</summary>
        public const int AbruptLimit = 2;

        /// <summary>Quiet time after the last display change before the library is opened again.</summary>
        public static readonly TimeSpan Settle = TimeSpan.FromSeconds(3);

        /// <summary>Fixed text (never an exception message), for the log and for Settings. Max 120 characters.</summary>
        public const string BlockedReason = "citirea temperaturilor în proces s-a oprit după închideri bruște repetate";

        /// <summary>False once the app ended abruptly <see cref="AbruptLimit"/> times in a row.</summary>
        public static bool AllowInProcess(int unexplainedInARow) => unexplainedInARow < AbruptLimit;

        /// <summary>
        /// What this refresh may do.
        /// </summary>
        /// <param name="unexplainedInARow">Runs that ended without naming a reason, back to back.</param>
        /// <param name="displayChangedAt">
        /// When Windows last reported a display change, or null when the library's handles are known to be fresh. Stays
        /// set until a reopen has actually happened, so a change arriving while the library is opening is not lost.
        /// </param>
        /// <param name="now">The clock (injected, so the settle time is tested without waiting).</param>
        public static SensorStep Next(int unexplainedInARow, DateTime? displayChangedAt, DateTime now)
        {
            if (!AllowInProcess(unexplainedInARow)) return SensorStep.Blocked;
            if (displayChangedAt == null) return SensorStep.Read;
            // Duration(): a clock moved backwards must not make a change look old enough to be safe
            return (now - displayChangedAt.Value).Duration() >= Settle ? SensorStep.Reopen : SensorStep.Wait;
        }
    }
}
