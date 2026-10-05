using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WinNotch.Services;

namespace WinNotch.Widgets
{
    /// <summary>Lansator: type to open apps, settings, folders, or calculate with "=". Enter opens, ↑↓ cycle.</summary>
    internal sealed class LauncherWidget : Widget
    {
        private readonly TextBox _box;
        private readonly TextBlock _hint, _result;
        private List<LaunchItem> _matches = new List<LaunchItem>();
        private int _sel;
        public override Thickness CardPadding => new Thickness(12, 4, 6, 4);

        public LauncherWidget(NotchWindow w, WidgetSlot s) : base(w, s)
        {
            _box = new TextBox { Style = Ui.S("DarkBox"), FontFamily = Ui.Mono, FontSize = 13 };
            _box.PreviewMouseDown += (o, e) => W.EnableTyping(_box);
            _box.TextChanged += (o, e) => Update();
            _box.PreviewKeyDown += OnKey;
            _hint = Ui.T(Cw >= 6 ? "Caută aplicații, setări, foldere — sau calculează: =250*1,19" : "Caută sau =calcul", 12, "DimBrush");
            _hint.IsHitTestVisible = false;
            _result = Ui.T("", 12.5, "InkBrush", true, true);
            _result.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
            var chip = new Border { Background = Ui.B("ChipHoverBrush"), CornerRadius = new CornerRadius(8), Padding = new Thickness(9, 3, 9, 3), Child = _result, Cursor = Cursors.Hand, MaxWidth = Cw >= 6 ? 260 : 150 };
            chip.MouseLeftButtonUp += (o, e) => Run();
            chip.SetBinding(VisibilityProperty, new System.Windows.Data.Binding("Text") { Source = _result, Converter = new EmptyToCollapsed() });
            var q = new Grid(); q.Children.Add(_hint); q.Children.Add(_box);
            var g = Ui.Cols(Ui.Auto, Ui.Px(8), Ui.Star(), Ui.Px(6), Ui.Auto);
            g.Put(Ui.Icon(Ui.GSearch, 14, Ui.B("MutedBrush"))); g.Put(q, 2); g.Put(chip, 4);
            g.VerticalAlignment = VerticalAlignment.Center;
            Children.Add(g);
        }

        private void Update()
        {
            string t = _box.Text;
            _hint.Visibility = string.IsNullOrEmpty(t) ? Visibility.Visible : Visibility.Collapsed;
            string calc = Launcher.Calculate(t);
            _matches = calc == null ? Launcher.Search(t) : new List<LaunchItem>();
            _sel = 0;
            _result.Text = calc ?? (_matches.Count > 0 ? "↵ " + _matches[0].Name + (_matches.Count > 1 ? "  1/" + _matches.Count : "") : "");
        }

        private void OnKey(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { Run(); e.Handled = true; }
            else if (e.Key == Key.Escape) { _box.Text = ""; e.Handled = true; }
            else if ((e.Key == Key.Down || e.Key == Key.Up) && _matches.Count > 0)
            {
                _sel = (_sel + (e.Key == Key.Down ? 1 : _matches.Count - 1)) % _matches.Count;
                _result.Text = "↵ " + _matches[_sel].Name + "  " + (_sel + 1) + "/" + _matches.Count;
                e.Handled = true;
            }
        }

        private void Run()
        {
            string calc = Launcher.Calculate(_box.Text);
            if (calc != null) { W.CopyToClipboard(calc); _result.Text = "copiat ✓"; return; }
            if (_matches.Count == 0) return;
            Launcher.Open(_matches[_sel]);
            _box.Text = "";
        }

        private sealed class EmptyToCollapsed : System.Windows.Data.IValueConverter
        {
            public object Convert(object v, Type t, object p, System.Globalization.CultureInfo c) => string.IsNullOrEmpty(v as string) ? Visibility.Collapsed : Visibility.Visible;
            public object ConvertBack(object v, Type t, object p, System.Globalization.CultureInfo c) => throw new NotSupportedException();
        }
    }

    /// <summary>Unelte rapide: screenshot, area, text from screen, free RAM (the notch hides itself first).</summary>
    internal sealed class QuickToolsWidget : Widget
    {
        private readonly TextBlock _ram;
        public override Thickness CardPadding => new Thickness(6);

        public QuickToolsWidget(NotchWindow w, WidgetSlot s) : base(w, s)
        {
            _ram = Ui.T("", 11, "DimBrush");
            var items = new (string Glyph, string Label, string Sub, string Tip, Action Act)[]
            {
                (Ui.GCamera, "Captură", "ecran", "Tot monitorul, salvat și copiat", () => W.ScreenshotFull()),
                (Ui.GCrop, "Zonă", "Win+Alt+S", "Tragi peste o zonă", () => W.ScreenshotArea()),
                (Ui.GText, "Text", "Win+Alt+T", "Copiază textul dintr-o zonă a ecranului", () => W.TextFromScreen()),
                (Ui.GMemory, "RAM", "", "Eliberează memoria", () => W.OptimizeMemory()),
            };
            bool grid2 = Cw <= 2;
            var g = new System.Windows.Controls.Primitives.UniformGrid { Rows = grid2 ? 2 : 1, Columns = grid2 ? 2 : 4 };
            foreach (var it in items)
            {
                var sub = it.Glyph == Ui.GMemory ? _ram : Ui.T(it.Sub, 11, "DimBrush");
                var content = grid2
                    ? (UIElement)Ui.V(2, Ui.Icon(it.Glyph, 16), Ui.T(it.Label, 11.5, "InkBrush", true).Also(t => t.HorizontalAlignment = HorizontalAlignment.Center))
                    : Ui.H(8, Ui.Icon(it.Glyph, 15), Ui.V(0, Ui.T(it.Label, 12.5, "InkBrush", true), sub));
                var b = new Button { Style = Ui.S("TileButton"), ToolTip = it.Tip, Margin = new Thickness(2), Padding = new Thickness(8, 2, 6, 2), Content = content,
                                     HorizontalContentAlignment = grid2 ? HorizontalAlignment.Center : HorizontalAlignment.Left, VerticalContentAlignment = VerticalAlignment.Center };
                var act = it.Act;
                b.Click += (o, e) => act();
                g.Children.Add(b);
            }
            Children.Add(g);
            Refresh();
        }

        public override void Refresh()
        {
            var (used, total) = MemoryTools.Status();
            _ram.Text = total > 0 ? Math.Round(used * 100.0 / total) + "% folosit" : "";
        }
    }

    /// <summary>Clipboard: the last copied texts; click copies again, pin keeps one. 3×3 adds search.</summary>
    internal sealed class ClipboardWidget : Widget
    {
        private readonly StackPanel _list = new StackPanel();
        private readonly TextBox _search;
        private string _sig;

        public ClipboardWidget(NotchWindow w, WidgetSlot s) : base(w, s)
        {
            var g = Ui.Rows(Ui.Auto, Ui.Px(6), Ui.Star());
            var head = Ui.Cols(Ui.Auto, Ui.Star(), Ui.Auto);
            head.Put(Cap("CLIPBOARD"));
            if (Ch >= 3)
            {
                _search = new TextBox { Style = Ui.S("DarkBox"), FontSize = 11.5, Width = 130 };
                _search.PreviewMouseDown += (o, e) => W.EnableTyping(_search);
                _search.TextChanged += (o, e) => { _sig = null; Refresh(); };
                head.Put(new Border { Background = Ui.B("ChipHoverBrush"), CornerRadius = new CornerRadius(7), Padding = new Thickness(7, 0, 7, 0), Height = 24, Child = _search, ToolTip = "Caută în istoric" }, 2);
            }
            g.Put(head);
            g.Put(new ScrollViewer { Style = Ui.S("SlimScroll"), Content = _list }, 0, 2);
            Children.Add(g);
            Refresh();
        }

        public override void Refresh()
        {
            string q = _search?.Text?.Trim() ?? "";
            var clips = W.Clips.Where(c => q.Length == 0 || c.Text.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            string sig = q + "|" + string.Join("|", clips.Select(c => c.Text.GetHashCode() + "" + c.Pinned)) + DateTime.Now.Minute;
            if (sig == _sig) return;
            _sig = sig;
            _list.Children.Clear();
            if (clips.Count == 0) { _list.Children.Add(Ui.T(q.Length > 0 ? "Nimic găsit" : "Copiază un text și apare aici", 12, "DimBrush")); return; }
            foreach (var c in clips.OrderByDescending(c => c.Pinned))
            {
                string head = c.Text.Length > 200 ? c.Text.Substring(0, 200) : c.Text;
                string preview = head.Replace("\r", " ").Replace("\n", " ").Trim();
                var text = Ui.T(preview, 11.5, "InkBrush", false, true);
                var meta = Ui.T(c.Pinned ? "fixat" : Ui.Ago(c.At), 11, "DimBrush");
                var row = Ui.Cols(Ui.Star(), Ui.Px(6), Ui.Auto, Ui.Px(2), Ui.Px(24));
                row.Put(text); row.Put(meta, 2);
                var item = c;
                var pin = Ui.IconBtn(c.Pinned ? Ui.GUnpin : Ui.GPin, () => { item.Pinned = !item.Pinned; W.SavePins(); _sig = null; Refresh(); }, c.Pinned ? "Desprinde" : "Fixează", 24, 11, Ui.B("DimBrush"));
                if (c.Pinned) ((TextBlock)pin.Content).SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
                row.Put(pin, 4);
                text.OnClick(() =>
                {
                    W.CopyToClipboard(item.Text);
                    text.Text = "Copiat ✓";
                    var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
                    t.Tick += (o, e) => { t.Stop(); text.Text = preview; };
                    t.Start();
                });
                text.ToolTip = c.Text.Length > 400 ? c.Text.Substring(0, 400) + "…" : c.Text;
                _list.Children.Add(new Border { Background = Ui.B("ChipHoverBrush"), CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 2, 2, 2), Margin = new Thickness(0, 0, 4, 4), Child = row });
            }
        }
    }

    /// <summary>Notiță: the same note as on Unelte, saved automatically.</summary>
    internal sealed class NoteWidget : Widget
    {
        private readonly TextBox _box;
        private readonly DispatcherTimer _save = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        public override Thickness CardPadding => new Thickness(4);

        public NoteWidget(NotchWindow w, WidgetSlot s) : base(w, s)
        {
            _box = new TextBox
            {
                Style = Ui.S("DarkBox"), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalContentAlignment = VerticalAlignment.Top,
                VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto, Padding = new Thickness(8, 6, 8, 6), Text = W.S.Note ?? ""
            };
            _box.PreviewMouseDown += (o, e) => W.EnableTyping(_box);
            _box.TextChanged += (o, e) => { W.S.Note = _box.Text; _save.Stop(); _save.Start(); };
            _save.Tick += (o, e) => { _save.Stop(); W.S.Save(); };
            Unloaded += (o, e) => { if (_save.IsEnabled) { _save.Stop(); W.S.Save(); } };
            Children.Add(_box);
        }

        public override void Refresh()
        {
            if (!_box.IsKeyboardFocused && _box.Text != (W.S.Note ?? "")) _box.Text = W.S.Note ?? "";
        }
    }

    /// <summary>Spații de lucru: one click reopens a saved arrangement of apps; "+" saves the current one.</summary>
    internal sealed class WorkspacesWidget : Widget
    {
        private readonly Grid _row = new Grid();
        private int _count = -1;
        public override Thickness CardPadding => new Thickness(6);

        public WorkspacesWidget(NotchWindow w, WidgetSlot s) : base(w, s) { Children.Add(_row); Refresh(); }

        public override void Refresh()
        {
            var list = W.S.Workspaces;
            string sig = string.Join("|", list.Select(x => x.Name + x.Windows.Count));
            if (sig.GetHashCode() == _count) return;
            _count = sig.GetHashCode();
            _row.Children.Clear(); _row.ColumnDefinitions.Clear();
            int max = Math.Max(1, Cw - 1);
            var shown = list.Take(max).ToList();
            foreach (var ws in shown)
            {
                _row.ColumnDefinitions.Add(new ColumnDefinition { Width = Ui.Star() });
                var target = ws;
                var b = new Button { Style = Ui.S("TileButton"), Margin = new Thickness(2), Padding = new Thickness(8, 2, 8, 2), ToolTip = "Redeschide aplicațiile, așezate cum le-ai salvat",
                                     Content = Ui.V(0, Ui.T(ws.Name, 12.5, "InkBrush", true), Ui.T(ws.Windows.Count + " aplicații", 11, "DimBrush")),
                                     HorizontalContentAlignment = HorizontalAlignment.Left, VerticalContentAlignment = VerticalAlignment.Center };
                b.Click += async (o, e) => await WindowTools.RestoreAsync(target, W.Hwnd);
                Grid.SetColumn(b, _row.ColumnDefinitions.Count - 1);
                _row.Children.Add(b);
            }
            _row.ColumnDefinitions.Add(new ColumnDefinition { Width = shown.Count == 0 ? Ui.Star() : Ui.Px(44) });
            var add = new Button { Style = Ui.S("TileButton"), Margin = new Thickness(2), ToolTip = "Salvează aranjarea de acum",
                                   Content = shown.Count == 0 ? (object)Ui.T("+ salvează aranjarea de acum", 12, "MutedBrush") : Ui.Icon("", 14) };
            add.Click += (o, e) =>
            {
                string[] names = { "Lucru", "Gaming", "Seară", "Studiu", "Proiect" };
                string name = names.FirstOrDefault(n => !W.S.Workspaces.Any(x => x.Name == n)) ?? "Spațiu " + (W.S.Workspaces.Count + 1);
                var ws = WindowTools.Capture(W.Hwnd, name);
                if (ws.Windows.Count == 0) return;
                W.S.Workspaces.Add(ws);
                W.S.Save();
                _count = -1;
                Refresh();
            };
            Grid.SetColumn(add, _row.ColumnDefinitions.Count - 1);
            _row.Children.Add(add);
        }
    }

    /// <summary>Fereastra activă: keep on top, other monitor, half, mini.</summary>
    internal sealed class ActiveWindowWidget : Widget
    {
        private readonly TextBlock _cap;
        public ActiveWindowWidget(NotchWindow w, WidgetSlot s) : base(w, s)
        {
            _cap = Cap("FEREASTRA ACTIVĂ");
            var g = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1, Columns = 4 };
            foreach (var (glyph, label, tip, act) in new (string, string, string, Action<IntPtr>)[]
            {
                ("", "Deasupra", "Ține fereastra peste toate celelalte", WindowTools.ToggleTopmost),
                ("", "Monitor 2", "Mut-o pe celălalt monitor", WindowTools.MoveToNextMonitor),
                ("", "Jumătate", "Jumătate stânga / dreapta", WindowTools.SnapHalf),
                ("", "Mini", "Mică, în colțul din dreapta jos", WindowTools.Mini)
            })
            {
                var b = new Button { Style = Ui.S("TileButton"), Margin = new Thickness(2), ToolTip = tip, Content = Ui.H(5, Ui.Icon(glyph, 12, Ui.B("MutedBrush")), Ui.T(label, 11.5)),
                                     HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
                var a = act;
                b.Click += (o, e) => { var h = W.LastForeground; if (h != IntPtr.Zero) try { a(h); } catch { } };
                g.Children.Add(b);
            }
            Children.Add(Ui.Rows(Ui.Auto, Ui.Px(4), Ui.Star()).Also(r => { r.Put(_cap); r.Put(g, 0, 2); }));
            Refresh();
        }
        public override void Refresh()
        {
            var h = W.LastForeground;
            string name = h != IntPtr.Zero ? WindowTools.AppName(h) : "";
            _cap.Text = "FEREASTRA ACTIVĂ" + (name.Length > 0 ? " · " + name.ToUpperInvariant() : "");
        }
    }
}
