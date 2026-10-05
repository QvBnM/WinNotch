using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Win32;

namespace WinNotch
{
    /// <summary>User settings, saved as JSON in %AppData%\WinNotch\settings.json.</summary>
    public sealed partial class AppSettings
    {
        /// <summary>Standby items in display order (max 5). Ids: see <see cref="Widgets"/>.</summary>
        public List<string> Standby { get; set; } = new List<string> { "music", "clock", "weather" };
        public int DwellMs { get; set; } = 400;
        public bool SlimOverMaximized { get; set; } = true;
        /// <summary>Shrink to the small time/date pill after this many seconds without activity (0 = never).</summary>
        public int MiniAfterSec { get; set; } = 10;
        /// <summary>left | center | right</summary>
        public string Position { get; set; } = "center";
        /// <summary>alerts = hidden on busy monitors but alerts still pop; visible = never hide</summary>
        public string Fullscreen { get; set; } = "alerts";
        /// <summary>Accent picked in Settings; empty = the theme's own accent.</summary>
        public string Accent { get; set; } = "";
        public bool Temperatures { get; set; } = true;
        public string City { get; set; } = "București";
        public double Lat { get; set; } = 44.4268;
        public double Lon { get; set; } = 26.1025;
        public string Note { get; set; } = "";
        public List<string> Shelf { get; set; } = new List<string>();
        /// <summary>Private calendar link (.ics) from Google Calendar or Outlook, for "what's next".</summary>
        public string CalendarIcs { get; set; } = "";
        public bool Lyrics { get; set; } = true;
        /// <summary>20-20-20: every 20 minutes at the PC, look 20 seconds into the distance.</summary>
        public bool EyeBreak { get; set; } = true;
        public int EyeBreakMinutes { get; set; } = 20;
        public bool RamAlert { get; set; } = true;
        public bool AutoUpdate { get; set; } = true;
        /// <summary>Also offer pre-release (test) versions.</summary>
        public bool BetaChannel { get; set; }
        public DateTime UpdateSnoozeUntil { get; set; } = DateTime.MinValue;
        public int RamAlertPercent { get; set; } = 80;
        public List<Services.Workspace> Workspaces { get; set; } = new List<Services.Workspace>();
        /// <summary>Clipboard items the user pinned (kept across restarts; unpinned history stays in memory only).</summary>
        public List<string> PinnedClips { get; set; } = new List<string>();
        public double SpeedDownMbps { get; set; }
        public double SpeedUpMbps { get; set; }
        public int SpeedPingMs { get; set; } = -1;
        public DateTime SpeedTestedAt { get; set; }
        /// <summary>Last speed tests, newest last (max 20).</summary>
        public List<Services.SpeedRecord> SpeedHistory { get; set; } = new List<Services.SpeedRecord>();
        /// <summary>Show each browser tab that plays sound (needs the WinNotch browser extension).</summary>
        public bool BrowserTabs { get; set; } = true;
        /// <summary>Size of the open notch and alerts: 0 = automatic (by monitor), otherwise 1.0, 1.15, 1.25…</summary>
        public double UiScale { get; set; }
        /// <summary>Command Bar shortcut (P14): "space" = Win+Alt+Space, "k" = Win+Alt+K. Anything else reads as "space".</summary>
        public string CommandBarKey { get; set; } = "space";

        // ---- themes ----
        /// <summary>dark | light | auto (follows Windows)</summary>
        public string ThemeMode { get; set; } = "dark";
        public string ThemeDark { get; set; } = "Noapte";
        public string ThemeLight { get; set; } = "Luminos";
        /// <summary>Your own colors on top of a theme: theme name → (color key → #hex).</summary>
        public Dictionary<string, Dictionary<string, string>> ThemeOverrides { get; set; } = new Dictionary<string, Dictionary<string, string>>();
        public List<ThemePalette> CustomThemes { get; set; } = new List<ThemePalette>();
        public int CornerRadius { get; set; } = 28;
        public double BgOpacity { get; set; } = 1.0;

        // ---- pages ----
        /// <summary>Standard pages you hid: home, system, devices, tools.</summary>
        public List<string> HiddenPages { get; set; } = new List<string>();
        /// <summary>Your own pages (made from scratch or duplicated from a standard one), in tab order.</summary>
        public List<Widgets.UserPage> Pages { get; set; } = new List<Widgets.UserPage>();

        // Encrypted values that couldn't be opened (other account/PC): written back unchanged instead of the empty text.
        [System.Text.Json.Serialization.JsonIgnore] public string LockedIcs { get; set; }
        [System.Text.Json.Serialization.JsonIgnore] public string LockedNote { get; set; }

        public const int MaxStandby = 5;

        /// <summary>All standby items the user can pick from: id → label.</summary>
        public static readonly (string Id, string Name)[] Widgets =
        {
            ("music", "Muzică"), ("clock", "Ora"), ("date", "Data"), ("weather", "Vremea"),
            ("cpu", "CPU"), ("ram", "RAM"), ("ctemp", "Temp. CPU"), ("gtemp", "Temp. GPU"), ("stemp", "Temp. SSD"),
            ("bat", "Baterie"), ("vol", "Volum"), ("net", "Internet")
        };

        public static string Folder => global::WinNotch.Features.Smoke.SmokeMode.On     // smoke tests never touch the real settings, log or startup records
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinNotch", global::WinNotch.Features.Smoke.SmokeMode.FolderName)
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinNotch");
        private static string FilePath => Path.Combine(Folder, "settings.json");

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath));
                    if (s != null)
                    {
                        // Something encrypted for another account/PC stays encrypted (shown empty, never overwritten).
                        bool locked = false;
                        if (Secret.TryUnprotect(s.CalendarIcs, out var ics)) s.CalendarIcs = ics; else { locked = true; s.LockedIcs = s.CalendarIcs; s.CalendarIcs = ""; }
                        if (Secret.TryUnprotect(s.Note, out var note)) s.Note = note; else { locked = true; s.LockedNote = s.Note; s.Note = ""; }
                        var clips = new List<string>();
                        foreach (var c in s.PinnedClips ?? new List<string>())
                            if (Secret.TryUnprotect(c, out var pc)) clips.Add(pc); else locked = true;
                        s.PinnedClips = clips;
                        if (locked)
                        {
                            App.Log("Unele date criptate sunt de pe alt cont sau alt PC și nu pot fi citite; le păstrez în settings.locked.json.");
                            string lockedCopy = Path.Combine(Folder, "settings.locked.json");
                            try { if (!File.Exists(lockedCopy) && SafeToWrite(lockedCopy)) File.Copy(FilePath, lockedCopy, false); } catch { }
                        }
                        s.SpeedHistory = (s.SpeedHistory ?? new List<Services.SpeedRecord>()).Where(h => h != null).ToList();
                        s.Standby ??= new List<string>();
                        s.HiddenPages ??= new List<string>();
                        s.Pages ??= new List<Widgets.UserPage>();
                        foreach (var pg in s.Pages) pg.Widgets ??= new List<Widgets.WidgetSlot>();
                        s.ThemeOverrides ??= new Dictionary<string, Dictionary<string, string>>();
                        s.CustomThemes ??= new List<ThemePalette>();
                        if (s.Accent == "#F5A524") s.Accent = "";          // old default = the theme's own accent now
                        if (s.ThemeMode != "light" && s.ThemeMode != "auto") s.ThemeMode = "dark";
                        s.Shelf ??= new List<string>();
                        s.Workspaces ??= new List<Services.Workspace>();
                        s.PinnedClips ??= new List<string>();
                        s.NormalizeFeatures();
                        return s;
                    }
                }
            }
            catch (Exception ex) { App.Log("Setări corupte, folosesc valorile implicite: " + ex.Message); }
            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Folder);
                using var hold = HoldFolder();          // as admin: the folder can't be renamed/replaced while we write
                if (!SafeToWrite(FilePath) || !SafeToWrite(FilePath + ".tmp")) return;
                // The calendar's secret link, the note and pinned clips are encrypted for this Windows account (DPAPI):
                // other users, backups or a copied file can't read them.
                var plain = (CalendarIcs, Note, PinnedClips);
                string json;
                try
                {
                    CalendarIcs = string.IsNullOrEmpty(CalendarIcs) && LockedIcs != null ? LockedIcs : Secret.Protect(CalendarIcs);
                    Note = string.IsNullOrEmpty(Note) && LockedNote != null ? LockedNote : Secret.Protect(Note);
                    PinnedClips = (PinnedClips ?? new List<string>()).Select(Secret.Protect).ToList();
                    json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                }
                finally { (CalendarIcs, Note, PinnedClips) = plain; }
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, json);
                File.Move(tmp, FilePath, true);         // a crash mid-write can't leave a half-written settings file
            }
            catch (Exception ex) { App.Log("Nu pot salva setările: " + ex.Message); }
        }

        /// <summary>
        /// Running as administrator, WinNotch must not follow a link/junction planted in its own (user-writable) folder,
        /// or it could be tricked into writing or deleting some other file with admin rights.
        /// </summary>
        public static bool SafeToWrite(string path)
        {
            if (!App.IsAdmin) return true;
            try
            {
                foreach (var p in new[] { Folder, path })
                {
                    if (p == null) continue;
                    var attr = File.Exists(p) || Directory.Exists(p) ? File.GetAttributes(p) : 0;
                    if ((attr & FileAttributes.ReparsePoint) != 0) return false;
                }
                return true;
            }
            catch { return false; }
        }

        /// <summary>
        /// As administrator, opens the settings folder without delete sharing, so no other process can rename it and put a
        /// junction in its place between our check and our write. Not needed (null) with normal rights.
        /// </summary>
        public static IDisposable HoldFolder()
        {
            if (!App.IsAdmin) return null;
            try
            {
                return new FileStream(Folder, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1,
                    (FileOptions)0x02000000 | (FileOptions)0x00200000);      // BACKUP_SEMANTICS (open a folder) | OPEN_REPARSE_POINT
            }
            catch { return null; }
        }

        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        /// <summary>
        /// Start with Windows: always a normal "Run" entry, with normal rights. (Up to 0.6.4, when running as admin this
        /// was a logon task that started the whole app elevated; that task is removed when the temperature helper is
        /// installed, because an elevated app that reads your user folders and environment can be steered.)
        /// </summary>
        public static bool StartWithWindows
        {
            get
            {
                try
                {
                    using var k = Registry.CurrentUser.OpenSubKey(RunKey);
                    return k?.GetValue("WinNotch") != null;
                }
                catch { return false; }
            }
            set
            {
                try
                {
                    using var k = Registry.CurrentUser.OpenSubKey(RunKey, true) ?? Registry.CurrentUser.CreateSubKey(RunKey);
                    k.DeleteValue("WinNotch", false);
                    if (App.IsAdmin) Services.TempHelper.Schtasks("/Delete /TN WinNotch /F");      // the old elevated logon task
                    if (value) k.SetValue("WinNotch", "\"" + Environment.ProcessPath + "\"");
                }
                catch (Exception ex) { App.Log("Pornire cu Windows: " + ex.Message); }
            }
        }
    }

    /// <summary>Encrypts text for the current Windows account (DPAPI). Values without the prefix are read as plain text (older settings).</summary>
    internal static class Secret
    {
        private const string Prefix = "dpapi:";

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct Blob { public int Size; public IntPtr Data; }

        [System.Runtime.InteropServices.DllImport("crypt32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern bool CryptProtectData(ref Blob input, string desc, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, ref Blob output);
        [System.Runtime.InteropServices.DllImport("crypt32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern bool CryptUnprotectData(ref Blob input, IntPtr desc, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, ref Blob output);
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr p);

        public static string Protect(string plain)
        {
            if (string.IsNullOrEmpty(plain)) return plain;
            if (plain.StartsWith(Prefix)) return plain;           // still the encrypted text we couldn't open: keep it
            var r = Run(System.Text.Encoding.UTF8.GetBytes(plain), true);
            if (r == null) { App.Log("DPAPI indisponibil; o valoare a fost salvată necriptată."); return plain; }
            return Prefix + Convert.ToBase64String(r);
        }

        /// <summary>False when the value is encrypted for another account or PC; then it must be kept as it is.</summary>
        public static bool TryUnprotect(string stored, out string plain)
        {
            plain = stored;
            if (string.IsNullOrEmpty(stored) || !stored.StartsWith(Prefix)) return true;
            try
            {
                var r = Run(Convert.FromBase64String(stored.Substring(Prefix.Length)), false);
                if (r == null) return false;
                plain = System.Text.Encoding.UTF8.GetString(r);
                return true;
            }
            catch { return false; }
        }

        private static byte[] Run(byte[] data, bool protect)
        {
            var handle = System.Runtime.InteropServices.GCHandle.Alloc(data, System.Runtime.InteropServices.GCHandleType.Pinned);
            var input = new Blob { Size = data.Length, Data = handle.AddrOfPinnedObject() };
            var output = new Blob();
            try
            {
                const int UiForbidden = 0x1;
                bool ok = protect
                    ? CryptProtectData(ref input, "WinNotch", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, UiForbidden, ref output)
                    : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, UiForbidden, ref output);
                if (!ok) return null;
                var result = new byte[output.Size];
                System.Runtime.InteropServices.Marshal.Copy(output.Data, result, 0, output.Size);
                return result;
            }
            finally
            {
                handle.Free();
                if (output.Data != IntPtr.Zero) LocalFree(output.Data);
            }
        }
    }
}
