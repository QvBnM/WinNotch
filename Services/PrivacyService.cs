using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace WinNotch.Services
{
    public sealed class CapabilityUse
    {
        public bool InUse;
        public List<string> Apps = new List<string>();     // apps using it right now
        public DateTime Since;                              // when the current use started
        public string LastApp;                              // when not in use: who used it last
        public DateTime LastUsed;
    }

    /// <summary>
    /// Who is using the microphone or camera, read from the same place Windows uses for its privacy indicator
    /// (HKCU\...\CapabilityAccessManager\ConsentStore).
    /// </summary>
    public static class PrivacyService
    {
        private const string Root = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\";

        private static readonly Dictionary<string, (CapabilityUse Use, DateTime At)> Cache = new Dictionary<string, (CapabilityUse, DateTime)>();

        public static CapabilityUse Microphone() => Cached("microphone");
        public static CapabilityUse Camera() => Cached("webcam");

        /// <summary>The registry is read at most every 2 seconds; several places ask every second.</summary>
        private static CapabilityUse Cached(string capability)
        {
            lock (Cache)
            {
                if (Cache.TryGetValue(capability, out var c) && (DateTime.Now - c.At).TotalSeconds < 2) return c.Use;
                var use = Read(capability);
                Cache[capability] = (use, DateTime.Now);
                return use;
            }
        }

        private static CapabilityUse Read(string capability)
        {
            var use = new CapabilityUse();
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(Root + capability);
                if (key == null) return use;
                var entries = new List<(string name, long start, long stop)>();
                foreach (var sub in key.GetSubKeyNames())
                {
                    if (sub.Equals("NonPackaged", StringComparison.OrdinalIgnoreCase))
                    {
                        using var np = key.OpenSubKey(sub);
                        if (np == null) continue;
                        foreach (var app in np.GetSubKeyNames())
                        {
                            using var k = np.OpenSubKey(app);
                            entries.Add((NonPackagedName(app), ToLong(k?.GetValue("LastUsedTimeStart")), ToLong(k?.GetValue("LastUsedTimeStop"))));
                        }
                    }
                    else
                    {
                        using var k = key.OpenSubKey(sub);
                        entries.Add((PackagedName(sub), ToLong(k?.GetValue("LastUsedTimeStart")), ToLong(k?.GetValue("LastUsedTimeStop"))));
                    }
                }

                var active = entries.Where(e => e.start > 0 && e.stop == 0).ToList();
                use.InUse = active.Count > 0;
                use.Apps = active.Select(a => a.name).Distinct().ToList();
                if (use.InUse) use.Since = DateTime.FromFileTime(active.Max(a => a.start));

                var last = entries.Where(e => e.stop > 0).OrderByDescending(e => e.stop).FirstOrDefault();
                if (last.stop > 0)
                {
                    use.LastApp = last.name;
                    use.LastUsed = DateTime.FromFileTime(last.stop);
                }
            }
            catch (Exception ex) { App.Log("Confidențialitate: " + ex.Message); }
            return use;
        }

        private static long ToLong(object v) => v is long l ? l : v is int i ? i : 0;

        // "C:#Program Files#Discord#app-1.0#Discord.exe" → "Discord"
        private static string NonPackagedName(string key)
        {
            string file = key.Split('#').LastOrDefault() ?? key;
            return AudioSessionsService.Friendly(Path.GetFileNameWithoutExtension(file).ToLowerInvariant());
        }

        // "MSTeams_8wekyb3d8bbwe" → "Teams", "Microsoft.WindowsCamera_8wekyb3d8bbwe" → "Camera"
        private static string PackagedName(string key)
        {
            string name = key.Split('_')[0];
            int dot = name.LastIndexOf('.');
            if (dot >= 0) name = name.Substring(dot + 1);
            if (name.StartsWith("Windows")) name = name.Substring(7);
            if (name.StartsWith("MS")) name = name.Substring(2);
            return name.Length > 0 ? name : key;
        }
    }
}
