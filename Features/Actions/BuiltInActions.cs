using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WinNotch.Core.Actions;

namespace WinNotch.Features.Actions
{
    /// <summary>
    /// The things WinNotch already does, as actions: each one only calls the existing service or notch command through
    /// <see cref="IBuiltInHost"/>. Registered once at startup (App.xaml.cs). The buttons and panes keep working as before.
    /// </summary>
    public static class BuiltInActions
    {
        /// <summary>Icon-font glyphs (Segoe Fluent Icons / MDL2), the same font the notch uses.</summary>
        internal const string GVolume = "", GMute = "", GMic = "", GPlay = "", GNext = "", GPrev = "",
            GCamera = "", GCrop = "", GText = "", GMemory = "", GPin = "", GMonitor = "",
            GHalf = "", GMini = "", GWorkspace = "", GEject = "", GSettings = "", GNotch = "",
            GSpeed = "", GBluetooth = "", GWifi = "", GUpdate = "";

        private const string CatAudio = "Sunet", CatMedia = "Muzică", CatTools = "Unelte", CatWindow = "Fereastra activă",
            CatSettings = "Setări Windows", CatApp = "WinNotch";

        /// <summary>All fixed built-in actions (for registering and for the tests).</summary>
        public static IReadOnlyList<ActionDescriptor> Create(IBuiltInHost h)
        {
            Task<ActionResult> Do(Action a, string message = "") { a(); return ActionResult.OkTask(message); }

            var list = new List<ActionDescriptor>
            {
                // ---- audio
                new UndoableAction("audio.volume-set", "Setează volumul",
                    (args, ct) =>
                    {
                        int before = h.Volume, v = args.GetInt("valoare");
                        h.SetVolume(v);
                        return ActionResult.OkTask("Volum " + v + "%", new UndoToken("audio.volume-set", before.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                    },
                    (token, ct) =>
                    {
                        if (!int.TryParse(token.State, out int v) || v < 0 || v > 100) return Task.FromResult(ActionResult.Failed("Nu am ce anula."));
                        h.SetVolume(v);
                        return ActionResult.OkTask("Volum " + v + "%");
                    })
                {
                    Aliases = new[] { "volum", "volume", "set volume", "sunet la", "dă volumul" }, Category = CatAudio, Icon = GVolume,
                    Parameters = new[] { ActionParameter.Percent("valoare", "Volum") }, RequiresUiThread = true,
                },
                new UndoableAction("audio.mute", "Oprește / pornește sunetul",
                    (args, ct) =>
                    {
                        bool before = h.Muted;
                        h.ToggleMute();
                        return ActionResult.OkTask(before ? "Sunet pornit" : "Sunet oprit", new UndoToken("audio.mute", before ? "1" : "0"));
                    },
                    (token, ct) =>
                    {
                        bool want = token.State == "1";
                        if (h.Muted != want) h.ToggleMute();
                        return ActionResult.OkTask(want ? "Sunet oprit" : "Sunet pornit");
                    })
                {
                    Aliases = new[] { "mute", "unmute", "mut", "fără sunet", "liniște", "taie sunetul" }, Category = CatAudio, Icon = GMute, RequiresUiThread = true,
                },
                new UndoableAction("audio.mute-mic", "Mută / pornește microfonul",
                    (args, ct) =>
                    {
                        bool before = h.MicMuted;
                        h.MicMuted = !before;
                        return ActionResult.OkTask(before ? "Microfon pornit" : "Microfon oprit (în toate aplicațiile)", new UndoToken("audio.mute-mic", before ? "1" : "0"));
                    },
                    (token, ct) =>
                    {
                        h.MicMuted = token.State == "1";
                        return ActionResult.OkTask(h.MicMuted ? "Microfon oprit" : "Microfon pornit");
                    })
                {
                    Aliases = new[] { "mute mic", "mute microphone", "taie microfonul", "microfon", "mic" }, Category = CatAudio, Icon = GMic, RequiresUiThread = true,
                },

                // ---- media
                new ActionDescriptor("media.play-pause", "Redă / pune pe pauză", (a, ct) => Do(h.PlayPause), () => h.HasMedia)
                    { Aliases = new[] { "play", "pause", "pauză", "redă", "oprește muzica" }, Category = CatMedia, Icon = GPlay, RequiresUiThread = true, UnavailableMessage = "Nu se redă nimic acum." },
                new ActionDescriptor("media.next", "Piesa următoare", (a, ct) => Do(h.Next), () => h.HasMedia)
                    { Aliases = new[] { "next", "skip", "înainte", "următoarea" }, Category = CatMedia, Icon = GNext, RequiresUiThread = true, UnavailableMessage = "Nu se redă nimic acum." },
                new ActionDescriptor("media.previous", "Piesa anterioară", (a, ct) => Do(h.Previous), () => h.HasMedia)
                    { Aliases = new[] { "previous", "back", "înapoi", "precedenta" }, Category = CatMedia, Icon = GPrev, RequiresUiThread = true, UnavailableMessage = "Nu se redă nimic acum." },

                // ---- screen tools (the notch hides itself first, as with the buttons)
                new ActionDescriptor("tools.screenshot", "Captură ecran", (a, ct) => Do(h.ScreenshotFull))
                    { Aliases = new[] { "screenshot", "print screen", "poză ecran", "captura" }, Category = CatTools, Icon = GCamera, RequiresUiThread = true },
                new ActionDescriptor("tools.screenshot-area", "Captură zonă", (a, ct) => Do(h.ScreenshotArea))
                    { Aliases = new[] { "snip", "decupează", "screenshot area", "captură parțială" }, Category = CatTools, Icon = GCrop, RequiresUiThread = true },
                new ActionDescriptor("tools.ocr", "Text din ecran", (a, ct) => Do(h.TextFromScreen))
                    { Aliases = new[] { "ocr", "copiază text", "recunoaștere text", "text from screen" }, Category = CatTools, Icon = GText, RequiresUiThread = true },
                new ActionDescriptor("tools.free-ram", "Eliberează RAM", (a, ct) => Do(h.FreeMemory))
                    { Aliases = new[] { "memorie", "ram", "optimize", "free memory", "curăță memoria" }, Category = CatTools, Icon = GMemory, RequiresUiThread = true },

                // ---- the active window (the last app you were in)
                Win("window.topmost", "Fereastra activă deasupra", WindowCommand.Topmost, GPin, "always on top", "pin", "deasupra", "ține deasupra"),
                Win("window.next-monitor", "Mută fereastra pe celălalt monitor", WindowCommand.NextMonitor, GMonitor, "monitor 2", "move to monitor", "alt ecran"),
                Win("window.half", "Fereastra pe jumătate (stânga / dreapta)", WindowCommand.Half, GHalf, "snap", "jumătate", "half", "split", "stânga", "dreapta"),
                Win("window.mini", "Fereastra mică în colț", WindowCommand.Mini, GMini, "mini", "picture in picture", "colț", "mică"),

                // ---- Windows settings (ms-settings links, as in the launcher)
                Setting("settings.bluetooth", "Setări Bluetooth", "ms-settings:bluetooth", GBluetooth, "bluetooth", "dispozitive", "căști"),
                Setting("settings.sound", "Setări sunet", "ms-settings:sound", GVolume, "sound settings", "audio", "difuzoare"),
                Setting("settings.display", "Setări ecran", "ms-settings:display", GMonitor, "display", "rezoluție", "luminozitate"),
                Setting("settings.wifi", "Setări Wi-Fi", "ms-settings:network-wifi", GWifi, "wifi", "wireless", "rețea"),
                Setting("settings.update", "Windows Update", "ms-settings:windowsupdate", GUpdate, "update", "actualizări windows", "actualizare"),

                // ---- WinNotch itself
                new ActionDescriptor("winnotch.open", "Deschide notch-ul", (a, ct) => Do(h.OpenNotch))
                    { Aliases = new[] { "open notch", "notch", "deschide" }, Category = CatApp, Icon = GNotch, RequiresUiThread = true },
                new ActionDescriptor("winnotch.settings", "Deschide setările WinNotch", (a, ct) => Do(h.OpenSettings))
                    { Aliases = new[] { "settings", "setări", "preferințe", "configurare" }, Category = CatApp, Icon = GSettings, RequiresUiThread = true },
                new ActionDescriptor("winnotch.speed-test", "Test de viteză internet", (a, ct) => Do(h.StartSpeedTest))
                    { Aliases = new[] { "speedtest", "speed test", "viteză internet", "internet", "ping" }, Category = CatApp, Icon = GSpeed, RequiresUiThread = true },
            };
            return list;

            ActionDescriptor Win(string id, string title, WindowCommand c, string icon, params string[] aliases) =>
                new ActionDescriptor(id, title, (a, ct) => Do(() => h.Window(c)),
                                     () => h.HasTargetWindow && (c != WindowCommand.NextMonitor || h.MonitorCount > 1))
                {
                    Aliases = aliases, Category = CatWindow, Icon = icon, RequiresUiThread = true,
                    UnavailableMessage = c == WindowCommand.NextMonitor ? "E nevoie de o fereastră activă și de un al doilea monitor." : "Nu e nicio fereastră activă.",
                };

            ActionDescriptor Setting(string id, string title, string uri, string icon, params string[] aliases) =>
                new ActionDescriptor(id, title, (a, ct) => Do(() => h.OpenUri(uri))) { Aliases = aliases, Category = CatSettings, Icon = icon };
        }

        /// <summary>Registers the fixed actions and the two dynamic lists (workspaces, removable drives).</summary>
        public static void Register(ActionRegistry r, IBuiltInHost h)
        {
            foreach (var a in Create(h)) r.Register(a);
            r.RegisterProvider(new WorkspaceActions(h));
            r.RegisterProvider(new DriveActions(h));
        }

        /// <summary>"Lucru de acasă" → "lucru-de-acasa" (for dynamic ids).</summary>
        public static string Slug(string name, int max = 30)
        {
            var b = new StringBuilder();
            foreach (char c in ActionRegistry.Fold(name))
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')) b.Append(c);
                else if (b.Length > 0 && b[^1] != '-') b.Append('-');
            string s = b.ToString().Trim('-');
            if (s.Length > max) s = s.Substring(0, max).Trim('-');
            return s;
        }
    }

    /// <summary>"Deschide spațiul „Lucru”" for every saved workspace.</summary>
    public sealed class WorkspaceActions : IActionProvider
    {
        private readonly IBuiltInHost _h;
        public WorkspaceActions(IBuiltInHost h) { _h = h; }

        public event Action Changed;

        /// <summary>Workspaces were added, renamed or deleted.</summary>
        public void Invalidate() => Changed?.Invoke();

        public IEnumerable<ActionDescriptor> GetActions()
        {
            var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (var name in _h.WorkspaceNames() ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                string slug = BuiltInActions.Slug(name);
                if (slug.Length == 0) slug = "spatiu";
                string id = "workspace.open-" + slug;
                for (int i = 2; !used.Add(id); i++) id = "workspace.open-" + slug + "-" + i;
                string n = name;
                yield return new ActionDescriptor(id, "Deschide spațiul „" + n + "”", async (a, ct) =>
                {
                    await _h.OpenWorkspaceAsync(n).ConfigureAwait(true);
                    return ActionResult.Ok("Spațiul „" + n + "” e deschis");
                })
                {
                    Aliases = new[] { "spațiu de lucru " + n, "workspace " + n, n }, Category = "Spații de lucru", Icon = BuiltInActions.GWorkspace,
                    RequiresUiThread = true, Timeout = TimeSpan.FromSeconds(60),          // reopening apps takes a while
                };
            }
        }
    }

    /// <summary>"Scoate <unitate>" for every removable drive (asks for confirmation).</summary>
    public sealed class DriveActions : IActionProvider
    {
        private readonly IBuiltInHost _h;
        public DriveActions(IBuiltInHost h) { _h = h; }

        public event Action Changed;

        /// <summary>A drive was plugged in or removed.</summary>
        public void Invalidate() => Changed?.Invoke();

        public IEnumerable<ActionDescriptor> GetActions()
        {
            foreach (var d in _h.RemovableDrives() ?? Array.Empty<DriveItem>())
            {
                if (d?.Root == null || d.Root.Length < 2 || !char.IsLetter(d.Root[0]) || d.Root[1] != ':') continue;
                string letter = char.ToLowerInvariant(d.Root[0]).ToString();
                string root = char.ToUpperInvariant(d.Root[0]) + ":\\";
                string label = string.IsNullOrWhiteSpace(d.Name) ? "unitatea" : d.Name.Trim();
                yield return new ActionDescriptor("device.eject-" + letter, "Scoate " + label + " (" + root.TrimEnd('\\') + ")", (a, ct) =>
                    Task.FromResult(_h.Eject(root) ? ActionResult.Ok("Poți scoate " + label) : ActionResult.Failed("Unitatea nu a putut fi scoasă (e folosită?).")),
                    () => _h.RemovableDrives()?.Any(x => string.Equals(x?.Root, d.Root, StringComparison.OrdinalIgnoreCase)) ?? false)
                {
                    Aliases = new[] { "eject", "scoate stick", "usb", "safely remove", label }, Category = "Dispozitive", Icon = BuiltInActions.GEject,
                    Safety = ActionSafety.Confirm, RequiresUiThread = true,              // the shell's Eject must run on the UI thread
                    UnavailableMessage = "Unitatea nu mai e conectată.",
                };
            }
        }
    }
}
