using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WinNotch.Features.PagesFormat
{
    /// <summary>
    /// P75: the few pieces every one of the four pages is built from — a card, an engraved label, a number, a bar, a
    /// chip. The rules are in <see cref="PageFormat"/> (pure, tested); this only turns them into WPF, once, so a page
    /// is a list of what it shows and not a second copy of the recipe.
    ///
    /// Every colour goes through <c>SetResourceReference</c>, so a theme change re-colours a page that is already on
    /// screen; nothing here holds a colour of its own.
    /// </summary>
    internal static class Deck
    {
        private static FontFamily Face(bool mono) =>
            (FontFamily)Application.Current.FindResource(mono ? "PageMono" : "PageFont");

        /// <summary>One piece of text, by what it is for. <paramref name="reading"/> only applies to numbers.</summary>
        public static TextBlock T(Ink ink, string text = "", PageFormat.Reading reading = PageFormat.Reading.Plain)
        {
            var t = new TextBlock
            {
                Text = ink == Ink.Eyebrow || ink == Ink.Unit ? PageFormat.Eyebrow(text) : text ?? "",
                FontFamily = Face(PageFormat.Mono(ink)),
                FontSize = PageFormat.Size(ink),
                FontWeight = PageFormat.Strong(ink) ? (PageFormat.Mono(ink) ? FontWeights.Medium : FontWeights.SemiBold) : FontWeights.Normal,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            // The plates space the engraved labels by 0.085 em — under a pixel at 9.5 px. WPF has no letter-spacing on
            // a TextBlock, and faking it with thin spaces would be three times too wide and would break trimming, so
            // the label is drawn without it: the capitals, the face and the colour are what make it read as engraved.
            // PageFormat.Tracking keeps the number, because that is what the plates say.
            Paint(t, reading == PageFormat.Reading.Plain ? PageFormat.BrushKey(ink) : PageFormat.ReadingBrush(reading));
            return t;
        }

        /// <summary>A number with its unit beside it, the unit engraved ("48,2 MB/S").</summary>
        public static StackPanel Value(out TextBlock number, string unit, Ink ink = Ink.Value,
                                       PageFormat.Reading reading = PageFormat.Reading.Plain)
        {
            number = T(ink, "—", reading);
            var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(number);
            if (!string.IsNullOrEmpty(unit))
            {
                var u = T(Ink.Unit, unit);
                u.Margin = new Thickness(4, 1, 0, 0);
                sp.Children.Add(u);
            }
            return sp;
        }

        /// <summary>A card: the one surface a page is made of.</summary>
        public static Border Card(UIElement child)
        {
            var b = new Border
            {
                CornerRadius = new CornerRadius(PageFormat.CardRadius),
                Padding = new Thickness(PageFormat.CardPadH, PageFormat.CardPadV, PageFormat.CardPadH, PageFormat.CardPadV),
                Child = child,
                ClipToBounds = true,
            };
            b.SetResourceReference(Border.BackgroundProperty, "ChipBrush");
            return b;
        }

        /// <summary>A capsule: a chip or a small button.</summary>
        public static Border Chip(UIElement child, double padH = 9, double padV = 3)
        {
            var b = new Border
            {
                CornerRadius = new CornerRadius(999),
                Padding = new Thickness(padH, padV, padH, padV),
                Child = child,
                VerticalAlignment = VerticalAlignment.Center,
            };
            b.SetResourceReference(Border.BackgroundProperty, "ChipHoverBrush");
            return b;
        }

        /// <summary>A card's first line: the engraved group name, and whatever belongs at its right end.</summary>
        public static Grid Head(string eyebrow, UIElement right = null)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var e = T(Ink.Eyebrow, eyebrow);
            g.Children.Add(e);
            if (right != null)
            {
                Grid.SetColumn(right, 1);
                g.Children.Add(right);
            }
            return g;
        }

        /// <summary>A reading bar, 3 px: the rail and the fill, which the caller drives through the transform.</summary>
        public static Grid Bar(out ScaleTransform fill, string fillBrush = "AccentBrush")
        {
            var g = new Grid { Height = PageFormat.BarHeight, VerticalAlignment = VerticalAlignment.Center };
            var rail = new Border { CornerRadius = new CornerRadius(PageFormat.BarHeight / 2) };
            rail.SetResourceReference(Border.BackgroundProperty, "TrackBrush");
            fill = new ScaleTransform(0, 1);
            var f = new Border
            {
                CornerRadius = new CornerRadius(PageFormat.BarHeight / 2),
                RenderTransform = fill,
                RenderTransformOrigin = new Point(0, 0),
            };
            f.SetResourceReference(Border.BackgroundProperty, fillBrush);
            g.Children.Add(rail);
            g.Children.Add(f);
            return g;
        }

        /// <summary>Re-points a piece of text at another theme brush (a reading that changed what it is about).</summary>
        public static void Paint(TextBlock t, string brushKey) =>
            t.SetResourceReference(TextBlock.ForegroundProperty, brushKey);

        /// <summary>A column of children with the same gap between them.</summary>
        public static StackPanel Stack(double gap, params UIElement[] kids)
        {
            var sp = new StackPanel { Orientation = Orientation.Vertical };
            for (int i = 0; i < kids.Length; i++)
            {
                if (kids[i] == null) continue;
                if (sp.Children.Count > 0 && kids[i] is FrameworkElement fe)
                    fe.Margin = new Thickness(fe.Margin.Left, gap, fe.Margin.Right, fe.Margin.Bottom);
                sp.Children.Add(kids[i]);
            }
            return sp;
        }

        /// <summary>A row of children with the same gap between them.</summary>
        public static StackPanel Row(double gap, params UIElement[] kids)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            for (int i = 0; i < kids.Length; i++)
            {
                if (kids[i] == null) continue;
                if (sp.Children.Count > 0 && kids[i] is FrameworkElement fe)
                    fe.Margin = new Thickness(gap, fe.Margin.Top, fe.Margin.Right, fe.Margin.Bottom);
                sp.Children.Add(kids[i]);
            }
            return sp;
        }

        /// <summary>The two columns a page is laid out in, at the width the notch actually has.</summary>
        public static Grid Columns(double split, out Grid left, out Grid right)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(split, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(PageFormat.Gutter) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - split, GridUnitType.Star) });
            left = new Grid();
            right = new Grid();
            Grid.SetColumn(right, 2);
            g.Children.Add(left);
            g.Children.Add(right);
            return g;
        }
    }
}
