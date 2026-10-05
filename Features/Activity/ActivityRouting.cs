using System;
using WinNotch.Core.Activity;

namespace WinNotch.Features.Activity
{
    using Activity = WinNotch.Core.Activity.Activity;      // the namespace Features.Activity would hide the type

    /// <summary>
    /// How an existing alert becomes an <see cref="Activity"/> when the "activity-manager" switch is on (P13). Pure, so
    /// the characterization tests post exactly what the notch posts. Nothing about the alert's look changes: the same UI
    /// (<paramref name="payload"/>), size and duration; "important" → High, otherwise Normal (no existing alert is Low or
    /// persistent); the key and "has buttons" come from <see cref="LegacyAlerts"/>.
    /// </summary>
    public static class ActivityRouting
    {
        public static Activity FromAlert(string id, double width, double height, int durationMs, bool important, object payload)
        {
            var spec = LegacyAlerts.Find(id);
            return new Activity
            {
                Id = id,
                Key = spec?.Key ?? id,
                Priority = important ? ActivityPriority.High : ActivityPriority.Normal,
                Duration = TimeSpan.FromMilliseconds(durationMs),
                Interactive = spec?.Interactive ?? false,
                Width = width,
                Height = height,
                Payload = payload,
            };
        }

        /// <summary>
        /// What the caller of an alert learns, as ShowLive told it before: a button alert counts as shown only if it is on
        /// screen now (its caller then turns click-through off); any other alert counts unless it was dropped.
        /// </summary>
        public static bool Accepted(PostResult r, bool interactive, bool onScreen) =>
            interactive ? (r == PostResult.Shown || r == PostResult.Updated) && onScreen : r != PostResult.Dropped;
    }
}
