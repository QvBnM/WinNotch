using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WinNotch.Widgets;

namespace WinNotch.Features.WindowV2
{
    /// <summary>
    /// P52: what you can add, as the lower half of the Workspace — a wide, short band, which is the shape the space
    /// actually has. The gallery of the notch is built for a tall narrow panel (its categories run down a 170 px
    /// column); here the categories are a row of chips across the top and the widgets a strip that scrolls sideways,
    /// so the band shows widgets instead of its own scrollbars.
    /// <para>Nothing about a widget is reimplemented: the preview is the live one (<see cref="Gallery.LivePreview"/>),
    /// the sizes come from <see cref="Gallery.SizePreviews"/> and the drag is the shared gesture
    /// (<see cref="Core.Ui.WidgetDrag"/>). Only the arrangement is this band's.</para>
    /// </summary>
    internal sealed class WidgetLibrary : Grid
    {
        private readonly Action<string, (int W, int H)> _add;
        private readonly Func<Panel> _popupLayer;
        private readonly StackPanel _chips = new StackPanel { Orientation = Orientation.Horizontal };
        private readonly StackPanel _strip = new StackPanel { Orientation = Orientation.Horizontal };
        private readonly TextBlock _message = Ui.T("", 12, "WarnBrush");
        private readonly ScrollViewer _stripScroll;
        private string _category = All;
        private (Action Hide, Action Close) _popup;

        private const string All = "Toate";

        internal WidgetLibrary(Action<string, (int W, int H)> add, Func<Panel> popupLayer)
        {
            _add = add; _popupLayer = popupLayer;
            RowDefinitions.Add(new RowDefinition { Height = Ui.Auto });
            RowDefinitions.Add(new RowDefinition { Height = Ui.Auto });
            RowDefinitions.Add(new RowDefinition { Height = Ui.Star() });

            var head = Ui.Cols(Ui.Auto, Ui.Star(), Ui.Auto);
            head.Put(Ui.V(0, Ui.T("Adaugă un widget", 14.5, "InkBrush", true),
                             Ui.T("trage-l sus, pe pagină  ·  click pe el pentru toate mărimile", 11.5, "DimBrush")));
            _message.VerticalAlignment = VerticalAlignment.Center;
            _message.TextWrapping = TextWrapping.Wrap;
            head.Put(_message, 1);
            head.Margin = new Thickness(0, 0, 0, LayoutRules.Gap);
            this.Put(head);

            _chips.Margin = new Thickness(0, 0, 0, LayoutRules.Gap);
            this.Put(new ScrollViewer
            {
                Content = _chips, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            }, 0, 1);

            _stripScroll = new ScrollViewer
            {
                Style = Ui.S("SlimScroll"), Content = _strip,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            };
            // the wheel scrolls the strip sideways: there is nothing to scroll downwards in a band one row tall
            _stripScroll.PreviewMouseWheel += (o, e) =>
            {
                _stripScroll.ScrollToHorizontalOffset(_stripScroll.HorizontalOffset - e.Delta);
                e.Handled = true;
            };
            this.Put(_stripScroll, 0, 2);
            Build();
        }

        /// <summary>A line beside the title ("the page is full"), cleared with an empty text.</summary>
        internal void Message(string text) => _message.Text = text ?? "";

        /// <summary>P51: Esc closes the size pop-up before anything else.</summary>
        internal bool CloseOpenPopup()
        {
            if (_popup.Close == null) return false;
            _popup.Close();
            _popup = (null, null);
            return true;
        }

        private void Build()
        {
            _chips.Children.Clear();
            foreach (var c in new[] { All }.Concat(Catalog.Categories))
            {
                var cat = c;
                bool on = c == _category;
                var chip = new Button
                {
                    Style = Ui.S("NavButton"), Content = Ui.T(c, 12.5, on ? "OnAccentBrush" : "MutedBrush", on),
                    Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 6, 0), Cursor = Cursors.Hand,
                };
                if (on) chip.SetResourceReference(Control.BackgroundProperty, "AccentBrush");
                System.Windows.Automation.AutomationProperties.SetName(chip, c);
                chip.Click += (o, e) => { if (_category != cat) { _category = cat; Build(); } };
                _chips.Children.Add(chip);
            }

            _strip.Children.Clear();
            foreach (var d in Catalog.All.Where(d => _category == All || d.Category == _category))
            {
                var def = d;
                var preview = new Border { Height = 92, Child = Gallery.LivePreview(d, d.DefaultSize, PreviewScale(d)), IsHitTestVisible = false };
                var sizes = Ui.T(string.Join("  ·  ", d.Sizes.Select(z => z.W + "×" + z.H)), 10.5, "DimBrush", false, true);
                var card = new Border
                {
                    Width = 206, Margin = new Thickness(0, 0, LayoutRules.Gap, 4), Padding = new Thickness(12),
                    CornerRadius = new CornerRadius(LayoutRules.CardRadius), Cursor = Cursors.Hand,
                    Child = Ui.V(6, preview, Ui.T(d.Name, 13, "InkBrush", true), sizes),
                    ToolTip = d.Description,
                };
                card.SetResourceReference(Border.BackgroundProperty, "ChipBrush");
                Gallery.Hover(card);
                Core.Ui.WidgetDrag.Bind(card, def.Type, () => OpenSizes(def));
                _strip.Children.Add(card);
            }
        }

        /// <summary>The preview is shrunk to the card's room, never enlarged.</summary>
        private static double PreviewScale(WidgetDef d)
        {
            double w = d.DefaultSize.W * Gallery.Cell - WidgetPage.Gap;
            double h = d.DefaultSize.H * (WidgetPage.RowH + WidgetPage.Gap) - WidgetPage.Gap;
            return Math.Min(0.58, Math.Min(182 / Math.Max(1, w), 88 / Math.Max(1, h)));
        }

        /// <summary>Every size of this widget, over the window: click one to add it, or drag it onto the page.</summary>
        private void OpenSizes(WidgetDef d)
        {
            var layer = _popupLayer?.Invoke();
            if (layer == null) { _add(d.Type, d.DefaultSize); return; }
            var box = new StackPanel();
            box.Children.Add(Ui.T(d.Name, 15, "InkBrush", true));
            var hint = Ui.T(d.Description, 12, "MutedBrush");
            hint.TextWrapping = TextWrapping.Wrap;
            hint.Margin = new Thickness(0, 3, 0, LayoutRules.Gap);
            box.Children.Add(hint);
            box.Children.Add(Gallery.SizePreviews(d, null, size => { CloseOpenPopup(); _add(d.Type, size); },
                                                  (el, sz, click) => Core.Ui.WidgetDrag.Bind(el, d.Type + "|" + sz.W + "|" + sz.H, click,
                                                                                             before: () => _popup.Hide?.Invoke(),
                                                                                             after: () => CloseOpenPopup())));
            _popup = Gallery.ShowPopupIn(layer, box, new Thickness(0), 760, onClosed: () => _popup = (null, null));
        }
    }
}
