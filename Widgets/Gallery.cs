using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WinNotch.Panes;

namespace WinNotch.Widgets
{
    /// <summary>
    /// "Adaugă un widget", like on the iPhone: drag a widget onto the page and it goes in at its default size; click
    /// it and a picker shows every size it comes in, as live previews, to click or drag. Used in the notch and the editor.
    /// </summary>
    internal sealed class Gallery : Grid
    {
        private readonly StackPanel _cats = new StackPanel();
        private readonly WrapPanel _tiles = new WrapPanel();
        private readonly ScrollViewer _tilesScroll;
        private readonly Grid _picker = new Grid { Visibility = Visibility.Collapsed };
        private readonly TextBlock _msg;
        private string _cat = "Toate";
        private readonly Action<string, (int, int)> _add;

        /// <summary>A drag from the gallery started (the notch hides the gallery so the page is visible) / ended.</summary>
        public event Action DragStarted, DragEnded;

        /// <summary>
        /// The host shows the size picker as a pop-up over its window (the gallery itself stays as it is) and returns
        /// how to hide it for a moment (during a drag) and how to close it.
        /// </summary>
        public Func<FrameworkElement, (Action Hide, Action Close)> Popup;

        public Gallery(Action<string, (int, int)> add, Action close)
        {
            _add = add;
            ColumnDefinitions.Add(new ColumnDefinition { Width = Ui.Px(170) });
            ColumnDefinitions.Add(new ColumnDefinition { Width = Ui.Px(12) });
            ColumnDefinitions.Add(new ColumnDefinition { Width = Ui.Star() });
            RowDefinitions.Add(new RowDefinition { Height = Ui.Auto });
            RowDefinitions.Add(new RowDefinition { Height = Ui.Px(8) });
            RowDefinitions.Add(new RowDefinition { Height = Ui.Star() });

            var head = Ui.Cols(Ui.Auto, Ui.Px(10), Ui.Star(), Ui.Auto);
            head.Put(Ui.V(0, Ui.T("Adaugă un widget", 15, "InkBrush", true), Ui.T("trage-l pe pagină  ·  click pe el pentru toate mărimile", 11, "DimBrush")));
            _msg = Ui.T("", 12, "WarnBrush");
            _msg.VerticalAlignment = VerticalAlignment.Center;
            head.Put(_msg, 2);
            if (close != null) head.Put(Ui.PillBtn("Închide", close), 3);
            this.Put(head, 0, 0, 3);

            this.Put(_cats, 0, 2);
            _tilesScroll = new ScrollViewer { Style = Ui.S("SlimScroll"), Content = _tiles };
            this.Put(_tilesScroll, 2, 2);
            this.Put(_picker, 2, 2);
            Build();
        }

        public void Message(string text) => _msg.Text = text;

        private void Build()
        {
            _cats.Children.Clear();
            foreach (var c in new[] { "Toate" }.Concat(Catalog.Categories))
            {
                bool on = c == _cat;
                var b = new Button { Style = Ui.S("TileButton"), Margin = new Thickness(0, 0, 0, 3), Padding = new Thickness(10, 7, 10, 7), HorizontalContentAlignment = HorizontalAlignment.Left,
                                     Content = Ui.T(c, 12.5, on ? "InkBrush" : "MutedBrush", on), BorderThickness = new Thickness(1) };
                if (on) b.SetResourceReference(Control.BorderBrushProperty, "AccentBrush");
                var cat = c;
                b.Click += (o, e) => { _cat = cat; ClosePicker(); Build(); };
                _cats.Children.Add(b);
            }
            _tiles.Children.Clear();
            foreach (var d in Catalog.All.Where(d => _cat == "Toate" || d.Category == _cat))
            {
                var def = d;
                // each widget shown as itself (live, at its default size, scaled down), like the iPhone's widget gallery
                var sz = d.DefaultSize;
                double pw = sz.W * Cell - WidgetPage.Gap, ph = sz.H * RowStep - WidgetPage.Gap;
                double scale = Math.Min(0.62, Math.Min(214 / pw, 92 / ph));
                var preview = new Border { Height = 96, Child = LivePreview(d, sz, scale), IsHitTestVisible = false };
                var top = Ui.Cols(Ui.Star(), Ui.Auto);
                top.Put(Ui.T(d.Name, 13, "InkBrush", true));
                top.Put(Ui.T(string.Join(" · ", d.Sizes.Select(z => z.W + "×" + z.H)), 10.5, "DimBrush", false, true), 1);
                var desc = Ui.T(d.Description, 11.5, "MutedBrush");
                desc.TextWrapping = TextWrapping.Wrap; desc.TextTrimming = TextTrimming.CharacterEllipsis; desc.MaxHeight = 32;
                var card = new Border { Width = 238, Margin = new Thickness(0, 0, 8, 8), CornerRadius = new CornerRadius(18), Padding = new Thickness(10),
                                        Child = Ui.V(6, preview, top, desc), Cursor = Cursors.Hand, ToolTip = "Trage pe pagină (mărimea " + d.DefaultSize.W + "×" + d.DefaultSize.H + ") sau click pentru toate mărimile" };
                card.SetResourceReference(Border.BackgroundProperty, "ChipBrush");
                Hover(card);
                DragOrClick(card, def.Type, () => OpenPicker(def));
                _tiles.Children.Add(card);
            }
        }

        internal static void Hover(Border b)
        {
            b.MouseEnter += (o, e) => b.SetResourceReference(Border.BackgroundProperty, "ChipHoverBrush");
            b.MouseLeave += (o, e) => b.SetResourceReference(Border.BackgroundProperty, "ChipBrush");
        }

        /// <summary>Press and move: drags the widget out (data = type, or "type|W|H"); press and release: click.</summary>
        private void DragOrClick(FrameworkElement el, string data, Action click, Action dragStart = null, Action dragEnd = null)
        {
            Point? down = null;
            el.PreviewMouseLeftButtonDown += (o, e) => { down = e.GetPosition(el); };
            el.MouseLeave += (o, e) => down = null;
            el.PreviewMouseMove += (o, e) =>
            {
                if (down == null || e.LeftButton != MouseButtonState.Pressed) { down = null; return; }
                var d = e.GetPosition(el) - down.Value;
                if (Math.Abs(d.X) + Math.Abs(d.Y) < 6) return;
                down = null;
                _msg.Text = "";
                dragStart?.Invoke();
                DragStarted?.Invoke();
                try { DragDrop.DoDragDrop(el, new DataObject(WidgetPage.DragFormat, data), DragDropEffects.Copy); }
                catch (Exception ex) { App.Log("Galerie, tragere: " + ex.Message); }
                dragEnd?.Invoke();
                DragEnded?.Invoke();
            };
            el.MouseLeftButtonUp += (o, e) =>
            {
                if (down == null) return;
                down = null;
                e.Handled = true;
                click();
            };
        }

        // ------------------------------------------------------------------ size picker

        /// <summary>Every size of a widget in a pop-up over the window: click one to add it, or drag it onto the page.</summary>
        private void OpenPopup(WidgetDef d)
        {
            (Action Hide, Action Close) handle = (null, null);
            var close = Ui.PillBtn("Închide", () => handle.Close?.Invoke());
            var head = Ui.Cols(Ui.Auto, Ui.Px(12), Ui.Star(), Ui.Auto);
            var icon = new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(10), Child = Ui.Icon(d.Glyph, 16) };
            icon.SetResourceReference(Border.BackgroundProperty, "TrackBrush");
            head.Put(icon);
            head.Put(Ui.V(0, Ui.T(d.Name, 15, "InkBrush", true), Ui.T(d.Description + "  ·  click pe o mărime sau trage-o pe pagină", 11.5, "MutedBrush")), 2);
            head.Put(close, 3);
            head.Margin = new Thickness(0, 0, 0, 12);
            var previews = SizePreviews(d, null, size => { handle.Close?.Invoke(); _add(d.Type, size); },
                                        (el, sz, click) => DragOrClick(el, d.Type + "|" + sz.W + "|" + sz.H, click, () => handle.Hide?.Invoke(), () => handle.Close?.Invoke()));
            previews.HorizontalAlignment = HorizontalAlignment.Center;
            var g = Ui.Rows(Ui.Auto, Ui.Star());
            g.Put(head);
            g.Put(new ScrollViewer { Style = Ui.S("SlimScroll"), Content = previews, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, 0, 1);
            handle = Popup(g);
        }

        /// <summary>A pop-up card on a dimmed backdrop (click outside closes it), for hosts that have an overlay layer.</summary>
        internal static (Action Hide, Action Close) ShowPopupIn(Panel layer, FrameworkElement content, Thickness margin, double maxWidth = 640,
                                                               Action onClosed = null)
        {
            var card = new Border { Child = content, CornerRadius = new CornerRadius(22), Padding = new Thickness(18, 16, 18, 10), MaxWidth = maxWidth,
                                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, BorderThickness = new Thickness(1),
                                    Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 30, ShadowDepth = 8, Direction = 270, Opacity = 0.45, RenderingBias = System.Windows.Media.Effects.RenderingBias.Performance } };
            card.SetResourceReference(Border.BackgroundProperty, "NotchBrush");
            card.SetResourceReference(Border.BorderBrushProperty, "HoverBrush");
            var dim = new Border { Background = new SolidColorBrush(Color.FromArgb(0x88, 0, 0, 0)), CornerRadius = new CornerRadius(18), Margin = margin, Child = card, Padding = new Thickness(24, 16, 24, 16) };
            bool closed = false;
            // P51: closing it once, and the caller is told — otherwise its own fields (the sizes pop-up) stay as dead references
            Action close = () => { if (closed) return; closed = true; layer.Children.Remove(dim); onClosed?.Invoke(); };
            dim.MouseLeftButtonUp += (o, e) => { if (e.OriginalSource == dim) close(); };
            layer.Children.Add(dim);
            dim.Opacity = 0;
            dim.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(1, TimeSpan.FromMilliseconds(140)));
            return (() => dim.Visibility = Visibility.Hidden, close);
        }

        /// <summary>
        /// Every size a widget comes in, as live previews in proportion (half the real size). <paramref name="current"/>
        /// is marked "acum" (resizing a placed widget), otherwise the default size is marked. <paramref name="wire"/> can make
        /// each preview draggable; by default a click picks it.
        /// </summary>
        const double Cell = 114.7, RowStep = WidgetPage.RowH + WidgetPage.Gap;

        /// <summary>The widget itself at a size, live, scaled (what it will look like on the page).</summary>
        internal static FrameworkElement LivePreview(WidgetDef d, (int W, int H) sz, double scale)
        {
            double w = sz.W * Cell - WidgetPage.Gap, h = sz.H * RowStep - WidgetPage.Gap;
            try
            {
                var slot = Catalog.NewSlot(d.Type, sz);
                var widget = Catalog.Create(NotchWindow.Current, slot);
                try { widget.Refresh(); widget.MediaChanged(); } catch { }
                var inner = new Border { Width = w, Height = h, CornerRadius = new CornerRadius(WidgetPage.Radius), Padding = widget.CardPadding,
                                         Clip = new RectangleGeometry(new Rect(0, 0, w, h), WidgetPage.Radius, WidgetPage.Radius), Child = widget, IsHitTestVisible = false };
                inner.SetResourceReference(Border.BackgroundProperty, "TrackBrush");
                return new Viewbox { Width = w * scale, Height = h * scale, Child = inner, Stretch = Stretch.Uniform };
            }
            catch { return new Border { Width = w * scale, Height = h * scale }; }
        }

        internal static WrapPanel SizePreviews(WidgetDef d, (int W, int H)? current, Action<(int W, int H)> pick,
                                               Action<FrameworkElement, (int W, int H), Action> wire = null)
        {
            const double scale = 0.5;
            var previews = new WrapPanel();
            var marked = current ?? d.DefaultSize;
            foreach (var sz in d.Sizes)
            {
                var size = sz;
                var content = LivePreview(d, sz, scale);

                bool on = sz == marked;
                string note = on ? (current != null ? "  ·  acum" : "  ·  implicit") : "";
                var label = Ui.T(sz.W + " × " + sz.H + note, 11.5, on ? "AccentBrush" : "MutedBrush", true);
                label.HorizontalAlignment = HorizontalAlignment.Center;
                var box = new Border { Padding = new Thickness(8), CornerRadius = new CornerRadius(18), Margin = new Thickness(0, 0, 10, 10), Cursor = Cursors.Hand,
                                       BorderThickness = new Thickness(1.5), Child = Ui.V(6, content, label),
                                       ToolTip = current != null ? "Click: fă-l " + sz.W + "×" + sz.H : "Click: adaugă la " + sz.W + "×" + sz.H + "  ·  sau trage pe pagină" };
                box.SetResourceReference(Border.BackgroundProperty, "ChipBrush");
                box.SetResourceReference(Border.BorderBrushProperty, on ? "AccentBrush" : "ChipBrush");
                Hover(box);
                if (wire != null) wire(box, sz, () => pick(size));
                else
                {
                    bool pressed = false;
                    box.MouseLeftButtonDown += (o, e) => pressed = true;
                    box.MouseLeave += (o, e) => pressed = false;
                    box.MouseLeftButtonUp += (o, e) => { if (!pressed) return; pressed = false; e.Handled = true; pick(size); };
                }
                previews.Children.Add(box);
            }
            return previews;
        }

        private void ClosePicker()
        {
            _picker.Children.Clear();
            _picker.Visibility = Visibility.Collapsed;
            _tilesScroll.Visibility = Visibility.Visible;
        }

        private void OpenPicker(WidgetDef d)
        {
            if (Popup != null) { OpenPopup(d); return; }
            _picker.Children.Clear();
            var back = Ui.PillBtn("‹ Toate", ClosePicker);
            var head = Ui.Cols(Ui.Auto, Ui.Px(12), Ui.Star());
            head.Put(back);
            head.Put(Ui.V(0, Ui.T(d.Name, 15, "InkBrush", true), Ui.T(d.Description, 11.5, "MutedBrush")), 2);
            head.Margin = new Thickness(0, 0, 0, 10);

            var previews = SizePreviews(d, null, size => _add(d.Type, size), (el, sz, click) => DragOrClick(el, d.Type + "|" + sz.W + "|" + sz.H, click));
            var g = Ui.Rows(Ui.Auto, Ui.Star());
            g.Put(head);
            g.Put(new ScrollViewer { Style = Ui.S("SlimScroll"), Content = previews }, 0, 1);
            _picker.Children.Add(g);
            _picker.Visibility = Visibility.Visible;
            _tilesScroll.Visibility = Visibility.Collapsed;
        }
    }
}
