using System.Linq;
using System.Net;
using System.Text.RegularExpressions;

namespace WinNotch.Services
{
    /// <summary>Address checks for downloads that web pages can influence (tab covers). No UI dependencies, so it's unit-tested.</summary>
    public static class NetSafety
    {
        /// <summary>True for loopback, private, link-local, carrier-grade NAT, multicast and IPv6 forms that can wrap them.</summary>
        public static bool IsPrivate(IPAddress a)
        {
            if (a.IsIPv4MappedToIPv6) a = a.MapToIPv4();
            if (IPAddress.IsLoopback(a) || a.IsIPv6LinkLocal || a.IsIPv6SiteLocal || a.IsIPv6Multicast) return true;
            var b = a.GetAddressBytes();
            if (b.Length == 16)
                return (b[0] & 0xFE) == 0xFC || b.Take(12).All(x => x == 0)                      // fc00::/7; ::, ::1 and ::a.b.c.d (IPv4-compatible)
                    || (b[0] == 0x20 && b[1] == 0x02)                                         // 2002::/16 6to4 (wraps an IPv4)
                    || (b[0] == 0x20 && b[1] == 0x01 && b[2] == 0 && b[3] == 0)               // 2001::/32 Teredo
                    || (b[0] == 0 && b[1] == 0x64 && b[2] == 0xff && b[3] == 0x9b);           // 64:ff9b::/96 and 64:ff9b:1::/48 NAT64
            return b[0] == 10 || b[0] == 127 || b[0] == 0 || b[0] >= 224 ||
                   (b[0] == 169 && b[1] == 254) || (b[0] == 172 && (b[1] & 0xF0) == 16) || (b[0] == 192 && b[1] == 168) ||
                   (b[0] == 100 && (b[1] & 0xC0) == 64) ||
                   (b[0] == 192 && b[1] == 0 && (b[2] == 0 || b[2] == 2)) ||                          // 192.0.0.0/24, 192.0.2.0/24
                   (b[0] == 198 && b[1] == 51 && b[2] == 100) || (b[0] == 203 && b[1] == 0 && b[2] == 113);   // documentation ranges
        }

        /// <summary>YouTube video thumbnail for a watch / youtu.be / shorts / live link, or null.</summary>
        public static string YouTubeThumb(string url)
        {
            if (string.IsNullOrEmpty(url)) return null;
            var mm = Regex.Match(url, @"(?:youtube\.com/(?:watch\?(?:.*&)?v=|shorts/|live/)|youtu\.be/)([A-Za-z0-9_-]{11})");
            return mm.Success ? "https://i.ytimg.com/vi/" + mm.Groups[1].Value + "/mqdefault.jpg" : null;
        }
    }
}
