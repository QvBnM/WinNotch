using System;
using System.Collections.Generic; using System.IO; using System.Linq; using System.Net; using System.Net.Sockets;
using System.Text; using System.Threading; using System.Text.Json; using WinNotch.Services; using WinNotch.Widgets; using WinNotch.Core.Flags; using WinNotch.Core.Diagnostics; using WinNotch.Core.Update; using WinNotch.Core.Actions; using WinNotch.Features.Actions;
namespace WinNotch
{
    public static class App { public static void Log(string s) { } public static bool IsAdmin => false; }
    public partial class AppSettings { public static string Folder => T.TestFolder; public static bool SafeToWrite(string p) => true; public static IDisposable HoldFolder() => null; }

    /// <summary>
    /// Automated tests for the parts that don't need the WPF window: the extension bridge (real sockets), address
    /// safety, site names, the speed verdict, the calculator and the calendar. Run: tests\run-tests.bat
    /// </summary>
    public static partial class T
    {
        public static readonly string TestFolder = Path.Combine(Path.GetTempPath(), "winnotch-tests");
        static int pass, fail; static readonly List<string> lines = new List<string>();
        static void Check(string id, string name, bool ok, string detail = "")
        {
            if (ok) pass++; else fail++;
            lines.Add((ok ? "PASS" : "FAIL") + "  " + id + "  " + name + (ok || detail == "" ? "" : "  -> " + detail));
        }

        static readonly Dictionary<Socket, List<byte>> Pending = new Dictionary<Socket, List<byte>>();

        static (Socket s, string status) Connect(string origin)
        {
            var s = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            s.Connect(IPAddress.Loopback, BrowserBridge.Port);
            string key = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
            s.Send(Encoding.ASCII.GetBytes("GET / HTTP/1.1\r\nHost: 127.0.0.1\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Key: " + key + "\r\nSec-WebSocket-Version: 13\r\n" + (origin != null ? "Origin: " + origin + "\r\n" : "") + "\r\n"));
            s.ReceiveTimeout = 3000;
            var got = new List<byte>(); var buf = new byte[2048];
            try
            {
                while (true)
                {
                    int n = s.Receive(buf); if (n <= 0) break;
                    got.AddRange(buf.Take(n));
                    if (Encoding.ASCII.GetString(got.ToArray()).Contains("\r\n\r\n")) break;
                }
            }
            catch { }
            string text = Encoding.ASCII.GetString(got.ToArray());
            int end = text.IndexOf("\r\n\r\n");
            Pending[s] = end >= 0 ? got.Skip(end + 4).ToList() : new List<byte>();
            return (s, text.Split("\r\n")[0]);
        }

        /// <summary>Next text frame from the server (unmasked, short), or null.</summary>
        static string ReadFrame(Socket s, int timeoutMs = 2000)
        {
            var pend = Pending.TryGetValue(s, out var p) ? p : (Pending[s] = new List<byte>());
            var buf = new byte[4096];
            var until = DateTime.Now.AddMilliseconds(timeoutMs);
            while (true)
            {
                if (pend.Count >= 2)
                {
                    int len = pend[1] & 0x7F, hdr = 2;
                    if (len == 126 && pend.Count >= 4) { len = (pend[2] << 8) | pend[3]; hdr = 4; }
                    if (pend.Count >= hdr + len)
                    {
                        var payload = pend.Skip(hdr).Take(len).ToArray(); int op = pend[0] & 0x0F;
                        pend.RemoveRange(0, hdr + len);
                        if (op == 1) return Encoding.UTF8.GetString(payload);
                        if (op == 8) return null;
                        continue;
                    }
                }
                if (DateTime.Now > until) return null;
                try { s.ReceiveTimeout = Math.Max(50, (int)(until - DateTime.Now).TotalMilliseconds); int n = s.Receive(buf); if (n <= 0) return null; pend.AddRange(buf.Take(n)); }
                catch { return null; }
            }
        }

        static string Hmac(string key, string text) =>
            Convert.ToHexString(System.Security.Cryptography.HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

        /// <summary>The extension's side of the handshake: challenge → hello with proof → welcome (checked). True when both proved it.</summary>
        static bool Hello(Socket s, string tok, string browser, string inst, string wrongProof = null)
        {
            var ch = ReadFrame(s);
            if (ch == null) return false;
            string n = JsonDocument.Parse(ch).RootElement.GetProperty("n").GetString();
            string cn = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            SendText(s, JsonSerializer.Serialize(new { t = "hello", browser, inst, v = "1.6", cn, proof = wrongProof ?? Hmac(tok, "client|" + n + "|" + cn) }));
            var w = ReadFrame(s);
            if (w == null) return false;
            var root = JsonDocument.Parse(w).RootElement;
            return root.GetProperty("t").GetString() == "welcome" && root.GetProperty("p").GetString() == Hmac(tok, "server|" + n + "|" + cn);
        }

        static void SendText(Socket s, string txt)
        {
            var d = Encoding.UTF8.GetBytes(txt); var m = new byte[] { 1, 2, 3, 4 };
            var h = new List<byte> { 0x81 };
            if (d.Length < 126) h.Add((byte)(0x80 | d.Length));
            else if (d.Length < 65536) { h.Add(0x80 | 126); h.Add((byte)(d.Length >> 8)); h.Add((byte)d.Length); }
            else { h.Add(0x80 | 127); for (int i = 7; i >= 0; i--) h.Add((byte)((long)d.Length >> (8 * i))); }
            h.AddRange(m);
            s.Send(h.Concat(d.Select((b, i) => (byte)(b ^ m[i % 4]))).ToArray());
        }

        static bool Closed(Socket s)
        {
            try { s.ReceiveTimeout = 2000; var b = new byte[64]; int n = s.Receive(b); return n == 0 || (b[0] & 0x0F) == 8; } catch { return true; }
        }

        public static void Main()
        {
            string good = "chrome-extension://" + BrowserBridge.ExtensionId;
            if (Directory.Exists(TestFolder)) Directory.Delete(TestFolder, true);
            BrowserBridge.WriteExtension();
            string tok = BrowserBridge.Token;
            var bridge = new BrowserBridge();
            int changes = 0; bridge.Changed += () => Interlocked.Increment(ref changes);
            bridge.Start(); Thread.Sleep(300);

            var (s1, st1) = Connect("https://evil.example");
            Check("B1", "Pagina web (origin https) este refuzată", st1.Contains("403"), st1);
            var (s2, st2) = Connect("chrome-extension://aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
            Check("B2", "Altă extensie (alt ID) este refuzată", st2.Contains("403"), st2);
            var (s3, st3) = Connect(null);
            Check("B3", "Conexiune fără Origin (proces local) este refuzată", st3.Contains("403"), st3);
            var (ok, st4) = Connect(good);
            Check("B4", "Extensia WinNotch (ID fix) este acceptată", st4.Contains("101"), st4);
            var (edge, st4b) = Connect("extension://" + BrowserBridge.ExtensionId);
            Check("B5", "Extensia în Edge (extension://ID) este acceptată", st4b.Contains("101"), st4b);
            bool mutual = Hello(ok, tok, "chrome", "profilA");
            Check("B23", "Autentificare reciprocă: extensia dovedește token-ul și WinNotch îl dovedește înapoi (fără să-l trimită)", mutual);
            Hello(edge, tok, "msedge", "e1");
            var tabs = Enumerable.Range(1, 80).Select(i => new { id = i, title = "(3) Melodie " + i + " - YouTube", url = "https://www.youtube.com/watch?v=dQw4w9WgXcQ", audible = i == 1, muted = false, vol = 1,
                media = new { playing = i == 1, title = i == 1 ? "Melodie" : "", artist = "", art = "", duration = 200.5, position = 10, age = 0 } }).ToArray();
            SendText(ok, JsonSerializer.Serialize(new { t = "tabs", browser = "chrome", v = "1.3", tabs }));
            Thread.Sleep(400);
            var got = bridge.TabsFor("chrome");
            Check("B6", "Tab-urile trimise de extensie sunt primite", got.Count > 0, "count=" + got.Count);
            Check("B7", "Maximum 60 de tab-uri dintr-un mesaj", got.Count == 60, "count=" + got.Count);
            var t1 = got.FirstOrDefault(t => t.Id == 1);
            Check("B8", "Starea media din pagină e citită (playing, titlu, durată)", t1 != null && t1.HasMedia && t1.Playing && t1.MediaTitle == "Melodie" && Math.Abs(t1.Duration - 200.5) < 0.01);
            Check("B9", "Tab pe pauză raportat de pagină nu e „se aude”", got.First(t => t.Id == 2).IsPlaying == false);
            Check("B10", "Numele site-ului: YouTube", t1?.Site == "YouTube", t1?.Site);
            Check("B11", "ConnectedBrowsers include chrome", bridge.ConnectedBrowsers().Contains("chrome"));

            // command goes out to the extension
            bridge.Mute(t1, true);
            string cmd = ReadFrame(ok) ?? "";
            Check("B12", "Comanda „mute” ajunge la extensie", cmd.Contains("\"mute\"") && cmd.Contains("\"id\":1"), cmd);

            // second connection for the same browser replaces the first
            // another Chrome profile: both stay connected
            var (prof2, _) = Connect(good);
            Hello(prof2, tok, "chrome", "profilB");
            Thread.Sleep(400);
            ok.Blocking = true; ok.ReceiveTimeout = 600; bool okAlive; try { var bb = new byte[16]; ok.Receive(bb); okAlive = true; } catch (SocketException ex) { okAlive = ex.SocketErrorCode == SocketError.TimedOut; }
            Check("B19", "Două profiluri Chrome rămân conectate amândouă (nu se dau afară)", okAlive);
            prof2.Close();
            var (ok2, st5) = Connect(good);
            Hello(ok2, tok, "chrome", "profilA");
            Thread.Sleep(400);
            Check("B13", "O conexiune nouă din același profil o închide pe cea veche", Closed(ok));

            // limit of 6 connections (ok2 + edge open = 2 so far)
            var extra = new List<Socket>();
            for (int i = 0; i < 4; i++) { var (x, _) = Connect(good); extra.Add(x); }
            var (over, st6) = Connect(good);
            Check("B14", "Peste 6 conexiuni simultane sunt refuzate", st6 == "" || !st6.Contains("101"), st6);
            foreach (var x in extra) x.Close(); over.Close();
            Thread.Sleep(300);

            // oversized message drops the connection
            SendText(edge, "{\"t\":\"tabs\",\"browser\":\"msedge\",\"tabs\":[],\"pad\":\"" + new string('x', 600 * 1024) + "\"}");
            Check("B15", "Mesaj mai mare de 512 KB închide conexiunea", Closed(edge));

            // garbage JSON doesn't kill the bridge
            SendText(ok2, "nu e json {{{");
            SendText(ok2, JsonSerializer.Serialize(new { t = "tabs", browser = "chrome", tabs = new[] { new { id = 7, title = "x", url = "https://music.youtube.com/watch?v=abc", audible = true } } }));
            Thread.Sleep(400);
            Check("B16", "JSON invalid e ignorat, conexiunea merge mai departe", bridge.TabsFor("chrome").Any(t => t.Id == 7));
            SendText(ok2, JsonSerializer.Serialize(new { t = "tabs", browser = "chrome", tabs = new[] { new { id = 8, title = new string('a', 5000), url = "https://youtube.com/watch?v=abc", audible = true } } }));
            Thread.Sleep(400);
            var longTab = bridge.TabsFor("chrome").FirstOrDefault(t => t.Id == 8);
            Check("B17", "Titlu lung din pagină e tăiat la 300 de caractere", longTab != null && longTab.Title.Length == 300, longTab?.Title.Length.ToString());
            SendText(ok2, JsonSerializer.Serialize(new { t = "tabs", browser = "chrome", tabs = new[] { new { id = 9, title = "x", url = "https://youtube.com/watch?v=abc", audible = true, media = new { playing = true, title = "t", duration = 1e15, position = -5 } } } }));
            Thread.Sleep(400);
            var huge = bridge.TabsFor("chrome").FirstOrDefault(t => t.Id == 9);
            Check("B28", "Durată uriașă din pagină e limitată la 7 zile (cardul nu mai îngheață)", huge != null && huge.Duration == 7 * 86400 && huge.Position == 0, huge?.Duration.ToString());
            // covers handed over by the extension
            var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0x0D, 1, 2, 3 };
            SendText(ok2, JsonSerializer.Serialize(new { t = "art", url = "https://i.ytimg.com/vi/x/mq.jpg", data = Convert.ToBase64String(png) }));
            SendText(ok2, JsonSerializer.Serialize(new { t = "art", url = "https://evil/x.svg", data = Convert.ToBase64String(Encoding.UTF8.GetBytes("<svg onload=alert(1)>....")) }));
            SendText(ok2, JsonSerializer.Serialize(new { t = "art", url = "https://big/x.png", data = Convert.ToBase64String(png.Concat(new byte[310 * 1024]).ToArray()) }));
            Thread.Sleep(400);
            Check("B29", "Coperta trimisă de extensie (PNG) e primită", bridge.ArtFor("https://i.ytimg.com/vi/x/mq.jpg")?.Length == png.Length);
            Check("B30", "Copertă care nu e PNG/JPEG/WebP (ex. SVG) e refuzată", bridge.ArtFor("https://evil/x.svg") == null);
            Check("B31", "Copertă peste 300 KB e refuzată", bridge.ArtFor("https://big/x.png") == null);

            // unknown browser name is ignored
            var (ok3, _) = Connect(good);
            Hello(ok3, tok, "../../evil", "x");
            Thread.Sleep(300);
            Check("B18", "Nume de browser necunoscut e ignorat", !bridge.ConnectedBrowsers().Contains("../../evil"));
            // a local program that copies the Origin header but doesn't know the token
            var (fake, stf) = Connect(good);
            Hello(fake, tok, "msedge", "f", wrongProof: new string('0', 64));
            SendText(fake, JsonSerializer.Serialize(new { t = "tabs", browser = "msedge", tabs = new[] { new { id = 99, title = "fals", url = "https://youtube.com/watch?v=x", audible = true } } }));
            Thread.Sleep(400);
            Check("B20", "Proces local cu Origin copiat dar fără token: deconectat, tab-urile ignorate", Closed(fake) && bridge.TabsFor("msedge").Count == 0);
            var (old, _) = Connect(good);
            ReadFrame(old);
            SendText(old, JsonSerializer.Serialize(new { t = "hello", browser = "chrome", inst = "o", token = tok, v = "1.4" }));
            Check("B24", "Vechiul „hello” cu token-ul în clar nu mai e acceptat (token-ul nu circulă pe rețea)", Closed(old));
            var (slow, _) = Connect(good);
            var slowStart = DateTime.Now;
            bool slowClosed = false;
            while ((DateTime.Now - slowStart).TotalSeconds < 7) { if (ReadFrame(slow, 500) == null && Closed(slow)) { slowClosed = true; break; } }
            Check("B25", "Conexiune care nu se autentifică e închisă în câteva secunde (nu poate ține locul extensiei)", slowClosed && (DateTime.Now - slowStart).TotalSeconds < 7);
            var (notoken, _) = Connect(good);
            SendText(notoken, JsonSerializer.Serialize(new { t = "tabs", browser = "chrome", tabs = new[] { new { id = 98, title = "fals", url = "https://youtube.com/watch?v=x", audible = true } } }));
            Thread.Sleep(300);
            Check("B21", "Tab-uri trimise înainte de autentificare sunt ignorate", !bridge.TabsFor("chrome").Any(t => t.Id == 98));
            Check("B22", "Token-ul are 64 de caractere hex și e salvat în folderul extensiei", tok.Length == 64 && File.Exists(Path.Combine(TestFolder, "extension", "token.json")));
            bridge.Dispose();

            // site names / titles
            Check("S1", "music.youtube.com → YouTube Music", BrowserBridge.SiteName("https://music.youtube.com/watch?v=1") == "YouTube Music");
            Check("S2", "youtube.com/shorts → YouTube Shorts", BrowserBridge.SiteName("https://www.youtube.com/shorts/abc") == "YouTube Shorts");
            Check("S3", "instagram.com/reels → Instagram Reels", BrowserBridge.SiteName("https://www.instagram.com/reels/xyz/") == "Instagram Reels");
            Check("S4", "adresă invalidă → Tab", BrowserBridge.SiteName("::nu e url::") == "Tab");
            Check("S5", "„(3) Titlu - YouTube” → „Titlu”", BrowserBridge.CleanTitle("(3) Titlu - YouTube") == "Titlu", BrowserBridge.CleanTitle("(3) Titlu - YouTube"));
            Check("S6", "„Piesă - YouTube Music” → „Piesă”", BrowserBridge.CleanTitle("Piesă - YouTube Music") == "Piesă");

            // cover URL safety
            Func<string, bool> priv = ip => NetSafety.IsPrivate(IPAddress.Parse(ip));
            Check("P1", "192.168.1.1 (router) e adresă locală", priv("192.168.1.1"));
            Check("P2", "10.0.0.5 e adresă locală", priv("10.0.0.5"));
            Check("P3", "172.16.0.1 și 172.31.255.255 sunt locale", priv("172.16.0.1") && priv("172.31.255.255"));
            Check("P4", "172.32.0.1 NU e locală", !priv("172.32.0.1"));
            Check("P5", "127.0.0.1 și ::1 sunt locale", priv("127.0.0.1") && priv("::1"));
            Check("P6", "169.254.x (link-local) e locală", priv("169.254.10.10"));
            Check("P7", "fd00::1 (IPv6 privat) e local", priv("fd00::1"));
            Check("P8", "::ffff:192.168.0.1 (IPv4 în IPv6) e local", priv("::ffff:192.168.0.1"));
            Check("P9", "100.64.0.1 (CGNAT) e local", priv("100.64.0.1"));
            Check("P10", "8.8.8.8 și 142.250.0.1 sunt publice", !priv("8.8.8.8") && !priv("142.250.0.1"));
            Check("P11", "2a00:1450::1 (IPv6 public) e public", !priv("2a00:1450::1"));
            Check("P15", "2002:c0a8:0101:: (6to4 cu 192.168.1.1) e blocat", priv("2002:c0a8:101::1"));
            Check("P16", "2001:0::1 (Teredo) și 64:ff9b::c0a8:101 (NAT64) sunt blocate", priv("2001:0:0:0::1") && priv("64:ff9b::c0a8:101"));
            Check("P12", "Miniatura YouTube din URL watch", NetSafety.YouTubeThumb("https://www.youtube.com/watch?v=dQw4w9WgXcQ&t=1") == "https://i.ytimg.com/vi/dQw4w9WgXcQ/mqdefault.jpg");
            Check("P13", "Miniatura YouTube din youtu.be și shorts", NetSafety.YouTubeThumb("https://youtu.be/dQw4w9WgXcQ") != null && NetSafety.YouTubeThumb("https://youtube.com/shorts/dQw4w9WgXcQ") != null);
            Check("P14", "Fără miniatură pentru alte site-uri", NetSafety.YouTubeThumb("https://example.com/watch?v=dQw4w9WgXcQ") == null);

            // verdict
            var good1 = new SpeedRecord { Down = 300, Up = 50, Ping = 12, Router = 2, Conn = "Ethernet" };
            Check("V1", "Totul bine → verdict ok", NetService.Verdict(good1, null).Level == "ok");
            Check("V2", "Router lent pe cablu → recomandă restart router", NetService.Verdict(new SpeedRecord { Down = 300, Up = 50, Ping = 40, Router = 35, Conn = "Ethernet" }, null).Level == "bad");
            Check("V3", "Pierderi la router → bad", NetService.Verdict(new SpeedRecord { Down = 300, Up = 50, Ping = 12, Router = 3, RouterLoss = 25, Conn = "Wi-Fi" }, null).Level == "bad");
            Check("V4", "Router ok, internet cu pierderi → furnizor (warn)", NetService.Verdict(new SpeedRecord { Down = 300, Up = 50, Ping = 30, Router = 2, NetLoss = 20 }, null).Level == "warn");
            Check("V5", "Viteză la jumătate față de cel mai bun → warn", NetService.Verdict(new SpeedRecord { Down = 100, Up = 50, Ping = 12, Router = 2 }, new SpeedRecord { Down = 300 }).Level == "warn");
            Check("V6", "Test eșuat → bad cu explicație", NetService.Verdict(new SpeedRecord { Error = "timeout" }, null).Level == "bad");

            // calendar
            string Ics(params string[] ev) => "BEGIN:VCALENDAR\n" + string.Join("", ev.Select(e => "BEGIN:VEVENT\n" + e + "\nEND:VEVENT\n")) + "END:VCALENDAR\n";
            var now = new DateTime(2026, 10, 5, 10, 0, 0);
            var c1 = CalendarService.Upcoming(Ics("SUMMARY:Ziua lui Andrei\nDTSTART;VALUE=DATE:20261005"), now);
            Check("C1", "Eveniment de toată ziua de azi se vede și la 10:00 (nu dispare după 01:00)", c1.Any(e => e.Title == "Ziua lui Andrei"));
            var c2 = CalendarService.Upcoming(Ics("SUMMARY:Curs\nDTSTART:20261001T180000\nRRULE:FREQ=DAILY;COUNT=3"), now);
            Check("C2", "COUNT=3 pornit pe 1 oct: nu mai apare pe 5 oct", !c2.Any());
            var c3 = CalendarService.Upcoming(Ics("SUMMARY:Standup\nDTSTART:20260928T110000\nRRULE:FREQ=WEEKLY;BYDAY=MO,WE,FR\nEXDATE:20261005T110000"), now);
            Check("C3", "BYDAY=MO,WE,FR: apar miercuri și vineri, nu doar luni", c3.Any(e => e.Start.DayOfWeek == DayOfWeek.Wednesday) && c3.Any(e => e.Start.DayOfWeek == DayOfWeek.Friday));
            Check("C4", "EXDATE: ședința anulată de luni 5 oct nu apare", !c3.Any(e => e.Start == new DateTime(2026, 10, 5, 11, 0, 0)));
            var c5 = CalendarService.Upcoming(Ics("SUMMARY:Ședință lunară\nDTSTART:20260113T100000\nRRULE:FREQ=MONTHLY;BYDAY=2TU"), now, 30);
            Check("C5", "MONTHLY;BYDAY=2TU: a doua marți din octombrie (13 oct)", c5.Any(e => e.Start == new DateTime(2026, 10, 13, 10, 0, 0)), string.Join(",", c5.Select(e => e.Start)));
            var c6 = CalendarService.Upcoming(Ics("SUMMARY:Aniversare\nDTSTART;VALUE=DATE:20101008\nRRULE:FREQ=YEARLY"), now);
            Check("C6", "YEARLY: aniversarea din 8 octombrie apare în fiecare an", c6.Any(e => e.Start.Date == new DateTime(2026, 10, 8)));
            var c7 = CalendarService.Upcoming(Ics("UID:x1\nSUMMARY:Sync\nDTSTART:20260928T150000\nRRULE:FREQ=WEEKLY", "UID:x1\nSUMMARY:Sync (mutat)\nRECURRENCE-ID:20261005T150000\nDTSTART:20261006T160000"), now);
            Check("C7", "Ocurență mutată: apare o singură dată, la noua oră", !c7.Any(e => e.Start == new DateTime(2026, 10, 5, 15, 0, 0)) && c7.Any(e => e.Title == "Sync (mutat)"));
            var c8 = CalendarService.Upcoming(Ics("SUMMARY:Call NY\nDTSTART;TZID=America/New_York:20261005T090000"), now.Date);
            var tzNy = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
            var expected = TimeZoneInfo.ConvertTime(new DateTime(2026, 10, 5, 9, 0, 0), tzNy, TimeZoneInfo.Local);
            Check("C8", "TZID=America/New_York e convertit în ora locală", c8.Any(e => e.Start == expected), string.Join(",", c8.Select(e => e.Start)) + " vs " + expected);
            var c9 = CalendarService.Upcoming(Ics("SUMMARY:Lunar 31\nDTSTART:20260131T090000\nRRULE:FREQ=MONTHLY"), now, 60);
            Check("C9", "MONTHLY pe 31: luni fără 31 sunt sărite (nu 30 noiembrie)", c9.Any(e => e.Start.Day == 31) && !c9.Any(e => e.Start.Day == 30));
            var c10 = CalendarService.Upcoming(Ics("SUMMARY:Gata\nDTSTART:20260901T090000\nRRULE:FREQ=DAILY;UNTIL=20261003T235959Z"), now);
            Check("C10", "UNTIL în trecut: nu mai apare", !c10.Any());
            var bomb = Enumerable.Range(0, 20000).Select(i => "SUMMARY:E" + i + "\nDTSTART:19000101T090000\nRRULE:FREQ=DAILY").ToArray();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            bool bombOk; try { CalendarService.Upcoming(Ics(bomb), now); bombOk = true; } catch { bombOk = false; }
            Check("C11", "Calendar ostil (20.000 de evenimente zilnice din 1900) e procesat în sub 3 s", bombOk && sw.Elapsed.TotalSeconds < 3, sw.Elapsed.TotalSeconds.ToString("0.0") + " s");

            // launcher math
            Check("L1", "=250*1,19 → 297,5", (Launcher.Calculate("=250*1,19") ?? "").Replace(" ", "").StartsWith("297"), Launcher.Calculate("=250*1,19"));
            Check("L2", "Text cu litere nu e evaluat (fără injecție)", Launcher.Calculate("=1+1;drop") == null && Launcher.Calculate("=Convert(1,'System.String')") == null, Launcher.Calculate("=1+1;drop"));
            Check("L3", "Împărțire la zero nu crapă", SafeCalc("=1/0"));

            // widget grid (6 × 4)
            {
                WidgetSlot Sl(int c, int r, int w, int h) => new WidgetSlot { Type = "t", Col = c, Row = r, W = w, H = h };
                bool Valid(List<WidgetSlot> l) => l.All(x => Layout.Fits(x.Col, x.Row, x.W, x.H)) &&
                    !l.Any(a => l.Any(b => a != b && Layout.Overlaps(b, a.Col, a.Row, a.W, a.H)));
                var empty = new List<WidgetSlot>();
                Check("W1", "Pagină goală: primul loc liber e stânga-sus", Layout.FindFree(empty, 2, 2) is (0, 0));
                Check("W2", "Nu iese din grilă (6 coloane × 4 rânduri)", !Layout.Fits(5, 0, 2, 1) && !Layout.Fits(0, 3, 1, 2) && Layout.Fits(4, 2, 2, 2));
                var l3 = new List<WidgetSlot> { Sl(0, 0, 2, 2), Sl(2, 0, 2, 1) };
                Check("W3", "Loc ocupat nu e liber; găsește altul", !Layout.IsFree(l3, 1, 1, 1, 1) && Layout.FindFree(l3, 2, 2) is (2, 1) or (4, 0));
                var mover = Sl(4, 0, 2, 1);
                var l4 = new List<WidgetSlot> { Sl(0, 0, 2, 1), mover };
                bool moved = Layout.TryPlace(l4, mover, 0, 0, 2, 1);
                Check("W4", "Mutat peste altul: celălalt își găsește loc, fără suprapuneri", moved && mover.Col == 0 && mover.Row == 0 && Valid(l4));
                var full = new List<WidgetSlot> { Sl(0, 0, 6, 2), Sl(0, 2, 6, 2) };
                var big = full[0];
                bool grew = Layout.TryPlace(full, big, 0, 0, 6, 3);
                Check("W5", "Pagină plină: mărirea e refuzată și nimic nu se mișcă", !grew && big.H == 2 && full[1].Row == 2 && Valid(full));
                Check("W6", "Rânduri folosite", Layout.UsedRows(full) == 4 && Layout.UsedRows(empty) == 0);
                var orig = new WidgetSlot { Type = "text", Options = new Dictionary<string, string> { ["text"] = "a" } };
                var copy = orig.Clone(); copy.Options["text"] = "b";
                Check("W7", "Duplicarea unei pagini nu leagă opțiunile de original", copy.Id != orig.Id && orig.Options["text"] == "a");
                var rnd = new Random(7); var fuzz = new List<WidgetSlot>(); bool ok7 = true;
                for (int i = 0; i < 2000 && ok7; i++)
                {
                    if (fuzz.Count < 8 && rnd.Next(3) == 0) { var w = rnd.Next(1, 4); var h = rnd.Next(1, 3); var spot = Layout.FindFree(fuzz, w, h); if (spot != null) fuzz.Add(Sl(spot.Value.Col, spot.Value.Row, w, h)); }
                    else if (fuzz.Count > 0) { var m = fuzz[rnd.Next(fuzz.Count)]; Layout.TryPlace(fuzz, m, rnd.Next(-1, 7), rnd.Next(-1, 5), rnd.Next(1, 7), rnd.Next(1, 5)); }
                    ok7 = Valid(fuzz);
                }
                Check("W8", "2000 de mutări/redimensionări aleatoare: niciodată suprapuneri sau ieșiri din grilă", ok7);
            }

            FeatureFlagTests();
            UpdateTests();
            ActionTests();
            ContextTests();
            SmokeModeTests();
            AlertCharacterizationTests();
            ActivityTests();
            CommandBarTests();
            ContextPagesTests();
            QuickActionsTests();
            SmartClipboardTests();
            ShelfTests();
            AudioSwitchTests();
            NotchGuardTests();
            ShutdownTests();
            FullscreenTests();
            OverlayStackTests();

            Console.WriteLine(string.Join("\n", lines));
            Console.WriteLine($"\nTOTAL {pass + fail}: {pass} PASS, {fail} FAIL");
            Environment.ExitCode = fail == 0 ? 0 : 1;
        }

        /// <summary>Feature switches (P10): old settings, save/reload, Changed, automatic Disable, safe mode, health line.</summary>
        static void FeatureFlagTests()
        {
            var cat = new[]
            {
                new FeatureInfo("exp", "Exp", "", FeatureStage.Experimental, false),
                new FeatureInfo("beta", "Beta", "", FeatureStage.Beta, true),
                new FeatureInfo("stable", "Stabil", "", FeatureStage.Stable, true),
            };
            var opts = new JsonSerializerOptions { WriteIndented = true };

            // a settings.json from 0.6.6: no "Features" at all
            string old = "{\"Standby\":[\"music\",\"clock\"],\"City\":\"Cluj\",\"Note\":\"dpapi:AAAA\",\"DwellMs\":300}";
            var s0 = JsonSerializer.Deserialize<AppSettings>(old); s0.NormalizeFeatures();
            var f0 = new FeatureFlags(s0.Features, cat);
            Check("F1", "settings.json vechi fără „Features”: fiecare funcție are valoarea implicită",
                  s0.Features.Count == 0 && !f0.IsEnabled("exp") && f0.IsEnabled("beta") && f0.IsEnabled("stable"));
            var sNull = JsonSerializer.Deserialize<AppSettings>("{\"Features\":null}"); sNull.NormalizeFeatures();
            Check("F2", "„Features”: null (fișier editat de mână) devine gol, fără eroare", sNull.Features != null && !new FeatureFlags(sNull.Features, cat).IsEnabled("exp"));
            var real = new FeatureFlags(s0.Features);
            Check("F3", "Catalogul are „demo-flag”, Experimental, oprit implicit",
                  FeatureCatalog.Find(FeatureCatalog.DemoFlag) is { Stage: FeatureStage.Experimental, DefaultOn: false } && !real.IsEnabled(FeatureCatalog.DemoFlag));
            Check("F4", "ID-urile din catalog sunt unice și scrise cu litere mici și liniuțe",
                  FeatureCatalog.All.Select(f => f.Id).Distinct().Count() == FeatureCatalog.All.Count &&
                  FeatureCatalog.All.All(f => System.Text.RegularExpressions.Regex.IsMatch(f.Id, "^[a-z0-9]+(-[a-z0-9]+)*$") && f.Name.Length > 0 && f.Description.Length > 0));

            // demo-flag: on and off fire Changed exactly once each
            var fired = new List<string>(); real.Changed += id => fired.Add(id);
            real.Set(FeatureCatalog.DemoFlag, true);
            bool onOnce = fired.Count == 1 && fired[0] == FeatureCatalog.DemoFlag && real.IsEnabled(FeatureCatalog.DemoFlag);
            real.Set(FeatureCatalog.DemoFlag, true);
            bool sameNoEvent = fired.Count == 1;
            real.Set(FeatureCatalog.DemoFlag, false);
            Check("F5", "„demo-flag”: pornirea declanșează Changed o singură dată", onOnce);
            Check("F6", "Aceeași valoare din nou nu declanșează Changed", sameNoEvent);
            Check("F7", "„demo-flag”: oprirea declanșează Changed o singură dată", fired.Count == 2 && !real.IsEnabled(FeatureCatalog.DemoFlag));
            real.Set("nu-exista", true);
            Check("F8", "Funcție necunoscută: ignorată, fără Changed, oprită", fired.Count == 2 && !real.IsEnabled("nu-exista"));

            // save and reload
            var s1 = new AppSettings(); var f1 = new FeatureFlags(s1.Features, cat);
            f1.Set("exp", true); f1.Set("beta", false); f1.Set("stable", true);
            string json = JsonSerializer.Serialize(s1, opts);
            var s2 = JsonSerializer.Deserialize<AppSettings>(json); s2.NormalizeFeatures();
            var f2 = new FeatureFlags(s2.Features, cat);
            Check("F9", "Salvare și recitire: alegerile rămân", f2.IsEnabled("exp") && !f2.IsEnabled("beta") && f2.IsEnabled("stable"), json);
            Check("F10", "Se salvează doar ce diferă de implicit", s2.Features.Count == 2 && !s2.Features.ContainsKey("stable"), json);
            var s3 = JsonSerializer.Deserialize<AppSettings>("{\"Features\":{\"din-viitor\":true,\"exp\":true}}"); s3.NormalizeFeatures();
            new FeatureFlags(s3.Features, cat).Set("exp", false);
            Check("F11", "Cheile unor funcții din versiuni mai noi sunt păstrate la salvare", s3.Features.TryGetValue("din-viitor", out bool fut) && fut && !s3.Features.ContainsKey("exp"));

            // automatic Disable
            var logs = new List<string>(); int saves = 0; var now = new DateTime(2026, 1, 1, 12, 0, 0);
            var s4 = new AppSettings();
            var f4 = new FeatureFlags(s4.Features, cat, save: () => saves++, log: logs.Add, now: () => now);
            int ch4 = 0; f4.Changed += _ => ch4++;
            f4.Disable("beta", "API nedocumentat indisponibil");
            Check("F12", "Disable: oprește funcția, Changed o dată, salvează, scrie motivul în log",
                  !f4.IsEnabled("beta") && ch4 == 1 && saves == 1 && s4.Features.TryGetValue("beta", out bool b4) && !b4 &&
                  logs.Any(l => l.Contains("beta") && l.Contains("API nedocumentat indisponibil")) && f4.DisabledReason("beta") == "API nedocumentat indisponibil");
            f4.Set("exp", true); ch4 = 0; logs.Clear();
            f4.ReportError("exp", new InvalidOperationException("C:\\Users\\ion\\secret.txt")); now = now.AddMinutes(4);
            f4.ReportError("exp", new InvalidOperationException("x")); now = now.AddMinutes(4);
            bool stillOn = f4.IsEnabled("exp");
            f4.ReportError("exp", new InvalidOperationException("x"));
            Check("F13", "3 erori în 10 minute: funcția se oprește singură (Changed o dată)", stillOn && !f4.IsEnabled("exp") && ch4 == 1 && f4.DisabledReason("exp") != null);
            Check("F14", "Log-ul erorilor are doar tipul erorii, fără mesaj (fără căi sau date personale)",
                  logs.Count > 0 && !logs.Any(l => l.Contains("secret") || l.Contains("Users")) && logs.Any(l => l.Contains("InvalidOperationException")));
            f4.Set("stable", true); ch4 = 0;
            f4.ReportError("stable", new Exception()); now = now.AddMinutes(11);
            f4.ReportError("stable", new Exception()); now = now.AddMinutes(11);
            f4.ReportError("stable", new Exception());
            Check("F15", "Erori rare (peste 10 minute între ele) nu opresc funcția", f4.IsEnabled("stable") && ch4 == 0);
            f4.Set("exp", true);
            Check("F16", "Pornită din nou de utilizator după oprirea automată: motivul dispare", f4.IsEnabled("exp") && f4.DisabledReason("exp") == null);

            // a handler that throws: logged, the others still run, Set and Disable don't throw (on a background thread it would end the app)
            var logs6 = new List<string>();
            var f6 = new FeatureFlags(new AppSettings().Features, cat, log: logs6.Add);
            int after6 = 0;
            f6.Changed += _ => throw new InvalidOperationException("C:\\Users\\ion\\x");
            f6.Changed += _ => after6++;
            bool threw6 = false;
            try { f6.Set("exp", true); f6.Disable("exp", "test"); } catch { threw6 = true; }
            Check("F24", "Un abonat la Changed care aruncă: nu oprește Set/Disable și nici ceilalți abonați", !threw6 && after6 == 2 && !f6.IsEnabled("exp"));
            Check("F25", "Eroarea abonatului ajunge în log doar ca tip", logs6.Any(l => l.Contains("InvalidOperationException")) && !logs6.Any(l => l.Contains("Users")));
            var t6 = new Thread(() => f6.Set("exp", true)); t6.Start(); t6.Join();
            Check("F26", "Pe un fir de fundal, un abonat care aruncă nu oprește procesul", f6.IsEnabled("exp") && after6 == 3);

            // Save in Settings re-applies only what you changed: a feature switched off automatically meanwhile stays off
            var f7 = new FeatureFlags(new AppSettings().Features, cat);
            f7.Set("exp", true);
            var shown7 = new Dictionary<string, bool> { ["exp"] = true, ["beta"] = true, ["stable"] = true };   // the page, when built
            f7.Disable("exp", "API lipsă");                                                                     // meanwhile, automatically
            int ch7 = 0; f7.Changed += _ => ch7++;
            f7.ApplyChoices(shown7, new Dictionary<string, bool> { ["exp"] = true, ["beta"] = false, ["stable"] = true });
            Check("F27", "„Salvează” aplică doar ce ai schimbat: o funcție oprită automat între timp rămâne oprită, cu motivul",
                  !f7.IsEnabled("exp") && f7.DisabledReason("exp") == "API lipsă" && !f7.IsEnabled("beta") && f7.IsEnabled("stable") && ch7 == 1);
            f7.ApplyChoices(new Dictionary<string, bool> { ["exp"] = false }, new Dictionary<string, bool> { ["exp"] = true });
            Check("F29", "Repornită de tine din Setări după oprirea automată: pornește și motivul dispare", f7.IsEnabled("exp") && f7.DisabledReason("exp") == null);
            var f8 = new FeatureFlags(new AppSettings().Features, cat);
            f8.Disable("exp", new string('x', 500) + "\nlinie");
            Check("F28", "Motivul din Disable e limitat (120 de caractere, un singur rând)",
                  f8.DisabledReason("exp").Length <= FeatureFlags.MaxReason + 1 && !f8.DisabledReason("exp").Contains('\n'));

            // safe mode
            var s5 = new AppSettings(); new FeatureFlags(s5.Features, cat).Set("exp", true);
            string before5 = JsonSerializer.Serialize(s5);
            var f5 = new FeatureFlags(s5.Features, cat, safeMode: true);
            int ch5 = 0; f5.Changed += _ => ch5++;
            Check("F17", "Mod sigur: Experimental și Beta sunt oprite, Stable rămâne", !f5.IsEnabled("exp") && !f5.IsEnabled("beta") && f5.IsEnabled("stable"));
            Check("F18", "Mod sigur: ce e salvat nu se schimbă (și Setări arată alegerea ta)", JsonSerializer.Serialize(s5) == before5 && f5.IsSaved("exp") && f5.IsSaved("beta"));
            f5.Set("exp", false); f5.Set("exp", true);
            Check("F19", "Mod sigur: schimbarea unei funcții Experimental nu declanșează Changed (rămâne oprită)", ch5 == 0 && !f5.IsEnabled("exp"));

            // health summary
            HealthLog.TakeErrors();
            HealthLog.CountError("exp"); HealthLog.CountError("exp"); HealthLog.CountError("beta");
            var errs = HealthLog.TakeErrors();
            string line = HealthLog.Line(142, 0.83, errs);
            Check("F20", "Rezumatul de sănătate: RAM, CPU mediu și erorile pe funcții", line.Contains("RAM 142 MB") && line.Contains("CPU mediu 0.8%") && line.Contains("beta=1") && line.Contains("exp=2"), line);
            Check("F21", "După rezumat, numărătoarea erorilor o ia de la zero", HealthLog.TakeErrors().Length == 0 && HealthLog.Line(1, 0, HealthLog.TakeErrors()).Contains("fără erori"));
            Check("F22", "CPU mediu: 1 s de procesor în 10 s pe 4 nuclee = 2,5%", Math.Abs(HealthLog.Percent(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(10), 4) - 2.5) < 0.001);
            string written = null; HealthLog.Start(l => written = l); string now6 = HealthLog.WriteNow(); HealthLog.Stop();
            Check("F23", "Rezumatul real se scrie în log", written != null && written == now6 && written.StartsWith("Sănătate (6 h): RAM "), written);
        }

        sealed class MemStore : WinNotch.Core.Update.IStartupStore
        {
            public string Json;          // what startup.json would hold (round-tripped like the real file)
            public WinNotch.Core.Update.RollbackNote Note;
            public int Saves;
            public WinNotch.Core.Update.StartupState Load() => Json == null ? null : JsonSerializer.Deserialize<WinNotch.Core.Update.StartupState>(Json);
            public void Save(WinNotch.Core.Update.StartupState s) { Json = JsonSerializer.Serialize(s); Saves++; }
            public WinNotch.Core.Update.RollbackNote TakeRollbackNote() { var n = Note; Note = null; return n; }
            public void WriteRollbackNote(WinNotch.Core.Update.RollbackNote n) => Note = n;
            public void DeleteRollbackNote() => Note = null;
        }

        /// <summary>The app side of the startup steps, recorded in order.</summary>
        sealed class FakeHost : WinNotch.Core.Update.IStartupHost
        {
            public string ExePath { get; set; }
            public bool Valid = true, StartOk = true, MutexFree = true;
            public readonly List<string> Steps = new List<string>();
            public bool PreviousExeValid() => Valid;
            public void ReleaseExeLock() => Steps.Add("unlock-exe");
            public void LockExe() => Steps.Add("lock-exe");
            public void ReleaseMutex() => Steps.Add("release-mutex");
            public bool TakeMutex() { Steps.Add("take-mutex"); return MutexFree; }
            public bool Start(string exe, string args) { Steps.Add("start " + Path.GetFileName(exe) + (args == null ? "" : " " + args)); return StartOk; }
            public void Log(string m) => Steps.Add("log");
        }

        /// <summary>P01: version order, release choice (beta channel, refused versions), crash guard, rollback files.</summary>
        static void UpdateTests()
        {
            AppVersion V(string s) { AppVersion.TryParse(s, out var v); return v; }

            // ---- versions
            Check("AV1", "0.6.10 > 0.6.9 (pe numere, nu pe text)", V("0.6.10") > V("0.6.9"));
            Check("AV2", "0.10.0 > 0.9.9 și 1.0.0 > 0.11.0", V("0.10.0") > V("0.9.9") && V("1.0.0") > V("0.11.0"));
            Check("AV3", "0.7.0 > 0.7.0-rc.2 > 0.7.0-rc.1", V("0.7.0") > V("0.7.0-rc.2") && V("0.7.0-rc.2") > V("0.7.0-rc.1") && V("0.7.0-rc.10") > V("0.7.0-rc.2"));
            Check("AV4", "v0.6.9, 0.6.9.0 și 0.6.9+abc sunt aceeași versiune; ToString = textul semnat", V("v0.6.9") == V("0.6.9") && V("0.6.9.0") == V("0.6.9") && V("0.6.9+abc") == V("0.6.9") && V("v0.7.0-rc.1").ToString() == "0.7.0-rc.1" && V("0.6.9.0").ToString() == "0.6.9");
            bool noThrow = true; int bad = 0;
            foreach (var t in new[] { null, "", "abc", "1..2", "1.2.3.4.5", "-1.0.0", "1.0.0-", "1.0.0-rc..1", "1.0.0-rc!", "99999999999.0.0", "1.x.0", " ", "v" })
                try { if (!AppVersion.TryParse(t, out _)) bad++; } catch { noThrow = false; }
            Check("AV5", "Text invalid: refuzat, fără excepție", noThrow && bad == 13, "bad=" + bad);

            // ---- which release is offered
            UpdateInfo R(string v, bool pre = false) => new UpdateInfo { Version = V(v), PreRelease = pre, ExeUrl = "x", SigUrl = "y", Size = 1 };
            var cur = V("0.6.9");
            Check("AV6", "Aceeași versiune nu se propune", ReleaseFeed.Pick(new[] { R("0.6.9") }, cur, false) == null);
            Check("AV7", "Versiune mai veche: refuzată", ReleaseFeed.Pick(new[] { R("0.6.8"), R("0.6.1") }, cur, true) == null);
            Check("AV8", "Mai nouă: propusă, cea mai nouă dintre ele", ReleaseFeed.Pick(new[] { R("0.6.10"), R("0.6.11"), R("0.6.8") }, cur, false)?.Version == V("0.6.11"));
            var withPre = new[] { R("0.6.10"), R("0.7.0-rc.1", true) };
            Check("RF1", "Canal beta oprit: pre-release ignorat", ReleaseFeed.Pick(withPre, cur, false)?.Version == V("0.6.10"));
            Check("RF2", "Canal beta pornit: pre-release propus", ReleaseFeed.Pick(withPre, cur, true)?.Version == V("0.7.0-rc.1"));
            Check("RF3", "Pe un rc, versiunea finală e propusă și cu beta oprit", ReleaseFeed.Pick(new[] { R("0.7.0") }, V("0.7.0-rc.2"), false)?.Version == V("0.7.0"));
            Func<AppVersion, bool> refused = v => v == V("0.6.10");
            Check("RF4", "Versiunea refuzată nu mai e propusă", ReleaseFeed.Pick(new[] { R("0.6.10") }, cur, false, refused) == null);
            Check("RF5", "O versiune mai nouă decât cea refuzată e propusă", ReleaseFeed.Pick(new[] { R("0.6.10"), R("0.6.11") }, cur, false, refused)?.Version == V("0.6.11"));
            string repo = "QvBnM/winnotch", dl = "https://github.com/" + repo + "/releases/download/";
            string Rel(string tag, bool pre, string host = null, bool draft = false) =>
                "{\"tag_name\":\"" + tag + "\",\"prerelease\":" + (pre ? "true" : "false") + ",\"draft\":" + (draft ? "true" : "false") + ",\"body\":\"## Nou\\n- x\",\"assets\":[" +
                "{\"name\":\"WinNotch.exe\",\"size\":1000,\"browser_download_url\":\"" + (host ?? dl) + tag + "/WinNotch.exe\"}," +
                "{\"name\":\"WinNotch.exe.sig\",\"size\":88,\"browser_download_url\":\"" + (host ?? dl) + tag + "/WinNotch.exe.sig\"}]}";
            var feed = ReleaseFeed.Parse("[" + Rel("v0.7.0-rc.1", true) + "," + Rel("v0.6.10", false) + "," + Rel("v0.8.0", false, draft: true) + "," + Rel("v0.9.0", false, "https://evil.example/") + "," + Rel("vX", false) + "]", repo, 300L << 20);
            Check("RF6", "Lista GitHub: ciorne, tag-uri invalide și fișiere de pe alt site sunt ignorate", feed.Count == 2 && feed.Any(f => f.PreRelease && f.Version == V("0.7.0-rc.1")), "count=" + feed.Count);
            Check("RF7", "Lista GitHub cu beta oprit → 0.6.10, cu beta pornit → 0.7.0-rc.1",
                  ReleaseFeed.Pick(feed, cur, false)?.Version == V("0.6.10") && ReleaseFeed.Pick(feed, cur, true)?.Version == V("0.7.0-rc.1"));
            var single = ReleaseFeed.Parse(Rel("v0.6.10", false), repo, 300L << 20);
            Check("RF8", "„latest” (un singur release) se citește la fel; JSON stricat = nimic, fără excepție",
                  single.Count == 1 && single[0].Notes.Contains("Nou") && ReleaseFeed.Parse("{nu e json", repo, 1).Count == 0 && ReleaseFeed.Parse(null, repo, 1).Count == 0);
            var pre2 = ReleaseFeed.Parse(Rel("v0.7.0-rc.2", false), repo, 300L << 20);
            Check("RF9", "Un tag cu sufix e pre-release chiar dacă GitHub nu îl marchează așa", pre2.Count == 1 && pre2[0].PreRelease && ReleaseFeed.Pick(pre2, cur, false) == null);

            // ---- the crash guard
            var t0 = new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);
            var now = t0;
            var me = V("0.6.9");
            StartupGuard G(MemStore st) => new StartupGuard(st, me, () => now);
            StartupAction Crash(MemStore st, bool old = true, bool safe = false, int minutes = 1) { var a = G(st).Begin(safe, old); now = now.AddMinutes(minutes); return a; }
            StartupState Saved(MemStore st) => JsonSerializer.Deserialize<StartupState>(st.Json);

            var s1 = new MemStore();
            var a1 = Crash(s1); var a2 = Crash(s1); var a3 = Crash(s1);
            Check("SG1", "2 închideri bruște = nimic (pornire normală)", a1 == StartupAction.Normal && a2 == StartupAction.Normal && a3 == StartupAction.Normal);
            var a4 = G(s1).Begin(false, true);
            Check("SG2", "3 închideri bruște în 5 minute → repornire în modul sigur", a4 == StartupAction.RestartInSafeMode);
            var safeRun = G(s1); var a5 = safeRun.Begin(true, true);
            Check("SG3", "Repornirea cu --safe-mode rulează în modul sigur (fără o nouă repornire)", a5 == StartupAction.SafeMode && safeRun.InSafeMode && Saved(s1).AutoSafe);
            now = now.AddMinutes(1);
            var a6 = G(s1).Begin(false, true);
            Check("SG4", "Încă o închidere bruscă la scurt timp după modul sigur automat → revenire la versiunea anterioară", a6 == StartupAction.Rollback);

            now = t0; var s2 = new MemStore();
            Crash(s2, minutes: 8); Crash(s2, minutes: 8); Crash(s2, minutes: 8);
            Check("SG5", "3 închideri bruște rare (în 20 de minute) = nimic", G(s2).Begin(false, true) == StartupAction.Normal);

            now = t0; var s3 = new MemStore();
            for (int i = 0; i < 5; i++) { var g = G(s3); g.Begin(false, true); now = now.AddSeconds(30); g.MarkCleanExit(); }
            Check("SG6", "„Ieșire” / oprirea Windows / repornirea pentru actualizare nu se numără", G(s3).Begin(false, true) == StartupAction.Normal);

            // healthy: counted in steady minute ticks, an error starts the 10 minutes over
            now = t0; var s4 = new MemStore();
            Crash(s4); Crash(s4);
            var hg = G(s4); hg.Begin(false, true);
            var myStart4 = now;
            bool Tick(StartupGuard g, int minutes) { bool any = false; for (int i = 0; i < minutes; i++) { now = now.AddMinutes(1); any |= g.CheckHealthy(); } return any; }
            bool notYet = !Tick(hg, 9) && !hg.OldExeMayBeDeleted;
            hg.NoteError();
            bool stillNot = !Tick(hg, 9);
            bool healthy = Tick(hg, 1) && hg.OldExeMayBeDeleted && !Tick(hg, 1);
            Check("SG7", "10 minute fără erori → sănătoasă: abia acum se poate șterge WinNotch.old.exe (o eroare reia numărătoarea)", notYet && stillNot && healthy);
            var stAfter = Saved(s4);
            Check("SG8", "Sănătoasă: contorul se golește, rămâne doar pornirea curentă", stAfter.Healthy && stAfter.Starts.Count == 1 && stAfter.Starts[0] == myStart4);
            hg.MarkCleanExit();
            Check("SG8b", "Sănătoasă, apoi oprită curat: pornirea următoare e normală", G(s4).Begin(false, true) == StartupAction.Normal);

            now = t0; var s4b = new MemStore(); var sleepy = G(s4b); sleepy.Begin(false, true);
            Tick(sleepy, 3);
            now = now.AddMinutes(60);                         // laptop asleep (or the clock jumped forward)
            bool afterSleep = sleepy.CheckHealthy();
            bool soon = Tick(sleepy, 5);
            now = now.AddHours(-3);                           // clock set back
            bool back = sleepy.CheckHealthy() || Tick(sleepy, 5);
            bool finally10 = Tick(sleepy, 10);
            Check("SG17", "Somn sau ceas schimbat nu scurtează cele 10 minute (se reiau de la zero)", !afterSleep && !soon && !back && finally10);

            now = t0; var s5 = new MemStore();
            Crash(s5); Crash(s5); Crash(s5);
            G(s5).Begin(false, false);                        // → restart in safe mode
            G(s5).Begin(true, false); now = now.AddMinutes(1);
            Check("SG9", "Fără o versiune anterioară validă: fără revenire, rămâne în modul sigur", G(s5).Begin(false, false) == StartupAction.SafeMode);

            // M1: a safe-mode run that worked for a long time, then one abrupt end (power cut) → not a rollback
            now = t0; var s11 = new MemStore();
            Crash(s11); Crash(s11); Crash(s11);
            G(s11).Begin(false, true); G(s11).Begin(true, true);
            now = now.AddHours(2);
            var late = G(s11).Begin(false, true);
            Check("SG18", "Mod sigur care a mers 2 ore, apoi o pană de curent → nu revine (doar o închidere obișnuită)", late == StartupAction.Normal);
            now = t0; var s12 = new MemStore();
            G(s12).Begin(true, true); now = now.AddMinutes(1);   // --safe-mode by hand, then a crash
            Check("SG19", "Mod sigur pornit de mână (--safe-mode), apoi o închidere bruscă → nu revine", G(s12).Begin(false, true) == StartupAction.Normal);
            now = t0; var s13 = new MemStore();
            Crash(s13); Crash(s13); Crash(s13);
            G(s13).Begin(false, true); var sq = G(s13); sq.Begin(true, true); now = now.AddMinutes(1); sq.MarkCleanExit();
            Check("SG20", "Ieșire curată din modul sigur, apoi o pornire normală în 5 minute → normală (nu iar mod sigur)", G(s13).Begin(false, true) == StartupAction.Normal);

            // 30 minutes between rollbacks, across versions: 0.7.0 rolls back to 0.6.9, which then crashes the same way
            now = t0; var s6 = new MemStore();
            StartupGuard G70() => new StartupGuard(s6, V("0.7.0"), () => now);
            for (int i = 0; i < 3; i++) { G70().Begin(false, true); now = now.AddMinutes(1); }
            G70().Begin(false, true); G70().Begin(true, true); now = now.AddMinutes(1);
            var rb = G70();
            bool first = rb.Begin(false, true) == StartupAction.Rollback;
            rb.RollingBack("test");
            for (int i = 0; i < 3; i++) { G(s6).Begin(false, true); now = now.AddMinutes(1); }
            G(s6).Begin(false, true); G(s6).Begin(true, true); now = now.AddMinutes(1);
            var second = G(s6).Begin(false, true);
            Check("SG10", "A doua revenire în 30 de minute (altă versiune) e blocată: rămâne în modul sigur", first && second == StartupAction.SafeMode);
            now = now.AddMinutes(31);
            var st6 = Saved(s6); st6.Running = true; st6.SafeMode = true; st6.AutoSafe = true; st6.SafeStartedAt = now.AddMinutes(-1);
            var s6b = new MemStore { Json = JsonSerializer.Serialize(st6) };
            Check("SG11", "După 30 de minute, o nouă revenire e din nou posibilă", G(s6b).Begin(false, true) == StartupAction.Rollback);

            // the restored version reads the note: message once, refused version not offered, a newer one is
            now = t0; var s7 = new MemStore { Note = new RollbackNote { Refused = "0.7.0", Reason = "x", At = t0 } };
            var older = new StartupGuard(s7, V("0.6.9"), () => now);
            older.Begin(false, false);
            var again = new StartupGuard(s7, V("0.6.9"), () => now); again.Begin(false, false);
            Check("SG12", "După revenire: mesajul „Am revenit la 0.6.9: 0.7.0 se închidea”, o singură dată",
                  older.RollbackMessage == "Am revenit la 0.6.9: 0.7.0 se închidea" && s7.Note == null && again.RollbackMessage == null);
            Check("SG13", "Versiunea refuzată nu mai e propusă, dar una mai nouă da",
                  older.IsRefused(V("0.7.0")) && ReleaseFeed.Pick(new[] { R("0.7.0") }, V("0.6.9"), false, older.IsRefused) == null &&
                  ReleaseFeed.Pick(new[] { R("0.7.0"), R("0.7.1") }, V("0.6.9"), false, older.IsRefused)?.Version == V("0.7.1"));
            var s8 = new MemStore { Note = new RollbackNote { Refused = "0.6.9" } };
            var same = new StartupGuard(s8, V("0.6.9"), () => now); same.Begin(false, false);
            Check("SG14", "O notă veche despre versiunea instalată acum (reinstalată între timp) e ignorată", same.RollbackMessage == null && !same.IsRefused(V("0.6.9")));
            var s9 = new MemStore(); var rg = new StartupGuard(s9, V("0.7.0"), () => now); rg.Begin(false, true); rg.RollingBack("motiv");
            var st9 = Saved(s9);
            Check("SG15", "Revenirea scrie rollback.json (versiunea refuzată + motivul), ține minte versiunea și golește contorul",
                  s9.Note?.Refused == "0.7.0" && s9.Note.Reason == "motiv" && rg.IsRefused(V("0.7.0")) && st9.Starts.Count == 0 && st9.Version == "" && !st9.Running);
            var s10 = new MemStore(); var ng = new StartupGuard(s10, V("0.7.1"), () => now);
            s10.Json = JsonSerializer.Serialize(new StartupState { Version = "0.7.0", Running = true, SafeMode = true, AutoSafe = true, SafeStartedAt = now, Starts = new List<DateTime> { now, now, now }, Refused = new List<string> { "0.7.0" } });
            Check("SG16", "O versiune nouă începe cu contorul gol, dar păstrează lista refuzată", ng.Begin(false, true) == StartupAction.Normal && ng.IsRefused(V("0.7.0")));
            var s14 = new MemStore(); var rf = new StartupGuard(s14, V("0.7.0"), () => now); rf.Begin(false, true); rf.RollingBack("x"); rf.RollbackFailed();
            var st14 = Saved(s14);
            Check("SG21", "Schimbarea fișierelor a eșuat: nimic refuzat, nota ștearsă, rularea continuă în modul sigur (numărată)",
                  !rf.IsRefused(V("0.7.0")) && s14.Note == null && rf.InSafeMode && st14.Running && st14.Starts.Count == 1 && !st14.AutoSafe);
            var s15 = new MemStore(); var cs = new StartupGuard(s15, me, () => now); cs.Begin(false, true); cs.ContinueInSafeMode(); now = now.AddMinutes(1);
            Check("SG22", "Continuat în modul sigur (fără revenire automată): o închidere bruscă ulterioară nu duce la revenire", G(s15).Begin(false, true) != StartupAction.Rollback);
            var s16 = new MemStore(); var se = new StartupGuard(s16, me, () => now); se.Begin(false, true);
            se.MarkCleanExit(sessionEnding: true);
            bool cleanNow = !Saved(s16).Running;
            now = now.AddMinutes(1); se.CheckHealthy();
            bool stillClean = !Saved(s16).Running;
            now = now.AddMinutes(2); se.CheckHealthy();
            Check("SG23", "Oprirea Windows anulată de alt program: după 2 minute protecția se reia", cleanNow && stillClean && Saved(s16).Running);
            var s17 = new MemStore(); var rs = new StartupGuard(s17, me, () => now); rs.Begin(false, true); rs.MarkCleanExit(); rs.Resume();
            Check("SG24", "Actualizarea nu a putut reporni: rularea curentă e din nou protejată", Saved(s17).Running && Saved(s17).Starts.Count == 1);
            var junk = new MemStore { Json = "{\"Refused\":[\"../../x\",\"0.7.0\",\"0.7.0\",null,\"abc\"],\"Starts\":null}" };
            var jg = new StartupGuard(junk, me, () => now); jg.Begin(false, true);
            Check("SG25", "startup.json cu valori ciudate: doar versiunile valide rămân refuzate, fără duplicate", jg.State.Refused.Count == 1 && jg.IsRefused(V("0.7.0")));
            Check("SG26", "Revenirea doar la un WinNotch mai vechi", StartupGuard.IsValidPrevious(V("0.6.8"), me) && !StartupGuard.IsValidPrevious(V("0.6.9"), me) && !StartupGuard.IsValidPrevious(V("0.7.0"), me) && !StartupGuard.IsValidPrevious(null, me));

            // ---- the order of the startup steps (StartupCoordinator)
            string cdir = Path.Combine(TestFolder, "coord");
            if (Directory.Exists(cdir)) Directory.Delete(cdir, true);
            Directory.CreateDirectory(cdir);
            string cexe = Path.Combine(cdir, "WinNotch.exe");
            now = t0; var c1 = new MemStore();
            var h1 = new FakeHost { ExePath = cexe };
            for (int i = 0; i < 3; i++) { StartupCoordinator.Run(G(c1), h1, false, "--safe-mode"); now = now.AddMinutes(1); }
            h1.Steps.Clear();
            var o1 = StartupCoordinator.Run(G(c1), h1, false, "--safe-mode");
            // Steps without the log entries: these tests pin the ORDER of the real steps. That the hand-over also writes
            // its reason in the log (P51c) is pinned separately, by SD20 / SD20b.
            string RealSteps(FakeHost h) => string.Join("|", h.Steps.Where(x => x != "log"));
            Check("SC1", "Repornirea în modul sigur: mutex-ul se eliberează abia înainte de pornire, cu --safe-mode",
                  o1 == StartupOutcome.HandedOver && RealSteps(h1) == "release-mutex|start WinNotch.exe --safe-mode" &&
                  h1.Steps.Last() == "log");                 // motivul („Închidere: repornire în mod sigur”) e ultimul lucru scris
            var h2 = new FakeHost { ExePath = cexe };
            var o2 = StartupCoordinator.Run(G(c1), h2, true, "--safe-mode");
            now = now.AddMinutes(1);
            File.WriteAllText(cexe, "nou"); File.WriteAllText(Rollback.OldPath(cexe), "vechi");
            var h3 = new FakeHost { ExePath = cexe };
            var o3 = StartupCoordinator.Run(G(c1), h3, false, "--safe-mode");
            Check("SC2", "Revenirea: nota scrisă înaintea schimbării, apoi exe deblocat, schimbat, mutex eliberat, versiunea veche pornită",
                  o2 == StartupOutcome.ContinueSafe && o3 == StartupOutcome.HandedOver && c1.Note?.Refused == "0.6.9" &&
                  RealSteps(h3) == "unlock-exe|release-mutex|start WinNotch.exe" && h3.Steps.Last() == "log" && File.ReadAllText(cexe) == "vechi");
            now = t0; var c2 = new MemStore();
            var h4 = new FakeHost { ExePath = cexe, StartOk = false, MutexFree = false };
            for (int i = 0; i < 3; i++) { StartupCoordinator.Run(G(c2), new FakeHost { ExePath = cexe }, false, "--safe-mode"); now = now.AddMinutes(1); }
            var o4 = StartupCoordinator.Run(G(c2), h4, false, "--safe-mode");
            Check("SC3", "Repornirea nu pornește și între timp a pornit alt WinNotch: această copie iese", o4 == StartupOutcome.Quit && h4.Steps.Contains("take-mutex"));
            now = t0; var c3 = new MemStore();
            var h5 = new FakeHost { ExePath = cexe, StartOk = false };
            for (int i = 0; i < 3; i++) { StartupCoordinator.Run(G(c3), new FakeHost { ExePath = cexe }, false, "--safe-mode"); now = now.AddMinutes(1); }
            var o5 = StartupCoordinator.Run(G(c3), h5, false, "--safe-mode");
            Check("SC4", "Repornirea nu pornește: continuă aici, în modul sigur, cu mutex-ul luat înapoi", o5 == StartupOutcome.ContinueSafe && h5.Steps.Last() == "take-mutex");
            if (File.Exists(Rollback.OldPath(cexe))) File.Delete(Rollback.OldPath(cexe));
            now = t0; var c4 = new MemStore();
            for (int i = 0; i < 3; i++) { StartupCoordinator.Run(G(c4), new FakeHost { ExePath = cexe }, false, "--safe-mode"); now = now.AddMinutes(1); }
            StartupCoordinator.Run(G(c4), new FakeHost { ExePath = cexe }, false, "--safe-mode");
            StartupCoordinator.Run(G(c4), new FakeHost { ExePath = cexe }, true, "--safe-mode"); now = now.AddMinutes(1);
            var h6 = new FakeHost { ExePath = cexe, Valid = true };       // valid version reported, but the file is gone by now
            var o6 = StartupCoordinator.Run(G(c4), h6, false, "--safe-mode");
            Check("SC5", "Schimbarea nu se poate face: rămâne în modul sigur, mutex-ul nu a fost eliberat, nimic refuzat",
                  o6 == StartupOutcome.ContinueSafe && !h6.Steps.Contains("release-mutex") && !h6.Steps.Any(x => x.StartsWith("start")) && c4.Note == null && !Saved(c4).Refused.Contains("0.6.9") && Saved(c4).Running && Saved(c4).SafeMode);

            // ---- startup.json on disk
            string folder = Path.Combine(TestFolder, "startup");
            Directory.CreateDirectory(folder);
            var fs = new FileStartupStore(folder);
            var fg = new StartupGuard(fs, V("0.6.9"), () => now);
            bool missingOk = fg.Begin(false, true) == StartupAction.Normal && File.Exists(fs.StatePath);
            File.WriteAllText(fs.StatePath, "{ nu e json");
            bool corruptOk; try { corruptOk = new StartupGuard(fs, V("0.6.9"), () => now).Begin(false, true) == StartupAction.Normal; } catch { corruptOk = false; }
            File.WriteAllText(fs.StatePath, "null");
            bool nullOk; try { nullOk = new StartupGuard(fs, V("0.6.9"), () => now).Begin(false, true) == StartupAction.Normal; } catch { nullOk = false; }
            Check("ST1", "startup.json lipsă, corupt sau „null” = stare implicită, fără excepție", missingOk && corruptOk && nullOk);
            var w = new StartupGuard(fs, V("0.6.9"), () => now); w.Begin(false, true); w.RollingBack("r");
            Check("ST2", "Scriere atomică: fără fișier .tmp rămas; rollback.json citit o dată și șters",
                  !File.Exists(fs.StatePath + ".tmp") && File.Exists(fs.NotePath) && fs.TakeRollbackNote()?.Refused == "0.6.9" && !File.Exists(fs.NotePath) && fs.TakeRollbackNote() == null);

            // ---- the file swap
            string dir = Path.Combine(TestFolder, "rollback");
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);
            string exe = Path.Combine(dir, "WinNotch.exe");
            File.WriteAllText(exe, "nou"); File.WriteAllText(Rollback.OldPath(exe), "vechi");
            bool released = false;
            bool sw = Rollback.Swap(exe, () => released = true, null);
            Check("RB1", "Revenire: WinNotch.exe → WinNotch.rejected.exe, WinNotch.old.exe → WinNotch.exe",
                  sw && released && File.ReadAllText(exe) == "vechi" && File.ReadAllText(Rollback.RejectedPath(exe)) == "nou" && !File.Exists(Rollback.OldPath(exe)));
            string logged = null;
            bool sw2 = Rollback.Swap(exe, () => released = false, l => logged = l);
            Check("RB2", "WinNotch.old.exe lipsă: nimic schimbat, doar un rând în log", !sw2 && released && File.ReadAllText(exe) == "vechi" && logged != null);
            File.WriteAllText(exe, "curent"); File.WriteAllText(Rollback.OldPath(exe), "vechi");
            if (File.Exists(Rollback.RejectedPath(exe))) File.Delete(Rollback.RejectedPath(exe));
            bool sw3 = Rollback.Swap(exe, () => File.Delete(Rollback.OldPath(exe)), null);      // old.exe disappears mid-way
            Check("RB3", "Eșec la jumătatea schimbării: WinNotch.exe rămâne versiunea curentă", !sw3 && File.Exists(exe) && File.ReadAllText(exe) == "curent");
        }

        /// <summary>Queues the UI work instead of running it (as when the UI thread is busy); <see cref="RunQueued"/> runs it later.</summary>
        sealed class LateUiDispatcher : IUiDispatcher
        {
            private Func<System.Threading.Tasks.Task<ActionResult>> _queued;
            public System.Threading.Tasks.Task<ActionResult> InvokeAsync(Func<System.Threading.Tasks.Task<ActionResult>> work) { _queued = work; return new System.Threading.Tasks.TaskCompletionSource<ActionResult>().Task; }
            public ActionResult RunQueued() => _queued?.Invoke().GetAwaiter().GetResult();
        }

        sealed class ThrowingUiDispatcher : IUiDispatcher
        {
            public System.Threading.Tasks.Task<ActionResult> InvokeAsync(Func<System.Threading.Tasks.Task<ActionResult>> work) => throw new InvalidOperationException("dispatcher oprit");
        }

        /// <summary>A provider that can fail once, like a device scan that hits a passing error.</summary>
        sealed class FlakyProvider : IActionProvider
        {
            public bool FailNext; public int Reads; public string Id = "x.dyn";
            public event Action Changed;
            public void Invalidate() => Changed?.Invoke();
            public IEnumerable<ActionDescriptor> GetActions()
            {
                Reads++;
                if (FailNext) { FailNext = false; throw new System.IO.IOException(); }
                return new[] { new ActionDescriptor(Id, "Dinamică", (a, ct) => ActionResult.OkTask()) };
            }
        }

        /// <summary>Records what the built-in actions call (no Windows, no WPF).</summary>
        sealed class FakeBuiltInHost : IBuiltInHost
        {
            public readonly List<string> Calls = new List<string>();
            public int Volume { get; set; } = 40;
            public void SetVolume(int p) { Volume = p; Calls.Add("vol " + p); }
            public bool Muted { get; set; }
            public void ToggleMute() { Muted = !Muted; Calls.Add("mute"); }
            public bool MicMuted { get; set; }
            public bool HasMedia { get; set; } = true;
            public void PlayPause() => Calls.Add("play");
            public void Next() => Calls.Add("next");
            public void Previous() => Calls.Add("prev");
            public void ScreenshotFull() => Calls.Add("shot");
            public void ScreenshotArea() => Calls.Add("area");
            public void TextFromScreen() => Calls.Add("ocr");
            public void FreeMemory() => Calls.Add("ram");
            public bool HasTargetWindow { get; set; } = true;
            public int MonitorCount { get; set; } = 1;
            public void Window(WindowCommand c) => Calls.Add("win " + c);
            public List<string> Spaces = new List<string> { "Lucru", "Seară" };
            public IReadOnlyList<string> WorkspaceNames() => Spaces;
            public System.Threading.Tasks.Task OpenWorkspaceAsync(string n) { Calls.Add("ws " + n); return System.Threading.Tasks.Task.CompletedTask; }
            public List<DriveItem> Drives = new List<DriveItem>();
            public IReadOnlyList<DriveItem> RemovableDrives() => Drives;
            public bool Eject(string root) { Calls.Add("eject " + root); return true; }
            public void OpenUri(string uri) => Calls.Add("uri " + uri);
            public void OpenNotch() => Calls.Add("open");
            public void OpenSettings() => Calls.Add("settings");
            public void StartSpeedTest() => Calls.Add("speed");
        }

        /// <summary>P11: the action registry (search, checks, timeouts, UI thread, providers, log) and the built-in actions.</summary>
        static void ActionTests()
        {
            ActionResult Run(ActionRegistry r, string id, Dictionary<string, string> args = null, ActionInvoker inv = ActionInvoker.CommandBar, CancellationToken ct = default, bool confirmed = false) =>
                r.InvokeAsync(id, args, inv, ct, confirmed).GetAwaiter().GetResult();
            ActionDescriptor A(string id, string title, string[] aliases = null, string cat = "", Func<bool> avail = null) =>
                new ActionDescriptor(id, title, (a, ct) => ActionResult.OkTask("ok"), avail) { Aliases = aliases ?? Array.Empty<string>(), Category = cat };

            var logs = new List<string>();
            var r = new ActionRegistry(log: logs.Add);
            r.Register(A("audio.mute-mic", "Mută microfonul", new[] { "mute mic", "taie microfonul" }, "Sunet"));
            r.Register(A("audio.mute", "Mut", new[] { "mute" }, "Sunet"));
            r.Register(A("tools.screenshot", "Captură ecran", new[] { "screenshot" }, "Unelte"));
            r.Register(A("media.play-pause", "Redă / pune pe pauză", new[] { "play" }, "Muzică"));
            r.Register(A("window.mini", "Fereastra mică în colț", new[] { "mini" }, "Fereastra activă"));

            bool dupThrows = false; try { r.Register(A("audio.mute", "Altceva")); } catch (InvalidOperationException) { dupThrows = true; }
            Check("AR1", "Id dublu → excepție la înregistrare", dupThrows);
            bool badIds = new[] { "Audio.Mute", "audio", "audio.mute.x", "audio. mute", "audio.mute_mic", "" }.All(id => { try { r.Register(A(id, "x")); return false; } catch (ArgumentException) { return true; } });
            Check("AR2", "Id în alt format decât „zonă.verb” → refuzat", badIds);
            Check("AR3", "Get pe un id inexistent → null; InvokeAsync → Failed, fără excepție", r.Get("nu.exista") == null && !Run(r, "nu.exista").Success && !Run(r, null).Success);

            Check("AR4", "„muta” găsește „Mută microfonul” (fără diacritice, fără majuscule)", r.Search("muta", ActionInvoker.CommandBar).Any(a => a.Id == "audio.mute-mic"));
            Check("AR5", "Aliasul englez găsește acțiunea", r.Search("screenshot", ActionInvoker.CommandBar).FirstOrDefault()?.Id == "tools.screenshot");
            var order = r.Search("mut", ActionInvoker.CommandBar).Select(a => a.Id).ToList();
            Check("AR6", "Ordine: exactă („Mut”) > început de cuvânt („Mută microfonul”)", order.Count >= 2 && order[0] == "audio.mute" && order[1] == "audio.mute-mic", string.Join(",", order));
            Check("AR7", "Ordine: subșir > fuzzy; fuzzy găsește literele în ordine", ActionRegistry.Score("capturaecran", "ecran") == 200 && ActionRegistry.Score("captura ecran", "cpe") == 100 &&
                  ActionRegistry.Score("captura ecran", "ecran") == 300 && ActionRegistry.Score("mut", "mut") == 400 && ActionRegistry.Score("mut", "xyz") == 0);
            var r2 = new ActionRegistry();
            r2.Register(A("x.alpha", "Deschide alfa")); r2.Register(A("x.beta", "Deschide beta"));
            Run(r2, "x.beta");
            Check("AR8", "La egalitate, cele folosite recent urcă", r2.Search("deschide", ActionInvoker.CommandBar).First().Id == "x.beta");
            Check("AR9", "Numărul maxim de rezultate e respectat", r.Search("a", ActionInvoker.CommandBar, 2).Count == 2 && r.Search("e", ActionInvoker.CommandBar, 0).Count == 0);
            Run(r, "window.mini"); Run(r, "tools.screenshot");
            var recent = r.Search("", ActionInvoker.CommandBar).Select(a => a.Id).ToList();
            Check("AR10", "Text gol → cele folosite recent, cea mai nouă prima", recent.SequenceEqual(new[] { "tools.screenshot", "window.mini" }), string.Join(",", recent));
            var r3 = new ActionRegistry();
            for (int i = 0; i < 60; i++) { r3.Register(A("x.a" + i, "A" + i)); Run(r3, "x.a" + i); }
            Check("AR11", "Istoricul de utilizare: maximum 50, doar în memorie", r3.Recent.Count == 50 && r3.Recent[0] == "x.a59");

            // who may call
            var onlyUi = new ActionDescriptor("x.ui-only", "Doar din interfață", (a, ct) => ActionResult.OkTask()) { AllowedInvokers = ActionInvoker.UI };
            r.Register(onlyUi);
            Check("AR12", "Invoker nepermis → Failed (și nu apare la căutare)", !Run(r, "x.ui-only", inv: ActionInvoker.LocalApi).Success && !Run(r, "x.ui-only").Success && Run(r, "x.ui-only", inv: ActionInvoker.UI).Success &&
                  !r.Search("doar", ActionInvoker.CommandBar).Any());
            bool apiRefused = false;
            try { r.Register(new ActionDescriptor("x.eject", "Scoate", (a, ct) => ActionResult.OkTask()) { Safety = ActionSafety.Confirm, AllowedInvokers = ActionInvoker.Default | ActionInvoker.LocalApi }); }
            catch (ArgumentException) { apiRefused = true; }
            r.Register(new ActionDescriptor("x.safe-api", "Sigură", (a, ct) => ActionResult.OkTask()) { AllowedInvokers = ActionInvoker.Default | ActionInvoker.LocalApi });
            Check("AR13", "Acțiune ne-sigură cu LocalApi → refuzată la înregistrare; una sigură e acceptată", apiRefused && Run(r, "x.safe-api", inv: ActionInvoker.LocalApi).Success);
            Check("AR13b", "Implicit nimeni din API-ul local", !Run(r, "audio.mute", inv: ActionInvoker.LocalApi).Success);

            // availability and feature flags
            r.Register(new ActionDescriptor("x.usb", "Scoate stick", (a, ct) => ActionResult.OkTask(), () => false) { UnavailableMessage = "Nu e niciun stick conectat." });
            var na = Run(r, "x.usb");
            Check("AR14", "IsAvailable fals → Failed cu mesaj", !na.Success && na.Message == "Nu e niciun stick conectat.");
            var flags = new FeatureFlags(new AppSettings().Features, new[] { new FeatureInfo("demo-x", "Demo", "d", FeatureStage.Experimental, false) });
            var rf = new ActionRegistry(flags);
            rf.Register(new ActionDescriptor("x.flagged", "Cu comutator", (a, ct) => ActionResult.OkTask()) { FeatureId = "demo-x" });
            bool offFails = !Run(rf, "x.flagged").Success && rf.Search("comutator", ActionInvoker.CommandBar).Count == 0;
            flags.Set("demo-x", true);
            Check("AR15", "FeatureId oprit → Failed (și ascunsă); pornit → merge", offFails && Run(rf, "x.flagged").Success);

            // parameters
            var rp = new ActionRegistry(log: logs.Add);
            rp.Register(new ActionDescriptor("x.params", "Cu parametri", (a, ct) => ActionResult.OkTask(a.GetInt("v") + "|" + a.GetText("mod") + "|" + a.GetText("t")))
            {
                Parameters = new[] { ActionParameter.Percent("v", "Volum"), ActionParameter.Enum("mod", "Mod", "stanga", "dreapta"), ActionParameter.Text("t", "Text", 5) }
            });
            Dictionary<string, string> P(string v, string mod, string t) => new Dictionary<string, string> { ["v"] = v, ["mod"] = mod, ["t"] = t };
            var good = Run(rp, "x.params", P("40%", "Dreapta", "abc"));
            Check("AR16", "Parametri valizi: convertiți (40% → 40, Enum fără majuscule)", good.Success && good.Message == "40|dreapta|abc", good.Message);
            Check("AR17", "Percent 150 → Failed; număr invalid → Failed", !Run(rp, "x.params", P("150", "stanga", "a")).Success && !Run(rp, "x.params", P("abc", "stanga", "a")).Success && !Run(rp, "x.params", P("-1", "stanga", "a")).Success);
            Check("AR18", "Enum necunoscut → Failed", !Run(rp, "x.params", P("10", "sus", "a")).Success);
            Check("AR19", "Text prea lung → Failed; parametru lipsă sau necunoscut → Failed",
                  !Run(rp, "x.params", P("10", "stanga", "abcdef")).Success && !Run(rp, "x.params", new Dictionary<string, string> { ["v"] = "10", ["mod"] = "stanga" }).Success &&
                  !Run(rp, "x.params", new Dictionary<string, string> { ["v"] = "10", ["mod"] = "stanga", ["t"] = "a", ["x"] = "1" }).Success);
            logs.Clear();
            Run(rp, "x.params", P("77", "stanga", "SECRET"));
            Check("AR20", "Logul are doar id, invoker, rezultat — niciodată valorile parametrilor",
                  logs.Count == 1 && logs[0].Contains("x.params") && logs[0].Contains("CommandBar") && !logs.Any(l => l.Contains("SECRET") || l.Contains("77")), string.Join(" / ", logs));

            // errors, timeout, cancellation
            var flagLog = new List<string>();
            var rflags2 = new FeatureFlags(new AppSettings().Features, new[] { new FeatureInfo("demo-e", "Demo", "d", FeatureStage.Stable, true) }, log: flagLog.Add);
            var re = new ActionRegistry(rflags2, log: logs.Add);
            re.Register(new ActionDescriptor("x.throws", "Aruncă", (a, ct) => throw new InvalidOperationException("C:\\\\secret")) { FeatureId = "demo-e" });
            re.Register(new ActionDescriptor("x.throws-async", "Aruncă async", async (a, ct) => { await System.Threading.Tasks.Task.Yield(); throw new FormatException(); }) { FeatureId = "demo-e" });
            logs.Clear();
            bool noThrow = true; ActionResult e1 = null, e2 = null;
            try { e1 = Run(re, "x.throws"); e2 = Run(re, "x.throws-async"); } catch { noThrow = false; }
            Check("AR21", "Excepție în ExecuteAsync → Failed + ReportError(FeatureId) (doar tipul în log)",
                  noThrow && !e1.Success && !e2.Success && flagLog.Count(l => l.Contains("demo-e")) == 2 && !logs.Any(l => l.Contains("secret")), string.Join(" / ", flagLog));
            bool cancelled = false;
            var rt = new ActionRegistry();
            rt.Register(new ActionDescriptor("x.slow", "Lentă", async (a, ct) =>
            {
                try { await System.Threading.Tasks.Task.Delay(5000, ct); } catch (OperationCanceledException) { cancelled = true; throw; }
                return ActionResult.Ok();
            }) { Timeout = TimeSpan.FromMilliseconds(150) });
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var slow = Run(rt, "x.slow");
            Thread.Sleep(100);
            Check("AR22", "Timeout → Failed repede, iar anularea ajunge la acțiune", !slow.Success && slow.Message.Contains("prea mult") && sw.ElapsedMilliseconds < 2000 && cancelled);
            using (var cts = new CancellationTokenSource(100))
            {
                cancelled = false;
                var c = Run(rt, "x.slow", ct: cts.Token);
                Thread.Sleep(100);
                Check("AR23", "Anularea de către apelant → Failed „Anulat.” și propagată", !c.Success && c.Message == "Anulat." && cancelled);
            }

            // UI thread
            var ui = new InlineUiDispatcher();
            var ru = new ActionRegistry(ui: ui);
            ru.Register(new ActionDescriptor("x.ui", "Pe UI", (a, ct) => ActionResult.OkTask()) { RequiresUiThread = true });
            ru.Register(new ActionDescriptor("x.bg", "Fără UI", (a, ct) => ActionResult.OkTask()));
            Run(ru, "x.ui"); Run(ru, "x.bg");
            Check("AR24", "RequiresUiThread → rulează prin IUiDispatcher (celelalte nu)", ui.Calls == 1);

            // dynamic providers
            var host = new FakeBuiltInHost();
            var rd = new ActionRegistry();
            var wsp = new WorkspaceActions(host);
            rd.RegisterProvider(wsp);
            bool before = rd.Get("workspace.open-lucru") != null && rd.Get("workspace.open-seara") != null;
            host.Spaces = new List<string> { "Gaming" };
            bool cached = rd.Get("workspace.open-lucru") != null;           // cached until the provider says it changed
            wsp.Invalidate();
            Check("AR25", "Provider dinamic: după invalidare apar cele noi, cele vechi dispar",
                  before && cached && rd.Get("workspace.open-gaming") != null && rd.Get("workspace.open-lucru") == null);
            Run(rd, "workspace.open-gaming");
            host.Spaces = new List<string> { "Lucru", "lucru", "!!!" };
            rd.Refresh();
            var ids = rd.All.Select(a => a.Id).ToList();
            Check("AR26", "Spații cu nume care dau același id sau fără litere: id-uri unice și valide",
                  ids.Contains("workspace.open-lucru") && ids.Contains("workspace.open-lucru-2") && ids.Contains("workspace.open-spatiu") && host.Calls.Contains("ws Gaming"));

            // ActionInvoked
            int events = 0; string evId = null; bool evOk = true;
            r.ActionInvoked += (id, inv, ok) => { events++; evId = id; evOk = ok; };
            Run(r, "tools.screenshot");
            bool once = events == 1 && evId == "tools.screenshot" && evOk;
            Run(r, "x.usb");
            Check("AR27", "ActionInvoked o dată per invocare (și pentru eșecuri)", once && events == 2 && evId == "x.usb" && !evOk);
            r.ActionInvoked += (id, inv, ok) => throw new Exception();
            bool survives; try { survives = Run(r, "tools.screenshot").Success; } catch { survives = false; }
            Check("AR28", "Un abonat la ActionInvoked care dă eroare nu strică invocarea", survives);

            // ---- R1 fixes: cancelled token, dispatcher, null task, provider errors, bad timeout, null aliases, confirmation
            int ran = 0;
            var rc = new ActionRegistry(ui: new InlineUiDispatcher());
            rc.Register(new ActionDescriptor("x.count", "Numără", (a, ct) => { ran++; return ActionResult.OkTask(); }));
            rc.Register(new ActionDescriptor("x.count-ui", "Numără pe UI", (a, ct) => { ran++; return ActionResult.OkTask(); }) { RequiresUiThread = true });
            using (var done = new CancellationTokenSource())
            {
                done.Cancel();
                var c1 = Run(rc, "x.count", ct: done.Token); var c2 = Run(rc, "x.count-ui", ct: done.Token);
                Check("AR29", "Token deja anulat → Failed „Anulat.”, acțiunea nu rulează (nici pe UI)", !c1.Success && !c2.Success && c1.Message == "Anulat." && ran == 0);
            }
            var lateUi = new LateUiDispatcher();
            var rl = new ActionRegistry(ui: lateUi);
            rl.Register(new ActionDescriptor("x.late", "Târzie", (a, ct) => { ran++; return ActionResult.OkTask(); }) { RequiresUiThread = true, Timeout = TimeSpan.FromMilliseconds(50) });
            var late = Run(rl, "x.late");
            bool lateNoThrow = true; ActionResult lateRun = null;
            try { lateRun = lateUi.RunQueued(); } catch { lateNoThrow = false; }
            Check("AR30", "Timeout cât acțiunea aștepta firul UI: Failed, iar când UI-ul ajunge la ea, nu mai pornește", !late.Success && lateNoThrow && ran == 0 && lateRun != null && !lateRun.Success);
            var rthrow = new ActionRegistry(ui: new ThrowingUiDispatcher());
            rthrow.Register(new ActionDescriptor("x.ui2", "Pe UI", (a, ct) => ActionResult.OkTask()) { RequiresUiThread = true });
            var rn = new ActionRegistry();
            rn.Register(new ActionDescriptor("x.null-task", "Task null", (a, ct) => null));
            rn.Register(new ActionDescriptor("x.null-result", "Rezultat null", (a, ct) => System.Threading.Tasks.Task.FromResult<ActionResult>(null)));
            bool noThrow2 = true; ActionResult d1 = null, d2 = null, d3 = null;
            try { d1 = Run(rthrow, "x.ui2"); d2 = Run(rn, "x.null-task"); d3 = Run(rn, "x.null-result"); } catch { noThrow2 = false; }
            Check("AR31", "Dispatcher care aruncă, task null sau rezultat null → Failed, fără excepție", noThrow2 && !d1.Success && !d2.Success && !d3.Success);
            var flaky = new FlakyProvider();
            var rpv = new ActionRegistry();
            rpv.RegisterProvider(flaky);
            flaky.FailNext = true;
            bool firstEmpty = rpv.Get("x.dyn") == null;
            bool secondOk = rpv.Get("x.dyn") != null;                 // the error wasn't kept in the cache
            int reads = flaky.Reads;
            rpv.Get("x.dyn");
            bool cachedNow = flaky.Reads == reads;
            flaky.Id = "x.dyn2"; flaky.Invalidate();
            Check("AR32", "Provider care aruncă o dată: rezultatul gol nu rămâne în cache; după succes e păstrat, iar Invalidate îl golește",
                  firstEmpty && secondOk && cachedNow && rpv.Get("x.dyn2") != null && rpv.Get("x.dyn") == null);
            bool zero = false, neg = false, huge = false;
            try { rn.Register(new ActionDescriptor("x.t0", "T", (a, ct) => ActionResult.OkTask()) { Timeout = TimeSpan.Zero }); } catch (ArgumentException) { zero = true; }
            try { rn.Register(new ActionDescriptor("x.t1", "T", (a, ct) => ActionResult.OkTask()) { Timeout = TimeSpan.FromSeconds(-1) }); } catch (ArgumentException) { neg = true; }
            try { rn.Register(new ActionDescriptor("x.t2", "T", (a, ct) => ActionResult.OkTask()) { Timeout = TimeSpan.FromDays(100) }); } catch (ArgumentException) { huge = true; }
            Check("AR33", "Timeout zero, negativ sau uriaș → refuzat la înregistrare", zero && neg && huge && rn.Get("x.t0") == null);
            rn.Register(new ActionDescriptor("x.no-alias", "Fără aliasuri", (a, ct) => ActionResult.OkTask()) { Aliases = null, Parameters = null });
            bool searchOk; try { searchOk = rn.Search("alias", ActionInvoker.CommandBar).Any(a => a.Id == "x.no-alias") && Run(rn, "x.no-alias").Success; } catch { searchOk = false; }
            Check("AR34", "Aliases sau Parameters null → liste goale; căutarea și pornirea merg", searchOk && rn.Get("x.no-alias").Aliases.Count == 0);
            int ejected = 0;
            var rcf = new ActionRegistry();
            rcf.Register(new ActionDescriptor("x.confirm", "Cu confirmare", (a, ct) => { ejected++; return ActionResult.OkTask(); }) { Safety = ActionSafety.Confirm });
            var noYes = Run(rcf, "x.confirm");
            var wf = Run(rcf, "x.confirm", inv: ActionInvoker.Workflow, confirmed: true);
            var yes = Run(rcf, "x.confirm", confirmed: true);
            Check("AR35", "Confirm fără confirmare → Failed și nu rulează; Workflow nu are voie implicit; cu confirmed=true rulează",
                  !noYes.Success && noYes.Message.Contains("confirmare") && !wf.Success && yes.Success && ejected == 1 &&
                  (rcf.Get("x.confirm").AllowedInvokers & ActionInvoker.Workflow) == 0);
            rcf.Register(new ActionDescriptor("x.confirm-wf", "Cu confirmare, workflow", (a, ct) => ActionResult.OkTask()) { Safety = ActionSafety.Confirm, AllowedInvokers = ActionInvoker.Workflow });
            Check("AR36", "Workflow poate fi permis explicit unei acțiuni cu confirmare, dar tot cere confirmed=true",
                  !Run(rcf, "x.confirm-wf", inv: ActionInvoker.Workflow).Success && Run(rcf, "x.confirm-wf", inv: ActionInvoker.Workflow, confirmed: true).Success);

            // ---- built-in actions
            var bh = new FakeBuiltInHost { Drives = new List<DriveItem> { new DriveItem { Root = "E:\\", Name = "SanDisk" } } };
            var rb = new ActionRegistry();
            bool regOk = true; try { BuiltInActions.Register(rb, bh); } catch { regOk = false; }
            var all = rb.All;
            Check("BA1", "Toate acțiunile incluse: id unic și valid, titlu, cel puțin un alias, iconiță, categorie",
                  regOk && all.Select(a => a.Id).Distinct().Count() == all.Count && all.All(a => ActionRegistry.IsValidId(a.Id) && a.Title.Length > 0 && a.Aliases.Count > 0 && a.Icon.Length > 0 && a.Category.Length > 0),
                  "count=" + all.Count);
            var expected = new[] { "audio.volume-set", "audio.mute", "audio.mute-mic", "media.play-pause", "media.next", "media.previous", "tools.screenshot", "tools.screenshot-area",
                "tools.ocr", "tools.free-ram", "window.topmost", "window.next-monitor", "window.half", "window.mini", "settings.bluetooth", "settings.sound", "settings.display",
                "settings.wifi", "settings.update", "winnotch.open", "winnotch.settings", "winnotch.speed-test", "workspace.open-lucru", "workspace.open-seara", "device.eject-e" };
            Check("BA2", "Lista completă: audio, media, unelte, fereastră, spații, dispozitive, setări Windows, WinNotch", expected.All(id => rb.Get(id) != null), string.Join(",", expected.Where(id => rb.Get(id) == null)));
            Check("BA3", "Scoaterea USB cere confirmare; nimic periculos; nimic deschis API-ului local",
                  rb.Get("device.eject-e").Safety == ActionSafety.Confirm && all.Count(a => a.Safety != ActionSafety.Safe) == 1 &&
                  (rb.Get("device.eject-e").AllowedInvokers & ActionInvoker.Workflow) == 0 && (rb.Get("audio.mute").AllowedInvokers & ActionInvoker.Workflow) != 0 &&
                  all.All(a => a.Safety != ActionSafety.Dangerous && (a.AllowedInvokers & ActionInvoker.LocalApi) == 0));
            Check("BA4", "Titlurile sunt în română (diacritice corecte, fără ş/ţ cu sedilă)", all.All(a => !a.Title.Contains('ş') && !a.Title.Contains('ţ')) && all.Count(a => a.Title.Any(ch => "ăâîșț".Contains(char.ToLowerInvariant(ch)))) >= 10);
            var vol = Run(rb, "audio.volume-set", new Dictionary<string, string> { ["valoare"] = "70" });
            bool undone = ((IUndoableAction)rb.Get("audio.volume-set")).UndoAsync(vol.Undo, default).GetAwaiter().GetResult().Success && bh.Volume == 40;
            Check("BA5", "Volumul: setat la 70%, iar Undo îl readuce la valoarea dinainte (40%)", vol.Success && bh.Calls.Contains("vol 70") && vol.Undo?.State == "40" && undone);
            var mic = Run(rb, "audio.mute-mic");
            bool micUndo = bh.MicMuted && ((IUndoableAction)rb.Get("audio.mute-mic")).UndoAsync(mic.Undo, default).GetAwaiter().GetResult().Success && !bh.MicMuted;
            var wrongTok = ((IUndoableAction)rb.Get("audio.mute")).UndoAsync(vol.Undo, default).GetAwaiter().GetResult();
            Check("BA6", "Microfonul: Undo îl readuce; un token al altei acțiuni e refuzat", micUndo && !wrongTok.Success);
            bh.HasMedia = false; bh.HasTargetWindow = true; bh.MonitorCount = 1;
            Check("BA7", "Disponibilitate: media fără sursă și „monitorul 2” cu un singur monitor → Failed cu mesaj",
                  !Run(rb, "media.next").Success && Run(rb, "media.next").Message.Length > 0 && !Run(rb, "window.next-monitor").Success && Run(rb, "window.mini").Success);
            bool ejectNeedsYes = !Run(rb, "device.eject-e").Success && !bh.Calls.Contains("eject E:\\");
            Run(rb, "settings.wifi"); Run(rb, "device.eject-e", confirmed: true); Run(rb, "winnotch.speed-test");
            Check("BA8", "Acțiunile apelează serviciile existente (setări prin ms-settings, Eject pe rădăcina unității)",
                  ejectNeedsYes && bh.Calls.Contains("uri ms-settings:network-wifi") && bh.Calls.Contains("eject E:\\") && bh.Calls.Contains("speed") && bh.Calls.Contains("win Mini"));
            bh.Drives.Clear();
            Check("BA9", "Stick scos între timp: „Scoate” devine indisponibilă", !Run(rb, "device.eject-e", confirmed: true).Success);
            Check("BA10", "Căutare reală: „muta” → microfonul, „wifi” → setările Wi-Fi, „poza ecran” → captura",
                  rb.Search("muta", ActionInvoker.CommandBar).Any(a => a.Id == "audio.mute-mic") && rb.Search("wifi", ActionInvoker.CommandBar).First().Id == "settings.wifi" &&
                  rb.Search("poza ecran", ActionInvoker.CommandBar).First().Id == "tools.screenshot");
            Check("BA11", "Slug: diacritice și semne → id curat", BuiltInActions.Slug("Lucru de acasă!") == "lucru-de-acasa" && BuiltInActions.Slug("  Ședință / Zoom  ") == "sedinta-zoom" && BuiltInActions.Slug("???") == "");
        }

        static bool SafeCalc(string q) { try { Launcher.Calculate(q); return true; } catch { return false; } }
    }
}
