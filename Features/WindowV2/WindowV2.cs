using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using WinNotch.Core.Actions;
using WinNotch.Core.Flags;
using WinNotch.Features.NotchAnchored;
using WinNotch.Services;

namespace WinNotch.Features.WindowV2
{
    /// <summary>
    /// P52: the WinNotch window, version 2. Its signature is the header: the same silhouette as the anchored notch
    /// (P50) — stuck to the top edge, rounded only at the bottom, with the two concave fillets — so the window reads as
    /// the same object as the notch in the screen's edge. Everything else is quiet: a 240 px sidebar of categories, a
    /// centre with the search field and cards built from the <b>existing</b> action registry, and a 300 px right column
    /// with what the app already knows (clipboard, privacy). All the decisions about sizes and columns are in
    /// <see cref="LayoutRules"/> (pure, tested). Nothing here invents a feature: the cards are actions that exist, and
    /// the pages / themes / settings / news open the classic window, exactly as before.
    /// </summary>
    public sealed class WindowV2 : Window
    {
        private readonly AppSettings _s;
        private readonly NotchWindow _notch;
        private readonly Grid _sidebar = new Grid();
        private readonly StackPanel _sidebarItems = new StackPanel();
        private readonly StackPanel _center = new StackPanel();
        private readonly StackPanel _right = new StackPanel { Width = LayoutRules.RightWidth };
        private readonly Border _rightHost;
        private readonly TextBlock _hint = Ui.T("", 12, "MutedBrush");
        private readonly TextBox _search = new TextBox { FontSize = 13.5, BorderThickness = new Thickness(0), Background = Brushes.Transparent, MinWidth = 200 };
        private readonly Path _headerShape = new Path { IsHitTestVisible = false };
        private readonly TextBlock _clock = Ui.T("--:--", 12, "MutedBrush", false, true);
        private readonly System.Windows.Threading.DispatcherTimer _tick =
            new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        private readonly StackPanel _cards = new StackPanel();
        private UIElement _searchCard;
        private string _category = LayoutRules.DefaultCategory;
        private Grid _body;
        private int _cols = -1;
        private Action<string> _flagHandler;

        public WindowV2(AppSettings s, NotchWindow notch)
        {
            _s = s; _notch = notch;
            Title = "WinNotch";
            MinWidth = LayoutRules.MinWidth;
            MinHeight = LayoutRules.MinHeight;
            var wa = SystemParameters.WorkArea;
            Width = Math.Min(1400, wa.Width * 0.9);
            Height = Math.Min(900, wa.Height * 0.88);
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;
            FontFamily = (FontFamily)Application.Current.FindResource("UiFont");
            SetResourceReference(ForegroundProperty, "InkBrush");
            SetResourceReference(BackgroundProperty, "SegBrush");       // opaque: NotchBrush carries the pill's own transparency

            _rightHost = new Border { Child = _right, Padding = new Thickness(LayoutRules.Gap, 0, 0, 0) };
            Content = BuildShell();
            SizeChanged += (o, e) => Relayout();
            PreviewKeyDown += OnKey;
            _tick.Tick += (o, e) => OnTick();
            Loaded += (o, e) => { OnTick(); _tick.Start(); Relayout(); };
            StateChanged += (o, e) => { if (WindowState == WindowState.Minimized) _tick.Stop(); else { OnTick(); _tick.Start(); } };
            Closed += (o, e) =>
            {
                _tick.Stop();
                if (_flagHandler != null && FeatureFlags.Current != null) FeatureFlags.Current.Changed -= _flagHandler;
                _flagHandler = null;
            };
            // The switch can be turned off from Settings while the window is open: then it closes, like any feature stopping.
            _flagHandler = id =>
            {
                if (!string.Equals(id, LayoutRules.FeatureId, StringComparison.Ordinal)) return;
                Dispatcher.InvokeAsync(() => { if (!(FeatureFlags.Current?.IsEnabled(LayoutRules.FeatureId) ?? false)) Close(); });
            };
            if (FeatureFlags.Current != null) FeatureFlags.Current.Changed += _flagHandler;
        }

        /// <summary>Opens the window on a category; the old window's ids ("themes", "settings", "news") and page ids still work.</summary>
        public void Open(string pageId = null)
        {
            try
            {
                _category = LayoutRules.CategoryFor(pageId);
                BuildSidebar();
                BuildCenter();
                BuildRight();
                Relayout();
            }
            catch (Exception ex) { FeatureFlags.Current?.ReportError(LayoutRules.FeatureId, ex); }
        }

        /// <summary>The clock and the right column, every 10 s and only while the window is really on screen.</summary>
        private void OnTick()
        {
            try
            {
                if (!IsVisible || WindowState == WindowState.Minimized) return;
                _clock.Text = DateTime.Now.ToString("HH:mm");
                BuildRight();
            }
            catch (Exception ex) { FeatureFlags.Current?.ReportError(LayoutRules.FeatureId, ex); }
        }

        // ------------------------------------------------------------------ the shell

        private UIElement BuildShell()
        {
            var root = Ui.Rows(Ui.Px(LayoutRules.HeaderHeight), Ui.Star(), Ui.Auto);
            root.Put(BuildHeader());

            _body = Ui.Cols(Ui.Px(LayoutRules.SidebarWidth), Ui.Star(), Ui.Auto);
            _sidebar.Children.Add(new ScrollViewer { Style = Ui.S("SlimScroll"), Content = _sidebarItems, Padding = new Thickness(12, 12, 12, 12) });
            var sideHost = new Border { Child = _sidebar, CornerRadius = new CornerRadius(0, LayoutRules.CardRadius, LayoutRules.CardRadius, 0) };
            sideHost.SetResourceReference(Border.BackgroundProperty, "ChipBrush");
            _body.Put(sideHost);
            _body.Put(new ScrollViewer { Style = Ui.S("SlimScroll"), Content = _center, Padding = new Thickness(LayoutRules.Pad, LayoutRules.Pad, LayoutRules.Pad, LayoutRules.Pad) }, 1);
            _body.Put(_rightHost, 2);
            root.Put(_body, 0, 1);

            var bar = new Border { Padding = new Thickness(LayoutRules.Pad, 10, LayoutRules.Pad, 12) };
            bar.SetResourceReference(Border.BackgroundProperty, "ChipBrush");
            var hintRow = Ui.Cols(Ui.Auto, Ui.Star(), Ui.Auto);
            hintRow.Put(Ui.Icon("", 12, Ui.B("MutedBrush")));
            _hint.Margin = new Thickness(8, 0, 0, 0);
            hintRow.Put(_hint, 1);
            hintRow.Put(Ui.Chip(Ui.T("Win + Alt + Space", 11, "DimBrush"), 8, 3, LayoutRules.ChipRadius), 2);
            bar.Child = hintRow;
            root.Put(bar, 0, 2);
            return root;
        }

        /// <summary>The header is the notch, unfolded: the same silhouette as P50, with the tabs of the notch.</summary>
        private UIElement BuildHeader()
        {
            var host = new Grid();
            _headerShape.SetResourceReference(Shape.FillProperty, "NotchBrush");
            _headerShape.HorizontalAlignment = HorizontalAlignment.Center;
            _headerShape.VerticalAlignment = VerticalAlignment.Top;
            host.Children.Add(_headerShape);

            var row = Ui.Cols(Ui.Auto, Ui.Star(), Ui.Auto);
            row.Margin = new Thickness(LayoutRules.Pad, 0, LayoutRules.Pad, 0);
            var tabs = Ui.H(4);
            foreach (var (id, title, glyph) in new[]
                     {
                         ("actiuni", "Acasă", ""), ("sistem", "Sistem", "\uE713"),
                         ("sunet", "Dispozitive", "\uE767"), ("captura", "Unelte", "\uE722"),
                     })
                tabs.Children.Add(HeaderTab(id, title, glyph));
            row.Put(tabs);

            var rightSide = Ui.H(8, Ui.IconBtn("", ToggleTheme, "Schimbă tema", 28, 13), _clock);
            rightSide.VerticalAlignment = VerticalAlignment.Center;
            row.Put(rightSide, 2);
            row.VerticalAlignment = VerticalAlignment.Center;
            host.Children.Add(row);
            return host;
        }

        private Button HeaderTab(string id, string title, string glyph)
        {
            var btn = new Button { Style = Ui.S("IconButton"), Padding = new Thickness(10, 4, 10, 4), Cursor = Cursors.Hand };
            var content = Ui.H(6, Ui.Icon(glyph, 12, Ui.B(_category == id ? "InkBrush" : "MutedBrush")),
                               Ui.T(title, 12.5, _category == id ? "InkBrush" : "MutedBrush", _category == id));
            content.VerticalAlignment = VerticalAlignment.Center;
            btn.Content = content;
            btn.Click += (o, e) => Select(id);
            return btn;
        }

        /// <summary>Light ↔ dark, through the existing theme manager (the window takes the app's theme, like everything else).</summary>
        private void ToggleTheme()
        {
            _s.ThemeMode = string.Equals(_s.ThemeMode, "light", StringComparison.Ordinal) ? "dark" : "light";
            _s.Save();
            ThemeManager.Apply(_s);
            _notch?.ApplySettings();
        }

        // ------------------------------------------------------------------ sidebar

        private void BuildSidebar()
        {
            _sidebarItems.Children.Clear();
            _sidebarItems.Children.Add(Ui.Cap("TOATE UNELTELE"));
            foreach (var c in LayoutRules.Categories) _sidebarItems.Children.Add(SidebarRow(c));
        }

        private UIElement SidebarRow(V2Category c)
        {
            bool on = string.Equals(c.Id, _category, StringComparison.Ordinal);
            var mark = new Border { Width = 3, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 2, 8, 2) };
            if (on) mark.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
            var row = Ui.Cols(Ui.Px(3), Ui.Auto, Ui.Star());
            row.Put(mark);
            row.Put(Ui.Icon(c.Glyph, 13, Ui.B(on ? "InkBrush" : "MutedBrush")), 1);
            var label = Ui.T(c.Title, 13.5, on ? "InkBrush" : "MutedBrush", on);
            label.Margin = new Thickness(8, 0, 0, 0);
            label.VerticalAlignment = VerticalAlignment.Center;
            row.Put(label, 2);
            // A Button, not a Border: it takes the focus with Tab and answers Enter and Space, as the brief asks.
            var host = new Button
            {
                Style = Ui.S("IconButton"), Content = row, Padding = new Thickness(4, 7, 8, 7),
                Cursor = Cursors.Hand, Margin = new Thickness(0, 2, 0, 2),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
            };
            System.Windows.Automation.AutomationProperties.SetName(host, c.Title);
            if (on) host.SetResourceReference(Control.BackgroundProperty, "ChipHoverBrush");
            host.Click += (o, e) => Select(c.Id);
            return host;
        }

        private void Select(string categoryId)
        {
            try
            {
                if (LayoutRules.Find(categoryId) == null || string.Equals(categoryId, _category, StringComparison.Ordinal)) return;
                _category = categoryId;
                BuildSidebar();
                BuildCenter();
                Relayout();
            }
            catch (Exception ex) { FeatureFlags.Current?.ReportError(LayoutRules.FeatureId, ex); }
        }

        // ------------------------------------------------------------------ centre

        /// <summary>
        /// Only the cards are rebuilt; the search card is built once and stays in the tree (a WPF element has exactly one
        /// parent, so putting it into a new row would throw — that is what kept v2 from opening at all).
        /// </summary>
        private void BuildCenter()
        {
            if (_searchCard == null)
            {
                _searchCard = BuildSearch();
                _center.Children.Add(_searchCard);
                _center.Children.Add(_cards);
            }
            _cards.Children.Clear();
            _cols = LayoutRules.Columns(CenterWidth());
            if (LayoutRules.IsClassicContent(_category)) { _cards.Children.Add(ClassicCard()); return; }
            var cat = LayoutRules.Find(_category);
            var actions = VisibleActions().Where(a => string.Equals(LayoutRules.CategoryForAction(a.Category), _category, StringComparison.Ordinal)).ToList();
            _cards.Children.Add(Group(cat?.Title ?? "Acțiuni", actions));
        }

        private UIElement BuildSearch()
        {
            var row = Ui.Cols(Ui.Auto, Ui.Star(), Ui.Auto);
            row.Put(Ui.Icon("", 13, Ui.B("MutedBrush")));
            _search.Margin = new Thickness(8, 0, 8, 0);
            _search.SetResourceReference(Control.ForegroundProperty, "InkBrush");
            _search.SetResourceReference(TextBoxBase.CaretBrushProperty, "InkBrush");
            _search.VerticalAlignment = VerticalAlignment.Center;
            row.Put(_search, 1);
            row.Put(Ui.Chip(Ui.T("Ctrl + K", 11, "DimBrush"), 8, 3, LayoutRules.ChipRadius), 2);
            var card = new Border { Child = row, Padding = new Thickness(14, 10, 10, 10), CornerRadius = new CornerRadius(LayoutRules.CardRadius), Margin = new Thickness(0, 0, 0, LayoutRules.Pad) };
            card.SetResourceReference(Border.BackgroundProperty, "ChipBrush");
            return card;
        }

        private UIElement ClassicCard()
        {
            var cat = LayoutRules.Find(_category);
            var text = Ui.V(4, Ui.T(cat?.Title ?? "", 15, "InkBrush", true),
                            Ui.T("Se deschide în fereastra clasică, neschimbată.", 12, "MutedBrush"));
            var open = Ui.PillBtn("Deschide", () => ((App)Application.Current).OpenClassicEditor(ClassicPageId()), true);
            open.HorizontalAlignment = HorizontalAlignment.Left;
            open.Margin = new Thickness(0, LayoutRules.Gap, 0, 0);
            return Ui.Card(Ui.V(0, text, open), 16, 14, LayoutRules.CardRadius);
        }

        private string ClassicPageId() => _category switch
        {
            "teme" => "themes",
            "setari" => "settings",
            "noutati" => "news",
            _ => null,
        };

        private IReadOnlyList<ActionDescriptor> VisibleActions()
        {
            var reg = ActionRegistry.Current;
            if (reg == null) return Array.Empty<ActionDescriptor>();
            // Every registered action, in the registry's own order; it refuses the ones that are off or unavailable at invoke time.
            try { return reg.All.Where(a => (a.AllowedInvokers & ActionInvoker.UI) == ActionInvoker.UI).ToList(); }
            catch { return Array.Empty<ActionDescriptor>(); }
        }

        private UIElement Group(string title, IReadOnlyList<ActionDescriptor> actions)
        {
            var box = Ui.V(0, Ui.Cap(title.ToUpperInvariant()));
            if (actions.Count == 0)
            {
                box.Children.Add(Ui.T("Nimic aici deocamdată: pornește funcțiile din Setări → funcții noi.", 12.5, "MutedBrush"));
                return box;
            }
            var grid = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            int cols = LayoutRules.Columns(CenterWidth());
            for (int i = 0; i < cols; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = Ui.Star() });
            for (int i = 0; i < actions.Count; i++)
            {
                int row = i / cols;
                if (grid.RowDefinitions.Count <= row) grid.RowDefinitions.Add(new RowDefinition { Height = Ui.Auto });
                var card = ActionCard(actions[i]);
                Grid.SetColumn(card, i % cols);
                Grid.SetRow(card, row);
                grid.Children.Add(card);
            }
            box.Children.Add(grid);
            return box;
        }

        private UIElement ActionCard(ActionDescriptor a)
        {
            var icon = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(LayoutRules.ChipRadius), Child = Ui.Icon(string.IsNullOrEmpty(a.Icon) ? "" : a.Icon, 16) };
            icon.SetResourceReference(Border.BackgroundProperty, "TrackBrush");      // the family colour at ~12 %, from the theme
            icon.HorizontalAlignment = HorizontalAlignment.Left;
            var title = Ui.T(a.Title, 13.5, "InkBrush", true);
            title.TextWrapping = TextWrapping.Wrap;
            var desc = Ui.T(a.Category ?? "", 12, "MutedBrush");
            desc.TextWrapping = TextWrapping.Wrap;
            var run = Ui.PillBtn(a.Safety == ActionSafety.Safe ? "Pornește" : "Pornește…", () => Run(a), true);
            run.HorizontalAlignment = HorizontalAlignment.Left;
            run.Margin = new Thickness(0, LayoutRules.Gap, 0, 0);
            var card = Ui.Card(Ui.V(8, icon, title, desc, run), 14, 14, LayoutRules.CardRadius);
            card.Margin = new Thickness(0, 0, LayoutRules.Gap, LayoutRules.Gap);
            return card;
        }

        private void Run(ActionDescriptor a)
        {
            // The registry does the checking: a Confirm/Dangerous action is asked about first, and only then confirmed.
            bool confirmed = a.Safety == ActionSafety.Safe ||
                             MessageBox.Show(this, a.Title + "?", "WinNotch", MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK;
            if (!confirmed) return;
            var task = ActionRegistry.Current?.InvokeAsync(a.Id, null, ActionInvoker.UI, default, confirmed);
            if (task == null) return;
            // The registry says why it refused (a switch off, a missing parameter, nothing available): the user sees it.
            _ = task.ContinueWith(t =>
            {
                string msg = t.IsCompletedSuccessfully ? t.Result?.Message : null;
                if (!string.IsNullOrEmpty(msg)) Dispatcher.InvokeAsync(() => _hint.Text = msg);
            }, System.Threading.Tasks.TaskScheduler.Default);
        }

        // ------------------------------------------------------------------ right column

        private void BuildRight()
        {
            _right.Children.Clear();
            var clips = _s.PinnedClips ?? new List<string>();
            _right.Children.Add(Ui.Cap("CLIPBOARD"));
            _right.Children.Add(Ui.T(clips.Count == 0 ? "Nimic fixat" : clips.Count + (clips.Count == 1 ? " element fixat" : " elemente fixate"), 12, "MutedBrush"));
            foreach (var c in clips.Take(3))
            {
                var t = Ui.T(c.Length > 60 ? c.Substring(0, 59) + "…" : c, 12, "InkBrush");
                t.TextWrapping = TextWrapping.Wrap;
                var card = Ui.Card(t, 12, 10, LayoutRules.ChipRadius);
                card.Margin = new Thickness(0, 6, 0, 0);
                _right.Children.Add(card);
            }

            _right.Children.Add(Ui.Cap("CONFIDENȚIALITATE"));
            _right.Children.Add(PrivacyRow("Microfon", PrivacyService.Microphone()));
            _right.Children.Add(PrivacyRow("Cameră", PrivacyService.Camera()));
        }

        private UIElement PrivacyRow(string name, CapabilityUse use)
        {
            var row = Ui.Cols(Ui.Star(), Ui.Auto);
            row.Put(Ui.T(name, 12.5, "InkBrush"));
            var state = Ui.T(use != null && use.InUse ? "în folosință" : "oprit", 12, use != null && use.InUse ? "WarnBrush" : "MutedBrush");
            row.Put(state, 1);
            row.Margin = new Thickness(0, 6, 0, 0);
            return row;
        }

        // ------------------------------------------------------------------ layout

        private double CenterWidth()
        {
            // One source of truth: both decisions are read from the window's width, not from an already shrunk one.
            double full = ActualWidth > 0 ? ActualWidth : Width;
            double w = full;
            if (!LayoutRules.SingleColumn(full)) w -= LayoutRules.SidebarWidth;
            if (LayoutRules.ShowRightColumn(full)) w -= LayoutRules.RightWidth;
            return Math.Max(200, w - 2 * LayoutRules.Pad);
        }

        private void Relayout()
        {
            try
            {
                double w = ActualWidth > 0 ? ActualWidth : Width;
                _body.ColumnDefinitions[0].Width = LayoutRules.SingleColumn(w) ? new GridLength(0) : Ui.Px(LayoutRules.SidebarWidth);
                _rightHost.Visibility = LayoutRules.ShowRightColumn(w) ? Visibility.Visible : Visibility.Collapsed;
                _hint.Text = LayoutRules.Hint(FeatureFlags.Current?.IsEnabled(Features.CommandBar.CommandBarRules.FeatureId) ?? false);
                DrawHeader(w);
                // The cards are rebuilt only when the number of columns really changes, not on every frame of a resize.
                if (_cards.Children.Count == 0 || LayoutRules.Columns(CenterWidth()) != _cols) BuildCenter();
            }
            catch (Exception ex) { FeatureFlags.Current?.ReportError(LayoutRules.FeatureId, ex); }
        }

        /// <summary>The header's silhouette: the anchored notch's shape (P50), rebuilt on resize and frozen.</summary>
        private void DrawHeader(double windowWidth)
        {
            double w = Math.Max(240, Math.Min(windowWidth - 2 * LayoutRules.Pad, windowWidth));
            double h = LayoutRules.HeaderHeight;
            double r = AnchoredGeometry.Radius(_s.CornerRadius);
            double e = AnchoredGeometry.Ear(_s.CornerRadius, w, windowWidth);
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(0, 0), true, true);
                if (e > 0) c.ArcTo(new Point(e, e), new Size(e, e), 0, false, SweepDirection.Clockwise, false, false);
                c.LineTo(new Point(e, h - r), false, false);
                if (r > 0) c.ArcTo(new Point(e + r, h), new Size(r, r), 0, false, SweepDirection.Counterclockwise, false, false);
                c.LineTo(new Point(e + w - r, h), false, false);
                if (r > 0) c.ArcTo(new Point(e + w, h - r), new Size(r, r), 0, false, SweepDirection.Counterclockwise, false, false);
                c.LineTo(new Point(e + w, e), false, false);
                if (e > 0) c.ArcTo(new Point(e + w + e, 0), new Size(e, e), 0, false, SweepDirection.Clockwise, false, false);
            }
            g.Freeze();
            _headerShape.Width = w + 2 * e;
            _headerShape.Height = h;
            _headerShape.Data = g;
        }

        private void OnKey(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) { Close(); e.Handled = true; }
            else if (e.Key == Key.K && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                _search.Focus();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && _search.IsKeyboardFocusWithin && !string.IsNullOrWhiteSpace(_search.Text))
            {
                // the existing Command Bar does the searching: no second system
                var hit = VisibleActions().FirstOrDefault(a => (a.Title ?? "").Contains(_search.Text, StringComparison.OrdinalIgnoreCase));
                if (hit != null) Run(hit);
                e.Handled = true;
            }
        }
    }
}
