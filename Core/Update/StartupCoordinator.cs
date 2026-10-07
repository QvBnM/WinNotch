using System;

namespace WinNotch.Core.Update
{
    /// <summary>What the startup steps need from the app (the real one in App.xaml.cs, a fake in the tests).</summary>
    public interface IStartupHost
    {
        /// <summary>Full path of the running WinNotch.exe.</summary>
        string ExePath { get; }
        /// <summary>WinNotch.old.exe exists and is an older WinNotch (see <see cref="StartupGuard.IsValidPrevious"/>).</summary>
        bool PreviousExeValid();
        /// <summary>The app keeps its own exe open; it must be closed before the file can be renamed.</summary>
        void ReleaseExeLock();
        void LockExe();
        /// <summary>The single-instance mutex.</summary>
        void ReleaseMutex();
        /// <summary>Takes the single-instance mutex again; false if another WinNotch took it meanwhile.</summary>
        bool TakeMutex();
        /// <summary>Starts an exe by its full path; false if it couldn't.</summary>
        bool Start(string exe, string args);
        void Log(string message);
    }

    public enum StartupOutcome
    {
        /// <summary>Go on starting normally.</summary>
        Continue,
        /// <summary>Go on, in safe mode.</summary>
        ContinueSafe,
        /// <summary>Another process (safe mode or the previous version) took over: quit now.</summary>
        HandedOver,
        /// <summary>Quit without starting anything (another WinNotch is running).</summary>
        Quit,
    }

    /// <summary>
    /// The order of the startup steps around <see cref="StartupGuard"/>, kept out of the WPF code so it is tested:
    /// the single-instance mutex is released only right before the other process starts, and taken back (checked) if
    /// that fails; the rollback note is written before the files are swapped.
    /// </summary>
    public static class StartupCoordinator
    {
        public const string RollbackReason = "se închidea brusc, inclusiv în modul sigur";

        public static StartupOutcome Run(StartupGuard guard, IStartupHost host, bool safeModeArg, string safeModeArgText)
        {
            var action = guard.Begin(safeModeArg, host.PreviousExeValid());
            switch (action)
            {
                case StartupAction.Rollback:
                    guard.RollingBack(RollbackReason);
                    if (!Rollback.Swap(host.ExePath, host.ReleaseExeLock, host.Log))
                    {
                        guard.RollbackFailed();         // nothing destructive happened: stay, in safe mode
                        host.LockExe();
                        return StartupOutcome.ContinueSafe;
                    }
                    host.ReleaseMutex();
                    if (!host.Start(host.ExePath, null))
                        host.Log("Revenire: versiunea anterioară nu a pornit; pornește WinNotch din nou.");
                    Ending(host, Diagnostics.ShutdownKind.Rollback);
                    return StartupOutcome.HandedOver;

                case StartupAction.RestartInSafeMode:
                    host.ReleaseMutex();
                    if (host.Start(host.ExePath, safeModeArgText))
                    {
                        Ending(host, Diagnostics.ShutdownKind.SafeRestart);
                        return StartupOutcome.HandedOver;
                    }
                    host.Log("Repornirea în modul sigur nu a reușit; continui în modul sigur.");
                    if (!host.TakeMutex())
                    {
                        Ending(host, Diagnostics.ShutdownKind.SecondInstance);
                        return StartupOutcome.Quit;
                    }
                    guard.ContinueInSafeMode();
                    return StartupOutcome.ContinueSafe;

                case StartupAction.SafeMode:
                    return StartupOutcome.ContinueSafe;

                default:
                    return StartupOutcome.Continue;
            }
        }

        /// <summary>
        /// P51c: this process is handing over and will quit; the reason goes to the log for the user. Here and not in
        /// App.xaml.cs because only this method knows which of the two hand-overs happened.
        /// <para>Only the log: the reason is deliberately NOT written to startup.json. By this point the mutex has been
        /// released and the successor (the previous version, the safe-mode run, or a WinNotch that was already running)
        /// may have recorded its own start, so a save here would overwrite its state — and it would never be read
        /// anyway, because every hand-over leaves <c>Running = false</c>.</para>
        /// </summary>
        private static void Ending(IStartupHost host, Diagnostics.ShutdownKind kind) =>
            host.Log(Diagnostics.ShutdownJournal.Line(kind));
    }
}
