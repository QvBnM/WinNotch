using System;
using System.Collections.Generic;
using System.Linq;

namespace WinNotch.Features.WindowV2
{
    /// <summary>
    /// P52: where each option of the old settings page lives in the new one. The "settings.*" actions (P14) name an
    /// option by the control it used to be (<c>PosBox</c>, <c>EyeBox</c>…); this table says which section of the Setări
    /// tab shows it now, so every one of those actions still lands on it. Pure and tested: a target that is not here
    /// would be an action pointing nowhere.
    /// </summary>
    public static class SettingsMap
    {
        /// <summary>Old control name → (section of the Setări tab, what the option is called there).</summary>
        private static readonly (string Target, string Section, string Name)[] Entries =
        {
            ("WidgetRows", "standby", "Ce apare în standby"),
            ("DwellSlider", "notch", "Se deschide la hover după"),
            ("PosBox", "notch", "Poziție"),
            ("FsBox", "notch", "Peste jocuri / fullscreen"),
            ("MiniBox", "notch", "Se micșorează singur după"),
            ("ScaleBox", "notch", "Mărimea notch-ului deschis"),
            ("SlimBox", "notch", "Mic peste ferestre maximizate"),
            ("CmdKeyBox", "notch", "Scurtătura Command Bar"),
            ("AccentPanel", "notch", "Culoare accent"),
            ("TempsBox", "sistem", "Citește temperaturile PC-ului"),
            ("StartBox", "sistem", "Pornește odată cu Windows"),
            ("UpdateBox", "sistem", "Caută singur versiuni noi"),
            ("BetaBox", "sistem", "Canal beta"),
            ("LyricsBox", "acasa", "Versurile piesei"),
            ("EyeBox", "acasa", "Pauză pentru ochi"),
            ("RamAlertBox", "acasa", "Alertă când memoria RAM se umple"),
            ("IcsBox", "acasa", "Link iCal (.ics)"),
            ("CityBox", "acasa", "Vremea"),
            ("TabsBox", "browser", "Arată fiecare tab separat"),
            ("WorkspaceRows", "spatii", "Spații de lucru"),
            ("ContextPageRows", "context", "Pagina după context"),
            ("ClipPeekBox", "functii", "Mesaj scurt în pastilă la copiere"),
            ("FeatureRows", "functii", "Funcții noi"),
        };

        /// <summary>Every option the old page had, as a target name.</summary>
        public static IReadOnlyList<string> Targets => Entries.Select(e => e.Target).ToList();

        /// <summary>The section of the Setări tab that shows this option, or null for an unknown target.</summary>
        public static string SectionFor(string target) =>
            Entries.FirstOrDefault(e => string.Equals(e.Target, target, StringComparison.Ordinal)).Section;

        /// <summary>What the option is called in the new page, or null for an unknown target.</summary>
        public static string NameFor(string target) =>
            Entries.FirstOrDefault(e => string.Equals(e.Target, target, StringComparison.Ordinal)).Name;
    }
}
