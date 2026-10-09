using System;
using System.Collections.Generic;
using System.Linq;

namespace WinNotch.Core.Ui
{
    /// <summary>
    /// P52: everything the "Teme și culori" page actually changes — chosen theme, your colours on top of it, a new
    /// theme, deleting one, picking a colour. Extracted once out of <c>EditorWindow</c> so the classic window and the
    /// new one edit the themes through the same code instead of each keeping its own copy of the rules. Only settings
    /// are touched here: saving and re-applying stays with the caller (it knows what to redraw).
    /// </summary>
    internal static class ThemeEdits
    {
        /// <summary>Your own colour for one key of one theme. The theme editor wins over the accent from Settings.</summary>
        internal static void SetOverride(AppSettings s, string theme, string key, string hex)
        {
            if (key == "Accent") s.Accent = "";
            if (!s.ThemeOverrides.TryGetValue(theme, out var ov)) s.ThemeOverrides[theme] = ov = new Dictionary<string, string>();
            ov[key] = hex;
        }

        /// <summary>Back to the theme's own colour for that key.</summary>
        internal static void ResetOverride(AppSettings s, string theme, string key)
        {
            if (key == "Accent") s.Accent = "";
            if (!s.ThemeOverrides.TryGetValue(theme, out var ov)) return;
            ov.Remove(key);
            if (ov.Count == 0) s.ThemeOverrides.Remove(theme);
        }

        /// <summary>Every colour of that theme back to the preset.</summary>
        internal static void ResetAll(AppSettings s, string theme)
        {
            s.ThemeOverrides.Remove(theme);
            s.Accent = "";
        }

        /// <summary>True when that key of that theme carries a colour of yours (shown with a "back" button).</summary>
        internal static bool IsChanged(AppSettings s, string theme, string key) =>
            (s.ThemeOverrides.TryGetValue(theme, out var ov) && ov.ContainsKey(key)) ||
            (key == "Accent" && !string.IsNullOrEmpty(s.Accent));

        /// <summary>Picks the theme used for the dark or for the light mode.</summary>
        internal static void Choose(AppSettings s, string name, bool light)
        {
            if (light) s.ThemeLight = name; else s.ThemeDark = name;
        }

        /// <summary>
        /// Keeps the colours in use (with your changes) as a theme of its own and selects it. False when the name is
        /// empty or already taken — the caller marks the box.
        /// </summary>
        internal static bool SaveAs(AppSettings s, string name)
        {
            var n = (name ?? "").Trim();
            if (n.Length == 0 || ThemeManager.All(s).Any(p => p.Name == n)) return false;
            var t = ThemeManager.Current(s).Clone(n);
            t.Light = ThemeManager.IsLight(s);
            s.CustomThemes.Add(t);
            Choose(s, n, t.Light);
            s.Accent = "";
            return true;
        }

        /// <summary>Deletes one of your themes (a preset is never deleted) and falls back to a preset if it was in use.</summary>
        internal static bool Delete(AppSettings s, ThemePalette t)
        {
            if (t == null || ThemeManager.Presets.Contains(t)) return false;
            s.CustomThemes.Remove(t);
            s.ThemeOverrides.Remove(t.Name);
            if (s.ThemeDark == t.Name) s.ThemeDark = "Noapte";
            if (s.ThemeLight == t.Name) s.ThemeLight = "Luminos";
            return true;
        }

        /// <summary>#RRGGBB or #AARRGGBB.</summary>
        internal static bool ValidHex(string t)
        {
            var v = (t ?? "").Trim();
            if (!v.StartsWith("#") || (v.Length != 7 && v.Length != 9)) return false;
            return v.Skip(1).All(Uri.IsHexDigit);
        }

        /// <summary>The Windows colour dialog, opened on a starting colour; null when the user cancels.</summary>
        internal static string PickColor(string start)
        {
            using var dlg = new System.Windows.Forms.ColorDialog { FullOpen = true, AnyColor = true };
            var c = ThemeManager.Parse(start, "#5AA9FF");
            dlg.Color = System.Drawing.Color.FromArgb(c.R, c.G, c.B);
            if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return null;
            return $"#{dlg.Color.R:X2}{dlg.Color.G:X2}{dlg.Color.B:X2}";
        }
    }
}
