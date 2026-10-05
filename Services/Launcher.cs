using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace WinNotch.Services
{
    public sealed class LaunchItem
    {
        public string Name;
        public string Target;      // path, folder or ms-settings: URI
        public string Kind;        // "Aplicație", "Setare", "Folder"
    }

    /// <summary>Type-to-open for apps (Start menu), Windows settings and common folders, plus quick math with "=".</summary>
    public static class Launcher
    {
        private static List<LaunchItem> _items = new List<LaunchItem>();

        private static readonly (string Name, string Uri)[] Settings =
        {
            ("Bluetooth și dispozitive", "ms-settings:bluetooth"), ("Sunet", "ms-settings:sound"), ("Ecran", "ms-settings:display"),
            ("Wi-Fi", "ms-settings:network-wifi"), ("Rețea și internet", "ms-settings:network"), ("Windows Update", "ms-settings:windowsupdate"),
            ("Aplicații instalate", "ms-settings:appsfeatures"), ("Pornire aplicații", "ms-settings:startupapps"), ("Baterie și alimentare", "ms-settings:powersleep"),
            ("Personalizare", "ms-settings:personalization"), ("Fundal", "ms-settings:personalization-background"), ("Culori", "ms-settings:colors"),
            ("Confidențialitate cameră", "ms-settings:privacy-webcam"), ("Confidențialitate microfon", "ms-settings:privacy-microphone"),
            ("Notificări", "ms-settings:notifications"), ("Stocare", "ms-settings:storagesense"), ("Imprimante", "ms-settings:printers"),
            ("Mouse", "ms-settings:mousetouchpad"), ("Tastatură", "ms-settings:typing"), ("Ora și limba", "ms-settings:dateandtime"),
            ("Manager de activități", "taskmgr.exe"), ("Panou de control", "control.exe")
        };

        public static void IndexAsync()
        {
            Task.Run(() =>
            {
                var list = new List<LaunchItem>();
                foreach (var root in new[]
                {
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
                    Environment.GetFolderPath(Environment.SpecialFolder.StartMenu)
                })
                {
                    try
                    {
                        if (!Directory.Exists(root)) continue;
                        foreach (var f in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
                        {
                            string ext = Path.GetExtension(f).ToLowerInvariant();
                            if (ext != ".lnk" && ext != ".url") continue;
                            string name = Path.GetFileNameWithoutExtension(f);
                            if (name.IndexOf("uninstall", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("dezinstal", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                            if (list.Any(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) continue;
                            list.Add(new LaunchItem { Name = name, Target = f, Kind = "Aplicație" });
                        }
                    }
                    catch { }
                }
                foreach (var (n, u) in Settings) list.Add(new LaunchItem { Name = n, Target = u, Kind = "Setare" });
                foreach (var (n, sf) in new[] { ("Descărcări", "Downloads"), ("Documente", "Documents"), ("Imagini", "Pictures"), ("Desktop", "Desktop") })
                {
                    string p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), sf);
                    if (Directory.Exists(p)) list.Add(new LaunchItem { Name = n, Target = p, Kind = "Folder" });
                }
                _items = list;
            });
        }

        public static List<LaunchItem> Search(string q, int max = 5)
        {
            q = (q ?? "").Trim();
            if (q.Length == 0) return new List<LaunchItem>();
            string ql = q.ToLowerInvariant();
            return _items
                .Select(i => (i, s: Score(i.Name.ToLowerInvariant(), ql)))
                .Where(x => x.s > 0)
                .OrderByDescending(x => x.s).ThenBy(x => x.i.Name.Length)
                .Take(max).Select(x => x.i).ToList();
        }

        private static int Score(string name, string q)
        {
            if (name == q) return 100;
            if (name.StartsWith(q)) return 80;
            if (name.Split(' ', '-', '.').Any(w => w.StartsWith(q))) return 60;
            if (name.Contains(q)) return 40;
            // initials: "vsc" → Visual Studio Code
            var initials = new string(name.Split(' ').Where(w => w.Length > 0).Select(w => w[0]).ToArray());
            return initials.StartsWith(q) ? 30 : 0;
        }

        public static void Open(LaunchItem item)
        {
            try { Shell.Open(item.Target); }
            catch (Exception ex) { App.Log("Lansare: " + ex.Message); }
        }

        /// <summary>"=250*1,19" or "(3+4)/2" → "297,5". Returns null if the text isn't a calculation.</summary>
        public static string Calculate(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            string expr = text.Trim();
            bool forced = expr.StartsWith("=");
            if (forced) expr = expr.Substring(1);
            expr = expr.Replace(',', '.').Replace('×', '*').Replace('x', '*').Replace('÷', '/').Replace(" ", "");
            if (!Regex.IsMatch(expr, @"^[0-9\.\+\-\*/\(\)%]+$")) return null;
            if (!forced && !Regex.IsMatch(expr, @"\d[\+\-\*/%]")) return null;    // "2024" alone is a search, not math
            try
            {
                expr = Regex.Replace(expr, @"(\d+(?:\.\d+)?)%", "($1/100)");
                var v = Convert.ToDouble(new DataTable().Compute(expr, null), CultureInfo.InvariantCulture);
                if (double.IsNaN(v) || double.IsInfinity(v)) return null;
                return Math.Round(v, 6).ToString("#,0.######", new CultureInfo("ro-RO"));
            }
            catch { return null; }
        }
    }
}
