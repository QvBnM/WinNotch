using System;
using System.Collections.Generic;
using System.Linq;

namespace WinNotch.Core.Context
{
    /// <summary>One row of <see cref="AppCategories"/>.</summary>
    public sealed class AppCategoryEntry
    {
        public AppCategoryEntry(string name, AppCategory category, string display = null, bool prefix = false)
        {
            Name = AppCategories.Normalize(name);
            Category = category;
            Display = display;
            Prefix = prefix;
        }

        /// <summary>Normalized process name ("ms-teams"), or its beginning when <see cref="Prefix"/> is set ("gimp" → "gimp-2.10").</summary>
        public string Name { get; }
        public AppCategory Category { get; }
        /// <summary>Readable name ("Teams"), used for meetings; null = from the process name.</summary>
        public string Display { get; }
        /// <summary>Matches names that start with <see cref="Name"/> (apps that put their version in the exe name).</summary>
        public bool Prefix { get; }
    }

    /// <summary>
    /// Process name → kind of app. Names are compared lowercase, without path and ".exe", so "C:\…\Code.exe", "code.exe"
    /// and "Code" are the same; friendly names from the privacy indicator ("Teams", "Zoom") match too. Both new and old
    /// names of an app are listed (Teams: "ms-teams" and "teams"). To add an app, add a row to <see cref="DefaultEntries"/>;
    /// unknown names are <see cref="AppCategory.Other"/>.
    /// </summary>
    public sealed class AppCategories
    {
        private static AppCategoryEntry E(string n, AppCategory c, string display = null) => new AppCategoryEntry(n, c, display);
        private static AppCategoryEntry P(string n, AppCategory c, string display = null) => new AppCategoryEntry(n, c, display, prefix: true);

        public static readonly IReadOnlyList<AppCategoryEntry> DefaultEntries = new[]
        {
            // ---- development
            E("code", AppCategory.Dev), E("code - insiders", AppCategory.Dev), E("vscodium", AppCategory.Dev), E("cursor", AppCategory.Dev),
            E("windsurf", AppCategory.Dev), E("zed", AppCategory.Dev), E("devenv", AppCategory.Dev), E("rider64", AppCategory.Dev),
            E("idea64", AppCategory.Dev), E("idea", AppCategory.Dev), E("pycharm64", AppCategory.Dev), E("webstorm64", AppCategory.Dev),
            E("clion64", AppCategory.Dev), E("goland64", AppCategory.Dev), E("phpstorm64", AppCategory.Dev), E("rustrover64", AppCategory.Dev),
            E("datagrip64", AppCategory.Dev), E("studio64", AppCategory.Dev), E("sublime_text", AppCategory.Dev), E("notepad++", AppCategory.Dev),
            E("windowsterminal", AppCategory.Dev), E("wt", AppCategory.Dev), E("cmd", AppCategory.Dev), E("powershell", AppCategory.Dev),
            E("pwsh", AppCategory.Dev), E("mintty", AppCategory.Dev), E("git-bash", AppCategory.Dev), E("githubdesktop", AppCategory.Dev),
            E("postman", AppCategory.Dev), E("insomnia", AppCategory.Dev), E("docker desktop", AppCategory.Dev), E("gvim", AppCategory.Dev),
            E("ssms", AppCategory.Dev), E("azuredatastudio", AppCategory.Dev), E("wsl", AppCategory.Dev), E("wslhost", AppCategory.Dev),

            // ---- browsers
            E("chrome", AppCategory.Browser), E("msedge", AppCategory.Browser), E("firefox", AppCategory.Browser), E("opera", AppCategory.Browser),
            E("opera_gx", AppCategory.Browser), E("brave", AppCategory.Browser), E("vivaldi", AppCategory.Browser), E("arc", AppCategory.Browser),
            E("zen", AppCategory.Browser), E("librewolf", AppCategory.Browser), E("waterfox", AppCategory.Browser), E("floorp", AppCategory.Browser),
            E("iexplore", AppCategory.Browser), E("thorium", AppCategory.Browser), E("chromium", AppCategory.Browser), E("browser", AppCategory.Browser),
            E("edge", AppCategory.Browser),

            // ---- meetings (new and old names)
            E("ms-teams", AppCategory.Meeting, "Teams"), E("teams", AppCategory.Meeting, "Teams"), E("msteams", AppCategory.Meeting, "Teams"),
            E("zoom", AppCategory.Meeting, "Zoom"), E("cpthost", AppCategory.Meeting, "Zoom"), E("zoomworkplace", AppCategory.Meeting, "Zoom"),
            E("webex", AppCategory.Meeting, "Webex"), E("ciscocollabhost", AppCategory.Meeting, "Webex"), E("webexmta", AppCategory.Meeting, "Webex"),
            E("atmgr", AppCategory.Meeting, "Webex"), E("skype", AppCategory.Meeting, "Skype"), E("skypeapp", AppCategory.Meeting, "Skype"),
            E("lync", AppCategory.Meeting, "Skype for Business"), E("gotomeeting", AppCategory.Meeting, "GoTo Meeting"), E("goto", AppCategory.Meeting, "GoTo Meeting"),
            E("jitsi meet", AppCategory.Meeting, "Jitsi"), E("bluejeans", AppCategory.Meeting, "BlueJeans"), E("ringcentral", AppCategory.Meeting, "RingCentral"),

            // ---- games and launchers
            E("steam", AppCategory.Game), E("steamwebhelper", AppCategory.Game), E("epicgameslauncher", AppCategory.Game), E("battle.net", AppCategory.Game),
            E("riotclientservices", AppCategory.Game), E("eadesktop", AppCategory.Game), E("ubisoftconnect", AppCategory.Game), E("upc", AppCategory.Game),
            E("galaxyclient", AppCategory.Game), E("xboxapp", AppCategory.Game), E("gamebar", AppCategory.Game), E("cs2", AppCategory.Game),
            E("csgo", AppCategory.Game), E("dota2", AppCategory.Game), E("valorant", AppCategory.Game), E("valorant-win64-shipping", AppCategory.Game),
            E("league of legends", AppCategory.Game), E("leagueclient", AppCategory.Game), E("fortniteclient-win64-shipping", AppCategory.Game),
            E("r5apex", AppCategory.Game), E("r5apex_dx12", AppCategory.Game), E("overwatch", AppCategory.Game), E("gta5", AppCategory.Game),
            E("gta5_enhanced", AppCategory.Game), E("rdr2", AppCategory.Game), E("eldenring", AppCategory.Game), E("cyberpunk2077", AppCategory.Game),
            E("witcher3", AppCategory.Game), E("bg3", AppCategory.Game), E("bg3_dx11", AppCategory.Game), E("rocketleague", AppCategory.Game),
            E("minecraft", AppCategory.Game), E("minecraft.windows", AppCategory.Game), E("robloxplayerbeta", AppCategory.Game),
            E("eurotrucks2", AppCategory.Game), E("amtrucks", AppCategory.Game), E("cod", AppCategory.Game), E("forzahorizon5", AppCategory.Game),
            E("helldivers2", AppCategory.Game), E("pubg", AppCategory.Game), E("tslgame", AppCategory.Game), E("rainbowsix", AppCategory.Game),
            P("fc2", AppCategory.Game), P("fifa", AppCategory.Game),

            // ---- media
            E("spotify", AppCategory.Media), E("vlc", AppCategory.Media), E("wmplayer", AppCategory.Media), E("music.ui", AppCategory.Media),
            E("microsoft.media.player", AppCategory.Media), E("zunemusic", AppCategory.Media), E("zunevideo", AppCategory.Media), E("video.ui", AppCategory.Media),
            E("mpc-hc64", AppCategory.Media), E("mpc-hc", AppCategory.Media), E("mpc-be64", AppCategory.Media), E("mpv", AppCategory.Media),
            E("potplayermini64", AppCategory.Media), E("potplayermini", AppCategory.Media), E("itunes", AppCategory.Media), E("applemusic", AppCategory.Media),
            E("tidal", AppCategory.Media), E("deezer", AppCategory.Media), E("foobar2000", AppCategory.Media), E("aimp", AppCategory.Media),
            E("musicbee", AppCategory.Media), E("netflix", AppCategory.Media), E("plex", AppCategory.Media), E("kodi", AppCategory.Media),
            E("stremio", AppCategory.Media), E("jellyfin media player", AppCategory.Media), E("amazon music", AppCategory.Media),

            // ---- office and notes
            E("winword", AppCategory.Office), E("excel", AppCategory.Office), E("powerpnt", AppCategory.Office), E("outlook", AppCategory.Office),
            E("olk", AppCategory.Office), E("onenote", AppCategory.Office), E("onenoteim", AppCategory.Office), E("msaccess", AppCategory.Office),
            E("mspub", AppCategory.Office), E("visio", AppCategory.Office), E("winproj", AppCategory.Office), E("acrord32", AppCategory.Office),
            E("acrobat", AppCategory.Office), E("sumatrapdf", AppCategory.Office), E("soffice", AppCategory.Office), E("soffice.bin", AppCategory.Office),
            E("notion", AppCategory.Office), E("obsidian", AppCategory.Office), E("evernote", AppCategory.Office), E("thunderbird", AppCategory.Office),
            E("wps", AppCategory.Office), E("et", AppCategory.Office), E("wpp", AppCategory.Office), E("notepad", AppCategory.Office),

            // ---- creative apps
            E("photoshop", AppCategory.Creator), E("illustrator", AppCategory.Creator), E("afterfx", AppCategory.Creator), E("indesign", AppCategory.Creator),
            E("lightroom", AppCategory.Creator), E("lightroomclassic", AppCategory.Creator), P("adobe premiere", AppCategory.Creator), E("premiere pro", AppCategory.Creator),
            E("audition", AppCategory.Creator), E("animate", AppCategory.Creator), E("figma", AppCategory.Creator), E("blender", AppCategory.Creator),
            E("resolve", AppCategory.Creator), E("obs64", AppCategory.Creator), E("obs32", AppCategory.Creator), E("obs", AppCategory.Creator),
            P("gimp", AppCategory.Creator), E("krita", AppCategory.Creator), E("inkscape", AppCategory.Creator), E("audacity", AppCategory.Creator),
            E("fl64", AppCategory.Creator), E("fl", AppCategory.Creator), P("ableton live", AppCategory.Creator), E("reaper", AppCategory.Creator),
            E("capcut", AppCategory.Creator), E("clipchamp", AppCategory.Creator), E("canva", AppCategory.Creator), E("photo", AppCategory.Creator),
            E("designer", AppCategory.Creator), E("paintdotnet", AppCategory.Creator), E("mspaint", AppCategory.Creator),
            P("vegas", AppCategory.Creator), E("shotcut", AppCategory.Creator), E("kdenlive", AppCategory.Creator), E("handbrake", AppCategory.Creator),
        };

        public static AppCategories Default { get; } = new AppCategories(DefaultEntries);

        private readonly Dictionary<string, AppCategoryEntry> _exact = new Dictionary<string, AppCategoryEntry>(StringComparer.Ordinal);
        private readonly List<AppCategoryEntry> _prefixes = new List<AppCategoryEntry>();

        /// <param name="entries">Later rows win over earlier ones with the same name (extra rows can override the defaults).</param>
        public AppCategories(IEnumerable<AppCategoryEntry> entries)
        {
            foreach (var e in entries ?? Enumerable.Empty<AppCategoryEntry>())
            {
                if (e == null || e.Name.Length == 0) continue;
                if (e.Prefix) _prefixes.Add(e);
                else _exact[e.Name] = e;
            }
            _prefixes.Sort((a, b) => b.Name.Length.CompareTo(a.Name.Length));     // the longest beginning wins
        }

        /// <summary>The defaults plus more rows (which win on the same name).</summary>
        public AppCategories With(params AppCategoryEntry[] more) => new AppCategories(DefaultEntries.Concat(more ?? Array.Empty<AppCategoryEntry>()));

        /// <summary>"C:\Apps\Code.EXE" → "code"; null → "".</summary>
        public static string Normalize(string process)
        {
            if (string.IsNullOrWhiteSpace(process)) return "";
            string s = process.Trim();
            int slash = Math.Max(s.LastIndexOf('\\'), s.LastIndexOf('/'));
            if (slash >= 0) s = s.Substring(slash + 1);
            s = s.ToLowerInvariant();
            if (s.EndsWith(".exe", StringComparison.Ordinal)) s = s.Substring(0, s.Length - 4);
            return s.Trim();
        }

        public AppCategoryEntry Find(string process)
        {
            string n = Normalize(process);
            if (n.Length == 0) return null;
            if (_exact.TryGetValue(n, out var e)) return e;
            return _prefixes.FirstOrDefault(p => n.StartsWith(p.Name, StringComparison.Ordinal));
        }

        public AppCategory Categorize(string process) => Find(process)?.Category ?? AppCategory.Other;

        /// <summary>Readable name: the row's display name, else the process name with a capital ("zoom" → "Zoom").</summary>
        public string DisplayName(string process)
        {
            var e = Find(process);
            if (e?.Display != null) return e.Display;
            string n = Normalize(process);
            return n.Length == 0 ? "" : char.ToUpperInvariant(n[0]) + n.Substring(1);
        }
    }
}
