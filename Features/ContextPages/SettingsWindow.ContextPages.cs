using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using WinNotch.Core.Context;
using WinNotch.Features.ContextPages;
using WinNotch.Widgets;

namespace WinNotch
{
    /// <summary>P27 in Settings: one row per context category, each with a page or „—” (no change). Applied on "Salvează".</summary>
    public partial class SettingsWindow
    {
        private readonly Dictionary<AppCategory, ComboBox> _contextPageBoxes = new Dictionary<AppCategory, ComboBox>();

        /// <summary>
        /// The rows, from the saved mapping. Every page is offered (a hidden one says so: it is skipped while hidden); a page
        /// that no longer exists shows as „—” and is dropped on save.
        /// </summary>
        private void BuildContextPages()
        {
            ContextPageRows.Children.Clear();
            _contextPageBoxes.Clear();
            var pages = Catalog.Standard.Select(p => (p.Id, Name: p.Name, Hidden: _s.HiddenPages.Contains(p.Id)))
                               .Concat(_s.Pages.Select(p => (p.Id, Name: p.Name, Hidden: p.Hidden))).ToList();
            var map = _s.ContextPages ?? AppSettings.NewContextPages();
            foreach (var cat in ContextPageRules.Categories)
            {
                var g = new Grid { Margin = new Thickness(0, 3, 0, 3) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.Children.Add(new TextBlock { Text = Features.Context.ContextActions.CategoryName(cat), VerticalAlignment = VerticalAlignment.Center });
                var box = new ComboBox();
                box.Items.Add(new ComboBoxItem { Tag = "", Content = "—" });
                foreach (var (id, name, hidden) in pages)
                    box.Items.Add(new ComboBoxItem { Tag = id, Content = hidden ? name + " (ascunsă)" : name });
                map.TryGetValue(ContextPageRules.Key(cat), out var chosen);
                SelectTag(box, chosen ?? "");
                Grid.SetColumn(box, 1);
                g.Children.Add(box);
                ContextPageRows.Children.Add(g);
                _contextPageBoxes[cat] = box;
            }
        }

        /// <summary>What the rows show now, as the new mapping (only the categories with a page).</summary>
        private Dictionary<string, string> ContextPagesChosen()
        {
            var map = AppSettings.NewContextPages();
            foreach (var (cat, box) in _contextPageBoxes)
            {
                string id = TagOf(box);
                if (!string.IsNullOrEmpty(id)) map[ContextPageRules.Key(cat)] = id;
            }
            return AppSettings.CleanContextPages(map);
        }
    }
}
