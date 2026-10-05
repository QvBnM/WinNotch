using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace WinNotch.Services
{
    /// <summary>
    /// One list of everything that plays media, and the pick for the big card.
    /// Apps come from Windows' media sessions; browser tabs come from the extension (each tab its own entry, with its
    /// own title, cover, position and instant play/pause). When the extension is connected, Windows' sessions of that
    /// browser are ignored: they can't be matched reliably to tabs and were the source of the mixed-up covers.
    /// </summary>
    public sealed class NowPlaying
    {
        private readonly MediaService _media;
        private readonly BrowserBridge _bridge;
        private readonly Func<bool> _tabsOn;
        private readonly object _lock = new object();




        private readonly HashSet<string> _artFailed = new HashSet<string>();
        private readonly Queue<string> _artOrder = new Queue<string>();

        private string _selected;              // the user's pick
        private string _shown;                 // what the card shows; stays while it plays
        private readonly Dictionary<string, int> _order = new Dictionary<string, int>();
        private int _seq;
        private readonly Dictionary<string, bool> _wasPlaying = new Dictionary<string, bool>();
        private readonly Dictionary<string, BitmapImage> _art = new Dictionary<string, BitmapImage>();
        private readonly HashSet<string> _artLoading = new HashSet<string>();
        private string _lastTrackKey = "";
        private readonly Dictionary<string, DateTime> _announced = new Dictionary<string, DateTime>();
        private DateTime _lastAnnounce = DateTime.MinValue;

        /// <summary>Song identity independent of the source: "(3) Titlu - YouTube" from a tab = "Titlu" from Windows.</summary>
        internal static string TrackKey(string title, string artist)
        {
            string t = (title ?? "").Trim();
            t = System.Text.RegularExpressions.Regex.Replace(t, @"^\(\d+\+?\)\s*", "");
            foreach (var suffix in new[] { " - YouTube", " - YouTube Music", " – YouTube" })
                if (t.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) t = t.Substring(0, t.Length - suffix.Length);
            return t.Trim().ToLowerInvariant();
        }

        /// <summary>Raised on any thread. true = the card moved to a new song/video.</summary>
        public event Action<bool> Changed;

        public NowPlaying(MediaService media, BrowserBridge bridge, Func<bool> tabsOn)
        {
            _media = media;
            _bridge = bridge;
            _tabsOn = tabsOn;
            _media.Changed += Update;
            _bridge.Changed += Update;
        }

        public static string BrowserOf(string appId)
        {
            string low = (appId ?? "").ToLowerInvariant();
            if (low.Contains("msedge")) return "msedge";
            if (low.Contains("chrome")) return "chrome";
            if (low.Contains("brave")) return "brave";
            if (low.Contains("opera")) return "opera";
            if (low.Contains("vivaldi")) return "vivaldi";
            return null;
        }

        private List<MediaInfo> _snapshot;       // rebuilt on every change, read many times per second by the UI
        private MediaInfo _info;

        /// <summary>Everything that plays (or played and is paused), in the order it first appeared.</summary>
        public List<MediaInfo> All => _snapshot ?? Build();

        private List<MediaInfo> Build()
        {
            {
                var connected = _tabsOn() ? _bridge.ConnectedBrowsers() : new List<string>();
                var windows = _media.All;
                var list = new List<MediaInfo>();
                foreach (var m in windows)
                {
                    var b = BrowserOf(m.AppId);
                    if (b != null && connected.Contains(b)) continue;      // the tabs below replace it
                    list.Add(m);
                }
                foreach (var b in connected)
                    foreach (var t in _bridge.TabsFor(b))
                    {
                        if (!t.HasMedia && !t.Audible && !t.Paused) continue;
                        try { list.Add(FromTab(t, windows)); }          // one odd tab never blocks the whole list
                        catch (Exception ex) { App.Log("Tab media: " + ex.Message); }
                    }
                lock (_lock)
                {
                    foreach (var m in list)
                    {
                        if (!_order.TryGetValue(m.Id, out var o)) _order[m.Id] = o = ++_seq;
                        m.Order = o;
                    }
                    if (_order.Count > 200)
                        foreach (var k in _order.Keys.Except(list.Select(x => x.Id)).ToList()) _order.Remove(k);
                }
                return list.OrderBy(m => m.Order).ToList();
            }
        }

        private MediaInfo FromTab(BrowserTab t, List<MediaInfo> windows)
        {
            string title = !string.IsNullOrWhiteSpace(t.MediaTitle) ? t.MediaTitle : BrowserBridge.CleanTitle(t.Title);
            var m = new MediaInfo
            {
                Id = "tab:" + t.Browser + ":" + t.Id,
                Tab = t,
                HasSession = true,
                Title = string.IsNullOrWhiteSpace(title) ? t.Site : title,
                Artist = !string.IsNullOrWhiteSpace(t.Artist) ? t.Artist : t.Site,
                App = t.Site,
                Playing = t.IsPlaying,
                Duration = TimeSpan.FromSeconds(Math.Clamp(t.Duration, 0, 7 * 86400)),
                Position = TimeSpan.FromSeconds(Math.Clamp(t.Position, 0, 7 * 86400)),
                PositionUpdated = t.ReceivedAt == default ? DateTimeOffset.Now : t.ReceivedAt
            };

            string url = !string.IsNullOrEmpty(t.ArtUrl) ? t.ArtUrl : NetSafety.YouTubeThumb(t.Url);
            if (!string.IsNullOrEmpty(url))
            {
                BitmapImage img;
                lock (_lock) _art.TryGetValue(url, out img);
                if (img != null) { m.Art = img; m.ArtKey = url; }
                else LoadArt(url);
            }
            if (m.Art == null)
            {
                // The browser's own Windows session for the same video, if its title matches (cover only).
                var w = windows.FirstOrDefault(x => x.Art != null && BrowserOf(x.AppId) == t.Browser &&
                                                    string.Equals(x.Title?.Trim(), m.Title.Trim(), StringComparison.OrdinalIgnoreCase));
                if (w != null) { m.Art = w.Art; m.ArtKey = w.ArtKey; }
            }
            return m;
        }



        /// <summary>
        /// Tab covers: the extension downloads them in the browser (through its proxy / VPN) and sends the bytes;
        /// WinNotch never connects to an address a web page chose. Only PNG / JPEG / WebP up to 300 KB and 4096 px
        /// are decoded, and none at all when running as administrator (image decoders in an elevated process are
        /// exactly what an attacker would aim at).
        /// </summary>
        private void LoadArt(string url)
        {
            if (App.IsAdmin || url.Length > 2048) return;
            var bytes = _bridge.ArtFor(url);
            if (bytes == null) return;
            lock (_lock) { if (_artFailed.Contains(url) || !_artLoading.Add(url)) return; }
            _ = Task.Run(() =>
            {
                try
                {
                    if (!BrowserBridge.IsSafeImage(bytes)) throw new InvalidDataException("format refuzat");
                    var frame = BitmapDecoder.Create(new MemoryStream(bytes), BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
                    if (frame.PixelWidth > 4096 || frame.PixelHeight > 4096) throw new InvalidDataException("imagine prea mare");
                    var img = MediaService.ImageFromStream(new MemoryStream(bytes));
                    lock (_lock)
                    {
                        _art[url] = img;
                        _artOrder.Enqueue(url);
                        while (_artOrder.Count > 60) _art.Remove(_artOrder.Dequeue());     // forget the oldest, not everything
                    }
                    Update();
                }
                catch (Exception ex)
                {
                    lock (_lock) { if (_artFailed.Count > 500) _artFailed.Clear(); _artFailed.Add(url); }     // don't retry a bad cover
                    App.Log("Copertă tab: " + ex.Message);
                }
                finally { lock (_lock) _artLoading.Remove(url); }
            });
        }

        /// <summary>
        /// The card: the user's pick; otherwise it stays on what it shows while that plays, and moves only when it stops
        /// (or when something new starts while nothing else plays).
        /// </summary>
        public MediaInfo Info => _info ?? Pick(All);

        private MediaInfo Pick(List<MediaInfo> all)
        {
            lock (_lock)
            {
                var sel = all.FirstOrDefault(m => m.Id == _selected);
                if (sel != null) return sel;
                _selected = null;
                var shown = all.FirstOrDefault(m => m.Id == _shown);
                bool anyPlaying = all.Any(m => m.Playing);
                if (shown != null && (shown.Playing || !anyPlaying)) return shown;
                var pick = all.FirstOrDefault(m => m.Playing) ?? shown ?? all.FirstOrDefault();
                _shown = pick?.Id;
                return pick ?? new MediaInfo();
            }
        }

        public List<MediaInfo> AllAndInfo(out MediaInfo info)
        {
            var all = All;
            info = Pick(all);
            return all;
        }

        private readonly object _updateLock = new object();

        /// <summary>
        /// Called from several threads (Windows media events, each browser connection, cover downloads, the UI); one at a
        /// time, so an older rebuild can't overwrite a newer one and a song change isn't announced twice.
        /// </summary>
        private void Update()
        {
            bool trackChanged;
            lock (_updateLock)
            {
                trackChanged = UpdateLocked();
            }
            Changed?.Invoke(trackChanged);
        }

        private bool UpdateLocked()
        {
            var all = Build();
            _snapshot = all;
            lock (_lock)
            {
                // Something just started: the card follows it, unless the card already shows something that plays.
                foreach (var m in all)
                {
                    _wasPlaying.TryGetValue(m.Id, out bool was);
                    if (m.Playing && !was)
                    {
                        var cur = all.FirstOrDefault(x => x.Id == (_selected ?? _shown));
                        if (cur == null || !cur.Playing || cur.Id == m.Id) { _selected = null; _shown = m.Id; }
                    }
                }
                _wasPlaying.Clear();
                foreach (var m in all) _wasPlaying[m.Id] = m.Playing;
            }
            var info = Pick(all);
            _info = info;
            // An empty title is a page/app between two states (loading, ad ending): not a new song.
            if (!info.HasSession || string.IsNullOrWhiteSpace(info.Title)) return false;
            string key = TrackKey(info.Title, info.Artist);
            if (key == _lastTrackKey) return false;
            bool first = _lastTrackKey.Length == 0;
            _lastTrackKey = key;
            if (first) return false;
            // The same video seen through Windows and through the extension, a tab title that flickers, an ad in the
            // middle: announce each song at most once in 15 minutes, and nothing more often than every 10 seconds.
            var now = DateTime.Now;
            foreach (var old in _announced.Where(k => (now - k.Value).TotalMinutes > 15).Select(k => k.Key).ToList()) _announced.Remove(old);
            if (_announced.ContainsKey(key) || (now - _lastAnnounce).TotalSeconds < 10) return false;
            _announced[key] = now;
            _lastAnnounce = now;
            return true;
        }

        public void Select(string id)
        {
            lock (_lock) { _selected = id; _shown = id; _info = null; }
            Update();
        }

        private MediaInfo Find(string id) => id == null ? Info : All.FirstOrDefault(m => m.Id == id);

        /// <summary>Forces a rebuild (e.g. after the settings changed).</summary>
        public void Refresh() => Update();

        public void PlayPause(string id = null)
        {
            var m = Find(id);
            if (m == null) return;
            if (m.Tab != null)
            {
                if (m.Tab.IsPlaying) _bridge.Pause(m.Tab); else _bridge.Play(m.Tab);
                Update();
            }
            else _media.PlayPause(m.Id);
        }

        public void Next()
        {
            var m = Info;
            if (m.Tab != null) _bridge.Next(m.Tab); else if (m.HasSession) _media.Next(m.Id);
        }

        public void Previous()
        {
            var m = Info;
            if (m.Tab != null) _bridge.Previous(m.Tab); else if (m.HasSession) _media.Previous(m.Id);
        }

        public void Seek(TimeSpan pos)
        {
            var m = Info;
            if (m.Tab != null) { _bridge.Seek(m.Tab, pos.TotalSeconds); Update(); }
            else if (m.HasSession) _media.Seek(m.Id, pos);
        }
    }
}
