using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace WinNotch.Features.WindowV2
{
    /// <summary>
    /// P52/P70: the pieces every page of this window is built from. P70 redrew them as an instrument panel instead of
    /// a settings sheet: a group is an engraved label over a run of rows, a row is a name, one line of help and its
    /// control at the right edge, and the rows are told apart by a hairline — no cards, no boxes inside boxes. Every
    /// number, path and shortcut is monospaced, so a column of values lines up.
    /// <para>All of them draw with the theme's own tokens, so the window has one look wherever you are in it, in any
    /// theme. The names here did not change with the look: the pages that call them did not have to be rewritten.</para>
    /// </summary>
    internal static class V2Controls
    {
        internal const double LabelWidth = 210;

        /// <summary>A row is tall enough to read a name and a line of help under it without them touching.</summary>
        internal const double RowPadV = 11;

        /// <summary>
        /// P70: set by the window while it is open, cleared when it closes. Every labelled control reports what the
        /// user just changed ("Poziție", "Centru") so the footer can say it — this window applies on the spot, and the
        /// only honest replacement for a "Save" button is telling you what it just did.
        /// </summary>
        internal static Action<string, string> Report;

        private static string TextOf(IReadOnlyList<(string Value, string Text)> options, string value)
        {
            if (options == null) return value ?? "";
            foreach (var o in options)
                if (string.Equals(o.Value, value, StringComparison.Ordinal)) return o.Text;
            return value ?? "";
        }

        // ------------------------------------------------------------------ the new primitives

        /// <summary>
        /// A group's name, engraved: the label face, in capitals, small and quiet. It names a run of rows; it is not a
        /// heading you read, it is a tab on a drawer.
        /// </summary>
        internal static TextBlock Eyebrow(string text)
        {
            var t = new TextBlock
            {
                Text = (text ?? "").ToUpperInvariant(),
                FontFamily = (System.Windows.Media.FontFamily)Application.Current.FindResource("LabelFont"),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = Ui.B("DimBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            };
            return t;
        }

        /// <summary>A page's name, in the label face: the one piece of type in the window allowed to be big.</summary>
        internal static TextBlock Title(string text)
        {
            return new TextBlock
            {
                Text = text ?? "",
                FontFamily = (System.Windows.Media.FontFamily)Application.Current.FindResource("LabelFont"),
                FontSize = 26,
                FontWeight = FontWeights.SemiBold,
                Foreground = Ui.B("InkBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
        }

        /// <summary>The hairline that tells two rows apart. One pixel, the theme's line colour, nothing else.</summary>
        internal static Border Rule(double top = 0, double bottom = 0)
        {
            var b = new Border { Height = 1, Margin = new Thickness(0, top, 0, bottom), SnapsToDevicePixels = true };
            b.SetResourceReference(Border.BackgroundProperty, "TrackBrush");
            return b;
        }

        /// <summary>A number, a path or a shortcut. Monospaced, so values in a column line up under each other.</summary>
        internal static TextBlock Mono(string text, double size = 12.5, string brush = "MutedBrush")
        {
            var t = Ui.T(text, size, brush, false, true);
            t.VerticalAlignment = VerticalAlignment.Center;
            return t;
        }

        // ------------------------------------------------------------------ groups and rows

        /// <summary>
        /// A group of options: its engraved name, one line saying what the group is for, then the rows. It is drawn
        /// without a box — the rows' own hairlines are the structure, so a page reads as one surface, not a pile of
        /// cards. The name is kept from the old API: every page already calls <c>Card</c>.
        /// </summary>
        internal static Border Card(string title, string hint, UIElement body)
        {
            var sp = new StackPanel();
            var head = Eyebrow(title);
            head.Margin = new Thickness(0, 0, 0, string.IsNullOrEmpty(hint) ? 8 : 4);
            sp.Children.Add(head);
            if (!string.IsNullOrEmpty(hint))
            {
                var h = Ui.T(hint, 12, "DimBrush");
                h.TextWrapping = TextWrapping.Wrap;
                h.Margin = new Thickness(0, 0, 0, 10);
                sp.Children.Add(h);
            }
            sp.Children.Add(body);
            // Transparent on purpose: the group is a run of rows on the page, not a panel floating over it.
            return new Border { Child = sp, Background = null, Margin = new Thickness(0, 0, 0, LayoutRules.Pad) };
        }

        /// <summary>A stack of rows, the usual body of a group.</summary>
        internal static StackPanel Stack(params UIElement[] rows)
        {
            var sp = new StackPanel();
            foreach (var r in rows) if (r != null) sp.Children.Add(r);
            return sp;
        }

        /// <summary>
        /// One option: its name and, under it, one line of help; its control at the right edge; a hairline below. The
        /// help is one line — anything longer belongs in the documentation, not in the window.
        /// </summary>
        internal static FrameworkElement Row(string label, UIElement control, string hint = null)
        {
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, LayoutRules.Pad, 0) };
            var name = Ui.T(label, 14, "InkBrush");
            name.TextWrapping = TextWrapping.Wrap;
            text.Children.Add(name);
            if (!string.IsNullOrEmpty(hint))
            {
                var h = Ui.T(hint, 12, "DimBrush");
                h.TextWrapping = TextWrapping.Wrap;
                h.Margin = new Thickness(0, 2, 0, 0);
                text.Children.Add(h);
            }

            var g = Ui.Cols(Ui.Star(), Ui.Auto);
            g.Margin = new Thickness(0, RowPadV, 0, RowPadV);
            g.Put(text);
            if (control != null)
            {
                if (control is FrameworkElement fe)
                {
                    fe.HorizontalAlignment = HorizontalAlignment.Right;
                    fe.VerticalAlignment = VerticalAlignment.Center;
                }
                g.Put(control, 1);
            }

            var row = new StackPanel();
            row.Children.Add(g);
            row.Children.Add(Rule());
            return row;
        }

        /// <summary>A line of help, optionally indented to line up under a control.</summary>
        internal static TextBlock Hint(string text, double indent = 0)
        {
            var t = Ui.T(text, 11.5, "DimBrush");
            t.TextWrapping = TextWrapping.Wrap;
            t.Margin = new Thickness(indent, 4, 0, 0);
            return t;
        }

        /// <summary>
        /// An on/off switch: the name on the left, the switch on the right, like every other row. Changes apply the
        /// moment you make them — this window has no "Save" button, and nothing waits.
        /// </summary>
        internal static FrameworkElement Toggle(string label, bool value, Action<bool> set, string hint = null) =>
            Row(label, Switch(value, v => { set(v); Report?.Invoke(label, v ? "pornit" : "oprit"); }, label), hint);

        /// <summary>The switch on its own, for the rows that build their own layout (a feature's name and description).</summary>
        internal static Button Switch(bool value, Action<bool> set, string name)
        {
            var knob = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(9), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(3, 0, 0, 0) };
            var track = new Border { Width = 44, Height = 24, CornerRadius = new CornerRadius(12), Child = knob, HorizontalAlignment = HorizontalAlignment.Left, Cursor = Cursors.Hand, BorderThickness = new Thickness(1) };
            var btn = new Button { Style = Ui.S("NavButton"), Content = track, Padding = new Thickness(0), Cursor = Cursors.Hand, HorizontalAlignment = HorizontalAlignment.Left };
            System.Windows.Automation.AutomationProperties.SetName(btn, name ?? "");

            bool on = value;
            void Paint()
            {
                track.SetResourceReference(Border.BackgroundProperty, on ? "AccentBrush" : "SegBrush");
                track.SetResourceReference(Border.BorderBrushProperty, on ? "AccentBrush" : "BorderBrush");
                knob.SetResourceReference(Border.BackgroundProperty, on ? "OnAccentBrush" : "DimBrush");
                knob.HorizontalAlignment = on ? HorizontalAlignment.Right : HorizontalAlignment.Left;
                knob.Margin = on ? new Thickness(0, 0, 3, 0) : new Thickness(3, 0, 0, 0);
            }
            Paint();
            btn.Click += (o, e) => { on = !on; Paint(); set(on); };
            return btn;
        }

        /// <summary>
        /// Two or three choices, shown all at once: you see what else there is without opening anything. More than
        /// three go in a <see cref="ChoiceBox"/> instead — a row of six buttons is a wall, not a choice.
        /// </summary>
        internal static FrameworkElement Segmented(IReadOnlyList<(string Value, string Text)> options, string value, Action<string> set, string name)
        {
            var strip = new StackPanel { Orientation = Orientation.Horizontal };
            var frame = new Border
            {
                Child = strip, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9),
                HorizontalAlignment = HorizontalAlignment.Right, ClipToBounds = true,
            };
            frame.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");

            var buttons = new List<(Button Btn, string Value, TextBlock Text)>();
            void Paint()
            {
                foreach (var (btn, v, text) in buttons)
                {
                    bool on = string.Equals(v, value, StringComparison.Ordinal);
                    if (on) btn.SetResourceReference(Control.BackgroundProperty, "AccentBrush");
                    else btn.SetResourceReference(Control.BackgroundProperty, "ChipHoverBrush");
                    text.SetResourceReference(TextBlock.ForegroundProperty, on ? "OnAccentBrush" : "MutedBrush");
                    text.FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
                }
            }

            for (int i = 0; i < options.Count; i++)
            {
                if (i > 0)
                {
                    var sep = new Border { Width = 1 };
                    sep.SetResourceReference(Border.BackgroundProperty, "BorderBrush");
                    strip.Children.Add(sep);
                }
                var opt = options[i];
                var label = Ui.T(opt.Text, 13, "MutedBrush");
                var btn = new Button { Style = Ui.S("NavButton"), Padding = new Thickness(14, 7, 14, 7), Content = label, Cursor = Cursors.Hand };
                System.Windows.Automation.AutomationProperties.SetName(btn, (name ?? "") + ": " + opt.Text);
                btn.Click += (o, e) => { value = opt.Value; Paint(); set(opt.Value); };
                buttons.Add((btn, opt.Value, label));
                strip.Children.Add(btn);
            }
            Paint();
            return frame;
        }

        /// <summary>
        /// A number you change in steps, never one you type: the value between two buttons, monospaced so it does not
        /// jump sideways as it changes.
        /// </summary>
        internal static FrameworkElement Stepper(double value, double min, double max, double step,
                                                 Func<double, string> format, Action<double> set, string name)
        {
            double v = Math.Clamp(value, min, max);
            var text = Mono(format(v), 13, "InkBrush");
            text.HorizontalAlignment = HorizontalAlignment.Center;
            var middle = new Border { Child = text, MinWidth = 76, BorderThickness = new Thickness(0, 1, 0, 1), Padding = new Thickness(8, 6, 8, 6) };
            middle.SetResourceReference(Border.BackgroundProperty, "SegBrush");
            middle.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");

            Button End(string glyph, double delta, string tip)
            {
                var b = new Button
                {
                    Style = Ui.S("NavButton"), Padding = new Thickness(11, 6, 11, 6), Cursor = Cursors.Hand,
                    Content = Ui.T(glyph, 13, "InkBrush"),
                };
                System.Windows.Automation.AutomationProperties.SetName(b, (name ?? "") + ": " + tip);
                b.SetResourceReference(Control.BackgroundProperty, "ChipHoverBrush");
                b.Click += (o, e) =>
                {
                    double next = Math.Clamp(v + delta, min, max);
                    if (Math.Abs(next - v) < 0.0001) return;
                    v = next;
                    text.Text = format(v);
                    set(v);
                };
                return b;
            }

            var strip = new StackPanel { Orientation = Orientation.Horizontal };
            strip.Children.Add(End("−", -step, "mai puțin"));
            strip.Children.Add(middle);
            strip.Children.Add(End("+", step, "mai mult"));
            var frame = new Border
            {
                Child = strip, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9),
                HorizontalAlignment = HorizontalAlignment.Right, ClipToBounds = true,
            };
            frame.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
            return frame;
        }

        /// <summary>Something that cannot be undone: said in the danger colour, and never the first thing you reach.</summary>
        internal static Button Danger(string text, Action click)
        {
            var b = new Button
            {
                Style = Ui.S("NavButton"), Padding = new Thickness(14, 7, 14, 7), Cursor = Cursors.Hand,
                Content = Ui.T(text, 13, "HotBrush"), BorderThickness = new Thickness(1),
            };
            System.Windows.Automation.AutomationProperties.SetName(b, text ?? "");
            b.SetResourceReference(Control.BorderBrushProperty, "HotBrush");
            if (click != null) b.Click += (o, e) => click();
            return b;
        }

        /// <summary>One choice out of several: a drop-down in the theme's colours.</summary>
        internal static FrameworkElement Choice(string label, IReadOnlyList<(string Value, string Text)> options, string value, Action<string> set, string hint = null)
        {
            void Pick(string v) { set(v); Report?.Invoke(label, TextOf(options, v)); }
            // Two or three choices are shown all at once; more than that would be a wall of buttons, so they fold away.
            if (options != null && options.Count > 1 && options.Count <= 3 && options.All(o => (o.Text ?? "").Length <= 14))
                return Row(label, Segmented(options, value, Pick, label), hint);
            var box = ChoiceBox(options, value, Pick);
            box.HorizontalAlignment = HorizontalAlignment.Right;
            box.MinWidth = 240;
            return Row(label, box, hint);
        }

        /// <summary>The drop-down on its own, for the rows that build their own layout.</summary>
        internal static ComboBox ChoiceBox(IReadOnlyList<(string Value, string Text)> options, string value, Action<string> set)
        {
            var box = new ComboBox { Style = Ui.S("V2Combo") };
            foreach (var (v, t) in options) box.Items.Add(new ComboBoxItem { Tag = v, Content = t });
            box.SelectedItem = box.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == (value ?? ""))
                               ?? box.Items.Cast<ComboBoxItem>().FirstOrDefault();
            box.SelectionChanged += (o, e) => { if (box.SelectedItem is ComboBoxItem it) set((string)it.Tag); };
            return box;
        }

        /// <summary>A slider with its value written beside it, monospaced so the row does not twitch as you drag.</summary>
        internal static FrameworkElement SliderRow(string label, double min, double max, double step, double value,
                                                   Func<double, string> format, Action<double> set, string hint = null)
        {
            var g = Ui.Cols(Ui.Px(200), Ui.Px(76));
            var slider = new Slider
            {
                Minimum = min, Maximum = max, Value = Math.Clamp(value, min, max), TickFrequency = step,
                IsSnapToTickEnabled = step > 0, IsMoveToPointEnabled = true, VerticalAlignment = VerticalAlignment.Center,
            };
            System.Windows.Automation.AutomationProperties.SetName(slider, label ?? "");
            var text = Mono(format(value), 13, "InkBrush");
            text.HorizontalAlignment = HorizontalAlignment.Right;
            slider.ValueChanged += (o, e) =>
            {
                text.Text = format(e.NewValue);
                set(e.NewValue);
                Report?.Invoke(label, text.Text);
            };
            g.Put(slider);
            g.Put(text, 1);
            return Row(label, g, hint);
        }

        /// <summary>A text field in the theme's colours (the default WPF box is a white rectangle whatever the theme).</summary>
        internal static TextBox Field(string text, double width = double.NaN)
        {
            var box = new TextBox { Text = text ?? "", Padding = new Thickness(9, 6, 9, 6), BorderThickness = new Thickness(1) };
            if (!double.IsNaN(width)) box.Width = width;
            box.SetResourceReference(Control.BackgroundProperty, "SegBrush");
            box.SetResourceReference(Control.ForegroundProperty, "InkBrush");
            box.SetResourceReference(Control.BorderBrushProperty, "BorderBrush");
            box.SetResourceReference(TextBoxBase.CaretBrushProperty, "AccentBrush");
            return box;
        }

        /// <summary>A text field that saves shortly after you stop typing.</summary>
        internal static FrameworkElement TextRow(string label, string value, Action<string> set, Action<Action> soon, string hint = null)
        {
            var box = Field(value);
            box.MinWidth = 240;
            box.TextChanged += (o, e) => { var v = box.Text; soon(() => set(v)); };
            return Row(label, box, hint);
        }

        /// <summary>A small square button with a glyph, for the ▲ ▼ ✕ of a list.</summary>
        internal static Button Mini(string glyph, Action click, string tip)
        {
            var b = Ui.IconBtn(glyph, click, tip, 26, 11);
            System.Windows.Automation.AutomationProperties.SetName(b, tip ?? "");
            b.Margin = new Thickness(2, 0, 0, 0);
            return b;
        }

        /// <summary>A row of a list: a number, a name, and its own buttons on the right.</summary>
        internal static FrameworkElement ListRow(UIElement left, UIElement middle, UIElement right)
        {
            var g = Ui.Cols(Ui.Auto, Ui.Star(), Ui.Auto);
            if (left != null) g.Put(left);
            g.Put(middle, 1);
            if (right != null) g.Put(right, 2);
            g.Margin = new Thickness(0, 4, 0, 4);
            return g;
        }
    }
}
