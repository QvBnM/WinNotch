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
    /// P52: the pieces every page of this window is built from — a card, a labelled row, a switch, a list of choices,
    /// a slider, a text field. All of them draw with the theme's own tokens, so the window has one look wherever you
    /// are in it. This is the whole reason the settings could not keep the old window's content: that is a fixed light
    /// sheet, and it never belonged on a dark theme.
    /// </summary>
    internal static class V2Controls
    {
        internal const double LabelWidth = 210;

        /// <summary>A card with a title, one line of help and a body.</summary>
        internal static Border Card(string title, string hint, UIElement body)
        {
            var sp = new StackPanel();
            var head = Ui.T(title, 15, "InkBrush", true);
            sp.Children.Add(head);
            if (!string.IsNullOrEmpty(hint))
            {
                var h = Ui.T(hint, 12, "MutedBrush");
                h.TextWrapping = TextWrapping.Wrap;
                h.Margin = new Thickness(0, 3, 0, 12);
                sp.Children.Add(h);
            }
            else head.Margin = new Thickness(0, 0, 0, 12);
            sp.Children.Add(body);
            var card = Ui.Card(sp, 18, 18, LayoutRules.CardRadius);
            card.Margin = new Thickness(0, 0, 0, LayoutRules.Gap + 2);
            return card;
        }

        /// <summary>A stack of rows, the usual body of a card.</summary>
        internal static StackPanel Stack(params UIElement[] rows)
        {
            var sp = new StackPanel();
            foreach (var r in rows) if (r != null) sp.Children.Add(r);
            return sp;
        }

        /// <summary>Label on the left, control on the right, optional line of help under them.</summary>
        internal static FrameworkElement Row(string label, UIElement control, string hint = null)
        {
            var g = Ui.Cols(Ui.Px(LabelWidth), Ui.Star());
            var text = Ui.T(label, 13, "InkBrush");
            text.TextWrapping = TextWrapping.Wrap;
            text.VerticalAlignment = VerticalAlignment.Center;
            text.Margin = new Thickness(0, 0, LayoutRules.Gap, 0);
            g.Put(text);
            g.Put(control, 1);
            if (string.IsNullOrEmpty(hint)) { g.Margin = new Thickness(0, 0, 0, 10); return g; }
            var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            sp.Children.Add(g);
            sp.Children.Add(Hint(hint, LabelWidth));
            return sp;
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
        /// An on/off switch: the label on the left, the switch on the right, like every other row. Changes apply the
        /// moment you make them — this window has no "Save" button, and nothing waits.
        /// </summary>
        internal static FrameworkElement Toggle(string label, bool value, Action<bool> set, string hint = null) =>
            Row(label, Switch(value, set, label), hint);

        /// <summary>The switch on its own, for the rows that build their own layout (a feature's name and description).</summary>
        internal static Button Switch(bool value, Action<bool> set, string name)
        {
            var knob = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(9), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(3, 0, 0, 0) };
            var track = new Border { Width = 44, Height = 24, CornerRadius = new CornerRadius(12), Child = knob, HorizontalAlignment = HorizontalAlignment.Left, Cursor = Cursors.Hand };
            var btn = new Button { Style = Ui.S("NavButton"), Content = track, Padding = new Thickness(0), Cursor = Cursors.Hand, HorizontalAlignment = HorizontalAlignment.Left };
            System.Windows.Automation.AutomationProperties.SetName(btn, name ?? "");

            bool on = value;
            void Paint()
            {
                track.SetResourceReference(Border.BackgroundProperty, on ? "AccentBrush" : "TrackBrush");
                knob.SetResourceReference(Border.BackgroundProperty, on ? "OnAccentBrush" : "MutedBrush");
                knob.HorizontalAlignment = on ? HorizontalAlignment.Right : HorizontalAlignment.Left;
                knob.Margin = on ? new Thickness(0, 0, 3, 0) : new Thickness(3, 0, 0, 0);
            }
            Paint();
            btn.Click += (o, e) => { on = !on; Paint(); set(on); };
            return btn;
        }

        /// <summary>One choice out of a few: a drop-down in the theme's colours.</summary>
        internal static FrameworkElement Choice(string label, IReadOnlyList<(string Value, string Text)> options, string value, Action<string> set, string hint = null)
        {
            var box = ChoiceBox(options, value, set);
            box.HorizontalAlignment = HorizontalAlignment.Left;
            box.MinWidth = 280;
            return Row(label, box, hint);
        }

        /// <summary>The drop-down on its own, for the rows that build their own layout.</summary>
        internal static ComboBox ChoiceBox(IReadOnlyList<(string Value, string Text)> options, string value, Action<string> set)
        {
            var box = new ComboBox { Padding = new Thickness(8, 5, 8, 5) };
            foreach (var (v, t) in options) box.Items.Add(new ComboBoxItem { Tag = v, Content = t });
            box.SelectedItem = box.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == (value ?? ""))
                               ?? box.Items.Cast<ComboBoxItem>().FirstOrDefault();
            box.SelectionChanged += (o, e) => { if (box.SelectedItem is ComboBoxItem it) set((string)it.Tag); };
            return box;
        }

        /// <summary>A slider with its value written beside it.</summary>
        internal static FrameworkElement SliderRow(string label, double min, double max, double step, double value,
                                                   Func<double, string> format, Action<double> set, string hint = null)
        {
            var g = Ui.Cols(Ui.Star(), Ui.Px(70));
            var slider = new Slider
            {
                Minimum = min, Maximum = max, Value = Math.Clamp(value, min, max), TickFrequency = step,
                IsSnapToTickEnabled = step > 0, IsMoveToPointEnabled = true, VerticalAlignment = VerticalAlignment.Center,
            };
            var text = Ui.T(format(value), 12.5, "MutedBrush");
            text.HorizontalAlignment = HorizontalAlignment.Right;
            text.VerticalAlignment = VerticalAlignment.Center;
            slider.ValueChanged += (o, e) => { text.Text = format(e.NewValue); set(e.NewValue); };
            g.Put(slider);
            g.Put(text, 1);
            return Row(label, g, hint);
        }

        /// <summary>A text field in the theme's colours (the default WPF box is a white rectangle whatever the theme).</summary>
        internal static TextBox Field(string text, double width = double.NaN)
        {
            var box = new TextBox { Text = text ?? "", Padding = new Thickness(8, 5, 8, 5), BorderThickness = new Thickness(1) };
            if (!double.IsNaN(width)) box.Width = width;
            box.SetResourceReference(Control.BackgroundProperty, "TrackBrush");
            box.SetResourceReference(Control.ForegroundProperty, "InkBrush");
            box.SetResourceReference(Control.BorderBrushProperty, "BorderBrush");
            box.SetResourceReference(TextBoxBase.CaretBrushProperty, "InkBrush");
            return box;
        }

        /// <summary>A text field that saves shortly after you stop typing.</summary>
        internal static FrameworkElement TextRow(string label, string value, Action<string> set, Action<Action> soon, string hint = null)
        {
            var box = Field(value);
            box.TextChanged += (o, e) => { var v = box.Text; soon(() => set(v)); };
            return Row(label, box, hint);
        }

        /// <summary>A small square button with a glyph, for the ▲ ▼ ✕ of a list.</summary>
        internal static Button Mini(string glyph, Action click, string tip)
        {
            var b = Ui.IconBtn(glyph, click, tip, 26, 11);
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
            g.Margin = new Thickness(0, 3, 0, 3);
            return g;
        }
    }
}
