using System;
using System.Collections.Generic;
using System.Linq;
using WinNotch.Core.Actions;

namespace WinNotch.Features.CommandBar
{
    /// <summary>What the "settings.*" actions call: the WinNotch window, on the Settings page, at one option.</summary>
    public interface ISettingsHost
    {
        /// <summary>Opens Settings and brings the control named <paramref name="target"/> (x:Name in SettingsWindow.xaml) into view.</summary>
        void OpenSettingsAt(string target);
    }

    /// <summary>One option of the Settings page, as an action.</summary>
    public sealed class SettingsOption
    {
        public SettingsOption(string id, string title, string target, string section, params string[] aliases)
        {
            Id = id; Title = title; Target = target; Section = section; Aliases = aliases ?? Array.Empty<string>();
        }

        /// <summary>"settings.&lt;nume&gt;": stable once published.</summary>
        public string Id { get; }
        public string Title { get; }
        /// <summary>x:Name of the control in SettingsWindow.xaml (the tests check it exists).</summary>
        public string Target { get; }
        /// <summary>The Settings section it is in (its header text).</summary>
        public string Section { get; }
        public IReadOnlyList<string> Aliases { get; }
        /// <summary>Lists (standby items, workspaces, accent colours, pages by context, feature switches) open at their section, not one control.</summary>
        public bool SectionOnly => Target is "WidgetRows" or "WorkspaceRows" or "AccentPanel" or "ContextPageRows" or "FeatureRows";
    }

    /// <summary>
    /// P14: every option of the Settings page is an action that opens Settings at that option ("settings.position",
    /// "settings.eye-break"…). Safe, on the UI thread. The ids of the Windows settings links ("settings.bluetooth",
    /// "settings.sound", "settings.display", "settings.wifi", "settings.update") are taken: these never reuse them.
    /// </summary>
    public static class SettingsActions
    {
        public const string Category = "Setări WinNotch";
        /// <summary>Segoe Fluent / MDL2 "Settings".</summary>
        internal const string GSettings = "";

        private const string Standby = "Ce apare în standby", Behaviour = "Comportament", Home = "Acasă și sănătate",
            Tabs = "Tab-uri din browser", Spaces = "Spații de lucru", Accent = "Culoare accent", Weather = "Vremea", ContextPages = "Pagina după context",
            SmartClipboard = "Smart Clipboard", New = "Funcții noi (experimental)";

        public static readonly IReadOnlyList<SettingsOption> All = new[]
        {
            new SettingsOption("settings.standby-items", "Setări: ce apare în standby", "WidgetRows", Standby, "setări standby", "pastila închisă", "standby items", "widgets standby"),
            new SettingsOption("settings.hover-delay", "Setări: deschiderea la hover", "DwellSlider", Behaviour, "setări hover", "întârziere hover", "hover delay", "dwell"),
            new SettingsOption("settings.position", "Setări: poziția notch-ului", "PosBox", Behaviour, "setări poziție", "poziție", "stânga centru dreapta", "position"),
            new SettingsOption("settings.fullscreen", "Setări: peste jocuri și ecran complet", "FsBox", Behaviour, "setări ecran complet", "fullscreen", "jocuri"),
            new SettingsOption("settings.mini-after", "Setări: micșorarea automată", "MiniBox", Behaviour, "setări micșorare", "pastila mică", "mini pill"),
            new SettingsOption("settings.size", "Setări: mărimea notch-ului deschis", "ScaleBox", Behaviour, "setări mărime", "scalare", "zoom", "size"),
            new SettingsOption("settings.slim", "Setări: mic peste ferestre maximizate", "SlimBox", Behaviour, "setări maximizat", "slim", "bara de titlu"),
            new SettingsOption("settings.command-bar-key", "Setări: scurtătura Command Bar", "CmdKeyBox", Behaviour, "setări scurtătură", "command bar", "win alt space", "win alt k", "hotkey", "shortcut"),
            new SettingsOption("settings.mini-progress", "Setări: linia de progres a piesei", "MiniProgressBox", Behaviour, "setări progres", "linia piesei", "dunga", "bara piesei", "song progress"),
            new SettingsOption("settings.temperatures", "Setări: temperaturile PC-ului", "TempsBox", Behaviour, "setări temperatură", "temperaturi", "temperature"),
            new SettingsOption("settings.start-with-windows", "Setări: pornește odată cu Windows", "StartBox", Behaviour, "setări pornire", "pornire automată", "startup", "autostart"),
            new SettingsOption("settings.auto-update", "Setări: actualizări automate", "UpdateBox", Behaviour, "setări actualizări", "versiuni noi", "auto update"),
            new SettingsOption("settings.beta-channel", "Setări: canalul beta", "BetaBox", Behaviour, "setări beta", "versiuni de test", "beta channel"),
            new SettingsOption("settings.lyrics", "Setări: versurile piesei", "LyricsBox", Home, "setări versuri", "versuri", "lyrics"),
            new SettingsOption("settings.eye-break", "Setări: pauza pentru ochi", "EyeBox", Home, "setări ochi", "pauză ochi", "20-20-20", "eye break"),
            new SettingsOption("settings.ram-alert", "Setări: alerta de memorie RAM", "RamAlertBox", Home, "setări ram", "alertă memorie", "ram alert"),
            new SettingsOption("settings.calendar", "Setări: calendarul (link iCal)", "IcsBox", Home, "setări calendar", "ical", "ics", "google calendar", "outlook"),
            new SettingsOption("settings.browser-tabs", "Setări: tab-urile din browser", "TabsBox", Tabs, "setări browser", "extensia", "tab-uri", "browser tabs"),
            new SettingsOption("settings.workspaces", "Setări: spațiile de lucru", "WorkspaceRows", Spaces, "setări spații", "spații de lucru", "workspaces"),
            new SettingsOption("settings.accent", "Setări: culoarea accent", "AccentPanel", Accent, "setări culoare", "accent", "accent color"),
            new SettingsOption("settings.weather", "Setări: vremea (orașul)", "CityBox", Weather, "setări vreme", "oraș", "coordonate", "weather"),
            new SettingsOption("settings.context-pages", "Setări: pagina după context", "ContextPageRows", ContextPages, "setări pagină context", "pagina după context", "pagini după context", "context pages"),
            new SettingsOption("settings.clipboard-peek", "Setări: mesaj la copiere (Smart Clipboard)", "ClipPeekBox", SmartClipboard, "setări clipboard", "smart clipboard", "mesaj la copiere", "clipboard peek"),
            new SettingsOption("settings.features", "Setări: funcții noi (experimental)", "FeatureRows", New, "setări funcții noi", "experimental", "comutatoare", "feature flags"),
        };

        public static SettingsOption Find(string id) => All.FirstOrDefault(o => o.Id == id);

        public static IReadOnlyList<ActionDescriptor> Create(ISettingsHost host)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            return All.Select(o => (ActionDescriptor)new ActionDescriptor(o.Id, o.Title, (args, ct) =>
            {
                host.OpenSettingsAt(o.Target);
                return ActionResult.OkTask("Setări › " + o.Section);
            })
            {
                Aliases = o.Aliases, Category = Category, Icon = GSettings, Safety = ActionSafety.Safe, RequiresUiThread = true,
            }).ToList();
        }

        /// <summary>Registered once at startup, from App.RegisterActions.</summary>
        public static void Register(ActionRegistry registry, ISettingsHost host)
        {
            foreach (var a in Create(host)) registry.Register(a);
        }
    }
}
