using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using WinNotch.Core.Actions;
using WinNotch.Core.Flags;
using WinNotch.Features.CommandBar;
using WinNotch.Features.SmartClipboard;
using WinNotch.Features.Smoke;

namespace WinNotch
{
    public static partial class T
    {
        /// <summary>The app's side of the "clipboard.*" actions, faked: what was put in the clipboard, opened, asked.</summary>
        sealed class FakeClipHost : ISmartClipboardHost
        {
            public string CurrentText { get; set; }
            public readonly List<string> Written = new List<string>(), Urls = new List<string>(), Folders = new List<string>();
            public bool Busy;
            public string FolderAnswer;
            public bool SetText(string text) { if (Busy) return false; Written.Add(text); CurrentText = text; return true; }
            public void OpenUrl(string url) => Urls.Add(url);
            public string OpenFolder(string path) { Folders.Add(path); return FolderAnswer; }
        }

        static string B64Url(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        /// <summary>The same JSON value (numbers as written, keys in order), however it is indented.</summary>
        static bool SameJsonValue(string a, string b)
        {
            try
            {
                using var x = JsonDocument.Parse(a);
                using var y = JsonDocument.Parse(b);
                return JsonSerializer.Serialize(x.RootElement) == JsonSerializer.Serialize(y.RootElement);
            }
            catch (JsonException) { return false; }
        }

        static SmartClipKind KindOf(string s) => SmartClipRecognizer.Recognize(s).Kind;

        /// <summary>
        /// P21 "Smart Clipboard": recognition of every kind (and the look-alikes that aren't), the priority order, the
        /// limits, the URL cleaning byte for byte, JSON / JWT, the password managers' markers, the chips, the actions
        /// through the registry, the settings, the smoke-test commands and the WPF hooks pinned in the source.
        /// </summary>
        static void SmartClipboardTests()
        {
            // ---- the switch
            var info = FeatureCatalog.Find(SmartClipboardActions.FeatureId);
            var onStore = AppSettings.NewFeatures(); onStore[SmartClipboardActions.FeatureId] = true;
            Check("SC1", "Comutatorul „smart-clipboard”: în catalog, Experimental, oprit implicit, oprit în mod sigur chiar dacă e pornit; același id ca FeatureCatalog.SmartClipboard",
                  info != null && FeatureCatalog.SmartClipboard == SmartClipboardActions.FeatureId && info.Stage == FeatureStage.Experimental && !info.DefaultOn &&
                  !new FeatureFlags(AppSettings.NewFeatures()).IsEnabled(SmartClipboardActions.FeatureId) && new FeatureFlags(onStore).IsEnabled(SmartClipboardActions.FeatureId) &&
                  !new FeatureFlags(onStore, safeMode: true).IsEnabled(SmartClipboardActions.FeatureId) && info.Name == "Smart Clipboard" && info.Description.Contains("log"));

            // ---- JSON
            const string minJson = "{\"nume\":\"Ion\",\"oraș\":\"Brașov\",\"note\":[10,9.50,1e3],\"activ\":true,\"x\":null,\"dublu\":1,\"dublu\":2}";
            var j = SmartClipRecognizer.Recognize("  " + minJson + "\n");
            Check("SC2", "JSON: obiect recunoscut (și cu spații în jur); formatat cu 2 spații și compact, ambele echivalente; diacriticele, numerele scrise („9.50”, „1e3”), ordinea și cheile dublate rămân",
                  j.Kind == SmartClipKind.Json && j.Text == minJson && j.Minified == minJson && SameJsonValue(j.Formatted, minJson) &&
                  j.Formatted.Replace("\r\n", "\n").StartsWith("{\n  \"nume\": \"Ion\",\n  \"oraș\": \"Brașov\"", StringComparison.Ordinal) &&
                  j.Formatted.Contains("9.50") && j.Formatted.Contains("1e3") && Regex.Matches(j.Formatted, "\"dublu\"").Count == 2 &&
                  KindOf("[1, 2, {\"a\": [true]}]") == SmartClipKind.Json && KindOf("{}") == SmartClipKind.Json, j.Formatted);
            string deep64 = new string('[', 64) + new string(']', 64), deep65 = new string('[', 65) + new string(']', 65);
            var notJson = new[] { "{nu e json}", "[1,2,", "{} extra", "{\"a\":1}}", "{'a':1}", "[1,]", "{\"a\":1,}", "{/*x*/}", "{\"a\":01}", "{\"a\":NaN}", "[1] [2]", "{\"a\"}", deep65 };
            Check("SC3", "JSON fals: text între acolade, neterminat, cu ceva după, ghilimele simple, virgule în plus, comentarii, 01, NaN, două valori, prea adânc (65) → nu e JSON; 64 de niveluri merg; un număr sau un șir singur nu sunt JSON aici",
                  notJson.All(s => KindOf(s) != SmartClipKind.Json) && notJson.All(s => !SmartClipRecognizer.TryJson(s, out _, out _)) && KindOf(deep64) == SmartClipKind.Json &&
                  KindOf("42") != SmartClipKind.Json && KindOf("\"text\"") != SmartClipKind.Json && KindOf("true") != SmartClipKind.Json,
                  string.Join(" | ", notJson.Where(s => KindOf(s) == SmartClipKind.Json)));
            var chipsMin = SmartClipboardActions.ChipsFor(j);
            var chipsFmt = SmartClipboardActions.ChipsFor(SmartClipRecognizer.Recognize(j.Formatted));
            var chipsFmtLf = SmartClipboardActions.ChipsFor(SmartClipRecognizer.Recognize(j.Formatted.Replace("\r\n", "\n")));
            Check("SC4", "JSON: compact → doar „Formatează”; deja formatat (și cu \\n în loc de \\r\\n) → doar „Compactează”; „{}” → niciun chip; altfel ambele",
                  string.Join(",", chipsMin.Select(c => c.ActionId)) == SmartClipboardActions.FormatJsonId &&
                  string.Join(",", chipsFmt.Select(c => c.ActionId)) == SmartClipboardActions.MinifyJsonId &&
                  string.Join(",", chipsFmtLf.Select(c => c.ActionId)) == SmartClipboardActions.MinifyJsonId &&
                  SmartClipboardActions.ChipsFor(SmartClipRecognizer.Recognize("{}")).Count == 0 &&
                  SmartClipboardActions.ChipsFor(SmartClipRecognizer.Recognize("{ \"a\" : 1 }")).Select(c => c.ActionId).SequenceEqual(new[] { SmartClipboardActions.FormatJsonId, SmartClipboardActions.MinifyJsonId }));

            // ---- URL
            var goodUrls = new[] { "https://exemplu.ro", "http://exemplu.ro/a?b=1#c", "HTTPS://Exemplu.ro/Pagina", "https://localhost:8080/x", "https://[::1]/x", "https://192.168.1.10/admin", "https://ro.wikipedia.org/wiki/Bra%C8%99ov" };
            var badUrls = new[] { "http://", "https://exa mple.com", "htp://x.ro", "www.exemplu.ro", "exemplu.ro", "ftp://x.ro/a", "javascript:alert(1)", "file:///C:/x", "https://-", "https://intranet", "https://x.ro/\ta", "https://" + new string('a', 9000) + ".ro" };
            Check("SC5", "URL: doar http:// și https:// cu o gazdă reală (cu punct, localhost sau IP); fals: fără gazdă, cu spații, altă schemă, fără schemă, prea lung",
                  goodUrls.All(u => KindOf(u) == SmartClipKind.Url) && badUrls.All(u => KindOf(u) != SmartClipKind.Url),
                  "bune refuzate: " + string.Join(" ", goodUrls.Where(u => KindOf(u) != SmartClipKind.Url)) + " · rele primite: " + string.Join(" ", badUrls.Where(u => KindOf(u) == SmartClipKind.Url)));

            string Clean(string u) => SmartClipRecognizer.CleanUrl(u, out _);
            const string plain = "https://exemplu.ro/caută?q=a%20b&pagina=2&Q=Ă#sus";
            string same = SmartClipRecognizer.CleanUrl(plain, out int none);
            Check("SC6", "Curățarea URL: fără parametri de urmărire → exact același șir (aceeași referință, 0 scoși)",
                  ReferenceEquals(same, plain) && none == 0 && Clean("https://x.ro/") == "https://x.ro/" && Clean("https://x.ro/?") == "https://x.ro/?" &&
                  Clean("https://x.ro/?a=1&&b=2") == "https://x.ro/?a=1&&b=2");
            string mixed = SmartClipRecognizer.CleanUrl("https://x.ro/p?utm_source=nl&id=7&fbclid=IwAR0x&q=a%20b%C4%83&utm_medium=email&sort=desc#sectiunea-2", out int removedMixed);
            Check("SC7", "Curățarea URL amestecată: scoate doar utm_*, fbclid…; restul rămâne octet cu octet: ordinea, codificarea (%20, %C4%83), fragmentul",
                  mixed == "https://x.ro/p?id=7&q=a%20b%C4%83&sort=desc#sectiunea-2" && removedMixed == 3, mixed);
            Check("SC8", "Curățarea URL: doar parametri de urmărire → fără „?” (fragmentul rămâne, și dacă arată ca un parametru); fără valoare („?fbclid”) e scos; „?&utm_source=x” → fără „?”",
                  Clean("https://x.ro/a?utm_source=x&utm_campaign=y") == "https://x.ro/a" && Clean("https://x.ro/a?gclid=1#utm_source=frag") == "https://x.ro/a#utm_source=frag" &&
                  Clean("https://x.ro/a?fbclid") == "https://x.ro/a" && Clean("https://x.ro/?&utm_source=x") == "https://x.ro/" &&
                  Clean("https://x.ro/a?b=1#c?utm_source=2") == "https://x.ro/a?b=1#c?utm_source=2");
            var listed = new[] { "utm_source", "utm_medium", "utm_campaign", "utm_term", "utm_content", "utm_id", "fbclid", "gclid", "dclid", "gbraid", "wbraid", "msclkid",
                                 "mc_cid", "mc_eid", "yclid", "igshid", "igsh", "_hsenc", "_hsmi", "mkt_tok", "ref_src", "twclid", "ttclid", "li_fat_id" };
            var kept = new[] { "UTM_SOURCE", "Utm_source", "Fbclid", "GCLID", "utm_", "utm", "ref", "source", "id", "q", "si", "_ga", "fbclid2", "xutm_source", "utm%5Fsource" };
            Check("SC9", "Lista de urmărire: fiecare parametru din listă (și orice utm_…) e scos; majusculele contează (UTM_SOURCE, Fbclid rămân: ar putea fi ai site-ului), „utm_” gol, „si”, „_ga”, numele codificate rămân",
                  listed.All(SmartClipRecognizer.IsTrackingParameter) && kept.All(k => !SmartClipRecognizer.IsTrackingParameter(k)) &&
                  listed.All(p => Clean("https://x.ro/?a=1&" + p + "=v") == "https://x.ro/?a=1") && kept.All(p => Clean("https://x.ro/?" + p + "=v") == "https://x.ro/?" + p + "=v") &&
                  SmartClipRecognizer.TrackingParameters.Count == 18 && SmartClipRecognizer.TrackingPrefix == "utm_");
            var tracked = SmartClipRecognizer.Recognize("https://x.ro/a?utm_source=x&b=2");
            var clean = SmartClipRecognizer.Recognize("https://x.ro/a?b=2");
            Check("SC10", "Chip-urile unui link: cu urmărire → „Curăță link-ul” și „Deschide”; fără → doar „Deschide”",
                  tracked.TrackingRemoved == 1 && tracked.Normalized == "https://x.ro/a?b=2" &&
                  SmartClipboardActions.ChipsFor(tracked).Select(c => c.ActionId).SequenceEqual(new[] { SmartClipboardActions.CleanUrlId, SmartClipboardActions.OpenUrlId }) &&
                  SmartClipboardActions.ChipsFor(clean).Select(c => c.ActionId).SequenceEqual(new[] { SmartClipboardActions.OpenUrlId }));

            // ---- e-mail
            var em = SmartClipRecognizer.Recognize("Ion.Pop+notch@Exemplu.RO");
            var badMail = new[] { "a@b", "@b.ro", "a@", "a@@b.ro", "a@b@c.ro", "a@b..ro", "a b@c.ro", "a@-b.ro", "a@b-.ro", "a@b.r0", "a@b.r", ".a@b.ro", "a.@b.ro", "a..b@c.ro", "a@b_c.ro", "ion(at)exemplu.ro", "a\"b@c.ro", new string('a', 65) + "@b.ro" };
            Check("SC11", "E-mail: recunoscut, normalizat (domeniul cu litere mici, „mailto:” scos, partea locală neatinsă); fals: fără domeniu, două @, puncte greșite, spații, TLD cu cifre sau de o literă, partea locală prea lungă",
                  em.Kind == SmartClipKind.Email && em.Normalized == "Ion.Pop+notch@exemplu.ro" && SmartClipRecognizer.Recognize("mailto:a@b.ro").Normalized == "a@b.ro" &&
                  KindOf("MAILTO:x@y.ro") == SmartClipKind.Email && badMail.All(m => KindOf(m) != SmartClipKind.Email),
                  string.Join(" ", badMail.Where(m => KindOf(m) == SmartClipKind.Email)));

            // ---- colour
            var c3 = SmartClipRecognizer.Recognize("#fA0");
            var c6 = SmartClipRecognizer.Recognize("#1a2B3c");
            var c8 = SmartClipRecognizer.Recognize("#11223380");
            var badColor = new[] { "#hashtag", "#12345", "#123", "#1234", "123456", "#GGGGGG", "##fff", "#ff f", "# fff", "#1234567", "#123456789", "fff" };
            Check("SC12", "Culoare: #RGB (cu o literă), #RRGGBB, #RRGGBBAA → mostră și rgb()/rgba(); fals: #hashtag, #12345, #123 (pare număr de issue), #RGBA, fără #, cifre greșite",
                  c3.Kind == SmartClipKind.Color && c3.R == 255 && c3.G == 170 && c3.B == 0 && c3.A == 255 && c3.Normalized == "#FFAA00" &&
                  c6.Normalized == "#1A2B3C" && SmartClipRecognizer.Rgb(c6) == "rgb(26, 43, 60)" &&
                  c8.A == 0x80 && c8.Normalized == "#11223380" && SmartClipRecognizer.Rgb(c8) == "rgba(17, 34, 51, 0.5)" &&
                  SmartClipboardActions.ChipsFor(c6).Single().Swatch && badColor.All(c => KindOf(c) != SmartClipKind.Color),
                  string.Join(" ", badColor.Where(c => KindOf(c) == SmartClipKind.Color)));

            // ---- IP
            var goodIp = new Dictionary<string, string>
            {
                ["192.168.1.1"] = "192.168.1.1", ["0.0.0.0"] = "0.0.0.0", ["255.255.255.255"] = "255.255.255.255", ["::1"] = "::1",
                ["2001:DB8:0:0:0:0:0:1"] = "2001:db8::1", ["::ffff:192.0.2.1"] = "::ffff:192.0.2.1", ["fe80::1"] = "fe80::1",
            };
            var badIp = new[] { "256.1.1.1", "1.2.3", "01.2.3.4", "1", "0x7f.0.0.1", "1.2.3.4.5", "fe80::1%eth0", "12:30", "12:30:45", "std::map", "1.2.3.-4", "1.2.3.4/24", "1..2.3", "999.999.999.999", "1.2.3.4:80" };
            Check("SC13", "IP: IPv4 strict și IPv6 (normalizat: litere mici, forma scurtă); fals: >255, 3 sau 5 părți, zerouri în față, hex, zonă, ore („12:30”), cod („std::map”), port, mască",
                  goodIp.All(kv => SmartClipRecognizer.Recognize(kv.Key) is { Kind: SmartClipKind.Ip } r && r.Normalized == kv.Value) && badIp.All(i => KindOf(i) != SmartClipKind.Ip),
                  string.Join(" ", goodIp.Keys.Where(k => SmartClipRecognizer.Recognize(k).Normalized != goodIp[k]).Concat(badIp.Where(i => KindOf(i) == SmartClipKind.Ip))));

            // ---- path
            var goodPath = new Dictionary<string, string>
            {
                [@"C:\Users\Ion\Documents"] = @"C:\Users\Ion\Documents", ["\"D:\\Proiecte\\a b\\x.txt\""] = @"D:\Proiecte\a b\x.txt", ["c:/x/y"] = "c:/x/y", [@"E:\"] = @"E:\",
            };
            var badPath = new[] { "C:", "C:x", @"\\server\share", "//server/share", @"\\?\C:\x", @"C:\a|b", @"1:\x", @"CC:\x", "C:\\a\u0001b", "file://server/x", @"C:\a:b", @"C:\a<b", @"C:\a*", @"C:\a?", @"\Windows", "~/x", "C:\\" + new string('a', 1100) };
            Check("SC14", "Cale: o cale locală cu literă de unitate (și între ghilimele, și cu /); fals: fără separator, căi de rețea (\\\\server, //server, file://server), caractere interzise, două puncte în plus, relative, prea lungi",
                  goodPath.All(kv => SmartClipRecognizer.Recognize(kv.Key) is { Kind: SmartClipKind.Path } r && r.Normalized == kv.Value) && badPath.All(p => KindOf(p) != SmartClipKind.Path) &&
                  SmartClipRecognizer.IsNetworkPath(@"\\server\share") && SmartClipRecognizer.IsNetworkPath("//server/x") && SmartClipRecognizer.IsNetworkPath(@"\/server") &&
                  SmartClipRecognizer.IsNetworkPath("file://server/share/x") && !SmartClipRecognizer.IsNetworkPath(@"C:\x") && !SmartClipRecognizer.IsNetworkPath("file:///C:/x"),
                  string.Join(" ", badPath.Where(p => KindOf(p) == SmartClipKind.Path)));

            // ---- phone
            var goodPhone = new Dictionary<string, string>
            {
                ["0721 123 456"] = "0721123456", ["0721123456"] = "0721123456", ["+40 721 123 456"] = "+40721123456", ["0040-721-123-456"] = "+40721123456",
                ["(021) 312 34 56"] = "0213123456", ["+1 (555) 123-4567"] = "+15551234567", ["0721/123/456"] = "0721123456",
            };
            var badPhone = new[] { "12345", "2026-10-06", "06-10-2026", "06/10/2026", "1.234,56", "1234,56", "192.168.1.1", "123456789", "1234567890", "+0721123456", "+40", "+4072",
                                   "0721  123456", "07-21--123456", "0721 123 456 789 012 345", "07a1234567", "(0721 123456", "0721) 123456", "0721 123 456-", "-0721123456",
                                   "0721.123.456", "000123456789", "+40 (721) (123) 456", "0721()123456", "++40721123456", "1 000 000", "100 000,00 lei" };
            Check("SC15", "Telefon: național (cu 0), internațional (+ sau 00), cu spații, cratime, / și o pereche de paranteze → doar cifre (+ pentru internațional); fals: numere scurte, date, sume, IP-uri, separatoare duble, litere, puncte",
                  goodPhone.All(kv => SmartClipRecognizer.Recognize(kv.Key) is { Kind: SmartClipKind.Phone } r && r.Normalized == kv.Value) && badPhone.All(p => KindOf(p) != SmartClipKind.Phone) &&
                  KindOf("12345") == SmartClipKind.None && KindOf("2026-10-06") == SmartClipKind.None && KindOf("1.234,56") == SmartClipKind.None && KindOf("192.168.1.1") == SmartClipKind.Ip,
                  string.Join(" | ", goodPhone.Keys.Where(k => SmartClipRecognizer.Recognize(k).Normalized != goodPhone[k]).Concat(badPhone.Where(p => KindOf(p) == SmartClipKind.Phone))));

            // ---- JWT
            string h = B64Url("{\"alg\":\"HS256\",\"typ\":\"JWT\"}"), p0 = B64Url("{\"sub\":\"1234567890\",\"nume\":\"Ion Popescu\",\"iat\":1516239022}");
            const string sig = "SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c";
            string jwt = h + "." + p0 + "." + sig;
            var jw = SmartClipRecognizer.Recognize(jwt);
            JsonElement decodedRoot = default;
            bool decodedOk = false;
            try { using var dd = JsonDocument.Parse(jw.Normalized ?? ""); decodedRoot = dd.RootElement.Clone(); decodedOk = true; } catch (JsonException) { }
            Check("SC16", "JWT: decodat local (base64url) în {\"header\", \"payload\"}, indentat; semnătura nu apare; și cu semnătura goală („alg: none”)",
                  jw.Kind == SmartClipKind.Jwt && decodedOk && decodedRoot.GetProperty("header").GetProperty("alg").GetString() == "HS256" &&
                  decodedRoot.GetProperty("payload").GetProperty("nume").GetString() == "Ion Popescu" && decodedRoot.GetProperty("payload").GetProperty("iat").GetInt64() == 1516239022 &&
                  !jw.Normalized.Contains(sig) && !jw.Normalized.Contains("SflKx") && jw.Normalized.Contains("\n") &&
                  KindOf(B64Url("{\"alg\":\"none\"}") + "." + p0 + ".") == SmartClipKind.Jwt &&
                  SmartClipboardActions.ChipsFor(jw).Single().ActionId == SmartClipboardActions.DecodeJwtId, jw.Normalized);
            var badJwt = new[]
            {
                "eyJ!.eyJ.abc", h + "." + p0, h + "." + p0 + "." + sig + ".x", "." + p0 + "." + sig, h + ".." + sig, B64Url("hello") + "." + p0 + "." + sig,
                B64Url("{\"typ\":\"JWT\"}") + "." + p0 + "." + sig, B64Url("{\"alg\":5}") + "." + p0 + "." + sig, h + "." + B64Url("[1,2]") + "." + sig,
                h + "." + B64Url("\"text\"") + "." + sig, h + "." + B64Url("{\"a\":") + "." + sig, h + "=." + p0 + "." + sig, "abc.def.ghi", "1.2.3", "a.b.c", "x" + h + "." + p0 + "." + sig,
                h + "." + Convert.ToBase64String(new byte[] { 0xff, 0xfe, 0x7b, 0x7d }).TrimEnd('=') + "." + sig,
            };
            Check("SC17", "JWT fals: base64 invalid, 2 sau 4 părți, părți goale, antet care nu e JSON sau fără „alg”, conținut care nu e obiect JSON (listă, șir, neterminat, UTF-8 greșit), padding, versiuni („1.2.3”)",
                  badJwt.All(x => KindOf(x) != SmartClipKind.Jwt) && SmartClipRecognizer.FromBase64Url("a") == null && SmartClipRecognizer.FromBase64Url("ab+c") == null,
                  string.Join(" | ", badJwt.Where(x => KindOf(x) == SmartClipKind.Jwt)));

            // ---- priority and limits
            Check("SC18", "Ordinea: JSON → JWT → URL → e-mail → culoare → IP → cale → telefon; un link cu „@” e link, nu e-mail; un IP nu e telefon; JWT-ul nu e text simplu",
                  SmartClipRecognizer.Priority.SequenceEqual(new[] { SmartClipKind.Json, SmartClipKind.Jwt, SmartClipKind.Url, SmartClipKind.Email, SmartClipKind.Color, SmartClipKind.Ip, SmartClipKind.Path, SmartClipKind.Phone }) &&
                  KindOf("https://ion@exemplu.ro/x") == SmartClipKind.Url && KindOf("http://a.ro/?mail=ion@exemplu.ro") == SmartClipKind.Url && KindOf("ion@exemplu.ro") == SmartClipKind.Email &&
                  KindOf("10.0.0.1") == SmartClipKind.Ip && KindOf(jwt) == SmartClipKind.Jwt && KindOf("[\"https://x.ro\"]") == SmartClipKind.Json &&
                  KindOf("{\"a\":\"ion@exemplu.ro\"}") == SmartClipKind.Json && KindOf("Salut, ce faci?") == SmartClipKind.None && KindOf("") == SmartClipKind.None && KindOf(null) == SmartClipKind.None &&
                  KindOf("   \n\t ") == SmartClipKind.None);
            string bigJson = "[" + string.Join(",", Enumerable.Repeat("\"abcdefgh\"", 6600)) + "]";            // ~72 KB
            string okJson = "[" + string.Join(",", Enumerable.Repeat("\"abcdefgh\"", 5800)) + "]";             // ~64 KB - a bit
            Check("SC19", "Limita de mărime: peste 64 KB nimic nu e analizat (nici un JSON valid); sub limită da; textele pe mai multe rânduri sunt doar JSON (un link pe două rânduri nu e link)",
                  bigJson.Length > SmartClipRecognizer.MaxInput && KindOf(bigJson) == SmartClipKind.None && okJson.Length <= SmartClipRecognizer.MaxInput && KindOf(okJson) == SmartClipKind.Json &&
                  KindOf("https://x.ro\nhttps://y.ro") == SmartClipKind.None && KindOf("ion@x.ro\r\n") == SmartClipKind.Email && KindOf("{\n\"a\": 1\n}") == SmartClipKind.Json,
                  bigJson.Length + "/" + okJson.Length);
            var nasty = new[]
            {
                new string('(', 60000), new string('@', 60000), string.Concat(Enumerable.Repeat("a.", 30000)), "{" + new string('[', 30000), new string('#', 60000),
                "https://x.ro/?" + string.Concat(Enumerable.Repeat("utm_a=1&", 900)), string.Concat(Enumerable.Repeat("eyJh.", 12000)), new string(':', 60000), "+" + new string('0', 60000),
                "C:\\" + string.Concat(Enumerable.Repeat("a\\", 30000)), string.Concat(Enumerable.Repeat("a@b.", 15000)) + "ro",
            };
            var sw = Stopwatch.StartNew();
            var slow = new List<int>();
            for (int i = 0; i < nasty.Length; i++)
            {
                var one = Stopwatch.StartNew();
                SmartClipRecognizer.Recognize(nasty[i]);
                if (one.ElapsedMilliseconds > 500) slow.Add(i);
            }
            Check("SC20", "Intrări patologice (60 000 de paranteze, @, puncte, adâncime, „#”, „:”, căi lungi, sute de parametri): nicio excepție, fiecare sub 0,5 s (parsare manuală, fără regex)",
                  slow.Count == 0 && sw.ElapsedMilliseconds < 3000 && !NoComments(Src("Features/SmartClipboard/SmartClip.cs")).Contains("Regex"),
                  "lente: " + string.Join(",", slow) + " · total " + sw.ElapsedMilliseconds + " ms");
            var cache = new SmartClipCache();
            string ct = "#ffffff";
            var first = cache.Get(ct);
            Check("SC21", "Memoria recunoașterii: același text (aceeași referință) → același rezultat, fără a-l analiza din nou; alt text → altul; null → None",
                  ReferenceEquals(cache.Get(ct), first) && first.Kind == SmartClipKind.Color && cache.Get("ion@exemplu.ro").Kind == SmartClipKind.Email &&
                  !ReferenceEquals(cache.Get(new string(ct.ToCharArray())), first) && cache.Get(null) == SmartClip.None);

            // ---- chips and the peek
            var samples = new Dictionary<SmartClipKind, string>
            {
                [SmartClipKind.Json] = minJson, [SmartClipKind.Jwt] = jwt, [SmartClipKind.Url] = "https://x.ro/?utm_source=a", [SmartClipKind.Email] = "ion@exemplu.ro",
                [SmartClipKind.Color] = "#123456", [SmartClipKind.Ip] = "10.1.2.3", [SmartClipKind.Path] = @"C:\Windows", [SmartClipKind.Phone] = "0721 123 456",
            };
            var allChips = samples.ToDictionary(kv => kv.Key, kv => SmartClipboardActions.ChipsFor(SmartClipRecognizer.Recognize(kv.Value)));
            Check("SC22", "Chip-uri pentru fiecare tip (1–3, etichete scurte în română, acțiuni existente); text simplu → niciunul; eticheta tipului",
                  samples.All(kv => SmartClipRecognizer.Recognize(kv.Value).Kind == kv.Key) && allChips.Values.All(l => l.Count >= 1 && l.Count <= 3) &&
                  allChips.Values.SelectMany(l => l).All(c => SmartClipboardActions.AllIds.Contains(c.ActionId) && !string.IsNullOrWhiteSpace(c.Label) && c.Label.Length <= 20) &&
                  allChips.Values.SelectMany(l => l).Select(c => c.ActionId).Distinct().Count() == 9 &&
                  SmartClipboardActions.ChipsFor(SmartClip.None).Count == 0 && SmartClipboardActions.ChipsFor(null).Count == 0 &&
                  samples.Keys.All(k => SmartClipboardActions.KindLabel(k).Length > 0) && SmartClipboardActions.KindLabel(SmartClipKind.None) == "");
            var titles = samples.ToDictionary(kv => kv.Key, kv => SmartClipboardActions.PeekTitle(SmartClipRecognizer.Recognize(kv.Value)));
            Check("SC23", "Peek: titlu fix, fără conținut, doar pentru JSON de formatat, link cu urmărire și JWT; nimic pentru celelalte, pentru JSON deja formatat și link fără urmărire",
                  titles[SmartClipKind.Json] == "JSON copiat · Formatează" && titles[SmartClipKind.Url] == "Link cu urmărire copiat · Curăță" && titles[SmartClipKind.Jwt] == "Token JWT copiat · Decodează" &&
                  new[] { SmartClipKind.Email, SmartClipKind.Color, SmartClipKind.Ip, SmartClipKind.Path, SmartClipKind.Phone }.All(k => titles[k] == null) &&
                  SmartClipboardActions.PeekTitle(SmartClipRecognizer.Recognize(j.Formatted)) == null && SmartClipboardActions.PeekTitle(clean) == null &&
                  SmartClipboardActions.PeekTitle(null) == null && titles.Values.Where(t => t != null).All(t => t.Length <= Core.Activity.Activity.MaxTitle && !t.Contains("Ion") && !t.Contains("x.ro")));

            // ---- the actions
            var probe = new FakeClipHost();
            var actions = SmartClipboardActions.Create(probe);
            Check("SC24", "Acțiunile: 10, id-uri „clipboard.verb” valide și unice, titlu și aliasuri în română și engleză, Safe, comutatorul „smart-clipboard”, pe firul UI, fără parametri, categoria Clipboard",
                  actions.Count == 10 && actions.Select(a => a.Id).SequenceEqual(SmartClipboardActions.AllIds) && actions.All(a => ActionRegistry.IsValidId(a.Id) && a.Id.StartsWith("clipboard.", StringComparison.Ordinal)) &&
                  actions.Select(a => a.Id).Distinct().Count() == 10 && actions.All(a => a.Title.StartsWith("Smart Clipboard: ", StringComparison.Ordinal) && a.Aliases.Count >= 3) &&
                  actions.All(a => a.Safety == ActionSafety.Safe && a.FeatureId == "smart-clipboard" && a.RequiresUiThread && a.Parameters.Count == 0 && a.Category == "Clipboard" &&
                                   !string.IsNullOrEmpty(a.Icon) && !string.IsNullOrEmpty(a.UnavailableMessage) && (a.AllowedInvokers & ActionInvoker.LocalApi) == 0) &&
                  actions.All(a => Regex.IsMatch(a.Aliases[0], "[ăâîșț]|deschide|culoare") && Regex.IsMatch(a.Aliases[a.Aliases.Count - 1], "^[a-z ]+$")) &&
                  actions.All(a => !a.IsAvailable()));

            var host = new FakeClipHost();
            var logs = new List<string>();
            var ui = new InlineUiDispatcher();
            var reg = new ActionRegistry(new FeatureFlags(onStore), ui, logs.Add);
            SmartClipboardActions.Register(reg, host);
            ActionResult Run(string id, ActionRegistry r = null) => (r ?? reg).InvokeAsync(id, null, ActionInvoker.UI).GetAwaiter().GetResult();

            host.CurrentText = minJson;
            var fmt = Run(SmartClipboardActions.FormatJsonId);
            bool formattedPut = fmt.Success && host.Written.Count == 1 && host.Written[0] == j.Formatted && SameJsonValue(host.Written[0], minJson);
            var minifyAfter = Run(SmartClipboardActions.MinifyJsonId);               // now the formatted text is the current one
            bool minified = minifyAfter.Success && host.Written.Last() == minJson;
            var again = Run(SmartClipboardActions.MinifyJsonId);                     // already compact: not offered, refused
            Check("SC25", "Prin registru: „Formatează” pune JSON-ul formatat (echivalent) în clipboard, pe firul UI; apoi „Compactează” îl readuce; pe un JSON deja compact „Compactează” e indisponibilă",
                  formattedPut && minified && !again.Success && again.Message == "Nu ai copiat un JSON de compactat." && host.Written.Count == 2 && ui.Calls >= 2 &&
                  fmt.Message == "JSON formatat, în clipboard", fmt.Message + " / " + again.Message);

            host.CurrentText = "https://x.ro/a?utm_source=nl&b=%20&fbclid=1#f";
            var cu = Run(SmartClipboardActions.CleanUrlId);
            var ou = Run(SmartClipboardActions.OpenUrlId);
            var cuAgain = Run(SmartClipboardActions.CleanUrlId);
            host.CurrentText = "https://x.ro/a";
            Check("SC26", "Link: „Curăță” pune link-ul fără urmărire (restul neatins) și spune câți parametri a scos; „Deschide” cheamă OpenUrl cu link-ul (doar http/https); fără urmărire „Curăță” e indisponibilă",
                  cu.Success && host.Written.Last() == "https://x.ro/a?b=%20#f" && cu.Message.Contains("2 parametri") && ou.Success && host.Urls.Count == 1 && host.Urls[0] == "https://x.ro/a?b=%20#f" &&
                  !cuAgain.Success && !reg.Get(SmartClipboardActions.CleanUrlId).IsAvailable() && reg.Get(SmartClipboardActions.OpenUrlId).IsAvailable(), cu.Message);

            int written = host.Written.Count;
            host.CurrentText = jwt; var dj = Run(SmartClipboardActions.DecodeJwtId); string djText = host.Written.Last();
            host.CurrentText = "#1a2b3c"; var cc = Run(SmartClipboardActions.CopyColorId); string ccText = host.Written.Last();
            host.CurrentText = "mailto:Ion@Exemplu.RO"; var ce = Run(SmartClipboardActions.CopyEmailId); string ceText = host.Written.Last();
            host.CurrentText = "2001:DB8:0:0:0:0:0:1"; var ci = Run(SmartClipboardActions.CopyIpId); string ciText = host.Written.Last();
            host.CurrentText = "0040 721 123 456"; var cp = Run(SmartClipboardActions.CopyPhoneId); string cpText = host.Written.Last();
            host.CurrentText = "\"C:\\Users\\Ion\\a b.txt\""; var of = Run(SmartClipboardActions.OpenFolderId);
            host.FolderAnswer = "Calea copiată nu există pe acest PC."; var ofMissing = Run(SmartClipboardActions.OpenFolderId);
            Check("SC27", "Celelalte acțiuni: JWT decodat (fără semnătură), rgb(), adresa curățată, IP-ul normalizat, numărul (+40…), folderul căii (fără ghilimele, prin gazdă); motivul gazdei la eșec",
                  dj.Success && djText.Contains("\"payload\"") && !djText.Contains(sig) && cc.Success && ccText == "rgb(26, 43, 60)" && ce.Success && ceText == "Ion@exemplu.ro" &&
                  ci.Success && ciText == "2001:db8::1" && cp.Success && cpText == "+40721123456" && of.Success && host.Folders.Count == 2 && host.Folders[0] == @"C:\Users\Ion\a b.txt" &&
                  !ofMissing.Success && ofMissing.Message == "Calea copiată nu există pe acest PC." && host.Written.Count == written + 5);

            host.CurrentText = null;
            bool noneAvailable = SmartClipboardActions.AllIds.All(id => !reg.Get(id).IsAvailable());
            var nothing = Run(SmartClipboardActions.FormatJsonId);
            host.CurrentText = "Salut";
            bool plainNone = SmartClipboardActions.AllIds.All(id => !reg.Get(id).IsAvailable());
            host.CurrentText = minJson; host.Busy = true;
            var busy = Run(SmartClipboardActions.FormatJsonId);
            host.Busy = false;
            var regOff = new ActionRegistry(new FeatureFlags(AppSettings.NewFeatures()), new InlineUiDispatcher());
            SmartClipboardActions.Register(regOff, host);
            var off = Run(SmartClipboardActions.FormatJsonId, regOff);
            var regSafe = new ActionRegistry(new FeatureFlags(onStore, safeMode: true), new InlineUiDispatcher());
            SmartClipboardActions.Register(regSafe, host);
            var safe = Run(SmartClipboardActions.FormatJsonId, regSafe);
            var local = reg.InvokeAsync(SmartClipboardActions.FormatJsonId, null, ActionInvoker.LocalApi).GetAwaiter().GetResult();
            var withArg = reg.InvokeAsync(SmartClipboardActions.FormatJsonId, new Dictionary<string, string> { ["text"] = "{}" }, ActionInvoker.UI).GetAwaiter().GetResult();
            Check("SC28", "Fără text (sau text privat, deci neînregistrat), text simplu → nicio acțiune disponibilă; clipboard ocupat → mesaj; comutator oprit sau mod sigur → refuzat; API-ul local și parametrii (conținut din afară) → refuzate",
                  noneAvailable && plainNone && !nothing.Success && !busy.Success && busy.Message.Contains("folosit de altă aplicație") &&
                  !off.Success && off.Message.Contains("oprită") && !safe.Success && !local.Success && !withArg.Success && withArg.Message == "Parametru necunoscut.",
                  busy.Message + " / " + off.Message);

            string secret = "zq-secret-p21";
            host.CurrentText = "{\"parola\":\"" + secret + "\"}";
            Run(SmartClipboardActions.FormatJsonId);
            host.CurrentText = "https://x.ro/?utm_source=" + secret;
            Run(SmartClipboardActions.CleanUrlId);
            Run(SmartClipboardActions.OpenUrlId);
            Check("SC29", "Log-ul registrului: doar „Acțiune clipboard.… (UI): reușită/eșuată”, niciodată conținutul clipboard-ului (logic, cu un marcaj în JSON și în link)",
                  logs.Count >= 10 && logs.All(l => !l.Contains(secret) && !l.Contains("Ion") && !l.Contains("x.ro") && Regex.IsMatch(l, @"^Acțiune clipboard\.[a-z-]+ \((UI|LocalApi)\): (reușită|eșuată)$")),
                  string.Join(" | ", logs.Where(l => !Regex.IsMatch(l, @"^Acțiune clipboard\.[a-z-]+ \((UI|LocalApi)\): (reușită|eșuată)$"))));

            // ---- passwords stay ignored
            var ms0 = new MemoryStream(BitConverter.GetBytes(0));
            var markers = new[]
            {
                ClipboardPrivacy.IsPrivate(new[] { "UnicodeText", "ExcludeClipboardContentFromMonitorProcessing" }),
                ClipboardPrivacy.IsPrivate(new[] { "Text", "Clipboard Viewer Ignore" }),
                ClipboardPrivacy.IsPrivate(new[] { "Text", "CanIncludeInClipboardHistory" }, new Dictionary<string, object> { ["CanIncludeInClipboardHistory"] = BitConverter.GetBytes(0) }),
                ClipboardPrivacy.IsPrivate(new[] { "Text", "CanUploadToCloudClipboard" }, new Dictionary<string, object> { ["CanUploadToCloudClipboard"] = ms0 }),
                ClipboardPrivacy.IsPrivate(new[] { "Text", "excludeclipboardcontentfrommonitorprocessing" }),
                ClipboardPrivacy.IsPrivate(s => throw new InvalidOperationException("ocupat"), s => null),
                ClipboardPrivacy.IsPrivate(s => s == "CanIncludeInClipboardHistory", s => throw new System.Runtime.InteropServices.COMException("ocupat")),
                ClipboardPrivacy.IsPrivate((IEnumerable<string>)null), ClipboardPrivacy.IsPrivate(null, s => null),
            };
            var open = new[]
            {
                ClipboardPrivacy.IsPrivate(new[] { "UnicodeText", "Text", "Locale" }),
                ClipboardPrivacy.IsPrivate(new[] { "Text", "CanIncludeInClipboardHistory" }, new Dictionary<string, object> { ["CanIncludeInClipboardHistory"] = BitConverter.GetBytes(1) }),
                ClipboardPrivacy.IsPrivate(new[] { "Text", "CanUploadToCloudClipboard" }),
                ClipboardPrivacy.IsPrivate(new[] { "Text", "CanIncludeInClipboardHistory" }, new Dictionary<string, object> { ["CanIncludeInClipboardHistory"] = new byte[] { 0, 0 } }),
                ClipboardPrivacy.IsPrivate(Array.Empty<string>()),
            };
            Check("SC30", "Parolele (formatele private): „ExcludeClipboardContentFromMonitorProcessing”, „Clipboard Viewer Ignore”, DWORD 0 în „CanIncludeInClipboardHistory” / „CanUploadToCloudClipboard” (octeți sau stream), orice eroare → privat; fără ele (sau DWORD 1, sau prea scurt) → nu",
                  markers.All(x => x) && open.All(x => !x), string.Join(",", markers) + " / " + string.Join(",", open));

            string notch0 = Src("NotchWindow.xaml.cs");
            string onClip = Norm(NoComments(MethodBody(notch0, "private void OnClipboard()")));
            string isPriv = Norm(NoComments(MethodBody(notch0, "private static bool IsPrivateClip()")));
            var privateHost = new FakeClipHost();          // what the notch's hook leaves after a private copy: no text
            Check("SC31", "Un clipboard privat nu e analizat: OnClipboard uită textul anterior înainte de orice citire, iese la IsPrivateClip înainte de istoric și de Smart Clipboard; IsPrivateClip folosește aceeași verificare (ClipboardPrivacy), eroarea tot privat; fără text → niciun chip, nicio acțiune",
                  onClip.StartsWith("{ if (_ignoreClip) { _ignoreClip = false; return; } SmartClipboardForget(); try { if (!Clipboard.ContainsText()) return; if (IsPrivateClip()) return;", StringComparison.Ordinal) &&
                  onClip.IndexOf("if (IsPrivateClip()) return;", StringComparison.Ordinal) < onClip.IndexOf("SmartClipboardCopied(t);", StringComparison.Ordinal) &&
                  onClip.Contains("_tools.ClipsChanged(); SmartClipboardCopied(t); }") && Count(onClip, "SmartClipboard") == 2 &&
                  isPriv.Contains("var d = Clipboard.GetDataObject(); if (d == null) return false; return Features.SmartClipboard.ClipboardPrivacy.IsPrivate(d.GetDataPresent, d.GetData);") &&
                  isPriv.Contains("catch { return true; }") && !Norm(NoComments(notch0)).Contains("ExcludeClipboardContentFromMonitorProcessing") &&
                  SmartClipboardActions.Create(privateHost).All(a => !a.IsAvailable()) && SmartClipboardActions.ChipsFor(new SmartClipCache().Get(privateHost.CurrentText)).Count == 0);

            // ---- settings
            var fresh = new AppSettings();
            var old = JsonSerializer.Deserialize<AppSettings>("{\"Standby\":[\"music\"]}");
            var saved = new AppSettings { SmartClipboardPeek = true };
            var back = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(saved));
            var opt = SettingsActions.Find("settings.clipboard-peek");
            string xaml = Norm(Src("SettingsWindow.xaml")), settingsCs = Norm(NoComments(Src("SettingsWindow.xaml.cs")));
            Check("SC32", "Setarea „peek la copiere”: implicit oprită, oprită pentru setări vechi, salvată; în Setări (ClipPeekBox) cu acțiunea „settings.clipboard-peek”, citită și salvată o dată",
                  !fresh.SmartClipboardPeek && !old.SmartClipboardPeek && back.SmartClipboardPeek && opt != null && opt.Target == "ClipPeekBox" && opt.Section == "Smart Clipboard" &&
                  xaml.Contains("<CheckBox x:Name=\"ClipPeekBox\"") && xaml.Contains("Text=\"Smart Clipboard\"") &&
                  Count(settingsCs, "ClipPeekBox.IsChecked = s.SmartClipboardPeek;") == 1 && Count(settingsCs, "_s.SmartClipboardPeek = ClipPeekBox.IsChecked == true;") == 1);

            SmartClipboardSourcePins();
        }

        /// <summary>The WPF side (no compiler here): small hooks in the big files, the registry as the only way in, no polling, nothing of the content in the log.</summary>
        static void SmartClipboardSourcePins()
        {
            const string Part = "Features/SmartClipboard/NotchWindow.SmartClipboard.cs", Chips = "Features/SmartClipboard/SmartClipChips.cs";
            string notch0 = Src("NotchWindow.xaml.cs"), notch = Norm(NoComments(notch0)), part = Src(Part), partN = Norm(NoComments(part)), chips = Src(Chips), chipsN = Norm(NoComments(chips));
            string widget = Norm(NoComments(Src("Widgets/ToolWidgets.cs")));
            Check("SC33", "Legăturile: OnClipboard (2), CopyToClipboard (1), Cleanup (1), IsPrivateClip (1) în NotchWindow.xaml.cs; widget-ul Clipboard (3 rânduri); App: înregistrarea și pornirea după Quick Actions",
                  Count(notch, "SmartClipboard") == 5 && notch.Contains("try { _ignoreClip = true; Clipboard.SetText(text); SmartClipboardOurs(text); } catch { _ignoreClip = false; }") &&
                  Norm(NoComments(MethodBody(notch0, "public void Cleanup()"))).Contains("StopCommandBar(); StopQuickActions(); StopSmartClipboard();") &&
                  Count(widget, "SmartClip") == 4 && widget.Contains("var g = Ui.Rows(Ui.Auto, Ui.Px(6), Ui.Auto, Ui.Star());") &&
                  widget.Contains("g.Put(_smart = new Features.SmartClipboard.SmartClipChips(W), 0, 2); g.Put(new ScrollViewer { Style = Ui.S(\"SlimScroll\"), Content = _list }, 0, 3);") &&
                  widget.Contains("public override void Refresh() { _smart.Refresh(); string q =") &&
                  Norm(NoComments(Src("App.xaml.cs"))).Contains("_notch.StartQuickActions(); _notch.StartSmartClipboard();") &&
                  Norm(NoComments(Src("App.xaml.cs"))).Contains("Features.SmartClipboard.SmartClipboardActions.Register(registry, new Features.SmartClipboard.NotchSmartClipboardHost(_notch));"));
            Check("SC34", "Cu comutatorul oprit widget-ul arată ca azi: rândul de chip-uri pornește ascuns (înălțime 0), se ascunde la oprire și pentru text simplu; desenat doar când textul sau comutatorul se schimbă",
                  chipsN.Contains("Visibility = Visibility.Collapsed; Margin = new Thickness(0, 0, 0, 6);") &&
                  Norm(NoComments(MethodBody(chips, "private void Clear()"))).Contains("Visibility = Visibility.Collapsed;") &&
                  Norm(NoComments(MethodBody(chips, "public void Refresh()"))).Contains("if (on == _drawnOn && ReferenceEquals(text, _drawnFor)) return;") &&
                  Norm(NoComments(MethodBody(chips, "public void Refresh()"))).Contains("Draw(on && text != null ? _w.SmartClipboardCurrent() : SmartClip.None);") &&
                  partN.Contains("internal string SmartClipboardText => _scOn ? _scLatest : null;") && partN.Contains("internal SmartClip SmartClipboardCurrent() => _scOn ? _scCache.Get(_scLatest) : SmartClip.None;"));
            Check("SC35", "Protocolul comutatorului: Changed += / -=, UI prin Dispatcher, IsEnabled citit în handler, textul uitat la orice schimbare; erorile → ReportError(\"smart-clipboard\")",
                  partN.Contains("FeatureFlags.Current.Changed += _scFlagHandler;") && partN.Contains("FeatureFlags.Current.Changed -= _scFlagHandler;") &&
                  partN.Contains("Dispatcher.InvokeAsync(ApplySmartClipboardSwitch)") &&
                  Norm(NoComments(MethodBody(part, "private void ApplySmartClipboardSwitch()"))).Contains("bool on = SmartClipboardEnabled(); if (on == _scOn) return; _scOn = on; _scLatest = null;") &&
                  Norm(NoComments(MethodBody(part, "private void StopSmartClipboard()"))).Contains("_scOn = false; _scLatest = null;") &&
                  Count(partN, "FeatureFlags.Current?.ReportError(SmartClipboardActions.FeatureId, ex);") >= 1 && Count(chipsN, "FeatureFlags.Current?.ReportError(SmartClipboardActions.FeatureId, ex);") == 2 &&
                  Norm(NoComments(MethodBody(part, "private void SmartClipboardCopied(string text)"))).StartsWith("{ if (!_scOn) return;", StringComparison.Ordinal));
            Check("SC36", "Fără buclă: rezultatul e scris ca al nostru (_ignoreClip, notificarea următoare sărită: fără istoric, fără peek); clipboard-ul ocupat → false, nu excepție",
                  Norm(NoComments(MethodBody(part, "internal bool SmartClipboardWrite(string text)"))).Contains("try { _ignoreClip = true; Clipboard.SetText(text); } catch (Exception ex) when") &&
                  Norm(NoComments(MethodBody(part, "internal bool SmartClipboardWrite(string text)"))).Contains("{ _ignoreClip = false; return false; } SmartClipboardOurs(text); return true;") &&
                  Norm(NoComments(MethodBody(part, "private void SmartClipboardOurs(string text)"))) == "{ if (_scOn) _scLatest = text; }" &&
                  notch.Contains("if (_ignoreClip) { _ignoreClip = false; return; } SmartClipboardForget();"));
            Check("SC37", "Chip-urile pornesc acțiunile doar prin ActionRegistry.Current.InvokeAsync (UI, fără parametri, fără confirmare, fără ExecuteAsync); fără async void; rezultatul pe Dispatcher",
                  Regex.Matches(chipsN, @"(?<!Dispatcher)\.InvokeAsync\(").Count == 1 && chipsN.Contains("var r = await reg.InvokeAsync(id, null, ActionInvoker.UI);") &&
                  chipsN.Contains("await Dispatcher.InvokeAsync(() => ShowResult(r));") && !chipsN.Contains("ExecuteAsync(") && !chipsN.Contains("confirmed") &&
                  !chipsN.Contains("async void") && chipsN.Contains("_ = RunAsync(id);") && !partN.Contains("async void"));
            var files = Directory.EnumerateFiles(Path.Combine(RepoRoot(), "Features", "SmartClipboard"), "*.cs").Select(f => "Features/SmartClipboard/" + Path.GetFileName(f)).OrderBy(f => f).ToList();
            var forbidden = new[] { "new Timer", "Thread.Sleep", "Task.Delay", "GetForegroundWindow", "SetWinEventHook", "Process.Start", "ProcessStartInfo", "ShowLive(", "Alert(", "Clipboard.GetText", "Clipboard.GetDataObject" };
            var found = files.SelectMany(f => forbidden.Where(x => NoComments(Src(f)).Contains(x)).Select(x => f + ": " + x)).ToList();
            Check("SC38", "Fără timer de polling, fără citirea clipboard-ului sau a ferestrei din față, procese doar prin Shell.Open (link doar http/https, folder local, niciodată fișierul); un singur DispatcherTimer, o dată (rezultatul click-ului)",
                  files.Count == 6 && found.Count == 0 && Count(chipsN, "new DispatcherTimer") == 1 && chipsN.Contains("_messageTimer.Tick += (o, e) => { _messageTimer.Stop();") &&
                  files.Where(f => f != Chips).All(f => !NoComments(Src(f)).Contains("DispatcherTimer")) &&
                  Count(partN, "Services.Shell.Open(") == 2 && partN.Contains("if (!SmartClipRecognizer.IsUrl(url)) return; Services.Shell.Open(url);") &&
                  partN.Contains("DriveType.Network") && partN.Contains("string folder = Directory.Exists(p) ? p : File.Exists(p) ? Path.GetDirectoryName(p) : null;") &&
                  partN.Contains("Services.Shell.Open(folder);"), string.Join(" | ", found) + " · " + string.Join(",", files));
            var logCalls = files.SelectMany(f => Src(f).Split('\n').Where(l => l.Contains("App.Log(", StringComparison.Ordinal)).Select(l => l.Trim())).ToList();
            Check("SC39", "În log, din Features/SmartClipboard, doar texte fixe și contorul de recunoașteri: niciun text copiat, tip lângă conținut, mesaj de excepție sau parametru",
                  logCalls.Count == 2 && logCalls.Any(l => l.Contains("App.Log(\"Smart Clipboard: pornit.\")")) &&
                  logCalls.Any(l => l.Contains("App.Log(\"Smart Clipboard: oprit (\" + _scRecognized + \" recunoașteri).\")")) &&
                  logCalls.All(l => !Regex.IsMatch(l, @"text|Text|clip\b|Normalized|_scLatest|Message|ex\b|Kind|title|url|path")),
                  string.Join(" | ", logCalls));
            Check("SC40", "Fără culori scrise în cod: pensulele temei (SetResourceReference), stilul GhostPill; mostra e culoarea copiată (Ui.Rgb din R, G, B, A); id-uri UI Automation stabile",
                  !Regex.IsMatch(chips + part, @"Color\.From|#[0-9A-Fa-f]{6}|new SolidColorBrush|Brushes\.") && Count(chipsN, "SetResourceReference(") >= 3 &&
                  chipsN.Contains("Ui.S(\"GhostPill\")") && chipsN.Contains("Background = Ui.Rgb(clip.R, clip.G, clip.B, clip.A)") &&
                  chipsN.Contains("SmokeMode.SmartClipChipPrefix + chip.ActionId") && SmokeMode.SmartClipChipPrefix + SmartClipboardActions.FormatJsonId == "sc-clipboard.format-json" &&
                  chipsN.Contains("SmokeMode.SmartClipMessageAutomationId") && chipsN.Contains("SmokeMode.SmartClipRowAutomationId"));
            string copied = Norm(NoComments(MethodBody(part, "private void SmartClipboardCopied(string text)")));
            Check("SC41", "Peek: doar cu opțiunea, cu Activity Manager pornit și prin el (Post, Low, durata de peek), titlul fix al tipului; fără ele nimic",
                  copied.Contains("if (!S.SmartClipboardPeek || !_activityOn || _activity == null) return;") && copied.Contains("string title = SmartClipboardActions.PeekTitle(clip); if (title == null) return;") &&
                  copied.Contains("_activity.Post(new Core.Activity.Activity") && copied.Contains("Priority = Core.Activity.ActivityPriority.Low, Duration = Core.Activity.ActivityManager.PeekDuration, Title = title,") &&
                  copied.IndexOf("_scLatest = text;", StringComparison.Ordinal) < copied.IndexOf("_activity.Post(", StringComparison.Ordinal));

            // ---- the smoke test
            Check("SC42", "Comenzi de fum P21: „smoke-clipboard-page on|off” (majusculele nu contează); invalide ignorate",
                  SmokeMode.Parse("smoke-clipboard-page on") is { Kind: SmokeCommandKind.ClipboardPage, Argument: "on" } &&
                  SmokeMode.Parse("  SMOKE-CLIPBOARD-PAGE   Off ") is { Kind: SmokeCommandKind.ClipboardPage, Argument: "off" } &&
                  new[] { "smoke-clipboard-page", "smoke-clipboard-page yes", "smoke-clipboard-page on now", "smoke-clipboard on", "smoke-clipboard-page ../x" }.All(l => SmokeMode.Parse(l) == null) &&
                  SmokeMode.SmokeClipboardPageId == "smoke-clipboard");
            string smoke0 = Src("Features/Smoke/NotchWindow.Smoke.cs");
            string page = Norm(NoComments(MethodBody(smoke0, "private void SmokeClipboardPage(")));
            Check("SC43", "Pagina de test: doar în modul --smoke, cu widget-ul Clipboard (3 × 2) în setările din folderul de fum; adăugată și arătată, apoi scoasă",
                  Norm(NoComments(smoke0)).Contains("case SmokeCommandKind.ClipboardPage: SmokeClipboardPage(c.Argument == \"on\"); break;") &&
                  page.StartsWith("{ if (!SmokeMode.On) return;", StringComparison.Ordinal) && page.Contains("Widgets.Catalog.NewSlot(\"clipboard\", (3, 2))") &&
                  page.Contains("S.Pages.Add(page);") && page.Contains("S.Pages.Remove(page);") && page.Contains("ShowPane(UserPane(page));"));
            string sp = Src("tests/WinNotch.Smoke/SmokeProgram.cs");
            string body = MethodBody(sp, "private static void SmartClipboard()") ?? "";
            Check("SC44", "Testul de fum P21 rulează o singură dată (doar cu activity-manager oprit) și o spune; JSON copiat, oprit → niciun chip, pornit → „Formatează”, click, JSON formatat echivalent în clipboard, „Compactează” după, nimic în log; repune starea",
                  sp.Contains("if (!_activityOn) Run(step = \"Smart Clipboard") && sp.Contains("SKIP  Smart Clipboard") &&
                  Count(body, "Command(\"toggle feature \" + SmartClipboardFeature);") == 2 && body.Contains("Command(\"smoke-clipboard-page on\");") &&
                  body.Contains("Command(\"smoke-clipboard-page off\");") && Count(body, "SetClipboardText(SmokeJson);") == 2 && body.Contains("chip.AsButton().Invoke();") &&
                  body.Contains("SameJson(got, SmokeJson)") && body.Contains("SmartChip(ScMinify) != null && SmartChip(ScFormat) == null") &&
                  body.Contains("LogCount(SmokeJsonMarker) > 0") && body.Contains("if (SmartChips().Count > 0) Fail(") &&
                  body.Contains("\"Acțiune clipboard.format-json (UI): reușită\""));
        }
    }
}
