using System;
using System.Collections.Generic; using System.IO; using System.Linq; using System.Net; using System.Net.Sockets;
using System.Text; using System.Threading; using System.Text.Json; using WinNotch.Services; using WinNotch.Widgets;
namespace WinNotch
{
    public static class App { public static void Log(string s) { } public static bool IsAdmin => false; }
    public class AppSettings { public static string Folder => T.TestFolder; public static bool SafeToWrite(string p) => true; public static IDisposable HoldFolder() => null; }

    /// <summary>
    /// Automated tests for the parts that don't need the WPF window: the extension bridge (real sockets), address
    /// safety, site names, the speed verdict, the calculator and the calendar. Run: tests\run-tests.bat
    /// </summary>
    public static class T
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

            Console.WriteLine(string.Join("\n", lines));
            Console.WriteLine($"\nTOTAL {pass + fail}: {pass} PASS, {fail} FAIL");
            Environment.ExitCode = fail == 0 ? 0 : 1;
        }

        static bool SafeCalc(string q) { try { Launcher.Calculate(q); return true; } catch { return false; } }
    }
}
