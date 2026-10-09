using System;
using System.Collections.Generic;
using System.Linq;

namespace WinNotch.Features.WindowV2
{
    /// <summary>One entry of the sidebar: a group of things the user can do (only what exists today).</summary>
    public sealed class V2Category
    {
        public V2Category(string id, string title, string glyph, params string[] actionCategories)
        {
            Id = id; Title = title; Glyph = glyph; ActionCategories = actionCategories ?? Array.Empty<string>();
        }

        /// <summary>Stable id (lowercase, never renamed once shipped).</summary>
        public string Id { get; }
        /// <summary>Shown in the sidebar (Romanian).</summary>
        public string Title { get; }
        /// <summary>A glyph from the app's icon font.</summary>
        public string Glyph { get; }
        /// <summary>Which action categories (from the registry) belong here; empty: the category is not built from actions.</summary>
        public IReadOnlyList<string> ActionCategories { get; }
    }

    /// <summary>
    /// P52: the layout of the WinNotch window, version 2 — pure rules only (no WPF): how many card columns fit a width,
    /// when the right column and the sidebar step aside, which sidebar category a page or an action belongs to.
    /// The window itself is in <c>Features/WindowV2/WindowV2.cs</c>; with the switch off the old window opens.
    /// </summary>
    public static class LayoutRules
    {
        /// <summary>Same id as the entry in FeatureCatalog (the tests check they match).</summary>
        public const string FeatureId = "window-v2";

        public const double SidebarWidth = 240, RightWidth = 300, MinWidth = 900, MinHeight = 600;

        /// <summary>Cards per row: 4 over 1280, 3 over 900, 2 below (one when the window is narrower than the sidebar plus a card).</summary>
        public static int Columns(double width)
        {
            if (width >= 1280) return 4;
            if (width >= 900) return 3;
            if (width >= 620) return 2;
            return 1;
        }

        /// <summary>The right column (clipboard, last capture, privacy) only fits from 1100 px up.</summary>
        public static bool ShowRightColumn(double width) => width >= 1100;

        /// <summary>Under the minimum width everything stacks in one column and the sidebar becomes a row of chips.</summary>
        public static bool SingleColumn(double width) => width < MinWidth;

        /// <summary>The sidebar, in order. Only groups that exist today; new ones are added at the end, without rearranging.</summary>
        public static readonly IReadOnlyList<V2Category> Categories = new[]
        {
            new V2Category("actiuni", "Acțiuni", "", "Acțiuni", "Fereastră"),
            new V2Category("sistem", "Sistem", "\uE713", "Sistem"),
            new V2Category("clipboard", "Clipboard", "", "Clipboard"),
            new V2Category("captura", "Captură", "", "Captură"),
            new V2Category("sunet", "Sunet", "", "Sunet"),
            new V2Category("pagini", "Pagini", ""),
            new V2Category("teme", "Teme", ""),
            new V2Category("setari", "Setări", ""),
            new V2Category("noutati", "Noutăți", ""),
        };

        public static V2Category Find(string id) => Categories.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.Ordinal));

        /// <summary>The first category, shown when the window opens with nothing asked for.</summary>
        public static string DefaultCategory => Categories[0].Id;

        /// <summary>
        /// Which sidebar category a request opens: the old window's ids ("themes", "settings", "news") and a page id
        /// (anything else) keep working, so every entry point into the window still lands somewhere sensible.
        /// </summary>
        public static string CategoryFor(string pageId) => pageId switch
        {
            null or "" => DefaultCategory,
            "themes" => "teme",
            "settings" => "setari",
            "news" => "noutati",
            _ => "pagini",
        };

        /// <summary>Which category an action from the registry belongs to (its category name); unknown ones go to „Acțiuni”.</summary>
        public static string CategoryForAction(string actionCategory)
        {
            if (string.IsNullOrEmpty(actionCategory)) return "actiuni";
            var hit = Categories.FirstOrDefault(c => c.ActionCategories.Any(a => string.Equals(a, actionCategory, StringComparison.OrdinalIgnoreCase)));
            return hit?.Id ?? "actiuni";
        }

        /// <summary>Every category whose body is a page of its own (settings, news, themes, pages) rather than cards.</summary>
        public static bool IsPageContent(string categoryId) =>
            categoryId is "pagini" or "teme" or "setari" or "noutati";

        /// <summary>
        /// The pages whose content now lives in this window, built by the same code the classic window uses. The list
        /// grows one page at a time (settings first, as the brief asks); the rest still open the classic window.
        /// </summary>
        public static bool IsEmbeddedContent(string categoryId) =>
            categoryId is "setari" or "noutati";

        /// <summary>Categories that still show the old window's own pages (until their content moves here too).</summary>
        public static bool IsClassicContent(string categoryId) =>
            IsPageContent(categoryId) && !IsEmbeddedContent(categoryId);

        /// <summary>Geometry of the header-notch and of the cards (the brief's scale: 4 / 8 / 12 / 16 / 24 / 32).</summary>
        public const double HeaderHeight = 52, CardRadius = 18, ChipRadius = 12, Gap = 12, Pad = 24;

        /// <summary>The hint in the bottom bar: one line, and the shortcut that goes with it.</summary>
        public static string Hint(bool commandBarOn) => commandBarOn
            ? "Scrie ce vrei să faci, de exemplu „volum 30” sau „captură”."
            : "Pornește Command Bar-ul din Setări → funcții noi ca să scrii ce vrei să faci.";
    }
}
