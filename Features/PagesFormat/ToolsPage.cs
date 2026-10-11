using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WinNotch.Panes;
using WinNotch.Services;

namespace WinNotch.Features.PagesFormat
{
    /// <summary>
    /// P75, „Unelte”, after `docs/design/05-pagina-unelte.png`: the search and what you can do to the machine on the
    /// left, what you have copied and where you work on the right. The only page with fields in it, so the notch asks
    /// for the keyboard here and gives it back when you leave.
    ///
    /// The old <see cref="ToolsPane"/> stays exactly as it is and is what the notch shows while the switch is off.
    /// </summary>
    internal sealed class ToolsPage : Pane
    {
        public override double PanelHeight => PageFormat.ToolsHeight;

        private readonly TextBox _query;
        private readonly StackPanel _results, _clips, _spaces;
        private readonly Grid _calcRow;
        private readonly TextBlock _calcExpr, _calcValue, _clipCount, _ramPct, _activeName;
        private readonly Border _topBtn;
        private readonly TextBox _note;
        private readonly System.Windows.Threading.DispatcherTimer _noteSave =
            new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        private List<LaunchItem> _matches = new List<LaunchItem>();

        public ToolsPage(NotchWindow w) : base(w)
        {
            var cols = Deck.Columns(PageFormat.ToolsSplit, out var left, out var right);
            Children.Add(cols);

            // ---------- left: the search, the four tiles, the active window ----------
            _query = new TextBox { Style = Ui.S("DarkBox"), FontSize = 12, Padding = new Thickness(10, 6, 10, 6) };
            _query.PreviewMouseDown += (o, e) => W.EnableTyping(_query);
            _query.TextChanged += (o, e) => UpdateSearch();
            _query.PreviewKeyDown += OnQueryKey;

            _results = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };

            _calcExpr = Deck.T(Ink.Note, "");
            _calcValue = Deck.T(Ink.Value, "", PageFormat.Reading.Live);
            var calcHint = Deck.T(Ink.Label, "click = copiat");
            calcHint.Margin = new Thickness(7, 1, 0, 0);
            _calcRow = new Grid { Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed, Cursor = Cursors.Hand };
            _calcRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _calcRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _calcRow.Children.Add(_calcExpr);
            var calcRight = Deck.Row(0, _calcValue, calcHint);
            Grid.SetColumn(calcRight, 1);
            _calcRow.Children.Add(calcRight);
            _calcRow.MouseLeftButtonUp += (o, e) => { CopyCalc(); e.Handled = true; };

            var searchCard = Deck.Card(Deck.Stack(0, _query, _results, _calcRow));

            _ramPct = Deck.T(Ink.Medium, "—", PageFormat.Reading.Live);
            var tiles = new Grid { Margin = new Thickness(0, PageFormat.CardGap, 0, 0) };
            for (int i = 0; i < 7; i++)
                tiles.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = i % 2 == 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(6),
                });
            Place(tiles, Tile(Ui.GCamera, null, "Captură", () => W.ScreenshotFull()), 0);
            Place(tiles, Tile(Ui.GCrop, null, "Zonă", () => W.ScreenshotArea()), 2);
            Place(tiles, Tile(Ui.GText, null, "Text din ecran", () => W.TextFromScreen()), 4);
            Place(tiles, Tile(null, _ramPct, "Eliberează RAM", () => W.OptimizeMemory()), 6);

            _activeName = Deck.T(Ink.Eyebrow, "Fereastra activă");
            _topBtn = ActionChip("Deasupra", () => Act(WindowTools.ToggleTopmost));
            var actions = Deck.Row(6, _topBtn,
                                   ActionChip("Monitor 2", () => Act(WindowTools.MoveToNextMonitor)),
                                   ActionChip("Jumătate", () => Act(WindowTools.SnapHalf)),
                                   ActionChip("Mini", () => Act(WindowTools.Mini)));
            var activeGrid = new Grid();
            activeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
            activeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            activeGrid.Children.Add(_activeName);
            Grid.SetColumn(actions, 1);
            activeGrid.Children.Add(actions);
            var activeCard = Deck.Card(activeGrid);
            activeCard.Margin = new Thickness(0, PageFormat.CardGap, 0, 0);

            var leftStack = new Grid();
            leftStack.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            leftStack.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            leftStack.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            leftStack.Children.Add(searchCard);
            Grid.SetRow(tiles, 1);
            leftStack.Children.Add(tiles);
            Grid.SetRow(activeCard, 2);
            leftStack.Children.Add(activeCard);
            left.Children.Add(leftStack);

            // ---------- right: the clipboard, the workspaces, the note ----------
            _clipCount = Deck.T(Ink.Unit, "0");
            _clips = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
            var privacy = Deck.T(Ink.Label, "Parolele nu se înregistrează.");
            privacy.Margin = new Thickness(0, 10, 0, 0);
            Deck.Paint(privacy, "DimBrush");
            var clipCard = Deck.Card(Deck.Stack(0, Deck.Head("Clipboard", _clipCount), _clips, privacy));

            _spaces = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            var spacesCard = Deck.Card(Deck.Stack(0, Deck.T(Ink.Eyebrow, "Spații de lucru"), _spaces));
            spacesCard.Margin = new Thickness(0, PageFormat.CardGap, 0, 0);

            _note = new TextBox
            {
                Style = Ui.S("DarkBox"), FontSize = 11.5, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
                Text = W.S.Note ?? "", Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                Padding = new Thickness(0, 6, 0, 0),
            };
            _note.PreviewMouseDown += (o, e) => W.EnableTyping(_note);
            _noteSave.Tick += (o, e) => { _noteSave.Stop(); W.S.Save(); };
            _note.TextChanged += (o, e) => { W.S.Note = _note.Text; _noteSave.Stop(); _noteSave.Start(); };
            var noteCard = Deck.Card(Deck.Stack(0, Deck.Head("Notiță", Deck.T(Ink.Unit, "salvat")), _note));
            noteCard.Margin = new Thickness(0, PageFormat.CardGap, 0, 0);

            var rightStack = new Grid();
            rightStack.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            rightStack.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            rightStack.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            rightStack.Children.Add(clipCard);
            Grid.SetRow(spacesCard, 1);
            rightStack.Children.Add(spacesCard);
            Grid.SetRow(noteCard, 2);
            rightStack.Children.Add(noteCard);
            right.Children.Add(rightStack);
        }

        private static void Place(Grid g, UIElement e, int col)
        {
            Grid.SetColumn(e, col);
            g.Children.Add(e);
        }

        /// <summary>One of the four action tiles: the glyph (or a reading) above, the name below.</summary>
        private static Border Tile(string glyph, TextBlock reading, string name, Action click)
        {
            UIElement top = reading ?? (UIElement)Ui.Icon(glyph, 15);
            if (top is FrameworkElement fe) fe.HorizontalAlignment = HorizontalAlignment.Left;
            var label = Deck.T(Ink.Name, name);
            label.Margin = new Thickness(0, 0, 0, 0);
            var stack = new Grid();
            stack.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            stack.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            stack.Children.Add(top);
            Grid.SetRow(label, 1);
            stack.Children.Add(label);
            var b = new Border
            {
                CornerRadius = new CornerRadius(12), Height = 64, Cursor = Cursors.Hand,
                Padding = new Thickness(10, 9, 10, 9), Child = stack,
            };
            b.SetResourceReference(Border.BackgroundProperty, "ChipHoverBrush");
            b.MouseLeftButtonUp += (o, e) => { click(); e.Handled = true; };
            return b;
        }

        private static Border ActionChip(string name, Action click)
        {
            var b = Deck.Chip(Deck.T(Ink.Name, name), 11, 5);
            b.Cursor = Cursors.Hand;
            b.MouseLeftButtonUp += (o, e) => { click(); e.Handled = true; };
            return b;
        }

        private void Act(Action<IntPtr> a)
        {
            var h = W.LastForeground;
            if (h == IntPtr.Zero) return;
            try { a(h); } catch (Exception ex) { App.Log("Fereastra activă: " + ex.Message); }
            Refresh();
        }

        // ------------------------------------------------------------------ search

        private int _sel;

        private void UpdateSearch()
        {
            string q = _query.Text ?? "";
            string calc = Launcher.Calculate(q);
            _matches = calc == null ? Launcher.Search(q) : new List<LaunchItem>();
            _sel = 0;
            _calcRow.Visibility = calc == null ? Visibility.Collapsed : Visibility.Visible;
            if (calc != null)
            {
                _calcExpr.Text = "= " + q.TrimStart('=', ' ');
                _calcValue.Text = calc;
            }
            DrawResults();
        }

        private void DrawResults()
        {
            _results.Children.Clear();
            foreach (var item in _matches.Take(3))
            {
                var g = new Grid { Margin = new Thickness(0, 4, 0, 0), Cursor = Cursors.Hand };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var badge = new Border { Width = 14, Height = 14, CornerRadius = new CornerRadius(4), VerticalAlignment = VerticalAlignment.Center };
                badge.SetResourceReference(Border.BackgroundProperty, "ChipHoverBrush");
                g.Children.Add(badge);
                var name = Deck.T(Ink.Name, item.Name);
                Grid.SetColumn(name, 1);
                g.Children.Add(name);
                var kind = Deck.T(Ink.Unit, KindOf(item));
                Grid.SetColumn(kind, 2);
                g.Children.Add(kind);
                var captured = item;
                g.MouseLeftButtonUp += (o, e) => { Launcher.Open(captured); ClearQuery(); e.Handled = true; };
                _results.Children.Add(g);
            }
        }

        /// <summary>What the result is, engraved at its right end — the plates' „SETARE · FOLDER · APLICAȚIE”.</summary>
        private static string KindOf(LaunchItem item)
        {
            string s = item?.Kind ?? "";
            return s.Length > 0 ? s : "deschide";
        }

        private void OnQueryKey(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) { ClearQuery(); e.Handled = true; }
            else if (e.Key == Key.Down && _matches.Count > 0) { _sel = Math.Min(_sel + 1, _matches.Count - 1); e.Handled = true; }
            else if (e.Key == Key.Up && _matches.Count > 0) { _sel = Math.Max(0, _sel - 1); e.Handled = true; }
            else if (e.Key == Key.Enter) { RunFirst(); e.Handled = true; }
        }

        private void RunFirst()
        {
            string calc = Launcher.Calculate(_query.Text);
            if (calc != null) { CopyCalc(); return; }
            if (_matches.Count == 0) return;
            Launcher.Open(_matches[Math.Min(_sel, _matches.Count - 1)]);
            ClearQuery();
        }

        private void CopyCalc()
        {
            if (_calcRow.Visibility != Visibility.Visible) return;
            W.CopyToClipboard(_calcValue.Text);
            ClearQuery();
        }

        private void ClearQuery()
        {
            _query.Text = "";
            _matches = new List<LaunchItem>();
            _calcRow.Visibility = Visibility.Collapsed;
            DrawResults();
        }

        // ------------------------------------------------------------------ the rest

        public override void Shown() => Refresh();

        public override void Refresh() => NotchWindow.GuardFormat(RefreshCore);

        private void RefreshCore()
        {
            var h = W.LastForeground;
            string name = h != IntPtr.Zero ? WindowTools.AppName(h) : "";
            _activeName.Text = PageFormat.Eyebrow("Fereastra activă" + (name.Length > 0 ? " · " + name : ""));
            bool top = h != IntPtr.Zero && WindowTools.IsTopmost(h);
            _topBtn.SetResourceReference(Border.BackgroundProperty, top ? "AccentBrush" : "ChipHoverBrush");

            var (used, total) = MemoryTools.Status();
            _ramPct.Text = total > 0 ? Math.Round(used * 100.0 / total) + "%" : "—";

            _clipCount.Text = W.Clips.Count.ToString();
            _clips.Children.Clear();
            int i = 0;
            foreach (var c in W.Clips.OrderByDescending(x => x.Pinned).ThenByDescending(x => x.At).Take(3))
            {
                var row = ClipRow(c, first: i++ == 0);
                _clips.Children.Add(row);
            }
            if (W.Clips.Count == 0) _clips.Children.Add(Deck.T(Ink.Label, "Ce copiezi apare aici (ultimele 20)"));

            _spaces.Children.Clear();
            foreach (var ws in W.S.Workspaces.Skip(Math.Max(0, W.S.Workspaces.Count - 3)))
            {
                var target = ws;
                var chip = Deck.Chip(Deck.T(Ink.Name, ws.Name), 11, 4);
                chip.Margin = new Thickness(_spaces.Children.Count > 0 ? 6 : 0, 0, 0, 0);
                chip.Cursor = Cursors.Hand;
                chip.MouseLeftButtonUp += async (o, e) => { e.Handled = true; await WindowTools.RestoreAsync(target, W.Hwnd); };
                _spaces.Children.Add(chip);
            }
            if (W.S.Workspaces.Count == 0) _spaces.Children.Add(Deck.T(Ink.Label, "Niciun spațiu salvat"));
        }

        /// <summary>
        /// One line of the clipboard. The first one sits in a chip, as the plates draw it — what you would paste next
        /// is the one thing the card is about; the rest are only there so you can reach further back.
        /// </summary>
        private FrameworkElement ClipRow(ClipItem c, bool first)
        {
            string preview = (c.Text ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
            if (preview.Length > 46) preview = preview.Substring(0, 45) + "…";
            var text = Deck.T(first ? Ink.Name : Ink.Sub, preview);
            text.Margin = new Thickness(c.Pinned ? 7 : 0, 0, 0, 0);
            UIElement content = text;
            if (c.Pinned)
            {
                var star = Ui.Icon(Ui.GPin, 10);
                star.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
                var sp = new StackPanel { Orientation = Orientation.Horizontal };
                sp.Children.Add(star);
                sp.Children.Add(text);
                content = sp;
            }
            FrameworkElement row = first ? (FrameworkElement)Deck.Chip(content, 10, 6) : text;
            row.Margin = new Thickness(0, first ? 0 : 6, 0, 0);
            row.Cursor = Cursors.Hand;
            row.MouseLeftButtonUp += (o, e) => { W.CopyToClipboard(c.Text); e.Handled = true; };
            return row;
        }

        public override void Hidden()
        {
            // The note is written as you type, but a page that goes away mid-sentence must not lose the last word.
            if (_noteSave.IsEnabled) { _noteSave.Stop(); W.S.Save(); }
        }
    }
}
