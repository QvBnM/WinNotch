using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace WinNotch.Features.WindowV2
{
    /// <summary>
    /// P52: the two pages that are shown here exactly as the classic window builds them — the settings (the real
    /// <see cref="SettingsWindow"/> content, through <c>TakeContent</c>) and the release notes — plus the themes page,
    /// which is the same logic (<see cref="Core.Ui.ThemeEdits"/>, used by both windows) in this window's wrapping.
    /// Nothing about them is written twice; this class only hosts them and takes them apart again.
    /// </summary>
    internal sealed class EmbeddedPages
    {
        private SettingsWindow _settings;
        private ScrollViewer _host;
        private ScrollBarVisibility _hostScroll = ScrollBarVisibility.Auto;

        /// <summary>
        /// The settings page, moved in as it is. It scrolls itself (so the Save row stays visible), which means the
        /// host's own scrolling is switched off and the page is bound to the host's viewport height.
        /// </summary>
        internal FrameworkElement Settings(AppSettings s, NotchWindow notch, ScrollViewer host, Action reload)
        {
            Detach();
            _settings = new SettingsWindow(s);
            _settings.Saved += () => notch?.ApplySettings();
            _settings.Reverted += reload;
            var box = new Border { Child = _settings.TakeContent(), MaxWidth = 660, HorizontalAlignment = HorizontalAlignment.Left };
            _host = host;
            _hostScroll = host.VerticalScrollBarVisibility;
            host.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            box.SetBinding(FrameworkElement.HeightProperty, new Binding("ViewportHeight") { Source = host });
            return box;
        }

        /// <summary>
        /// What this version brought: the same notes the classic window shows, read with the same code
        /// (<c>Updater.ParseNotes(Updater.OwnNotes())</c>). Only the wrapping is new — the theme's own tokens.
        /// </summary>
        internal FrameworkElement News()
        {
            var sp = new StackPanel { MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left };
            sp.Children.Add(Ui.T("Noutăți în WinNotch " + Services.Updater.Current, 20, "InkBrush", true));
            var intro = Ui.T(Services.Updater.Configured
                                 ? "Versiunile noi se instalează din notch („Actualizează”); fiecare îți arată aici ce a adus."
                                 : "Ce a adus versiunea pe care o folosești.", 13, "MutedBrush");
            intro.TextWrapping = TextWrapping.Wrap;
            intro.Margin = new Thickness(0, 4, 0, LayoutRules.Pad);
            sp.Children.Add(intro);

            var items = Services.Updater.ParseNotes(Services.Updater.OwnNotes());
            if (items.Count == 0) sp.Children.Add(Ui.T("Nicio notă pentru această versiune.", 13, "DimBrush"));
            foreach (var group in items.GroupBy(i => i.Kind))
            {
                var list = new StackPanel();
                list.Children.Add(Ui.Cap(group.Key.ToUpperInvariant()));
                foreach (var (_, text) in group)
                {
                    var row = Ui.Cols(Ui.Px(18), Ui.Star());
                    var dot = Ui.T("•", 13, "AccentBrush", true);
                    dot.VerticalAlignment = VerticalAlignment.Top;
                    row.Put(dot);
                    var line = Ui.T(text, 13.5, "InkBrush");
                    line.TextWrapping = TextWrapping.Wrap;
                    row.Put(line, 1);
                    row.Margin = new Thickness(0, 0, 0, 8);
                    list.Children.Add(row);
                }
                var card = Ui.Card(list, 18, 14, LayoutRules.CardRadius);
                card.Margin = new Thickness(0, 0, 0, LayoutRules.Gap);
                sp.Children.Add(card);
            }
            return sp;
        }

        /// <summary>
        /// „Teme și culori”: everything it changes goes through <see cref="Core.Ui.ThemeEdits"/> — the one piece of
        /// logic, shared with the classic window — and the colours on screen come from the palettes themselves.
        /// </summary>
        internal FrameworkElement Themes(AppSettings s, NotchWindow notch, Action reload, Action<Action> soon)
        {
            void Apply(bool redraw)
            {
                s.Save();
                notch?.ApplySettings();
                if (redraw) reload();
            }

            var sp = new StackPanel { MaxWidth = 980, HorizontalAlignment = HorizontalAlignment.Left };
            sp.Children.Add(Ui.T("Teme și culori", 20, "InkBrush", true));
            var hint = Ui.T("Se aplică pe loc: deschide notch-ul (Win+Alt+N) ca să vezi rezultatul.", 13, "MutedBrush");
            hint.TextWrapping = TextWrapping.Wrap;
            hint.Margin = new Thickness(0, 4, 0, LayoutRules.Pad);
            sp.Children.Add(hint);

            // mode
            var modes = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var (val, label) in new[] { ("dark", "Întunecat"), ("light", "Luminos"), ("auto", "Automat (ca Windows)") })
            {
                var v = val;
                bool on = s.ThemeMode == val;
                var chip = new Button
                {
                    Style = Ui.S("NavButton"), Content = Ui.T(label, 13, on ? "OnAccentBrush" : "InkBrush", on),
                    Padding = new Thickness(14, 8, 14, 8), Margin = new Thickness(0, 0, 8, 0), Cursor = Cursors.Hand,
                };
                if (on) chip.SetResourceReference(Control.BackgroundProperty, "AccentBrush");
                chip.Click += (o, e) => { s.ThemeMode = v; Apply(true); };
                modes.Children.Add(chip);
            }
            sp.Children.Add(Section("Mod", s.ThemeMode == "auto"
                ? "Acum Windows e pe " + (ThemeManager.WindowsLight() ? "luminos" : "întunecat") + "; notch-ul se schimbă singur când îl schimbi."
                : null, modes));

            sp.Children.Add(Section("Tema pentru modul întunecat", null, ThemeCards(s, false, Apply)));
            sp.Children.Add(Section("Tema pentru modul luminos", null, ThemeCards(s, true, Apply)));

            // the colours of the theme in use
            var cur = ThemeManager.Current(s);
            string themeName = cur.Name;
            var colors = new WrapPanel();
            foreach (var (key, label) in ThemeManager.Keys)
            {
                var k = key;
                string hex = cur.Colors.TryGetValue(key, out var h) ? h : "#000000";
                var swatch = new Border
                {
                    Width = 30, Height = 30, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1),
                    Cursor = Cursors.Hand, ToolTip = "Alege culoarea",
                    Background = new System.Windows.Media.SolidColorBrush(ThemeManager.Parse(hex, "#000000")),
                };
                swatch.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
                swatch.MouseLeftButtonUp += (o, e) =>
                {
                    var picked = Core.Ui.ThemeEdits.PickColor(hex);
                    if (picked == null) return;
                    Core.Ui.ThemeEdits.SetOverride(s, themeName, k, picked);
                    Apply(true);
                };
                var row = Ui.Cols(Ui.Auto, Ui.Px(10), Ui.Star(), Ui.Auto);
                row.Put(swatch);
                row.Put(Ui.V(0, Ui.T(label, 12.5, "InkBrush"), Ui.T(hex, 11.5, "DimBrush", false, true)), 2);
                if (Core.Ui.ThemeEdits.IsChanged(s, themeName, key))
                    row.Put(Ui.IconBtn("", () => { Core.Ui.ThemeEdits.ResetOverride(s, themeName, k); Apply(true); }, "Înapoi la culoarea temei", 26, 12), 3);
                colors.Children.Add(new Border { Width = 220, Padding = new Thickness(0, 0, 12, 10), Child = row });
            }
            var colorBody = new StackPanel();
            colorBody.Children.Add(colors);
            var resetAll = Ui.PillBtn("Toate înapoi la tema „" + themeName + "”", () => { Core.Ui.ThemeEdits.ResetAll(s, themeName); Apply(true); });
            resetAll.HorizontalAlignment = HorizontalAlignment.Left;
            colorBody.Children.Add(resetAll);
            sp.Children.Add(Section("Culorile temei „" + themeName + "”", "Click pe o culoare ca s-o schimbi. Schimbările se țin minte pentru tema asta.", colorBody));

            // shape
            var shape = new StackPanel();
            shape.Children.Add(SliderRow("Rotunjirea colțurilor", 12, 40, s.CornerRadius, v => Math.Round(v) + " px",
                                         v => { s.CornerRadius = (int)Math.Round(v); soon(() => Apply(false)); }));
            shape.Children.Add(SliderRow("Opacitatea fundalului", 60, 100, Math.Round(s.BgOpacity * 100), v => Math.Round(v) + "%",
                                         v => { s.BgOpacity = Math.Round(v) / 100.0; soon(() => Apply(false)); }));
            sp.Children.Add(Section("Formă", "Fundalul transparent lasă să se vadă ce e în spate; 100% e opac.", shape));

            // a theme of your own
            var nameBox = WidgetInspector.Field(themeName + " (a mea)", 220);
            nameBox.MaxLength = 30;
            nameBox.Margin = new Thickness(0, 0, 8, 0);
            var save = Ui.PillBtn("Salvează ca temă nouă", () =>
            {
                if (!Core.Ui.ThemeEdits.SaveAs(s, nameBox.Text)) { nameBox.SetResourceReference(Control.BorderBrushProperty, "HotBrush"); return; }
                Apply(true);
            }, true);
            sp.Children.Add(Section("Temă nouă", "Păstrează culorile de acum (cu modificările tale) ca temă separată, pe care o poți alege oricând.",
                                    Ui.H(0, nameBox, save)));
            return sp;
        }

        /// <summary>The cards of the ready-made and of your own themes: a preview drawn in the palette's own colours.</summary>
        private static FrameworkElement ThemeCards(AppSettings s, bool light, Action<bool> apply)
        {
            var wrap = new WrapPanel();
            string chosen = light ? s.ThemeLight : s.ThemeDark;
            foreach (var t in ThemeManager.All(s).Where(p => p.Light == light).ToList())
            {
                var theme = t;
                bool on = t.Name == chosen;
                System.Windows.Media.SolidColorBrush C(string k, string fb) =>
                    new System.Windows.Media.SolidColorBrush(ThemeManager.Parse(theme.Colors.TryGetValue(k, out var v) ? v : null, fb));
                var mini = new Grid { Width = 150, Height = 74 };
                var inner = Ui.Cols(Ui.Star(), Ui.Px(6), Ui.Star());
                var card1 = new Border { Background = C("Chip", "#1B1D21"), CornerRadius = new CornerRadius(6), Padding = new Thickness(6) };
                card1.Child = Ui.V(4,
                    new Border { Height = 5, Width = 40, CornerRadius = new CornerRadius(2), Background = C("Ink", "#FFFFFF"), HorizontalAlignment = HorizontalAlignment.Left },
                    new Border { Height = 4, Width = 28, CornerRadius = new CornerRadius(2), Background = C("Muted", "#999999"), HorizontalAlignment = HorizontalAlignment.Left },
                    new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = C("Accent", "#F5A524"), Margin = new Thickness(0, 6, 10, 0) });
                var card2 = new Border
                {
                    Background = C("Chip", "#1B1D21"), CornerRadius = new CornerRadius(6), Padding = new Thickness(6),
                    Child = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(9), Background = C("Accent", "#F5A524"), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom },
                };
                inner.Put(card1); inner.Put(card2, 2);
                mini.Children.Add(new Border { Background = C("Notch", "#000000"), CornerRadius = new CornerRadius(12), Padding = new Thickness(8), Child = inner });

                var pick = new Button
                {
                    Style = Ui.S("NavButton"), Content = Ui.V(6, mini, Ui.T(theme.Name, 12.5, "InkBrush", on)),
                    Padding = new Thickness(8), Margin = new Thickness(0, 0, 8, 8), Cursor = Cursors.Hand, BorderThickness = new Thickness(1.5),
                };
                System.Windows.Automation.AutomationProperties.SetName(pick, theme.Name);
                if (on) { pick.SetResourceReference(Control.BackgroundProperty, "ChipHoverBrush"); pick.SetResourceReference(Control.BorderBrushProperty, "AccentBrush"); }
                else pick.BorderBrush = System.Windows.Media.Brushes.Transparent;
                pick.Click += (o, e) => { Core.Ui.ThemeEdits.Choose(s, theme.Name, light); apply(true); };

                if (ThemeManager.Presets.Contains(t)) { wrap.Children.Add(pick); continue; }
                var holder = new Grid();
                holder.Children.Add(pick);
                var del = Ui.IconBtn("", () => { if (Core.Ui.ThemeEdits.Delete(s, theme)) apply(true); }, "Șterge tema", 26, 11, Ui.B("HotBrush"));
                del.HorizontalAlignment = HorizontalAlignment.Right;
                del.VerticalAlignment = VerticalAlignment.Top;
                del.Margin = new Thickness(0, 4, 12, 0);
                holder.Children.Add(del);
                wrap.Children.Add(holder);
            }
            return wrap;
        }

        /// <summary>A section of a page: title, one line of help, body — this window's card, the theme's own tokens.</summary>
        private static FrameworkElement Section(string title, string hint, UIElement body)
        {
            var sp = new StackPanel();
            var head = Ui.T(title, 15, "InkBrush", true);
            sp.Children.Add(head);
            if (hint != null)
            {
                var h = Ui.T(hint, 12, "MutedBrush");
                h.TextWrapping = TextWrapping.Wrap;
                h.Margin = new Thickness(0, 2, 0, 10);
                sp.Children.Add(h);
            }
            else head.Margin = new Thickness(0, 0, 0, 10);
            sp.Children.Add(body);
            var card = Ui.Card(sp, 18, 18, LayoutRules.CardRadius);
            card.Margin = new Thickness(0, 0, 0, LayoutRules.Gap + 2);
            return card;
        }

        /// <summary>Label, slider, value — the row the shape settings use.</summary>
        private static FrameworkElement SliderRow(string label, double min, double max, double val, Func<double, string> fmt, Action<double> changed)
        {
            var g = Ui.Cols(Ui.Px(190), Ui.Star(), Ui.Px(60));
            g.Put(Ui.T(label, 13, "InkBrush"));
            var slider = new Slider { Minimum = min, Maximum = max, Value = val, VerticalAlignment = VerticalAlignment.Center, IsMoveToPointEnabled = true };
            var text = Ui.T(fmt(val), 12.5, "MutedBrush");
            text.HorizontalAlignment = HorizontalAlignment.Right;
            slider.ValueChanged += (o, e) => { text.Text = fmt(e.NewValue); changed(e.NewValue); };
            g.Put(slider, 1);
            g.Put(text, 2);
            g.Margin = new Thickness(0, 0, 0, 10);
            return g;
        }

        /// <summary>P14 ("settings.*" actions): the hosted settings page, scrolled to one option and focused.</summary>
        internal void Reveal(string target) => _settings?.Reveal(target);

        /// <summary>Lets go of whatever page was hosted; calling it twice is harmless.</summary>
        internal void Detach()
        {
            _settings?.Detach();
            _settings = null;
            if (_host != null) { _host.VerticalScrollBarVisibility = _hostScroll; _host = null; }
        }
    }
}
