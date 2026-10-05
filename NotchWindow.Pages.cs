using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using WinNotch.Panes;
using WinNotch.Widgets;

namespace WinNotch
{
    /// <summary>
    /// Pages and edit mode: the standard pages (fixed; can be hidden or duplicated), your own widget pages, the
    /// "Editează" mode in the notch with the gallery and the page menu.
    /// </summary>
    public partial class NotchWindow
    {
        private readonly Dictionary<string, WidgetPage> _userPanes = new Dictionary<string, WidgetPage>();
        internal bool Editing { get; private set; }
        private Button _editBtn;
        private StackPanel _editBar;
        private Button _addWidgetBtn;
        private FrameworkElement _gallery, _sizes, _banner;
        private Gallery _galleryView;

        private static readonly Dictionary<string, string> PageGlyphs = new Dictionary<string, string>
        {
            ["home"] = Ui.GHome, ["system"] = Ui.GSystem, ["devices"] = Ui.GDevices, ["tools"] = Ui.GTools,
            ["star"] = "", ["bolt"] = "", ["game"] = Ui.GGamepad, ["music"] = Ui.GMusic, ["work"] = "", ["heart"] = ""
        };
        internal static string PageGlyph(string icon) => icon != null && PageGlyphs.TryGetValue(icon, out var g) ? g : "";

        private Pane StandardPane(string id) => id switch { "home" => _home, "system" => _system, "devices" => _devices, "tools" => _tools, _ => null };
        private string StandardId(Pane p) => p == _home || p == _sources ? "home" : p == _system ? "system" : p == _devices ? "devices" : p == _tools ? "tools" : null;

        internal WidgetPage UserPane(UserPage pg)
        {
            if (_userPanes.TryGetValue(pg.Id, out var wp) && wp.Page == pg) return wp;
            wp = new WidgetPage(this, pg);
            wp.Changed += () => { S.Save(); RelayoutPanel(); ((App)Application.Current).PageChangedInNotch(pg.Id); };
            wp.Tapped += (slot, card) => ShowSizes(wp, slot);
            wp.Dropped += ok => { if (ok) { CloseOverlays(); RelayoutPanel(); } else ShowGalleryAgain("Pagina e plină: scoate sau micșorează un widget."); };
            _userPanes[pg.Id] = wp;
            return wp;
        }

        // ------------------------------------------------------------------ tabs

        private void RebuildTabs()
        {
            TabBar.Children.Clear();
            _tabs.Clear();
            // in edit mode hidden pages show too (faded), each tab with its eye: show / hide with one click
            var entries = new List<(string Glyph, string Name, Pane Pane, bool Hidden, Action Toggle)>();
            foreach (var (id, name, icon) in Catalog.Standard)
            {
                bool hidden = S.HiddenPages.Contains(id);
                var sid = id;
                if (!hidden || Editing) entries.Add((PageGlyph(icon), name, StandardPane(id), hidden, () => ToggleStandard(sid)));
            }
            foreach (var pg in S.Pages.Where(p => !p.Hidden || Editing))
            {
                var page = pg;
                entries.Add((PageGlyph(pg.Icon), pg.Name, UserPane(pg), pg.Hidden, () => ToggleUserPage(page)));
            }
            if (entries.Count == 0) entries.Add((Ui.GHome, "Acasă", _home, false, null));
            foreach (var gone in _userPanes.Keys.Where(k => !S.Pages.Any(p => p.Id == k)).ToList()) _userPanes.Remove(gone);

            bool compact = entries.Count > (Editing ? 4 : 5);          // many pages: icons only, names in tooltips
            foreach (var (glyph, name, pane, hidden, toggle) in entries)
            {
                var rb = new RadioButton { Style = Ui.S("TabRadio"), GroupName = "NotchTabs", ToolTip = compact ? name : null };
                var icon = Ui.Icon(glyph, 12);
                icon.SetBinding(TextBlock.ForegroundProperty, new System.Windows.Data.Binding("Foreground") { Source = rb });
                var content = compact ? Ui.H(0, icon) : Ui.H(6, icon, new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center });
                if (Editing && toggle != null)
                {
                    var eye = Ui.IconBtn(hidden ? "\uED1A" : "\uE7B3", () => Dispatcher.InvokeAsync(toggle), hidden ? "Arată în notch" : "Ascunde din notch", 20, 11);
                    ((TextBlock)eye.Content).SetBinding(TextBlock.ForegroundProperty, new System.Windows.Data.Binding("Foreground") { Source = rb });
                    eye.Margin = new Thickness(4, -4, -6, -4);
                    content.Children.Add(eye);
                }
                rb.Content = content;
                if (hidden) rb.Opacity = 0.45;
                var target = pane;
                rb.Checked += (o, e) => { if (_pane != target && !(target == _home && _pane == _sources)) { ShowPane(target); ContextPagesManualChoice(); } };     // P27 hook: picked by hand, kept 10 min
                rb.Tag = pane;
                _tabs.Add(rb);
                TabBar.Children.Add(rb);
            }
            bool currentShown = _pane != null && entries.Any(e => e.Pane == _pane || (_pane == _sources && e.Pane == _home));
            if (!currentShown) ShowPane(entries.FirstOrDefault(e => !e.Hidden).Pane ?? entries[0].Pane);
            foreach (var t in _tabs) if (t.Tag == _pane || (_pane == _sources && t.Tag == _home)) t.IsChecked = true;
        }

        private void ToggleStandard(string id)
        {
            if (S.HiddenPages.Contains(id)) S.HiddenPages.Remove(id);
            else if (VisibleCount > 1) S.HiddenPages.Add(id);
            else return;
            AfterPagesEdit();
        }

        private void ToggleUserPage(UserPage pg)
        {
            if (!pg.Hidden && VisibleCount <= 1) return;       // at least one page stays visible
            pg.Hidden = !pg.Hidden;
            AfterPagesEdit();
        }

        /// <summary>Pages were added, removed, renamed or edited elsewhere (editor window).</summary>
        internal void PagesChanged(string pageId = null)
        {
            if (pageId != null && _userPanes.TryGetValue(pageId, out var wp)) wp.Rebuild();
            RebuildTabs();
            RelayoutPanel();
        }

        // ------------------------------------------------------------------ header

        private void BuildHeaderButtons()
        {
            _editBtn = new Button { Style = Ui.S("IconButton"), Width = 26, Height = 26, ToolTip = "Editează paginile și widget-urile", Margin = new Thickness(0, 0, 4, 0),
                                    Content = Ui.Icon("", 12, Ui.B("MutedBrush")) };
            ((TextBlock)_editBtn.Content).SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            _editBtn.Click += (o, e) => EnterEdit();
            HeaderRight.Children.Insert(0, _editBtn);

            _editBar = new StackPanel { Orientation = Orientation.Horizontal, Visibility = Visibility.Collapsed };
            // everything about pages, themes and settings lives in one window; the notch keeps only the quick actions
            _addWidgetBtn = Ui.PillBtn("+ Widget", ToggleGallery); _addWidgetBtn.Margin = new Thickness(0, 0, 6, 0);
            var more = Ui.PillBtn("Pagini și setări", () => OpenEditor((_pane as WidgetPage)?.Page.Id ?? StandardId(_pane), null)); more.Margin = new Thickness(0, 0, 6, 0);
            var done = Ui.PillBtn("Gata", ExitEdit, true);
            _editBar.Children.Add(_addWidgetBtn); _editBar.Children.Add(more); _editBar.Children.Add(done);
            HeaderRight.Children.Insert(0, _editBar);
        }

        private void UpdateHeader()
        {
            _editBar.Visibility = Editing ? Visibility.Visible : Visibility.Collapsed;
            _editBtn.Visibility = SettingsBtn.Visibility = HeaderClock.Visibility = Editing ? Visibility.Collapsed : Visibility.Visible;
            _addWidgetBtn.IsEnabled = _pane is WidgetPage;
            _addWidgetBtn.Opacity = _pane is WidgetPage ? 1 : 0.4;
        }

        // ------------------------------------------------------------------ edit mode

        internal void EnterEdit()
        {
            if (_mode != Mode.Expanded) { _pinned = true; Expand(); }
            Editing = true;
            _pinned = true;
            RebuildTabs();
            ApplyEditToPane();
            UpdateHeader();
            RelayoutPanel();
        }

        internal void ExitEdit()
        {
            if (!Editing) return;
            Editing = false;
            CloseOverlays();
            foreach (var wp in _userPanes.Values) wp.Editing = false;
            RemoveBanner();
            RebuildTabs();                             // hidden pages leave the tab bar again
            UpdateHeader();
            S.Save();
            RelayoutPanel();
        }

        /// <summary>Edit mode on the page shown now: a widget page becomes editable, a standard page shows its notice.</summary>
        private void ApplyEditToPane()
        {
            RemoveBanner();
            foreach (var wp in _userPanes.Values) if (wp != _pane) wp.Editing = false;
            if (!Editing) return;
            if (_pane is WidgetPage page) page.Editing = true;
            else ShowStandardBanner(StandardId(_pane));
            UpdateHeader();
        }

        /// <summary>A standard page in edit mode: just a small note at the bottom; the page stays as it is.</summary>
        private void ShowStandardBanner(string id)
        {
            if (id == null) return;
            var info = Ui.T("Pagină standard: nu se modifică. Ca s-o schimbi, ", 11.5, "DimBrush");
            var copy = new Button { Style = Ui.S("IconButton"), Padding = new Thickness(0), Content = Ui.T("fă-ți o copie", 11.5, "AccentBrush"), Cursor = System.Windows.Input.Cursors.Hand };
            copy.Click += (o, e) => DuplicateStandard(id);
            var row = Ui.H(6, Ui.Icon("\uE946", 11, Ui.B("DimBrush")), info, copy);
            row.HorizontalAlignment = HorizontalAlignment.Left;
            row.VerticalAlignment = VerticalAlignment.Bottom;
            row.Margin = new Thickness(2, 0, 0, -2);
            _banner = row;
            OverlayHost.Children.Add(row);
        }

        private void RemoveBanner()
        {
            if (_banner != null) { OverlayHost.Children.Remove(_banner); _banner = null; }
        }

        private void CloseOverlays()
        {
            if (_gallery != null) { OverlayHost.Children.Remove(_gallery); _gallery = null; _galleryView = null; }
            if (_sizes != null) { OverlayHost.Children.Remove(_sizes); _sizes = null; }
            _popup.Close?.Invoke();
            _popup = (null, null);
        }

        private (Action Hide, Action Close) _popup;

        // ------------------------------------------------------------------ gallery

        private void ToggleGallery()
        {
            if (_gallery != null) { CloseOverlays(); RelayoutPanel(); return; }
            if (!(_pane is WidgetPage wp)) return;
            CloseOverlays();
            _galleryView = new Gallery((type, size) =>
            {
                if (wp.Add(type, size)) { CloseOverlays(); RelayoutPanel(); }
                else _galleryView?.Message("Pagina e plină: scoate sau micșorează un widget.");
            }, () => { CloseOverlays(); RelayoutPanel(); });
            var host = new Border { Margin = new Thickness(-6, 36, -6, -4), Padding = new Thickness(12), CornerRadius = new CornerRadius(16), Child = _galleryView };
            host.SetResourceReference(Border.BackgroundProperty, "NotchBrush");
            // a widget's sizes open in a pop-up over the gallery, which stays as it is
            _galleryView.Popup = fe => _popup = Gallery.ShowPopupIn(OverlayHost, fe, new Thickness(-6, 36, -6, -4));
            // while a widget is dragged out of the gallery, the gallery steps aside so the page is visible to drop on
            _galleryView.DragStarted += () => host.Visibility = Visibility.Hidden;
            _galleryView.DragEnded += () => { if (_gallery == host) { host.Visibility = Visibility.Visible; RelayoutPanel(); } };
            _gallery = host;
            OverlayHost.Children.Add(host);
            RelayoutPanel();
        }

        private void ShowGalleryAgain(string msg)
        {
            if (_gallery == null) return;
            _gallery.Visibility = Visibility.Visible;
            _galleryView?.Message(msg);
            RelayoutPanel();
        }

        // ------------------------------------------------------------------ sizes (tap on a widget)

        /// <summary>Like on the iPhone: every size the widget comes in, as previews; click one to resize it.</summary>
        private void ShowSizes(WidgetPage wp, WidgetSlot slot)
        {
            CloseOverlays();
            var def = Catalog.Get(slot.Type);
            if (def == null) return;
            TextBlock msg = Ui.T("", 12, "WarnBrush");
            var previews = Gallery.SizePreviews(def, (slot.W, slot.H), size =>
            {
                if (wp.Resize(slot, size)) { CloseOverlays(); RelayoutPanel(); }
                else msg.Text = "Nu încape la mărimea asta: fă loc pe pagină.";
            });
            var head = Ui.Cols(Ui.Star(), Ui.Auto, Ui.Auto, Ui.Auto);
            head.Put(Ui.V(0, Ui.T(def.Name, 15, "InkBrush", true), Ui.T("Alege mărimea", 11.5, "MutedBrush")));
            var settings = Ui.PillBtn("Setări…", () => OpenEditor(wp.Page.Id, slot.Id)); settings.Margin = new Thickness(0, 0, 6, 0);
            var remove = Ui.PillBtn("Scoate", () => { wp.Page.Widgets.Remove(slot); wp.Commit(); CloseOverlays(); RelayoutPanel(); }); remove.Margin = new Thickness(0, 0, 6, 0);
            head.Put(settings, 1); head.Put(remove, 2);
            head.Put(Ui.PillBtn("Închide", () => { CloseOverlays(); RelayoutPanel(); }, true), 3);
            var body = Ui.Rows(Ui.Auto, Ui.Auto, Ui.Star());
            body.Put(head); body.Put(msg, 0, 1);
            previews.HorizontalAlignment = HorizontalAlignment.Center;
            body.Put(new ScrollViewer { Style = Ui.S("SlimScroll"), Content = previews, Margin = new Thickness(0, 10, 0, 0) }, 0, 2);
            _popup = Gallery.ShowPopupIn(OverlayHost, body, new Thickness(-6, 36, -6, -4));
            _sizes = OverlayHost.Children[OverlayHost.Children.Count - 1] as FrameworkElement;
            RelayoutPanel();
        }

        private int VisibleCount => Catalog.Standard.Count(x => !S.HiddenPages.Contains(x.Id)) + S.Pages.Count(p => !p.Hidden);

        private void AfterPagesEdit()
        {
            S.Save();
            ((App)Application.Current).PagesChangedInNotch();
            RebuildTabs();
            ApplyEditToPane();
            RelayoutPanel();
        }

        internal void HideStandard(string id)
        {
            if (id == null || S.HiddenPages.Contains(id) || VisibleCount <= 1) return;
            S.HiddenPages.Add(id);
            AfterPagesEdit();
        }

        internal void DuplicateStandard(string id)
        {
            var (_, name, icon) = Catalog.Standard.First(s => s.Id == id);
            NewPage(Catalog.StandardLayout(id), name + " (copia mea)", icon);
        }

        private void NewPage(IEnumerable<WidgetSlot> from, string name, string icon = "star")
        {
            int n = 1;
            while (S.Pages.Any(p => p.Name == "Pagina mea " + n)) n++;
            var pg = new UserPage { Name = name ?? "Pagina mea " + n, Icon = icon, Widgets = from.Select(w => w.Clone()).ToList() };
            S.Pages.Add(pg);
            S.Save();
            RebuildTabs();
            ShowPane(UserPane(pg));
            ContextPagesManualChoice();                    // P27: a page just created counts as a manual choice
        }

        /// <summary>Opens the editor window (a page, a widget on it, or "themes").</summary>
        internal void OpenEditor(string pageId, string slotId)
        {
            if (Editing) ExitEdit();
            Collapse();
            ((App)Application.Current).OpenEditor(pageId, slotId);
        }

        internal void OpenSpeedTest()
        {
            ShowPane(_system);
            _system.StartSpeedTest();
        }

        /// <summary>Height of the open notch for the current page, gallery or menu.</summary>
        private double PanelH()
        {
            double h = _pane?.PanelHeight ?? 310;
            if (_gallery != null) h = Math.Max(h, 460);
            if (_sizes != null) h = Math.Max(h, 400);
            if (_banner != null) h += 22;              // room for the note under the page
            return h;
        }

        internal void RelayoutPanel()
        {
            ExpLayer.Height = PanelH();
            if (_mode == Mode.Expanded) ApplyMode();
        }

        /// <summary>Theme changed: everything is rebuilt with the new colors.</summary>
        private void RebuildUi()
        {
            string currentStd = StandardId(_pane);
            string currentUser = (_pane as WidgetPage)?.Page.Id;
            bool wasEditing = Editing;
            if (Editing) ExitEdit();
            _pane?.Hidden();
            _pane = null;
            _home = new HomePane(this);
            _system = new SystemPane(this);
            _devices = new DevicesPane(this);
            _tools = new ToolsPane(this);
            _sources = new SourcesPane(this);
            _userPanes.Clear();
            var userPage = S.Pages.FirstOrDefault(p => p.Id == currentUser);
            ShowPane(userPage != null ? UserPane(userPage) : StandardPane(currentStd ?? "home") ?? _home);
            RebuildTabs();
            _idleSig = null;
            BuildIdle();
            UpdateIdleValues();
            if (wasEditing) EnterEdit();
        }
    }
}
