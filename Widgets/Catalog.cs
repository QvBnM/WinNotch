using System;
using System.Collections.Generic;
using System.Linq;

namespace WinNotch.Widgets
{
    /// <summary>Every widget the gallery offers, with its sizes and settings; and the standard pages as widget layouts.</summary>
    internal static class Catalog
    {
        public static readonly string[] Categories = { "Media", "Timp și calendar", "Sistem", "Rețea", "Dispozitive", "Productivitate", "Personalizate" };

        static WidgetDef D(string type, string name, string cat, string glyph, string desc, (int, int)[] sizes, Func<NotchWindow, WidgetSlot, Widget> create, params OptionDef[] opts) =>
            new WidgetDef { Type = type, Name = name, Category = cat, Glyph = glyph, Description = desc, Sizes = sizes, Create = create, Options = opts, Custom = cat == "Personalizate" };

        static OptionDef O(string key, string label, OptionKind kind, string def = "", string hint = null, params (string, string)[] choices) =>
            new OptionDef { Key = key, Label = label, Kind = kind, Default = def, Hint = hint, Choices = choices };

        public static readonly List<WidgetDef> All = new List<WidgetDef>
        {
            D("music", "Muzică", "Media", Ui.GMusic, "Piesa, coperta, versurile și butoanele; lat: și sursele audio.", new[] { (2, 1), (4, 2), (6, 2) }, (w, s) => new MusicWidget(w, s)),
            D("sources", "Surse audio", "Media", Ui.GVol, "Ce se aude acum (aplicații și tab-uri), cu mute.", new[] { (4, 1), (6, 1), (3, 2) }, (w, s) => new SourcesWidget(w, s)),
            D("volume", "Volum", "Media", Ui.GVol, "Volumul general, cu mute.", new[] { (2, 1), (3, 1) }, (w, s) => new VolumeWidget(w, s)),

            D("clock", "Ceas", "Timp și calendar", Ui.GClock, "Ora; mai mare: data și săptămâna.", new[] { (1, 1), (2, 1), (2, 2) }, (w, s) => new ClockWidget(w, s)),
            D("calendar", "Calendar", "Timp și calendar", "\uE787", "Următorul eveniment; mare: următoarele.", new[] { (2, 1), (3, 2), (2, 2) }, (w, s) => new CalendarWidget(w, s)),
            D("weather", "Vremea", "Timp și calendar", Ui.GSun, "Acum, minima și maxima; mai mare: pe ore.", new[] { (1, 1), (2, 1), (2, 2), (3, 2) }, (w, s) => new WeatherWidget(w, s)),

            D("cpu", "Procesor", "Sistem", "\uE950", "Încărcare și temperatură; 3×2 cu grafic.", new[] { (1, 1), (2, 1), (3, 2) }, (w, s) => new CpuWidget(w, s)),
            D("memory", "Memorie", "Sistem", Ui.GMemory, "RAM folosit, cu buton ⚡ de optimizare.", new[] { (1, 1), (2, 1) }, (w, s) => new MemoryWidget(w, s)),
            D("gpu", "Placă video", "Sistem", "\uE7F4", "Temperatură și încărcare.", new[] { (1, 1), (2, 1) }, (w, s) => new GpuWidget(w, s)),
            D("battery", "Baterie", "Sistem", Ui.GBattery, "Procent, încărcare, timp rămas.", new[] { (1, 1), (2, 1) }, (w, s) => new BatteryWidget(w, s)),
            D("topapps", "Consumă acum", "Sistem", "\uE9F9", "Aplicațiile care folosesc cel mai mult.", new[] { (6, 1), (3, 2) }, (w, s) => new TopAppsWidget(w, s)),
            // P61: the summary of the last game session (Features/GameMode); empty until there is one
            D("lastgame", "Ultimul joc", "Sistem", Ui.GGamepad, "Rezumatul sesiunii: durată, încărcare, temperaturi, cine fura.", new[] { (3, 1), (6, 1), (3, 2) }, (w, s) => new Features.GameMode.GameWidget(w, s)),

            D("internet", "Internet", "Rețea", "\uE701", "Viteza acum față de maxim; 3×2 cu test de viteză.", new[] { (2, 1), (3, 2) }, (w, s) => new InternetWidget(w, s)),

            D("privacy", "Microfon și cameră", "Dispozitive", Ui.GMic, "Cine le folosește și de cât timp.", new[] { (2, 1), (2, 2), (3, 1) }, (w, s) => new PrivacyWidget(w, s)),
            D("audiodev", "Ieșire și intrare audio", "Dispozitive", Ui.GHeadphones, "Căștile/boxele și microfonul implicite.", new[] { (2, 1), (3, 1) }, (w, s) => new AudioDevicesWidget(w, s)),
            D("connected", "Dispozitive conectate", "Dispozitive", Ui.GUsb, "Bluetooth, USB, monitoare, stick-uri.", new[] { (3, 2), (4, 2), (4, 3) }, (w, s) => new ConnectedWidget(w, s)),

            D("launcher", "Lansator", "Productivitate", Ui.GSearch, "Deschizi aplicații, setări, foldere; =calcul.", new[] { (3, 1), (6, 1) }, (w, s) => new LauncherWidget(w, s)),
            D("quicktools", "Unelte rapide", "Productivitate", Ui.GCamera, "Captură, zonă, text din ecran, RAM.", new[] { (4, 1), (6, 1), (2, 2) }, (w, s) => new QuickToolsWidget(w, s)),
            D("clipboard", "Clipboard", "Productivitate", "\uE77F", "Ultimele texte copiate, fixate; 3×3 cu căutare.", new[] { (3, 2), (3, 3), (2, 2) }, (w, s) => new ClipboardWidget(w, s)),
            D("note", "Notiță", "Productivitate", "\uE70B", "Notița ta, salvată automat.", new[] { (2, 2), (3, 2), (3, 1) }, (w, s) => new NoteWidget(w, s)),
            D("workspaces", "Spații de lucru", "Productivitate", Ui.GTools, "Redeschide aplicațiile așezate cum le-ai salvat.", new[] { (3, 1), (4, 1), (6, 1) }, (w, s) => new WorkspacesWidget(w, s)),
            D("activewin", "Fereastra activă", "Productivitate", "\uE7C4", "Deasupra, alt monitor, jumătate, mini.", new[] { (3, 1), (4, 1) }, (w, s) => new ActiveWindowWidget(w, s)),

            D("shortcuts", "Scurtături", "Personalizate", "\uE71D", "Aplicații, foldere, fișiere și site-uri alese de tine.", new[] { (2, 1), (3, 1), (4, 1), (6, 1), (3, 2) }, (w, s) => new ShortcutsWidget(w, s),
              O("items", "Ce deschide (câte una pe rând: cale sau link; opțional „Nume = cale”)", OptionKind.Files, "", "Ex.: C:\\Program Files\\Spotify\\Spotify.exe  ·  Proiecte = D:\\Proiecte  ·  https://mail.google.com")),
            D("sensor", "Senzor", "Personalizate", "\uE9D9", "Orice valoare ca număr, bară sau grafic, cu prag.", new[] { (1, 1), (2, 1), (3, 1), (3, 2) }, (w, s) => new SensorWidget(w, s),
              O("metric", "Ce valoare arată", OptionKind.Choice, "gputemp", null, SensorWidget.Metrics),
              O("display", "Cum o arată", OptionKind.Choice, "bar", null, ("number", "Număr"), ("bar", "Bară"), ("chart", "Grafic (pe 2 rânduri)")),
              O("title", "Titlu (gol = automat)", OptionKind.Text),
              O("warn", "Avertizează peste (gol = fără prag)", OptionKind.Number),
              O("color", "Culoare (gol = accentul temei)", OptionKind.Color)),
            D("text", "Text", "Personalizate", "\uE8D2", "O notă, un citat, un cod.", new[] { (2, 1), (3, 1), (6, 1), (2, 2), (3, 2) }, (w, s) => new TextWidget(w, s),
              O("text", "Textul", OptionKind.MultiLine, "Scrie aici"),
              O("size", "Mărimea literelor", OptionKind.Number, "14"),
              O("align", "Aliniere", OptionKind.Choice, "left", null, ("left", "Stânga"), ("center", "Centru")),
              O("bold", "Îngroșat", OptionKind.Bool, "0"), O("mono", "Font de cod", OptionKind.Bool, "0"), O("muted", "Mai discret", OptionKind.Bool, "0")),
            D("link", "Link web", "Personalizate", "\uE71B", "Deschide un site.", new[] { (1, 1), (2, 1) }, (w, s) => new LinkWidget(w, s),
              O("url", "Adresa (https://…)", OptionKind.Text), O("title", "Titlu (gol = numele site-ului)", OptionKind.Text)),
            D("worldclock", "Ceas alt oraș", "Personalizate", "\uE909", "Ora din alt fus orar.", new[] { (1, 1), (2, 1) }, (w, s) => new WorldClockWidget(w, s),
              O("zone", "Fus orar", OptionKind.Choice, "America/New_York", null,
                ("Europe/London", "Londra"), ("Europe/Paris", "Paris / Berlin / Roma"), ("Europe/Istanbul", "Istanbul"), ("Asia/Dubai", "Dubai"),
                ("Asia/Kolkata", "India"), ("Asia/Shanghai", "China"), ("Asia/Tokyo", "Tokyo"), ("Australia/Sydney", "Sydney"),
                ("America/New_York", "New York"), ("America/Chicago", "Chicago"), ("America/Denver", "Denver"), ("America/Los_Angeles", "Los Angeles"),
                ("America/Sao_Paulo", "São Paulo"), ("UTC", "UTC")),
              O("label", "Eticheta (gol = orașul)", OptionKind.Text)),
            D("command", "Comandă", "Personalizate", "\uE756", "Rulează un program sau un script la click.", new[] { (1, 1), (2, 1) }, (w, s) => new CommandWidget(w, s),
              O("file", "Programul sau scriptul", OptionKind.Files), O("args", "Argumente (opțional)", OptionKind.Text), O("title", "Titlu", OptionKind.Text)),
        };

        public static WidgetDef Get(string type) => All.FirstOrDefault(d => d.Type == type);

        public static Widget Create(NotchWindow w, WidgetSlot s)
        {
            var d = Get(s.Type);
            try { return d != null ? d.Create(w, s) : new MissingWidget(w, s); }
            catch (Exception ex) { App.Log("Widget " + s.Type + ": " + ex.Message); return new MissingWidget(w, s); }
        }

        /// <summary>A new widget slot of this type with default size and options.</summary>
        public static WidgetSlot NewSlot(string type, (int W, int H)? size = null)
        {
            var d = Get(type);
            var sz = size ?? d.DefaultSize;
            var s = new WidgetSlot { Type = type, W = sz.W, H = sz.H };
            foreach (var o in d.Options) if (!string.IsNullOrEmpty(o.Default)) s.Options[o.Key] = o.Default;
            return s;
        }

        // ---------------- standard pages ----------------
        public static readonly (string Id, string Name, string Icon)[] Standard =
        {
            ("home", "Acasă", "home"), ("system", "Sistem", "system"), ("devices", "Dispozitive", "devices"), ("tools", "Unelte", "tools")
        };

        static WidgetSlot S(string type, int c, int r, int w, int h) { var s = NewSlot(type, (w, h)); s.Col = c; s.Row = r; return s; }

        /// <summary>A standard page as widgets, used when you duplicate it to change it.</summary>
        public static List<WidgetSlot> StandardLayout(string id) => id switch
        {
            "home" => new List<WidgetSlot> { S("music", 0, 0, 4, 2), S("clock", 4, 0, 2, 1), S("weather", 4, 1, 2, 1), S("sources", 0, 2, 4, 1), S("calendar", 4, 2, 2, 1) },
            "system" => new List<WidgetSlot> { S("cpu", 0, 0, 3, 2), S("internet", 3, 0, 3, 2), S("memory", 0, 2, 2, 1), S("gpu", 2, 2, 2, 1), S("battery", 4, 2, 2, 1), S("topapps", 0, 3, 6, 1) },
            "devices" => new List<WidgetSlot> { S("privacy", 0, 0, 2, 2), S("audiodev", 0, 2, 2, 1), S("connected", 2, 0, 4, 3) },
            "tools" => new List<WidgetSlot> { S("launcher", 0, 0, 6, 1), S("quicktools", 0, 1, 6, 1), S("workspaces", 0, 2, 3, 1), S("activewin", 0, 3, 3, 1), S("clipboard", 3, 2, 3, 2) },
            _ => new List<WidgetSlot>()
        };
    }
}
