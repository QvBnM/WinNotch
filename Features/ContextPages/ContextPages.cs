using System;
using System.Collections.Generic;
using System.Linq;
using WinNotch.Core.Context;

namespace WinNotch.Features.ContextPages
{
    /// <summary>
    /// P27 "Pagina după context", without WPF (ADR 0008): which page the notch opens on, from the context engine's snapshot
    /// and the user's mapping category → page. The notch only asks, once, when it opens; nothing here reads Windows.
    /// </summary>
    public static class ContextPageRules
    {
        /// <summary>The feature switch (same id as <see cref="Core.Flags.FeatureCatalog.ContextPages"/>; the tests check they match).</summary>
        public const string FeatureId = "context-pages";

        /// <summary>A page you pick yourself in the notch is kept this long: until then the context doesn't move you.</summary>
        public static readonly TimeSpan ManualHold = TimeSpan.FromMinutes(10);

        /// <summary>The categories you can map, in the order Settings shows them (every AppCategory except Other).</summary>
        public static readonly IReadOnlyList<AppCategory> Categories = new[]
        {
            AppCategory.Dev, AppCategory.Browser, AppCategory.Meeting, AppCategory.Game, AppCategory.Media, AppCategory.Office, AppCategory.Creator,
        };

        /// <summary>Key of a category in settings.json ("ContextPages"): the enum name ("Dev"), stable once shipped.</summary>
        public static string Key(AppCategory c) => c.ToString();

        /// <summary>"Dev" / "dev" → Dev; Other, unknown or empty → null.</summary>
        public static AppCategory? ParseKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return null;
            foreach (var c in Categories)
                if (string.Equals(Key(c), key.Trim(), StringComparison.OrdinalIgnoreCase)) return c;
            return null;
        }

        /// <summary>
        /// What the user is doing, as one category: a meeting going on wins (also a Meet call in a browser), then a game on
        /// the whole screen (an exclusive Direct3D game the table doesn't know), then the category of the app in front.
        /// Empty snapshot (engine off, --safe-mode, nothing read yet) → Other.
        /// </summary>
        public static AppCategory EffectiveCategory(ContextSnapshot s)
        {
            if (s == null) return AppCategory.Other;
            if (s.MeetingActive) return AppCategory.Meeting;
            if (s.Fullscreen == FullscreenKind.Game) return AppCategory.Game;
            return s.ForegroundCategory;
        }

        /// <summary>A manual choice made at <paramref name="lastManualUtc"/> still holds at <paramref name="nowUtc"/> (9:59 yes, 10:00 no).</summary>
        public static bool ManualChoiceHolds(DateTime? lastManualUtc, DateTime nowUtc) =>
            lastManualUtc is DateTime at && nowUtc - at < ManualHold;

        /// <summary>
        /// The page for <paramref name="category"/>, or null for "no change" (the notch opens where it was): Other, no
        /// mapping or „—” for it, a page that is hidden or deleted (not in <paramref name="availablePages"/>; ignored
        /// silently), or a page picked by hand less than <see cref="ManualHold"/> ago.
        /// </summary>
        public static string Resolve(AppCategory category, IReadOnlyDictionary<string, string> mapping, IEnumerable<string> availablePages,
                                     DateTime? lastManualUtc, DateTime nowUtc)
        {
            if (category == AppCategory.Other || mapping == null || availablePages == null) return null;
            if (ManualChoiceHolds(lastManualUtc, nowUtc)) return null;
            if (!mapping.TryGetValue(Key(category), out var page) || string.IsNullOrWhiteSpace(page)) return null;
            return availablePages.Contains(page, StringComparer.Ordinal) ? page : null;
        }

        /// <summary>The whole decision at open time: the switch, then the snapshot's category, then <see cref="Resolve"/>.</summary>
        public static string Choose(bool enabled, ContextSnapshot snapshot, IReadOnlyDictionary<string, string> mapping, IEnumerable<string> availablePages,
                                    DateTime? lastManualUtc, DateTime nowUtc) =>
            enabled ? Resolve(EffectiveCategory(snapshot), mapping, availablePages, lastManualUtc, nowUtc) : null;
    }

    /// <summary>
    /// The notch's state for P27: when you last picked a page by hand. The clock is injected (UTC; the tests drive it).
    /// UI thread only (the notch calls it from its tab clicks and from Expand).
    /// </summary>
    public sealed class ContextPageChooser
    {
        private readonly Func<DateTime> _utcNow;

        public ContextPageChooser(Func<DateTime> utcNow = null) { _utcNow = utcNow ?? (() => DateTime.UtcNow); }

        /// <summary>When a page was last picked by hand (null = never since start).</summary>
        public DateTime? LastManualUtc { get; private set; }

        /// <summary>You switched the page yourself in the notch: the context leaves it alone for <see cref="ContextPageRules.ManualHold"/>.</summary>
        public void ManualChoice() => LastManualUtc = _utcNow();

        /// <summary>The page to open on now, or null for "no change".</summary>
        public string OnOpen(bool enabled, ContextSnapshot snapshot, IReadOnlyDictionary<string, string> mapping, IEnumerable<string> availablePages) =>
            ContextPageRules.Choose(enabled, snapshot, mapping, availablePages, LastManualUtc, _utcNow());
    }
}
