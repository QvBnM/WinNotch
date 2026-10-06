using System;
using WinNotch.Core.Actions;

namespace WinNotch.Features.QuickActions
{
    /// <summary>What "quick-actions.show-hidden" calls: the notch's settings (the real one in the notch, a fake in the tests).</summary>
    public interface IQuickActionsHost
    {
        /// <summary>Every rule hidden with „Nu mai arăta” may suggest again; returns how many were hidden.</summary>
        int ShowHiddenAgain();
    }

    /// <summary>P20 as an action: undo every „Nu mai arăta” (only with the "quick-actions" switch on).</summary>
    public static class QuickActionsActions
    {
        public const string ShowHiddenId = "quick-actions.show-hidden";
        /// <summary>Segoe Fluent / MDL2 "Lightning bolt".</summary>
        internal const string GQuick = "";

        public static ActionDescriptor CreateShowHidden(IQuickActionsHost host)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            return new ActionDescriptor(ShowHiddenId, "Quick Actions: arată din nou sugestiile ascunse", (args, ct) =>
            {
                int n = host.ShowHiddenAgain();
                return ActionResult.OkTask(n == 0 ? "Nicio sugestie nu era ascunsă" : n == 1 ? "O sugestie poate apărea din nou" : n + " sugestii pot apărea din nou");
            })
            {
                Aliases = new[] { "quick actions", "acțiuni rapide", "sugestii", "nu mai arăta", "show suggestions", "reset suggestions" },
                Category = "WinNotch", Icon = GQuick, FeatureId = QuickActionRules.FeatureId, RequiresUiThread = true,
            };
        }

        /// <summary>Registered once at startup, from App.RegisterActions.</summary>
        public static void Register(ActionRegistry registry, IQuickActionsHost host) => registry.Register(CreateShowHidden(host));
    }
}
