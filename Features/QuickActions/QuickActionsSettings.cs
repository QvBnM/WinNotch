using System;
using System.Collections.Generic;
using System.Linq;
using WinNotch.Features.QuickActions;

namespace WinNotch
{
    public sealed partial class AppSettings
    {
        /// <summary>
        /// P20: the Quick Actions rules whose unasked suggestions you turned off with „Nu mai arăta” (rule ids, e.g.
        /// "meeting-headphones"). Their buttons still show on hover. Missing (a settings.json from before P20) = none.
        /// Never changed in place: a new list is put (a save from another thread may be reading the old one).
        /// </summary>
        public List<string> QuickActionsHidden { get; set; } = new List<string>();

        public const int MaxQuickActionsHidden = 50;

        /// <summary>Called after loading: null becomes empty; invalid ids and duplicates are dropped; at most 50 kept.</summary>
        internal void NormalizeQuickActions() => QuickActionsHidden = CleanQuickActionsHidden(QuickActionsHidden);

        internal static List<string> CleanQuickActionsHidden(IEnumerable<string> ids) =>
            (ids ?? Enumerable.Empty<string>()).Select(i => i?.Trim()).Where(QuickActionRules.IsValidRuleId)
                .Distinct(StringComparer.Ordinal).Take(MaxQuickActionsHidden).ToList();

        /// <summary>A copy with <paramref name="ruleId"/> hidden too (an invalid id changes nothing).</summary>
        internal List<string> WithQuickActionHidden(string ruleId) =>
            CleanQuickActionsHidden((QuickActionsHidden ?? new List<string>()).Concat(new[] { ruleId }));
    }
}
