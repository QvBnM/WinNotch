using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WinNotch.Panes;
using WinNotch.Widgets;

namespace WinNotch
{
    /// <summary>
    /// Editorul de widget-uri și teme: pages on the left (standard pages with eye / duplicate, your pages with
    /// rename, icon, order, delete), the page itself in the middle (live, editable: drag, resize, remove), the selected
    /// widget's size and settings on the right, the gallery underneath; and a "Teme și culori" page.
    /// </summary>
    internal sealed class EditorWindow : Window
    {
        // the editor's own chrome is light, whatever the notch theme is
        static readonly Brush Bg = Ui.Rgb(0xF6, 0xF7, 0xF9), Panel = Brushes.White, Line = Ui.Rgb(0xE3, 0xE6, 0xEA),
                              Ink = Ui.Rgb(0x15, 0x17, 0x1A), Muted = Ui.Rgb(0x5B, 0x62, 0x6D), Dim = Ui.Rgb(0x8A, 0x90, 0x99),
                              Blue = Ui.Rgb(0x25, 0x63, 0xEB), SelBg = Ui.Rgb(0xEA, 0xF1, 0xFF), Red = Ui.Rgb(0xB9, 0x1C, 0x1C), HoverBg = Ui.Rgb(0xF0, 0xF2, 0xF5);

        private readonly AppSettings S;
        private readonly NotchWindow N;
        private readonly StackPanel _side = new StackPanel();
        private readonly Border _center = new Border();
        private readonly Border _inspector = new Border { Width = 300, Padding = new Thickness(18), BorderThickness = new Thickness(1, 0, 0, 0) };
        private readonly DispatcherTimer _tick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        private readonly DispatcherTimer _saveSoon = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        private readonly DispatcherTimer _themeSoon = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        private Action _pending;
        private string _sel = "home";              // a page id, "std:<id>" or "themes"
        private WidgetPage _page;
        private Gallery _gallery;
        private TextBlock _inspectorMsg;
        private int _ticks;
        private ScrollViewer _centerScroll;
        private Grid _overlay;
        /// <summary>P51: the open pop-up (a widget's sizes), so Esc closes it too — here the window has real focus.</summary>
        private (Action Hide, Action Close) _edPopup;
        private SettingsWindow _settings;          // its content is shown on the "Setări" page
        /// <summary>P52: the window that owns the file dialogs — this one normally, the host when the content is embedded.</summary>
        private Window _dialogOwner;

        /// <summary>
        /// P52: the pages editor, shown inside the new WinNotch window instead of a window of its own — the same move
        /// <see cref="SettingsWindow.TakeContent"/> already makes for the settings. One editor, two windows: nothing is
        /// rewritten, this window is simply never shown. <see cref="DetachContent"/> gives it up again.
        /// </summary>
        internal FrameworkElement TakeContent(Window host)
        {
            _dialogOwner = host ?? this;
            var content = (FrameworkElement)Content;
            Content = null;
            // The window's own chrome does not travel with the content: a host on a dark theme would leave white cards
            // with white text, exactly as it did for the settings page.
            if (content is Panel sheet && sheet.Background == null) sheet.Background = Bg;
            content.SetValue(TextBlock.ForegroundProperty, Ink);
            _tick.Start();                           // Loaded never fires on a window that is not shown
            return content;
        }

        /// <summary>
        /// P51, embedded: the host window owns the keyboard, so Esc is offered here first — it closes the open pop-up
        /// (a widget's sizes) and nothing else. True when something was closed.
        /// </summary>
        internal bool CloseOpenPopup()
        {
            if (_edPopup.Close == null) return false;
            if (!(Core.Flags.FeatureFlags.Current?.IsEnabled(Core.Ui.OverlayStack.FeatureId) ?? false)) return false;
            _edPopup.Close();
            return true;
        }

        /// <summary>Gives up the embedded content: the timers stop, what is pending is saved, this window is released.</summary>
        internal void DetachContent()
        {
            _tick.Stop();
            _themeSoon.Stop();
            FlushPending();
            _settings?.Detach();
            S.Save();
            try { Close(); } catch { }               // never shown: closing just releases it
        }

        public EditorWindow(AppSettings s, NotchWindow n)
        {
            S = s; N = n;
            Title = "WinNotch";
            // P51: Esc closes the open pop-up (and only it); without one, the key goes where it did before
            PreviewKeyDown += (o, e) =>
            {
                if (e.Key != System.Windows.Input.Key.Escape || _edPopup.Close == null) return;
                // the switch is read here, not at subscription, so turning it off takes effect right away (and in --safe-mode)
                if (!(Core.Flags.FeatureFlags.Current?.IsEnabled(Core.Ui.OverlayStack.FeatureId) ?? false)) return;
                _edPopup.Close();
                e.Handled = true;
            };
            // size and place: see PlaceOnScreen (wide by default, always fully on the screen you're using)
            var wa = SystemParameters.WorkArea;
            Width = Math.Min(1500, wa.Width * 0.94); Height = wa.Height * 0.9;
            MinWidth = Math.Min(900, wa.Width - 40); MinHeight = Math.Min(560, wa.Height - 40);
            WindowStartupLocation = WindowStartupLocation.Manual;
            SourceInitialized += (o, e) => PlaceOnScreen();
            bool placed = false;
            Loaded += (o, e) => { if (!placed) { placed = true; PlaceOnScreen(); } };    // again, once the monitor's scaling is known
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
            FontSize = 13;
            Background = Bg;
            UseLayoutRounding = true;

            var root = new Grid();
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = Ui.Px(250) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = Ui.Star() });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = Ui.Auto });
            var sideHost = new Border { Background = Panel, BorderBrush = Line, BorderThickness = new Thickness(0, 0, 1, 0),
                                        Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _side, Padding = new Thickness(12, 16, 12, 16) } };
            root.Put(sideHost);
            _centerScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = _center };
            root.Put(_centerScroll, 1);
            _inspector.Background = Panel; _inspector.BorderBrush = Line;
            root.Put(_inspector, 2);
            _overlay = new Grid();                       // pop-ups (a widget's sizes) over the whole window
            Grid.SetColumnSpan(_overlay, 3);
            System.Windows.Controls.Panel.SetZIndex(_overlay, 100);
            root.Children.Add(_overlay);
            Content = root;

            _tick.Tick += (o, e) =>
            {
                if (_page == null) return;
                _page.Fast();
                if (++_ticks % 10 == 0) { _page.MediaChanged(); _page.Refresh(); }
            };
            _saveSoon.Tick += (o, e) => { _saveSoon.Stop(); var a = _pending; _pending = null; a?.Invoke(); };
            _themeSoon.Tick += (o, e) => { _themeSoon.Stop(); ApplyTheme(false); };
            Loaded += (o, e) => _tick.Start();
            Closed += (o, e) => { _tick.Stop(); _themeSoon.Stop(); FlushPending(); _settings?.Detach(); S.Save(); };
        }

        /// <summary>
        /// Centred on the monitor the mouse is on, inside its work area (never under the taskbar or above the top edge,
        /// whatever the scaling of each monitor): wide (94%, at most ~1500 px at 100%) and 90% tall.
        /// </summary>
        internal void PlaceOnScreen()
        {
            try
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                if (hwnd == IntPtr.Zero) return;
                Services.Native.GetCursorPos(out var pt);
                var mon = Services.Native.MonitorFromPoint(pt, 2 /* nearest */);
                var info = new Services.Native.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(Services.Native.MONITORINFO)) };
                if (!Services.Native.GetMonitorInfo(mon, ref info)) return;
                var wa = info.rcWork;
                int waW = wa.Right - wa.Left, waH = wa.Bottom - wa.Top;
                double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
                int w = (int)Math.Min(waW * 0.94, 1500 * dpi), h = (int)(waH * 0.9);
                int x = wa.Left + (waW - w) / 2, y = wa.Top + (waH - h) / 2;
                Services.Native.SetWindowPos(hwnd, IntPtr.Zero, x, y, w, h, Services.Native.SWP_NOZORDER | Services.Native.SWP_NOACTIVATE);
            }
            catch (Exception ex) { App.Log("Fereastra WinNotch, poziție: " + ex.Message); }
        }

        /// <summary>Shows a page (or "themes"), optionally with one of its widgets selected.</summary>
        public void Open(string pageId, string slotId)
        {
            FlushPending();
            if (pageId == "themes" || pageId == "settings" || pageId == "news") _sel = pageId;
            else if (pageId != null && S.Pages.Any(p => p.Id == pageId)) _sel = pageId;
            else if (pageId != null && Catalog.Standard.Any(x => x.Id == pageId)) _sel = "std:" + pageId;
            else if (_sel == null || (_sel != "themes" && _sel != "settings" && _sel != "news" && !_sel.StartsWith("std:") && !S.Pages.Any(p => p.Id == _sel)))
                _sel = S.Pages.FirstOrDefault()?.Id ?? "std:home";
            BuildSide();
            BuildCenter(slotId);
        }

        public void PagesChangedElsewhere()
        {
            BuildSide();
            if (_sel != null && _sel.StartsWith("std:")) BuildCenter(null);
        }

        /// <summary>The page was changed in the notch: rebuild the preview and the inspector from it.</summary>
        public void PageChangedElsewhere(string pageId)
        {
            if (_sel != pageId || _page == null) return;
            var keep = _page.SelectedSlot != null && _page.Page.Widgets.Contains(_page.SelectedSlot) ? _page.SelectedSlot.Id : null;
            FlushPending();
            BuildCenter(keep);
        }

        // =====================================================================
        //  Small controls for the light chrome
        // =====================================================================
        static TextBlock T(string text, double size = 13, Brush fg = null, bool bold = false) => new TextBlock
        {
            Text = text, FontSize = size, Foreground = fg ?? Ink, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center
        };

        static TextBlock Glyph(string g, double size = 14, Brush fg = null) => new TextBlock
        {
            Text = g, FontFamily = Ui.IconFont, FontSize = size, Foreground = fg ?? Ink, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center
        };

        static TextBlock Caption(string text) => new TextBlock { Text = text, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Dim, Margin = new Thickness(6, 14, 0, 6) };

        /// <summary>A clickable area with hover (rows, icon buttons, chips).</summary>
        static Border Click(UIElement child, Action a, string tip = null, Brush bg = null, double radius = 8, Thickness? pad = null)
        {
            var b = new Border { Child = child, Background = bg ?? Brushes.Transparent, CornerRadius = new CornerRadius(radius), Padding = pad ?? new Thickness(6, 4, 6, 4),
                                 Cursor = Cursors.Hand, ToolTip = tip };
            var normal = b.Background;
            b.MouseEnter += (o, e) => { if (b.Background == normal && normal == Brushes.Transparent) b.Background = HoverBg; };
            b.MouseLeave += (o, e) => { if (normal == Brushes.Transparent) b.Background = Brushes.Transparent; };
            bool pressed = false;                     // only a press and a release on the same element is a click
            b.MouseLeftButtonDown += (o, e) => pressed = true;
            b.MouseLeave += (o, e) => pressed = false;
            b.MouseLeftButtonUp += (o, e) => { if (!pressed) return; pressed = false; e.Handled = true; a(); };
            return b;
        }

        /// <summary>Soft, rounded button (the default Windows button is square and grey).</summary>
        static readonly ControlTemplate RoundTemplate = MakeRoundTemplate();
        static ControlTemplate MakeRoundTemplate()
        {
            var border = new FrameworkElementFactory(typeof(Border), "bd");
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
            border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            border.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            border.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding("BorderThickness") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            border.SetBinding(Border.PaddingProperty, new System.Windows.Data.Binding("Padding") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(cp);
            var t = new ControlTemplate(typeof(Button)) { VisualTree = border };
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(UIElement.OpacityProperty, 0.85));
            var pressed = new Trigger { Property = System.Windows.Controls.Primitives.ButtonBase.IsPressedProperty, Value = true };
            pressed.Setters.Add(new Setter(UIElement.OpacityProperty, 0.7));
            t.Triggers.Add(hover); t.Triggers.Add(pressed);
            t.Seal();
            return t;
        }

        static Button Btn(string text, Action a, bool primary = false, bool danger = false)
        {
            var b = new Button
            {
                Content = text, Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 8, 0), Cursor = Cursors.Hand, Template = RoundTemplate,
                Background = primary ? Blue : Panel, Foreground = primary ? Brushes.White : danger ? Red : Ink, BorderBrush = primary ? Blue : Line, BorderThickness = new Thickness(1)
            };
            b.Click += (o, e) => a();
            return b;
        }

        static Border Section(string title, string hint, UIElement body)
        {
            var sp = new StackPanel();
            sp.Children.Add(T(title, 15, Ink, true));
            if (hint != null) sp.Children.Add(T(hint, 12, Muted).Also(t => t.Margin = new Thickness(0, 2, 0, 10)));
            else ((FrameworkElement)sp.Children[0]).Margin = new Thickness(0, 0, 0, 10);
            sp.Children.Add(body);
            return new Border { Background = Panel, BorderBrush = Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16), Padding = new Thickness(18), Margin = new Thickness(0, 0, 0, 14), Child = sp };
        }

        /// <summary>Runs an action shortly after the last keystroke (typing in a name or an option).</summary>
        private void Soon(Action a)
        {
            _pending = a;
            _saveSoon.Stop();
            _saveSoon.Start();
        }

        private void FlushPending()
        {
            if (_pending == null) return;
            _saveSoon.Stop();
            var a = _pending; _pending = null;
            a();
        }

        private void PagesEdited(string pageId = null)
        {
            S.Save();
            N.PagesChanged(pageId);
        }

        private int VisibleCount => Catalog.Standard.Count(x => !S.HiddenPages.Contains(x.Id)) + S.Pages.Count(p => !p.Hidden);

        // =====================================================================
        //  Left: pages
        // =====================================================================
        private void BuildSide()
        {
            _side.Children.Clear();
            _side.Children.Add(T("Pagini", 18, Ink, true).Also(t => t.Margin = new Thickness(6, 0, 0, 0)));
            _side.Children.Add(T("Ce apare ca tab-uri în notch", 12, Muted).Also(t => t.Margin = new Thickness(6, 2, 0, 0)));

            _side.Children.Add(Caption("STANDARD · NU SE MODIFICĂ"));
            foreach (var (id, name, icon) in Catalog.Standard)
            {
                bool visible = !S.HiddenPages.Contains(id);
                var sid = id;
                var btns = new StackPanel { Orientation = Orientation.Horizontal };
                btns.Children.Add(Click(Glyph(visible ? "" : "", 13, visible ? Ink : Dim), () => ToggleStandard(sid), visible ? "Ascunde din notch" : "Arată în notch"));
                btns.Children.Add(Click(Glyph(Ui.GCopy, 13), () => DuplicateStandard(sid), "Duplică: copia o poți modifica"));
                _side.Children.Add(SideRow(NotchWindow.PageGlyph(icon), name, visible, _sel == "std:" + id, () => Select("std:" + sid), btns));
            }

            _side.Children.Add(Caption("ALE MELE"));
            if (S.Pages.Count == 0)
                _side.Children.Add(T("Nicio pagină încă. Duplică una standard sau fă una goală.", 12, Dim).Also(t => t.Margin = new Thickness(6, 0, 6, 6)));
            for (int i = 0; i < S.Pages.Count; i++)
            {
                var pg = S.Pages[i];
                int idx = i;
                var btns = new StackPanel { Orientation = Orientation.Horizontal };
                btns.Children.Add(Click(Glyph(pg.Hidden ? "" : "", 13, pg.Hidden ? Dim : Ink), () =>
                {
                    if (!pg.Hidden && VisibleCount <= 1) return;
                    pg.Hidden = !pg.Hidden; PagesEdited(); BuildSide();
                }, pg.Hidden ? "Arată în notch" : "Ascunde din notch"));
                var row = SideRow(NotchWindow.PageGlyph(pg.Icon), pg.Name, !pg.Hidden, _sel == pg.Id, () => Select(pg.Id), btns);
                Reorderable(row, idx);
                _side.Children.Add(row);
            }
            if (S.Pages.Count > 1) _side.Children.Add(T("Trage o pagină ca să-i schimbi locul.", 11.5, Dim).Also(t => t.Margin = new Thickness(6, 2, 0, 0)));
            var add = Click(Ui.H(8, Glyph("", 12, Blue), T("Pagină goală", 13, Blue)), () => NewPage(new List<WidgetSlot>(), null, "star"), null, null, 8, new Thickness(8, 7, 8, 7));
            add.Margin = new Thickness(0, 4, 0, 0);
            _side.Children.Add(add);

            _side.Children.Add(new Border { Height = 1, Background = Line, Margin = new Thickness(0, 14, 0, 8) });
            _side.Children.Add(SideRow("", "Teme și culori", true, _sel == "themes", () => Select("themes"), null));
            _side.Children.Add(SideRow(Ui.GSettings, "Setări", true, _sel == "settings", () => Select("settings"), null));
            _side.Children.Add(SideRow("\uE789", "Noutăți · " + Services.Updater.Current, true, _sel == "news", () => Select("news"), null));
        }

        private FrameworkElement SideRow(string glyph, string name, bool visible, bool selected, Action open, UIElement buttons)
        {
            var g = Ui.Cols(Ui.Px(24), Ui.Star(), Ui.Auto);
            g.Put(Glyph(glyph, 14, visible ? Ink : Dim));
            g.Put(new TextBlock { Text = name, FontSize = 13, Foreground = visible ? Ink : Dim, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
                                  FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal, Margin = new Thickness(4, 0, 4, 0) }, 1);
            if (buttons != null) g.Put(buttons, 2);
            var row = Click(g, open, null, selected ? SelBg : null, 8, new Thickness(6, 5, 4, 5));
            row.Margin = new Thickness(0, 0, 0, 2);
            return row;
        }

        /// <summary>Your pages can be dragged up and down the list; the order is the order of the tabs.</summary>
        private void Reorderable(FrameworkElement row, int idx)
        {
            const string fmt = "WinNotchPageIndex";
            Point? down = null;
            row.AllowDrop = true;
            row.PreviewMouseLeftButtonDown += (o, e) => down = e.GetPosition(row);
            row.MouseLeave += (o, e) => down = null;
            row.PreviewMouseLeftButtonUp += (o, e) => down = null;
            row.PreviewMouseMove += (o, e) =>
            {
                if (down == null || e.LeftButton != MouseButtonState.Pressed) { down = null; return; }
                var d = e.GetPosition(row) - down.Value;
                if (Math.Abs(d.Y) < 6) return;
                down = null;
                row.Opacity = 0.5;
                try { DragDrop.DoDragDrop(row, new DataObject(fmt, idx), DragDropEffects.Move); } catch { }
                row.Opacity = 1;
            };
            row.DragOver += (o, e) =>
            {
                e.Effects = e.Data.GetDataPresent(fmt) ? DragDropEffects.Move : DragDropEffects.None;
                if (row is Border b && e.Data.GetDataPresent(fmt)) b.BorderThickness = e.GetPosition(row).Y < row.ActualHeight / 2 ? new Thickness(0, 2, 0, 0) : new Thickness(0, 0, 0, 2);
                if (row is Border bb) bb.BorderBrush = Blue;
                e.Handled = true;
            };
            row.DragLeave += (o, e) => { if (row is Border b) b.BorderThickness = new Thickness(0); };
            row.Drop += (o, e) =>
            {
                if (row is Border b) b.BorderThickness = new Thickness(0);
                if (!(e.Data.GetData(fmt) is int from) || from < 0 || from >= S.Pages.Count) return;
                int to = idx + (e.GetPosition(row).Y < row.ActualHeight / 2 ? 0 : 1);
                var pg = S.Pages[from];
                S.Pages.RemoveAt(from);
                if (to > from) to--;
                S.Pages.Insert(Math.Clamp(to, 0, S.Pages.Count), pg);
                PagesEdited();
                Dispatcher.InvokeAsync(BuildSide);
            };
        }

        private void Select(string sel)
        {
            if (_sel == sel) return;
            FlushPending();
            _sel = sel;
            BuildSide();
            BuildCenter(null);
        }

        private void Move(int idx, int d)
        {
            int j = idx + d;
            if (j < 0 || j >= S.Pages.Count) return;
            (S.Pages[idx], S.Pages[j]) = (S.Pages[j], S.Pages[idx]);
            PagesEdited();
            BuildSide();
        }

        private void ToggleStandard(string id)
        {
            if (S.HiddenPages.Contains(id)) S.HiddenPages.Remove(id);
            else if (VisibleCount > 1) S.HiddenPages.Add(id);
            PagesEdited();
            BuildSide();
            if (_sel == "std:" + id) BuildCenter(null);
        }

        private void DuplicateStandard(string id)
        {
            var (_, name, icon) = Catalog.Standard.First(x => x.Id == id);
            NewPage(Catalog.StandardLayout(id), name + " (copia mea)", icon);
        }

        private void NewPage(IEnumerable<WidgetSlot> from, string name, string icon)
        {
            int n = 1;
            while (S.Pages.Any(p => p.Name == "Pagina mea " + n)) n++;
            var pg = new UserPage { Name = name ?? "Pagina mea " + n, Icon = icon ?? "star", Widgets = from.Select(w => w.Clone()).ToList() };
            S.Pages.Add(pg);
            PagesEdited();
            _sel = null;
            Select(pg.Id);
        }

        // =====================================================================
        //  Middle: the page
        // =====================================================================
        private void BuildCenter(string slotId)
        {
            _page = null;
            _gallery = null;
            _inspector.Visibility = Visibility.Visible;
            _centerScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            if (_sel != "settings" && _settings != null) { _settings.Detach(); _settings = null; }
            if (_sel == "themes") { _inspector.Visibility = Visibility.Collapsed; _center.Child = BuildThemes(); return; }
            if (_sel == "settings") { _inspector.Visibility = Visibility.Collapsed; _center.Child = BuildSettings(); return; }
            if (_sel == "news") { _inspector.Visibility = Visibility.Collapsed; _center.Child = BuildNews(); return; }
            if (_sel.StartsWith("std:")) { _center.Child = BuildStandard(_sel.Substring(4)); BuildInspector(null); return; }
            var pg = S.Pages.FirstOrDefault(p => p.Id == _sel);
            if (pg == null) { _sel = "std:home"; BuildSide(); BuildCenter(null); return; }

            var sp = new StackPanel { Margin = new Thickness(28, 22, 28, 28), HorizontalAlignment = HorizontalAlignment.Stretch, MaxWidth = 820 };

            // name and icon
            var nameBox = new TextBox { Text = pg.Name, FontSize = 20, FontWeight = FontWeights.SemiBold, BorderThickness = new Thickness(0, 0, 0, 1), BorderBrush = Line,
                                        Background = Brushes.Transparent, Width = 320, Padding = new Thickness(0, 2, 0, 4), MaxLength = 32, ToolTip = "Numele paginii (apare pe tab)" };
            nameBox.TextChanged += (o, e) =>
            {
                var v = nameBox.Text.Trim();
                if (v.Length == 0) return;
                pg.Name = v;
                Soon(() => { PagesEdited(); BuildSide(); });
            };
            var head = Ui.Cols(Ui.Auto, Ui.Star(), Ui.Auto);
            head.Put(nameBox);
            var actions = new StackPanel { Orientation = Orientation.Horizontal };
            actions.Children.Add(Btn("Duplică", () => NewPage(pg.Widgets, pg.Name + " (copie)", pg.Icon)));
            actions.Children.Add(DeleteButton(pg));
            head.Put(actions, 2);
            sp.Children.Add(head);

            var icons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            icons.Children.Add(T("Iconiță", 12, Muted).Also(t => t.Margin = new Thickness(0, 0, 10, 0)));
            foreach (var ic in new[] { "star", "bolt", "game", "music", "work", "heart", "home", "system", "devices", "tools" })
            {
                var key = ic;
                bool on = pg.Icon == ic;
                var chip = Click(Glyph(NotchWindow.PageGlyph(ic), 14, on ? Brushes.White : Ink), () => { pg.Icon = key; PagesEdited(); BuildSide(); BuildCenter(null); },
                                 null, on ? Blue : null, 8, new Thickness(7, 5, 7, 5));
                chip.Margin = new Thickness(0, 0, 4, 0);
                icons.Children.Add(chip);
            }
            sp.Children.Add(icons);

            sp.Children.Add(T("Trage un widget ca să-l muți; click pe el: în dreapta îi alegi mărimea și opțiunile; ⊖ îl scoate. Widget-uri noi: trage-le din galeria de dedesubt sau click pe ele pentru toate mărimile. Totul se salvează și apare în notch pe loc.",
                              12, Muted).Also(t => { t.Margin = new Thickness(0, 12, 0, 10); t.MaxWidth = 740; t.HorizontalAlignment = HorizontalAlignment.Left; }));

            // the live page, on the notch's own background
            _page = new WidgetPage(N, pg);
            _page.Editing = true;
            _page.Changed += () => { S.Save(); N.PagesChanged(pg.Id); };
            _page.Selected += slot => BuildInspector(slot);
            _page.Dropped += ok => _gallery?.Message(ok ? "" : "Pagina e plină: scoate sau micșorează un widget.");
            var preview = new Border { Width = 760, CornerRadius = new CornerRadius(Math.Clamp(S.CornerRadius, 8, 40)), Padding = new Thickness(20, 6, 20, 18), Child = _page };
            preview.SetResourceReference(Border.BackgroundProperty, "NotchBrush");
            // real size when there's room, smaller on small screens (dragging still lands in the right cell)
            sp.Children.Add(new Viewbox { Child = preview, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left });

            // gallery
            sp.Children.Add(T("Adaugă", 15, Ink, true).Also(t => t.Margin = new Thickness(0, 22, 0, 8)));
            _gallery = new Gallery((type, size) =>
            {
                if (_page.Add(type, size)) _gallery?.Message("");
                else _gallery?.Message("Pagina e plină: scoate sau micșorează un widget.");
            }, null);
            _gallery.Popup = fe => _edPopup = Gallery.ShowPopupIn(_overlay, fe, new Thickness(0), 780,
                                                                  onClosed: () => _edPopup = (null, null));
            var gHost = new Border { MaxWidth = 760, Height = 330, CornerRadius = new CornerRadius(20), Padding = new Thickness(14), Child = _gallery, HorizontalAlignment = HorizontalAlignment.Left };
            gHost.SetResourceReference(Border.BackgroundProperty, "NotchBrush");
            sp.Children.Add(gHost);
            _center.Child = sp;

            var sel = slotId != null ? pg.Widgets.FirstOrDefault(w => w.Id == slotId) : null;
            if (sel != null) _page.Select(sel);
            BuildInspector(sel);
            _page.Refresh();
        }

        private Button DeleteButton(UserPage pg)
        {
            bool armed = false;
            Button b = null;
            b = Btn("Șterge pagina", () =>
            {
                if (!armed) { armed = true; b.Content = "Sigur? Apasă din nou"; return; }
                S.Pages.Remove(pg);
                if (VisibleCount == 0) S.HiddenPages.Remove("home");
                PagesEdited();
                _sel = null;
                Select(S.Pages.FirstOrDefault()?.Id ?? "std:home");
            }, false, true);
            return b;
        }

        private FrameworkElement BuildStandard(string id)
        {
            var (_, name, icon) = Catalog.Standard.First(x => x.Id == id);
            bool visible = !S.HiddenPages.Contains(id);
            var sp = new StackPanel { Margin = new Thickness(28, 22, 28, 28), MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left };
            sp.Children.Add(Ui.H(10, Glyph(NotchWindow.PageGlyph(icon), 20), T(name, 20, Ink, true)));
            sp.Children.Add(T("Pagină standard: rămâne mereu la fel, ca să ai oricând varianta originală. Ca s-o schimbi, fă-ți o copie; " +
                              "copia are aceleași widget-uri și o poți modifica oricum. Originalul îl poți ascunde din notch.", 13, Muted).Also(t => t.Margin = new Thickness(0, 10, 0, 16)));
            var btns = new StackPanel { Orientation = Orientation.Horizontal };
            btns.Children.Add(Btn("Duplică și modifică", () => DuplicateStandard(id), true));
            btns.Children.Add(Btn(visible ? "Ascunde din notch" : "Arată în notch", () => ToggleStandard(id)));
            sp.Children.Add(btns);

            var list = new StackPanel();
            foreach (var w in Catalog.StandardLayout(id))
            {
                var d = Catalog.Get(w.Type);
                if (d == null) continue;
                var row = Ui.Cols(Ui.Px(28), Ui.Star(), Ui.Auto);
                row.Put(Glyph(d.Glyph, 14, Muted));
                row.Put(T(d.Name, 13), 1);
                row.Put(T(w.W + " × " + w.H, 12, Dim), 2);
                row.Margin = new Thickness(0, 0, 0, 8);
                list.Children.Add(row);
            }
            sp.Children.Add(Section("Ce conține copia", "Așa arată pagina transformată în widget-uri, ca s-o poți rearanja.", list).Also(b => b.Margin = new Thickness(0, 20, 0, 0)));
            return sp;
        }

        // =====================================================================
        //  Right: the selected widget
        // =====================================================================
        private void BuildInspector(WidgetSlot slot)
        {
            var sp = new StackPanel();
            _inspector.Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = sp };
            if (_page == null)
            {
                sp.Children.Add(T("Widget", 16, Ink, true));
                sp.Children.Add(T("Paginile standard nu se editează. Duplică pagina ca să-i poți schimba widget-urile.", 12.5, Muted).Also(t => t.Margin = new Thickness(0, 6, 0, 0)));
                return;
            }
            var def = slot != null ? Catalog.Get(slot.Type) : null;
            if (slot == null || def == null || !_page.Page.Widgets.Contains(slot))
            {
                sp.Children.Add(T("Widget", 16, Ink, true));
                sp.Children.Add(T("Click pe un widget din pagină ca să-i schimbi mărimea și opțiunile.", 12.5, Muted).Also(t => t.Margin = new Thickness(0, 6, 0, 14)));
                sp.Children.Add(T("Pe pagină: " + _page.Page.Widgets.Count + " widget-uri, " + Layout.UsedRows(_page.Page.Widgets) + " din " + Layout.MaxRows + " rânduri folosite.", 12, Dim));
                return;
            }

            sp.Children.Add(Ui.H(10, new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(10), Background = HoverBg, Child = Glyph(def.Glyph, 16) },
                                 T(def.Name, 16, Ink, true)));
            sp.Children.Add(T(def.Description, 12.5, Muted).Also(t => t.Margin = new Thickness(0, 8, 0, 16)));

            // size
            sp.Children.Add(T("Mărime: click pe una", 12, Muted, true).Also(t => t.Margin = new Thickness(0, 0, 0, 6)));
            var sizes = Gallery.SizePreviews(def, (slot.W, slot.H), size =>
            {
                FlushPending();
                if (_page.Resize(slot, size)) BuildInspector(slot);
                else if (_inspectorMsg != null) _inspectorMsg.Text = "Nu încape la mărimea asta: fă loc pe pagină.";
            });
            sp.Children.Add(new Border { Background = Ui.B("NotchBrush"), CornerRadius = new CornerRadius(18), Padding = new Thickness(10, 10, 0, 0), Child = sizes });
            _inspectorMsg = T("", 12, Red);
            sp.Children.Add(_inspectorMsg);

            // options
            if (def.Options.Length > 0)
            {
                sp.Children.Add(new Border { Height = 1, Background = Line, Margin = new Thickness(0, 12, 0, 12) });
                sp.Children.Add(T("Opțiuni", 14, Ink, true).Also(t => t.Margin = new Thickness(0, 0, 0, 8)));
                foreach (var o in def.Options) sp.Children.Add(OptionEditor(slot, o));
            }
            else sp.Children.Add(T("Widget-ul acesta nu are opțiuni: arată singur ce trebuie, după mărime.", 12, Dim).Also(t => t.Margin = new Thickness(0, 12, 0, 0)));

            sp.Children.Add(new Border { Height = 1, Background = Line, Margin = new Thickness(0, 12, 0, 12) });
            sp.Children.Add(Btn("Scoate widget-ul", () =>
            {
                FlushPending();
                _page.Page.Widgets.Remove(slot);
                _page.Select(null, false);
                _page.Commit();
                BuildInspector(null);
            }, false, true).Also(b => b.HorizontalAlignment = HorizontalAlignment.Left));
        }

        /// <summary>One option of the widget, edited live (rebuilt shortly after you stop typing).</summary>
        private FrameworkElement OptionEditor(WidgetSlot slot, OptionDef o)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            string cur = slot.Opt(o.Key, o.Default);
            void Set(string v, bool now)
            {
                slot.Options[o.Key] = v;
                if (now) { FlushPending(); _page.Commit(); }
                else Soon(() => _page?.Commit());
            }
            if (o.Kind != OptionKind.Bool) sp.Children.Add(T(o.Label, 12, Muted, true).Also(t => t.Margin = new Thickness(0, 0, 0, 4)));

            switch (o.Kind)
            {
                case OptionKind.Bool:
                {
                    var cb = new CheckBox { Content = o.Label, IsChecked = cur == "1", Foreground = Ink };
                    cb.Checked += (s, e) => Set("1", true);
                    cb.Unchecked += (s, e) => Set("0", true);
                    sp.Children.Add(cb);
                    break;
                }
                case OptionKind.Choice:
                {
                    var combo = new ComboBox { Padding = new Thickness(6, 4, 6, 4) };
                    foreach (var (v, label) in o.Choices) combo.Items.Add(new ComboBoxItem { Content = label, Tag = v });
                    combo.SelectedItem = combo.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == cur) ?? combo.Items.Cast<ComboBoxItem>().FirstOrDefault();
                    combo.SelectionChanged += (s, e) => { if (combo.SelectedItem is ComboBoxItem it) Set((string)it.Tag, true); };
                    sp.Children.Add(combo);
                    break;
                }
                case OptionKind.Color:
                {
                    var row = new WrapPanel();
                    var box = new TextBox { Text = cur, Width = 96, Padding = new Thickness(4, 3, 4, 3), Margin = new Thickness(0, 0, 6, 6), ToolTip = "#RRGGBB; gol = culoarea temei" };
                    foreach (var hex in new[] { "", "#5AA9FF", "#3DDC84", "#F5A524", "#FF5C5C", "#B48CFF", "#FF7EB6" })
                    {
                        var h = hex;
                        var sw = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(11), BorderBrush = Line, BorderThickness = new Thickness(1),
                                              Background = h == "" ? (Brush)new LinearGradientBrush(Colors.White, Colors.LightGray, 45) : new SolidColorBrush(ThemeManager.Parse(h, "#000000")),
                                              Margin = new Thickness(0, 0, 5, 6), Cursor = Cursors.Hand, ToolTip = h == "" ? "Culoarea temei" : h };
                        sw.MouseLeftButtonUp += (s, e) => { box.Text = h; Set(h, true); };
                        row.Children.Add(sw);
                    }
                    var pick = Btn("Alta…", () =>
                    {
                        var c = PickColor(slot.Opt(o.Key, ""));
                        if (c != null) { box.Text = c; Set(c, true); }
                    });
                    pick.Padding = new Thickness(8, 2, 8, 2); pick.Margin = new Thickness(0, 0, 6, 6);
                    box.TextChanged += (s, e) => { var t = box.Text.Trim(); if (t.Length == 0 || ValidHex(t)) Set(t, false); };
                    row.Children.Add(pick);
                    row.Children.Add(box);
                    sp.Children.Add(row);
                    break;
                }
                case OptionKind.Files:
                {
                    bool multi = o.Key == "items";
                    var box = new TextBox { Text = cur, AcceptsReturn = multi, TextWrapping = multi ? TextWrapping.NoWrap : TextWrapping.Wrap, Height = multi ? 120 : double.NaN,
                                            VerticalScrollBarVisibility = multi ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled, HorizontalScrollBarVisibility = multi ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled,
                                            Padding = new Thickness(4), FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 11.5 };
                    box.TextChanged += (s, e) => Set(box.Text.Replace("\r", ""), false);
                    sp.Children.Add(box);
                    var btns = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
                    void Append(string line)
                    {
                        if (multi) box.Text = (box.Text.TrimEnd('\r', '\n') + (box.Text.Trim().Length > 0 ? "\n" : "") + line);
                        else box.Text = line;
                        Set(box.Text.Replace("\r", ""), true);
                    }
                    btns.Children.Add(Btn(multi ? "+ Aplicație sau fișier…" : "Alege fișierul…", () =>
                    {
                        var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Alege", Filter = multi ? "Toate fișierele|*.*|Aplicații|*.exe;*.lnk" : "Programe și scripturi|*.exe;*.bat;*.cmd;*.ps1;*.lnk|Toate fișierele|*.*" };
                        if (dlg.ShowDialog(_dialogOwner ?? this) == true)
                            Append(multi ? System.IO.Path.GetFileNameWithoutExtension(dlg.FileName) + " = " + dlg.FileName : dlg.FileName);
                    }).Also(b => b.Margin = new Thickness(0, 0, 6, 6)));
                    if (multi)
                    {
                        btns.Children.Add(Btn("+ Folder…", () =>
                        {
                            var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Alege folderul" };
                            if (dlg.ShowDialog(_dialogOwner ?? this) == true)
                                Append(System.IO.Path.GetFileName(dlg.FolderName.TrimEnd('\\')) + " = " + dlg.FolderName);
                        }).Also(b => b.Margin = new Thickness(0, 0, 6, 6)));
                        btns.Children.Add(Btn("+ Link web", () => Append("https://")).Also(b => b.Margin = new Thickness(0, 0, 6, 6)));
                    }
                    sp.Children.Add(btns);
                    break;
                }
                default:     // Text, MultiLine, Number
                {
                    bool ml = o.Kind == OptionKind.MultiLine;
                    var box = new TextBox { Text = cur, AcceptsReturn = ml, TextWrapping = TextWrapping.Wrap, Height = ml ? 90 : double.NaN, Padding = new Thickness(4, 3, 4, 3),
                                            VerticalScrollBarVisibility = ml ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled };
                    if (o.Kind == OptionKind.Number) box.Width = 100;
                    box.TextChanged += (s, e) =>
                    {
                        var v = box.Text.Replace("\r", "");
                        if (o.Kind == OptionKind.Number && v.Trim().Length > 0 && !double.TryParse(v.Trim().Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _)) return;
                        Set(o.Kind == OptionKind.Number ? v.Trim().Replace(',', '.') : v, false);
                    };
                    box.HorizontalAlignment = o.Kind == OptionKind.Number ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
                    sp.Children.Add(box);
                    break;
                }
            }
            if (!string.IsNullOrEmpty(o.Hint)) sp.Children.Add(T(o.Hint, 11.5, Dim).Also(t => t.Margin = new Thickness(0, 4, 0, 0)));
            return sp;
        }

        static bool ValidHex(string t) => Core.Ui.ThemeEdits.ValidHex(t);

        // P52: one implementation for both windows (Core/Ui/ThemeEdits.cs)
        private string PickColor(string start) => Core.Ui.ThemeEdits.PickColor(start);

        // =====================================================================
        //  What this version brought
        // =====================================================================
        private FrameworkElement BuildNews()
        {
            var sp = new StackPanel { Margin = new Thickness(28, 22, 28, 28), MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left };
            sp.Children.Add(T("Noutăți în WinNotch " + Services.Updater.Current, 20, Ink, true));
            sp.Children.Add(T(Services.Updater.Configured ? "Versiunile noi se instalează din notch („Actualizează”); fiecare îți arată aici ce a adus." : "Ce a adus versiunea pe care o folosești.", 13, Muted).Also(t => t.Margin = new Thickness(0, 4, 0, 16)));
            var items = Services.Updater.ParseNotes(Services.Updater.OwnNotes());
            if (items.Count == 0) sp.Children.Add(T("Nicio notă pentru această versiune.", 13, Dim));
            foreach (var group in items.GroupBy(i => i.Kind))
            {
                var list = new StackPanel();
                foreach (var (_, text) in group)
                {
                    var g = Ui.Cols(Ui.Px(18), Ui.Star());
                    g.Put(T("•", 13, Blue, true).Also(t => t.VerticalAlignment = VerticalAlignment.Top));
                    g.Put(T(text, 13.5), 1);
                    g.Margin = new Thickness(0, 0, 0, 8);
                    list.Children.Add(g);
                }
                sp.Children.Add(Section(group.Key, null, list));
            }
            return sp;
        }

        // =====================================================================
        //  Settings: the same controls as before, now a page of this window
        // =====================================================================
        /// <summary>P14 ("settings.*" actions): the Settings page scrolled to one option, focused.</summary>
        internal void RevealSetting(string target) => _settings?.Reveal(target);

        private FrameworkElement BuildSettings()
        {
            _settings?.Detach();
            _settings = new SettingsWindow(S);
            _settings.Saved += () => N.ApplySettings();
            _settings.Reverted += () => { if (_sel == "settings") _center.Child = BuildSettings(); };
            var content = _settings.TakeContent();
            _centerScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;     // it scrolls itself; Save stays visible
            var host = new Border { Child = content, MaxWidth = 640, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(12, 6, 0, 0) };
            host.SetBinding(HeightProperty, new System.Windows.Data.Binding("ViewportHeight") { Source = _centerScroll });
            return host;
        }

        // =====================================================================
        //  Themes
        // =====================================================================
        private FrameworkElement BuildThemes()
        {
            var sp = new StackPanel { Margin = new Thickness(28, 22, 28, 28), MaxWidth = 980, HorizontalAlignment = HorizontalAlignment.Left };
            sp.Children.Add(T("Teme și culori", 20, Ink, true));
            sp.Children.Add(T("Se aplică pe loc: deschide notch-ul (Win+Alt+N) ca să vezi rezultatul.", 13, Muted).Also(t => t.Margin = new Thickness(0, 4, 0, 16)));

            // mode
            var modes = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var (val, label, glyph) in new[] { ("dark", "Întunecat", ""), ("light", "Luminos", ""), ("auto", "Automat (ca Windows)", "") })
            {
                bool on = S.ThemeMode == val;
                var v = val;
                var chip = Click(Ui.H(8, Glyph(glyph, 13, on ? Brushes.White : Ink), T(label, 13, on ? Brushes.White : Ink, on)),
                                 () => { S.ThemeMode = v; ApplyTheme(true); }, null, on ? Blue : HoverBg, 9, new Thickness(14, 8, 14, 8));
                chip.Margin = new Thickness(0, 0, 8, 0);
                modes.Children.Add(chip);
            }
            sp.Children.Add(Section("Mod", S.ThemeMode == "auto" ? "Acum Windows e pe " + (ThemeManager.WindowsLight() ? "luminos" : "întunecat") + "; notch-ul se schimbă singur când îl schimbi." : null, modes));

            sp.Children.Add(Section("Tema pentru modul întunecat", null, ThemeCards(false)));
            sp.Children.Add(Section("Tema pentru modul luminos", null, ThemeCards(true)));

            // colors of the theme in use
            var cur = ThemeManager.Current(S);
            string themeName = cur.Name;
            var colors = new WrapPanel();
            foreach (var (key, label) in ThemeManager.Keys)
            {
                var k = key;
                string hex = cur.Colors.TryGetValue(key, out var h) ? h : "#000000";
                bool changed = Core.Ui.ThemeEdits.IsChanged(S, themeName, key);
                var sw = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(8), BorderBrush = Line, BorderThickness = new Thickness(1), Cursor = Cursors.Hand,
                                      Background = new SolidColorBrush(ThemeManager.Parse(hex, "#000000")), ToolTip = "Alege culoarea" };
                sw.MouseLeftButtonUp += (s, e) =>
                {
                    var c = PickColor(hex);
                    if (c == null) return;
                    SetOverride(themeName, k, c);
                };
                var g = Ui.Cols(Ui.Auto, Ui.Px(10), Ui.Star(), Ui.Auto);
                g.Put(sw);
                g.Put(Ui.V(0, T(label, 12.5), T(hex, 11.5, Dim).Also(t => t.FontFamily = new FontFamily("Cascadia Mono, Consolas"))), 2);
                if (changed) g.Put(Click(Glyph("", 12, Muted), () => ResetOverride(themeName, k), "Înapoi la culoarea temei"), 3);
                colors.Children.Add(new Border { Width = 220, Padding = new Thickness(0, 0, 12, 10), Child = g });
            }
            var colorBody = new StackPanel();
            colorBody.Children.Add(colors);
            var resetAll = Btn("Toate înapoi la tema „" + themeName + "”", () =>
            {
                Core.Ui.ThemeEdits.ResetAll(S, themeName);
                ApplyTheme(true);
            });
            resetAll.HorizontalAlignment = HorizontalAlignment.Left;
            colorBody.Children.Add(resetAll);
            sp.Children.Add(Section("Culorile temei „" + themeName + "”", "Click pe o culoare ca s-o schimbi. Schimbările se țin minte pentru tema asta.", colorBody));

            // shape
            var shape = new StackPanel();
            shape.Children.Add(SliderRow("Rotunjirea colțurilor", 12, 40, S.CornerRadius, v => Math.Round(v) + " px", v => { S.CornerRadius = (int)Math.Round(v); _themeSoon.Stop(); _themeSoon.Start(); }));
            shape.Children.Add(SliderRow("Opacitatea fundalului", 60, 100, Math.Round(S.BgOpacity * 100), v => Math.Round(v) + "%", v => { S.BgOpacity = Math.Round(v) / 100.0; _themeSoon.Stop(); _themeSoon.Start(); }));
            sp.Children.Add(Section("Formă", "Fundalul transparent lasă să se vadă ce e în spate; 100% e opac.", shape));

            // save as theme
            var nameBox = new TextBox { Width = 220, Padding = new Thickness(4, 3, 4, 3), Text = themeName + " (a mea)", MaxLength = 30, Margin = new Thickness(0, 0, 8, 0) };
            var save = Btn("Salvează ca temă nouă", () =>
            {
                if (!Core.Ui.ThemeEdits.SaveAs(S, nameBox.Text)) { nameBox.BorderBrush = Red; return; }
                ApplyTheme(true);
            }, true);
            sp.Children.Add(Section("Temă nouă", "Păstrează culorile de acum (cu modificările tale) ca temă separată, pe care o poți alege oricând.", Ui.H(0, nameBox, save)));
            return sp;
        }

        private FrameworkElement SliderRow(string label, double min, double max, double val, Func<double, string> fmt, Action<double> changed)
        {
            var g = Ui.Cols(Ui.Px(190), Ui.Star(), Ui.Px(60));
            g.Put(T(label, 13));
            var sl = new Slider { Minimum = min, Maximum = max, Value = val, VerticalAlignment = VerticalAlignment.Center, IsMoveToPointEnabled = true };
            var txt = T(fmt(val), 12.5, Muted);
            txt.HorizontalAlignment = HorizontalAlignment.Right;
            sl.ValueChanged += (s, e) => { txt.Text = fmt(e.NewValue); changed(e.NewValue); };
            g.Put(sl, 1); g.Put(txt, 2);
            g.Margin = new Thickness(0, 0, 0, 10);
            return g;
        }

        private FrameworkElement ThemeCards(bool light)
        {
            var wrap = new WrapPanel();
            string chosen = light ? S.ThemeLight : S.ThemeDark;
            foreach (var t in ThemeManager.All(S).Where(p => p.Light == light).ToList())
            {
                var theme = t;
                bool on = t.Name == chosen;
                Brush C(string k, string fb) => new SolidColorBrush(ThemeManager.Parse(theme.Colors.TryGetValue(k, out var v) ? v : null, fb));
                var mini = new Grid { Width = 150, Height = 74 };
                var notch = new Border { Background = C("Notch", "#000000"), CornerRadius = new CornerRadius(12), Padding = new Thickness(8) };
                var inner = Ui.Cols(Ui.Star(), Ui.Px(6), Ui.Star());
                var card1 = new Border { Background = C("Chip", "#1B1D21"), CornerRadius = new CornerRadius(6), Padding = new Thickness(6) };
                card1.Child = Ui.V(4, new Border { Height = 5, Width = 40, CornerRadius = new CornerRadius(2), Background = C("Ink", "#FFFFFF"), HorizontalAlignment = HorizontalAlignment.Left },
                                      new Border { Height = 4, Width = 28, CornerRadius = new CornerRadius(2), Background = C("Muted", "#999999"), HorizontalAlignment = HorizontalAlignment.Left },
                                      new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = C("Accent", "#F5A524"), Margin = new Thickness(0, 6, 10, 0) });
                var card2 = new Border { Background = C("Chip", "#1B1D21"), CornerRadius = new CornerRadius(6), Padding = new Thickness(6),
                                         Child = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(9), Background = C("Accent", "#F5A524"), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom } };
                inner.Put(card1); inner.Put(card2, 2);
                notch.Child = inner;
                mini.Children.Add(notch);
                var body = Ui.V(6, mini, Ui.H(6, T(t.Name, 12.5, Ink, on), on ? Glyph("", 11, Blue) : new TextBlock()));
                var cardB = Click(body, () => { Core.Ui.ThemeEdits.Choose(S, theme.Name, light); ApplyTheme(true); }, null, on ? SelBg : null, 12, new Thickness(8));
                cardB.BorderBrush = on ? Blue : Brushes.Transparent;
                cardB.BorderThickness = new Thickness(1.5);
                cardB.Margin = new Thickness(0, 0, 8, 8);
                if (!ThemeManager.Presets.Contains(t))
                {
                    var holder = new Grid();
                    holder.Children.Add(cardB);
                    var del = Click(Glyph("", 11, Red), () =>
                    {
                        if (Core.Ui.ThemeEdits.Delete(S, theme)) ApplyTheme(true);
                    }, "Șterge tema", Panel, 10, new Thickness(5));
                    del.HorizontalAlignment = HorizontalAlignment.Right; del.VerticalAlignment = VerticalAlignment.Top; del.Margin = new Thickness(0, 4, 12, 0);
                    holder.Children.Add(del);
                    wrap.Children.Add(holder);
                }
                else wrap.Children.Add(cardB);
            }
            return wrap;
        }

        private void SetOverride(string theme, string key, string hex)
        {
            Core.Ui.ThemeEdits.SetOverride(S, theme, key, hex);
            ApplyTheme(true);
        }

        private void ResetOverride(string theme, string key)
        {
            Core.Ui.ThemeEdits.ResetOverride(S, theme, key);
            ApplyTheme(true);
        }

        /// <summary>Saves and applies the theme to the notch; redraws this page when a choice changed.</summary>
        private void ApplyTheme(bool redraw)
        {
            S.Save();
            N.ApplySettings();
            if (redraw && _sel == "themes")
            {
                var sv = FindScroll(_center);
                double off = sv?.VerticalOffset ?? 0;
                _center.Child = BuildThemes();
                if (sv != null) Dispatcher.InvokeAsync(() => sv.ScrollToVerticalOffset(off), DispatcherPriority.Loaded);
            }
        }

        private static ScrollViewer FindScroll(DependencyObject d)
        {
            for (var p = VisualTreeHelper.GetParent(d); p != null; p = VisualTreeHelper.GetParent(p))
                if (p is ScrollViewer sv) return sv;
            return null;
        }
    }
}
