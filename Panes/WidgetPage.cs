using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WinNotch.Widgets;

namespace WinNotch.Panes
{
    /// <summary>
    /// One of your pages: widgets on a 6-column grid (up to 4 rows). In edit mode: drag a widget to move it, drag its
    /// corner to resize (snaps to the sizes that widget supports), ⊖ removes it, the gear opens its settings.
    /// Used both in the notch and in the editor window.
    /// </summary>
    internal sealed class WidgetPage : Pane
    {
        public const double RowH = 64, Gap = 8, Radius = 18;
        public readonly UserPage Page;
        private readonly Grid _grid = new Grid();
        private readonly Canvas _dragLayer = new Canvas { IsHitTestVisible = false };
        private readonly Border _ghost;
        private readonly TextBlock _hint, _ghostLabel;
        private readonly Dictionary<WidgetSlot, (Border Card, Widget Widget)> _cards = new Dictionary<WidgetSlot, (Border, Widget)>();
        private bool _editing;
        private readonly Dictionary<WidgetSlot, FrameworkElement> _chromes = new Dictionary<WidgetSlot, FrameworkElement>();
        private Grid _host;

        /// <summary>Raised after any change to the layout (move, resize, remove, add); the host saves.</summary>
        public event Action Changed;
        /// <summary>A widget was clicked in edit mode (the editor window shows its settings).</summary>
        public event Action<WidgetSlot> Selected;
        public WidgetSlot SelectedSlot { get; private set; }

        public WidgetPage(NotchWindow w, UserPage page) : base(w)
        {
            Page = page;
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            RowDefinitions.Add(new RowDefinition { Height = Ui.Star() });
            _hint = Ui.T("trage ca să muți  ·  arcul din colț: mărimea  ·  ⊖ scoate  ·  click pe widget: mărimi și setări  ·  widget-uri noi: trage-le din galerie", 11.5, "DimBrush");
            _hint.Margin = new Thickness(0, 0, 0, 8);
            _hint.Visibility = Visibility.Collapsed;
            this.Put(_hint);
            var host = _host = new Grid { VerticalAlignment = VerticalAlignment.Top };
            host.Children.Add(_grid);
            host.Children.Add(_dragLayer);
            this.Put(host, 0, 1);
            _ghostLabel = new TextBlock { FontSize = 13, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            _ghostLabel.SetResourceReference(TextBlock.ForegroundProperty, "InkBrush");
            _ghost = new Border { CornerRadius = new CornerRadius(Radius), BorderThickness = new Thickness(2), Visibility = Visibility.Collapsed, Child = _ghostLabel };
            _ghost.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
            _ghost.Background = new SolidColorBrush(Color.FromArgb(0x22, 0xF5, 0xA5, 0x24));
            _dragLayer.Children.Add(_ghost);
            DragOver += OnGalleryDragOver;
            DragEnter += OnGalleryDragOver;
            DragLeave += (o, e) => { _ghost.Visibility = Visibility.Collapsed; _grid.Height = GridHeight; };
            Drop += OnGalleryDrop;
            for (int c = 0; c < Layout.Cols; c++)
            {
                if (c > 0) _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = Ui.Px(Gap) });
                _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = Ui.Star() });
            }
            Rebuild();
        }

        // in edit mode the whole grid shows (like an iPhone page being edited): room to move, resize and drop
        public int VisibleRows => _editing ? Layout.MaxRows : Math.Clamp(Math.Max(1, Layout.UsedRows(Page.Widgets)), 1, Layout.MaxRows);
        public double GridHeight => VisibleRows * (RowH + Gap) - Gap;
        public override double PanelHeight => GridHeight + 64 + (_editing ? 42 : 0);

        public bool Editing
        {
            get => _editing;
            set
            {
                if (_editing == value) return;
                _editing = value;
                _hint.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
                AllowDrop = value;
                _host.Margin = value ? new Thickness(4, 6, 4, 10) : new Thickness(0);      // room for the corner badges
                Background = value ? Brushes.Transparent : null;     // empty cells must catch drops too
                if (!value) SelectedSlot = null;
                Rebuild();
            }
        }

        // ------------------------------------------------------------------ building

        public void Rebuild()
        {
            _grid.Children.Clear();
            _grid.RowDefinitions.Clear();
            _cards.Clear();
            _chromes.Clear();
            int rows = VisibleRows;
            for (int r = 0; r < rows; r++)
            {
                if (r > 0) _grid.RowDefinitions.Add(new RowDefinition { Height = Ui.Px(Gap) });
                _grid.RowDefinitions.Add(new RowDefinition { Height = Ui.Px(RowH) });
            }
            if (_editing)          // empty cells, so you see where things can go
                for (int r = 0; r < rows; r++)
                    for (int c = 0; c < Layout.Cols; c++)
                    {
                        // soft empty slots (a faint fill, no hard lines)
                        var cell = new Border { CornerRadius = new CornerRadius(Radius), Background = Ui.B("TrackBrush"), Opacity = 0.35, IsHitTestVisible = false };
                        Grid.SetColumn(cell, c * 2); Grid.SetRow(cell, r * 2);
                        _grid.Children.Add(cell);
                    }
            foreach (var slot in Page.Widgets.Where(s => s.Row + s.H <= rows || !_editing))
            {
                if (slot.Row >= Layout.MaxRows) continue;
                var widget = Catalog.Create(W, slot);
                var card = new Border
                {
                    CornerRadius = new CornerRadius(Radius), Padding = widget.CardPadding, BorderThickness = new Thickness(1),
                    Child = widget
                };
                // rounded clip: tinted backgrounds and covers follow the card's rounded corners (ClipToBounds cuts square)
                card.SizeChanged += (o, e) => card.Clip = new RectangleGeometry(new Rect(e.NewSize), Radius, Radius);
                card.SetResourceReference(Border.BackgroundProperty, "ChipBrush");
                card.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
                Grid.SetColumn(card, slot.Col * 2);
                Grid.SetRow(card, slot.Row * 2);
                Grid.SetColumnSpan(card, Math.Max(1, slot.W * 2 - 1));
                Grid.SetRowSpan(card, Math.Max(1, slot.H * 2 - 1));
                _grid.Children.Add(card);
                if (_editing) { var ch = EditChrome(slot, card); _grid.Children.Add(ch); _chromes[slot] = ch; }
                _cards[slot] = (card, widget);
            }
            _grid.Height = GridHeight;
        }

        /// <summary>Edit-mode overlay of one widget: outline, remove, settings, resize grip; drag to move.</summary>
        private FrameworkElement EditChrome(WidgetSlot slot, Border card)
        {
            var o = new Grid { Background = new SolidColorBrush(Color.FromArgb(0x01, 0, 0, 0)), Cursor = Cursors.SizeAll };
            Grid.SetColumn(o, Grid.GetColumn(card)); Grid.SetRow(o, Grid.GetRow(card));
            Grid.SetColumnSpan(o, Grid.GetColumnSpan(card)); Grid.SetRowSpan(o, Grid.GetRowSpan(card));
            var outline = new Border { CornerRadius = new CornerRadius(Radius), BorderThickness = new Thickness(slot == SelectedSlot ? 2 : 1.5), IsHitTestVisible = false };
            outline.SetResourceReference(Border.BorderBrushProperty, slot == SelectedSlot ? "AccentBrush" : "HoverBrush");
            o.Children.Add(outline);

            // like the iPhone's Control Center: ⊖ on the top-left corner, a resize arc on the bottom-right corner
            var remove = Small("", "Scoate widget-ul", HorizontalAlignment.Left, VerticalAlignment.Top);
            remove.Click += (s, e) => { Page.Widgets.Remove(slot); if (SelectedSlot == slot) SelectedSlot = null; Commit(); };
            o.Children.Add(remove);
            var def = Catalog.Get(slot.Type);
            if (def != null && CanResize(slot, def))
            {
                var arc = new System.Windows.Shapes.Path
                {
                    Data = Geometry.Parse("M 26,6 A 20,20 0 0 1 6,26"), StrokeThickness = 4.5, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round
                };
                arc.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "InkBrush");
                var grip = new Grid { Width = 30, Height = 30, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
                                      Margin = new Thickness(0, 0, -5, -5), Background = Brushes.Transparent, Cursor = Cursors.SizeNWSE, ToolTip = "Trage ca să schimbi mărimea", Opacity = 0.85 };
                grip.Children.Add(arc);
                grip.MouseLeftButtonDown += (s, e) => { Select(slot, rebuild: false); BeginDrag(slot, card, e, resize: true); e.Handled = true; };
                o.Children.Add(grip);
            }
            // the size sits on the bottom edge, half outside, so the widget's own content stays fully visible
            var label = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(7, 1, 7, 1), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom,
                                     Margin = new Thickness(0, 0, 0, -9), IsHitTestVisible = false, BorderThickness = new Thickness(1), Child = Ui.T(slot.W + "×" + slot.H, 10.5, "MutedBrush", false, true) };
            label.SetResourceReference(Border.BackgroundProperty, "ChipHoverBrush");
            label.SetResourceReference(Border.BorderBrushProperty, "HoverBrush");
            if (slot == SelectedSlot) o.Children.Add(label);
            o.MouseLeftButtonDown += (s, e) => { Select(slot, rebuild: false); BeginDrag(slot, card, e, resize: false); };
            return o;
        }

        /// <summary>Is there another size of this widget that fits on the page (others may move)? Else no resize arc.</summary>
        private bool CanResize(WidgetSlot slot, WidgetDef def)
        {
            foreach (var sz in def.Sizes)
            {
                if (sz.W == slot.W && sz.H == slot.H) continue;
                var (list, map) = Copy();
                var me = map[slot];
                int col = Math.Min(slot.Col, Layout.Cols - sz.W), row = Math.Min(slot.Row, Layout.MaxRows - sz.H);
                if (col >= 0 && row >= 0 && Layout.TryPlace(list, me, col, row, sz.W, sz.H)) return true;
            }
            return false;
        }

        /// <summary>A throw-away copy of the layout, to try a move or a size without touching the real one.</summary>
        private (List<WidgetSlot> List, Dictionary<WidgetSlot, WidgetSlot> Map) Copy()
        {
            var map = Page.Widgets.ToDictionary(s => s, s => new WidgetSlot { Type = s.Type, Col = s.Col, Row = s.Row, W = s.W, H = s.H });
            return (map.Values.ToList(), map);
        }

        private static void Place(FrameworkElement el, int col, int row, int w, int h)
        {
            if (el == null) return;
            Grid.SetColumn(el, col * 2); Grid.SetRow(el, row * 2);
            Grid.SetColumnSpan(el, Math.Max(1, w * 2 - 1)); Grid.SetRowSpan(el, Math.Max(1, h * 2 - 1));
        }

        /// <summary>
        /// While dragging: the others move live to where they would end up (and the resized widget takes its new
        /// size), so you see the result before letting go. A place where it can't fit shows the ghost in red.
        /// </summary>
        private void PreviewReflow()
        {
            if (_dragSlot == null || _target == _previewed) return;
            _previewed = _target;
            var (list, map) = Copy();
            _targetOk = Layout.TryPlace(list, map[_dragSlot], _target.Col, _target.Row, _target.W, _target.H);
            foreach (var kv in _cards)
            {
                var s = kv.Key;
                var c = _targetOk ? map[s] : s;
                if (s == _dragSlot && !_resizing) continue;          // the moved card follows the pointer
                Place(kv.Value.Card, c.Col, c.Row, c.W, c.H);
                _chromes.TryGetValue(s, out var chrome);
                Place(chrome, c.Col, c.Row, c.W, c.H);
            }
            _ghost.SetResourceReference(Border.BorderBrushProperty, _targetOk ? "AccentBrush" : "HotBrush");
        }

        private static Button Small(string glyph, string tip, HorizontalAlignment h, VerticalAlignment v)
        {
            // like on the iPhone: round badges on the corners, half outside the widget, so they never cover its content
            var b = new Button { Style = Ui.S("IconButton"), Width = 22, Height = 22, ToolTip = tip, HorizontalAlignment = h, VerticalAlignment = v, Margin = new Thickness(-9) };
            var bg = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Child = Ui.Icon(glyph, 10), BorderThickness = new Thickness(1) };
            bg.SetResourceReference(Border.BackgroundProperty, "ChipHoverBrush");
            bg.SetResourceReference(Border.BorderBrushProperty, "HoverBrush");
            b.Content = bg;
            return b;
        }

        public void Select(WidgetSlot slot, bool rebuild = true)
        {
            if (SelectedSlot == slot) return;
            SelectedSlot = slot;
            Selected?.Invoke(slot);
            if (_editing && rebuild) Rebuild();      // during a drag the end of the drag rebuilds
        }

        // ------------------------------------------------------------------ drag to move / resize

        private WidgetSlot _dragSlot;
        private Border _dragCard;
        private bool _resizing, _moved;
        private Point _start;
        private (int Col, int Row, int W, int H) _target, _previewed;
        private bool _targetOk = true;
        private TranslateTransform _shift;

        private (double W, double H) Cell => ((_grid.ActualWidth + Gap) / Layout.Cols, RowH + Gap);

        private void BeginDrag(WidgetSlot slot, Border card, MouseButtonEventArgs e, bool resize)
        {
            if (!_editing) return;
            _dragSlot = slot; _dragCard = card; _resizing = resize; _moved = false;
            _start = e.GetPosition(_grid);
            _target = _previewed = (slot.Col, slot.Row, slot.W, slot.H);
            _targetOk = true;
            _shift = new TranslateTransform();
            if (!resize)
            {
                card.RenderTransform = _shift; Panel.SetZIndex(card, 50);
                if (_chromes.TryGetValue(slot, out var ch)) ch.Opacity = 0;      // its badges would stay behind; the ghost shows the spot
            }
            _grid.CaptureMouse();
            _grid.MouseMove += OnDragMove;
            _grid.MouseLeftButtonUp += OnDragEnd;
            _grid.LostMouseCapture += OnLost;
        }

        private void OnDragMove(object sender, MouseEventArgs e)
        {
            if (_dragSlot == null) return;
            var p = e.GetPosition(_grid);
            var d = p - _start;
            if (!_moved && Math.Abs(d.X) + Math.Abs(d.Y) < 4) return;
            _moved = true;
            var (cw, ch) = Cell;
            int maxRows = Layout.MaxRows;
            if (_resizing)
            {
                // the corner follows the pointer: the size is how many cells the pointer covers, snapped to the
                // closest size this widget allows that still fits on the page
                double wRaw = (p.X - _dragSlot.Col * cw) / cw, hRaw = (p.Y - _dragSlot.Row * ch) / ch;
                int w = Math.Clamp((int)Math.Ceiling(wRaw - 0.3), 1, Layout.Cols - _dragSlot.Col);
                int h = Math.Clamp((int)Math.Ceiling(hRaw - 0.3), 1, maxRows - _dragSlot.Row);
                var def = Catalog.Get(_dragSlot.Type);
                var fits = def.Sizes.Where(z => _dragSlot.Col + z.W <= Layout.Cols && _dragSlot.Row + z.H <= maxRows).ToList();
                if (fits.Count == 0) return;
                var size = fits.OrderBy(z => Math.Abs(z.W - w) * 2 + Math.Abs(z.H - h) * 3).First();
                _target = (_dragSlot.Col, _dragSlot.Row, size.W, size.H);
            }
            else
            {
                _shift.X = d.X; _shift.Y = d.Y;
                int col = (int)Math.Round(_dragSlot.Col + d.X / cw), row = (int)Math.Round(_dragSlot.Row + d.Y / ch);
                col = Math.Clamp(col, 0, Layout.Cols - _dragSlot.W);
                row = Math.Clamp(row, 0, maxRows - _dragSlot.H);
                _target = (col, row, _dragSlot.W, _dragSlot.H);
            }
            ShowGhost();
            PreviewReflow();
        }

        private void ShowGhost()
        {
            var (cw, ch) = Cell;
            _ghost.Visibility = Visibility.Visible;
            Canvas.SetLeft(_ghost, _target.Col * cw);
            Canvas.SetTop(_ghost, _target.Row * ch);
            _ghost.Width = _target.W * cw - Gap;
            _ghost.Height = _target.H * ch - Gap;
            _ghostLabel.Text = _target.W + " × " + _target.H;
            bool rowsGrow = _target.Row + _target.H > VisibleRows;
            if (rowsGrow) _grid.Height = Math.Min(Layout.MaxRows, _target.Row + _target.H) * ch - Gap;
        }

        private void OnDragEnd(object sender, MouseButtonEventArgs e) => EndDrag(true);
        private void OnLost(object sender, MouseEventArgs e) => EndDrag(false);

        private void EndDrag(bool apply)
        {
            if (_dragSlot == null) return;
            var slot = _dragSlot;
            _dragSlot = null;
            _grid.MouseMove -= OnDragMove;
            _grid.MouseLeftButtonUp -= OnDragEnd;
            _grid.LostMouseCapture -= OnLost;
            if (_grid.IsMouseCaptured) _grid.ReleaseMouseCapture();
            _ghost.Visibility = Visibility.Collapsed;
            if (_dragCard != null) { _dragCard.RenderTransform = null; Panel.SetZIndex(_dragCard, 0); }
            // pressed and released without moving: a tap, the host shows the widget's sizes (like on the iPhone)
            if (apply && !_moved && !_resizing) { Rebuild(); if (_cards.TryGetValue(slot, out var tc)) Tapped?.Invoke(slot, tc.Card); return; }
            if (apply && _moved && _targetOk && (_target.Col, _target.Row, _target.W, _target.H) != (slot.Col, slot.Row, slot.W, slot.H))
            {
                if (Layout.TryPlace(Page.Widgets, slot, _target.Col, _target.Row, _target.W, _target.H)) { Commit(); return; }
            }
            Rebuild();
        }

        // ------------------------------------------------------------------ drag from the gallery

        public const string DragFormat = "WinNotchWidget";

        /// <summary>What's being dragged from the gallery: "type" (default size) or "type|W|H" (a size picked in its preview).</summary>
        internal static (string Type, (int W, int H)? Size) ParseDrag(object data)
        {
            if (!(data is string s) || s.Length == 0) return (null, null);
            var parts = s.Split('|');
            if (parts.Length == 3 && int.TryParse(parts[1], out int w) && int.TryParse(parts[2], out int h)) return (parts[0], (w, h));
            return (parts[0], null);
        }

        private (int Col, int Row, int W, int H)? DropTarget(DragEventArgs e)
        {
            if (!_editing) return null;
            var (type, size) = ParseDrag(e.Data.GetData(DragFormat));
            var def = type != null ? Catalog.Get(type) : null;
            if (def == null) return null;
            var (cw, ch) = Cell;
            var p = e.GetPosition(_grid);
            // the widget's middle goes where the pointer is; smaller sizes are tried when it doesn't fit there
            foreach (var sz in new[] { size ?? def.DefaultSize }.Concat(def.Sizes.OrderBy(z => z.W * z.H)))
            {
                int col = Math.Clamp((int)Math.Floor(p.X / cw - (sz.W - 1) / 2.0), 0, Layout.Cols - sz.W);
                int row = Math.Clamp((int)Math.Floor(p.Y / ch - (sz.H - 1) / 2.0), 0, Layout.MaxRows - sz.H);
                if (Layout.IsFree(Page.Widgets, col, row, sz.W, sz.H)) return (col, row, sz.W, sz.H);
            }
            // nothing free right there: the default size where the pointer is, the others make room (or it goes elsewhere)
            var dz = size ?? def.DefaultSize;
            return (Math.Clamp((int)Math.Floor(p.X / cw - (dz.W - 1) / 2.0), 0, Layout.Cols - dz.W),
                    Math.Clamp((int)Math.Floor(p.Y / ch - (dz.H - 1) / 2.0), 0, Layout.MaxRows - dz.H), dz.W, dz.H);
        }

        private void OnGalleryDragOver(object sender, DragEventArgs e)
        {
            var t = DropTarget(e);
            e.Effects = t == null ? DragDropEffects.None : DragDropEffects.Copy;
            e.Handled = true;
            if (t == null) { _ghost.Visibility = Visibility.Collapsed; return; }
            _target = t.Value;
            ShowGhost();
        }

        private void OnGalleryDrop(object sender, DragEventArgs e)
        {
            var t = DropTarget(e);
            var (type, _) = ParseDrag(e.Data.GetData(DragFormat));
            _ghost.Visibility = Visibility.Collapsed;
            e.Handled = true;
            if (t == null || type == null) { Rebuild(); return; }
            var (col, row, w, h) = t.Value;
            var slot = Catalog.NewSlot(type, (w, h));
            Page.Widgets.Add(slot);
            if (!Layout.TryPlace(Page.Widgets, slot, col, row, w, h))
            {
                Page.Widgets.Remove(slot);
                bool ok = Add(type, (w, h));
                if (!ok) Rebuild();
                Dropped?.Invoke(ok);
                return;
            }
            SelectedSlot = slot;
            Commit();
            Selected?.Invoke(slot);
            Dropped?.Invoke(true);
        }

        /// <summary>A widget dragged from the gallery was dropped (true) or didn't fit (false).</summary>
        public event Action<bool> Dropped;
        /// <summary>A widget was clicked (not dragged) in edit mode: the host shows its sizes.</summary>
        public event Action<WidgetSlot, FrameworkElement> Tapped;

        /// <summary>Resizes a widget where it is (others move out of the way); if there's no room there, somewhere else.</summary>
        public bool Resize(WidgetSlot slot, (int W, int H) size)
        {
            if (slot.W == size.W && slot.H == size.H) return true;
            int col = Math.Min(slot.Col, Layout.Cols - size.W), row = Math.Min(slot.Row, Layout.MaxRows - size.H);
            bool ok = Layout.TryPlace(Page.Widgets, slot, col, row, size.W, size.H);
            if (!ok)
            {
                var spot = Layout.FindFree(Page.Widgets, size.W, size.H, slot);
                ok = spot != null && Layout.TryPlace(Page.Widgets, slot, spot.Value.Col, spot.Value.Row, size.W, size.H);
            }
            if (ok) Commit();
            return ok;
        }

        // ------------------------------------------------------------------ adding

        /// <summary>Adds a widget at the first free place. False when the page is full.</summary>
        public bool Add(string type, (int W, int H)? size = null)
        {
            var slot = Catalog.NewSlot(type, size);
            var spot = Layout.FindFree(Page.Widgets, slot.W, slot.H);
            if (spot == null)
            {
                // try the widget's other sizes, smallest first
                foreach (var sz in Catalog.Get(type).Sizes.OrderBy(s => s.W * s.H))
                {
                    spot = Layout.FindFree(Page.Widgets, sz.W, sz.H);
                    if (spot != null) { slot.W = sz.W; slot.H = sz.H; break; }
                }
                if (spot == null) return false;
            }
            (slot.Col, slot.Row) = spot.Value;
            Page.Widgets.Add(slot);
            SelectedSlot = slot;
            Commit();
            Selected?.Invoke(slot);
            return true;
        }

        /// <summary>The widget's settings or size changed: rebuild it.</summary>
        public void Commit()
        {
            Rebuild();
            Changed?.Invoke();
        }

        // ------------------------------------------------------------------ pane

        public override void Refresh()
        {
            foreach (var (_, wdg) in _cards.Values) try { wdg.Refresh(); } catch (Exception ex) { App.Log("Widget: " + ex.Message); }
        }

        public override void Fast()
        {
            foreach (var (_, wdg) in _cards.Values) wdg.Fast();
        }

        public void MediaChanged()
        {
            foreach (var (_, wdg) in _cards.Values) try { wdg.MediaChanged(); } catch { }
        }

        public override void Shown() { MediaChanged(); Refresh(); }

        public override void Hidden()
        {
            if (_dragSlot != null) EndDrag(false);
        }
    }
}
