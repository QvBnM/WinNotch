using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WinNotch.Panes;
using WinNotch.Services;

namespace WinNotch.Widgets
{
    /// <summary>Muzică: 2×1 cover + title · 4×2 with lyrics, progress, buttons · 6×2 plus the audio sources beside it.</summary>
    internal sealed class MusicWidget : Widget
    {
        private readonly Border _art, _tint;
        private readonly TextBlock _title, _artist, _cur, _dur, _l1, _l2, _play;
        private readonly ScaleTransform _prog;
        private readonly StackPanel _sources;
        private StackPanel _lyricBox;
        private List<LyricLine> _lyrics = new List<LyricLine>();
        private string _lyricsKey = "", _artKey = "", _srcSig;

        public override Thickness CardPadding => Tall ? new Thickness(14, 12, 14, 12) : new Thickness(10, 8, 10, 8);

        public MusicWidget(NotchWindow w, WidgetSlot s) : base(w, s)
        {
            double cover = Tall ? (Cw >= 6 ? 104 : 96) : 44;
            _tint = new Border { Margin = new Thickness(-14, -12, -14, -12) };
            Children.Add(_tint);
            _art = new Border { Width = cover, Height = cover, CornerRadius = new CornerRadius(Tall ? 16 : 10), Background = Ui.B("TrackBrush"), VerticalAlignment = VerticalAlignment.Center,
                                Child = Ui.Icon(Ui.GMusic, Tall ? 26 : 16, Ui.B("DimBrush")) };
            if (Tall) _art.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 5, Direction = 270, Opacity = 0.35, Color = Colors.Black, RenderingBias = System.Windows.Media.Effects.RenderingBias.Performance };
            _title = Ui.T("Nimic nu rulează", Tall ? 15 : 12.5, "InkBrush", true);
            _artist = Ui.T("", Tall ? 12 : 11, "MutedBrush");
            _l1 = Ui.T("", 13.5, "InkBrush", true);
            _l2 = Ui.T("", 12, "DimBrush");
            _cur = Ui.T("0:00", 10.5, "MutedBrush", false, true);
            _dur = Ui.T("0:00", 10.5, "MutedBrush", false, true);
            _play = Ui.Icon(Ui.GPlay, 14, Ui.B("ChipBrush"));

            var main = Ui.Cols(Ui.Auto, Ui.Px(Tall ? 14 : 9), Ui.Star());
            main.Put(_art);
            if (!Tall)
            {
                main.Put(Ui.V(0, _title, _artist).Also(v => v.VerticalAlignment = VerticalAlignment.Center), 2);
                Children.Add(main);
                MediaChanged();
                return;
            }
            var bar = Ui.Bar(out _prog, Ui.B("InkBrush"));
            bar.Margin = new Thickness(8, 0, 8, 0);
            var progRow = Ui.Cols(Ui.Auto, Ui.Star(), Ui.Auto);
            progRow.Put(_cur); progRow.Put(bar, 1); progRow.Put(_dur, 2);
            var play = new Button { Style = Ui.S("PlayButton"), Width = 32, Height = 32, Content = _play, Margin = new Thickness(4, 0, 4, 0) };
            play.Click += (o, e) => W.Now.PlayPause();
            var ctl = Ui.H(0, Ui.IconBtn(Ui.GPrev, () => W.Now.Previous(), "Anterioara", 30, 15), play, Ui.IconBtn(Ui.GNext, () => W.Now.Next(), "Următoarea", 30, 15));
            // lyrics take room only when there are some; on 2 rows just the current line, so nothing gets cut
            _lyricBox = Ui.V(0, _l1, _l2);
            _lyricBox.Visibility = Visibility.Collapsed;
            if (Ch < 3) _l2.Visibility = Visibility.Collapsed;
            _title.TextTrimming = _artist.TextTrimming = _l1.TextTrimming = TextTrimming.CharacterEllipsis;
            var info = Ui.V(2, _title, _artist, _lyricBox, progRow, ctl);
            info.VerticalAlignment = VerticalAlignment.Center;
            main.Put(info, 2);

            if (Cw >= 6)
            {
                var outer = Ui.Cols(Ui.Star(), Ui.Px(14), Ui.Px(200));
                outer.Put(main);
                _sources = new StackPanel();
                outer.Put(Ui.V(6, Cap("SE AUDE ACUM"), _sources), 2);
                Children.Add(outer);
            }
            else Children.Add(main);
            MediaChanged();
        }

        public override void MediaChanged()
        {
            var m = W.Now.Info;
            if (!m.HasSession)
            {
                _title.Text = "Nimic nu rulează";
                _artist.Text = Tall ? "Pornește muzică sau un video" : "";
                _art.Background = Ui.B("TrackBrush");
                _art.Child.Visibility = Visibility.Visible;
                _tint.Background = null;
                _l1.Text = _l2.Text = "";
                if (_lyricBox != null) _lyricBox.Visibility = Visibility.Collapsed;
                _play.Text = Ui.GPlay;
                return;
            }
            _title.Text = m.Title;
            _artist.Text = string.IsNullOrEmpty(m.Artist) ? m.App : m.Artist + (string.IsNullOrEmpty(m.App) || m.App == m.Artist ? "" : " · " + m.App);
            _play.Text = m.Playing ? Ui.GPause : Ui.GPlay;
            _dur.Text = Ui.Fmt(m.Duration);
            string artKey = m.Id + m.Title + m.ArtKey + (m.Art != null);
            if (artKey != _artKey)
            {
                _artKey = artKey;
                if (m.Art != null)
                {
                    _art.Background = new ImageBrush(m.Art) { Stretch = Stretch.UniformToFill };
                    _art.Child.Visibility = Visibility.Collapsed;
                    if (Tall)
                    {
                        var c = HomePane.Dominant(m.Art);
                        _tint.Background = new RadialGradientBrush
                        {
                            Center = new Point(0, 0.4), GradientOrigin = new Point(0, 0.4), RadiusX = 1.2, RadiusY = 1.4,
                            GradientStops = new GradientStopCollection
                            {
                                new GradientStop(Color.FromArgb(0x4A, c.R, c.G, c.B), 0),
                                new GradientStop(Color.FromArgb(0x1C, c.R, c.G, c.B), 0.45),
                                new GradientStop(Color.FromArgb(0x00, c.R, c.G, c.B), 0.8)
                            }
                        };
                    }
                }
                else
                {
                    _art.Background = Ui.B("TrackBrush");
                    _art.Child.Visibility = Visibility.Visible;
                    _tint.Background = null;
                }
            }
            string lk = m.Artist + "|" + m.Title;
            if (Tall && lk != _lyricsKey)
            {
                _lyricsKey = lk;
                _lyrics = new List<LyricLine>();
                _l1.Text = _l2.Text = "";
                if (_lyricBox != null) _lyricBox.Visibility = Visibility.Collapsed;
                bool allowed = m.Tab == null || m.Tab.Site is "YouTube Music" or "Spotify Web" or "SoundCloud" or "Deezer";
                if (W.S.Lyrics && allowed) LoadLyrics(m, lk);
            }
            RefreshSources();
        }

        private async void LoadLyrics(MediaInfo m, string key)
        {
            var lines = await LyricsService.GetAsync(m.Artist, m.Title, m.Duration);
            if (key == _lyricsKey) _lyrics = lines;
        }

        public override void Refresh() => RefreshSources();

        private void RefreshSources()
        {
            if (_sources == null) return;
            var items = HomePane.BuildSources(W).Take(4).ToList();
            string sig = string.Join("|", items.Select(HomePane.Sig));
            if (sig == _srcSig) return;
            _srcSig = sig;
            _sources.Children.Clear();
            if (items.Count == 0) _sources.Children.Add(Ui.T("nimic altceva", 11.5, "DimBrush"));
            foreach (var it in items)
            {
                var g = Ui.Cols(Ui.Auto, Ui.Px(7), Ui.Star(), Ui.Auto);
                g.Put(Ui.AppBadge(it.Name, 18));
                g.Put(Ui.T(it.Name, 11.5), 2);
                g.Put(HomePane.Level(it, 10), 3);
                var row = new Border { Background = Ui.B("ChipHoverBrush"), CornerRadius = new CornerRadius(8), Padding = new Thickness(6, 4, 6, 4), Margin = new Thickness(0, 0, 0, 4), Child = g };
                if (it.Media != null) { var id = it.Media.Id; row.OnClick(() => W.Now.Select(id)); }
                _sources.Children.Add(row);
            }
        }

        public override void Fast()
        {
            if (!Tall) return;
            var m = W.Now.Info;
            if (!m.HasSession) return;
            var pos = m.LivePosition;
            _cur.Text = Ui.Fmt(pos);
            _prog.ScaleX = m.Duration.TotalSeconds > 0 ? Math.Clamp(pos.TotalSeconds / m.Duration.TotalSeconds, 0, 1) : 0;
            if (_lyrics.Count > 0 && W.S.Lyrics)
            {
                int i = LyricsService.IndexAt(_lyrics, pos);
                string a = i >= 0 && !string.IsNullOrWhiteSpace(_lyrics[i].Text) ? _lyrics[i].Text : "♪";
                string b = i + 1 < _lyrics.Count ? _lyrics[i + 1].Text : "";
                if (_l1.Text != a) { _l1.Text = a; _l2.Text = b; _lyricBox.Visibility = Visibility.Visible; }
            }
        }
    }

    /// <summary>Surse audio: what is making sound now, each with mute; click puts it in the music widget.</summary>
    internal sealed class SourcesWidget : Widget
    {
        private readonly Grid _row = new Grid();
        private string _sig;
        public override Thickness CardPadding => new Thickness(6);

        public SourcesWidget(NotchWindow w, WidgetSlot s) : base(w, s) { Children.Add(_row); Refresh(); }

        public override void MediaChanged() { _sig = null; Refresh(); }

        public override void Refresh()
        {
            var items = HomePane.BuildSources(W);
            string sig = string.Join("|", items.Select(HomePane.Sig)) + W.Now.Info.Id;
            if (sig == _sig) return;
            _sig = sig;
            _row.Children.Clear();
            _row.ColumnDefinitions.Clear();
            _row.RowDefinitions.Clear();
            if (items.Count == 0)
            {
                var t = Ui.T("Nu se aude nimic acum", 12, "MutedBrush");
                t.HorizontalAlignment = HorizontalAlignment.Center;
                _row.Children.Add(t);
                return;
            }
            bool vertical = Ch >= 2;
            int max = vertical ? Ch * 2 : Math.Max(1, Cw - 1);
            var shown = items.Take(max).ToList();
            for (int i = 0; i < shown.Count; i++)
            {
                if (vertical) _row.RowDefinitions.Add(new RowDefinition { Height = Ui.Star() });
                else _row.ColumnDefinitions.Add(new ColumnDefinition { Width = Ui.Star() });
                var chip = Chip(shown[i]);
                chip.Margin = new Thickness(vertical ? 0 : (i == 0 ? 0 : 3), vertical ? (i == 0 ? 0 : 3) : 0, 0, 0);
                if (vertical) Grid.SetRow(chip, i); else Grid.SetColumn(chip, i);
                _row.Children.Add(chip);
            }
        }

        private FrameworkElement Chip(SourceItem s)
        {
            bool selected = s.Media != null && s.Media.Id == W.Now.Info.Id;
            var g = Ui.Cols(Ui.Auto, Ui.Star(), Ui.Auto, Ui.Auto);
            var badge = g.Put(Ui.AppBadge(s.Name, 22));
            if (!(s.Playing || s.Sounding)) badge.Opacity = 0.5;
            string sub = !string.IsNullOrEmpty(s.Title) ? s.Title : s.Muted ? "fără sunet" : s.Playing ? "se aude" : "pe pauză";
            var texts = Ui.V(0, Ui.T(s.Name, 11.5, "InkBrush", true), Ui.T(sub, 11, "MutedBrush"));
            texts.Margin = new Thickness(7, 0, 5, 0);
            texts.VerticalAlignment = VerticalAlignment.Center;
            g.Put(texts, 1);
            g.Put(HomePane.Level(s, 11), 2);
            if (s.Audio != null || s.Tab != null)
                g.Put(Ui.IconBtn(s.Muted ? Ui.GMute : Ui.GVol, () => { HomePane.ToggleMute(W, s); _sig = null; Refresh(); },
                    s.Muted ? "Pornește sunetul" : "Oprește sunetul", 24, 11, s.Muted ? Ui.B("HotBrush") : Ui.B("InkBrush")), 3);
            var b = new Border { Background = Ui.B("ChipHoverBrush"), CornerRadius = new CornerRadius(10), Padding = new Thickness(7, 2, 2, 2), BorderThickness = new Thickness(1), Child = g };
            if (selected) b.SetResourceReference(Border.BorderBrushProperty, "AccentBrush"); else b.BorderBrush = Brushes.Transparent;
            if (s.Media != null) { var id = s.Media.Id; b.OnClick(() => W.Now.Select(id)); }
            return b;
        }
    }

    /// <summary>Volum: master volume with mute.</summary>
    internal sealed class VolumeWidget : Widget
    {
        private readonly Slider _slider;
        private readonly TextBlock _n, _icon;
        private bool _sync, _drag;

        public VolumeWidget(NotchWindow w, WidgetSlot s) : base(w, s)
        {
            _icon = Ui.Icon(Ui.GVol, 15);
            var mute = new Button { Style = Ui.S("IconButton"), Width = 28, Height = 28, Content = _icon, ToolTip = "Fără sunet" };
            mute.Click += (o, e) => { W.ToggleMasterMute(); Refresh(); };
            _slider = Ui.MiniSlider();
            _slider.ValueChanged += (o, e) => { if (!_sync) { W.SetMasterVolume((int)Math.Round(e.NewValue)); _n.Text = ((int)e.NewValue).ToString(); } };
            _slider.PreviewMouseLeftButtonDown += (o, e) => _drag = true;
            _slider.PreviewMouseLeftButtonUp += (o, e) => _drag = false;
            _n = Ui.T("0", 12, "MutedBrush", false, true);
            _n.Width = 26; _n.TextAlignment = TextAlignment.Right;
            var g = Ui.Cols(Ui.Auto, Ui.Star(), Ui.Auto);
            g.Put(mute); g.Put(_slider, 1); g.Put(_n, 2);
            g.Margin = new Thickness(0, 0, 0, 0);
            Children.Add(Ui.Rows(Ui.Auto, Ui.Star()).Also(r => { r.Put(Cap("VOLUM")); r.Put(g, 0, 1); }));
            Refresh();
        }

        public override void Refresh()
        {
            if (_drag) return;
            _sync = true;
            _slider.Value = W.Audio.Volume;
            _sync = false;
            bool m = W.Audio.Muted;
            _n.Text = m ? "—" : W.Audio.Volume.ToString();
            _icon.Text = m ? Ui.GMute : Ui.GVol;
        }
    }

    internal static class Fluent
    {
        /// <summary>Small helper to configure an element inline: x.Also(e => e.Margin = ...).</summary>
        public static T Also<T>(this T e, Action<T> a) { a(e); return e; }
    }
}
