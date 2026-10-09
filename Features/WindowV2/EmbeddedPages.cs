using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace WinNotch.Features.WindowV2
{
    /// <summary>
    /// P52: the pages that used to open the classic window (settings, news, themes, pages), now shown inside the new
    /// window. Nothing is reimplemented here: each page is built by the same code the classic window uses — the
    /// settings page is the real <see cref="SettingsWindow"/> content (<c>TakeContent</c>), as the brief asks — and this
    /// class only hosts it and takes it apart again. One builder, two windows.
    /// </summary>
    internal sealed class EmbeddedPages
    {
        private SettingsWindow _settings;
        private ScrollViewer _host;
        private ScrollBarVisibility _hostScroll = ScrollBarVisibility.Auto;

        /// <summary>
        /// The settings page, moved in as it is. It scrolls itself (so the Save row stays visible), which means the
        /// host's own scrolling is switched off and the page is bound to the host's viewport height — exactly what the
        /// classic window does in <c>BuildSettings()</c>.
        /// </summary>
        internal FrameworkElement Settings(AppSettings s, NotchWindow notch, ScrollViewer host, Action reload)
        {
            Detach();
            _settings = new SettingsWindow(s);
            _settings.Saved += () => notch?.ApplySettings();
            _settings.Reverted += reload;
            var content = _settings.TakeContent();
            _host = host;
            _hostScroll = host.VerticalScrollBarVisibility;
            host.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            var box = new Border { Child = content, MaxWidth = 640, HorizontalAlignment = HorizontalAlignment.Left };
            box.SetBinding(FrameworkElement.HeightProperty, new Binding("ViewportHeight") { Source = host });
            return box;
        }

        /// <summary>
        /// What this version brought: the same notes the classic window shows, read with the same code
        /// (<c>Updater.ParseNotes(Updater.OwnNotes())</c>). Only the wrapping is new — the theme's own tokens, so the
        /// page reads the same on a light and on a dark theme.
        /// </summary>
        internal FrameworkElement News()
        {
            var sp = new StackPanel { MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left };
            sp.Children.Add(Ui.T("Noutăți în WinNotch " + Services.Updater.Current, 20, "InkBrush", true));
            var intro = Ui.T(Services.Updater.Configured
                                 ? "Versiunile noi se instalează din notch („Actualizează”); fiecare îți arată aici ce a adus."
                                 : "Ce a adus versiunea pe care o folosești.", 13, "MutedBrush");
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

        /// <summary>P14 ("settings.*" actions): the hosted settings page, scrolled to one option and focused.</summary>
        internal void Reveal(string target) => _settings?.Reveal(target);

        /// <summary>
        /// Lets go of whatever page was hosted: the settings page stops its own timer and the host gets its scrolling
        /// back. Called when the category changes and when the window closes; calling it twice is harmless.
        /// </summary>
        internal void Detach()
        {
            _settings?.Detach();
            _settings = null;
            if (_host != null) { _host.VerticalScrollBarVisibility = _hostScroll; _host = null; }
        }
    }
}
