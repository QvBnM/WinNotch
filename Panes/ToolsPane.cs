using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WinNotch.Services;

namespace WinNotch.Panes
{
    /// <summary>Unelte: launcher with quick math, workspaces, actions on the active window, clipboard history and a note.</summary>
    internal sealed class ToolsPane : Pane
    {
        public override double PanelHeight => 360;

        // launcher
        private readonly TextBox _query;
        private readonly TextBlock _hint, _result;
        private readonly Border _resultChip, _results;
        private readonly StackPanel _resultList = new StackPanel();
        private List<LaunchItem> _matches = new List<LaunchItem>();
        private int _sel;

        // workspaces + active window
        private readonly UniformGrid _wsGrid;
        private readonly TextBlock _wsEmpty, _activeCap;
        private readonly Button _topBtn;

        // clipboard + note
        private readonly RadioButton _tabClips, _tabNote;
        private readonly TextBox _search, _note;
        private readonly Border _searchBox;
        private readonly StackPanel _clipList = new StackPanel();
        private readonly ScrollViewer _clipScroll;

        public ToolsPane(NotchWindow w) : base(w)
        {
            RowDefinitions.Add(new RowDefinition { Height = Ui.Px(36) });
            RowDefinitions.Add(new RowDefinition { Height = Ui.Px(10) });
            RowDefinitions.Add(new RowDefinition { Height = Ui.Px(48) });
            RowDefinitions.Add(new RowDefinition { Height = Ui.Px(10) });
            RowDefinitions.Add(new RowDefinition { Height = Ui.Star() });

            // ---------------- launcher ----------------
            _query = new TextBox { Style = Ui.S("DarkBox"), FontFamily = Ui.Mono, FontSize = 13 };
            _query.PreviewMouseDown += (o, e) => W.EnableTyping(_query);
            _query.TextChanged += (o, e) => UpdateLauncher();
            _query.PreviewKeyDown += OnQueryKey;
            _hint = Ui.T("Caută aplicații, setări, foldere — sau calculează: =250*1,19", 12, "DimBrush");
            _hint.IsHitTestVisible = false;
            var qGrid = new Grid();
            qGrid.Children.Add(_hint);
            qGrid.Children.Add(_query);
            _result = Ui.T("", 13, "InkBrush", true, true);
            _result.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
            _resultChip = new Border { Background = Ui.B("ChipHoverBrush"), CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 3, 10, 3), Child = _result, Visibility = Visibility.Collapsed, Cursor = Cursors.Hand, ToolTip = "Click: copiază rezultatul" };
            _resultChip.MouseLeftButtonUp += (o, e) => RunFirst();
            var lGrid = Ui.Cols(Ui.Auto, Ui.Px(10), Ui.Star(), Ui.Px(8), Ui.Auto);
            lGrid.Put(Ui.Icon(Ui.GSearch, 14, Ui.B("MutedBrush")));
            lGrid.Put(qGrid, 2);
            lGrid.Put(_resultChip, 4);
            var launcher = new Border { Background = Ui.B("ChipBrush"), CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 0, 6, 0), Child = lGrid };
            this.Put(launcher);

            // ---------------- screen + memory: the notch hides itself before these run ----------------
            var quick = Ui.Cols(Ui.Star(), Ui.Px(8), Ui.Star(), Ui.Px(8), Ui.Star(), Ui.Px(8), Ui.Star());
            quick.Put(QuickBtn(Ui.GCamera, "Captură ecran", "monitorul curent", "Salvează tot ecranul în Imagini › Screenshots și îl copiază", () => W.ScreenshotFull()));
            quick.Put(QuickBtn(Ui.GCrop, "Captură zonă", "Win+Alt+S", "Tragi cu mouse-ul peste o zonă; se salvează și se copiază", () => W.ScreenshotArea()), 2);
            quick.Put(QuickBtn(Ui.GText, "Text din ecran", "Win+Alt+T", "Tragi peste un text (imagine, video, PDF scanat) și îl copiază ca text", () => W.TextFromScreen()), 4);
            _ramBtn = QuickBtn(Ui.GMemory, "Eliberează RAM", "", "Golește memoria ținută degeaba de aplicații" + (App.IsAdmin ? " și cache-ul Windows" : " (ca administrator golește și cache-ul Windows)"), () => W.OptimizeMemory());
            quick.Put(_ramBtn, 6);
            this.Put(quick, 0, 2);

            // ---------------- left: workspaces + active window ----------------
            var main = Ui.Cols(Ui.Star(), Ui.Px(10), Ui.Star());
            this.Put(main, 0, 4);

            var save = new TextBlock { Text = "+ salvează aranjarea de acum", FontSize = 11.5, Foreground = Ui.B("MutedBrush"), VerticalAlignment = VerticalAlignment.Center }.OnClick(SaveWorkspace);
            save.ToolTip = "Ține minte aplicațiile deschise acum și unde stau, pe fiecare monitor";
            var wsHead = Ui.Cols(Ui.Star(), Ui.Auto);
            wsHead.Put(Ui.Cap("SPAȚII DE LUCRU"));
            wsHead.Put(save, 1);
            _wsGrid = new UniformGrid { Rows = 1, Columns = 3 };
            _wsEmpty = Ui.T("Aranjează aplicațiile cum îți plac, apoi apasă „+ salvează aranjarea de acum”. Un click le redeschide exact așa.", 11, "DimBrush");
            _wsEmpty.TextWrapping = TextWrapping.Wrap;
            _wsEmpty.TextTrimming = TextTrimming.None;
            var wsArea = new Grid { Height = 78 };
            wsArea.Children.Add(_wsEmpty);
            wsArea.Children.Add(_wsGrid);

            _activeCap = Ui.Cap("FEREASTRA ACTIVĂ");
            _topBtn = ActionBtn("", "Deasupra", "Ține fereastra peste toate celelalte", () => Act(WindowTools.ToggleTopmost));
            var actions = Ui.Cols(Ui.Star(), Ui.Px(5), Ui.Star(), Ui.Px(5), Ui.Star(), Ui.Px(5), Ui.Star());
            actions.Put(_topBtn);
            actions.Put(ActionBtn("", "Monitor 2", "Mut-o pe celălalt monitor", () => Act(WindowTools.MoveToNextMonitor)), 2);
            actions.Put(ActionBtn("", "Jumătate", "Jumătate stânga / dreapta", () => Act(WindowTools.SnapHalf)), 4);
            actions.Put(ActionBtn("", "Mini", "Mică, în colțul din dreapta jos", () => Act(WindowTools.Mini)), 6);

            var leftRows = Ui.Rows(Ui.Auto, Ui.Px(8), Ui.Auto, Ui.Star(), Ui.Auto, Ui.Px(6), Ui.Px(32));
            leftRows.Put(wsHead);
            leftRows.Put(wsArea, 0, 2);
            leftRows.Put(_activeCap, 0, 4);
            leftRows.Put(actions, 0, 6);
            main.Put(Ui.Card(leftRows, 12, 10));

            // ---------------- right: clipboard + note ----------------
            _tabClips = new RadioButton { Style = Ui.S("SegRadio"), GroupName = "ToolsRight", Content = "Clipboard", IsChecked = true };
            _tabNote = new RadioButton { Style = Ui.S("SegRadio"), GroupName = "ToolsRight", Content = "Notiță" };
            _tabClips.Checked += (o, e) => SwitchRight();
            _tabNote.Checked += (o, e) => SwitchRight();
            var seg = new Border { Background = Ui.B("SegBrush"), CornerRadius = new CornerRadius(9), Padding = new Thickness(2), Child = Ui.H(0, _tabClips, _tabNote) };

            _search = new TextBox { Style = Ui.S("DarkBox"), FontSize = 11.5 };
            _search.PreviewMouseDown += (o, e) => W.EnableTyping(_search);
            _search.TextChanged += (o, e) => ClipsChanged();
            var sHint = Ui.T("Caută în istoric", 11.5, "DimBrush");
            sHint.IsHitTestVisible = false;
            _search.TextChanged += (o, e) => sHint.Visibility = string.IsNullOrEmpty(_search.Text) ? Visibility.Visible : Visibility.Collapsed;
            var sGrid = new Grid();
            sGrid.Children.Add(sHint);
            sGrid.Children.Add(_search);
            var sRow = Ui.Cols(Ui.Auto, Ui.Px(6), Ui.Star());
            sRow.Put(Ui.Icon(Ui.GSearch, 11, Ui.B("DimBrush")));
            sRow.Put(sGrid, 2);
            _searchBox = new Border { Background = Ui.B("ChipHoverBrush"), CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 0, 8, 0), Height = 26, Width = 150, Child = sRow };

            var rHead = Ui.Cols(Ui.Auto, Ui.Star(), Ui.Auto);
            rHead.Put(seg);
            rHead.Put(_searchBox, 2);

            _clipScroll = new ScrollViewer { Style = Ui.S("SlimScroll"), Content = _clipList };
            _note = new TextBox
            {
                Style = Ui.S("DarkBox"), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalContentAlignment = VerticalAlignment.Top,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = Ui.B("ChipHoverBrush"), Padding = new Thickness(8, 6, 8, 6),
                Text = W.S.Note ?? "", Visibility = Visibility.Collapsed
            };
            _note.PreviewMouseDown += (o, e) => W.EnableTyping(_note);
            var noteSave = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            noteSave.Tick += (o, e) => { noteSave.Stop(); W.S.Save(); };
            _note.TextChanged += (o, e) => { W.S.Note = _note.Text; noteSave.Stop(); noteSave.Start(); };   // saved right away, nothing lost on update

            var rBody = new Grid();
            rBody.Children.Add(_clipScroll);
            rBody.Children.Add(_note);
            var rightRows = Ui.Rows(Ui.Auto, Ui.Px(8), Ui.Star());
            rightRows.Put(rHead);
            rightRows.Put(rBody, 0, 2);
            main.Put(Ui.Card(rightRows, 12, 10), 2);

            // launcher results float over the panels below
            _results = new Border
            {
                Background = Ui.B("ChipBrush"), CornerRadius = new CornerRadius(12), Padding = new Thickness(6),
                BorderBrush = Ui.B("TrackBrush"), BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Top,
                Child = _resultList, Visibility = Visibility.Collapsed
            };
            Panel.SetZIndex(_results, 10);
            Grid.SetRowSpan(_results, 3);
            this.Put(_results, 0, 2);

            Rebuild();
            ClipsChanged();
        }

        private readonly Button _ramBtn;
        private TextBlock _ramSub;

        private Button QuickBtn(string glyph, string label, string sub, string tip, Action click)
        {
            var subText = Ui.T(sub, 11, "DimBrush");
            if (glyph == Ui.GMemory) _ramSub = subText;
            var icon = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(9), Background = Ui.B("TrackBrush"), Child = Ui.Icon(glyph, 15) };
            var g = Ui.Cols(Ui.Auto, Ui.Px(9), Ui.Star());
            g.Put(icon);
            g.Put(Ui.V(0, Ui.T(label, 12.5, "InkBrush", true), subText), 2);
            var b = new Button
            {
                Style = Ui.S("TileButton"), ToolTip = tip, Padding = new Thickness(9, 0, 8, 0),
                HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Center, Content = g
            };
            b.Click += (o, e) => click();
            return b;
        }

        private static Button ActionBtn(string glyph, string label, string tip, Action click)
        {
            var b = new Button { Style = Ui.S("TileButton"), ToolTip = tip, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
            b.Content = Ui.H(6, Ui.Icon(glyph, 13, Ui.B("MutedBrush")), Ui.T(label, 11));
            b.Click += (o, e) => click();
            return b;
        }

        private void Act(Action<IntPtr> a)
        {
            var h = W.LastForeground;
            if (h == IntPtr.Zero) return;
            try { a(h); } catch (Exception ex) { App.Log("Fereastra activă: " + ex.Message); }
            Refresh();
        }

        public override void Refresh()
        {
            var h = W.LastForeground;
            string name = h != IntPtr.Zero ? WindowTools.AppName(h) : "";
            _activeCap.Text = "FEREASTRA ACTIVĂ" + (name.Length > 0 ? " · " + name.ToUpperInvariant() : "");
            bool top = h != IntPtr.Zero && WindowTools.IsTopmost(h);
            if (top) _topBtn.SetResourceReference(Control.BorderBrushProperty, "AccentBrush");
            else _topBtn.BorderBrush = Brushes.Transparent;
            _tabClips.Content = "Clipboard · " + W.Clips.Count;
            var (used, total) = MemoryTools.Status();
            if (_ramSub != null && total > 0) _ramSub.Text = Math.Round(used * 100.0 / total) + "% folosit";
        }

        // ---------------------------------------------------------------- launcher

        private void UpdateLauncher()
        {
            string q = _query.Text;
            _hint.Visibility = string.IsNullOrEmpty(q) ? Visibility.Visible : Visibility.Collapsed;
            string calc = Launcher.Calculate(q);
            _matches = calc == null ? Launcher.Search(q) : new List<LaunchItem>();
            _sel = 0;
            if (calc != null)
            {
                _result.Text = calc;
                _resultChip.Visibility = Visibility.Visible;
            }
            else if (_matches.Count > 0)
            {
                _result.Text = "↵ " + _matches[0].Name;
                _resultChip.Visibility = Visibility.Visible;
            }
            else _resultChip.Visibility = Visibility.Collapsed;
            DrawResults();
        }

        private void DrawResults()
        {
            _resultList.Children.Clear();
            if (_matches.Count == 0) { _results.Visibility = Visibility.Collapsed; return; }
            for (int i = 0; i < _matches.Count; i++)
            {
                var m = _matches[i];
                string glyph = m.Kind == "Setare" ? Ui.GSettings : m.Kind == "Folder" ? Ui.GFolder : Ui.GApp;
                var g = Ui.Cols(Ui.Px(22), Ui.Px(10), Ui.Star(), Ui.Auto);
                g.Put(Ui.Icon(glyph, 13, Ui.B("MutedBrush")));
                g.Put(Ui.T(m.Name, 12.5), 2);
                g.Put(Ui.T(m.Kind, 10.5, "DimBrush"), 3);
                var row = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 5, 10, 5), Child = g, Background = i == _sel ? Ui.B("ChipHoverBrush") : Brushes.Transparent };
                var item = m;
                row.OnClick(() => { Launcher.Open(item); ClearQuery(); });
                _resultList.Children.Add(row);
            }
            _results.Visibility = Visibility.Visible;
        }

        private void OnQueryKey(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { RunFirst(); e.Handled = true; }
            else if (e.Key == Key.Escape) { ClearQuery(); e.Handled = true; }
            else if (e.Key == Key.Down && _matches.Count > 0) { _sel = Math.Min(_matches.Count - 1, _sel + 1); DrawResults(); e.Handled = true; }
            else if (e.Key == Key.Up && _matches.Count > 0) { _sel = Math.Max(0, _sel - 1); DrawResults(); e.Handled = true; }
        }

        private void RunFirst()
        {
            string calc = Launcher.Calculate(_query.Text);
            if (calc != null)
            {
                W.CopyToClipboard(calc);
                _result.Text = "copiat ✓";
                return;
            }
            if (_matches.Count == 0) return;
            Launcher.Open(_matches[Math.Min(_sel, _matches.Count - 1)]);
            ClearQuery();
        }

        private void ClearQuery()
        {
            _query.Text = "";
            _matches.Clear();
            DrawResults();
        }

        // ---------------------------------------------------------------- workspaces

        public void Rebuild()
        {
            _wsGrid.Children.Clear();
            var list = W.S.Workspaces.Skip(Math.Max(0, W.S.Workspaces.Count - 3)).ToList();
            _wsEmpty.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            _wsGrid.Columns = 3;
            foreach (var ws in list)
            {
                var icons = new StackPanel { Orientation = Orientation.Horizontal };
                int k = 0;
                foreach (var win in ws.Windows.Take(4))
                {
                    var badge = Ui.AppBadge(AudioSessionsService.Friendly(Path.GetFileNameWithoutExtension(win.Exe).ToLowerInvariant()), 18);
                    badge.Margin = new Thickness(k++ == 0 ? 0 : -5, 0, 0, 0);
                    badge.BorderBrush = Ui.B("ChipHoverBrush");
                    badge.BorderThickness = new Thickness(1.5);
                    icons.Children.Add(badge);
                }
                var label = Ui.V(0, Ui.T(ws.Name, 12, "InkBrush", true), Ui.T(ws.Windows.Count + (ws.Windows.Count == 1 ? " aplicație" : " aplicații"), 10, "DimBrush"));
                var content = Ui.V(6, icons, label);
                var b = new Button { Style = Ui.S("TileButton"), Padding = new Thickness(9, 8, 9, 8), Margin = new Thickness(0, 0, 6, 0), Content = content, ToolTip = "Deschide și aranjează · click dreapta: redenumește sau șterge în Setări", HorizontalContentAlignment = HorizontalAlignment.Left };
                var target = ws;
                b.Click += async (o, e) => await WindowTools.RestoreAsync(target, W.Hwnd);
                var menu = new ContextMenu();
                var del = new MenuItem { Header = "Șterge „" + ws.Name + "”" };
                del.Click += (o, e) => { W.S.Workspaces.Remove(target); W.S.Save(); Rebuild(); };
                var upd = new MenuItem { Header = "Actualizează cu aranjarea de acum" };
                upd.Click += (o, e) =>
                {
                    var fresh = WindowTools.Capture(W.Hwnd, target.Name);
                    target.Windows = fresh.Windows;
                    W.S.Save();
                    Rebuild();
                };
                menu.Items.Add(upd);
                menu.Items.Add(del);
                b.ContextMenu = menu;
                _wsGrid.Children.Add(b);
            }
        }

        private void SaveWorkspace()
        {
            var ws = WindowTools.Capture(W.Hwnd, NextName());
            if (ws.Windows.Count == 0) return;
            W.S.Workspaces.Add(ws);
            W.S.Save();
            Rebuild();
        }

        private string NextName()
        {
            string[] names = { "Lucru", "Gaming", "Seară", "Studiu", "Proiect" };
            foreach (var n in names) if (!W.S.Workspaces.Any(w => w.Name == n)) return n;
            return "Spațiu " + (W.S.Workspaces.Count + 1);
        }

        // ---------------------------------------------------------------- clipboard + note

        private void SwitchRight()
        {
            if (_clipScroll == null || _note == null) return;
            bool clips = _tabClips.IsChecked == true;
            _clipScroll.Visibility = clips ? Visibility.Visible : Visibility.Collapsed;
            _searchBox.Visibility = clips ? Visibility.Visible : Visibility.Collapsed;
            _note.Visibility = clips ? Visibility.Collapsed : Visibility.Visible;
        }

        public void ClipsChanged()
        {
            _clipList.Children.Clear();
            string f = _search?.Text?.Trim() ?? "";
            var items = W.Clips.Where(c => f.Length == 0 || c.Text.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0)
                               .OrderByDescending(c => c.Pinned).ThenByDescending(c => c.At).ToList();
            if (_tabClips != null) _tabClips.Content = "Clipboard · " + W.Clips.Count;
            if (items.Count == 0)
            {
                _clipList.Children.Add(Ui.T(W.Clips.Count == 0 ? "Ce copiezi apare aici (ultimele 20)" : "Nimic găsit", 11.5, "DimBrush"));
                return;
            }
            foreach (var c in items) _clipList.Children.Add(ClipRow(c));
        }

        private FrameworkElement ClipRow(ClipItem c)
        {
            string head = c.Text.Length > 200 ? c.Text.Substring(0, 200) : c.Text;     // clips can be megabytes long
            string preview = head.Replace("\r", " ").Replace("\n", " ").Trim();
            if (preview.Length > 90) preview = preview.Substring(0, 90) + "…";
            bool code = preview.Contains("{") || preview.StartsWith("git ") || preview.StartsWith("dotnet ") || preview.Contains("();");
            var text = Ui.T(preview, code ? 11 : 12, "InkBrush", false, code);
            var meta = Ui.T(c.Pinned ? "fixat" : Ui.Ago(c.At), 10.5, "DimBrush");
            var g = Ui.Cols(Ui.Star(), Ui.Px(8), Ui.Auto, Ui.Px(4), Ui.Px(24), Ui.Px(24));
            g.Put(text);
            g.Put(meta, 2);
            g.Put(Ui.IconBtn(Ui.GCopy, () => Copy(c, text, preview), "Copiază", 24, 12, Ui.B("MutedBrush")), 4);
            var pin = Ui.IconBtn(c.Pinned ? Ui.GUnpin : Ui.GPin, () => { c.Pinned = !c.Pinned; W.SavePins(); ClipsChanged(); }, c.Pinned ? "Desprinde" : "Fixează sus (rămâne și după repornire)", 24, 12, Ui.B("DimBrush"));
            if (c.Pinned) ((TextBlock)pin.Content).SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
            g.Put(pin, 5);
            text.OnClick(() => Copy(c, text, preview));
            text.ToolTip = c.Text.Length > 400 ? c.Text.Substring(0, 400) + "…" : c.Text;
            return new Border { Background = Ui.B("ChipHoverBrush"), CornerRadius = new CornerRadius(9), Padding = new Thickness(10, 2, 4, 2), Margin = new Thickness(0, 0, 4, 4), Child = g };
        }

        private void Copy(ClipItem c, TextBlock text, string preview)
        {
            W.CopyToClipboard(c.Text);
            text.Text = "Copiat ✓";
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
            t.Tick += (o, e) => { t.Stop(); text.Text = preview; };
            t.Start();
        }
    }
}
