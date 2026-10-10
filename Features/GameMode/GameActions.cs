using System;
using WinNotch.Core.Actions;
using WinNotch.Core.Perf;

namespace WinNotch.Features.GameMode
{
    /// <summary>What the game actions call (the notch in the app, a fake in the tests).</summary>
    public interface IGameReportHost
    {
        /// <summary>The newest session, or null when there is none yet.</summary>
        GameReport LastReport();
        /// <summary>Shows that session in the notch, the same way it was shown when the game ended.</summary>
        void ShowReport(GameReport report);
    }

    /// <summary>
    /// P61: the summary as an action, so it is reachable from the Command Bar and a shortcut and not only in the
    /// seconds after a game ends — which is exactly when a person is least likely to be looking at the notch.
    /// </summary>
    public static class GameActions
    {
        public const string LastReportId = "game.last-report";
        /// <summary>
        /// The same glyph the alert and the widget use (<c>Ui.GGamepad</c>): one picture for one feature, so the
        /// summary looks like the same thing wherever it shows up.
        /// </summary>
        internal const string GGame = Ui.GGamepad;

        public static ActionDescriptor CreateLastReport(IGameReportHost host)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            return new ActionDescriptor(LastReportId, "Ultimul joc: rezumatul",
                (args, ct) =>
                {
                    var report = host.LastReport();
                    if (report == null || !report.Measured)
                        return System.Threading.Tasks.Task.FromResult(ActionResult.Failed("Nu am încă nicio sesiune de joc măsurată."));
                    host.ShowReport(report);
                    return ActionResult.OkTask(report.Headline());
                })
            {
                Aliases = new[] { "ultimul joc", "rezumat joc", "cum a mers", "sesiunea de joc",
                                  "last game", "game report", "game summary" },
                Category = "WinNotch",
                Icon = GGame,
                FeatureId = GameDetect.FeatureId,
                RequiresUiThread = true,
                UnavailableMessage = "Modul de joc e oprit (Setări › Funcții noi).",
            };
        }

        /// <summary>Registered once at startup, from App.RegisterActions.</summary>
        public static void Register(ActionRegistry registry, IGameReportHost host) => registry.Register(CreateLastReport(host));
    }
}
