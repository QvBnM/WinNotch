using System;
using System.Collections.Generic;
using System.Linq;

namespace WinNotch.Features.WindowV2
{
    /// <summary>One of the window's four tabs: a whole area of the app, not a list of links.</summary>
    public sealed class V2Tab
    {
        public V2Tab(string id, string title, string glyph)
        {
            Id = id; Title = title; Glyph = glyph;
        }

        /// <summary>Stable id (lowercase, never renamed once shipped).</summary>
        public string Id { get; }
        /// <summary>Shown in the header (Romanian).</summary>
        public string Title { get; }
        /// <summary>A glyph from the app's icon font.</summary>
        public string Glyph { get; }
    }

    /// <summary>One entry of the contextual panel on the left (only inside the tab that owns it).</summary>
    public sealed class V2Section
    {
        public V2Section(string id, string title, string glyph, params string[] actionCategories)
        {
            Id = id; Title = title; Glyph = glyph; ActionCategories = actionCategories ?? Array.Empty<string>();
        }

        public string Id { get; }
        public string Title { get; }
        public string Glyph { get; }
        /// <summary>Which action categories (from the registry) belong here; empty: the section is not built from actions.</summary>
        public IReadOnlyList<string> ActionCategories { get; }
    }

    /// <summary>
    /// P52: the layout of the WinNotch window — pure rules only (no WPF).
    /// <para>The window has <b>one</b> navigation: four tabs in the header (Workspace, Widgeturi, Teme, Sistem). The
    /// column on the left is not a second navigation: it belongs to the open tab and changes with it (the pages and the
    /// icon in Workspace, the sections in Sistem, nothing in the other two). The column on the right is the inspector of
    /// whatever is selected, and only Workspace has one. That is the whole structure.</para>
    /// The window itself is in <c>Features/WindowV2/</c>; with the switch off the old window opens.
    /// </summary>
    public static class LayoutRules
    {
        /// <summary>Same id as the entry in FeatureCatalog (the tests check they match).</summary>
        public const string FeatureId = "window-v2";

        public const double LeftWidth = 212, InspectorWidth = 300, MinWidth = 900, MinHeight = 600;

        public const string Workspace = "workspace", Widgets = "widgeturi", Themes = "teme", System = "sistem";

        /// <summary>The header's tabs, in order. Four areas; nothing else navigates.</summary>
        public static readonly IReadOnlyList<V2Tab> Tabs = new[]
        {
            new V2Tab(Workspace, "Workspace", ""),
            new V2Tab(Widgets, "Widgeturi", ""),
            new V2Tab(Themes, "Teme", ""),
            new V2Tab(System, "Sistem", ""),
        };

        public static V2Tab FindTab(string id) => Tabs.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.Ordinal));

        /// <summary>The tab the window opens on when nothing is asked for.</summary>
        public static string DefaultTab => Tabs[0].Id;

        /// <summary>
        /// Which tab a request opens: the old window's ids ("themes", "settings", "news") and a page id (anything else)
        /// keep working, so every entry point into the window still lands somewhere sensible.
        /// </summary>
        public static string TabFor(string pageId) => pageId switch
        {
            null or "" => DefaultTab,
            "themes" => Themes,
            "settings" or "news" => System,
            _ => Workspace,
        };

        /// <summary>Inside Sistem, the section a request opens (the settings and the news keep their own entry points).</summary>
        public static string SectionFor(string pageId) => pageId switch
        {
            "settings" => "setari",
            "news" => "noutati",
            _ => Sections[0].Id,
        };

        /// <summary>The contextual panel of the Sistem tab. Only groups that exist today.</summary>
        public static readonly IReadOnlyList<V2Section> Sections = new[]
        {
            new V2Section("actiuni", "Acțiuni", "", "Acțiuni", "Fereastră"),
            new V2Section("sistem", "Sistem", "", "Sistem"),
            new V2Section("clipboard", "Clipboard", "", "Clipboard"),
            new V2Section("captura", "Captură", "", "Captură"),
            new V2Section("sunet", "Sunet", "", "Sunet"),
            new V2Section("setari", "Setări", ""),
            new V2Section("noutati", "Noutăți", ""),
        };

        public static V2Section FindSection(string id) => Sections.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.Ordinal));

        /// <summary>Which section an action from the registry belongs to; an unknown one goes to „Acțiuni”.</summary>
        public static string SectionForAction(string actionCategory)
        {
            if (string.IsNullOrEmpty(actionCategory)) return "actiuni";
            var hit = Sections.FirstOrDefault(s => s.ActionCategories.Any(a => string.Equals(a, actionCategory, StringComparison.OrdinalIgnoreCase)));
            return hit?.Id ?? "actiuni";
        }

        /// <summary>Sections that are a page of their own (built by the same code the classic window uses).</summary>
        public static bool IsPageSection(string sectionId) => sectionId is "setari" or "noutati";

        /// <summary>The tab's own column on the left: the pages in Workspace, the sections in Sistem, nothing elsewhere.</summary>
        public static bool HasLeftPanel(string tabId) => tabId == Workspace || tabId == System;

        /// <summary>Only the workspace has something to inspect (the selected widget).</summary>
        public static bool HasInspector(string tabId) => tabId == Workspace;

        /// <summary>Under this width the left panel folds away so the content keeps its room.</summary>
        public static bool ShowLeftPanel(string tabId, double width) => HasLeftPanel(tabId) && width >= MinWidth;

        /// <summary>The inspector needs the width of the panel plus a usable page beside it.</summary>
        public static bool ShowInspector(string tabId, double width) => HasInspector(tabId) && width >= 1120;

        /// <summary>Cards per row in the Sistem tab: 3 over 1280, 2 over 900, 1 below.</summary>
        public static int Columns(double width)
        {
            if (width >= 1280) return 3;
            if (width >= 900) return 2;
            return 1;
        }

        /// <summary>Geometry and spacing (the brief's scale: 4 / 8 / 12 / 16 / 24 / 32).</summary>
        public const double HeaderHeight = 56, CardRadius = 18, ChipRadius = 12, Gap = 12, Pad = 24;

        /// <summary>The line in the bottom bar, beside the command field.</summary>
        public static string Hint(bool commandBarOn) => commandBarOn
            ? "Scrie ce vrei să faci, de exemplu „volum 30” sau „captură”."
            : "Pornește Command Bar-ul din Sistem → Setări → funcții noi ca să scrii ce vrei să faci.";
    }
}
