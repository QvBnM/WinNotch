using System;
using System.Diagnostics;
using System.Linq;
using WinNotch.Core.Flags;
using WinNotch.Core.Perf;
using WinNotch.Features.Activity;
using WinNotch.Features.GameMode;

namespace WinNotch
{
    /// <summary>
    /// P61: the notch's side of the game mode — a few lines of glue, as the rules for 0.7+ ask. The watcher
    /// (<see cref="GameWatcher"/>) decides everything; this only does what needs a window: quiets WinNotch down while
    /// a game runs, and shows the summary when it ends.
    /// <para><b>What "quiet" means, honestly.</b> Over a fullscreen game the notch is already hidden and its
    /// per-frame work already stopped (P53), so there is not much left to switch off — and inventing some would be the
    /// snake oil the author ruled out. Two things are real and measurable: our own process priority drops (you can see
    /// it in Task Manager), and the weather and calendar fetches are postponed, so no network hiccup of ours lands in
    /// the middle of a match. That is all, and it is all this claims.</para>
    /// </summary>
    public partial class NotchWindow
    {
        private GameWatcher _game;
        private GameReportStore _gameStore;
        private GameReport _lastGameReport;
        private bool _lastGameLoaded;
        private ProcessPriorityClass? _priorityBefore;

        /// <summary>
        /// The newest session of this run, or — once, the first time anyone asks — the newest on disk. Read from the
        /// file only once per run: a widget asks for this about once a second, and reading a file that often to answer
        /// the same question would be exactly the kind of waste this section is supposed to find.
        /// </summary>
        internal GameReport LastGameReport
        {
            get
            {
                if (_lastGameReport != null) return _lastGameReport;
                if (_lastGameLoaded) return null;
                _lastGameLoaded = true;
                return _lastGameReport = GameStore().Latest();
            }
        }

        internal GameReportStore GameStore() => _gameStore ??= new GameReportStore(AppSettings.Folder, App.Log);

        /// <summary>True while a game session is open (the pill and the widgets can say so).</summary>
        internal bool GameRunning => (_game?.Playing ?? "").Length > 0;

        /// <summary>WinNotch holds its own work back while a game runs. Read by the slow part of SecondTick.</summary>
        internal bool GameQuiet { get; private set; }

        /// <summary>Called once from App, after the context engine. Idle with the switch off.</summary>
        internal void StartGameMode(Core.Context.ContextEngine engine)
        {
            try
            {
                _game = new GameWatcher(FeatureFlags.Current, PerfMonitor.Current, engine, new NotchGameHost(this),
                                        GameStore(), temps: () => ((double?)Temps.Cpu, (double?)Temps.Gpu), log: App.Log);
                _game.Start();
            }
            catch (Exception ex) { App.Log("Mod de joc: pornirea a eșuat: " + ex.GetType().Name); }
        }

        /// <summary>Hook, one line in SecondTick: a game that simply vanished leaves no event behind.</summary>
        private void GameModeTick() => _game?.Tick();

        internal void StopGameMode()
        {
            try { _game?.Dispose(); } catch { }
            _game = null;
            RestorePriority();
        }

        // ------------------------------------------------------------------ what the host does

        /// <summary>
        /// Our own process priority, down while a game runs and back afterwards. Only ours: changing another
        /// program's priority is P64, with its own switch and its own way back. Nothing here survives the process, so
        /// a crash mid-game cannot leave the machine changed — which is why P61 needs no restore journal.
        /// </summary>
        private void SetQuiet(bool on)
        {
            GameQuiet = on;
            try
            {
                using var me = Process.GetCurrentProcess();
                if (on)
                {
                    if (_priorityBefore == null) _priorityBefore = me.PriorityClass;
                    if (me.PriorityClass == ProcessPriorityClass.Normal) me.PriorityClass = ProcessPriorityClass.BelowNormal;
                }
                else RestorePriority(me);
            }
            catch (Exception ex) { FeatureFlags.Current?.ReportError(GameDetect.FeatureId, ex); }
        }

        private void RestorePriority(Process me = null)
        {
            if (_priorityBefore == null) return;
            var before = _priorityBefore.Value;
            _priorityBefore = null;
            try
            {
                if (me != null) { me.PriorityClass = before; return; }
                using var self = Process.GetCurrentProcess();
                self.PriorityClass = before;
            }
            catch (Exception ex) { App.Log("Mod de joc, prioritatea înapoi: " + ex.GetType().Name); }
        }

        /// <summary>
        /// The summary, as the alert the notch already knows how to show: the headline on the first line, the two
        /// lines that matter on the second, and a button to the whole thing. Everything else lives in the
        /// „Ultimul joc” widget and in the Performanță tab, which have room for it.
        /// </summary>
        private void ShowGameReport(GameReport report)
        {
            if (report == null) return;
            _lastGameReport = report;
            var lines = report.Lines();
            string sub = string.Join("  ·  ", lines.Take(2));
            var details = Ui.PillBtn("Detalii", () =>
            {
                EndLive();
                try { (System.Windows.Application.Current as App)?.OpenEditor(Features.WindowV2.LayoutRules.Performance); }
                catch (Exception ex) { App.Log("Mod de joc, deschiderea detaliilor: " + ex.GetType().Name); }
            });
            var row = LiveRow(LiveIcon(Features.GameMode.GameActions.GGame, COk), report.Headline(), sub, details);
            ShowInteractive(LegacyAlerts.GameReport, row, 620, 58, 10000);
        }

        /// <summary>Shows a session again, from the action. Same alert as when the game ended.</summary>
        internal void ShowGameReportAgain(GameReport report) => ShowGameReport(report);

        /// <summary>The real <see cref="IGameHost"/>: the notch. Always called on the UI thread (see the dispatch below).</summary>
        private sealed class NotchGameHost : IGameHost
        {
            private readonly NotchWindow _n;
            public NotchGameHost(NotchWindow notch) { _n = notch; }

            // The watcher may call from the context engine's timer thread: everything that touches the window goes
            // back to the Dispatcher, as the rules for 0.7+ require.
            public void Quiet(bool on) => _n.Dispatcher.InvokeAsync(() => _n.SetQuiet(on));
            public void SessionEnded(GameReport report) => _n.Dispatcher.InvokeAsync(() => _n.ShowGameReport(report));
        }
    }
}

namespace WinNotch.Features.GameMode
{
    /// <summary>The real <see cref="IGameReportHost"/>: the notch, for the "game.last-report" action.</summary>
    internal sealed class NotchGameReportHost : IGameReportHost
    {
        private readonly NotchWindow _n;
        public NotchGameReportHost(NotchWindow notch) { _n = notch; }

        public Core.Perf.GameReport LastReport() => _n?.LastGameReport;
        public void ShowReport(Core.Perf.GameReport report) => _n?.ShowGameReportAgain(report);
    }
}
