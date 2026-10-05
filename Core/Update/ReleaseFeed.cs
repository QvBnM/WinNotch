using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace WinNotch.Core.Update
{
    /// <summary>A WinNotch release published on GitHub, with its exe and signature.</summary>
    public sealed class UpdateInfo
    {
        public AppVersion Version;
        public bool PreRelease;
        public string Notes = "";
        public string ExeUrl, SigUrl;
        public long Size;
    }

    /// <summary>
    /// Reads GitHub's release JSON (one release from /releases/latest, or the list from /releases) and picks the
    /// version to offer. No network here: <see cref="Services.Updater"/> downloads, this decides.
    /// </summary>
    public static class ReleaseFeed
    {
        /// <summary>Usable releases in the JSON (an object or an array). Drafts, bad tags and missing/foreign files are skipped.</summary>
        public static List<UpdateInfo> Parse(string json, string repo, long maxExe)
        {
            var list = new List<UpdateInfo>();
            if (string.IsNullOrEmpty(json) || json.Length > 2_000_000) return list;
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var r in doc.RootElement.EnumerateArray().Take(50))
                        if (One(r, repo, maxExe) is UpdateInfo u) list.Add(u);
                }
                else if (One(doc.RootElement, repo, maxExe) is UpdateInfo u) list.Add(u);
            }
            catch (JsonException) { }
            return list;
        }

        private static UpdateInfo One(JsonElement root, string repo, long maxExe)
        {
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (root.TryGetProperty("draft", out var d) && d.ValueKind == JsonValueKind.True) return null;
            string tag = root.TryGetProperty("tag_name", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : "";
            if (!AppVersion.TryParse(tag, out var v)) return null;
            bool pre = (root.TryGetProperty("prerelease", out var p) && p.ValueKind == JsonValueKind.True) || v.IsPreRelease;
            var info = new UpdateInfo
            {
                Version = v, PreRelease = pre,
                Notes = root.TryGetProperty("body", out var b) && b.ValueKind == JsonValueKind.String ? b.GetString() ?? "" : ""
            };
            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
                foreach (var a in assets.EnumerateArray())
                {
                    if (a.ValueKind != JsonValueKind.Object) continue;
                    string name = a.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
                    string url = a.TryGetProperty("browser_download_url", out var u) && u.ValueKind == JsonValueKind.String ? u.GetString() : null;
                    if (url == null || !url.StartsWith("https://github.com/" + repo + "/releases/download/", StringComparison.OrdinalIgnoreCase)) continue;
                    if (name == "WinNotch.exe") { info.ExeUrl = url; info.Size = a.TryGetProperty("size", out var sz) && sz.TryGetInt64(out long l) ? l : 0; }
                    else if (name == "WinNotch.exe.sig") info.SigUrl = url;
                }
            return info.ExeUrl != null && info.SigUrl != null && info.Size > 0 && info.Size <= maxExe ? info : null;
        }

        /// <summary>
        /// The newest release worth offering: newer than <paramref name="current"/>, not refused (a version this PC rolled
        /// back from), and a final version unless the beta channel is on.
        /// </summary>
        public static UpdateInfo Pick(IEnumerable<UpdateInfo> releases, AppVersion current, bool beta, Func<AppVersion, bool> refused = null)
        {
            return (releases ?? Enumerable.Empty<UpdateInfo>())
                .Where(r => r != null && (beta || !r.PreRelease) && r.Version > current && !(refused?.Invoke(r.Version) ?? false))
                .OrderByDescending(r => r.Version)
                .FirstOrDefault();
        }
    }
}
