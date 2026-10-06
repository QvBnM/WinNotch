using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using WinNotch.Services;

namespace WinNotch.Panes
{
    /// <summary>
    /// One thing that makes sound: an app's audio stream (level, volume, mute) and/or its media session (title, play/pause),
    /// or a single browser tab when the WinNotch extension is installed.
    /// </summary>
    internal sealed class SourceItem
    {
        public string Name;
        public string Title;
        public string Via;               // "Chrome" for a tab, so two YouTube tabs in different browsers can be told apart
        public AudioSource Audio;
        public MediaInfo Media;
        public BrowserTab Tab;

        /// <summary>Heard now (with a few seconds' hold, so quiet moments don't make it blink).</summary>
        public bool Sounding => Tab != null ? Tab.IsPlaying && !Tab.Muted : Audio != null ? !Audio.Muted && Audio.Active : Media != null && Media.Playing;
        public bool Playing => Tab != null ? Tab.IsPlaying : Media != null && Media.Playing;
        public bool Muted => Tab != null ? Tab.Muted : Audio != null && Audio.Muted;
        public float Peak => Sounding ? Math.Max(0.15f, Audio?.Peak ?? 0.3f) : 0;

        /// <summary>Items paused from WinNotch stay listed for a while, so they can be resumed.</summary>
        public static readonly Dictionary<string, DateTime> PausedAt = new Dictionary<string, DateTime>();
        public string Key => Tab != null ? "tab:" + Tab.Browser + ":" + Tab.Id : Audio != null ? "app:" + Audio.Process : "media:" + (Media?.Id ?? Name);

        /// <summary>When each source first appeared: the list keeps this order, so nothing jumps around.</summary>
        private static readonly Dictionary<string, (long Order, DateTime Seen)> Seen = new Dictionary<string, (long, DateTime)>();
        private static long _seq;
        internal static long OrderOf(string key)
        {
            foreach (var k in PausedAt.Where(kv => (DateTime.Now - kv.Value).TotalMinutes > 3).Select(kv => kv.Key).ToList()) PausedAt.Remove(k);
            var now = DateTime.Now;
            if (!Seen.TryGetValue(key, out var e)) e = (++_seq, now);
            Seen[key] = (e.Order, now);
            foreach (var old in Seen.Where(kv => (now - kv.Value.Seen).TotalSeconds > 20).Select(kv => kv.Key).ToList()) Seen.Remove(old);
            return e.Order;
        }
        public bool RecentlyPaused => PausedAt.TryGetValue(Key, out var t) && (DateTime.Now - t).TotalMinutes < 3;

        /// <summary>Only what is making sound now (or a moment ago, or was muted/paused from here).</summary>
        public bool Visible =>
            Tab != null ? Tab.IsPlaying || RecentlyPaused :
            Audio != null ? Audio.Active || RecentlyPaused :
            Media != null && (Media.Playing || RecentlyPaused);
    }

    /// <summary>
    /// Acasă: the song with live lyrics and a real visualizer, every app that's playing sound, time with what's next, weather.
    /// </summary>
    internal sealed class HomePane : Pane
    {
        public override double PanelHeight => 310;

        // media card
        private readonly Border _tint;
        private readonly Border _art;
        private readonly TextBlock _artGlyph;
        private readonly TextBlock _title, _artist, _lyric1, _lyric2, _cur, _dur, _volN;
        private readonly ScaleTransform _prog;
        private readonly TextBlock _playGlyph, _volGlyph;
        /// <summary>P30 hook (Features/AudioSwitch): the "Ieșire audio" button beside the volume goes here; empty with its switch off.</summary>
        internal readonly Border OutputSlot = new Border { VerticalAlignment = VerticalAlignment.Center };
        private readonly Slider _vol;
        private readonly Button _lyricsBtn;
        private readonly Rectangle[] _bars = new Rectangle[Visualizer.BandCount];
        private readonly ScaleTransform[] _barScale = new ScaleTransform[Visualizer.BandCount];
        private bool _syncing;

        // sources row
        private readonly Grid _sourcesRow;
        private string _sourcesSig;

        // right column
        private readonly TextBlock _clock, _date, _eventTitle, _eventWhen;
        private readonly FrameworkElement _eventRow;
        private readonly TextBlock _wGlyph, _wTemp, _wPlace;
        private readonly UniformGrid _hours;
        private string _hoursSig;

        private List<LyricLine> _lyrics = new List<LyricLine>();
        private string _lyricsKey = "", _artKey = "";

        public HomePane(NotchWindow w) : base(w)
        {
            ColumnDefinitions.Add(new ColumnDefinition { Width = Ui.Star() });
            ColumnDefinitions.Add(new ColumnDefinition { Width = Ui.Px(10) });
            ColumnDefinitions.Add(new ColumnDefinition { Width = Ui.Px(176) });

            // ---------------- left: media card + sources ----------------
            var left = Ui.Rows(Ui.Star(), Ui.Px(8), Ui.Px(44));
            this.Put(left);

            var card = new Grid { ClipToBounds = true };
            var cardBorder = new Border { Background = Ui.B("ChipBrush"), CornerRadius = new CornerRadius(16), Child = card, ClipToBounds = true };
            left.Put(cardBorder);

            _tint = new Border { CornerRadius = new CornerRadius(16) };
            card.Children.Add(_tint);

            var viz = new UniformGrid { Rows = 1, Columns = Visualizer.BandCount, Height = 46, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(10, 0, 10, 0), Opacity = 0.22 };
            for (int i = 0; i < Visualizer.BandCount; i++)
            {
                _barScale[i] = new ScaleTransform(1, 0.05);
                _bars[i] = new Rectangle { Margin = new Thickness(1.5, 0, 1.5, 0), RadiusX = 1.5, RadiusY = 1.5, RenderTransformOrigin = new Point(0.5, 1), RenderTransform = _barScale[i] };
                _bars[i].SetResourceReference(Shape.FillProperty, "AccentBrush");
                viz.Children.Add(_bars[i]);
            }
            card.Children.Add(viz);

            var content = Ui.Cols(Ui.Px(108), Ui.Px(16), Ui.Star());
            content.Margin = new Thickness(16, 12, 16, 12);
            content.VerticalAlignment = VerticalAlignment.Center;
            card.Children.Add(content);

            _artGlyph = Ui.Icon(Ui.GMusic, 30, Ui.B("DimBrush"));
            _art = new Border
            {
                Width = 108, Height = 108, CornerRadius = new CornerRadius(14), Background = Ui.B("TrackBrush"), Child = _artGlyph,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 24, ShadowDepth = 8, Direction = 270, Opacity = 0.5, Color = Colors.Black }
            };
            content.Put(_art);

            _title = Ui.T("Nimic nu rulează", 16, "InkBrush", true);
            _artist = Ui.T("Pornește muzică sau un video în orice aplicație", 12, "MutedBrush");
            _lyric1 = Ui.T("", 14, "InkBrush", true);
            _lyric1.Foreground = Ui.B("InkBrush");
            _lyric2 = Ui.T("", 12.5, "InkBrush");
            _lyric2.Foreground = Ui.B("DimBrush");
            var lyrics = Ui.V(1, _lyric1, _lyric2);
            lyrics.Margin = new Thickness(0, 3, 0, 1);

            var progRow = Ui.Cols(Ui.Auto, Ui.Star(), Ui.Auto);
            _cur = Ui.T("0:00", 10, "MutedBrush", false, true);
            _dur = Ui.T("0:00", 10, "MutedBrush", false, true);
            var bar = Ui.Bar(out _prog, Ui.B("InkBrush"));
            bar.Margin = new Thickness(8, 0, 8, 0);
            progRow.Put(_cur); progRow.Put(bar, 1); progRow.Put(_dur, 2);

            _playGlyph = Ui.Icon(Ui.GPlay, 15, Ui.B("ChipBrush"));
            var play = new Button { Style = Ui.S("PlayButton"), Width = 38, Height = 38, Content = _playGlyph, Margin = new Thickness(6, 0, 6, 0) };
            play.Click += (o, e) => W.Now.PlayPause();
            _lyricsBtn = Ui.PillBtn("Versuri", ToggleLyrics);
            _lyricsBtn.Margin = new Thickness(6, 0, 0, 0);
            _volGlyph = Ui.Icon(Ui.GVol, 13, Ui.B("MutedBrush"));
            var muteBtn = new Button { Style = Ui.S("IconButton"), Width = 26, Height = 26, Content = _volGlyph, ToolTip = "Fără sunet", Margin = new Thickness(8, 0, 2, 0) };
            muteBtn.Click += (o, e) => W.ToggleMasterMute();
            _vol = Ui.MiniSlider();
            _vol.ValueChanged += (o, e) => { if (!_syncing) { W.SetMasterVolume((int)Math.Round(e.NewValue)); _volN.Text = ((int)e.NewValue).ToString(); } };
            _volN = Ui.T("0", 11, "MutedBrush", false, true);
            _volN.Width = 22; _volN.TextAlignment = TextAlignment.Right;
            var ctl = Ui.Cols(Ui.Auto, Ui.Auto, Ui.Auto, Ui.Auto, Ui.Auto, Ui.Star(), Ui.Auto, Ui.Auto);
            ctl.Put(Ui.IconBtn(Ui.GPrev, () => W.Now.Previous(), "Anterioara", 32, 17));
            ctl.Put(play, 1);
            ctl.Put(Ui.IconBtn(Ui.GNext, () => W.Now.Next(), "Următoarea", 32, 17), 2);
            ctl.Put(_lyricsBtn, 3);
            ctl.Put(muteBtn, 4);
            ctl.Put(_vol, 5);
            ctl.Put(_volN, 6);
            ctl.Put(OutputSlot, 7);

            var info = Ui.V(4, _title, _artist, lyrics, progRow, ctl);
            info.VerticalAlignment = VerticalAlignment.Center;
            content.Put(info, 2);

            _sourcesRow = new Grid();
            left.Put(_sourcesRow, 0, 2);

            // ---------------- right: time + next event, weather ----------------
            var right = Ui.Rows(Ui.Auto, Ui.Px(10), Ui.Star());
            this.Put(right, 2);

            _clock = Ui.T("--:--", 28, "InkBrush", true);
            _date = Ui.T("", 11.5, "MutedBrush");
            _eventTitle = Ui.T("", 11.5, "InkBrush", true);
            _eventWhen = Ui.T("", 10.5, "MutedBrush");
            var evBar = new Border { Width = 3, CornerRadius = new CornerRadius(2), Background = Ui.B("InfoBrush"), Margin = new Thickness(0, 0, 7, 0) };
            var evGrid = Ui.Cols(Ui.Auto, Ui.Star());
            evGrid.Put(evBar);
            evGrid.Put(Ui.V(0, _eventTitle, _eventWhen), 1);
            _eventRow = new Border { BorderBrush = Ui.B("TrackBrush"), BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 7, 0, 0), Margin = new Thickness(0, 6, 0, 0), Child = evGrid };
            right.Put(Ui.Card(Ui.V(2, _clock, _date, _eventRow), 14, 11));

            _wGlyph = Ui.Icon(Ui.GSun, 18);
            _wTemp = Ui.T("—", 20, "InkBrush", true);
            _wPlace = Ui.T("", 11, "MutedBrush");
            _wPlace.TextTrimming = TextTrimming.CharacterEllipsis;     // long city names end in "…" instead of being cut
            _wPlace.TextWrapping = TextWrapping.NoWrap;
            _wTemp.Margin = new Thickness(7, 0, 7, 0);
            var wTop = Ui.Cols(Ui.Auto, Ui.Auto, Ui.Star());
            wTop.Put(_wGlyph); wTop.Put(_wTemp, 1); wTop.Put(_wPlace, 2);
            _hours = new UniformGrid { Rows = 1, Columns = 4, VerticalAlignment = VerticalAlignment.Bottom };
            var wGrid = Ui.Rows(Ui.Auto, Ui.Star());
            wGrid.Put(wTop);
            wGrid.Put(_hours, 0, 1);
            right.Put(Ui.Card(wGrid, 14, 10), 0, 2);

            SyncVolume(W.Audio.Volume, W.Audio.Muted);
        }

        private bool _visible;

        public override void Shown()
        {
            _visible = true;
            _sourcesSig = null;
            OnMediaChanged();
        }

        public override void Hidden()
        {
            // Closed: drop the chips (and their animations); they're rebuilt when the notch opens again.
            _visible = false;
            _sourcesRow.Children.Clear();
            _sourcesSig = null;
        }

        public override void Refresh()
        {
            var now = DateTime.Now;
            _clock.Text = now.ToString("HH:mm");
            _date.Text = NotchWindow.Ro.TextInfo.ToTitleCase(now.ToString("dddd, d MMMM", NotchWindow.Ro));

            var ev = W.Events.FirstOrDefault();
            if (ev != null)
            {
                _eventRow.Visibility = Visibility.Visible;
                _eventTitle.Text = ev.Title;
                _eventWhen.Text = When(ev);
            }
            else if (string.IsNullOrWhiteSpace(W.S.CalendarIcs))
            {
                _eventRow.Visibility = Visibility.Visible;
                _eventTitle.Text = "Leagă calendarul";
                _eventWhen.Text = "Setări → Calendar";
            }
            else _eventRow.Visibility = Visibility.Collapsed;

            var wx = W.Weather;
            _wGlyph.Text = W.WeatherGlyph();
            _wGlyph.Foreground = W.WeatherBrush();
            _wTemp.Text = wx.Ok ? Math.Round(wx.Temp) + "°" : "—";
            _wPlace.Text = W.S.City + "\n" + (wx.Ok ? Math.Round(wx.Min) + "° / " + Math.Round(wx.Max) + "°" : "fără date");
            string hoursSig = string.Join(",", wx.Hours.Select(h => h.Item1.ToString("HH") + Math.Round(h.Item2)));
            if (hoursSig != _hoursSig)                 // rebuilt only when the forecast changed, not every second
            {
                _hoursSig = hoursSig;
                _hours.Children.Clear();
                foreach (var (t, temp) in wx.Hours)
                {
                    var hourBox = Ui.V(1, Ui.T(t.ToString("HH"), 11, "DimBrush"), Ui.T(Math.Round(temp) + "°", 11));
                    foreach (UIElement c in hourBox.Children) ((TextBlock)c).HorizontalAlignment = HorizontalAlignment.Center;
                    _hours.Children.Add(hourBox);
                }
            }

            RefreshSources();
        }

        private static string When(CalendarEvent ev)
        {
            var d = ev.Start.Date;
            string day = d == DateTime.Today ? "azi" : d == DateTime.Today.AddDays(1) ? "mâine" : ev.Start.ToString("dddd", NotchWindow.Ro);
            return ev.AllDay ? day + ", toată ziua" : day + ", " + ev.Start.ToString("HH:mm");
        }

        // ---------------------------------------------------------------- media

        public void OnMediaChanged()
        {
            var m = W.Now.Info;
            if (!m.HasSession)
            {
                _title.Text = "Nimic nu rulează";
                _artist.Text = "Pornește muzică sau un video în orice aplicație";
                _art.Background = Ui.B("TrackBrush");
                _artGlyph.Visibility = Visibility.Visible;
                _tint.Background = null;
                _lyric1.Text = _lyric2.Text = "";
                _playGlyph.Text = Ui.GPlay;
                _prog.ScaleX = 0;
                _cur.Text = _dur.Text = "0:00";
                _lyrics = new List<LyricLine>();
                return;
            }
            _title.Text = m.Title;
            _artist.Text = string.IsNullOrEmpty(m.Artist) ? m.App : m.Artist + (string.IsNullOrEmpty(m.App) ? "" : "  ·  " + m.App);
            _playGlyph.Text = m.Playing ? Ui.GPause : Ui.GPlay;
            _dur.Text = Ui.Fmt(m.Duration);

            string artKey = m.Id + m.Title + m.ArtKey + (m.Art != null);
            if (artKey != _artKey)
            {
                _artKey = artKey;
                if (m.Art != null)
                {
                    _art.Background = new ImageBrush(m.Art) { Stretch = Stretch.UniformToFill };
                    _artGlyph.Visibility = Visibility.Collapsed;
                    var c = Dominant(m.Art);
                    _tint.Background = new RadialGradientBrush
                    {
                        Center = new Point(0, 0.4), GradientOrigin = new Point(0, 0.4), RadiusX = 1.2, RadiusY = 1.4,
                        GradientStops = new GradientStopCollection
                        {
                            new GradientStop(Color.FromArgb(0x55, c.R, c.G, c.B), 0),
                            new GradientStop(Color.FromArgb(0x22, c.R, c.G, c.B), 0.45),
                            new GradientStop(Color.FromArgb(0x00, c.R, c.G, c.B), 0.8)
                        }
                    };
                }
                else
                {
                    _art.Background = Ui.B("TrackBrush");
                    _artGlyph.Visibility = Visibility.Visible;
                    _tint.Background = null;
                }
            }

            string lk = m.Artist + "|" + m.Title;
            if (lk != _lyricsKey)
            {
                _lyricsKey = lk;
                _lyrics = new List<LyricLine>();
                _lyric1.Text = _lyric2.Text = "";
                if (W.S.Lyrics && LyricsAllowed(m)) LoadLyrics(m);
            }
            UpdateLyricsVisibility();
            if (_visible) RefreshSources();
        }

        /// <summary>
        /// Lyrics are looked up online (lrclib.net) by title and artist. For browser tabs that's only done on music sites,
        /// so titles of other pages you watch aren't sent anywhere.
        /// </summary>
        private static bool LyricsAllowed(MediaInfo m)
        {
            if (m.Tab == null) return true;
            return m.Tab.Site is "YouTube Music" or "Spotify Web" or "SoundCloud" or "Deezer";
        }

        private async void LoadLyrics(MediaInfo m)
        {
            string key = _lyricsKey;
            var lines = await LyricsService.GetAsync(m.Artist, m.Title, m.Duration);
            if (key != _lyricsKey) return;       // song changed meanwhile
            _lyrics = lines;
            UpdateLyricsVisibility();
        }

        private void ToggleLyrics()
        {
            W.S.Lyrics = !W.S.Lyrics;
            W.S.Save();
            if (W.S.Lyrics && _lyrics.Count == 0 && W.Now.Info.HasSession) { _lyricsKey = ""; OnMediaChanged(); }
            UpdateLyricsVisibility();
        }

        private void UpdateLyricsVisibility()
        {
            bool show = W.S.Lyrics && _lyrics.Count > 0;
            _lyric1.Visibility = _lyric2.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            _lyricsBtn.Visibility = W.Now.Info.HasSession ? Visibility.Visible : Visibility.Collapsed;
            _lyricsBtn.Opacity = W.S.Lyrics ? 1 : 0.55;
        }

        public void SyncVolume(int v, bool muted)
        {
            _syncing = true;
            _vol.Value = v;
            _syncing = false;
            _volN.Text = muted ? "—" : v.ToString();
            _volGlyph.Text = muted ? Ui.GMute : Ui.GVol;
        }

        public override void Fast()
        {
            var m = W.Now.Info;
            if (m.HasSession)
            {
                var pos = m.LivePosition;
                _cur.Text = Ui.Fmt(pos);
                _prog.ScaleX = m.Duration.TotalSeconds > 0 ? Math.Clamp(pos.TotalSeconds / m.Duration.TotalSeconds, 0, 1) : 0;
                if (_lyrics.Count > 0 && W.S.Lyrics)
                {
                    int i = LyricsService.IndexAt(_lyrics, pos);
                    string a = i >= 0 ? _lyrics[i].Text : "♪";
                    string b = i + 1 < _lyrics.Count ? _lyrics[i + 1].Text : "";
                    if (string.IsNullOrWhiteSpace(a)) a = "♪";
                    if (_lyric1.Text != a) { _lyric1.Text = a; _lyric2.Text = b; }
                }
            }
            var bands = W.Viz.Bands();
            for (int i = 0; i < bands.Length && i < _barScale.Length; i++) _barScale[i].ScaleY = 0.04 + bands[i] * 0.96;
        }

        /// <summary>Most colorful average of the cover, used to tint the card.</summary>
        internal static Color Dominant(BitmapSource src)
        {
            try
            {
                var small = new TransformedBitmap(src, new ScaleTransform(24.0 / src.PixelWidth, 24.0 / src.PixelHeight));
                var bgra = new FormatConvertedBitmap(small, PixelFormats.Bgra32, null, 0);
                int w = bgra.PixelWidth, h = bgra.PixelHeight;
                var px = new byte[w * h * 4];
                bgra.CopyPixels(px, w * 4, 0);
                double r = 0, g = 0, b = 0, wsum = 0;
                for (int i = 0; i < px.Length; i += 4)
                {
                    double bb = px[i], gg = px[i + 1], rr = px[i + 2];
                    double max = Math.Max(rr, Math.Max(gg, bb)), min = Math.Min(rr, Math.Min(gg, bb));
                    double sat = max <= 0 ? 0 : (max - min) / max;
                    double weight = 0.05 + sat * sat * (max / 255.0);
                    r += rr * weight; g += gg * weight; b += bb * weight; wsum += weight;
                }
                var c = Color.FromRgb((byte)(r / wsum), (byte)(g / wsum), (byte)(b / wsum));
                double lum = (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255;      // bright/white covers: keep text readable
                if (lum > 0.55) { double k = 0.55 / lum; c = Color.FromRgb((byte)(c.R * k), (byte)(c.G * k), (byte)(c.B * k)); }
                return c;
            }
            catch { return Color.FromRgb(0xF5, 0xA5, 0x24); }
        }

        // ---------------------------------------------------------------- sources

        /// <summary>
        /// Audio streams, browser tabs and media sessions merged into one list of what is making sound now, loudest first.
        /// </summary>
        internal static List<SourceItem> BuildSources(NotchWindow w, bool all = false)
        {
            var media = w.Now.All;
            var list = new List<SourceItem>();
            var used = new HashSet<MediaInfo>();
            foreach (var a in w.Sessions.Sources)
            {
                if (w.S.BrowserTabs && w.Bridge.IsConnected(a.Process))
                {
                    int before = list.Count;
                    foreach (var mi in media.Where(m => m.Tab != null && m.Tab.Browser == a.Process && !used.Contains(m)))
                    {
                        used.Add(mi);
                        list.Add(new SourceItem { Name = mi.Tab.Site, Via = a.Name, Title = mi.Title == mi.Tab.Site ? "" : mi.Title, Audio = a, Tab = mi.Tab, Media = mi });
                    }
                    // A visible tab explains the sound. Otherwise the browser's sound comes from something without a
                    // media element (a Meet/Discord call, a WebAudio game): show the browser itself.
                    if (list.Skip(before).Any(x => x.Visible) || !a.Active) continue;
                }
                var mi2 = media.FirstOrDefault(m => m.Tab == null && !used.Contains(m) && Matches(m, a));
                if (mi2 != null) used.Add(mi2);
                list.Add(new SourceItem { Name = a.Name, Title = mi2?.Title ?? "", Audio = a, Media = mi2 });
            }
            foreach (var m in media.Where(m => !used.Contains(m)))
                list.Add(m.Tab != null
                    ? new SourceItem { Name = m.Tab.Site, Via = AudioSessionsService.Friendly(m.Tab.Browser), Title = m.Title == m.Tab.Site ? "" : m.Title, Tab = m.Tab, Media = m }
                    : new SourceItem { Name = m.App, Title = m.Title, Media = m });
            if (!all) list = list.Where(s => s.Visible).ToList();
            return list.Select(x => (x, SourceItem.OrderOf(x.Key))).OrderBy(t => t.Item2).Select(t => t.x).ToList();
        }

        private static bool Matches(MediaInfo m, AudioSource a)
        {
            string id = (m.Id ?? "").ToLowerInvariant();
            string app = (m.App ?? "").ToLowerInvariant();
            return id.Contains(a.Process) || (app.Length > 0 && (a.Process.Contains(app) || a.Name.ToLowerInvariant() == app));
        }

        private static bool SameTitle(string media, string tab)
        {
            if (string.IsNullOrWhiteSpace(media) || string.IsNullOrWhiteSpace(tab)) return false;
            string a = media.Trim().ToLowerInvariant(), b = CleanTitle(tab).ToLowerInvariant();
            return b.Contains(a) || a.Contains(b);
        }

        internal static string CleanTitle(string t) => BrowserBridge.CleanTitle(t);

        /// <summary>Mute / unmute one source: the tab itself when it's a tab, otherwise the app.</summary>
        internal static void ToggleMute(NotchWindow w, SourceItem s)
        {
            if (s.Tab != null) w.Bridge.Mute(s.Tab, !s.Tab.Muted);
            else if (s.Audio != null) w.Sessions.ToggleMute(s.Audio);
        }

        internal static void PlayPause(NotchWindow w, SourceItem s)
        {
            if (s.Media == null) return;
            if (s.Playing) SourceItem.PausedAt[s.Key] = DateTime.Now; else SourceItem.PausedAt.Remove(s.Key);
            w.Now.PlayPause(s.Media.Id);
        }

        internal static string Sig(SourceItem i) => i.Key + i.Name + i.Title + i.Sounding + i.Muted + i.Playing;

        private void RefreshSources()
        {
            if (!_visible) return;
            var items = BuildSources(W);
            string sig = string.Join("|", items.Select(Sig)) + "|" + W.Now.Info.Id + W.Bridge.ConnectedBrowsers().Count;
            if (sig == _sourcesSig) return;
            _sourcesSig = sig;

            _sourcesRow.Children.Clear();
            _sourcesRow.ColumnDefinitions.Clear();
            if (items.Count == 0)
            {
                var hint = Ui.T("Nu se aude nimic acum", 12, "MutedBrush");
                hint.HorizontalAlignment = HorizontalAlignment.Center;
                _sourcesRow.Children.Add(Ui.Card(hint, 12, 0, 11));
                return;
            }
            int shown = Math.Min(3, items.Count);
            for (int i = 0; i < shown; i++)
            {
                if (i > 0) _sourcesRow.ColumnDefinitions.Add(new ColumnDefinition { Width = Ui.Px(6) });
                _sourcesRow.ColumnDefinitions.Add(new ColumnDefinition { Width = Ui.Star() });
                _sourcesRow.Put(Chip(items[i]), _sourcesRow.ColumnDefinitions.Count - 1);
            }
            if (items.Count > shown)
            {
                _sourcesRow.ColumnDefinitions.Add(new ColumnDefinition { Width = Ui.Px(6) });
                _sourcesRow.ColumnDefinitions.Add(new ColumnDefinition { Width = Ui.Px(46) });
                var more = new Button { Style = Ui.S("TileButton"), ToolTip = "Toate sursele audio", Content = Ui.V(0, Center(Ui.T("+" + (items.Count - shown), 13, "InkBrush", true)), Center(Ui.T("încă", 10.5, "MutedBrush"))) };
                more.VerticalContentAlignment = VerticalAlignment.Center;
                more.HorizontalContentAlignment = HorizontalAlignment.Center;
                more.Background = Ui.B("ChipBrush");
                more.Click += (o, e) => W.ShowSources();
                _sourcesRow.Put(more, _sourcesRow.ColumnDefinitions.Count - 1);
            }
        }

        private static TextBlock Center(TextBlock t) { t.HorizontalAlignment = HorizontalAlignment.Center; return t; }

        private FrameworkElement Chip(SourceItem s)
        {
            bool selected = s.Media != null && s.Media.Id == W.Now.Info.Id;
            var g = Ui.Cols(Ui.Auto, Ui.Star(), Ui.Auto, Ui.Auto);
            var badge = g.Put(Ui.AppBadge(s.Name, 24));
            if (!(s.Playing || s.Sounding)) badge.Opacity = 0.5;              // paused: dim the icon, keep the text readable
            string sub = !string.IsNullOrEmpty(s.Title) ? s.Title : s.Muted ? "fără sunet" : s.Playing ? "se aude" : "pe pauză";
            var texts = Ui.V(0, Ui.T(s.Name, 12, "InkBrush", true), Ui.T(sub, 11, "MutedBrush"));
            texts.Margin = new Thickness(8, 0, 6, 0);
            texts.VerticalAlignment = VerticalAlignment.Center;
            texts.ToolTip = s.Name + (s.Via != null ? " · " + s.Via : "") + (string.IsNullOrEmpty(s.Title) ? "" : "\n" + s.Title);
            g.Put(texts, 1);
            g.Put(Level(s, 12), 2);
            if (s.Audio != null || s.Tab != null)
            {
                var mute = Ui.IconBtn(s.Muted ? Ui.GMute : Ui.GVol, () => { ToggleMute(W, s); _sourcesSig = null; RefreshSources(); },
                    s.Muted ? "Pornește sunetul" : "Oprește sunetul", 26, 12, s.Muted ? Ui.B("HotBrush") : Ui.B("InkBrush"));
                g.Put(mute, 3);
            }
            var border = new Border
            {
                Background = Ui.B("ChipBrush"), CornerRadius = new CornerRadius(11), Padding = new Thickness(8, 0, 3, 0),
                BorderThickness = new Thickness(1), Child = g
            };
            if (selected) border.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
            else border.BorderBrush = Brushes.Transparent;
            if (s.Media != null) border.OnClick(() => { W.Now.Select(s.Media.Id); });
            else if (s.Tab != null) border.OnClick(() => W.Bridge.Focus(s.Tab));
            return border;
        }

        /// <summary>Four little bars: green and bouncing while the source is heard, grey and still otherwise.</summary>
        internal static FrameworkElement Level(SourceItem s, double height)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Height = height, VerticalAlignment = VerticalAlignment.Center };
            bool live = s.Sounding;
            double[] durs = { 0.52, 0.41, 0.6, 0.47 };
            for (int i = 0; i < 4; i++)
            {
                var scale = new ScaleTransform(1, live ? 0.5 : 0.3);
                var r = new Rectangle
                {
                    Width = 2.5, Height = height, Margin = new Thickness(i == 0 ? 0 : 1.5, 0, 0, 0),
                    RadiusX = 1.2, RadiusY = 1.2, RenderTransformOrigin = new Point(0.5, 1), RenderTransform = scale,
                    Fill = live ? Ui.B("OkBrush") : Ui.B("DimBrush")
                };
                if (live)
                    Ui.Loop(r, scale, ScaleTransform.ScaleYProperty, new System.Windows.Media.Animation.DoubleAnimation(0.25, 1, TimeSpan.FromSeconds(durs[i]))
                    { AutoReverse = true, RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever, BeginTime = TimeSpan.FromSeconds(i * 0.09) });
                sp.Children.Add(r);
            }
            return sp;
        }
    }
}
