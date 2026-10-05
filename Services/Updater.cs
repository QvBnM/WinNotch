using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace WinNotch.Services
{
    /// <summary>A newer WinNotch published on GitHub.</summary>
    public sealed class UpdateInfo
    {
        public Version Version;
        public string Notes = "";
        public string ExeUrl, SigUrl;
        public long Size;
    }

    /// <summary>
    /// Automatic updates from the project's GitHub Releases.
    ///
    /// GitHub builds WinNotch.exe from the code (Actions, on every version tag) and signs it with the WinNotch release
    /// key (ECDSA P-256, the private half lives only in GitHub's encrypted secrets). This app only installs a file whose
    /// signature matches the public key built into it, for exactly the version the release names, and only if that
    /// version is newer. So even someone who took over the GitHub repository or the download could not push their own
    /// exe, nor an older (vulnerable) one.
    /// </summary>
    public static class Updater
    {
        /// <summary>"owner/repo" on GitHub. Empty: updates are off (no repository set up yet).</summary>
        public const string Repo = "QvBnM/winnotch";

        private const string PublicKey = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEqSim56n+26WPzvTCdWKQ6qbRrldPd4SzzzPvuuwPWM4MYvjnowV+ERtSYsIzCsWyOqr2o3M7SrAiUCrRv1AZFQ==";
        private const long MaxExe = 300L * 1024 * 1024;
        public const string UpdatedArg = "--updated";

        public static bool Configured => Repo.Length > 0;
        public static Version Current { get; } = Normalize(Assembly.GetExecutingAssembly().GetName().Version);

        private static readonly HttpClient Http = CreateClient();

        private static HttpClient CreateClient()
        {
            var c = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = true, MaxAutomaticRedirections = 5, ConnectTimeout = TimeSpan.FromSeconds(10) })
                { Timeout = Timeout.InfiniteTimeSpan };
            c.DefaultRequestHeaders.UserAgent.ParseAdd("WinNotch/" + Current);
            return c;
        }

        private static Version Normalize(Version v) => v == null ? new Version(0, 0, 0) : new Version(v.Major, v.Minor, Math.Max(0, v.Build));

        /// <summary>The latest release, if it's newer than this one; null otherwise (or offline).</summary>
        public static async Task<UpdateInfo> CheckAsync()
        {
            if (!Configured) return null;
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/" + Repo + "/releases/latest");
                req.Headers.Accept.ParseAdd("application/vnd.github+json");
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                using var resp = await Http.SendAsync(req, cts.Token);
                if (!resp.IsSuccessStatusCode) return null;
                var text = await resp.Content.ReadAsStringAsync(cts.Token);
                if (text.Length > 2_000_000) return null;
                using var doc = JsonDocument.Parse(text);
                var root = doc.RootElement;
                string tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
                if (!Version.TryParse(tag.TrimStart('v', 'V'), out var v)) return null;
                v = Normalize(v);
                if (v <= Current) return null;
                var info = new UpdateInfo { Version = v, Notes = root.TryGetProperty("body", out var b) ? (b.GetString() ?? "") : "" };
                if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
                    foreach (var a in assets.EnumerateArray())
                    {
                        string name = a.GetProperty("name").GetString(), url = a.GetProperty("browser_download_url").GetString();
                        if (url == null || !url.StartsWith("https://github.com/" + Repo + "/releases/download/", StringComparison.OrdinalIgnoreCase)) continue;
                        if (name == "WinNotch.exe") { info.ExeUrl = url; info.Size = a.TryGetProperty("size", out var sz) ? sz.GetInt64() : 0; }
                        else if (name == "WinNotch.exe.sig") info.SigUrl = url;
                    }
                return info.ExeUrl != null && info.SigUrl != null && info.Size > 0 && info.Size <= MaxExe ? info : null;
            }
            catch (Exception ex) { App.Log("Actualizare, verificare: " + ex.Message); return null; }
        }

        private static string UpdateFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinNotch", "update");

        /// <summary>Downloads the new exe and checks its signature. Returns the verified file, or null.</summary>
        public static async Task<string> DownloadAsync(UpdateInfo u, IProgress<double> progress, CancellationToken ct)
        {
            Directory.CreateDirectory(UpdateFolder);
            foreach (var old in Directory.GetFiles(UpdateFolder)) try { File.Delete(old); } catch { }
            string path = Path.Combine(UpdateFolder, "WinNotch-" + u.Version + ".exe");

            byte[] sig;
            using (var r = await Http.GetAsync(u.SigUrl, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                r.EnsureSuccessStatusCode();
                var s = await r.Content.ReadAsStringAsync(ct);
                if (s.Length > 1000) throw new InvalidDataException("semnătură invalidă");
                sig = Convert.FromBase64String(s.Trim());
            }

            using (var r = await Http.GetAsync(u.ExeUrl, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                r.EnsureSuccessStatusCode();
                using var src = await r.Content.ReadAsStreamAsync(ct);
                using var dst = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
                var buf = new byte[81920];
                long total = 0;
                int n;
                while ((n = await src.ReadAsync(buf, ct)) > 0)
                {
                    total += n;
                    if (total > MaxExe) throw new InvalidDataException("fișier prea mare");
                    await dst.WriteAsync(buf.AsMemory(0, n), ct);
                    progress?.Report(u.Size > 0 ? Math.Min(1, (double)total / u.Size) : 0);
                }
            }

            if (!Verify(path, u.Version, sig))
            {
                try { File.Delete(path); } catch { }
                App.Log("Actualizare: semnătura nu se potrivește, fișierul a fost șters.");
                return null;
            }
            return path;
        }

        /// <summary>The release key signed "WinNotch-release|version|sha256 of the exe".</summary>
        public static bool Verify(string file, Version version, byte[] signature)
        {
            try
            {
                string hash;
                using (var fs = File.OpenRead(file)) hash = Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
                using var key = ECDsa.Create();
                key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(PublicKey), out _);
                var msg = Encoding.UTF8.GetBytes("WinNotch-release|" + version + "|" + hash);
                return key.VerifyData(msg, signature, HashAlgorithmName.SHA256);
            }
            catch { return false; }
        }

        /// <summary>
        /// Puts the verified exe in place of the running one and starts it. The running exe can't be overwritten but
        /// can be renamed: it becomes WinNotch.old.exe (deleted by the new version on start).
        /// </summary>
        public static bool Apply(string newExe, Action beforeStart)
        {
            string me = Environment.ProcessPath, old = Path.Combine(Path.GetDirectoryName(me), "WinNotch.old.exe");
            try
            {
                App.ReleaseOwnExe();
                if (File.Exists(old)) File.Delete(old);
                File.Move(me, old);
                try { File.Copy(newExe, me, false); }
                catch { File.Move(old, me); throw; }              // put the running version back
                File.SetLastWriteTimeUtc(me, DateTime.UtcNow);
                beforeStart?.Invoke();
                Process.Start(new ProcessStartInfo(me, UpdatedArg) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(me) });
                return true;
            }
            catch (Exception ex)
            {
                App.Log("Actualizare, înlocuire: " + ex.Message);
                App.LockOwnExe();
                return false;
            }
        }

        /// <summary>After an update: removes the previous version's exe and the download.</summary>
        public static void CleanUp()
        {
            string old = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath), "WinNotch.old.exe");
            Task.Run(async () =>
            {
                for (int i = 0; i < 20 && File.Exists(old); i++)
                {
                    try { File.Delete(old); } catch { await Task.Delay(500); }
                }
                try { if (Directory.Exists(UpdateFolder)) foreach (var f in Directory.GetFiles(UpdateFolder)) File.Delete(f); } catch { }
            });
        }

        /// <summary>
        /// Release notes as (kind, text) items. The notes are Markdown with a heading per kind ("## Nou",
        /// "## Îmbunătățit", "## Modificat", "## Reparat") and a "- " line per change.
        /// </summary>
        public static System.Collections.Generic.List<(string Kind, string Text)> ParseNotes(string md)
        {
            var list = new System.Collections.Generic.List<(string, string)>();
            string kind = "Nou";
            foreach (var raw in (md ?? "").Replace("\r", "").Split('\n'))
            {
                var line = raw.Trim();
                if (line.StartsWith("#")) { kind = line.TrimStart('#', ' ').Trim(); continue; }
                if (line.StartsWith("- ") || line.StartsWith("* ")) list.Add((kind, line.Substring(2).Trim()));
            }
            return list;
        }

        /// <summary>The notes of the version running now (built into the exe).</summary>
        public static string OwnNotes()
        {
            try
            {
                using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("notes.md");
                if (s == null) return "";
                using var r = new StreamReader(s, Encoding.UTF8);
                return r.ReadToEnd();
            }
            catch { return ""; }
        }

        /// <summary>The first meaningful lines of the release notes, for the notch.</summary>
        public static string Summary(string notes, int max = 120)
        {
            var line = (notes ?? "").Split('\n').Select(l => l.Trim().TrimStart('-', '*', '#', ' ')).FirstOrDefault(l => l.Length > 0) ?? "";
            return line.Length > max ? line.Substring(0, max - 1) + "…" : line;
        }
    }
}
