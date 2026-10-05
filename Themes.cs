using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace WinNotch
{
    /// <summary>A named set of colors for the notch. Keys match the brushes in Theme.xaml (without "Brush").</summary>
    public sealed class ThemePalette
    {
        public string Name { get; set; } = "";
        public bool Light { get; set; }
        public Dictionary<string, string> Colors { get; set; } = new Dictionary<string, string>();

        public ThemePalette Clone(string name = null) => new ThemePalette
        {
            Name = name ?? Name, Light = Light, Colors = new Dictionary<string, string>(Colors)
        };
    }

    /// <summary>
    /// Dark / light / automatic (follows Windows) themes, ready-made palettes, your own colors on top of any of them,
    /// corner rounding and background transparency. Colors are swapped as resources, so everything re-colors.
    /// </summary>
    public static class ThemeManager
    {
        /// <summary>Every color a theme defines, with its label in the editor.</summary>
        public static readonly (string Key, string Label)[] Keys =
        {
            ("Notch", "Fundal notch"), ("Chip", "Carduri"), ("ChipHover", "Carduri ridicate / butoane"), ("Ink", "Text"),
            ("Muted", "Text secundar"), ("Dim", "Text discret"), ("Accent", "Accent"), ("Track", "Bare și linii"),
            ("Ok", "Bine (verde)"), ("Warn", "Atenție"), ("Hot", "Pericol"), ("Info", "Informație"),
            ("Seg", "Bara de tab-uri"), ("Border", "Contur carduri"), ("OnAccent", "Text pe accent"), ("Hover", "Hover")
        };

        static ThemePalette P(string name, bool light, params string[] kv)
        {
            var p = new ThemePalette { Name = name, Light = light };
            for (int i = 0; i + 1 < kv.Length; i += 2) p.Colors[kv[i]] = kv[i + 1];
            return p;
        }

        public static readonly List<ThemePalette> Presets = new List<ThemePalette>
        {
            P("Noapte", false, "Notch","#000000","Chip","#1B1D21","ChipHover","#24272D","Ink","#F1F2F4","Muted","#B4BAC2","Dim","#9097A1",
              "Accent","#F5A524","Track","#2A2D33","Ok","#3DDC84","Warn","#FF9F43","Hot","#FF5C5C","Info","#5AA9FF",
              "Seg","#111316","Border","#00000000","OnAccent","#000000","Hover","#2E3239"),
            P("Grafit", false, "Notch","#17191D","Chip","#23262C","ChipHover","#2D3138","Ink","#EEF0F3","Muted","#B4BAC2","Dim","#959CA6",
              "Accent","#5AA9FF","Track","#343840","Ok","#3DDC84","Warn","#FF9F43","Hot","#FF5C5C","Info","#7CC0FF",
              "Seg","#111317","Border","#00000000","OnAccent","#000000","Hover","#383C44"),
            P("Nord", false, "Notch","#1E2430","Chip","#2A3140","ChipHover","#343C4D","Ink","#E5E9F0","Muted","#B8C0CF","Dim","#97A1B3",
              "Accent","#88C0D0","Track","#3B4455","Ok","#A3BE8C","Warn","#EBCB8B","Hot","#BF616A","Info","#81A1C1",
              "Seg","#181D27","Border","#00000000","OnAccent","#0F141C","Hover","#3F4859"),
            P("Contrast mare", false, "Notch","#000000","Chip","#000000","ChipHover","#1A1A1A","Ink","#FFFFFF","Muted","#FFFFFF","Dim","#E0E0E0",
              "Accent","#FFD400","Track","#5A5A5A","Ok","#00FF7F","Warn","#FFB000","Hot","#FF4D4D","Info","#4DB8FF",
              "Seg","#000000","Border","#FFFFFF","OnAccent","#000000","Hover","#333333"),
            P("Luminos", true, "Notch","#F4F5F7","Chip","#FFFFFF","ChipHover","#ECEEF1","Ink","#15171A","Muted","#4F5661","Dim","#6B7280",
              "Accent","#C2410C","Track","#E3E6EA","Ok","#15803D","Warn","#B45309","Hot","#B91C1C","Info","#1D63C9",
              "Seg","#E7E9ED","Border","#E3E6EA","OnAccent","#FFFFFF","Hover","#E2E5EA"),
            P("Hârtie", true, "Notch","#F5F0E6","Chip","#FFFAF0","ChipHover","#EFE7D8","Ink","#2B2620","Muted","#5E554A","Dim","#776C5F",
              "Accent","#B4532A","Track","#E6DCCB","Ok","#3F7D3A","Warn","#A15C0A","Hot","#A8322D","Info","#2F5F8A",
              "Seg","#ECE4D4","Border","#E6DCCB","OnAccent","#FFFFFF","Hover","#E9E0CF"),
        };

        public static IEnumerable<ThemePalette> All(AppSettings s) => Presets.Concat(s.CustomThemes ?? new List<ThemePalette>());

        public static ThemePalette Find(AppSettings s, string name, bool light) =>
            All(s).FirstOrDefault(p => p.Name == name && p.Light == light) ?? Presets.First(p => p.Light == light);

        /// <summary>Is Windows set to light apps right now?</summary>
        public static bool WindowsLight()
        {
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                return k?.GetValue("AppsUseLightTheme") is int v && v == 1;
            }
            catch { return false; }
        }

        public static bool IsLight(AppSettings s) => s.ThemeMode == "light" || (s.ThemeMode == "auto" && WindowsLight());

        /// <summary>The palette in use: the chosen theme for the current mode, with your own colors on top.</summary>
        public static ThemePalette Current(AppSettings s)
        {
            bool light = IsLight(s);
            var p = Find(s, light ? s.ThemeLight : s.ThemeDark, light).Clone();
            if (s.ThemeOverrides != null && s.ThemeOverrides.TryGetValue(p.Name, out var ov))
                foreach (var kv in ov) p.Colors[kv.Key] = kv.Value;
            if (!string.IsNullOrEmpty(s.Accent)) p.Colors["Accent"] = s.Accent;       // the accent picked in Settings
            return p;
        }

        public static string LastApplied { get; private set; } = "";

        /// <summary>Puts the theme's colors into the app's resources. Returns true when anything changed.</summary>
        public static bool Apply(AppSettings s)
        {
            var p = Current(s);
            string sig = string.Join(";", p.Colors.OrderBy(k => k.Key).Select(k => k.Key + "=" + k.Value)) + "|" + s.BgOpacity;
            if (sig == LastApplied) return false;
            LastApplied = sig;
            var res = Application.Current.Resources;
            foreach (var (key, _) in Keys)
            {
                var c = Parse(p.Colors.TryGetValue(key, out var hex) ? hex : null, Presets[0].Colors[key]);
                if (key == "Notch") c.A = (byte)Math.Round(Math.Clamp(s.BgOpacity, 0.6, 1) * 255);
                var b = new SolidColorBrush(c);
                b.Freeze();
                res[key + "Brush"] = b;
            }
            return true;
        }

        public static Color Parse(string hex, string fallback)
        {
            try { return (Color)ColorConverter.ConvertFromString(string.IsNullOrWhiteSpace(hex) ? fallback : hex); }
            catch { return (Color)ColorConverter.ConvertFromString(fallback); }
        }

        public static string Hex(Color c) => c.A == 255 ? $"#{c.R:X2}{c.G:X2}{c.B:X2}" : $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
    }
}
