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

    /// <summary>One section of the Setări tab: a group of options, listed in that tab's own column on the left.</summary>
    public sealed class V2Section
    {
        public V2Section(string id, string title, string glyph)
        {
            Id = id; Title = title; Glyph = glyph;
        }

        public string Id { get; }
        public string Title { get; }
        public string Glyph { get; }
    }

    /// <summary>
    /// P52: the layout of the WinNotch window — pure rules only (no WPF).
    /// <para>The window has <b>one</b> navigation: four tabs in the header (Workspace, Widgeturi, Teme, Setări). The
    /// column on the left is not a second navigation: it belongs to the open tab and changes with it (the pages and the
    /// icon in Workspace, the groups of options in Setări, nothing in the other two). The column on the right is the
    /// inspector of whatever is selected, and only Workspace has one. That is the whole structure.</para>
    /// The window itself is in <c>Features/WindowV2/</c>; with the switch off the old window opens.
    /// </summary>
    public static class LayoutRules
    {
        /// <summary>Same id as the entry in FeatureCatalog (the tests check they match).</summary>
        public const string FeatureId = "window-v2";

        public const double LeftWidth = 212, InspectorWidth = 300, MinWidth = 900, MinHeight = 600;

        public const string Workspace = "workspace", Widgets = "widgeturi", Themes = "teme", Settings = "setari";
        /// <summary>P60: the Performanță tab. Not one of the four: it exists only while its own switch is on.</summary>
        public const string Performance = "performanta";

        /// <summary>The header's tabs, in order. Four areas; nothing else navigates.</summary>
        public static readonly IReadOnlyList<V2Tab> Tabs = new[]
        {
            new V2Tab(Workspace, "Workspace", ""),
            new V2Tab(Widgets, "Widgeturi", ""),
            new V2Tab(Themes, "Teme", ""),
            new V2Tab(Settings, "Setări", ""),
        };

        /// <summary>
        /// P60: the fifth tab, which is not always there. The window's rule is "one navigation", not "exactly four
        /// doors": a whole area of the app earns a tab, and a switched-off area must not leave an empty one behind. So
        /// Performanță is listed only while its switch is on, and <see cref="Tabs"/> stays the permanent four.
        /// </summary>
        public static readonly V2Tab PerformanceTab = new V2Tab(Performance, "Performanță", "\uE9D9");

        /// <summary>The tabs to draw, in order: the permanent four, plus Performanță when its switch is on.</summary>
        public static IReadOnlyList<V2Tab> TabsFor(bool performance)
        {
            if (!performance) return Tabs;
            var list = new List<V2Tab>(Tabs);
            list.Add(PerformanceTab);
            return list;
        }

        public static V2Tab FindTab(string id) =>
            string.Equals(id, Performance, StringComparison.Ordinal) ? PerformanceTab
            : Tabs.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.Ordinal));

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
            "settings" or "news" => Settings,
            "performance" or Performance => Performance,
            _ => Workspace,
        };

        /// <summary>Inside Setări, the section a request opens (the news keeps its own entry point).</summary>
        public static string SectionFor(string pageId) => pageId switch
        {
            "news" => "noutati",
            _ => Sections[0].Id,
        };

        /// <summary>
        /// The contextual panel of the Setări tab: the groups the options are split into, in the order they matter.
        /// They are sections of one page, not a second navigation of the window.
        /// </summary>
        public static readonly IReadOnlyList<V2Section> Sections = new[]
        {
            new V2Section("notch", "Notch", ""),
            new V2Section("standby", "Standby", ""),
            new V2Section("acasa", "Acasă și sănătate", ""),
            new V2Section("browser", "Browser", ""),
            new V2Section("sistem", "Sistem", ""),
            new V2Section("spatii", "Spații de lucru", ""),
            new V2Section("context", "Pagina după context", ""),
            new V2Section("functii", "Funcții noi", ""),
            new V2Section("actiuni", "Acțiuni", ""),
            new V2Section("noutati", "Noutăți", ""),
        };

        public static V2Section FindSection(string id) => Sections.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.Ordinal));

        /// <summary>The tab's own column on the left: the pages in Workspace, the sections in Setări, nothing elsewhere.</summary>
        public static bool HasLeftPanel(string tabId) => tabId == Workspace || tabId == Settings;

        /// <summary>Only the workspace has something to inspect (the selected widget).</summary>
        public static bool HasInspector(string tabId) => tabId == Workspace;

        /// <summary>Under this width the left panel folds away so the content keeps its room.</summary>
        public static bool ShowLeftPanel(string tabId, double width) => HasLeftPanel(tabId) && width >= MinWidth;

        /// <summary>The inspector needs the width of the panel plus a usable page beside it.</summary>
        public static bool ShowInspector(string tabId, double width) => HasInspector(tabId) && width >= 1120;

        /// <summary>Cards per row in the Acțiuni section: 3 over 1280, 2 over 900, 1 below.</summary>
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
            : "Pornește Command Bar-ul din Setări → Funcții noi ca să scrii ce vrei să faci.";
    }
}
