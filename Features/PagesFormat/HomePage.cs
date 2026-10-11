using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WinNotch.Panes;
using WinNotch.Services;

namespace WinNotch.Features.PagesFormat
{
    /// <summary>
    /// P75, „Acasă”, after `docs/design/02-pagina-acasa.png`: the music fills the left column in the one card in the
    /// whole app that takes a colour of its own — the cover's, faded into the card — with the lyrics in amber under the
    /// title. On the right, the clock, the next thing in the calendar and the weather, one under the other.
    ///
    /// The old <see cref="HomePane"/> stays exactly as it is and is what the notch shows while the switch is off.
    /// </summary>
    internal sealed class HomePage : Pane
    {
        public override double PanelHeight => PageFormat.HomeHeight;

        private readonly Border _art, _tint, _play;
        private readonly TextBlock _artGlyph, _playGlyph, _title, _artist, _cur, _dur, _clock, _date,
                                   _evWhen, _evTitle, _wTemp, _wGlyph, _wWord, _wRange;
        private readonly StackPanel _hours;
        private readonly ScaleTransform _prog;
        private readonly Grid _evRow;
        private string _artKey = "", _hoursSig = "";

        public HomePage(NotchWindow w) : base(w)
        {
            var cols = Deck.Columns(PageFormat.HomeSplit, out var left, out var right);
            Children.Add(cols);

            // ---------- left: the music ----------
            var card = new Grid();
            var surface = new Border { CornerRadius = new CornerRadius(PageFormat.CardRadius) };
            surface.SetResourceReference(Border.BackgroundProperty, "ChipBrush");
            _tint = new Border { CornerRadius = new CornerRadius(PageFormat.CardRadius) };
            card.Children.Add(surface);
            card.Children.Add(_tint);

            _artGlyph = Ui.Icon(Ui.GMusic, 18);
            _art = new Border
            {
                Width = 46, Height = 46, CornerRadius = new CornerRadius(11),
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                Child = _artGlyph,
            };
            _art.SetResourceReference(Border.BackgroundProperty, "TrackBrush");

            _title = Deck.T(Ink.Title, "Nimic nu rulează");
            _title.FontSize = 13.5;
            _artist = Deck.T(Ink.Sub, "Pornește muzică sau un video în orice aplicație");

            var head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(57) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.Children.Add(_art);
            var titles = Deck.Stack(2, _title, _artist);
            titles.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(titles, 1);
            head.Children.Add(titles);

            _lyric1 = Deck.T(Ink.Title, "", PageFormat.Reading.Live);
            _lyric1.FontSize = 12.5;
            _lyric2 = Deck.T(Ink.Sub, "");
            Deck.Paint(_lyric2, "DimBrush");
            var lyrics = Deck.Stack(2, _lyric1, _lyric2);
            lyrics.Margin = new Thickness(0, 10, 0, 0);

            _cur = Deck.T(Ink.Note, "0:00");
            _dur = Deck.T(Ink.Note, "0:00");
            Deck.Paint(_cur, "MutedBrush");
            var line = Deck.Bar(out _prog);
            line.Margin = new Thickness(9, 0, 9, 0);
            var progRow = new Grid { Margin = new Thickness(0, 12, 0, 0) };
            progRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            progRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            progRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            progRow.Children.Add(_cur);
            Grid.SetColumn(line, 1);
            progRow.Children.Add(line);
            Grid.SetColumn(_dur, 2);
            progRow.Children.Add(_dur);

            // The one filled button on the page: what you press most often should be the thing you see first.
            _playGlyph = Ui.Icon(Ui.GPlay, 12);
            _playGlyph.SetResourceReference(TextBlock.ForegroundProperty, "OnAccentBrush");
            _play = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Child = _playGlyph, Cursor = Cursors.Hand };
            _play.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
            _play.MouseLeftButtonUp += (o, e) => { W.Now.PlayPause(); e.Handled = true; };
            var transport = Deck.Row(9,
                Tap(Ui.Icon(Ui.GPrev, 14), () => W.Now.Previous()),
                _play,
                Tap(Ui.Icon(Ui.GNext, 14), () => W.Now.Next()));
            transport.Margin = new Thickness(0, 10, 0, 0);

            var inner = Deck.Stack(0, head, lyrics, progRow, transport);
            inner.Margin = new Thickness(PageFormat.CardPadH, PageFormat.CardPadV, PageFormat.CardPadH, PageFormat.CardPadV);
            card.Children.Add(inner);

            left.Children.Add(card);

            // ---------- right: the clock, the calendar, the weather ----------
            _clock = Deck.T(Ink.Big, "--:--");
            _clock.FontSize = 22;
            _clock.FontWeight = FontWeights.Bold;
            _date = Deck.T(Ink.Sub, "");
            var clockCard = Deck.Card(Deck.Stack(3, _clock, _date));

            _evWhen = Deck.T(Ink.Note, "");
            Deck.Paint(_evWhen, "InfoBrush");
            _evTitle = Deck.T(Ink.Title, "");
            _evTitle.FontSize = 12;
            var rule = new Border { Width = 2, CornerRadius = new CornerRadius(1), Margin = new Thickness(0, 1, 11, 1) };
            rule.SetResourceReference(Border.BackgroundProperty, "InfoBrush");
            _evRow = new Grid();
            _evRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _evRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _evRow.Children.Add(rule);
            var ev = Deck.Stack(2, _evWhen, _evTitle);
            Grid.SetColumn(ev, 1);
            _evRow.Children.Add(ev);
            var evCard = Deck.Card(_evRow);
            evCard.Margin = new Thickness(0, PageFormat.CardGap, 0, 0);

            _wTemp = Deck.T(Ink.Big, "—");
            _wGlyph = Ui.Icon(Ui.GCloud, 13);
            _wWord = Deck.T(Ink.Sub, "");
            _wRange = Deck.T(Ink.Note, "");
            var wHead = new Grid();
            wHead.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            wHead.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            wHead.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            wHead.Children.Add(_wTemp);
            var wWhat = Deck.Row(6, _wGlyph, _wWord);
            wWhat.Margin = new Thickness(9, 3, 0, 0);
            Grid.SetColumn(wWhat, 1);
            wHead.Children.Add(wWhat);
            Grid.SetColumn(_wRange, 2);
            wHead.Children.Add(_wRange);
            _hours = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            var wCard = Deck.Card(Deck.Stack(0, wHead, _hours));
            wCard.Margin = new Thickness(0, PageFormat.CardGap, 0, 0);

            var rightStack = new Grid();
            rightStack.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            rightStack.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            rightStack.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            rightStack.Children.Add(clockCard);
            Grid.SetRow(evCard, 1);
            rightStack.Children.Add(evCard);
            Grid.SetRow(wCard, 2);
            rightStack.Children.Add(wCard);
            right.Children.Add(rightStack);
        }

        private readonly TextBlock _lyric1, _lyric2;
        private System.Collections.Generic.List<LyricLine> _lyrics = new System.Collections.Generic.List<LyricLine>();
        private string _lyricsKey = "";

        /// <summary>When the next thing in the calendar is. The event carries no end time, so neither does this.</summary>
        private static string When(CalendarEvent ev)
        {
            var d = ev.Start.Date;
            string day = d == DateTime.Today ? "azi" : d == DateTime.Today.AddDays(1) ? "mâine" : ev.Start.ToString("dddd", NotchWindow.Ro);
            return ev.AllDay ? day + ", toată ziua" : day + ", " + ev.Start.ToString("HH:mm");
        }

        private static FrameworkElement Tap(TextBlock glyph, Action click)
        {
            glyph.Cursor = Cursors.Hand;
            glyph.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            glyph.MouseLeftButtonUp += (o, e) => { click(); e.Handled = true; };
            return glyph;
        }

        public override void Shown()
        {
            _artKey = "";
            _hoursSig = "";
            OnMediaChanged();
            Refresh();
        }

        public override void Refresh() => NotchWindow.GuardFormat(RefreshCore);

        private void RefreshCore()
        {
            var now = DateTime.Now;
            _clock.Text = now.ToString("HH:mm");
            _date.Text = NotchWindow.Ro.TextInfo.ToTitleCase(now.ToString("dddd, d MMMM", NotchWindow.Ro));

            var ev = W.Events.FirstOrDefault();
            if (ev != null)
            {
                _evRow.Visibility = Visibility.Visible;
                _evWhen.Text = When(ev);
                _evTitle.Text = ev.Title;
            }
            else if (string.IsNullOrWhiteSpace(W.S.CalendarIcs))
            {
                _evRow.Visibility = Visibility.Visible;
                _evWhen.Text = "Setări › Calendar";
                _evTitle.Text = "Leagă calendarul";
            }
            else _evRow.Visibility = Visibility.Collapsed;

            var wx = W.Weather;
            _wTemp.Text = wx.Ok ? Math.Round(wx.Temp) + "°" : "—";
            // Starea vremii o spune deja glifa (aceeași pe care o folosește pastila); aici, lângă ea, stă locul —
            // n-are rost un al doilea tabel de coduri meteo doar ca să scriem cuvântul.
            _wGlyph.Text = W.WeatherGlyph();
            _wGlyph.Foreground = W.WeatherBrush();
            _wWord.Text = wx.Ok ? W.S.City : "fără date";
            _wRange.Text = wx.Ok ? Math.Round(wx.Min) + "° / " + Math.Round(wx.Max) + "°" : "";
            string sig = string.Join(",", wx.Hours.Select(h => h.Item1.ToString("HH") + Math.Round(h.Item2)));
            if (sig != _hoursSig)
            {
                _hoursSig = sig;
                _hours.Children.Clear();
                foreach (var (t, temp) in wx.Hours)
                {
                    var hour = Deck.T(Ink.Unit, t.ToString("HH"));
                    var deg = Deck.T(Ink.Note, Math.Round(temp) + "°");
                    Deck.Paint(deg, "MutedBrush");
                    var box = Deck.Stack(2, hour, deg);
                    box.Width = 54;
                    hour.HorizontalAlignment = HorizontalAlignment.Left;
                    deg.HorizontalAlignment = HorizontalAlignment.Left;
                    _hours.Children.Add(box);
                }
            }
        }

        public override void Fast() => NotchWindow.GuardFormat(FastCore);

        private void FastCore()
        {
            var m = W.Now.Info;
            if (!m.HasSession || m.Duration <= TimeSpan.Zero) return;
            _prog.ScaleX = Math.Clamp(m.LivePosition.TotalSeconds / m.Duration.TotalSeconds, 0, 1);
            _cur.Text = Ui.Fmt(m.LivePosition);
            if (_lyrics.Count > 0 && W.S.Lyrics)
            {
                int i = LyricsService.IndexAt(_lyrics, m.LivePosition);
                string a = i >= 0 ? _lyrics[i].Text : "♪";
                string b = i + 1 < _lyrics.Count ? _lyrics[i + 1].Text : "";
                if (string.IsNullOrWhiteSpace(a)) a = "♪";
                if (_lyric1.Text != a) { _lyric1.Text = a; _lyric2.Text = b; }
            }
        }

        /// <summary>The same service the old page reads, so there is one lyrics reader and not two.</summary>
        private async void LoadLyrics(MediaInfo m)
        {
            string key = _lyricsKey;
            try
            {
                var lines = await LyricsService.GetAsync(m.Artist, m.Title, m.Duration);
                if (key == _lyricsKey) _lyrics = lines;
            }
            catch (Exception ex) { App.Log("Versuri: " + ex.Message); }
        }

        /// <summary>
        /// The track changed: the cover, the title, and the tint the whole card takes from it. The colour is read with
        /// the same routine the old page uses (<see cref="HomePane.Dominant"/>) — one cover reader, not two.
        /// </summary>
        public void OnMediaChanged() => NotchWindow.GuardFormat(OnMediaChangedCore);

        private void OnMediaChangedCore()
        {
            var m = W.Now.Info;
            if (!m.HasSession)
            {
                _title.Text = "Nimic nu rulează";
                _artist.Text = "Pornește muzică sau un video în orice aplicație";
                _art.SetResourceReference(Border.BackgroundProperty, "TrackBrush");
                _artGlyph.Visibility = Visibility.Visible;
                _tint.Background = null;
                _lyric1.Text = _lyric2.Text = "";
                _playGlyph.Text = Ui.GPlay;
                _prog.ScaleX = 0;
                _cur.Text = _dur.Text = "0:00";
                _artKey = "";
                return;
            }
            _title.Text = m.Title;
            _artist.Text = string.IsNullOrEmpty(m.Artist) ? m.App : m.Artist + (string.IsNullOrEmpty(m.App) ? "" : " · " + m.App);
            _playGlyph.Text = m.Playing ? Ui.GPause : Ui.GPlay;
            _dur.Text = Ui.Fmt(m.Duration);

            string lk = m.Artist + "|" + m.Title;
            if (lk != _lyricsKey)
            {
                _lyricsKey = lk;
                _lyrics = new System.Collections.Generic.List<LyricLine>();
                _lyric1.Text = _lyric2.Text = "";
                if (W.S.Lyrics) LoadLyrics(m);
            }

            string key = m.Id + m.Title + m.ArtKey + (m.Art != null);
            if (key == _artKey) return;
            _artKey = key;
            if (m.Art != null)
            {
                _art.Background = new ImageBrush(m.Art) { Stretch = Stretch.UniformToFill };
                _artGlyph.Visibility = Visibility.Collapsed;
                var c = HomePane.Dominant(m.Art);
                _tint.Background = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0), EndPoint = new Point(1, 0),
                    GradientStops = new GradientStopCollection
                    {
                        new GradientStop(Color.FromArgb(0x4D, c.R, c.G, c.B), 0),
                        new GradientStop(Color.FromArgb(0x1A, c.R, c.G, c.B), 0.32),
                        new GradientStop(Color.FromArgb(0x00, c.R, c.G, c.B), 0.62),
                    },
                };
            }
            else
            {
                _art.SetResourceReference(Border.BackgroundProperty, "TrackBrush");
                _artGlyph.Visibility = Visibility.Visible;
                _tint.Background = null;
            }
        }
    }
}
