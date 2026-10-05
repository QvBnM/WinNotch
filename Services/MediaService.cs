using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Windows.Media.Control;

namespace WinNotch.Services
{
    public sealed class MediaInfo
    {
        public string Id = "";          // unique: app id + "#n" for Windows sessions, "tab:chrome:12" for browser tabs
        public bool HasSession;
        public string Title = "";
        public string Artist = "";
        public string App = "";
        public bool Playing;
        public BitmapImage Art;
        public string ArtKey = "";      // changes when the cover changes
        public TimeSpan Position;
        public TimeSpan Duration;
        public DateTimeOffset PositionUpdated;
        public int Order;               // first seen = lower; keeps lists in a stable order
        public string AppId = "";       // SourceAppUserModelId (Windows sessions only)
        public BrowserTab Tab;          // set when this comes from a browser tab (extension)

        /// <summary>Position extrapolated to now while playing (apps only report it every few seconds).</summary>
        public TimeSpan LivePosition
        {
            get
            {
                if (!Playing || Duration <= TimeSpan.Zero) return Position;
                var p = Position + (DateTimeOffset.Now - PositionUpdated);
                return p > Duration ? Duration : p < TimeSpan.Zero ? TimeSpan.Zero : p;
            }
        }
    }

    /// <summary>
    /// Every app that talks to Windows' media controls (Spotify, VLC, Media Player, browsers without the extension…).
    /// On any change all sessions are re-read together: Windows hands out a new wrapper object for the same session on
    /// every event, so tracking them by object (or by app id, which is the same for every Chrome tab) mixes them up.
    /// </summary>
    public sealed class MediaService
    {
        private GlobalSystemMediaTransportControlsSessionManager _mgr;
        private readonly object _lock = new object();
        private Dictionary<string, MediaInfo> _infos = new Dictionary<string, MediaInfo>();
        private Dictionary<string, GlobalSystemMediaTransportControlsSession> _sessions = new Dictionary<string, GlobalSystemMediaTransportControlsSession>();
        private readonly HashSet<string> _hooked = new HashSet<string>();
        private readonly Dictionary<string, BitmapImage> _artCache = new Dictionary<string, BitmapImage>();
        private readonly SemaphoreSlim _scanLock = new SemaphoreSlim(1, 1);
        private int _pending;
        private int _order;
        private int _idSeq;

        /// <summary>Raised on a background thread after the sessions were re-read.</summary>
        public event Action Changed;

        public List<MediaInfo> All
        {
            get { lock (_lock) return _infos.Values.Where(i => i.HasSession).OrderBy(i => i.Order).ToList(); }
        }

        public async Task StartAsync()
        {
            try
            {
                _mgr = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                _mgr.SessionsChanged += (s, e) => Kick();
                _mgr.CurrentSessionChanged += (s, e) => Kick();
                await ScanAsync();
            }
            catch (Exception ex) { App.Log("Media indisponibil: " + ex.Message); }
        }

        /// <summary>Something changed somewhere: re-read everything, at most once per ~120 ms.</summary>
        private void Kick()
        {
            if (Interlocked.Exchange(ref _pending, 1) == 1) return;
            _ = Task.Run(async () =>
            {
                await Task.Delay(120);
                Interlocked.Exchange(ref _pending, 0);
                await ScanAsync();
            });
        }

        /// <summary>Also called every couple of seconds as a safety net (some apps forget to send events).</summary>
        public void Poll() => Kick();

        private async Task ScanAsync()
        {
            if (_mgr == null) return;
            await _scanLock.WaitAsync();
            try
            {
                var list = _mgr.GetSessions();
                var infos = new Dictionary<string, MediaInfo>();
                var sessions = new Dictionary<string, GlobalSystemMediaTransportControlsSession>();
                var fresh = new List<(GlobalSystemMediaTransportControlsSession S, MediaInfo I)>();
                foreach (var s in list)
                {
                    var info = await ReadAsync(s, "", s.SourceAppUserModelId ?? "");
                    if (info != null) fresh.Add((s, info));
                }

                // Ids must stay with the same session when others close (Windows gives no stable id):
                // same app + same title keeps its id; then the same app's remaining ids in order (song changed);
                // otherwise a new number that is never reused.
                Dictionary<string, MediaInfo> prevInfos;
                lock (_lock) prevInfos = _infos;
                var taken = new HashSet<string>();
                foreach (var f in fresh)
                {
                    var m = prevInfos.Values.FirstOrDefault(o => o.AppId == f.I.AppId && o.Title == f.I.Title && !taken.Contains(o.Id));
                    if (m != null) { f.I.Id = m.Id; taken.Add(m.Id); }
                }
                foreach (var f in fresh.Where(f => f.I.Id.Length == 0))
                {
                    var m = prevInfos.Values.OrderBy(o => o.Order).FirstOrDefault(o => o.AppId == f.I.AppId && !taken.Contains(o.Id));
                    f.I.Id = m?.Id ?? f.I.AppId + "#" + (++_idSeq);
                    taken.Add(f.I.Id);
                }
                foreach (var (s, info) in fresh)
                {
                    sessions[info.Id] = s;
                    infos[info.Id] = info;
                    if (_hooked.Add(info.Id))
                    {
                        s.MediaPropertiesChanged += (a, b) => Kick();
                        s.PlaybackInfoChanged += (a, b) => Kick();
                        s.TimelinePropertiesChanged += (a, b) => Kick();
                    }
                }
                lock (_lock)
                {
                    foreach (var kv in infos)
                        kv.Value.Order = _infos.TryGetValue(kv.Key, out var old) ? old.Order : ++_order;
                    _infos = infos;
                    _sessions = sessions;
                }
                // Sessions that went away may come back under the same id with new objects: hook them again then.
                _hooked.RemoveWhere(h => !sessions.ContainsKey(h));
            }
            catch (Exception ex) { App.Log("Media: " + ex.Message); }
            finally { _scanLock.Release(); }
            Changed?.Invoke();
        }

        private async Task<MediaInfo> ReadAsync(GlobalSystemMediaTransportControlsSession s, string id, string aumid)
        {
            try
            {
                var info = new MediaInfo { Id = id, AppId = aumid, App = CleanApp(aumid) };
                var props = await s.TryGetMediaPropertiesAsync();
                info.HasSession = props != null && !string.IsNullOrWhiteSpace(props.Title);
                info.Title = props?.Title ?? "";
                info.Artist = props?.Artist ?? "";

                var pb = s.GetPlaybackInfo();
                info.Playing = pb != null && pb.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

                var tl = s.GetTimelineProperties();
                if (tl != null)
                {
                    info.Duration = tl.EndTime - tl.StartTime;
                    info.Position = tl.Position - tl.StartTime;
                    info.PositionUpdated = tl.LastUpdatedTime.Year < 2000 ? DateTimeOffset.Now : tl.LastUpdatedTime;
                }

                // Cover: cached per song, so re-reading on every event doesn't reload it.
                string artKey = aumid + "|" + info.Title + "|" + info.Artist;
                BitmapImage art;
                lock (_lock) _artCache.TryGetValue(artKey, out art);
                if (art == null)
                {
                    art = await LoadArtAsync(props);
                    if (art != null) lock (_lock) { if (_artCache.Count > 40) _artCache.Clear(); _artCache[artKey] = art; }
                }
                info.Art = art;
                info.ArtKey = art != null ? artKey : "";
                return info;
            }
            catch (Exception ex) { App.Log("Media citire: " + ex.Message); return null; }
        }

        private static async Task<BitmapImage> LoadArtAsync(GlobalSystemMediaTransportControlsSessionMediaProperties props)
        {
            try
            {
                if (props?.Thumbnail == null) return null;
                using var ras = await props.Thumbnail.OpenReadAsync();
                using var src = ras.AsStreamForRead();
                var ms = new MemoryStream();
                await src.CopyToAsync(ms);
                ms.Position = 0;
                return ImageFromStream(ms);
            }
            catch { return null; }
        }

        internal static BitmapImage ImageFromStream(Stream ms)
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 220;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }

        /// <summary>Readable app name from an AUMID: "Spotify.exe" → Spotify, "MSEdge" → Edge, "Chrome" → Chrome.</summary>
        public static string CleanApp(string id)
        {
            if (string.IsNullOrEmpty(id)) return "";
            string low = id.ToLowerInvariant();
            if (low.Contains("spotify")) return "Spotify";
            if (low.Contains("msedge")) return "Edge";
            if (low.Contains("chrome")) return "Chrome";
            if (low.Contains("firefox")) return "Firefox";
            if (low.Contains("opera")) return "Opera";
            if (low.Contains("brave")) return "Brave";
            if (low.Contains("vivaldi")) return "Vivaldi";
            if (low.Contains("vlc")) return "VLC";
            if (low.Contains("zunemusic") || low.Contains("media player")) return "Media Player";
            string s = id;
            int bang = s.LastIndexOf('!');
            if (bang >= 0) s = s.Substring(bang + 1);
            if (s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) s = s.Substring(0, s.Length - 4);
            int dot = s.LastIndexOf('.');
            if (dot >= 0 && dot < s.Length - 1) s = s.Substring(dot + 1);
            return s.Length > 0 ? char.ToUpper(s[0]) + s.Substring(1) : s;
        }

        private GlobalSystemMediaTransportControlsSession Session(string id)
        {
            lock (_lock) return id != null && _sessions.TryGetValue(id, out var s) ? s : null;
        }

        public async void PlayPause(string id) { try { var s = Session(id); if (s != null) await s.TryTogglePlayPauseAsync(); } catch { } Kick(); }
        public async void Next(string id) { try { var s = Session(id); if (s != null) await s.TrySkipNextAsync(); } catch { } Kick(); }
        public async void Previous(string id) { try { var s = Session(id); if (s != null) await s.TrySkipPreviousAsync(); } catch { } Kick(); }
        public async void Seek(string id, TimeSpan pos) { try { var s = Session(id); if (s != null) await s.TryChangePlaybackPositionAsync(pos.Ticks); } catch { } Kick(); }
    }
}
