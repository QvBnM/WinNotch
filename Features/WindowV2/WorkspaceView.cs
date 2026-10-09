using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WinNotch.Panes;
using WinNotch.Widgets;

namespace WinNotch.Features.WindowV2
{
    /// <summary>
    /// P52, the Workspace tab: the page you are building. The column on the left is this tab's own — your pages and,
    /// under them, the icon of the page you are on; the middle is the live page and the widget manager; the right is
    /// the inspector of the selected widget. Nothing here is a second navigation.
    /// <para>The page itself is the real one (<see cref="WidgetPage"/>, the same control the notch shows) and the
    /// widget manager is the real gallery (<see cref="Gallery"/>) — both already draw in the app's theme, so this view
    /// only arranges them and writes the changes through the same <c>Commit</c> as before.</para>
    /// </summary>
    internal sealed class WorkspaceView : Grid
    {
        private readonly AppSettings _s;
        private readonly NotchWindow _notch;
        private readonly Func<Window> _owner;
        private readonly Action<Action> _soon;
        private readonly Action _flush;
        private readonly Action<string> _say;                 // a line in the bottom bar

        private readonly StackPanel _left = new StackPanel();
        private readonly StackPanel _centre = new StackPanel();
        private readonly Border _leftHost, _centreHost;
        private readonly WidgetInspector _inspector;
        private readonly Grid _overlay = new Grid();          // the size pop-up, over the whole view

        private string _sel;                                  // a page id, or "std:<id>"
        private WidgetPage _page;
        private Gallery _gallery;
        /// <summary>P51: the open pop-up (a widget's sizes), so Esc closes it first.</summary>
        private (Action Hide, Action Close) _popup;

        internal WorkspaceView(AppSettings s, NotchWindow notch, Func<Window> owner, Action<Action> soon, Action flush, Action<string> say)
        {
            _s = s; _notch = notch; _owner = owner; _soon = soon; _flush = flush; _say = say;
            _inspector = new WidgetInspector(soon, flush, owner);

            ColumnDefinitions.Add(new ColumnDefinition { Width = Ui.Px(LayoutRules.LeftWidth) });
            ColumnDefinitions.Add(new ColumnDefinition { Width = Ui.Star() });
            ColumnDefinitions.Add(new ColumnDefinition { Width = Ui.Px(LayoutRules.InspectorWidth) });

            _leftHost = new Border
            {
                Padding = new Thickness(LayoutRules.Gap, LayoutRules.Gap, LayoutRules.Gap, LayoutRules.Pad),
                Child = new ScrollViewer { Style = Ui.S("SlimScroll"), Content = _left },
            };
            _leftHost.SetResourceReference(Border.BackgroundProperty, "ChipBrush");
            this.Put(_leftHost);

            _centreHost = new Border
            {
                Padding = new Thickness(LayoutRules.Pad, LayoutRules.Pad, LayoutRules.Pad, LayoutRules.Pad),
                Child = new ScrollViewer { Style = Ui.S("SlimScroll"), Content = _centre },
            };
            this.Put(_centreHost, 1);
            this.Put(_inspector, 2);

            System.Windows.Controls.Panel.SetZIndex(_overlay, 50);
            SetColumnSpan(_overlay, 3);
            Children.Add(_overlay);
        }

        /// <summary>The page the user is editing, or null while a standard page is shown.</summary>
        internal WidgetPage Page => _page;

        /// <summary>P51: Esc closes the size pop-up before anything else. True when something was closed.</summary>
        internal bool CloseOpenPopup()
        {
            if (_popup.Close == null) return false;
            _popup.Close();
            return true;
        }

        /// <summary>Opens a page (one of yours, or "std:&lt;id&gt;"), optionally with one of its widgets selected.</summary>
        internal void Open(string pageId, string slotId)
        {
            if (pageId != null && _s.Pages.Any(p => p.Id == pageId)) _sel = pageId;
            else if (pageId != null && Catalog.Standard.Any(x => x.Id == pageId)) _sel = "std:" + pageId;
            else if (_sel == null || (!_sel.StartsWith("std:", StringComparison.Ordinal) && !_s.Pages.Any(p => p.Id == _sel)))
                _sel = _s.Pages.FirstOrDefault()?.Id ?? "std:" + Catalog.Standard.First().Id;
            BuildLeft();
            BuildCentre(slotId);
        }

        /// <summary>A page was changed somewhere else (the notch): rebuild what is on screen.</summary>
        internal void PageChangedElsewhere(string pageId)
        {
            if (_sel != pageId || _page == null) return;
            var keep = _page.SelectedSlot != null && _page.Page.Widgets.Contains(_page.SelectedSlot) ? _page.SelectedSlot.Id : null;
            _flush();
            BuildCentre(keep);
        }

        internal void PagesChangedElsewhere()
        {
            BuildLeft();
            if (_sel != null && _sel.StartsWith("std:", StringComparison.Ordinal)) BuildCentre(null);
        }

        /// <summary>The left panel folds away on a narrow window; the inspector too, a little later.</summary>
        internal void Relayout(double width)
        {
            bool left = LayoutRules.ShowLeftPanel(LayoutRules.Workspace, width);
            ColumnDefinitions[0].Width = left ? Ui.Px(LayoutRules.LeftWidth) : new GridLength(0);
            _leftHost.Visibility = left ? Visibility.Visible : Visibility.Collapsed;
            bool insp = LayoutRules.ShowInspector(LayoutRules.Workspace, width);
            ColumnDefinitions[2].Width = insp ? Ui.Px(LayoutRules.InspectorWidth) : new GridLength(0);
            _inspector.Visibility = insp ? Visibility.Visible : Visibility.Collapsed;
        }

        // ------------------------------------------------------------------ left: the pages, then this page's icon

        private int VisibleCount => Catalog.Standard.Count(x => !_s.HiddenPages.Contains(x.Id)) + _s.Pages.Count(p => !p.Hidden);

        private void BuildLeft()
        {
            _left.Children.Clear();
            _left.Children.Add(Ui.Cap("PAGINILE MELE"));
            if (_s.Pages.Count == 0)
            {
                var none = Ui.T("Nicio pagină încă. Fă una goală sau copiază una standard.", 12, "DimBrush");
                none.TextWrapping = TextWrapping.Wrap;
                none.Margin = new Thickness(4, 0, 4, 6);
                _left.Children.Add(none);
            }
            for (int i = 0; i < _s.Pages.Count; i++)
            {
                var pg = _s.Pages[i];
                _left.Children.Add(PageRow(NotchWindow.PageGlyph(pg.Icon), pg.Name, !pg.Hidden, _sel == pg.Id,
                                           () => Select(pg.Id), () => ToggleMine(pg), null));
            }
            var add = Ui.PillBtn("+ Pagină goală", () => NewPage(new List<WidgetSlot>(), null, "star"), true);
            add.HorizontalAlignment = HorizontalAlignment.Stretch;
            add.Margin = new Thickness(0, 6, 0, 0);
            _left.Children.Add(add);

            _left.Children.Add(Ui.Cap("STANDARD"));
            foreach (var (id, name, icon) in Catalog.Standard)
            {
                var sid = id;
                bool visible = !_s.HiddenPages.Contains(id);
                _left.Children.Add(PageRow(NotchWindow.PageGlyph(icon), name, visible, _sel == "std:" + id,
                                           () => Select("std:" + sid), () => ToggleStandard(sid), () => DuplicateStandard(sid)));
            }

            if (CurrentPage() is UserPage cur) _left.Children.Add(IconPicker(cur));
        }

        private UserPage CurrentPage() => _sel != null && !_sel.StartsWith("std:", StringComparison.Ordinal)
            ? _s.Pages.FirstOrDefault(p => p.Id == _sel) : null;

        private UIElement PageRow(string glyph, string name, bool visible, bool on, Action open, Action toggle, Action duplicate)
        {
            var mark = new Border { Width = 3, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 2, 6, 2) };
            if (on) mark.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
            var row = Ui.Cols(Ui.Px(3), Ui.Auto, Ui.Star(), Ui.Auto);
            row.Put(mark);
            row.Put(Ui.Icon(glyph, 13, Ui.B(visible ? (on ? "InkBrush" : "MutedBrush") : "DimBrush")), 1);
            var label = Ui.T(name, 13, visible ? (on ? "InkBrush" : "MutedBrush") : "DimBrush", on);
            label.Margin = new Thickness(8, 0, 6, 0);
            label.TextTrimming = TextTrimming.CharacterEllipsis;
            label.VerticalAlignment = VerticalAlignment.Center;
            row.Put(label, 2);

            var tools = Ui.H(2);
            tools.Children.Add(Ui.IconBtn(visible ? "" : "", toggle, visible ? "Ascunde din notch" : "Arată în notch", 24, 11));
            if (duplicate != null) tools.Children.Add(Ui.IconBtn("", duplicate, "Duplică: copia o poți modifica", 24, 11));
            row.Put(tools, 3);

            var host = new Button
            {
                Style = Ui.S("NavButton"), Content = row, Padding = new Thickness(4, 6, 4, 6),
                Cursor = Cursors.Hand, Margin = new Thickness(0, 1, 0, 1),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
            };
            System.Windows.Automation.AutomationProperties.SetName(host, name);
            if (on) host.SetResourceReference(Control.BackgroundProperty, "ChipHoverBrush");
            host.Click += (o, e) => open();
            return host;
        }

        /// <summary>The icon of the page you are on — the contextual half of this column, as in the design.</summary>
        private UIElement IconPicker(UserPage pg)
        {
            var box = new StackPanel();
            box.Children.Add(Ui.Cap("ICONIȚA PAGINII"));
            var wrap = new WrapPanel();
            foreach (var ic in new[] { "star", "bolt", "game", "music", "work", "heart", "home", "system", "devices", "tools" })
            {
                var key = ic;
                bool on = pg.Icon == ic;
                var b = new Button
                {
                    Style = Ui.S("NavButton"), Padding = new Thickness(7), Margin = new Thickness(0, 0, 4, 4), Cursor = Cursors.Hand,
                    Content = Ui.Icon(NotchWindow.PageGlyph(ic), 14, Ui.B(on ? "OnAccentBrush" : "MutedBrush")),
                };
                if (on) b.SetResourceReference(Control.BackgroundProperty, "AccentBrush");
                System.Windows.Automation.AutomationProperties.SetName(b, "Iconiță " + ic);
                b.Click += (o, e) => { pg.Icon = key; PagesEdited(); BuildLeft(); };
                wrap.Children.Add(b);
            }
            box.Children.Add(wrap);
            return box;
        }

        // ------------------------------------------------------------------ middle: the page and the widget manager

        private void BuildCentre(string slotId)
        {
            _page = null;
            _gallery = null;
            _centre.Children.Clear();
            _popup = (null, null);
            _overlay.Children.Clear();

            if (_sel != null && _sel.StartsWith("std:", StringComparison.Ordinal)) { BuildStandard(_sel.Substring(4)); _inspector.Show(null, null); return; }
            var pg = _s.Pages.FirstOrDefault(p => p.Id == _sel);
            if (pg == null) { _sel = null; Open(null, null); return; }

            // name and the two actions on it
            var nameBox = WidgetInspector.Field(pg.Name, double.NaN);
            nameBox.FontSize = 19;
            nameBox.FontWeight = FontWeights.SemiBold;
            nameBox.MaxLength = 32;
            nameBox.MinWidth = 260;
            nameBox.HorizontalAlignment = HorizontalAlignment.Left;
            nameBox.ToolTip = "Numele paginii (apare pe tab, în notch)";
            nameBox.TextChanged += (o, e) =>
            {
                var v = nameBox.Text.Trim();
                if (v.Length == 0) return;
                pg.Name = v;
                _soon(() => { PagesEdited(); BuildLeft(); });
            };
            var head = Ui.Cols(Ui.Auto, Ui.Star(), Ui.Auto);
            head.Put(nameBox);
            head.Put(Ui.H(8, Ui.PillBtn("Duplică", () => NewPage(pg.Widgets, pg.Name + " (copie)", pg.Icon)), DeleteButton(pg)), 2);
            head.Margin = new Thickness(0, 0, 0, 6);
            _centre.Children.Add(head);

            var help = Ui.T("Trage un widget ca să-l muți; click pe el ca să-i alegi mărimea și opțiunile în dreapta. " +
                            "Widget-uri noi: trage-le din managerul de dedesubt. Totul se salvează și apare în notch pe loc.",
                            12, "MutedBrush");
            help.TextWrapping = TextWrapping.Wrap;
            help.MaxWidth = 760;
            help.HorizontalAlignment = HorizontalAlignment.Left;
            help.Margin = new Thickness(0, 0, 0, LayoutRules.Pad);
            _centre.Children.Add(help);

            // the live page, on the notch's own surface
            _page = new WidgetPage(_notch, pg) { Editing = true };
            _page.Changed += () => { _s.Save(); _notch?.PagesChanged(pg.Id); };
            _page.Selected += slot => _inspector.Show(_page, slot);
            _page.Dropped += ok => { if (!ok) Full(); };
            var surface = new Border
            {
                Width = 760, CornerRadius = new CornerRadius(Math.Clamp(_s.CornerRadius, 12, 28)),
                Padding = new Thickness(20, 10, 20, 18), Child = _page,
            };
            surface.SetResourceReference(Border.BackgroundProperty, "NotchBrush");
            _centre.Children.Add(new Viewbox
            {
                Child = surface, Stretch = System.Windows.Media.Stretch.Uniform, StretchDirection = StretchDirection.DownOnly,
                MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left,
            });

            // the widget manager
            _centre.Children.Add(Ui.Cap("MANAGER WIDGETURI"));
            _gallery = new Gallery((type, size) => { if (!_page.Add(type, size)) Full(); else _gallery?.Message(""); }, null)
            {
                Popup = fe => _popup = Gallery.ShowPopupIn(_overlay, fe, new Thickness(0), 780, onClosed: () => _popup = (null, null)),
            };
            var gHost = new Border
            {
                Height = 340, CornerRadius = new CornerRadius(LayoutRules.CardRadius), Padding = new Thickness(14),
                Child = _gallery, MaxWidth = 900, HorizontalAlignment = HorizontalAlignment.Left,
            };
            gHost.SetResourceReference(Border.BackgroundProperty, "ChipBrush");
            _centre.Children.Add(gHost);

            var sel = slotId != null ? pg.Widgets.FirstOrDefault(w => w.Id == slotId) : null;
            if (sel != null) _page.Select(sel);
            _inspector.Show(_page, sel);
            _page.Refresh();
        }

        private void Full()
        {
            _gallery?.Message("Pagina e plină: scoate sau micșorează un widget.");
            _say("Pagina e plină: scoate sau micșorează un widget.");
        }

        private void BuildStandard(string id)
        {
            var (_, name, icon) = Catalog.Standard.First(x => x.Id == id);
            bool visible = !_s.HiddenPages.Contains(id);
            _centre.Children.Add(Ui.H(10, Ui.Icon(NotchWindow.PageGlyph(icon), 20), Ui.T(name, 20, "InkBrush", true)));
            var line = Ui.T("Pagină standard: rămâne mereu la fel, ca să ai oricând varianta originală. Ca s-o schimbi, fă-ți o copie; " +
                            "copia are aceleași widget-uri și o poți modifica oricum. Originalul îl poți ascunde din notch.", 13, "MutedBrush");
            line.TextWrapping = TextWrapping.Wrap;
            line.MaxWidth = 760;
            line.HorizontalAlignment = HorizontalAlignment.Left;
            line.Margin = new Thickness(0, 10, 0, LayoutRules.Pad);
            _centre.Children.Add(line);
            var btns = Ui.H(8, Ui.PillBtn("Duplică și modifică", () => DuplicateStandard(id), true),
                               Ui.PillBtn(visible ? "Ascunde din notch" : "Arată în notch", () => ToggleStandard(id)));
            btns.HorizontalAlignment = HorizontalAlignment.Left;
            _centre.Children.Add(btns);

            _centre.Children.Add(Ui.Cap("CE CONȚINE COPIA"));
            var list = new StackPanel();
            foreach (var w in Catalog.StandardLayout(id))
            {
                var d = Catalog.Get(w.Type);
                if (d == null) continue;
                var row = Ui.Cols(Ui.Px(28), Ui.Star(), Ui.Auto);
                row.Put(Ui.Icon(d.Glyph, 14, Ui.B("MutedBrush")));
                row.Put(Ui.T(d.Name, 13, "InkBrush"), 1);
                row.Put(Ui.T(w.W + " × " + w.H, 12, "DimBrush", false, true), 2);
                row.Margin = new Thickness(0, 0, 0, 8);
                list.Children.Add(row);
            }
            var card = Ui.Card(list, 18, 14, LayoutRules.CardRadius);
            card.MaxWidth = 760;
            card.HorizontalAlignment = HorizontalAlignment.Left;
            _centre.Children.Add(card);
        }

        private Button DeleteButton(UserPage pg)
        {
            bool armed = false;
            Button b = null;
            b = Ui.PillBtn("Șterge pagina", () =>
            {
                if (!armed) { armed = true; b.Content = "Sigur? Apasă din nou"; return; }
                _s.Pages.Remove(pg);
                if (VisibleCount == 0) _s.HiddenPages.Remove("home");
                PagesEdited();
                _sel = null;
                Open(null, null);
            });
            return b;
        }

        // ------------------------------------------------------------------ changes

        private void Select(string sel)
        {
            if (_sel == sel) return;
            _flush();
            _sel = sel;
            BuildLeft();
            BuildCentre(null);
        }

        private void PagesEdited()
        {
            _s.Save();
            _notch?.PagesChanged(null);
        }

        private void ToggleMine(UserPage pg)
        {
            if (!pg.Hidden && VisibleCount <= 1) { _say("Lasă cel puțin o pagină vizibilă în notch."); return; }
            pg.Hidden = !pg.Hidden;
            PagesEdited();
            BuildLeft();
        }

        private void ToggleStandard(string id)
        {
            if (_s.HiddenPages.Contains(id)) _s.HiddenPages.Remove(id);
            else if (VisibleCount > 1) _s.HiddenPages.Add(id);
            else { _say("Lasă cel puțin o pagină vizibilă în notch."); return; }
            PagesEdited();
            BuildLeft();
            if (_sel == "std:" + id) BuildCentre(null);
        }

        private void DuplicateStandard(string id)
        {
            var (_, name, icon) = Catalog.Standard.First(x => x.Id == id);
            NewPage(Catalog.StandardLayout(id), name + " (copia mea)", icon);
        }

        private void NewPage(IEnumerable<WidgetSlot> from, string name, string icon)
        {
            int n = 1;
            while (_s.Pages.Any(p => p.Name == "Pagina mea " + n)) n++;
            var pg = new UserPage { Name = name ?? "Pagina mea " + n, Icon = icon ?? "star", Widgets = from.Select(w => w.Clone()).ToList() };
            _s.Pages.Add(pg);
            PagesEdited();
            _sel = null;
            Select(pg.Id);
        }
    }
}
