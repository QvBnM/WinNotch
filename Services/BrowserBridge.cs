using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace WinNotch.Services
{
    /// <summary>One browser tab that is playing (or was paused from WinNotch).</summary>
    public sealed class BrowserTab
    {
        public int Id;
        public string Browser = "";      // process name: chrome, msedge, opera, brave, vivaldi
        public string Title = "";
        public string Url = "";
        public string Site = "";         // "YouTube", "YouTube Music", "Instagram"…
        public bool Audible;
        public bool Muted;
        public bool Paused;              // paused from WinNotch, can be resumed
        public double Volume = 1;        // 0..1, applied to the page's audio/video elements

        // What the page itself reports (play/pause events, instant; Chrome's own "audible" lags 2–3 s)
        public bool HasMedia;            // the page has played audio/video at some point
        public bool Playing;
        public string MediaTitle = "";   // from the page's media metadata (YouTube, YouTube Music, Spotify…)
        public string Artist = "";
        public string ArtUrl = "";
        public double Duration;          // seconds, 0 = unknown / live
        public double Position;          // seconds, at ReceivedAt
        public DateTimeOffset ReceivedAt;

        /// <summary>Heard now: from the page's play/pause when known, otherwise Chrome's "audible".</summary>
        public bool IsPlaying => HasMedia ? Playing : Audible;
    }

    /// <summary>
    /// Windows sees a browser as a single app, so three YouTube tabs look like one "Chrome". The small WinNotch
    /// extension (folder "extension", loaded in chrome://extensions) connects here over a local WebSocket
    /// (127.0.0.1 only) and reports which tabs make sound; WinNotch sends back mute / pause / volume / focus.
    /// </summary>
    public sealed class BrowserBridge : IDisposable
    {
        public const int Port = 47811;
        /// <summary>The WinNotch extension's fixed ID (from the "key" in its manifest). No other extension may connect.</summary>
        public const string ExtensionId = "acbejacebahoklfdjbopaikpnfdfahic";
        private const int MaxConnections = 6, MaxMessage = 512 * 1024, MaxTabs = 60, MaxArt = 300 * 1024;
        /// <summary>Seconds a new connection has to prove itself (hello with a valid proof); then it's closed.</summary>
        private const int HelloSeconds = 4;
        private int _active;
        private const string WsGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

        private sealed class Conn
        {
            public WebSocket Ws;
            public string Browser = "";          // set by the extension's first message
            public string Instance = "";         // browser profile
            public bool Authed;                  // proved it knows the token (HMAC of the two nonces)
            public string Nonce = "";            // our random challenge for this connection
            public DateTime Opened = DateTime.UtcNow;
            public string Version = "";
            public List<BrowserTab> Tabs = new List<BrowserTab>();
            public readonly SemaphoreSlim SendLock = new SemaphoreSlim(1, 1);
        }

        private readonly object _lock = new object();
        private readonly List<Conn> _conns = new List<Conn>();
        private TcpListener _listener;
        private CancellationTokenSource _cts;

        public event Action Changed;

        /// <summary>An extension from before 0.6.5 tried to connect (it sends its token in clear): it needs ↻ in the browser.</summary>
        public bool OldExtensionSeen { get; private set; }

        // Covers handed over by the extension (the browser downloaded them): url -> bytes, the latest 60.
        private readonly Dictionary<string, byte[]> _artBytes = new Dictionary<string, byte[]>();
        private readonly Queue<string> _artQueue = new Queue<string>();

        /// <summary>The cover the extension sent for this URL, or null (WinNotch never downloads page covers itself).</summary>
        public byte[] ArtFor(string url)
        {
            if (string.IsNullOrEmpty(url)) return null;
            lock (_lock) return _artBytes.TryGetValue(url, out var b) ? b : null;
        }

        /// <summary>PNG, JPEG or WebP by their first bytes (the only formats accepted from web pages).</summary>
        public static bool IsSafeImage(byte[] b)
        {
            if (b == null || b.Length < 12) return false;
            if (b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) return true;                       // PNG
            if (b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return true;                                      // JPEG
            return b[0] == 'R' && b[1] == 'I' && b[2] == 'F' && b[3] == 'F' && b[8] == 'W' && b[9] == 'E' && b[10] == 'B' && b[11] == 'P';   // WebP
        }

        private static string Hmac(string text) =>
            Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(Token), Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

        public void Start()
        {
            try
            {
                _cts = new CancellationTokenSource();
                _listener = new TcpListener(IPAddress.Loopback, Port) { ExclusiveAddressUse = true };
                _listener.Start();
                _ = AcceptLoop(_cts.Token);
            }
            catch (Exception ex) { App.Log("Extensia de browser: nu pot asculta pe portul " + Port + ": " + ex.Message); }
        }

        /// <summary>Process names of browsers whose extension is connected right now.</summary>
        public List<string> ConnectedBrowsers()
        {
            lock (_lock) return _conns.Where(c => c.Authed && c.Browser.Length > 0).Select(c => c.Browser).Distinct().ToList();
        }

        public bool IsConnected(string process)
        {
            lock (_lock) return _conns.Any(c => c.Authed && c.Browser == process);
        }

        /// <summary>Tabs that play sound right now, or that were paused from WinNotch, for one browser process.</summary>
        public List<BrowserTab> TabsFor(string process)
        {
            lock (_lock) return _conns.Where(c => c.Authed && c.Browser == process).SelectMany(c => c.Tabs).GroupBy(t => t.Id).Select(g => g.Last()).ToList();
        }

        public void Mute(BrowserTab t, bool on) { t.Muted = on; Send(t.Browser, new { t = "mute", id = t.Id, on }); }
        public void Pause(BrowserTab t) { t.Paused = true; t.Playing = false; t.Audible = false; Send(t.Browser, new { t = "pause", id = t.Id }); }
        public void Play(BrowserTab t) { t.Paused = false; t.Playing = true; t.ReceivedAt = DateTimeOffset.Now; Send(t.Browser, new { t = "play", id = t.Id }); }
        public void SetVolume(BrowserTab t, double v) { t.Volume = v; Send(t.Browser, new { t = "vol", id = t.Id, v = Math.Round(v, 3) }); }
        public void Focus(BrowserTab t) => Send(t.Browser, new { t = "focus", id = t.Id });
        public void Next(BrowserTab t) => Send(t.Browser, new { t = "next", id = t.Id });
        public void Previous(BrowserTab t) => Send(t.Browser, new { t = "prev", id = t.Id });
        public void Seek(BrowserTab t, double seconds) { t.Position = seconds; t.ReceivedAt = DateTimeOffset.Now; Send(t.Browser, new { t = "seek", id = t.Id, v = Math.Round(seconds, 1) }); }
        public void PauseOnly(BrowserTab t) { t.Playing = false; t.Paused = true; Send(t.Browser, new { t = "pause", id = t.Id }); }

        private void Send(string browser, object msg)
        {
            List<Conn> targets;
            lock (_lock) targets = _conns.Where(c => c.Authed && c.Browser == browser).ToList();
            var bytes = JsonSerializer.SerializeToUtf8Bytes(msg);
            foreach (var c in targets) _ = SendAsync(c, bytes);
        }

        private static async Task SendAsync(Conn c, byte[] bytes)
        {
            await c.SendLock.WaitAsync();
            try { if (c.Ws.State == WebSocketState.Open) await c.Ws.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None); }
            catch { }
            finally { c.SendLock.Release(); }
        }

        private async Task AcceptLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient client;
                try { client = await _listener.AcceptTcpClientAsync(ct); }
                catch { return; }
                if (Interlocked.Increment(ref _active) > MaxConnections) { Interlocked.Decrement(ref _active); try { client.Close(); } catch { } continue; }
                _ = Task.Run(async () => { try { await Handle(client, ct); } finally { Interlocked.Decrement(ref _active); } });
            }
        }

        private async Task Handle(TcpClient client, CancellationToken ct)
        {
            Conn conn = null;
            try
            {
                client.NoDelay = true;
                var stream = client.GetStream();
                string request = await ReadHeaders(stream, ct);
                if (request == null) { client.Close(); return; }
                var headers = ParseHeaders(request);
                headers.TryGetValue("origin", out var origin);
                headers.TryGetValue("sec-websocket-key", out var key);
                // Only browser extensions may connect; web pages (which could also reach localhost) are refused.
                bool fromExtension = origin == "chrome-extension://" + ExtensionId || origin == "extension://" + ExtensionId;
                if (key == null || !fromExtension)
                {
                    var deny = Encoding.ASCII.GetBytes("HTTP/1.1 403 Forbidden\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
                    await stream.WriteAsync(deny, 0, deny.Length, ct);
                    client.Close();
                    return;
                }
                string accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key.Trim() + WsGuid)));
                var resp = Encoding.ASCII.GetBytes("HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: " + accept + "\r\n\r\n");
                await stream.WriteAsync(resp, 0, resp.Length, ct);

                var ws = WebSocket.CreateFromStream(stream, new WebSocketCreationOptions { IsServer = true, KeepAliveInterval = TimeSpan.FromSeconds(20) });
                conn = new Conn { Ws = ws, Nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant() };
                lock (_lock) _conns.Add(conn);
                // We speak first: a random challenge. The extension answers with HMAC(token, nonces) and never sends the
                // token itself; we answer with our own proof, so the extension also knows it's really talking to WinNotch.
                await SendAsync(conn, JsonSerializer.SerializeToUtf8Bytes(new { t = "challenge", n = conn.Nonce }));

                var buf = new byte[16 * 1024];
                var ms = new MemoryStream();
                while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    // The extension talks at least every 20 s (ping); a silent connection is dropped after 90 s.
                    // Not proven yet: only a few seconds in total (a program holding connections open can't block the extension).
                    double left = HelloSeconds - (DateTime.UtcNow - conn.Opened).TotalSeconds;
                    if (!conn.Authed && left <= 0) break;
                    using var idle = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    idle.CancelAfter(conn.Authed ? TimeSpan.FromSeconds(90) : TimeSpan.FromSeconds(Math.Max(0.2, left)));
                    var r = await ws.ReceiveAsync(new ArraySegment<byte>(buf), idle.Token);
                    if (r.MessageType == WebSocketMessageType.Close) break;
                    if (ms.Length + r.Count > MaxMessage) break;
                    ms.Write(buf, 0, r.Count);
                    if (!r.EndOfMessage) continue;
                    var data = ms.ToArray();
                    ms.SetLength(0);
                    OnMessage(conn, data);
                }
            }
            catch (Exception ex) when (!(ex is OperationCanceledException || ex is WebSocketException || ex is IOException)) { App.Log("Extensia de browser: " + ex.Message); }
            catch { }
            finally
            {
                if (conn != null)
                {
                    lock (_lock) _conns.Remove(conn);
                    Changed?.Invoke();
                }
                try { client.Close(); } catch { }
            }
        }

        private void OnMessage(Conn conn, byte[] data)
        {
            try
            {
                using var doc = JsonDocument.Parse(data);
                var root = doc.RootElement;
                string type = Str(root, "t");
                if (type == "hello" && !conn.Authed)
                {
                    // The extension proves it's WinNotch's own: HMAC of our challenge and its nonce, keyed with the token
                    // WinNotch wrote into its folder. Anything else that copies the Origin header is dropped here.
                    string cn = Str(root, "cn"), proof = Str(root, "proof");
                    bool ok = Token.Length >= 32 && cn.Length == 64 && cn.All(Uri.IsHexDigit) && proof.Length == 64 &&
                              CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(proof.ToLowerInvariant()), Encoding.ASCII.GetBytes(Hmac("client|" + conn.Nonce + "|" + cn)));
                    if (!ok)
                    {
                        if (proof.Length == 0 && root.TryGetProperty("token", out _)) OldExtensionSeen = true;
                        try { conn.Ws.Abort(); } catch { }
                        return;
                    }
                    conn.Authed = true;
                    _ = SendAsync(conn, JsonSerializer.SerializeToUtf8Bytes(new { t = "welcome", p = Hmac("server|" + conn.Nonce + "|" + cn) }));
                    string b = Str(root, "browser"), inst = Str(root, "inst");
                    if (Browsers.Contains(b))
                    {
                        conn.Browser = b;
                        conn.Instance = inst;
                        // One connection per browser profile: an older one from the same profile (extension restarted) is closed.
                        // Two different profiles of the same browser are both kept.
                        List<Conn> stale;
                        lock (_lock) stale = _conns.Where(c => c != conn && c.Browser == b && (c.Instance == inst || c.Instance.Length == 0)).ToList();
                        foreach (var c in stale) try { c.Ws.Abort(); } catch { }
                    }
                    conn.Version = Str(root, "v");
                }
                if (!conn.Authed) return;
                if (type == "art")
                {
                    // a cover the browser downloaded for us: only small PNG / JPEG / WebP
                    string url = Str(root, "url"), b64 = root.TryGetProperty("data", out var dv) && dv.ValueKind == JsonValueKind.String ? dv.GetString() : null;
                    if (url.Length == 0 || b64 == null || b64.Length > MaxArt * 4 / 3 + 8) return;
                    byte[] bytes;
                    try { bytes = Convert.FromBase64String(b64); } catch { return; }
                    if (bytes.Length > MaxArt || !IsSafeImage(bytes)) return;
                    lock (_lock)
                    {
                        if (!_artBytes.ContainsKey(url)) _artQueue.Enqueue(url);
                        _artBytes[url] = bytes;
                        while (_artQueue.Count > 60) _artBytes.Remove(_artQueue.Dequeue());
                    }
                    Changed?.Invoke();
                    return;
                }
                if (type != "tabs" || !root.TryGetProperty("tabs", out var arr) || arr.ValueKind != JsonValueKind.Array) return;
                var list = new List<BrowserTab>();
                foreach (var t in arr.EnumerateArray().Take(MaxTabs))
                {
                    var tab = new BrowserTab
                    {
                        Id = t.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number && id.TryGetInt32(out var iv) ? iv : 0,
                        Browser = conn.Browser,
                        Title = Short(Str(t, "title")),
                        Url = Str(t, "url"),
                        Audible = Bool(t, "audible"),
                        Muted = Bool(t, "muted"),
                        Paused = Bool(t, "paused"),
                        Volume = t.TryGetProperty("vol", out var v) && v.ValueKind == JsonValueKind.Number && double.IsFinite(v.GetDouble()) ? Math.Clamp(v.GetDouble(), 0, 1) : 1
                    };
                    tab.Site = SiteName(tab.Url);
                    if (t.TryGetProperty("media", out var md) && md.ValueKind == JsonValueKind.Object)
                    {
                        tab.HasMedia = true;
                        tab.Playing = Bool(md, "playing");
                        tab.MediaTitle = Short(Str(md, "title"));
                        tab.Artist = Short(Str(md, "artist"));
                        tab.ArtUrl = Str(md, "art");
                        tab.Duration = Num(md, "duration");
                        tab.Position = Num(md, "position");
                        tab.ReceivedAt = DateTimeOffset.Now - TimeSpan.FromMilliseconds(Math.Max(0, Num(md, "age")));
                    }
                    list.Add(tab);
                }
                lock (_lock) conn.Tabs = list;
                Changed?.Invoke();
            }
            catch (Exception ex) { App.Log("Extensia de browser, mesaj: " + ex.Message); }
        }

        private static readonly HashSet<string> Browsers = new HashSet<string> { "chrome", "msedge", "opera", "brave", "vivaldi" };

        /// <summary>Text from the extension, cut to a sane length (titles come from web pages).</summary>
        private static string Str(JsonElement e, string name)
        {
            var s = e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
            return s.Length > 2048 ? s.Substring(0, 2048) : s;
        }

        /// <summary>A number from a web page, kept to 0 … 7 days (a page can claim any duration; huge ones used to crash the card).</summary>
        /// <summary>Titles shown in the notch: at most 300 characters (a page can't fill the card with a wall of text).</summary>
        private static string Short(string s) => s.Length > 300 ? s.Substring(0, 300) : s;

        private static double Num(JsonElement e, string name)
        {
            if (!e.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Number) return 0;
            double d = v.GetDouble();
            return double.IsFinite(d) && d > 0 ? Math.Min(d, 7 * 86400.0) : 0;
        }

        private static bool Bool(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && (v.ValueKind == JsonValueKind.True);

        /// <summary>"(3) Song name - YouTube" → "Song name".</summary>
        public static string CleanTitle(string t)
        {
            t = (t ?? "").Trim();
            t = System.Text.RegularExpressions.Regex.Replace(t, @"^\(\d+\+?\)\s*", "");
            foreach (var suffix in new[] { " - YouTube Music", " - YouTube", " • Instagram", " | Instagram", " | TikTok", " | Facebook", " - Twitch", " | Netflix", " - SoundCloud", " | Spotify" })
                if (t.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) t = t.Substring(0, t.Length - suffix.Length);
            return t;
        }

        /// <summary>Friendly site name from a URL ("music.youtube.com" → "YouTube Music").</summary>
        public static string SiteName(string url)
        {
            string host;
            try { host = new Uri(url).Host.ToLowerInvariant(); } catch { return "Tab"; }
            if (host.StartsWith("www.")) host = host.Substring(4);
            if (host.StartsWith("m.")) host = host.Substring(2);
            if (host == "music.youtube.com") return "YouTube Music";
            if (host.EndsWith("youtube.com") || host == "youtu.be") return url.Contains("/shorts/") ? "YouTube Shorts" : "YouTube";
            if (host.EndsWith("instagram.com")) return url.Contains("/reels/") || url.Contains("/reel/") ? "Instagram Reels" : "Instagram";
            if (host.EndsWith("facebook.com")) return url.Contains("/reel") ? "Facebook Reels" : "Facebook";
            if (host.EndsWith("tiktok.com")) return "TikTok";
            if (host == "open.spotify.com") return "Spotify Web";
            if (host.EndsWith("soundcloud.com")) return "SoundCloud";
            if (host.EndsWith("twitch.tv")) return "Twitch";
            if (host.EndsWith("netflix.com")) return "Netflix";
            if (host.EndsWith("primevideo.com")) return "Prime Video";
            if (host.EndsWith("disneyplus.com")) return "Disney+";
            if (host.EndsWith("hbomax.com") || host.EndsWith("max.com")) return "Max";
            if (host.EndsWith("x.com") || host.EndsWith("twitter.com")) return "X";
            if (host.EndsWith("reddit.com")) return "Reddit";
            if (host.EndsWith("meet.google.com")) return "Google Meet";
            if (host.EndsWith("discord.com")) return "Discord Web";
            if (host.EndsWith("kick.com")) return "Kick";
            if (host.EndsWith("deezer.com")) return "Deezer";
            if (host.EndsWith("vimeo.com")) return "Vimeo";
            var parts = host.Split('.');
            string core = parts.Length >= 2 ? parts[parts.Length - 2] : host;
            return core.Length > 0 ? char.ToUpper(core[0]) + core.Substring(1) : "Tab";
        }

        private static async Task<string> ReadHeaders(NetworkStream s, CancellationToken ct)
        {
            var sb = new StringBuilder();
            var one = new byte[1];
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(5000);
            try
            {
                while (sb.Length < 16 * 1024)
                {
                    int n = await s.ReadAsync(one, 0, 1, timeout.Token);
                    if (n == 0) return null;
                    sb.Append((char)one[0]);
                    if (sb.Length >= 4 && sb[sb.Length - 1] == '\n' && sb[sb.Length - 2] == '\r' && sb[sb.Length - 3] == '\n' && sb[sb.Length - 4] == '\r')
                        return sb.ToString();
                }
            }
            catch { }
            return null;
        }

        private static Dictionary<string, string> ParseHeaders(string req)
        {
            var d = new Dictionary<string, string>();
            foreach (var line in req.Split("\r\n").Skip(1))
            {
                int i = line.IndexOf(':');
                if (i > 0) d[line.Substring(0, i).Trim().ToLowerInvariant()] = line.Substring(i + 1).Trim();
            }
            return d;
        }

        // ------------------------------------------------------------ extension files

        /// <summary>Folder with the unpacked extension (%AppData%\WinNotch\extension), written from the exe on start.</summary>
        public static string ExtensionFolder => Path.Combine(AppSettings.Folder, "extension");

        /// <summary>Shared secret between WinNotch and its extension, kept in the extension folder (token.json).</summary>
        public static string Token { get; private set; } = "";

        public static void WriteExtension()
        {
            try
            {
                if (!App.IsAdmin) Directory.CreateDirectory(ExtensionFolder);
                string tokenPath = Path.Combine(ExtensionFolder, "token.json");
                try
                {
                    if (File.Exists(tokenPath))
                        Token = JsonDocument.Parse(File.ReadAllText(tokenPath)).RootElement.GetProperty("token").GetString() ?? "";
                }
                catch { Token = ""; }
                // As administrator nothing is written here: the folder belongs to your account, and an elevated write
                // into it could be redirected (junction) somewhere else. The normal-rights WinNotch keeps it up to date.
                if (App.IsAdmin) return;
                using var hold = AppSettings.HoldFolder();
                if (!AppSettings.SafeToWrite(ExtensionFolder)) return;
                if (Token.Length < 32 && AppSettings.SafeToWrite(tokenPath))
                {
                    Token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
                    File.WriteAllText(tokenPath, "{\"token\":\"" + Token + "\"}");
                }
                var asm = Assembly.GetExecutingAssembly();
                foreach (var name in asm.GetManifestResourceNames().Where(n => n.StartsWith("ext/")))
                {
                    using var src = asm.GetManifestResourceStream(name);
                    using var ms = new MemoryStream();
                    src.CopyTo(ms);
                    var path = Path.Combine(ExtensionFolder, name.Substring(4));
                    if (!AppSettings.SafeToWrite(ExtensionFolder) || !AppSettings.SafeToWrite(path)) return;
                    var bytes = ms.ToArray();
                    if (File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes)) continue;
                    File.WriteAllBytes(path, bytes);
                }
            }
            catch (Exception ex) { App.Log("Extensia de browser, scriere fișiere: " + ex.Message); }
        }

        public void Dispose()
        {
            try { _cts?.Cancel(); _listener?.Stop(); } catch { }
            lock (_lock)
                foreach (var c in _conns) try { c.Ws.Abort(); } catch { }
        }
    }
}
