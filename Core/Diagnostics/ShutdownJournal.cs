using System;

namespace WinNotch.Core.Diagnostics
{
    /// <summary>Why the process ended. One fixed word per reason; nothing from an exception message.</summary>
    public enum ShutdownKind
    {
        /// <summary>"Ieșire" in the tray menu.</summary>
        UserTray,
        /// <summary>Restarting into a downloaded, verified version.</summary>
        Update,
        /// <summary>The guard put the previous version back.</summary>
        Rollback,
        /// <summary>The guard restarts this version with --safe-mode.</summary>
        SafeRestart,
        /// <summary>Another WinNotch already holds the single-instance mutex.</summary>
        SecondInstance,
        /// <summary>The temperature helper / its install or removal: a run with no window.</summary>
        HelperMode,
        /// <summary>A start that failed halfway (counted as a crash on purpose).</summary>
        StartupFailed,
        /// <summary>Windows is shutting down or signing the user out.</summary>
        WindowsShutdown,
        /// <summary>Restarting with administrator rights, at the user's request.</summary>
        RestartAsAdmin,
        /// <summary>An unhandled exception on a background thread; the runtime still ends the process.</summary>
        FatalError,
        /// <summary>No reason was written: the process died without reaching any exit of ours.</summary>
        Unexplained,
    }

    /// <summary>
    /// The last line of every run: <c>Închidere: &lt;motiv&gt;</c>, and at the next start
    /// <c>Închidere anterioară: neexplicată</c> when the previous run reached none of our exits.
    /// <para>Pure on purpose: a crash that corrupts the process state (an AccessViolationException from a driver, a
    /// stack overflow) runs no handler at all — not <c>AppDomain.UnhandledException</c>, not a <c>catch</c> — so such a
    /// death can never write its own line. It is named at the <em>next</em> start instead, from what
    /// <see cref="Update.StartupGuard"/> already records, which is why the reason is a value here and not a message.</para>
    /// </summary>
    public static class ShutdownJournal
    {
        /// <summary>Features/Diagnostics: the switch that lets the notch report an unexplained closure.</summary>
        public const string FeatureId = "shutdown-report";

        public const string Prefix = "Închidere: ";
        public const string PreviousPrefix = "Închidere anterioară: ";

        /// <summary>Two unexplained closures in a row are worth telling the user about.</summary>
        public const int AlertAfter = 2;

        /// <summary>The fixed Romanian text of a reason (never an exception message).</summary>
        public static string Text(ShutdownKind kind) => kind switch
        {
            ShutdownKind.UserTray => "cerere utilizator (tray)",
            ShutdownKind.Update => "actualizare",
            ShutdownKind.Rollback => "revenire automată",
            ShutdownKind.SafeRestart => "repornire în mod sigur",
            ShutdownKind.SecondInstance => "a doua instanță",
            ShutdownKind.HelperMode => "mod ajutător",
            ShutdownKind.StartupFailed => "pornirea a eșuat",
            ShutdownKind.WindowsShutdown => "Windows se închide",
            ShutdownKind.RestartAsAdmin => "repornire ca administrator",
            ShutdownKind.FatalError => "eroare fatală",
            ShutdownKind.Unexplained => "neexplicată",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        /// <summary>The line this run writes as it ends.</summary>
        public static string Line(ShutdownKind kind) => Prefix + Text(kind);

        /// <summary>The line the next start writes about the run before it.</summary>
        public static string PreviousLine(ShutdownKind kind) => PreviousPrefix + Text(kind);

        /// <summary>
        /// Whether to show the user "WinNotch s-a închis singur": only from the second unexplained closure in a row, so
        /// one killed process (Task Manager, a forced shutdown) stays a line in the log.
        /// </summary>
        public static bool ShouldAlert(int unexplainedInARow) => unexplainedInARow >= AlertAfter;

        /// <summary>What the notch shows when <see cref="ShouldAlert"/> holds.</summary>
        public const string AlertTitle = "WinNotch s-a închis singur";
        public const string AlertBody = "Detalii în log.";
        public const string AlertButton = "Deschide log-ul";
    }
}
