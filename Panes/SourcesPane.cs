using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WinNotch.Panes
{
    /// <summary>Everything that makes sound right now (each browser tab separately with the extension), with play/pause, volume and mute.</summary>
    internal sealed class SourcesPane : Pane
    {
        public override double PanelHeight => 310;

        private readonly StackPanel _list = new StackPanel();
        private readonly TextBlock _cap;
        private readonly Button _extHint;
        private string _sig;
        private bool _dragging;

        public SourcesPane(NotchWindow w) : base(w)
        {
            var root = Ui.Rows(Ui.Auto, Ui.Px(8), Ui.Star());
            var header = Ui.Cols(Ui.Auto, Ui.Px(10), Ui.Star(), Ui.Auto);
            var back = new Button { Style = Ui.S("TileButton"), Width = 28, Height = 28, Content = Ui.Icon(Ui.GBack, 12), ToolTip = "Înapoi" };
            back.Click += (o, e) => W.ShowHome();
            header.Put(back);
            _cap = Ui.Cap("SE AUDE ACUM");
            header.Put(_cap, 2);
            _extHint = new Button { Style = Ui.S("LinkButton"), Content = "Vezi fiecare tab din browser separat  ›", Visibility = Visibility.Collapsed };
            _extHint.Click += (o, e) => ((App)Application.Current).OpenSettings();
            header.Put(_extHint, 3);
            root.Put(header);
            root.Put(new ScrollViewer { Style = Ui.S("SlimScroll"), Content = _list }, 0, 2);
            this.Put(Ui.Card(root, 12, 10));
        }

        public override void Shown() { _sig = null; Refresh(); }

        public override void Refresh()
        {
            if (_dragging) return;
            var items = HomePane.BuildSources(W);
            _cap.Text = items.Count == 1 ? "SE AUDE ACUM · 1 SURSĂ" : "SE AUDE ACUM · " + items.Count + " SURSE";

            // A browser is playing but its tabs can't be told apart: point to the extension.
            var browsers = new[] { "chrome", "msedge", "opera", "brave", "vivaldi" };
            bool needExt = W.S.BrowserTabs && items.Any(i => i.Tab == null && i.Audio != null && browsers.Contains(i.Audio.Process) && !W.Bridge.IsConnected(i.Audio.Process));
            _extHint.Visibility = needExt ? Visibility.Visible : Visibility.Collapsed;

            string sig = string.Join("|", items.Select(HomePane.Sig)) + "|" + W.Now.Info.Id;
            if (sig == _sig) return;
            _sig = sig;
            _list.Children.Clear();
            if (items.Count == 0)
            {
                _list.Children.Add(Ui.T("Nu se aude nimic acum. Aici apare tot ce redă sunet: aplicații, video, tab-uri.", 12.5, "MutedBrush"));
                return;
            }
            foreach (var s in items) _list.Children.Add(Row(s));
        }

        private FrameworkElement Row(SourceItem s)
        {
            bool selected = s.Media != null && s.Media.Id == W.Now.Info.Id;
            var g = Ui.Cols(Ui.Px(28), Ui.Px(10), Ui.Star(), Ui.Px(10), Ui.Px(22), Ui.Px(6), Ui.Px(28), Ui.Px(4), Ui.Px(28), Ui.Px(8), Ui.Px(140), Ui.Px(4), Ui.Px(28));
            var badge = g.Put(Ui.AppBadge(s.Name, 28));
            if (!(s.Playing || s.Sounding)) badge.Opacity = 0.5;

            var name = Ui.T(s.Name, 13, "InkBrush", true);
            var nameRow = new StackPanel { Orientation = Orientation.Horizontal };
            nameRow.Children.Add(name);
            if (s.Via != null) nameRow.Children.Add(new TextBlock { Text = "  ·  " + s.Via, FontSize = 11.5, Foreground = Ui.B("MutedBrush"), VerticalAlignment = VerticalAlignment.Center });
            string sub = !string.IsNullOrEmpty(s.Title) ? s.Title : s.Muted ? "fără sunet" : s.Playing ? "se aude" : "pe pauză";
            var texts = Ui.V(1, nameRow, Ui.T(sub, 11.5, "MutedBrush"));
            texts.VerticalAlignment = VerticalAlignment.Center;
            g.Put(texts, 2);
            g.Put(HomePane.Level(s, 14), 4);

            if (s.Tab != null || s.Media != null)
                g.Put(Ui.IconBtn(s.Playing ? Ui.GPause : Ui.GPlay, () => { HomePane.PlayPause(W, s); _sig = null; Refresh(); }, s.Playing ? "Pauză" : "Redă", 28, 13), 6);
            if (s.Tab != null)
                g.Put(Ui.IconBtn("", () => W.Bridge.Focus(s.Tab), "Deschide tab-ul", 28, 12), 8);

            if (s.Audio != null || s.Tab != null)
            {
                var vol = Ui.MiniSlider();
                vol.Value = Math.Round((s.Tab != null ? s.Tab.Volume : s.Audio.Volume) * 100);
                vol.ToolTip = s.Tab != null ? "Volumul acestui tab" : "Volumul aplicației";
                vol.ValueChanged += (o, e) =>
                {
                    if (s.Tab != null) W.Bridge.SetVolume(s.Tab, e.NewValue / 100);
                    else W.Sessions.SetVolume(s.Audio, (float)(e.NewValue / 100));
                };
                vol.PreviewMouseLeftButtonDown += (o, e) => _dragging = true;
                vol.PreviewMouseLeftButtonUp += (o, e) => { _dragging = false; _sig = null; };
                vol.LostMouseCapture += (o, e) => _dragging = false;
                if (s.Muted) vol.Opacity = 0.4;
                g.Put(vol, 10);
                g.Put(Ui.IconBtn(s.Muted ? Ui.GMute : Ui.GVol, () => { HomePane.ToggleMute(W, s); _sig = null; Refresh(); },
                    s.Muted ? "Pornește sunetul" : "Oprește sunetul", 28, 13, s.Muted ? Ui.B("HotBrush") : Ui.B("InkBrush")), 12);
            }

            var border = new Border
            {
                Background = Ui.B("ChipHoverBrush"), CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 4, 6, 4),
                Margin = new Thickness(0, 0, 0, 5), BorderThickness = new Thickness(1), Child = g
            };
            if (selected) border.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
            else border.BorderBrush = Brushes.Transparent;
            if (s.Media != null)
            {
                var id = s.Media.Id;
                texts.OnClick(() => { W.Now.Select(id); W.ShowHome(); });
                texts.ToolTip = "Pune-l în cardul mare";
            }
            else if (s.Tab != null)
            {
                texts.OnClick(() => W.Bridge.Focus(s.Tab));
                texts.ToolTip = "Deschide tab-ul";
            }
            return border;
        }
    }
}
