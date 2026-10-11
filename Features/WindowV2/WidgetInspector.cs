using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using WinNotch.Panes;
using WinNotch.Widgets;

namespace WinNotch.Features.WindowV2
{
    /// <summary>
    /// P52: the right column of the Workspace — the selected widget's size and options. The classic window has the same
    /// panel, but drawn in its own fixed light palette and tied to that window; here it is the app's theme and nothing
    /// else. No rule is duplicated: the sizes come from <see cref="Gallery.SizePreviews"/>, the options from the
    /// widget's own <see cref="OptionDef"/> list, and every change goes through the page's <c>Commit</c>, exactly as
    /// before — only the controls are new.
    /// </summary>
    internal sealed class WidgetInspector : Border
    {
        private readonly Action<Action> _soon;      // runs a change shortly after the last keystroke
        private readonly Action _flush;
        private readonly Func<Window> _owner;       // for the file dialogs
        private readonly StackPanel _body = new StackPanel();
        private TextBlock _message;

        internal WidgetInspector(Action<Action> soon, Action flush, Func<Window> owner)
        {
            _soon = soon; _flush = flush; _owner = owner;
            Padding = new Thickness(LayoutRules.Pad, LayoutRules.Pad, LayoutRules.Pad, LayoutRules.Pad);
            BorderThickness = new Thickness(1, 0, 0, 0);
            SetResourceReference(BackgroundProperty, "ChipBrush");
            SetResourceReference(BorderBrushProperty, "BorderBrush");
            Child = new ScrollViewer { Style = Ui.S("SlimScroll"), Content = _body };
        }

        /// <summary>What the panel shows now; null for either argument means the empty state.</summary>
        internal void Show(WidgetPage page, WidgetSlot slot)
        {
            _body.Children.Clear();
            _message = null;
            var def = slot != null ? Catalog.Get(slot.Type) : null;

            if (page == null)
            {
                Empty("Pagină standard", "Paginile standard nu se editează. Fă-ți o copie ca să le poți schimba widget-urile.");
                return;
            }
            if (slot == null || def == null || !page.Page.Widgets.Contains(slot))
            {
                Empty("Niciun widget ales", "Click pe un widget din pagină ca să-i schimbi mărimea și opțiunile.");
                var used = Ui.T("Pe pagină: " + page.Page.Widgets.Count + " widget-uri, " +
                                Layout.UsedRows(page.Page.Widgets) + " din " + Layout.MaxRows + " rânduri folosite.", 12, "DimBrush");
                used.TextWrapping = TextWrapping.Wrap;
                used.Margin = new Thickness(0, LayoutRules.Gap, 0, 0);
                _body.Children.Add(used);
                return;
            }

            var icon = new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(LayoutRules.ChipRadius), Child = Ui.Icon(def.Glyph, 16) };
            icon.SetResourceReference(Border.BackgroundProperty, "TrackBrush");
            var head = Ui.H(10, icon, Ui.T(def.Name, 16, "InkBrush", true));
            head.Margin = new Thickness(0, 0, 0, 8);
            _body.Children.Add(head);
            var desc = Ui.T(def.Description, 12.5, "MutedBrush");
            desc.TextWrapping = TextWrapping.Wrap;
            desc.Margin = new Thickness(0, 0, 0, LayoutRules.Pad);
            _body.Children.Add(desc);

            // size
            _body.Children.Add(V2Controls.Eyebrow("MĂRIME"));
            var sizes = Gallery.SizePreviews(def, (slot.W, slot.H), size =>
            {
                _flush();
                if (page.Resize(slot, size)) Show(page, slot);
                else if (_message != null) _message.Text = "Nu încape la mărimea asta: fă loc pe pagină.";
            });
            _body.Children.Add(sizes);
            _message = Ui.T("", 12, "HotBrush");
            _message.TextWrapping = TextWrapping.Wrap;
            _body.Children.Add(_message);

            // options
            if (def.Options.Length > 0)
            {
                _body.Children.Add(V2Controls.Eyebrow("OPȚIUNI"));
                foreach (var o in def.Options) _body.Children.Add(OptionEditor(page, slot, o));
            }
            else
            {
                var none = Ui.T("Widget-ul acesta nu are opțiuni: arată singur ce trebuie, după mărime.", 12, "DimBrush");
                none.TextWrapping = TextWrapping.Wrap;
                none.Margin = new Thickness(0, LayoutRules.Gap, 0, 0);
                _body.Children.Add(none);
            }

            var remove = Ui.PillBtn("Scoate widget-ul", () =>
            {
                _flush();
                page.Page.Widgets.Remove(slot);
                page.Select(null, false);
                page.Commit();
                Show(page, null);
            });
            remove.HorizontalAlignment = HorizontalAlignment.Left;
            remove.Margin = new Thickness(0, LayoutRules.Pad, 0, 0);
            _body.Children.Add(remove);
        }

        private void Empty(string title, string line)
        {
            _body.Children.Add(Ui.T(title, 15, "InkBrush", true));
            var t = Ui.T(line, 12.5, "MutedBrush");
            t.TextWrapping = TextWrapping.Wrap;
            t.Margin = new Thickness(0, 6, 0, 0);
            _body.Children.Add(t);
        }

        /// <summary>One option of the widget, edited live (applied shortly after you stop typing).</summary>
        private FrameworkElement OptionEditor(WidgetPage page, WidgetSlot slot, OptionDef o)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 0, 0, LayoutRules.Gap) };
            string cur = slot.Opt(o.Key, o.Default);
            void Set(string v, bool now)
            {
                slot.Options[o.Key] = v;
                if (now) { _flush(); page.Commit(); }
                else _soon(() => page.Commit());
            }
            if (o.Kind != OptionKind.Bool)
            {
                var label = Ui.T(o.Label, 12, "MutedBrush", true);
                label.TextWrapping = TextWrapping.Wrap;
                label.Margin = new Thickness(0, 0, 0, 4);
                sp.Children.Add(label);
            }

            switch (o.Kind)
            {
                case OptionKind.Bool:
                {
                    var cb = new CheckBox { Content = Ui.T(o.Label, 12.5, "InkBrush"), IsChecked = cur == "1" };
                    cb.Checked += (s, e) => Set("1", true);
                    cb.Unchecked += (s, e) => Set("0", true);
                    sp.Children.Add(cb);
                    break;
                }
                case OptionKind.Choice:
                {
                    var combo = new ComboBox { Style = Ui.S("V2Combo") };
                    foreach (var (v, label) in o.Choices) combo.Items.Add(new ComboBoxItem { Content = label, Tag = v });
                    combo.SelectedItem = combo.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == cur)
                                         ?? combo.Items.Cast<ComboBoxItem>().FirstOrDefault();
                    combo.SelectionChanged += (s, e) => { if (combo.SelectedItem is ComboBoxItem it) Set((string)it.Tag, true); };
                    sp.Children.Add(combo);
                    break;
                }
                case OptionKind.Color:
                {
                    var row = new WrapPanel();
                    var box = Field(cur, 96);
                    foreach (var hex in new[] { "", "#5AA9FF", "#3DDC84", "#F5A524", "#FF5C5C", "#B48CFF", "#FF7EB6" })
                    {
                        var h = hex;
                        var sw = new Border
                        {
                            Width = 22, Height = 22, CornerRadius = new CornerRadius(11), BorderThickness = new Thickness(1),
                            Margin = new Thickness(0, 0, 5, 6), Cursor = Cursors.Hand, ToolTip = h == "" ? "Culoarea temei" : h,
                        };
                        sw.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
                        if (h == "") sw.SetResourceReference(Border.BackgroundProperty, "TrackBrush");
                        else sw.Background = new SolidColorBrush(ThemeManager.Parse(h, "#000000"));
                        sw.MouseLeftButtonUp += (s, e) => { box.Text = h; Set(h, true); };
                        row.Children.Add(sw);
                    }
                    var pick = Ui.PillBtn("Alta…", () =>
                    {
                        var c = Core.Ui.ThemeEdits.PickColor(slot.Opt(o.Key, ""));
                        if (c != null) { box.Text = c; Set(c, true); }
                    });
                    pick.Margin = new Thickness(0, 0, 6, 6);
                    box.TextChanged += (s, e) =>
                    {
                        var t = box.Text.Trim();
                        if (t.Length == 0 || Core.Ui.ThemeEdits.ValidHex(t)) Set(t, false);
                    };
                    row.Children.Add(pick);
                    row.Children.Add(box);
                    sp.Children.Add(row);
                    break;
                }
                case OptionKind.Files:
                {
                    bool multi = o.Key == "items";
                    var box = Field(cur, double.NaN);
                    box.AcceptsReturn = multi;
                    box.TextWrapping = multi ? TextWrapping.NoWrap : TextWrapping.Wrap;
                    if (multi) { box.Height = 120; box.VerticalScrollBarVisibility = ScrollBarVisibility.Auto; box.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto; }
                    box.FontFamily = Ui.Mono;
                    box.FontSize = 11.5;
                    box.TextChanged += (s, e) => Set(box.Text.Replace("\r", ""), false);
                    sp.Children.Add(box);

                    void Append(string line)
                    {
                        box.Text = multi
                            ? box.Text.TrimEnd('\r', '\n') + (box.Text.Trim().Length > 0 ? "\n" : "") + line
                            : line;
                        Set(box.Text.Replace("\r", ""), true);
                    }
                    var btns = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
                    btns.Children.Add(Pill(multi ? "+ Aplicație sau fișier…" : "Alege fișierul…", () =>
                    {
                        var dlg = new Microsoft.Win32.OpenFileDialog
                        {
                            Title = "Alege",
                            Filter = multi ? "Toate fișierele|*.*|Aplicații|*.exe;*.lnk"
                                           : "Programe și scripturi|*.exe;*.bat;*.cmd;*.ps1;*.lnk|Toate fișierele|*.*",
                        };
                        if (dlg.ShowDialog(_owner()) == true)
                            Append(multi ? System.IO.Path.GetFileNameWithoutExtension(dlg.FileName) + " = " + dlg.FileName : dlg.FileName);
                    }));
                    if (multi)
                    {
                        btns.Children.Add(Pill("+ Folder…", () =>
                        {
                            var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Alege folderul" };
                            if (dlg.ShowDialog(_owner()) == true)
                                Append(System.IO.Path.GetFileName(dlg.FolderName.TrimEnd('\\')) + " = " + dlg.FolderName);
                        }));
                        btns.Children.Add(Pill("+ Link web", () => Append("https://")));
                    }
                    sp.Children.Add(btns);
                    break;
                }
                default:     // Text, MultiLine, Number
                {
                    bool ml = o.Kind == OptionKind.MultiLine;
                    var box = Field(cur, o.Kind == OptionKind.Number ? 100 : double.NaN);
                    box.AcceptsReturn = ml;
                    box.TextWrapping = TextWrapping.Wrap;
                    if (ml) { box.Height = 90; box.VerticalScrollBarVisibility = ScrollBarVisibility.Auto; }
                    box.HorizontalAlignment = o.Kind == OptionKind.Number ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
                    box.TextChanged += (s, e) =>
                    {
                        var v = box.Text.Replace("\r", "");
                        if (o.Kind == OptionKind.Number && v.Trim().Length > 0 &&
                            !double.TryParse(v.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out _)) return;
                        Set(o.Kind == OptionKind.Number ? v.Trim().Replace(',', '.') : v, false);
                    };
                    sp.Children.Add(box);
                    break;
                }
            }
            if (!string.IsNullOrEmpty(o.Hint))
            {
                var hint = Ui.T(o.Hint, 11.5, "DimBrush");
                hint.TextWrapping = TextWrapping.Wrap;
                hint.Margin = new Thickness(0, 4, 0, 0);
                sp.Children.Add(hint);
            }
            return sp;
        }

        /// <summary>A text field in the app's theme (the default WPF box is a white rectangle whatever the theme).</summary>
        internal static TextBox Field(string text, double width)
        {
            var box = new TextBox { Text = text ?? "", Padding = new Thickness(8, 5, 8, 5), BorderThickness = new Thickness(1) };
            if (!double.IsNaN(width)) box.Width = width;
            box.SetResourceReference(Control.BackgroundProperty, "TrackBrush");
            box.SetResourceReference(Control.ForegroundProperty, "InkBrush");
            box.SetResourceReference(Control.BorderBrushProperty, "BorderBrush");
            box.SetResourceReference(TextBoxBase.CaretBrushProperty, "InkBrush");
            return box;
        }

        private static Button Pill(string text, Action click)
        {
            var b = Ui.PillBtn(text, click);
            b.Margin = new Thickness(0, 0, 6, 6);
            return b;
        }
    }
}
