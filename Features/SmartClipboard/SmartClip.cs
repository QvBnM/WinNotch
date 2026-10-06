using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace WinNotch.Features.SmartClipboard
{
    /// <summary>What a copied text is (P21). <see cref="None"/> = plain text: no chips.</summary>
    public enum SmartClipKind { None, Json, Jwt, Url, Email, Color, Ip, Path, Phone }

    /// <summary>
    /// A recognized clipboard text and what its actions need (computed once per text). Holds clipboard content: it is
    /// shown in the UI and put back in the clipboard, never written to the log.
    /// </summary>
    public sealed class SmartClip
    {
        public static readonly SmartClip None = new SmartClip { Kind = SmartClipKind.None };

        public SmartClipKind Kind { get; init; }
        /// <summary>The trimmed text that was recognized.</summary>
        public string Text { get; init; } = "";
        /// <summary>
        /// The kind's normalized form: e-mail (no "mailto:", domain lowercase), IP (canonical), phone (digits, "+" for
        /// international), colour ("#RRGGBB" / "#RRGGBBAA"), path (without quotes), URL (without tracking parameters),
        /// JWT (header and payload as formatted JSON).
        /// </summary>
        public string Normalized { get; init; } = "";
        /// <summary>JSON: indented and minified (both equivalent to <see cref="Text"/>).</summary>
        public string Formatted { get; init; } = "";
        public string Minified { get; init; } = "";
        /// <summary>URL: how many tracking parameters <see cref="Normalized"/> leaves out (0 = nothing to clean).</summary>
        public int TrackingRemoved { get; init; }
        /// <summary>Colour (content, not a theme colour): the swatch and rgb().</summary>
        public byte R { get; init; }
        public byte G { get; init; }
        public byte B { get; init; }
        public byte A { get; init; } = 255;
    }

    /// <summary>
    /// P21 "Smart Clipboard" without WPF (ADR 0010): recognizes a copied text by hand-written parsing (no regular
    /// expressions, so nothing can backtrack), with a size limit, in a fixed priority order:
    /// JSON → JWT → URL → e-mail → colour → IP → path → phone. Anything else is <see cref="SmartClipKind.None"/>.
    /// Never reads the clipboard, the disk or the network; never logs.
    /// </summary>
    public static class SmartClipRecognizer
    {
        /// <summary>Bigger texts are not looked at (64 KB of characters).</summary>
        public const int MaxInput = 64 * 1024;
        public const int MaxJsonDepth = 64;
        /// <summary>A formatted JSON bigger than this is not offered (deep nesting multiplies the indentation).</summary>
        public const int MaxJsonOutput = 1024 * 1024;
        public const int MaxUrl = 8192, MaxJwt = 16 * 1024, MaxEmail = 254, MaxPath = 1024, MaxPhone = 32;

        /// <summary>The kinds, in the order they are tried (the first that matches wins).</summary>
        public static readonly IReadOnlyList<SmartClipKind> Priority = new[]
        {
            SmartClipKind.Json, SmartClipKind.Jwt, SmartClipKind.Url, SmartClipKind.Email, SmartClipKind.Color, SmartClipKind.Ip, SmartClipKind.Path, SmartClipKind.Phone,
        };

        public static SmartClip Recognize(string text)
        {
            if (string.IsNullOrEmpty(text) || text.Length > MaxInput) return SmartClip.None;
            string t = text.Trim();
            if (t.Length == 0) return SmartClip.None;
            try
            {
                if ((t[0] == '{' || t[0] == '[') && TryJson(t, out var formatted, out var minified))
                    return new SmartClip { Kind = SmartClipKind.Json, Text = t, Formatted = formatted, Minified = minified };
                // every other kind is one token on one line
                if (t.IndexOf('\n') >= 0 || t.IndexOf('\r') >= 0) return SmartClip.None;
                if (TryJwt(t, out var jwt)) return new SmartClip { Kind = SmartClipKind.Jwt, Text = t, Normalized = jwt };
                if (IsUrl(t))
                {
                    string clean = CleanUrl(t, out int removed);
                    return new SmartClip { Kind = SmartClipKind.Url, Text = t, Normalized = clean, TrackingRemoved = removed };
                }
                if (TryEmail(t, out var email)) return new SmartClip { Kind = SmartClipKind.Email, Text = t, Normalized = email };
                if (TryColor(t, out byte r, out byte g, out byte b, out byte a, out var hex))
                    return new SmartClip { Kind = SmartClipKind.Color, Text = t, Normalized = hex, R = r, G = g, B = b, A = a };
                if (TryIp(t, out var ip)) return new SmartClip { Kind = SmartClipKind.Ip, Text = t, Normalized = ip };
                if (TryPath(t, out var path)) return new SmartClip { Kind = SmartClipKind.Path, Text = t, Normalized = path };
                if (TryPhone(t, out var phone)) return new SmartClip { Kind = SmartClipKind.Phone, Text = t, Normalized = phone };
            }
            catch (Exception ex) when (ex is ArgumentException || ex is FormatException || ex is InvalidOperationException || ex is OverflowException)
            {
                // a parser surprised by odd input: plain text
            }
            return SmartClip.None;
        }

        // ------------------------------------------------------------------ JSON

        /// <summary>An object or an array, the whole text (nothing after it), at most <see cref="MaxJsonDepth"/> deep.</summary>
        public static bool TryJson(string t, out string formatted, out string minified)
        {
            formatted = minified = null;
            if (string.IsNullOrEmpty(t) || t.Length > MaxInput) return false;
            char last = t[t.Length - 1];
            if (!(t[0] == '{' && last == '}') && !(t[0] == '[' && last == ']')) return false;
            try
            {
                using var doc = JsonDocument.Parse(t, new JsonDocumentOptions { MaxDepth = MaxJsonDepth });
                formatted = Write(doc.RootElement, true);
                minified = Write(doc.RootElement, false);
            }
            catch (JsonException) { return false; }
            return formatted.Length <= MaxJsonOutput;
        }

        /// <summary>
        /// The same JSON written again (numbers as written, keys in order, duplicates kept). Letters with diacritics stay
        /// as they are; only what JSON must escape is escaped.
        /// </summary>
        public static string Write(JsonElement e, bool indented)
        {
            var buf = new ArrayBufferWriter<byte>();
            using (var w = new Utf8JsonWriter(buf, new JsonWriterOptions { Indented = indented, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, MaxDepth = MaxJsonDepth + 2 }))
                e.WriteTo(w);
            return Encoding.UTF8.GetString(buf.WrittenSpan);
        }

        // ------------------------------------------------------------------ JWT

        /// <summary>
        /// header.payload.signature, base64url; the header a JSON object with "alg", the payload a JSON object. Decoded
        /// locally: the signature is not verified, not decoded and not shown. <paramref name="decoded"/> is
        /// {"header": …, "payload": …}, indented.
        /// </summary>
        public static bool TryJwt(string t, out string decoded)
        {
            decoded = null;
            if (string.IsNullOrEmpty(t) || t.Length > MaxJwt) return false;
            var parts = t.Split('.');
            if (parts.Length != 3 || parts[0].Length == 0 || parts[1].Length == 0) return false;
            foreach (var p in parts) if (!p.All(IsBase64UrlChar)) return false;
            var header = FromBase64Url(parts[0]);
            var payload = FromBase64Url(parts[1]);
            if (header == null || payload == null) return false;
            try
            {
                using var h = JsonDocument.Parse(header, new JsonDocumentOptions { MaxDepth = 16 });
                if (h.RootElement.ValueKind != JsonValueKind.Object || !h.RootElement.TryGetProperty("alg", out var alg) || alg.ValueKind != JsonValueKind.String) return false;
                using var p = JsonDocument.Parse(payload, new JsonDocumentOptions { MaxDepth = 32 });
                if (p.RootElement.ValueKind != JsonValueKind.Object) return false;
                var buf = new ArrayBufferWriter<byte>();
                using (var w = new Utf8JsonWriter(buf, new JsonWriterOptions { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
                {
                    w.WriteStartObject();
                    w.WritePropertyName("header");
                    h.RootElement.WriteTo(w);
                    w.WritePropertyName("payload");
                    p.RootElement.WriteTo(w);
                    w.WriteEndObject();
                }
                decoded = Encoding.UTF8.GetString(buf.WrittenSpan);
                return true;
            }
            catch (JsonException) { return false; }
        }

        private static bool IsBase64UrlChar(char c) => (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-' || c == '_';

        /// <summary>base64url without padding → bytes; null when it isn't valid.</summary>
        public static byte[] FromBase64Url(string s)
        {
            if (string.IsNullOrEmpty(s) || s.Length % 4 == 1 || !s.All(IsBase64UrlChar)) return null;
            var b64 = new StringBuilder(s.Length + 3).Append(s).Replace('-', '+').Replace('_', '/');
            while (b64.Length % 4 != 0) b64.Append('=');
            var bytes = new byte[b64.Length / 4 * 3];
            return Convert.TryFromBase64String(b64.ToString(), bytes, out int n) ? bytes.AsSpan(0, n).ToArray() : null;
        }

        // ------------------------------------------------------------------ URL

        /// <summary>http:// or https:// with a real host, no spaces or control characters. Other schemes are not links here.</summary>
        public static bool IsUrl(string t)
        {
            if (string.IsNullOrEmpty(t) || t.Length > MaxUrl) return false;
            if (!t.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !t.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return false;
            foreach (char c in t) if (c <= ' ' || c == '\u007f' || char.IsWhiteSpace(c)) return false;
            if (!Uri.TryCreate(t, UriKind.Absolute, out var u)) return false;
            if (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps) return false;
            string host = u.Host;
            if (string.IsNullOrEmpty(host)) return false;
            return host.Contains('.') || u.HostNameType == UriHostNameType.IPv6 || string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Tracking parameters removed, exactly (case-sensitive): the common ad, e-mail and social ones.</summary>
        public static readonly IReadOnlyCollection<string> TrackingParameters = new HashSet<string>(StringComparer.Ordinal)
        {
            "fbclid", "gclid", "dclid", "gbraid", "wbraid", "msclkid", "mc_cid", "mc_eid", "yclid", "igshid", "igsh",
            "_hsenc", "_hsmi", "mkt_tok", "ref_src", "twclid", "ttclid", "li_fat_id",
        };

        /// <summary>Also every "utm_…" (utm_source, utm_medium, utm_campaign, utm_term, utm_content, utm_id…).</summary>
        public const string TrackingPrefix = "utm_";

        /// <summary>
        /// Lowercase only, as the ad networks write them: "UTM_SOURCE" or "Fbclid" could be a site's own parameter, so it
        /// stays (removing less never breaks a link). The name is compared as written (not percent-decoded).
        /// </summary>
        public static bool IsTrackingParameter(string name) =>
            !string.IsNullOrEmpty(name) && (TrackingParameters.Contains(name) || (name.Length > TrackingPrefix.Length && name.StartsWith(TrackingPrefix, StringComparison.Ordinal)));

        /// <summary>
        /// The link without its tracking parameters; everything else stays byte for byte (the other parameters and their
        /// order, their encoding, the fragment after "#"). Only tracking parameters → no "?" left. Nothing removed → the
        /// very same string.
        /// </summary>
        public static string CleanUrl(string url, out int removed)
        {
            removed = 0;
            if (string.IsNullOrEmpty(url)) return url ?? "";
            int hash = url.IndexOf('#');
            string head = hash >= 0 ? url.Substring(0, hash) : url;
            string fragment = hash >= 0 ? url.Substring(hash) : "";
            int q = head.IndexOf('?');
            if (q < 0) return url;
            string path = head.Substring(0, q);
            var kept = new List<string>();
            foreach (var part in head.Substring(q + 1).Split('&'))
            {
                int eq = part.IndexOf('=');
                string name = eq >= 0 ? part.Substring(0, eq) : part;
                if (IsTrackingParameter(name)) removed++;
                else kept.Add(part);
            }
            if (removed == 0) return url;
            return kept.All(k => k.Length == 0) ? path + fragment : path + "?" + string.Join("&", kept) + fragment;
        }

        // ------------------------------------------------------------------ e-mail

        private const string LocalSpecials = ".!#$%&'*+/=?^_`{|}~-";

        /// <summary>name@domain.tld (also with "mailto:"): one "@", a domain with at least two labels and a letter TLD.</summary>
        public static bool TryEmail(string t, out string normalized)
        {
            normalized = null;
            if (string.IsNullOrEmpty(t)) return false;
            string s = t.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ? t.Substring(7) : t;
            if (s.Length == 0 || s.Length > MaxEmail) return false;
            int at = s.IndexOf('@');
            if (at <= 0 || at != s.LastIndexOf('@') || at == s.Length - 1) return false;
            string local = s.Substring(0, at), domain = s.Substring(at + 1);
            if (local.Length > 64 || local[0] == '.' || local[local.Length - 1] == '.' || local.Contains("..")) return false;
            foreach (char c in local) if (!IsAsciiLetterOrDigit(c) && LocalSpecials.IndexOf(c) < 0) return false;
            if (!IsDomain(domain)) return false;
            normalized = local + "@" + domain.ToLowerInvariant();
            return true;
        }

        private static bool IsDomain(string d)
        {
            if (d.Length == 0 || d.Length > 253) return false;
            var labels = d.Split('.');
            if (labels.Length < 2) return false;
            foreach (var l in labels)
            {
                if (l.Length == 0 || l.Length > 63 || l[0] == '-' || l[l.Length - 1] == '-') return false;
                foreach (char c in l) if (!IsAsciiLetterOrDigit(c) && c != '-') return false;
            }
            string tld = labels[labels.Length - 1];
            return tld.Length >= 2 && tld.All(IsAsciiLetter);
        }

        // ------------------------------------------------------------------ colour

        /// <summary>
        /// "#RRGGBB", "#RRGGBBAA", or "#RGB" with at least one letter ("#123" is more likely an issue number than a
        /// colour). Without "#" it isn't a colour ("123456" is a number); "#RGBA" isn't offered either.
        /// </summary>
        public static bool TryColor(string t, out byte r, out byte g, out byte b, out byte a, out string normalized)
        {
            r = g = b = 0; a = 255; normalized = null;
            if (string.IsNullOrEmpty(t) || t[0] != '#') return false;
            string hex = t.Substring(1);
            if ((hex.Length != 3 && hex.Length != 6 && hex.Length != 8) || !hex.All(IsHexDigit)) return false;
            if (hex.Length == 3)
            {
                if (!hex.Any(char.IsAsciiLetter)) return false;
                hex = new string(new[] { hex[0], hex[0], hex[1], hex[1], hex[2], hex[2] });
            }
            r = Hex2(hex, 0); g = Hex2(hex, 2); b = Hex2(hex, 4);
            if (hex.Length == 8) a = Hex2(hex, 6);
            normalized = "#" + hex.ToUpperInvariant();
            return true;
        }

        private static byte Hex2(string s, int i) => byte.Parse(s.AsSpan(i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

        /// <summary>"rgb(18, 52, 86)", or "rgba(18, 52, 86, 0.5)" with transparency.</summary>
        public static string Rgb(SmartClip c) =>
            c.A == 255 ? "rgb(" + c.R + ", " + c.G + ", " + c.B + ")"
                       : "rgba(" + c.R + ", " + c.G + ", " + c.B + ", " + Math.Round(c.A / 255.0, 2).ToString(CultureInfo.InvariantCulture) + ")";

        // ------------------------------------------------------------------ IP

        /// <summary>
        /// IPv4 written strictly (four numbers 0–255, no leading zeros: "1" or "0x7f.1" are not addresses here), or IPv6
        /// (only hex digits, ":" and "." for an embedded IPv4; no zone).
        /// </summary>
        public static bool TryIp(string t, out string normalized)
        {
            normalized = null;
            if (string.IsNullOrEmpty(t) || t.Length > 45) return false;
            if (t.IndexOf(':') < 0)
            {
                var parts = t.Split('.');
                if (parts.Length != 4) return false;
                foreach (var p in parts)
                {
                    if (p.Length == 0 || p.Length > 3 || !p.All(char.IsAsciiDigit)) return false;
                    if (p.Length > 1 && p[0] == '0') return false;
                    if (int.Parse(p, CultureInfo.InvariantCulture) > 255) return false;
                }
                normalized = t;
                return true;
            }
            if (t.Count(c => c == ':') < 2) return false;
            foreach (char c in t) if (!IsHexDigit(c) && c != ':' && c != '.') return false;
            if (!IPAddress.TryParse(t, out var addr) || addr.AddressFamily != AddressFamily.InterNetworkV6) return false;
            normalized = addr.ToString();
            return true;
        }

        // ------------------------------------------------------------------ path

        private const string BadPathChars = "<>\"|?*";

        /// <summary>
        /// A local Windows path: drive letter, ":", then "\" or "/" ("C:\Users\ion", also in quotes). Network paths
        /// (\\server\share, //server) are never paths here: opening them would log in to that server with your account.
        /// </summary>
        public static bool TryPath(string t, out string normalized)
        {
            normalized = null;
            if (string.IsNullOrEmpty(t)) return false;
            string s = t.Length >= 2 && t[0] == '"' && t[t.Length - 1] == '"' ? t.Substring(1, t.Length - 2) : t;
            if (s.Length < 3 || s.Length > MaxPath) return false;
            if (!IsAsciiLetter(s[0]) || s[1] != ':' || (s[2] != '\\' && s[2] != '/')) return false;
            for (int i = 2; i < s.Length; i++)
            {
                char c = s[i];
                if (c < ' ' || c == ':' || BadPathChars.IndexOf(c) >= 0) return false;
            }
            normalized = s;
            return true;
        }

        /// <summary>Two leading separators (\\, //, \/, /\) or a file:// URI with a host: a network path (never opened).</summary>
        public static bool IsNetworkPath(string path)
        {
            var p = (path ?? "").Trim().Trim('"');
            if (p.Length >= 2 && (p[0] == '\\' || p[0] == '/') && (p[1] == '\\' || p[1] == '/')) return true;
            return Uri.TryCreate(p, UriKind.Absolute, out var u) && u.IsFile && (u.IsUnc || !string.IsNullOrEmpty(u.Host));
        }

        // ------------------------------------------------------------------ phone

        /// <summary>
        /// A phone number: "+" and 8–15 digits, "00" and 10–17 digits, or a national number starting with 0 and 9–15
        /// digits; separators only spaces, "-", "/" and one pair of parentheses, never two in a row. Not a phone: dates
        /// ("2026-10-06"), amounts ("1.234,56"), short numbers ("12345"), IPs (dots are never accepted).
        /// </summary>
        public static bool TryPhone(string t, out string normalized)
        {
            normalized = null;
            if (string.IsNullOrEmpty(t) || t.Length > MaxPhone) return false;
            var digits = new StringBuilder();
            bool plus = false, open = false, closed = false;
            char prev = '\0';
            for (int i = 0; i < t.Length; i++)
            {
                char c = t[i];
                if (char.IsAsciiDigit(c)) { digits.Append(c); prev = c; continue; }
                if (c == '+') { if (i != 0) return false; plus = true; prev = c; continue; }
                if (c == '(')
                {
                    if (open || (prev != '\0' && prev != ' ' && prev != '+')) return false;
                    open = true; prev = c; continue;
                }
                if (c == ')')
                {
                    if (!open || closed || !char.IsAsciiDigit(prev)) return false;
                    closed = true; prev = c; continue;
                }
                if (c == ' ' || c == '-' || c == '/')
                {
                    if (prev == '\0' || prev == ' ' || prev == '-' || prev == '/' || prev == '(' || prev == '+') return false;
                    prev = c; continue;
                }
                return false;
            }
            if (open != closed || !char.IsAsciiDigit(prev) && prev != ')') return false;
            string d = digits.ToString();
            if (d.Length == 0) return false;
            if (plus)
            {
                if (d.Length < 8 || d.Length > 15 || d[0] == '0') return false;
                normalized = "+" + d;
                return true;
            }
            if (d[0] != '0') return false;
            if (d.StartsWith("00", StringComparison.Ordinal))
            {
                if (d.Length < 10 || d.Length > 17 || d[2] == '0') return false;
                normalized = "+" + d.Substring(2);
                return true;
            }
            if (d.Length < 9 || d.Length > 15) return false;
            normalized = d;
            return true;
        }

        // ------------------------------------------------------------------ helpers

        private static bool IsAsciiLetter(char c) => (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');
        private static bool IsAsciiLetterOrDigit(char c) => IsAsciiLetter(c) || (c >= '0' && c <= '9');
        private static bool IsHexDigit(char c) => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
    }

    /// <summary>
    /// The last text and its recognition, so a text is looked at once however often the widget redraws or the Command Bar
    /// asks which actions are available. Thread-safe (the registry may ask from any thread).
    /// </summary>
    public sealed class SmartClipCache
    {
        private readonly object _lock = new object();
        private string _text;
        private SmartClip _clip = SmartClip.None;

        public SmartClip Get(string text)
        {
            if (text == null) return SmartClip.None;
            lock (_lock)
            {
                if (ReferenceEquals(text, _text)) return _clip;
            }
            var clip = SmartClipRecognizer.Recognize(text);
            lock (_lock) { _text = text; _clip = clip; }
            return clip;
        }
    }
}
