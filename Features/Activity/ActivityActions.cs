using System;
using WinNotch.Core.Actions;
using WinNotch.Core.Activity;

namespace WinNotch.Features.Activity
{
    /// <summary>What the activity actions call (the notch's manager in the app, a fake in the tests).</summary>
    public interface IActivityHost
    {
        /// <summary>Removes every activity (alerts, queue, persistent ones). Returns how many there were.</summary>
        int DismissAll();
    }

    /// <summary>The Activity Manager's actions (P13): "activity.dismiss-all".</summary>
    public static class ActivityActions
    {
        public const string DismissAllId = "activity.dismiss-all";
        /// <summary>Segoe Fluent / MDL2 "Clear all notifications".</summary>
        internal const string GClear = "";

        public static ActionDescriptor CreateDismissAll(IActivityHost host) =>
            new ActionDescriptor(DismissAllId, "Închide toate activitățile din notch",
                (args, ct) =>
                {
                    int n = host.DismissAll();
                    return ActionResult.OkTask(n == 0 ? "Nu era nicio activitate" : n == 1 ? "Am închis o activitate" : "Am închis " + n + " activități");
                })
            {
                Aliases = new[] { "închide activitățile", "șterge alertele", "închide alertele", "gata cu alertele", "dismiss all", "clear activities", "clear alerts" },
                Category = "WinNotch",
                Icon = GClear,
                FeatureId = ActivityManager.FeatureId,
                RequiresUiThread = true,
                UnavailableMessage = "Managerul de activități e oprit (Setări › Funcții noi).",
            };

        /// <summary>Registered once at startup, from App.RegisterActions.</summary>
        public static void Register(ActionRegistry registry, IActivityHost host) => registry.Register(CreateDismissAll(host));
    }
}
