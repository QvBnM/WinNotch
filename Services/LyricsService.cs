using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace WinNotch.Services
{
    public sealed class LyricLine
    {
        public TimeSpan Time;
        public string Text;
    }

    /// <summary>Time-synced lyrics from LRCLIB (free, no account): https://lrclib.net</summary>
    public static class LyricsService
    {
        private static readonly HttpClient Http = CreateClient();
        private static readonly Dictionary<string, List<LyricLine>> Cache = new Dictionary<string, List<LyricLine>>();
        private static readonly Regex LineRx = new Regex(@"^\[(\d+):(\d+(?:\.\d+)?)\]\s*(.*)$", RegexOptions.Compiled);

        private static HttpClient CreateClient()
        {
            var c = new HttpClient { Timeout = TimeSpan.FromSeconds(8), MaxResponseContentBufferSize = 1024 * 1024 };
            c.DefaultRequestHeaders.UserAgent.ParseAdd("WinNotch/0.2 (https://github.com)");
            return c;
        }

        /// <returns>Synced lines, or an empty list when the song has none.</returns>
        public static async Task<List<LyricLine>> GetAsync(string artist, string title, TimeSpan duration)
        {
            if (string.IsNullOrWhiteSpace(title)) return new List<LyricLine>();
            string key = (artist + "|" + title).ToLowerInvariant();
            lock (Cache) if (Cache.TryGetValue(key, out var hit)) return hit;

            var lines = new List<LyricLine>();
            bool reached = false;          // only remember "no lyrics" when the server actually answered
            try
            {
                string cleanTitle = Regex.Replace(title, @"\s*[\(\[](official|lyric|audio|video|hd|4k|visualizer)[^\)\]]*[\)\]]", "", RegexOptions.IgnoreCase).Trim();
                string url = "https://lrclib.net/api/get?artist_name=" + Uri.EscapeDataString(artist ?? "") +
                             "&track_name=" + Uri.EscapeDataString(cleanTitle) +
                             (duration > TimeSpan.Zero ? "&duration=" + (int)duration.TotalSeconds : "");
                string synced = await Fetch(url);
                if (synced == null)
                {
                    // No exact match: search and take the first result that has synced lyrics.
                    string q = "https://lrclib.net/api/search?q=" + Uri.EscapeDataString((artist + " " + cleanTitle).Trim());
                    using var resp = await Http.GetAsync(q);
                    if (resp.IsSuccessStatusCode)
                    {
                        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                        foreach (var item in doc.RootElement.EnumerateArray())
                        {
                            if (item.TryGetProperty("syncedLyrics", out var sl) && sl.ValueKind == JsonValueKind.String)
                            {
                                synced = sl.GetString();
                                break;
                            }
                        }
                    }
                }
                if (!string.IsNullOrEmpty(synced)) lines = Parse(synced);
                reached = true;
            }
            catch (Exception ex) { App.Log("Versuri: " + ex.Message); }

            if (reached) lock (Cache) { if (Cache.Count > 300) Cache.Clear(); Cache[key] = lines; }
            return lines;
        }

        private static async Task<string> Fetch(string url)
        {
            using var resp = await Http.GetAsync(url);
            if (!resp.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            return doc.RootElement.TryGetProperty("syncedLyrics", out var sl) && sl.ValueKind == JsonValueKind.String ? sl.GetString() : null;
        }

        public static List<LyricLine> Parse(string lrc)
        {
            var list = new List<LyricLine>();
            foreach (var raw in lrc.Split('\n'))
            {
                var m = LineRx.Match(raw.Trim());
                if (!m.Success) continue;
                double sec = int.Parse(m.Groups[1].Value) * 60 + double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                list.Add(new LyricLine { Time = TimeSpan.FromSeconds(sec), Text = m.Groups[3].Value.Trim() });
            }
            return list.OrderBy(l => l.Time).ToList();
        }

        /// <summary>Index of the line being sung at <paramref name="pos"/>, or -1 before the first line.</summary>
        public static int IndexAt(List<LyricLine> lines, TimeSpan pos)
        {
            int idx = -1;
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i].Time <= pos + TimeSpan.FromMilliseconds(250)) idx = i;
                else break;
            }
            return idx;
        }
    }
}
