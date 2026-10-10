using System;
using WinNotch.Core.Actions;
using WinNotch.Core.Perf;

namespace WinNotch.Features.Performance
{
    /// <summary>What the performance actions call (the app in real life, a fake in the tests).</summary>
    public interface IPerfHost
    {
        /// <summary>
        /// Opens the WinNotch window on the Performanță tab. Returns null when it worked, or a short reason in
        /// Romanian — the tab lives in the new window, so it cannot open while that switch is off, and saying so is
        /// more use than a generic failure.
        /// </summary>
        string OpenPerformance();
    }

    /// <summary>
    /// P60: the section as an action, so it is reachable from the Command Bar and from a shortcut, not only by
    /// clicking through the window — the rule from <c>CLAUDE.md</c> that every new capability is also an action.
    /// </summary>
    public static class PerfActions
    {
        public const string OpenId = "perf.open";
        /// <summary>Segoe Fluent / MDL2 "Speed high".</summary>
        internal const string GSpeed = "";

        public static ActionDescriptor CreateOpen(IPerfHost host) =>
            new ActionDescriptor(OpenId, "Deschide Performanță",
                (args, ct) =>
                {
                    string why = host.OpenPerformance();
                    return why == null ? ActionResult.OkTask("Am deschis fila Performanță")
                                       : System.Threading.Tasks.Task.FromResult(ActionResult.Failed(why));
                })
            {
                Aliases = new[] { "performanță", "performanta", "monitorizare", "procesor și memorie", "cine consumă",
                                  "performance", "monitor", "resources", "task manager" },
                Category = "WinNotch",
                Icon = GSpeed,
                FeatureId = PerfRules.FeatureId,
                RequiresUiThread = true,
                UnavailableMessage = "Secțiunea Performanță e oprită (Setări › Funcții noi).",
            };

        /// <summary>Registered once at startup, from App.RegisterActions.</summary>
        public static void Register(ActionRegistry registry, IPerfHost host) => registry.Register(CreateOpen(host));
    }
}
