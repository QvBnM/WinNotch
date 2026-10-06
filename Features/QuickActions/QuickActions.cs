using System;
using System.Collections.Generic;
using System.Linq;
using WinNotch.Core.Actions;
using WinNotch.Core.Context;
using WinNotch.Core.Flags;

namespace WinNotch.Features.QuickActions
{
    /// <summary>One button of a rule: an action of the registry (by id, or the first one whose id starts with a prefix).</summary>
    public sealed class QuickActionRef
    {
        /// <summary>Exact action id ("audio.mute-mic"). Either this or <see cref="Prefix"/>.</summary>
        public string Id { get; init; }
        /// <summary>Actions that come and go ("device.open-" → "device.open-e"): the first usable one, in id order.</summary>
        public string Prefix { get; init; }
        /// <summary>
        /// The rule's condition already says the action can run (a stick is connected: the context engine's UsbDrive), so its
        /// own IsAvailable isn't asked when the row is built (it would read the drives on the UI thread; R1). Its feature
        /// switch is still checked, and the registry checks availability again when it is clicked.
        /// </summary>
        public bool TrustContext { get; init; }
        /// <summary>Short Romanian text on the button; null = the action's title.</summary>
        public string Label { get; init; }
        /// <summary>Fixed parameter values ("valoare" = "40" for "Volum 40%"), checked like any other caller's.</summary>
        public IReadOnlyDictionary<string, string> Args { get => _args; init => _args = value ?? Empty; }
        private readonly IReadOnlyDictionary<string, string> _args = Empty;

        private static readonly IReadOnlyDictionary<string, string> Empty = new Dictionary<string, string>();
    }

    /// <summary>
    /// One row of the table: when the context matches <see cref="When"/>, its actions are offered under the pill. The id is
    /// stable once published (it is the key of „Nu mai arăta” in settings.json, "QuickActionsHidden").
    /// </summary>
    public sealed class QuickActionRule
    {
        /// <summary>Lowercase words with dashes ("meeting-headphones").</summary>
        public string Id { get; init; }
        /// <summary>Romanian, for the tooltip of „Nu mai arăta” and the suggestion ("Întâlnire cu căști").</summary>
        public string Title { get; init; }
        /// <summary>The parts of the context the condition reads (a suggestion is considered only when one of them changes).</summary>
        public ContextField Fields { get; init; }
        /// <summary>The condition, on the context engine's snapshot only (pure; an exception counts as "no").</summary>
        public Func<ContextSnapshot, bool> When { get; init; }
        /// <summary>May be offered unasked (a peek through the Activity Manager), at most one in 10 minutes.</summary>
        public bool Suggest { get; init; }
        /// <summary>2–4 buttons, in order.</summary>
        public IReadOnlyList<QuickActionRef> Actions { get; init; } = Array.Empty<QuickActionRef>();
    }

    /// <summary>A button to draw: the action to start (always through the registry) and its fixed parameters.</summary>
    public sealed class QuickActionItem
    {
        public string ActionId { get; init; }
        public string Label { get; init; }
        /// <summary>The action's full title (tooltip).</summary>
        public string Title { get; init; }
        public string Icon { get; init; }
        public IReadOnlyDictionary<string, string> Args { get; init; }
    }

    /// <summary>The rule that won and its buttons (only usable actions, at least <see cref="QuickActionRules.MinActions"/>).</summary>
    public sealed class QuickActionChoice
    {
        public QuickActionRule Rule { get; init; }
        public IReadOnlyList<QuickActionItem> Items { get; init; }
    }

    /// <summary>What the evaluator needs from the action registry (the real one in <see cref="RegistryQuickActionCatalog"/>, a fake in the tests).</summary>
    public interface IQuickActionCatalog
    {
        ActionDescriptor Get(string id);
        IEnumerable<ActionDescriptor> WithPrefix(string prefix);
        /// <summary>Its feature switch is on (or it has none).</summary>
        bool FeatureOn(ActionDescriptor a);
        /// <summary>It can run now (a song for "pauză"); asked only after <see cref="FeatureOn"/>.</summary>
        bool IsAvailable(ActionDescriptor a);
    }

    /// <summary>The app's registry as a catalog: the same checks the registry makes before running an action.</summary>
    public sealed class RegistryQuickActionCatalog : IQuickActionCatalog
    {
        private readonly ActionRegistry _r;
        private readonly FeatureFlags _flags;

        public RegistryQuickActionCatalog(ActionRegistry registry, FeatureFlags flags = null)
        {
            _r = registry ?? throw new ArgumentNullException(nameof(registry));
            _flags = flags;
        }

        public ActionDescriptor Get(string id) => _r.Get(id);

        public IEnumerable<ActionDescriptor> WithPrefix(string prefix) =>
            string.IsNullOrEmpty(prefix) ? Enumerable.Empty<ActionDescriptor>()
                : _r.All.Where(a => a.Id.StartsWith(prefix, StringComparison.Ordinal)).OrderBy(a => a.Id, StringComparer.Ordinal);

        public bool FeatureOn(ActionDescriptor a)
        {
            if (a == null) return false;
            if (string.IsNullOrEmpty(a.FeatureId)) return true;
            var f = _flags ?? FeatureFlags.Current;
            return f != null && f.IsEnabled(a.FeatureId);
        }

        public bool IsAvailable(ActionDescriptor a)
        {
            if (a == null) return false;
            try { return a.IsAvailable(); } catch { return false; }
        }
    }

    /// <summary>
    /// P20 "Quick Actions" without WPF (ADR 0009): which 2–4 actions go under the pill for what you are doing now, and
    /// when one is suggested unasked. The rules are data (<see cref="Table"/>); the actions come from the registry by id,
    /// and only the usable ones are offered: Safe (never Confirm or Dangerous), allowed for Quick Actions, available now,
    /// their feature on, their parameters given and valid. A rule shows if at least <see cref="MinActions"/> of its actions
    /// is left; the first matching rule of the table wins. Nothing here reads Windows: only the context engine's snapshot.
    /// </summary>
    public static class QuickActionRules
    {
        /// <summary>The feature switch (same id as <see cref="FeatureCatalog.QuickActions"/>; the tests check they match).</summary>
        public const string FeatureId = "quick-actions";

        /// <summary>Id (and key) of the suggestion's activity (a Low peek through the Activity Manager).</summary>
        public const string SuggestionActivityId = "quick-actions";

        public const int MinActions = 1, MaxActions = 4;

        /// <summary>At most one unasked suggestion in this time (any rule).</summary>
        public static readonly TimeSpan SuggestionInterval = TimeSpan.FromMinutes(10);

        /// <summary>Battery under this (on battery, not charging) → the battery saver.</summary>
        public const int LowBattery = 20;

        public const string MeetingHeadphones = "meeting-headphones", BatteryLow = "battery-low", MediaPlaying = "media-playing", UsbDrive = "usb-drive";

        private static QuickActionRef A(string id, string label, string arg = null, string value = null) => new QuickActionRef
        {
            Id = id, Label = label, Args = arg == null ? null : new Dictionary<string, string>(StringComparer.Ordinal) { [arg] = value },
        };

        private static QuickActionRef P(string prefix, string label) => new QuickActionRef { Prefix = prefix, Label = label, TrustContext = true };

        /// <summary>
        /// The rules, most important first (the first that matches and has a usable action wins). To add one: a new row
        /// with a new id, a condition on the snapshot, the fields it reads and 2–4 action ids (tests QA2–QA4 check them).
        /// </summary>
        public static readonly IReadOnlyList<QuickActionRule> Table = new[]
        {
            // a call with headphones on (Bluetooth ones count): mute the microphone, a comfortable volume
            new QuickActionRule
            {
                Id = MeetingHeadphones, Title = "Întâlnire cu căști", Fields = ContextField.Meeting | ContextField.AudioOutput, Suggest = true,
                When = s => s.MeetingActive && (s.AudioOutput == AudioOutputKind.Headphones || s.AudioOutput == AudioOutputKind.Bluetooth),
                Actions = new[] { A("audio.mute-mic", "Mută / pornește microfonul"), A("audio.volume-set", "Volum 40%", "valoare", "40") },
            },
            // the battery alert already warns (20 % / 10 %): no suggestion of our own, only the buttons on hover
            new QuickActionRule
            {
                Id = BatteryLow, Title = "Baterie descărcată", Fields = ContextField.Power, Suggest = false,
                When = s => s.HasBattery && s.OnBattery && s.BatteryPercent >= 0 && s.BatteryPercent < LowBattery,
                Actions = new[] { A("settings.battery-saver", "Economisire"), A("settings.display", "Luminozitate") },
            },
            // songs already have their own alert: no suggestion, only the buttons on hover
            new QuickActionRule
            {
                Id = MediaPlaying, Title = "Se redă ceva", Fields = ContextField.Media, Suggest = false,
                When = s => s.MediaPlaying,
                Actions = new[] { A("media.play-pause", "Pauză"), A("media.next", "Următoarea") },
            },
            // "scoate" needs a confirmation, so it is never a quick action (filtered out); kept here for when it can be undone
            new QuickActionRule
            {
                Id = UsbDrive, Title = "Stick USB conectat", Fields = ContextField.UsbDrive, Suggest = true,
                When = s => s.UsbDriveConnected,
                Actions = new[] { P("device.open-", "Deschide stick-ul"), P("device.eject-", "Scoate") },
            },
        };

        /// <summary>The fields any suggestion rule reads: other changes of the context are ignored right away.</summary>
        public static ContextField SuggestionFields(IReadOnlyList<QuickActionRule> rules = null) =>
            (rules ?? Table).Where(r => r.Suggest).Aggregate(ContextField.None, (f, r) => f | r.Fields);

        public static QuickActionRule Find(string id) => Table.FirstOrDefault(r => r.Id == id);

        /// <summary>The condition, safely: null snapshot or an exception → false.</summary>
        public static bool Matches(QuickActionRule rule, ContextSnapshot s)
        {
            if (rule?.When == null || s == null) return false;
            try { return rule.When(s); } catch { return false; }
        }

        /// <summary>
        /// May this action be a quick action now: Safe only, allowed for <see cref="ActionInvoker.QuickAction"/>, its
        /// parameters known, the required ones given and valid, its feature on and (unless <paramref name="trustContext"/>:
        /// the rule's condition stands for it) available.
        /// </summary>
        public static bool Usable(ActionDescriptor a, IReadOnlyDictionary<string, string> args, IQuickActionCatalog catalog, bool trustContext = false)
        {
            if (a == null || catalog == null) return false;
            if (a.Safety != ActionSafety.Safe) return false;
            if ((a.AllowedInvokers & ActionInvoker.QuickAction) == 0) return false;
            args ??= new Dictionary<string, string>();
            foreach (var k in args.Keys) if (!a.Parameters.Any(p => p.Name == k)) return false;
            foreach (var p in a.Parameters)
            {
                if (!args.TryGetValue(p.Name, out var v)) { if (p.Required) return false; continue; }
                if (!p.TryConvert(v, out _, out _)) return false;
            }
            if (!catalog.FeatureOn(a)) return false;
            return trustContext || catalog.IsAvailable(a);
        }

        /// <summary>The rule's usable buttons, in order, without duplicates, at most <see cref="MaxActions"/>.</summary>
        public static IReadOnlyList<QuickActionItem> Resolve(QuickActionRule rule, IQuickActionCatalog catalog)
        {
            var items = new List<QuickActionItem>();
            if (rule == null || catalog == null) return items;
            foreach (var r in rule.Actions ?? Array.Empty<QuickActionRef>())
            {
                if (r == null || items.Count >= MaxActions) continue;
                IEnumerable<ActionDescriptor> candidates = !string.IsNullOrEmpty(r.Id) ? new[] { catalog.Get(r.Id) }
                    : catalog.WithPrefix(r.Prefix) ?? Enumerable.Empty<ActionDescriptor>();
                var a = candidates.FirstOrDefault(c => c != null && items.All(i => i.ActionId != c.Id) && Usable(c, r.Args, catalog, r.TrustContext));
                if (a == null) continue;
                // a prefixed one (one per drive) shows its own title: it names the drive
                string label = r.Prefix != null && string.IsNullOrEmpty(r.Id) ? a.Title : r.Label ?? a.Title;
                items.Add(new QuickActionItem { ActionId = a.Id, Label = label, Title = a.Title, Icon = a.Icon, Args = r.Args });
            }
            return items;
        }

        /// <summary>The first rule (table order) that matches and keeps at least <see cref="MinActions"/> usable actions.</summary>
        public static QuickActionChoice Choose(ContextSnapshot s, IQuickActionCatalog catalog, IReadOnlyList<QuickActionRule> rules = null)
        {
            if (s == null || catalog == null) return null;
            foreach (var rule in rules ?? Table)
            {
                if (!Matches(rule, s)) continue;
                var items = Resolve(rule, catalog);
                if (items.Count >= MinActions) return new QuickActionChoice { Rule = rule, Items = items };
            }
            return null;
        }

        /// <summary>
        /// On hover / open: the switch, then the snapshot (null or Empty — engine off, --safe-mode — matches nothing), then
        /// <see cref="Choose"/>. Null = no row. „Nu mai arăta” doesn't apply here: it is about unasked suggestions.
        /// </summary>
        public static QuickActionChoice ForHover(bool enabled, ContextSnapshot s, IQuickActionCatalog catalog, IReadOnlyList<QuickActionRule> rules = null) =>
            enabled ? Choose(s, catalog, rules) : null;

        /// <summary>
        /// The rule to suggest unasked after a change of the context, ignoring the 10-minute limit (see
        /// <see cref="QuickActionSuggester"/>): the switch and the Activity Manager on (suggestions are only its peeks), a
        /// rule marked <see cref="QuickActionRule.Suggest"/> whose fields changed, that matches now and didn't before, not
        /// hidden with „Nu mai arăta”, with a usable action. Null = nothing to suggest.
        /// </summary>
        public static QuickActionChoice NewlyMatching(bool enabled, bool activityManagerOn, ContextSnapshot old, ContextSnapshot now, ContextField fields,
                                                     IQuickActionCatalog catalog, ICollection<string> hidden, IReadOnlyList<QuickActionRule> rules = null)
        {
            if (!enabled || !activityManagerOn || now == null || catalog == null) return null;
            old ??= ContextSnapshot.Empty;
            foreach (var rule in rules ?? Table)
            {
                if (!rule.Suggest || (rule.Fields & fields) == 0) continue;
                if (hidden != null && rule.Id != null && hidden.Contains(rule.Id)) continue;
                if (!Matches(rule, now) || Matches(rule, old)) continue;
                var items = Resolve(rule, catalog);
                if (items.Count >= MinActions) return new QuickActionChoice { Rule = rule, Items = items };
            }
            return null;
        }

        /// <summary>
        /// A suggestion shown at <paramref name="lastUtc"/> blocks the next until 10:00 later (9:59 no, 10:00 yes). A clock
        /// set back keeps blocking (fewer interruptions is the safe side).
        /// </summary>
        public static bool IntervalOver(DateTime? lastUtc, DateTime nowUtc) => lastUtc is not DateTime at || nowUtc - at >= SuggestionInterval;

        /// <summary>The peek's text (Romanian, no personal data: the rule's title only).</summary>
        public static string SuggestionTitle(QuickActionRule rule) => (rule?.Title ?? "Quick Actions") + " · acțiuni rapide la hover";

        /// <summary>
        /// The click's result for the row: the action's sentence on one line, cut to <paramref name="max"/> characters (with
        /// "…"); empty → "Gata" / "Nu a mers".
        /// </summary>
        public static string ShortMessage(string message, bool success, int max = 60)
        {
            string m = (message ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
            if (m.Length == 0) m = success ? "Gata" : "Nu a mers";
            if (max < 2) max = 2;
            return m.Length <= max ? m : m.Substring(0, max - 1).TrimEnd() + "…";
        }

        /// <summary>Rule ids as stored for „Nu mai arăta”: lowercase words with dashes, at most 40 characters.</summary>
        public static bool IsValidRuleId(string id) =>
            !string.IsNullOrEmpty(id) && id.Length <= 40 && System.Text.RegularExpressions.Regex.IsMatch(id, "^[a-z0-9]+(-[a-z0-9]+)*$");
    }

    public enum SuggestionOutcome { None, Suggest, TooSoon }

    public static class QuickActionSuggestions
    {
        /// <summary>
        /// The peek is on screen (Shown) or replaced one in place (Updated): the 10 minutes start. Queued, Grouped (folded
        /// into „N noutăți”) or Dropped: nothing was seen yet, the limit isn't used up (R1).
        /// </summary>
        public static bool ConsumesInterval(Core.Activity.PostResult r) =>
            r == Core.Activity.PostResult.Shown || r == Core.Activity.PostResult.Updated;
    }

    /// <summary>
    /// The unasked suggestions' memory: when the last one was shown. Clock injected (UTC; the tests drive it). UI thread
    /// only (the notch calls it from the Dispatcher). In memory: after a restart the first change may suggest again.
    /// </summary>
    public sealed class QuickActionSuggester
    {
        private readonly Func<DateTime> _utcNow;

        public QuickActionSuggester(Func<DateTime> utcNow = null) { _utcNow = utcNow ?? (() => DateTime.UtcNow); }

        public DateTime? LastShownUtc { get; private set; }

        /// <summary>What to do with this change: nothing, suggest <paramref name="choice"/>, or wait (one in 10 minutes).</summary>
        public SuggestionOutcome Consider(bool enabled, bool activityManagerOn, ContextSnapshot old, ContextSnapshot now, ContextField fields,
                                          IQuickActionCatalog catalog, ICollection<string> hidden, out QuickActionChoice choice,
                                          IReadOnlyList<QuickActionRule> rules = null)
        {
            choice = QuickActionRules.NewlyMatching(enabled, activityManagerOn, old, now, fields, catalog, hidden, rules);
            if (choice == null) return SuggestionOutcome.None;
            return QuickActionRules.IntervalOver(LastShownUtc, _utcNow()) ? SuggestionOutcome.Suggest : SuggestionOutcome.TooSoon;
        }

        /// <summary>The suggestion was really posted (not dropped): the 10 minutes start now.</summary>
        public void Shown() => LastShownUtc = _utcNow();
    }
}
