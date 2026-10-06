using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using WinNotch.Features.SmartClipboard;

namespace WinNotch.Features.Shelf
{
    /// <summary>Why a path doesn't go on the shelf (or why the shelf said no). None = it went on.</summary>
    public enum ShelfRefusal { None, Invalid, Network, Missing, UnsafeShortcut, Duplicate, Full }

    /// <summary>What a path is, as far as the shelf knows (files are never opened to find out, only their name).</summary>
    public enum ShelfItemKind { File, Folder, Image, Archive, Document }

    /// <summary>The two image formats "shelf.convert-image" turns into each other.</summary>
    public enum ShelfImageFormat { None, Png, Jpeg }

    /// <summary>An image the shelf can't read or write; the message is a fixed Romanian sentence (shown, never logged).</summary>
    public sealed class ShelfImageException : Exception
    {
        public ShelfImageException(string message) : base(message) { }
    }

    /// <summary>
    /// One reference on the shelf: a local path (never a copy of the file). The id is stable (the same path always gets
    /// the same id, also after a restart) and says nothing about the path: 12 hex digits of its SHA-256.
    /// </summary>
    public sealed class ShelfItem
    {
        public ShelfItem(string path, bool isFolder)
        {
            Path = path;
            IsFolder = isFolder;
            Id = ShelfPaths.IdOf(path);
            Name = ShelfPaths.NameOf(path);
            Kind = ShelfPaths.KindOf(path, isFolder);
        }

        public string Id { get; }
        /// <summary>Normalized: drive letter in capitals, "\" separators, no "\" at the end (except "C:\").</summary>
        public string Path { get; }
        public bool IsFolder { get; }
        /// <summary>The last part of the path, shown in the list (never logged).</summary>
        public string Name { get; }
        public ShelfItemKind Kind { get; }
        /// <summary>False once a check (off the UI thread) found it gone; null = not checked since it was loaded.</summary>
        public bool? Exists { get; set; }

        public bool CanOcr => !IsFolder && ShelfPaths.IsOcrImage(Path);
        public ShelfImageFormat ConvertsTo => IsFolder ? ShelfImageFormat.None : ShelfPaths.ConvertTarget(Path);
    }

    /// <summary>
    /// The shelf (P23, ADR 0011): at most <see cref="MaxItems"/> references to local files and folders, in the order they
    /// came, without duplicates, kept until you empty it. Thread-safe: drops are checked off the UI thread and added from
    /// there, the actions read it from any thread. Nothing here touches the disk: the checks are in <see cref="ShelfPaths"/>.
    /// </summary>
    public sealed class ShelfModel
    {
        public const int MaxItems = 20;

        private readonly object _lock = new object();
        private List<ShelfItem> _items = new List<ShelfItem>();

        /// <summary>Goes up on every change (the notch redraws when it moved).</summary>
        public int Version { get; private set; }

        public IReadOnlyList<ShelfItem> Items { get { lock (_lock) return _items.ToArray(); } }
        public int Count { get { lock (_lock) return _items.Count; } }

        /// <summary>
        /// Adds a path that already passed <see cref="ShelfPaths.Check"/>. The 21st is refused (<see cref="ShelfRefusal.Full"/>):
        /// nothing already on the shelf is ever dropped without you asking. The same path again (any case, "/" or "\") is
        /// <see cref="ShelfRefusal.Duplicate"/>.
        /// </summary>
        public ShelfRefusal TryAdd(string normalizedPath, bool isFolder)
        {
            if (string.IsNullOrEmpty(normalizedPath)) return ShelfRefusal.Invalid;
            var item = new ShelfItem(normalizedPath, isFolder);
            lock (_lock)
            {
                if (_items.Any(x => x.Id == item.Id)) return ShelfRefusal.Duplicate;
                if (_items.Count >= MaxItems) return ShelfRefusal.Full;
                _items = new List<ShelfItem>(_items) { item };
                Version++;
                item.Exists = true;
                return ShelfRefusal.None;
            }
        }

        public bool Remove(string id)
        {
            lock (_lock)
            {
                int n = _items.RemoveAll(x => x.Id == id);
                if (n > 0) Version++;
                return n > 0;
            }
        }

        /// <summary>Removes the given ids (gone from the disk); returns how many went.</summary>
        public int RemoveAll(IEnumerable<string> ids)
        {
            var set = new HashSet<string>(ids ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
            lock (_lock)
            {
                int n = _items.RemoveAll(x => set.Contains(x.Id));
                if (n > 0) Version++;
                return n;
            }
        }

        public int Clear()
        {
            lock (_lock)
            {
                int n = _items.Count;
                _items = new List<ShelfItem>();
                if (n > 0) Version++;
                return n;
            }
        }

        /// <summary>
        /// The item an action asks for: its position in the list ("1" … "20", as shown) or its id (12 hex digits). Null when
        /// there is no such item (it was removed meanwhile).
        /// </summary>
        public ShelfItem Find(string key)
        {
            key = key?.Trim();
            if (string.IsNullOrEmpty(key)) return null;
            lock (_lock)
            {
                if (key.Length <= 2 && key.All(char.IsAsciiDigit) && int.TryParse(key, out int n))
                    return n >= 1 && n <= _items.Count ? _items[n - 1] : null;
                return ShelfPaths.IsValidId(key) ? _items.FirstOrDefault(x => x.Id == key.ToLowerInvariant()) : null;
            }
        }

        /// <summary>
        /// From settings.json: each entry checked again for its form only (no disk access at startup), network paths,
        /// broken ones and duplicates dropped, at most 20. A folder is saved with a "\" at the end.
        /// </summary>
        public void Load(IEnumerable<string> saved)
        {
            var list = new List<ShelfItem>();
            foreach (var s in saved ?? Enumerable.Empty<string>())
            {
                if (list.Count >= MaxItems) break;
                if (ShelfPaths.TryNormalize(s, out var p, out bool folder) != ShelfRefusal.None) continue;
                var item = new ShelfItem(p, folder);
                if (list.Any(x => x.Id == item.Id)) continue;
                list.Add(item);
            }
            lock (_lock) { _items = list; Version++; }
        }

        /// <summary>What goes into settings.json: the paths, folders with a "\" at the end (a new list every time).</summary>
        public List<string> ToSettings()
        {
            lock (_lock) return _items.Select(x => x.IsFolder && x.Path.Length > 3 ? x.Path + "\\" : x.Path).ToList();
        }
    }

    /// <summary>How a drop (or the smoke command) went: counts only, the message is fixed text.</summary>
    public sealed class ShelfAddReport
    {
        public int Added, Duplicates, Refused, Network, Full;
        /// <summary>The drop had more paths than are looked at (<see cref="ShelfPaths.MaxDropped"/>).</summary>
        public int Ignored;

        public void Count(ShelfRefusal r)
        {
            switch (r)
            {
                case ShelfRefusal.None: Added++; break;
                case ShelfRefusal.Duplicate: Duplicates++; break;
                case ShelfRefusal.Full: Full++; break;
                case ShelfRefusal.Network: Network++; Refused++; break;
                default: Refused++; break;
            }
        }

        /// <summary>A short Romanian sentence for the shelf's header (no file names).</summary>
        public string Message()
        {
            var parts = new List<string>();
            if (Added > 0) parts.Add(Added == 1 ? "Un element adăugat" : Added + " elemente adăugate");
            if (Duplicates > 0) parts.Add(Duplicates == 1 ? "unul era deja în raft" : Duplicates + " erau deja în raft");
            if (Network > 0) parts.Add(Network == 1 ? "unul e din rețea (refuzat)" : Network + " sunt din rețea (refuzate)");
            if (Refused - Network > 0) parts.Add(Refused - Network == 1 ? "unul nu e un fișier local valid" : (Refused - Network) + " nu sunt fișiere locale valide");
            if (Full + Ignored > 0) parts.Add("raftul e plin (maximum " + ShelfModel.MaxItems + "): " + (Full + Ignored == 1 ? "unul n-a mai încăput" : (Full + Ignored) + " n-au mai încăput"));
            if (parts.Count == 0) return "Nimic de adăugat.";
            string s = string.Join("; ", parts) + ".";
            return char.ToUpperInvariant(s[0]) + s.Substring(1);
        }

        /// <summary>The fixed line for log.txt: counters only.</summary>
        public string LogLine() =>
            "Raft: adăugate " + Added + ", refuzate " + Refused + " (din rețea " + Network + "), dubluri " + Duplicates + ", peste limită " + (Full + Ignored) + ".";
    }

    /// <summary>
    /// The checks on a path, pure: its form (<see cref="TryNormalize"/>) and, with an <see cref="IShelfFileSystem"/>, the
    /// drive, the file and the shortcuts (<see cref="Check"/>). Network paths are refused before anything touches them:
    /// opening one, even to read its icon, makes Windows log in to that server with your account (DOCUMENTATIE.md § 14).
    /// The network forms are the ones Smart Clipboard and the Shortcuts widget already refuse (<see cref="SmartClipRecognizer.IsNetworkPath"/>).
    /// </summary>
    public static class ShelfPaths
    {
        /// <summary>A drop with more paths than this: only the first ones are looked at.</summary>
        public const int MaxDropped = 64;
        /// <summary>.lnk / .url files bigger than this aren't read (a real shortcut is a few KB): refused.</summary>
        public const int MaxShortcutBytes = 64 * 1024;

        private static readonly string[] DeviceNames = { "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$" };
        /// <summary>Shell files that make Explorer fetch an icon or a location as soon as it shows them: never on the shelf.</summary>
        private static readonly string[] ShellLures = { ".scf", ".library-ms", ".searchconnector-ms" };

        /// <summary>
        /// The form only, no disk access: a local path with a drive letter ("C:\Users\ion\a.txt", also in quotes or with
        /// "/"). Refused: network paths (\\server\share, //server, \\?\UNC\…, file://server/…), device paths (\\?\C:\…,
        /// \\.\…), relative paths, "." / ".." parts, parts ending in "." or a space, device names (CON, NUL, COM1…),
        /// streams ("a.txt:x"), wildcards and control characters. Normalized: drive letter in capitals, "\" only, no
        /// trailing "\" (except "C:\"); <paramref name="folderHint"/> = it ended with a "\" (how settings.json marks a folder).
        /// </summary>
        public static ShelfRefusal TryNormalize(string raw, out string path, out bool folderHint)
        {
            path = null; folderHint = false;
            string s = raw?.Trim();
            if (string.IsNullOrEmpty(s)) return ShelfRefusal.Invalid;
            if (s.Length >= 2 && s[0] == '"' && s[s.Length - 1] == '"') s = s.Substring(1, s.Length - 2).Trim();
            if (SmartClipRecognizer.IsNetworkPath(s)) return ShelfRefusal.Network;          // \\, //, \/, \\?\, \\.\, file://host
            if (s.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) return ShelfRefusal.Invalid;
            s = s.Replace('/', '\\');
            if (!SmartClipRecognizer.TryPath(s, out var p)) return ShelfRefusal.Invalid;   // drive letter, no wildcards, no ":" after it
            if (p.IndexOf("\\\\", 2, StringComparison.Ordinal) >= 0) return ShelfRefusal.Invalid;
            folderHint = p.Length > 3 && p.EndsWith("\\", StringComparison.Ordinal);
            p = p.Length > 3 ? p.TrimEnd('\\') : p;
            foreach (var part in p.Substring(3).Split('\\', StringSplitOptions.RemoveEmptyEntries))
            {
                if (part == "." || part == ".." || part.EndsWith(".", StringComparison.Ordinal) || part.EndsWith(" ", StringComparison.Ordinal)) return ShelfRefusal.Invalid;
                if (IsDeviceName(part)) return ShelfRefusal.Invalid;
            }
            path = char.ToUpperInvariant(p[0]) + p.Substring(1);
            return ShelfRefusal.None;
        }

        /// <summary>CON, NUL, COM1, LPT9… (also with an extension, "nul.txt"): Windows opens the device, not a file.</summary>
        private static bool IsDeviceName(string part)
        {
            string stem = part.Split('.')[0].TrimEnd(' ').ToUpperInvariant();
            if (DeviceNames.Contains(stem)) return true;
            return stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) &&
                   (char.IsAsciiDigit(stem[3]) || stem[3] == '¹' || stem[3] == '²' || stem[3] == '³');
        }

        /// <summary>
        /// The whole check before a path goes on the shelf (off the UI thread: a sleeping USB stick can take seconds).
        /// After the form: the drive must be local or removable (a mapped network drive is refused, before the file is
        /// looked at), the path must exist; a shortcut must be verifiable locally (<see cref="ShelfShortcuts"/>).
        /// </summary>
        public static ShelfRefusal Check(string raw, IShelfFileSystem fs, out string path, out bool isFolder)
        {
            isFolder = false;
            var r = TryNormalize(raw, out path, out _);
            if (r != ShelfRefusal.None) return r;
            if (fs == null) return ShelfRefusal.Missing;
            switch (fs.DriveOf(path.Substring(0, 3)))
            {
                case ShelfDrive.Network: path = null; return ShelfRefusal.Network;
                case ShelfDrive.Missing: path = null; return ShelfRefusal.Missing;
            }
            var kind = fs.Kind(path);
            if (kind == ShelfEntry.None) { path = null; return ShelfRefusal.Missing; }
            isFolder = kind == ShelfEntry.Folder;
            if (!isFolder)
            {
                string ext = Extension(path);
                if (ShellLures.Contains(ext)) { path = null; return ShelfRefusal.UnsafeShortcut; }
                if (ext == ".lnk" || ext == ".url")
                {
                    var head = fs.ReadHead(path, MaxShortcutBytes + 1);
                    bool ok = head != null && head.Length <= MaxShortcutBytes &&
                              (ext == ".lnk" ? ShelfShortcuts.IsLocalLink(head, fs.Expand, root => fs.DriveOf(root)) : ShelfShortcuts.IsLocalUrlFile(head));
                    if (!ok) { path = null; return ShelfRefusal.UnsafeShortcut; }
                }
            }
            return ShelfRefusal.None;
        }

        /// <summary>The extension in lowercase, with the dot ("" without one).</summary>
        public static string Extension(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            int slash = path.LastIndexOf('\\'), dot = path.LastIndexOf('.');
            return dot > slash && dot < path.Length - 1 ? path.Substring(dot).ToLowerInvariant() : "";
        }

        /// <summary>The last part of a normalized path ("a.txt"; "C:\" for a drive).</summary>
        public static string NameOf(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            if (path.Length <= 3) return path;
            int slash = path.LastIndexOf('\\');
            return slash >= 0 ? path.Substring(slash + 1) : path;
        }

        /// <summary>The folder a normalized path is in ("C:\a" for "C:\a\b.txt", "C:\" for "C:\b.txt", null for "C:\").</summary>
        public static string FolderOf(string path)
        {
            if (string.IsNullOrEmpty(path) || path.Length <= 3) return null;
            int slash = path.LastIndexOf('\\');
            if (slash < 2) return null;
            return slash == 2 ? path.Substring(0, 3) : path.Substring(0, slash);
        }

        /// <summary>12 lowercase hex digits of the SHA-256 of the path in capitals (paths on Windows ignore case).</summary>
        public static string IdOf(string path)
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes((path ?? "").ToUpperInvariant()));
            return Convert.ToHexString(hash, 0, 6).ToLowerInvariant();
        }

        public static bool IsValidId(string id) =>
            id != null && id.Length == 12 && id.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'));

        private static readonly string[] OcrImages = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff" };
        private static readonly string[] Archives = { ".zip", ".7z", ".rar", ".tar", ".gz", ".tgz" };
        private static readonly string[] Documents = { ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".md", ".odt", ".rtf", ".csv" };

        public static bool IsOcrImage(string path) => OcrImages.Contains(Extension(path));

        /// <summary>PNG → JPEG, JPEG → PNG (by the name; the content is checked again before converting), else None.</summary>
        public static ShelfImageFormat ConvertTarget(string path) => Extension(path) switch
        {
            ".png" => ShelfImageFormat.Jpeg,
            ".jpg" or ".jpeg" => ShelfImageFormat.Png,
            _ => ShelfImageFormat.None,
        };

        /// <summary>The icon's kind, from the name only (no Shell icon is ever asked for: see ADR 0011).</summary>
        public static ShelfItemKind KindOf(string path, bool isFolder)
        {
            if (isFolder) return ShelfItemKind.Folder;
            string ext = Extension(path);
            if (OcrImages.Contains(ext) || ext == ".webp" || ext == ".heic") return ShelfItemKind.Image;
            if (Archives.Contains(ext)) return ShelfItemKind.Archive;
            if (Documents.Contains(ext)) return ShelfItemKind.Document;
            return ShelfItemKind.File;
        }

        /// <summary>The content's format from its first bytes: PNG (89 50 4E 47 0D 0A 1A 0A), JPEG (FF D8 FF), else None.</summary>
        public static ShelfImageFormat Sniff(byte[] head)
        {
            if (head == null) return ShelfImageFormat.None;
            if (head.Length >= 8 && head[0] == 0x89 && head[1] == 0x50 && head[2] == 0x4E && head[3] == 0x47 && head[4] == 0x0D && head[5] == 0x0A && head[6] == 0x1A && head[7] == 0x0A)
                return ShelfImageFormat.Png;
            if (head.Length >= 3 && head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF) return ShelfImageFormat.Jpeg;
            return ShelfImageFormat.None;
        }

        /// <summary>
        /// For a JPEG made from a PNG (JPEG has no transparency): BGRA pixels put over white, in place, alpha set to 255.
        /// A transparent pixel becomes white, an opaque one stays as it is (instead of the black a plain conversion gives).
        /// </summary>
        public static void FlattenOnWhite(byte[] bgra)
        {
            if (bgra == null) return;
            for (int i = 0; i + 3 < bgra.Length; i += 4)
            {
                int a = bgra[i + 3];
                if (a == 255) continue;
                for (int c = 0; c < 3; c++) bgra[i + c] = (byte)((bgra[i + c] * a + 255 * (255 - a) + 127) / 255);
                bgra[i + 3] = 255;
            }
        }
    }

    /// <summary>
    /// New file names beside an existing file, never one that is there already: "x.zip", "x (2).zip" … "x (999).zip".
    /// The caller creates each with FileMode.CreateNew and takes the next one when it exists (no check-then-write race).
    /// </summary>
    public static class ShelfNames
    {
        public const int MaxTries = 999;

        public static IEnumerable<string> Candidates(string baseName, string extension)
        {
            if (string.IsNullOrEmpty(baseName)) baseName = "Raft";
            extension ??= "";
            yield return baseName + extension;
            for (int i = 2; i <= MaxTries; i++) yield return baseName + " (" + i + ")" + extension;
        }

        /// <summary>The zip's name without ".zip", from the item's name: a folder's name, a file's without its extension ("raport.pdf" → "raport"; ".env" stays ".env").</summary>
        public static string ZipBase(string name, bool isFolder)
        {
            name ??= "";
            if (isFolder) return name;
            int dot = name.LastIndexOf('.');
            return dot > 0 ? name.Substring(0, dot) : name;
        }

        /// <summary>The converted image's extension.</summary>
        public static string ExtensionFor(ShelfImageFormat f) => f == ShelfImageFormat.Png ? ".png" : f == ShelfImageFormat.Jpeg ? ".jpg" : "";
    }

    /// <summary>
    /// Plan B of the spike (ADR 0011): the closed pill lets clicks through, so it can't receive a drop; it opens by hover
    /// while you drag over it instead. The old rule "a button pressed while the mouse is on the pill was a click for the
    /// window below" would keep it shut during a drag, so: a left button already down when the mouse came onto the pill
    /// is a drag carried in (it may open the notch); one pressed on the pill stays a click-through. Pure, fed every tick.
    /// </summary>
    public sealed class ShelfDragHover
    {
        private bool _downOutside;

        /// <summary>True while a drag carried in from outside is over the pill.</summary>
        public bool CarriedIn { get; private set; }

        public bool Update(bool inside, bool leftDown)
        {
            if (!leftDown) { _downOutside = false; CarriedIn = false; return false; }
            if (!inside) { _downOutside = true; CarriedIn = false; return false; }
            CarriedIn = _downOutside;
            return CarriedIn;
        }

        public void Reset() { _downOutside = false; CarriedIn = false; }
    }
}
