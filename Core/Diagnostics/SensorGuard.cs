namespace WinNotch.Core.Diagnostics
{
    /// <summary>What a refresh of the hardware sensors is allowed to do this time round.</summary>
    public enum SensorStep
    {
        /// <summary>Read the sensors as usual.</summary>
        Read,
        /// <summary>The monitors changed: close and open the library again, read nothing this time.</summary>
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
    /// Two rules keep it away instead:</para>
    /// <list type="number">
    /// <item>after the monitors change, the library is closed and opened again before anything is read, so the dead
    /// handle is never used;</item>
    /// <item>if the app still ended abruptly twice in a row, the in-process read stops for good (the user turns it back
    /// on in Settings). Temperatures go missing; the app stays up.</item>
    /// </list>
    /// Pure rules: no WPF, no hardware, so every branch is tested.
    /// </summary>
    public static class SensorGuard
    {
        /// <summary>Abrupt closures in a row after which the in-process read is given up.</summary>
        public const int AbruptLimit = 2;

        /// <summary>Fixed text (never an exception message), for the log and for Settings. Max 120 characters.</summary>
        public const string BlockedReason = "citirea temperaturilor în proces s-a oprit după închideri bruște repetate";

        /// <summary>False once the app ended abruptly <see cref="AbruptLimit"/> times in a row.</summary>
        public static bool AllowInProcess(int unexplainedInARow) => unexplainedInARow < AbruptLimit;

        /// <summary>
        /// What this refresh may do. <paramref name="displayChanged"/> is set by Windows' display-settings event and
        /// cleared once the library has been reopened.
        /// </summary>
        public static SensorStep Next(int unexplainedInARow, bool displayChanged)
        {
            if (!AllowInProcess(unexplainedInARow)) return SensorStep.Blocked;
            return displayChanged ? SensorStep.Reopen : SensorStep.Read;
        }
    }
}
