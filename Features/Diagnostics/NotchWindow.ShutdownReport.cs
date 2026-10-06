using System;
using WinNotch.Core.Diagnostics;
using WinNotch.Core.Flags;
using WinNotch.Features.Activity;
using WinNotch.Services;

namespace WinNotch
{
    /// <summary>
    /// P51c: tells the user when WinNotch closed by itself. Every exit of ours writes its reason in the log
    /// (<see cref="ShutdownJournal"/>); a run that ends without one is counted by
    /// <see cref="Core.Update.StartupGuard"/>, and from the second such closure in a row the notch says so once, with a
    /// button that opens the log.
    /// <para>Shown once per start, from <c>UpdateTick</c> (one line there), only in standby and only after the first
    /// seconds, like the rollback message. The count is not cleared here: it is what also keeps the in-process
    /// temperature read switched off (<see cref="SensorGuard"/>), and only a clean run clears it.</para>
    /// <para>Switch "shutdown-report" (Beta, on): off, the log still names every closure and nothing is shown.</para>
    /// </summary>
    public partial class NotchWindow
    {
        private bool _shutdownReportShown;

        private static bool ShutdownReportEnabled() => FeatureFlags.Current?.IsEnabled(ShutdownJournal.FeatureId) ?? false;

        /// <summary>
        /// Hook, one line in UpdateTick. True when the alert was shown, so the tick stops there this second.
        /// </summary>
        private bool ShutdownReportTick()
        {
            if (_shutdownReportShown || _mode != Mode.Idle || _tick <= 3) return false;
            if (!ShutdownReportEnabled()) return false;
            int inARow = App.Guard?.UnexplainedInARow ?? 0;
            if (!ShutdownJournal.ShouldAlert(inARow)) return false;
            _shutdownReportShown = true;
            try { ShowShutdownReport(inARow); }
            catch (Exception ex) { FeatureFlags.Current?.ReportError(ShutdownJournal.FeatureId, ex); }
            return true;
        }

        private void ShowShutdownReport(int inARow)
        {
            var open = Ui.PillBtn("Deschide log-ul", () =>
            {
                try { Shell.Open(App.LogPath); }
                catch (Exception ex) { App.Log("Raportul închiderilor, deschiderea log-ului: " + ex.GetType().Name); }
                EndLive();
            });
            string sub = "De " + inARow + " ori la rând, fără să ceri tu ieșirea. " + ShutdownJournal.AlertBody;
            var row = LiveRow(LiveIcon(Ui.GWarn, CWarn), ShutdownJournal.AlertTitle, sub, open);
            ShowInteractive(LegacyAlerts.ShutdownUnexplained, row, 520, 58, 9000);
        }
    }
}
