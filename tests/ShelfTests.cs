using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using WinNotch.Core.Actions;
using WinNotch.Core.Flags;
using WinNotch.Features.Shelf;
using WinNotch.Features.Smoke;

namespace WinNotch
{
    public static partial class T
    {
        /// <summary>The disk as the shelf sees it, faked: drives, entries, file heads; every path it was asked about.</summary>
        sealed class FakeShelfFs : IShelfFileSystem
        {
            public readonly Dictionary<string, ShelfDrive> Drives = new Dictionary<string, ShelfDrive>(StringComparer.OrdinalIgnoreCase)
            {
                [@"C:\"] = ShelfDrive.Local, [@"D:\"] = ShelfDrive.Local, [@"E:\"] = ShelfDrive.Removable, [@"Z:\"] = ShelfDrive.Network,
            };
            public readonly Dictionary<string, ShelfEntry> Entries = new Dictionary<string, ShelfEntry>(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, byte[]> Heads = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            public readonly List<string> Touched = new List<string>();
            public ShelfDrive DriveOf(string root) => Drives.TryGetValue(root, out var d) ? d : ShelfDrive.Missing;
            public ShelfEntry Kind(string path) { Touched.Add(path); return Entries.TryGetValue(path, out var k) ? k : ShelfEntry.None; }
            public byte[] ReadHead(string path, int max) { Touched.Add(path); return Heads.TryGetValue(path, out var b) ? b.Take(max).ToArray() : null; }
            public string Expand(string text) => (text ?? "").Replace("%LOGONSERVER%", @"\\DC01").Replace("%ProgramFiles%", @"C:\Program Files");
            public FakeShelfFs File(string p) { Entries[p] = ShelfEntry.File; return this; }
            public FakeShelfFs Folder(string p) { Entries[p] = ShelfEntry.Folder; return this; }
        }

        /// <summary>The app's side of the "shelf.*" actions, faked.</summary>
        sealed class FakeShelfHost : IShelfHost
        {
            public ShelfModel Model { get; } = new ShelfModel();
            public IShelfFileSystem Files { get; set; }
            public readonly List<string> Written = new List<string>(), Folders = new List<string>(), Ocr = new List<string>();
            public readonly List<string[]> Copied = new List<string[]>();
            public readonly List<(string Path, ShelfImageFormat To)> Converts = new List<(string, ShelfImageFormat)>();
            public bool Busy;
            public string OcrText = "linia unu\nlinia doi";
            public string ConvertError;
            public int ChangedCount;
            public bool SetText(string text) { if (Busy) return false; Written.Add(text); return true; }
            public bool SetFiles(IReadOnlyList<string> paths) { if (Busy) return false; Copied.Add(paths.ToArray()); return true; }
            public string OpenFolder(string folder) { Folders.Add(folder); return null; }
            public string OcrError;
            public Task<string> RecognizeTextAsync(string imagePath, CancellationToken ct)
            {
                Ocr.Add(imagePath);
                if (OcrError != null) throw new ShelfImageException(OcrError);
                return Task.FromResult(OcrText);
            }
            public Task<ShelfFileResult> ConvertImageAsync(string imagePath, ShelfImageFormat to, CancellationToken ct)
            {
                Converts.Add((imagePath, to));
                if (ConvertError != null) throw new ShelfImageException(ConvertError);
                string made = Path.ChangeExtension(imagePath, ShelfNames.ExtensionFor(to)).Replace('/', '\\');
                return Task.FromResult(new ShelfFileResult { Success = true, Path = made });
            }
            public void Changed() => ChangedCount++;
        }

        // ------------------------------------------------------------------ .lnk files, written by hand (MS-SHLLINK)

        static void U32(List<byte> b, uint v) => b.AddRange(BitConverter.GetBytes(v));
        static void U16(List<byte> b, int v) => b.AddRange(BitConverter.GetBytes((ushort)v));
        static void Counted(List<byte> b, string s) { U16(b, s.Length); b.AddRange(Encoding.Unicode.GetBytes(s)); }

        /// <summary>A shortcut to <paramref name="basePath"/>, with the parts the tests switch on and off.</summary>
        static byte[] Lnk(string basePath, uint driveType = 3, bool network = false, bool linkInfo = true, bool localFlag = true,
                          string workDir = null, string icon = null, string envTarget = null, bool unicodeOffsets = true, bool idList = true)
        {
            uint flags = 0x80;                                                   // IsUnicode
            if (idList) flags |= 0x1;
            if (linkInfo) flags |= 0x2;
            if (workDir != null) flags |= 0x10;
            if (icon != null) flags |= 0x40;
            if (envTarget != null) flags |= 0x200;
            var b = new List<byte>();
            U32(b, 0x4C);
            b.AddRange(new byte[] { 0x01, 0x14, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0xC0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x46 });
            U32(b, flags);
            b.AddRange(new byte[0x4C - b.Count]);
            if (idList) { U16(b, 2); U16(b, 0); }
            if (linkInfo)
            {
                int header = unicodeOffsets ? 0x24 : 0x1C;
                var vol = new List<byte>();
                U32(vol, 0x11); U32(vol, driveType); U32(vol, 0x1234); U32(vol, 0x10); vol.Add(0);
                byte[] ansi = Encoding.Latin1.GetBytes(basePath + "\0"), suffix = { 0 };
                byte[] wide = Encoding.Unicode.GetBytes(basePath + "\0"), wideSuffix = { 0, 0 };
                int volOff = header, baseOff = volOff + vol.Count, sufOff = baseOff + ansi.Length, wideOff = sufOff + suffix.Length, wideSufOff = wideOff + wide.Length;
                int size = (unicodeOffsets ? wideSufOff + wideSuffix.Length : wideOff);
                var li = new List<byte>();
                U32(li, (uint)size); U32(li, (uint)header); U32(li, (localFlag ? 1u : 0u) | (network ? 2u : 0u));
                U32(li, (uint)volOff); U32(li, (uint)baseOff); U32(li, network ? (uint)sufOff : 0u); U32(li, (uint)sufOff);
                if (unicodeOffsets) { U32(li, (uint)wideOff); U32(li, (uint)wideSufOff); }
                li.AddRange(vol); li.AddRange(ansi); li.AddRange(suffix);
                if (unicodeOffsets) { li.AddRange(wide); li.AddRange(wideSuffix); }
                b.AddRange(li);
            }
            if (workDir != null) Counted(b, workDir);
            if (icon != null) Counted(b, icon);
            if (envTarget != null)
            {
                U32(b, 0x314); U32(b, 0xA0000001);
                var a = new byte[260]; var w = new byte[520];
                Encoding.Latin1.GetBytes(envTarget).CopyTo(a, 0);
                Encoding.Unicode.GetBytes(envTarget).CopyTo(w, 0);
                b.AddRange(a); b.AddRange(w);
            }
            U32(b, 0);
            return b.ToArray();
        }

        static string NewTempDir()
        {
            string d = Path.Combine(Path.GetTempPath(), "winnotch-raft-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(d);
            return d;
        }

        static ActionResult ShelfRun(ActionRegistry r, string id, string element = null, ActionInvoker inv = ActionInvoker.UI) =>
            r.InvokeAsync(id, element == null ? null : new Dictionary<string, string> { [ShelfActions.ElementParam] = element }, inv).GetAwaiter().GetResult();

        /// <summary>"shelf.copy-files": the group parameter ("elemente"), left out when <paramref name="elements"/> is null.</summary>
        static ActionResult ShelfCopyFiles(ActionRegistry r, string elements = null, ActionInvoker inv = ActionInvoker.UI) =>
            r.InvokeAsync(ShelfActions.CopyFilesId, elements == null ? null : new Dictionary<string, string> { [ShelfActions.ElementsParam] = elements }, inv)
             .GetAwaiter().GetResult();

        /// <summary>
        /// P23 "Raft": the switch, every form of network path (refused before the disk is touched) and valid local paths,
        /// mapped drives, shortcuts (.lnk / .url) checked from their bytes, the model (20 at most, the 21st refused, no
        /// duplicates, ids), new names never over an existing file, the zip, the image helpers, the actions through the
        /// registry with a fake host, the log without paths, the settings, the hover rule while dragging, the smoke command
        /// and the WPF hooks pinned in the source.
        /// </summary>
        static void ShelfTests()
        {
            // ---- the switch
            var info = FeatureCatalog.Find(ShelfActions.FeatureId);
            var onStore = AppSettings.NewFeatures(); onStore[ShelfActions.FeatureId] = true;
            Check("SH1", "Comutatorul „shelf”: în catalog, Experimental, oprit implicit, oprit în mod sigur chiar dacă e pornit; același id ca FeatureCatalog.Shelf",
                  info != null && FeatureCatalog.Shelf == ShelfActions.FeatureId && info.Stage == FeatureStage.Experimental && !info.DefaultOn && info.Name == "Raft" &&
                  !new FeatureFlags(AppSettings.NewFeatures()).IsEnabled(ShelfActions.FeatureId) && new FeatureFlags(onStore).IsEnabled(ShelfActions.FeatureId) &&
                  !new FeatureFlags(onStore, safeMode: true).IsEnabled(ShelfActions.FeatureId));

            // ---- the form of a path
            ShelfRefusal N(string raw) => ShelfPaths.TryNormalize(raw, out _, out _);
            string Norm1(string raw) => ShelfPaths.TryNormalize(raw, out var p, out _) == ShelfRefusal.None ? p : null;
            Check("SH2", "Căi locale valide: literă de unitate, „/” sau „\\”, ghilimele, diacritice, spații, rădăcina; normalizate (literă mare, „\\”, fără „\\” la final)",
                  Norm1(@"C:\Users\ion\Documente\raport final.pdf") == @"C:\Users\ion\Documente\raport final.pdf" && Norm1("c:/Users/ion/a.txt") == @"C:\Users\ion\a.txt" &&
                  Norm1("\"D:\\Poze\\vară ăîșț.png\"") == @"D:\Poze\vară ăîșț.png" && Norm1(@"E:\") == @"E:\" && Norm1(@"C:\Proiect\") == @"C:\Proiect" &&
                  ShelfPaths.TryNormalize(@"C:\Proiect\", out _, out bool hint) == ShelfRefusal.None && hint && Norm1(@"C:\RUNNER~1\x.txt") == @"C:\RUNNER~1\x.txt");
            var networkForms = new[]
            {
                @"\\server\share\a.txt", @"\\server", "//server/share/a.txt", @"\/server/x", @"/\server\x", @"\\?\UNC\server\share\a.txt", @"\\?\unc\server\x",
                @"\\?\C:\Users\a.txt", @"\\.\pipe\x", @"\\.\C:\a.txt", @"\\192.168.1.10\c$\x", @"\\server@SSL@443\DavWWWRoot\x", "file://server/share/a.txt",
                "\"\\\\server\\share\\a.txt\"", @"  \\server\share  ",
            };
            Check("SH3", "Orice formă de cale de rețea e refuzată ca rețea, fără să atingă discul: \\\\server, //server, \\/, \\\\?\\UNC\\, \\\\?\\C:\\ (dispozitiv), \\\\.\\, IP, WebDAV, file://server, în ghilimele, cu spații",
                  networkForms.All(p => N(p) == ShelfRefusal.Network), string.Join(" | ", networkForms.Where(p => N(p) != ShelfRefusal.Network)));
            var invalid = new[]
            {
                null, "", "   ", "a.txt", @"Users\ion", @"C:a.txt", "C:", @"C:\a\..\b", @"C:\a\.\b", @"C:\a.", @"C:\a \b", @"C:\x\CON", @"C:\x\nul.txt", @"C:\COM1",
                @"C:\LPT9.log", @"C:\a.txt:ascuns", @"C:\a*.txt", @"C:\a?.txt", "C:\\a\tb", @"C:\a\\b", "file:///C:/a.txt", "http://x.ro/a", new string('a', 2000),
                @"C:\" + new string('a', 1100),
            };
            Check("SH4", "Căi refuzate ca nevalide: relative, fără „\\” după literă, „.” / „..”, punct sau spațiu la final, nume de dispozitiv (CON, NUL, COM1, LPT9), fluxuri „:x”, metacaractere, control, „\\\\” dublu, file:///, http, prea lungi",
                  invalid.All(p => N(p) == ShelfRefusal.Invalid), string.Join(" | ", invalid.Where(p => N(p) != ShelfRefusal.Invalid).Select(p => p?.Substring(0, Math.Min(p.Length, 30)) ?? "null")));

            // ---- with the disk
            var fs = new FakeShelfFs().File(@"C:\a\doc.pdf").Folder(@"C:\Proiect").File(@"E:\stick\poza.png").File(@"Z:\birou\x.txt").File(@"Q:\x.txt");
            ShelfRefusal C(string raw, out string p, out bool folder) => ShelfPaths.Check(raw, fs, out p, out folder);
            var r1 = C(@"C:\a\doc.pdf", out var p1, out bool f1);
            var r2 = C(@"C:\Proiect\", out var p2, out bool f2);
            var r3 = C(@"E:\stick\poza.png", out _, out _);
            int before = fs.Touched.Count;
            var r4 = C(@"Z:\birou\x.txt", out var p4, out _);
            var r5 = C(@"\\server\share\x.txt", out _, out _);
            bool untouched = fs.Touched.Count == before;
            Check("SH5", "Cu discul: fișier și folder local acceptate, stick (amovibil) acceptat; unitate mapată de rețea (Z:) și cale UNC refuzate ca rețea fără să fie atinse; unitate lipsă și fișier lipsă refuzate",
                  r1 == ShelfRefusal.None && p1 == @"C:\a\doc.pdf" && !f1 && r2 == ShelfRefusal.None && p2 == @"C:\Proiect" && f2 && r3 == ShelfRefusal.None &&
                  r4 == ShelfRefusal.Network && p4 == null && r5 == ShelfRefusal.Network && untouched &&
                  C(@"Q:\x.txt", out _, out _) == ShelfRefusal.Missing && C(@"C:\a\lipsa.pdf", out _, out _) == ShelfRefusal.Missing &&
                  ShelfPaths.Check(@"C:\a\doc.pdf", null, out _, out _) == ShelfRefusal.Missing);

            // ---- shortcuts
            var lfs = new FakeShelfFs();
            bool Link(byte[] b) { lfs.File(@"C:\s\x.lnk"); lfs.Heads[@"C:\s\x.lnk"] = b; return ShelfPaths.Check(@"C:\s\x.lnk", lfs, out _, out _) == ShelfRefusal.None; }
            Check("SH6", "Scurtături .lnk citite ca octeți (fără rezolvare, fără rețea): țintă locală / pe stick → acceptată; LinkInfo de rețea, volum „remote”, fără LinkInfo, țintă pe unitate mapată, dosar de lucru / iconiță / țintă %VAR% de rețea, fișier stricat sau prea mare → refuzată",
                  Link(Lnk(@"C:\Program Files\App\app.exe")) && Link(Lnk(@"E:\stick\a.txt", driveType: 2)) && Link(Lnk(@"C:\x.txt", unicodeOffsets: false)) &&
                  Link(Lnk(@"C:\x.txt", workDir: @"C:\x", icon: @"%ProgramFiles%\App\app.ico", envTarget: @"%ProgramFiles%\App\app.exe")) && Link(Lnk(@"C:\x.txt", idList: false)) &&
                  !Link(Lnk(@"C:\x.txt", network: true)) && !Link(Lnk(@"C:\x.txt", driveType: 4)) && !Link(Lnk(@"C:\x.txt", driveType: 0)) && !Link(Lnk(@"C:\x.txt", linkInfo: false)) &&
                  !Link(Lnk(@"C:\x.txt", localFlag: false)) && !Link(Lnk(@"Z:\birou\x.txt")) && !Link(Lnk(@"\\server\share\x.txt")) && !Link(Lnk(@"Q:\x.txt")) &&
                  !Link(Lnk(@"C:\x.txt", workDir: @"\\server\share")) && !Link(Lnk(@"C:\x.txt", icon: @"\\server\i.ico")) && !Link(Lnk(@"C:\x.txt", icon: "//server/i.ico")) &&
                  !Link(Lnk(@"C:\x.txt", envTarget: @"%LOGONSERVER%\share\x.exe")) && !Link(Lnk(@"C:\x.txt", envTarget: @"%NECUNOSCUT%\x.exe")) &&
                  !Link(Lnk(@"C:\x.txt", envTarget: @"\\server\x.exe")) && !Link(new byte[0]) && !Link(new byte[10]) && !Link(Lnk(@"C:\x.txt").Take(100).ToArray()) &&
                  !Link(Lnk(@"C:\x.txt").Concat(new byte[ShelfPaths.MaxShortcutBytes]).ToArray()) && !Link(Encoding.ASCII.GetBytes("nu e o scurtătură, doar text oarecare suficient de lung ca să treacă de antet")));
            bool Url(string text, string ext = ".url")
            {
                string p = @"C:\s\x" + ext;
                lfs.File(p); lfs.Heads[p] = Encoding.UTF8.GetBytes(text);
                return ShelfPaths.Check(p, lfs, out _, out _) == ShelfRefusal.None;
            }
            Check("SH7", "Scurtături .url (text INI): http(s) și iconiță locală → acceptate; URL file://server, iconiță \\\\server, dosar de lucru UNC → refuzate; .scf, .library-ms, .searchconnector-ms refuzate mereu",
                  Url("[InternetShortcut]\r\nURL=https://example.ro/a?b=1\r\nIconFile=C:\\Windows\\x.ico\r\nIconIndex=0\r\n") && Url("[InternetShortcut]\nURL=file:///C:/a.txt\n") &&
                  !Url("[InternetShortcut]\r\nURL=file://server/share/a.txt\r\n") && !Url("[InternetShortcut]\r\nURL=https://x.ro\r\nIconFile=\\\\server\\i.ico\r\n") &&
                  !Url("[InternetShortcut]\nURL=https://x.ro\nWorkingDirectory=\\\\server\\s\n") && !Url("[InternetShortcut]\nIconFile=\"//server/i.ico\"\n") && !Url("") && !Url("fără egal") &&
                  !Url("[Shell]\nIconFile=C:\\x.ico\n", ".scf") && !Url("<x/>", ".library-ms") && !Url("<x/>", ".searchConnector-ms"));

            // ---- the model
            var m = new ShelfModel();
            var adds = Enumerable.Range(1, 21).Select(i => m.TryAdd(@"C:\f\" + i + ".txt", false)).ToList();
            var dupe = m.TryAdd(@"c:\F\3.TXT", false);
            Check("SH8", "Raftul: maximum 20; al 21-lea e refuzat (Full) și nimic nu e scos fără să ceri; dublura (altă scriere a aceleiași căi) e refuzată; ordinea în care au venit",
                  adds.Take(20).All(a => a == ShelfRefusal.None) && adds[20] == ShelfRefusal.Full && m.Count == 20 && dupe == ShelfRefusal.Duplicate &&
                  m.Items[0].Path == @"C:\f\1.txt" && m.Items[19].Path == @"C:\f\20.txt" && ShelfModel.MaxItems == 20 && m.TryAdd(null, false) == ShelfRefusal.Invalid);
            var first = m.Items[0];
            int v0 = m.Version;
            bool removed = m.Remove(first.Id), again = m.Remove(first.Id);
            Check("SH9", "Id stabil (12 hex, același pentru aceeași cale în orice scriere, altul pentru altă cale, nu conține calea); Find după poziție (1–20) sau id; Remove, RemoveAll, Clear schimbă versiunea",
                  ShelfPaths.IsValidId(first.Id) && first.Id == ShelfPaths.IdOf(@"c:\F\1.TXT") && first.Id != m.Items[0].Id && !first.Id.Contains("txt") &&
                  removed && !again && m.Version > v0 && m.Find("1")?.Path == @"C:\f\2.txt" && m.Find("19")?.Path == @"C:\f\20.txt" && m.Find("20") == null && m.Find("0") == null &&
                  m.Find(m.Items[2].Id) == m.Items[2] && m.Find(m.Items[2].Id.ToUpperInvariant()) == m.Items[2] && m.Find("abc") == null && m.Find(null) == null && m.Find("-1") == null &&
                  m.RemoveAll(new[] { m.Items[0].Id, m.Items[1].Id, "nu" }) == 2 && m.Count == 17 && m.Clear() == 17 && m.Count == 0 && m.Clear() == 0);
            var loaded = new ShelfModel();
            var saved = new List<string> { @"C:\a.txt", @"\\server\x", @"c:\A.TXT", @"C:\Proiect\", "gunoi", null, @"D:\b\..\c" };
            saved.AddRange(Enumerable.Range(1, 30).Select(i => @"E:\x\" + i));
            loaded.Load(saved);
            var back = loaded.ToSettings();
            Check("SH10", "Din settings.json: forma verificată din nou (fără disc), rețea / gunoi / dubluri scoase, cel mult 20; folderul se salvează cu „\\” la final și revine ca folder",
                  loaded.Count == 20 && loaded.Items[0].Path == @"C:\a.txt" && loaded.Items[1].Path == @"C:\Proiect" && loaded.Items[1].IsFolder && !loaded.Items[0].IsFolder &&
                  back[1] == @"C:\Proiect\" && back[0] == @"C:\a.txt" && back.All(s => !s.StartsWith(@"\\")) && back.Count == 20 &&
                  !ReferenceEquals(back, loaded.ToSettings()));

            // ---- kinds, names, formats
            Check("SH11", "Tipul după nume (fără iconița Shell): folder, imagine, arhivă, document, fișier; OCR doar pe imagini, conversie doar PNG ↔ JPG; nume, folder, extensie",
                  ShelfPaths.KindOf(@"C:\a", true) == ShelfItemKind.Folder && ShelfPaths.KindOf(@"C:\a.PNG", false) == ShelfItemKind.Image && ShelfPaths.KindOf(@"C:\a.zip", false) == ShelfItemKind.Archive &&
                  ShelfPaths.KindOf(@"C:\a.pdf", false) == ShelfItemKind.Document && ShelfPaths.KindOf(@"C:\a.exe", false) == ShelfItemKind.File && ShelfPaths.KindOf(@"C:\a.b\c", false) == ShelfItemKind.File &&
                  new ShelfItem(@"C:\p\a.jpg", false).CanOcr && !new ShelfItem(@"C:\p\a.pdf", false).CanOcr && !new ShelfItem(@"C:\p\a.png", true).CanOcr &&
                  new ShelfItem(@"C:\p\a.png", false).ConvertsTo == ShelfImageFormat.Jpeg && new ShelfItem(@"C:\p\a.JPEG", false).ConvertsTo == ShelfImageFormat.Png &&
                  new ShelfItem(@"C:\p\a.gif", false).ConvertsTo == ShelfImageFormat.None && ShelfPaths.NameOf(@"C:\p\a.jpg") == "a.jpg" && ShelfPaths.NameOf(@"C:\") == @"C:\" &&
                  ShelfPaths.FolderOf(@"C:\p\a.jpg") == @"C:\p" && ShelfPaths.FolderOf(@"C:\a.jpg") == @"C:\" && ShelfPaths.FolderOf(@"C:\") == null && ShelfPaths.Extension(@"C:\a.b\c") == "");
            var cands = ShelfNames.Candidates("raport", ".zip").ToList();
            Check("SH12", "Nume noi: „x.zip”, „x (2).zip”… „x (999).zip”, apoi nimic; baza zip-ului: numele folderului, fișierul fără extensie („.env” rămâne „.env”); extensiile conversiei",
                  cands.Count == 999 && cands[0] == "raport.zip" && cands[1] == "raport (2).zip" && cands[998] == "raport (999).zip" && cands.Distinct().Count() == 999 &&
                  ShelfNames.ZipBase("raport.final.pdf", false) == "raport.final" && ShelfNames.ZipBase(".env", false) == ".env" && ShelfNames.ZipBase("Proiect.v2", true) == "Proiect.v2" &&
                  ShelfNames.Candidates(null, ".zip").First() == "Raft.zip" && ShelfNames.ExtensionFor(ShelfImageFormat.Png) == ".png" && ShelfNames.ExtensionFor(ShelfImageFormat.Jpeg) == ".jpg");
            var px = new byte[] { 10, 20, 30, 255, 10, 20, 30, 0, 0, 0, 0, 128, 200, 100, 50, 128 };
            ShelfPaths.FlattenOnWhite(px);
            Check("SH13", "Formatul din octeți (PNG, JPEG, nimic); pixelii puși pe alb pentru JPEG: opac neschimbat, transparent → alb, pe jumătate → amestec",
                  ShelfPaths.Sniff(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1 }) == ShelfImageFormat.Png && ShelfPaths.Sniff(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }) == ShelfImageFormat.Jpeg &&
                  ShelfPaths.Sniff(new byte[] { 0x89, 0x50 }) == ShelfImageFormat.None && ShelfPaths.Sniff(Encoding.ASCII.GetBytes("GIF89a")) == ShelfImageFormat.None && ShelfPaths.Sniff(null) == ShelfImageFormat.None &&
                  px.Take(4).SequenceEqual(new byte[] { 10, 20, 30, 255 }) && px.Skip(4).Take(4).SequenceEqual(new byte[] { 255, 255, 255, 255 }) &&
                  px[8] == 127 && px[11] == 255 && px[12] == 227 && px[13] == 177 && px[14] == 152 && px[15] == 255);

            // ---- new files on the real disk: never over an existing one
            string dir = NewTempDir();
            try
            {
                File.WriteAllText(Path.Combine(dir, "x.zip"), "vechi");
                File.WriteAllText(Path.Combine(dir, "x (2).zip"), "vechi 2");
                var made = ShelfFiles.CreateNewBeside(dir, "x", ".zip", (s, t) => s.Write(new byte[] { 1, 2, 3 }), CancellationToken.None);
                bool failedWrite = false;
                try { ShelfFiles.CreateNewBeside(dir, "y", ".zip", (s, t) => { s.WriteByte(1); throw new IOException("disc plin"); }, CancellationToken.None); }
                catch (IOException) { failedWrite = true; }
                bool cancelled = false;
                using (var cts = new CancellationTokenSource())
                {
                    try { ShelfFiles.CreateNewBeside(dir, "z", ".zip", (s, t) => { s.WriteByte(1); cts.Cancel(); t.ThrowIfCancellationRequested(); }, cts.Token); }
                    catch (OperationCanceledException) { cancelled = true; }
                }
                Check("SH14", "Fișier nou alături, cu FileMode.CreateNew: „x.zip” și „x (2).zip” existente rămân neatinse, se creează „x (3).zip”; scriere eșuată sau anulată → fișierul început e șters",
                      made.Success && Path.GetFileName(made.Path) == "x (3).zip" && File.ReadAllBytes(made.Path).SequenceEqual(new byte[] { 1, 2, 3 }) &&
                      File.ReadAllText(Path.Combine(dir, "x.zip")) == "vechi" && File.ReadAllText(Path.Combine(dir, "x (2).zip")) == "vechi 2" &&
                      failedWrite && !File.Exists(Path.Combine(dir, "y.zip")) && cancelled && !File.Exists(Path.Combine(dir, "z.zip")) &&
                      !ShelfFiles.CreateNewBeside(null, "x", ".zip", (s, t) => { }, CancellationToken.None).Success);

                // ---- the zip
                string doc = Path.Combine(dir, "raport.txt");
                File.WriteAllText(doc, "conținut ăîșț");
                File.WriteAllText(Path.Combine(dir, "raport.zip"), "un zip vechi");
                var z1 = ShelfArchive.Zip(doc, false, CancellationToken.None);
                string proj = Path.Combine(dir, "Proiect");
                Directory.CreateDirectory(Path.Combine(proj, "sub"));
                Directory.CreateDirectory(Path.Combine(proj, "gol"));
                File.WriteAllText(Path.Combine(proj, "a.txt"), "a");
                File.WriteAllText(Path.Combine(proj, "sub", "b.txt"), "bb");
                bool linked = false;
                try { File.CreateSymbolicLink(Path.Combine(proj, "link-afara"), Path.GetTempPath()); linked = true; } catch (Exception) { }
                var z2 = ShelfArchive.Zip(proj, true, CancellationToken.None);
                List<string> names2;
                using (var za = ZipFile.OpenRead(z2.Path)) names2 = za.Entries.Select(e => e.FullName).OrderBy(n => n, StringComparer.Ordinal).ToList();
                string zip1Text;
                using (var za = ZipFile.OpenRead(z1.Path)) using (var rd = new StreamReader(za.Entries.Single().Open())) zip1Text = rd.ReadToEnd();
                Check("SH15", "Zip: fișierul → „raport (2).zip” (cel vechi neatins), aceleași octeți; folderul → zip în folderul părinte, cu subfoldere și foldere goale; legăturile din folder nu sunt urmate",
                      z1.Success && Path.GetFileName(z1.Path) == "raport (2).zip" && zip1Text == "conținut ăîșț" && File.ReadAllText(Path.Combine(dir, "raport.zip")) == "un zip vechi" &&
                      z1.Files == 1 && z1.Message.StartsWith("Arhivă creată: raport (2).zip (un fișier)", StringComparison.Ordinal) &&
                      z2.Success && Path.GetDirectoryName(z2.Path) == dir && Path.GetFileName(z2.Path) == "Proiect.zip" && z2.Files == 2 &&
                      names2.SequenceEqual(new[] { "Proiect/a.txt", "Proiect/gol/", "Proiect/sub/b.txt" }) && (!linked || z2.Skipped == 1),
                      string.Join(",", names2) + " · link " + linked + " · skipped " + z2.Skipped);
                int zipsBefore = Directory.GetFiles(dir, "*.zip").Length;
                var tooMany = ShelfArchive.Zip(proj, true, CancellationToken.None, maxEntries: 1);
                var tooBig = ShelfArchive.Zip(proj, true, CancellationToken.None, maxBytes: 2);
                bool zipCancelled = false;
                using (var cts = new CancellationTokenSource())
                {
                    cts.Cancel();
                    try { ShelfArchive.Zip(proj, true, cts.Token); } catch (OperationCanceledException) { zipCancelled = true; }
                }
                Check("SH16", "Zip cu limite verificate înainte de scriere (prea multe fișiere, prea mare) și anulare: niciun zip nou; element lipsă și rădăcina unei unități refuzate",
                      !tooMany.Success && !tooBig.Success && zipCancelled && Directory.GetFiles(dir, "*.zip").Length == zipsBefore &&
                      !ShelfArchive.Zip(Path.Combine(dir, "lipsă.txt"), false, CancellationToken.None).Success && !ShelfArchive.Zip(Path.GetPathRoot(dir), true, CancellationToken.None).Success &&
                      ShelfArchive.MaxEntries == 10_000 && ShelfArchive.MaxBytes == 4L << 30);
            }
            finally { try { Directory.Delete(dir, true); } catch (IOException) { } }

            // ---- the actions through the registry
            var hfs = new FakeShelfFs().File(@"C:\p\doc.pdf").File(@"C:\p\poza.png").File(@"C:\p\scan.jpg").Folder(@"C:\Proiect").File(@"C:\p\gone.txt");
            var host = new FakeShelfHost { Files = hfs };
            var logs = new List<string>();
            var reg = new ActionRegistry(new FeatureFlags(onStore), new InlineUiDispatcher(), l => logs.Add(l));
            ShelfActions.Register(reg, host);
            var all = ShelfActions.AllIds.Select(reg.Get).ToList();
            Check("SH17", "8 acțiuni „shelf.*” înregistrate: id-uri unice zonă.verb, titlu și alias-uri în română și engleză, categoria Raft, iconiță, Safe, FeatureId „shelf”; parametrul „element” (text scurt) la cele pe un element, „elemente” (opțional) la copierea fișierelor; UI doar unde trebuie; API-ul local nu",
                  all.All(a => a != null) && ShelfActions.AllIds.Distinct().Count() == 8 && all.All(a => Regex.IsMatch(a.Id, @"^shelf\.[a-z]+(-[a-z]+)*$") && a.Title.StartsWith("Raft: ", StringComparison.Ordinal) &&
                      a.Aliases.Count >= 2 && a.Category == "Raft" && a.Icon.Length > 0 && a.Safety == ActionSafety.Safe && a.FeatureId == "shelf" && (a.AllowedInvokers & ActionInvoker.LocalApi) == 0 &&
                      !string.IsNullOrEmpty(a.UnavailableMessage)) &&
                  all.Where(a => a.Id != ShelfActions.ClearId && a.Id != ShelfActions.CopyFilesId).All(a => a.Parameters.Count == 1 && a.Parameters[0].Name == "element" && a.Parameters[0].Kind == ParamKind.Text && a.Parameters[0].MaxLength == 16 && a.Parameters[0].Required) &&
                  reg.Get(ShelfActions.ClearId).Parameters.Count == 0 &&
                  reg.Get(ShelfActions.CopyFilesId).Parameters.Count == 1 && reg.Get(ShelfActions.CopyFilesId).Parameters[0].Name == "elemente" &&
                  !reg.Get(ShelfActions.CopyFilesId).Parameters[0].Required && reg.Get(ShelfActions.CopyFilesId).Parameters[0].MaxLength == ShelfActions.ElementsMaxLength &&
                  new[] { ShelfActions.CopyPathId, ShelfActions.OpenFolderId, ShelfActions.OcrId, ShelfActions.CopyFilesId }.All(i => reg.Get(i).RequiresUiThread) &&
                  new[] { ShelfActions.ZipId, ShelfActions.ConvertId, ShelfActions.RemoveId, ShelfActions.ClearId }.All(i => !reg.Get(i).RequiresUiThread) &&
                  reg.Get(ShelfActions.ZipId).Timeout == TimeSpan.FromMinutes(10) && reg.Get(ShelfActions.OcrId).Timeout == TimeSpan.FromSeconds(60));
            var emptyCopy = ShelfRun(reg, ShelfActions.CopyPathId, "1");
            foreach (var p in new[] { @"C:\p\doc.pdf", @"C:\p\poza.png", @"C:\p\scan.jpg", @"C:\Proiect", @"C:\p\gone.txt" })
                host.Model.TryAdd(p, p == @"C:\Proiect");
            hfs.Entries.Remove(@"C:\p\gone.txt");                                     // deleted after it was added
            var regOff = new ActionRegistry(new FeatureFlags(AppSettings.NewFeatures()), new InlineUiDispatcher());
            ShelfActions.Register(regOff, host);
            var regSafe = new ActionRegistry(new FeatureFlags(onStore, safeMode: true), new InlineUiDispatcher());
            ShelfActions.Register(regSafe, host);
            Check("SH18", "Comutator oprit sau mod sigur → refuzate; raft gol → indisponibile („Raftul e gol.”); API-ul local refuzat; element lipsă, poziție 0 / 21, gunoi sau prea lung → refuzat, fără excepție",
                  !ShelfRun(regOff, ShelfActions.CopyPathId, "1").Success && !ShelfRun(regSafe, ShelfActions.ClearId).Success && !emptyCopy.Success && emptyCopy.Message == "Raftul e gol." &&
                  !ShelfRun(reg, ShelfActions.CopyPathId, "1", ActionInvoker.LocalApi).Success && !ShelfRun(reg, ShelfActions.CopyPathId).Success &&
                  ShelfRun(reg, ShelfActions.CopyPathId, "0").Message == "Elementul nu mai e în raft." && !ShelfRun(reg, ShelfActions.CopyPathId, "21").Success &&
                  !ShelfRun(reg, ShelfActions.CopyPathId, "gunoi").Success && !ShelfRun(reg, ShelfActions.CopyPathId, new string('1', 17)).Success && host.Written.Count == 0 && host.Model.Count == 5);
            var copy = ShelfRun(reg, ShelfActions.CopyPathId, "1");
            string imgId = host.Model.Items[1].Id;
            var copyById = ShelfRun(reg, ShelfActions.CopyPathId, imgId);
            host.Busy = true; var busy = ShelfRun(reg, ShelfActions.CopyPathId, "1"); host.Busy = false;
            var folderOfFile = ShelfRun(reg, ShelfActions.OpenFolderId, "1");
            var folderOfFolder = ShelfRun(reg, ShelfActions.OpenFolderId, "4");
            int changedBefore = host.ChangedCount;
            var gone = ShelfRun(reg, ShelfActions.CopyPathId, "5");
            Check("SH19", "Copiază calea → exact calea în clipboard (după poziție sau id); clipboard ocupat → mesaj; deschide folderul: al fișierului (nu fișierul), folderul însuși; dispărut de pe disc → scos din raft, cu mesaj",
                  copy.Success && copyById.Success && host.Written.SequenceEqual(new[] { @"C:\p\doc.pdf", @"C:\p\poza.png" }) && !busy.Success && busy.Message.Contains("Clipboard") &&
                  folderOfFile.Success && folderOfFolder.Success && host.Folders.SequenceEqual(new[] { @"C:\p", @"C:\Proiect" }) &&
                  !gone.Success && gone.Message.Contains("l-am scos din raft") && host.Model.Count == 4 && host.ChangedCount == changedBefore + 1);
            var ocrImg = ShelfRun(reg, ShelfActions.OcrId, "2");
            var ocrPdf = ShelfRun(reg, ShelfActions.OcrId, "1");
            host.OcrText = null; var ocrNoEngine = ShelfRun(reg, ShelfActions.OcrId, "2");
            host.OcrText = "  "; var ocrEmpty = ShelfRun(reg, ShelfActions.OcrId, "2");
            host.OcrText = "x";
            int writtenBefore = host.Written.Count;
            var toJpg = ShelfRun(reg, ShelfActions.ConvertId, "2");
            var toPng = ShelfRun(reg, ShelfActions.ConvertId, "3");
            var convPdf = ShelfRun(reg, ShelfActions.ConvertId, "1");
            host.ConvertError = "Fișierul nu e un PNG valid."; var convBad = ShelfRun(reg, ShelfActions.ConvertId, "2"); host.ConvertError = null;
            Check("SH20", "OCR doar pe imagini → textul în clipboard (fără OCR în Windows / fără text → mesaj); conversie PNG → JPG și JPG → PNG prin gazdă, rezultatul adăugat în raft; PDF refuzat; eroarea imaginii → mesajul ei fix",
                  ocrImg.Success && ocrImg.Message == "Text copiat · 2 rânduri" && host.Ocr.First() == @"C:\p\poza.png" && host.Written.Contains("linia unu\nlinia doi") &&
                  !ocrPdf.Success && ocrPdf.Message.Contains("nu e o imagine") && !ocrNoEngine.Success && ocrNoEngine.Message.Contains("recunoaștere de text") && !ocrEmpty.Success &&
                  toJpg.Success && toPng.Success && host.Converts.Take(2).SequenceEqual(new[] { (@"C:\p\poza.png", ShelfImageFormat.Jpeg), (@"C:\p\scan.jpg", ShelfImageFormat.Png) }) &&
                  toJpg.Message.StartsWith("JPG creat: poza.jpg", StringComparison.Ordinal) && toJpg.Message.EndsWith("adăugat în raft", StringComparison.Ordinal) &&
                  host.Model.Items.Any(i => i.Path == @"C:\p\poza.jpg") && host.Model.Items.Any(i => i.Path == @"C:\p\scan.png") &&
                  !convPdf.Success && convPdf.Message.Contains("PNG și JPG") && host.Converts.Count == 3 && !convBad.Success && convBad.Message == "Fișierul nu e un PNG valid." &&
                  host.Written.Count == writtenBefore);
            int n0 = host.Model.Count;
            var zipMissing = ShelfRun(reg, ShelfActions.ZipId, "1");             // the fake disk has it, the real one doesn't: a message, never an exception
            var remove = ShelfRun(reg, ShelfActions.RemoveId, "1");
            var removeAgain = ShelfRun(reg, ShelfActions.RemoveId, remove.Success ? ShelfPaths.IdOf(@"C:\p\doc.pdf") : "1");
            var clear = ShelfRun(reg, ShelfActions.ClearId);
            var clearEmpty = ShelfRun(reg, ShelfActions.ClearId);
            Check("SH21", "Zip prin registru: un element care nu e pe disc → mesaj, nu excepție; scoate un element (fișierul rămâne), a doua oară → „nu mai e în raft”; golește → „Raft golit (N elemente)”, apoi indisponibil",
                  !zipMissing.Success && zipMissing.Message.Length > 0 && remove.Success && !removeAgain.Success && host.Model.Count == 0 && clear.Success &&
                  clear.Message == "Raft golit (" + (n0 - 1) + " elemente)" && !clearEmpty.Success && hfs.Entries.ContainsKey(@"C:\p\doc.pdf"));
            Check("SH22", "Log-ul registrului: doar id-ul și rezultatul; nicio cale, niciun nume de fișier, niciun parametru",
                  logs.Count >= 20 && logs.All(l => Regex.IsMatch(l, @"^Acțiune shelf\.[a-z-]+ \((UI|LocalApi)\): (reușită|eșuată)$")) &&
                  logs.All(l => !l.Contains("doc") && !l.Contains("poza") && !l.Contains(@"C:\") && !l.Contains(imgId)),
                  string.Join(" | ", logs.Where(l => !Regex.IsMatch(l, @"^Acțiune shelf\.[a-z-]+ \((UI|LocalApi)\): (reușită|eșuată)$"))));

            // ---- P23.2: the ticks and "copiază fișierele" (the files on the clipboard, Ctrl+V does the copying)
            var cfs = new FakeShelfFs().File(@"C:\p\a.txt").File(@"C:\p\b.txt").Folder(@"C:\Proiect").File(@"E:\stick\c.txt").File(@"C:\p\d.txt");
            var chost = new FakeShelfHost { Files = cfs };
            foreach (var x in new[] { @"C:\p\a.txt", @"C:\p\b.txt", @"C:\Proiect", @"E:\stick\c.txt", @"C:\p\d.txt" })
                chost.Model.TryAdd(x, x == @"C:\Proiect");
            var items = chost.Model.Items;
            string idB = items[1].Id, idD = items[4].Id;
            var sel = new ShelfSelection();
            bool onB = sel.Toggle(idB), onD = sel.Toggle(idD), offB = sel.Toggle(idB);
            Check("SH35", "Alegerea rândurilor: bifa pune / scoate, nimic bifat = tot raftul, ordinea raftului se păstrează, cheile sunt id-uri; elementele scoase din raft sunt uitate",
                  onB && onD && !offB && sel.Count == 1 && sel.Has(idD) && !sel.Has(idB) &&
                  sel.Chosen(items).Select(x => x.Path).SequenceEqual(new[] { @"C:\p\d.txt" }) && sel.Keys(items) == idD &&
                  sel.Toggle(idB) && sel.Chosen(items).Select(x => x.Path).SequenceEqual(new[] { @"C:\p\b.txt", @"C:\p\d.txt" }) &&
                  sel.Keys(items) == idB + "," + idD &&
                  new ShelfSelection().Chosen(items).Count == 5 && new ShelfSelection().Keys(items) == null &&
                  sel.Prune(items.Where(x => x.Id != idD).ToList()) && sel.Count == 1 && sel.Has(idB) && !sel.Prune(items));
            var many = chost.Model.FindMany(idB + "," + idD);
            Check("SH36", "„elemente”: id-uri, poziții sau amândouă, separate prin virgulă / spațiu; gol, „tot” sau „all” = tot raftul; dubluri și chei inexistente ignorate; mereu în ordinea raftului",
                  many.Select(x => x.Path).SequenceEqual(new[] { @"C:\p\b.txt", @"C:\p\d.txt" }) &&
                  chost.Model.FindMany("5, 2").Select(x => x.Path).SequenceEqual(new[] { @"C:\p\b.txt", @"C:\p\d.txt" }) &&
                  chost.Model.FindMany("2 2 " + idB).Count == 1 && chost.Model.FindMany("1," + idB + ",gunoi,0,21").Count == 2 &&
                  chost.Model.FindMany(null).Count == 5 && chost.Model.FindMany("  ").Count == 5 && chost.Model.FindMany("tot").Count == 5 &&
                  chost.Model.FindMany("All").Count == 5 && chost.Model.FindMany("gunoi").Count == 0 &&
                  new ShelfModel().FindMany(null).Count == 0);
            var creg = new ActionRegistry(new FeatureFlags(onStore), new InlineUiDispatcher());
            ShelfActions.Register(creg, chost);
            var copyAll = ShelfCopyFiles(creg);
            var copySome = ShelfCopyFiles(creg, idB + "," + idD);
            chost.Busy = true; var copyBusy = ShelfCopyFiles(creg, idB); chost.Busy = false;
            int afterBusy = chost.Copied.Count;                                      // nothing went on the clipboard while it was busy
            cfs.Entries.Remove(@"C:\p\d.txt");                                     // deleted after it was added
            int changed0 = chost.ChangedCount;
            var copyGone = ShelfCopyFiles(creg, idB + "," + idD);
            cfs.Drives[@"E:\"] = ShelfDrive.Missing;                                // the stick is unplugged: its item stays
            var copyStick = ShelfCopyFiles(creg, "3," + ShelfPaths.IdOf(@"E:\stick\c.txt"));
            var copyNothing = ShelfCopyFiles(creg, ShelfPaths.IdOf(@"E:\stick\c.txt"));
            Check("SH37", "„Copiază fișierele”: căile pe clipboard (tot raftul sau doar cele alese, în ordine, foldere incluse); clipboard ocupat → mesaj; element șters de pe disc → scos din raft și sărit; unitate deconectată → rămâne în raft, mesaj, nimic copiat",
                  copyAll.Success && chost.Copied[0].SequenceEqual(new[] { @"C:\p\a.txt", @"C:\p\b.txt", @"C:\Proiect", @"E:\stick\c.txt", @"C:\p\d.txt" }) &&
                  copyAll.Message == "5 fișiere pe clipboard · dă Ctrl+V unde le vrei" &&
                  copySome.Success && chost.Copied[1].SequenceEqual(new[] { @"C:\p\b.txt", @"C:\p\d.txt" }) &&
                  !copyBusy.Success && copyBusy.Message.Contains("Clipboard") && afterBusy == 2 &&
                  copyGone.Success && chost.Copied[2].SequenceEqual(new[] { @"C:\p\b.txt" }) && copyGone.Message.EndsWith("(unul sărit)", StringComparison.Ordinal) &&
                  chost.Model.Count == 4 && chost.ChangedCount == changed0 + 1 &&
                  copyStick.Success && chost.Copied[3].SequenceEqual(new[] { @"C:\Proiect" }) && chost.Model.Count == 4 &&
                  !copyNothing.Success && copyNothing.Message.Contains("nu e conectată") && chost.Copied.Count == 4 &&
                  ShelfActions.CopiedMessage(1, 0) == "Un fișier pe clipboard · dă Ctrl+V unde îl vrei" && ShelfActions.CopiedMessage(2, 2) == "2 fișiere pe clipboard · dă Ctrl+V unde le vrei (2 sărite)");
            var cregOff = new ActionRegistry(new FeatureFlags(AppSettings.NewFeatures()), new InlineUiDispatcher());
            ShelfActions.Register(cregOff, chost);
            var emptyHost = new FakeShelfHost { Files = cfs };
            var ereg = new ActionRegistry(new FeatureFlags(onStore), new InlineUiDispatcher());
            ShelfActions.Register(ereg, emptyHost);
            Check("SH38", "„Copiază fișierele”: comutator oprit → refuzată; raft gol → indisponibilă; API-ul local refuzat; chei care nu există → „Raftul e gol.”, fără excepție; parametru prea lung refuzat",
                  !ShelfCopyFiles(cregOff).Success && !ShelfCopyFiles(ereg).Success && ShelfCopyFiles(ereg).Message == "Raftul e gol." &&
                  !ShelfCopyFiles(creg, idB, ActionInvoker.LocalApi).Success && !ShelfCopyFiles(creg, "gunoi").Success &&
                  ShelfCopyFiles(creg, "gunoi").Message == "Raftul e gol." && !ShelfCopyFiles(creg, new string('1', ShelfActions.ElementsMaxLength + 1)).Success &&
                  emptyHost.Copied.Count == 0);

            // ---- the drop's report, the settings, the hover while dragging
            var rep = new ShelfAddReport();
            foreach (var x in new[] { ShelfRefusal.None, ShelfRefusal.None, ShelfRefusal.Duplicate, ShelfRefusal.Network, ShelfRefusal.Missing, ShelfRefusal.UnsafeShortcut, ShelfRefusal.Full }) rep.Count(x);
            rep.Ignored = 2;
            Check("SH23", "Raportul unui drop: contoare; mesaj în română fără nume de fișiere; rândul de log doar cu numere",
                  rep.Added == 2 && rep.Duplicates == 1 && rep.Network == 1 && rep.Refused == 3 && rep.Full == 1 &&
                  rep.Message() == "2 elemente adăugate; unul era deja în raft; unul e din rețea (refuzat); 2 nu sunt fișiere locale valide; raftul e plin (maximum 20): 3 n-au mai încăput." &&
                  rep.LogLine() == "Raft: adăugate 2, refuzate 3 (din rețea 1), dubluri 1, peste limită 3." && Regex.IsMatch(rep.LogLine(), @"^Raft: [a-zăîâșț0-9 ,()]+\.$") &&
                  new ShelfAddReport { Added = 1 }.Message() == "Un element adăugat." && new ShelfAddReport().Message() == "Nimic de adăugat." &&
                  new ShelfAddReport { Added = 1 }.LogLine().StartsWith("Raft: adăugate 1, refuzate 0", StringComparison.Ordinal));
            var fresh = new AppSettings();
            var old = JsonSerializer.Deserialize<AppSettings>("{\"DwellMs\":300}");
            var withShelf = JsonSerializer.Deserialize<AppSettings>("{\"Shelf\":[\"C:\\\\a.txt\",\"\\\\\\\\srv\\\\x\",\"C:\\\\A.TXT\",\"D:\\\\Proiect\\\\\"]}");
            withShelf.NormalizeShelf();
            var nul = JsonSerializer.Deserialize<AppSettings>("{\"Shelf\":null}");
            nul.NormalizeShelf();
            string appSettings = Norm(NoComments(Src("AppSettings.cs")));
            Check("SH24", "Setarea „Shelf” (cheia veche, nefolosită, preluată): goală implicit și pentru setări vechi; la citire rețea și dubluri scoase, folderul păstrat; null → gol; salvată cu restul (scriere atomică .tmp + mutare)",
                  fresh.Shelf != null && fresh.Shelf.Count == 0 && old.Shelf.Count == 0 && withShelf.Shelf.SequenceEqual(new[] { @"C:\a.txt", @"D:\Proiect\" }) && nul.Shelf != null && nul.Shelf.Count == 0 &&
                  appSettings.Contains("s.NormalizeShelf();") && !appSettings.Contains("public List<string> Shelf") &&
                  appSettings.Contains("File.WriteAllText(tmp, json); File.Move(tmp, FilePath, true);"));
            var hov = new ShelfDragHover();
            bool click = hov.Update(false, false) | hov.Update(true, true);                       // pressed on the pill: a click for the window below
            hov.Update(true, false);
            bool carried = !hov.Update(false, true) & hov.Update(true, true) & hov.CarriedIn;     // pressed outside, then moved onto the pill
            bool released = !hov.Update(true, false) & !hov.CarriedIn;
            hov.Update(false, true); hov.Reset();
            bool reset = !hov.Update(true, true);
            Check("SH25", "Hover în timpul unei tragere: butonul apăsat pe pastilă rămâne click spre fereastra de dedesubt; apăsat în afară și adus pe pastilă = tragere (poate deschide); eliberat sau Reset → gata",
                  !click && carried && released && reset);

            // ---- the smoke test command
            var sc = SmokeMode.Parse(@"smoke-shelf-add C:\Users\RUNNER~1\AppData\Local\Temp\Raft Test ăș.TXT");
            Check("SH26", "Comanda de fum „smoke-shelf-add <cale>”: calea păstrată exact (majuscule, spații, diacritice); fără cale, prea lungă sau cu caractere de control → ignorată",
                  sc is { Kind: SmokeCommandKind.ShelfAdd } && sc.Argument == @"C:\Users\RUNNER~1\AppData\Local\Temp\Raft Test ăș.TXT" &&
                  SmokeMode.Parse("  SMOKE-SHELF-ADD   D:\\A.txt  ") is { Kind: SmokeCommandKind.ShelfAdd, Argument: "D:\\A.txt" } &&
                  new[] { "smoke-shelf-add", "smoke-shelf-add   ", "smoke-shelf-addC:\\x", "smoke-shelf-add C:\\" + new string('a', 190), "smoke-shelf-add C:\\a\u0001b" }.All(l => SmokeMode.Parse(l) == null) &&
                  SmokeMode.ShelfItemPrefix == "shelf-item-" && SmokeMode.ShelfCopyPrefix == "shelf-copy-" && SmokeMode.ShelfToggleAutomationId == "shelf-toggle" && SmokeMode.MaxShelfPath == 180);

            ShelfR1Tests();
            ShelfSourcePins();
        }

        /// <summary>The WPF side (compiled by CI on Windows): small hooks in the big files, the protocol of the switch, no polling, no paths in the log.</summary>
        static void ShelfSourcePins()
        {
            const string Part = "Features/Shelf/NotchWindow.Shelf.cs", Images = "Features/Shelf/ShelfImages.cs";
            string notch0 = Src("NotchWindow.xaml.cs"), notch = Norm(NoComments(notch0)), part = Src(Part), partN = Norm(NoComments(part));
            string expand = Norm(NoComments(MethodBody(notch0, "private void Expand()"))), collapse = Norm(NoComments(MethodBody(notch0, "private void Collapse()")));
            string pages = Norm(NoComments(Src("NotchWindow.Pages.cs"))), app = Norm(NoComments(Src("App.xaml.cs")));
            Check("SH27", "Legăturile: PollTick (1 rând + condiția click-ului), la finalul Expand, în Collapse, StopShelf în Cleanup; PanelH și UpdateHeader în Pages; App: înregistrarea și pornirea după Smart Clipboard; WidgetPage moștenește din nou AllowDrop",
                  Count(notch, "Shelf") == 4 && notch.Contains("bool shelfDrag = ShelfDragHover(ins) || AlertInterruptDragging; if (ins && !shelfDrag && (Native.GetAsyncKeyState(0x01) < 0 || Native.GetAsyncKeyState(0x02) < 0)) _clickedThrough = true;") /* P51b: the same detector, fed through the alert as well */ &&
                  expand.EndsWith("ApplyHidden(); UpdateVisualizer(); ShelfOnOpen(); NotchGuardOpened(); }", StringComparison.Ordinal) /* B1: the safety net's hook after it */ && collapse.Contains("_leaveStart = null; ShelfOnClose(); StopTyping();") &&
                  Norm(NoComments(MethodBody(notch0, "public void Cleanup()"))).Contains("StopQuickActions(); StopSmartClipboard(); StopShelf();") &&
                  Count(pages, "Shelf") == 2 && Norm(NoComments(MethodBody(Src("NotchWindow.Pages.cs"), "private double PanelH()"))).Contains("h = Math.Max(h, ShelfPanelHeight());") &&
                  Norm(NoComments(MethodBody(Src("NotchWindow.Pages.cs"), "private void UpdateHeader()"))).EndsWith("ShelfHeaderChanged(); QuickActionsHeaderChanged(); }", StringComparison.Ordinal) &&
                  app.Contains("_notch.StartSmartClipboard(); _notch.StartShelf();") && app.Contains("Features.Shelf.ShelfActions.Register(registry, new Features.Shelf.NotchShelfHost(_notch));") &&
                  Norm(NoComments(Src("Panes/WidgetPage.cs"))).Contains("if (value) AllowDrop = true; else ClearValue(AllowDropProperty);"));
            Check("SH28", "Protocolul comutatorului: Changed += / -=, UI prin Dispatcher, IsEnabled citit în handler; oprit → fără AllowDrop, fără handler-e, fără buton; erorile → ReportError(\"shelf\")",
                  partN.Contains("FeatureFlags.Current.Changed += _shFlagHandler;") && partN.Contains("FeatureFlags.Current.Changed -= _shFlagHandler;") &&
                  partN.Contains("Dispatcher.InvokeAsync(ApplyShelfSwitch)") &&
                  Norm(NoComments(MethodBody(part, "private void ApplyShelfSwitch()"))).Contains("bool on = ShelfEnabled(); if (on == _shOn) return; _shOn = on;") &&
                  Norm(NoComments(MethodBody(part, "private void ShelfOff()"))).StartsWith("{ ShelfWireDrop(false); ShelfHidePanel();", StringComparison.Ordinal) &&
                  Count(partN, "Pill.AllowDrop = true;") == 1 && partN.Contains("Pill.ClearValue(AllowDropProperty);") && Count(partN, "AllowDrop") == 2 &&
                  Count(partN, "Pill.AddHandler(") == 6 && Count(partN, "Pill.RemoveHandler(") == 6 &&
                  Norm(NoComments(MethodBody(part, "private bool ShelfDragHover(bool inside)"))).StartsWith("{ if (!_shOn) return false;", StringComparison.Ordinal) &&
                  Norm(NoComments(MethodBody(part, "private void ShelfOnOpen()"))).StartsWith("{ if (!_shOn) return;", StringComparison.Ordinal) &&
                  Count(partN, "FeatureFlags.Current?.ReportError(ShelfActions.FeatureId, ex);") >= 5 && !partN.Contains("async void"));
            Check("SH29", "Drop doar pe notch-ul deschis (fără editare, fără drag-ul nostru), efect Link/Copy, niciodată Move; tragerea în afară: FileDrop cu căile locale (rândurile bifate merg împreună), doar cele care nu s-au găsit lipsă, Copy|Link; clipboard-ul cu fișiere: FileDrop + „Preferred DropEffect” copiere, niciodată mutare; verificările în fundal (Task.Run)",
                  Norm(NoComments(MethodBody(part, "private bool ShelfAccepts(DragEventArgs e)"))).Contains("if (!_shOn || _mode != Mode.Expanded || Editing || e?.Data == null) return false;") &&
                  partN.Contains("!e.Data.GetDataPresent(ShelfDragFormat)") && !Regex.IsMatch(partN, @"DragDropEffects\.Move|DragDropEffects\.All") &&
                  Count(partN, "data.SetData(DataFormats.FileDrop, paths.ToArray());") == 2 && partN.Contains("DragDrop.DoDragDrop(row, data, DragDropEffects.Copy | DragDropEffects.Link);") &&
                  partN.Contains("var dragged = _shSelection.Count > 1 && _shSelection.Has(it.Id) ? _shSelection.Chosen(_shModel.Items) : new[] { it };") &&
                  partN.Contains("if (x.Exists != false && ShelfPaths.TryNormalize(x.Path, out var p, out _) == ShelfRefusal.None) paths.Add(p);") &&
                  partN.Contains("data.SetData(\"Preferred DropEffect\", new System.IO.MemoryStream(BitConverter.GetBytes((int)(DragDropEffects.Copy | DragDropEffects.Link))));") &&
                  partN.Contains("Clipboard.SetDataObject(data, true);") && Count(partN, "_ignoreClip = true;") == 2 && Count(partN, "_ignoreClip = false;") == 2 &&
                  Norm(NoComments(MethodBody(part, "private async Task ShelfAddPathsAsync(string[] raw)"))).Contains("await Task.Run(") &&
                  Norm(NoComments(MethodBody(part, "private async Task ShelfPruneAsync()"))).Contains("await Task.Run(") &&
                  partN.Contains("PreviewDragEnterEvent") && partN.Contains("PreviewDropEvent"));
            var files = Directory.EnumerateFiles(Path.Combine(RepoRoot(), "Features", "Shelf"), "*.cs").Select(f => "Features/Shelf/" + Path.GetFileName(f)).OrderBy(f => f).ToList();
            var forbidden = new[] { "new Timer", "Thread.Sleep", "Task.Delay", "GetForegroundWindow", "SetWinEventHook", "Process.Start", "ProcessStartInfo", "SHGetFileInfo", "FileIcon(",
                                    "ExtractIcon", "IShellLink", "FileMode.Create,", "FileMode.Create)", "FileMode.OpenOrCreate", "FileMode.Truncate", "File.Copy(", "File.Move(", "File.WriteAll",
                                    "Clipboard.GetText", "Directory.Delete", "ShowLive(", "Alert(" };
            var found = files.SelectMany(f => forbidden.Where(x => NoComments(Src(f)).Contains(x)).Select(x => f + ": " + x)).ToList();
            string filesN = Norm(NoComments(Src("Features/Shelf/ShelfFiles.cs")));
            Check("SH30", "Fără polling (timer, Sleep, Delay), fără iconița Shell sau rezolvarea scurtăturilor, fără suprascriere (doar FileMode.CreateNew), fără copiere / mutare / ștergere de fișiere ale tale; un singur DispatcherTimer, o dată (mesajul); Shell.Open doar pe folder",
                  files.Count == 7 && found.Count == 0 && Count(filesN, "FileMode.CreateNew") == 1 && Count(partN, "new DispatcherTimer") == 1 &&
                  partN.Contains("_shMessageTimer.Tick += (o, e) => { _shMessageTimer.Stop();") && files.Where(f => f != Part).All(f => !NoComments(Src(f)).Contains("DispatcherTimer")) &&
                  Count(partN, "Services.Shell.Open(") == 1 && partN.Contains("Services.Shell.Open(p.TrimEnd('\\\\') + \"\\\\\");") &&
                  Count(filesN, "File.Delete(path)") == 1 && Norm(NoComments(Src(Images))).Contains("BitmapCacheOption.None") && Norm(NoComments(Src(Images))).Contains("SetApartmentState(ApartmentState.STA)"),
                  string.Join(" | ", found) + " · " + string.Join(",", files));
            var logCalls = files.SelectMany(f => Src(f).Split('\n').Where(l => l.Contains("App.Log(", StringComparison.Ordinal)).Select(l => l.Trim())).ToList();
            string smokeSide = Norm(NoComments(MethodBody(Src("Features/Smoke/NotchWindow.Smoke.cs"), "private void SmokeShelfAdd(string path)")));
            Check("SH31", "În log, din Features/Shelf, doar texte fixe și contoare: nicio cale, niciun nume de fișier, niciun mesaj de excepție; comanda de fum nu scrie calea",
                  logCalls.Count == 4 && logCalls.Any(l => l.Contains("App.Log(\"Raft: pornit (\" + _shModel.Count + \" elemente).\");")) && logCalls.Any(l => l.Contains("App.Log(\"Raft: oprit.\");")) &&
                  logCalls.Any(l => l.Contains("App.Log(report.LogLine());")) && logCalls.Any(l => l.Contains("App.Log(\"Raft: \" + n + \" elemente care nu mai există, scoase.\");")) &&
                  logCalls.All(l => !Regex.IsMatch(l, @"\b(path|Path|Name|raw|file|files|ex)\b|Message\(")) &&
                  smokeSide.StartsWith("{ if (!SmokeMode.On) return; if (!_shOn) { App.Log(\"Test de fum: raftul e oprit; comanda e ignorată.\"); return; } App.Log(\"Test de fum: un fișier pentru raft (aceleași verificări ca la tragere).\"); _ = ShelfAddPathsAsync(new[] { path }); }", StringComparison.Ordinal),
                  string.Join(" | ", logCalls));
            Check("SH32", "Fără culori scrise în cod: pensulele temei (SetResourceReference), stilurile GhostPill / AccentPill / IconButton; iconițe din fontul aplicației; id-uri UI Automation stabile",
                  !Regex.IsMatch(part, @"Color\.From|#[0-9A-Fa-f]{6}|new SolidColorBrush|Brushes\.|Ui\.B\(") && Count(partN, "SetResourceReference(") >= 8 &&
                  partN.Contains("Ui.S(\"GhostPill\")") && partN.Contains("Ui.S(\"IconButton\")") && partN.Contains("SetResourceReference(TextBlock.FontFamilyProperty, \"IconFont\")") &&
                  partN.Contains("SmokeMode.ShelfItemPrefix + it.Id") && partN.Contains("SmokeMode.ShelfCopyPrefix") && partN.Contains("SmokeMode.ShelfToggleAutomationId") &&
                  partN.Contains("SmokeMode.ShelfClearAutomationId") && partN.Contains("SmokeMode.ShelfMessageAutomationId") && partN.Contains("CornerRadius = new CornerRadius(16)"));
            Check("SH33", "Butoanele pornesc acțiunile doar prin ActionRegistry.Current.InvokeAsync (UI, fără confirmare, fără ExecuteAsync); același click de două ori cât rulează → o dată",
                  Regex.Matches(partN, @"(?<!Dispatcher)\.InvokeAsync\(").Count == 1 && partN.Contains("var r = await reg.InvokeAsync(actionId, args, ActionInvoker.UI);") &&
                  !partN.Contains("ExecuteAsync(") && !partN.Contains("confirmed") && partN.Contains("if (!_shRunning.Add(key)) return;") && Count(partN, "_ = ShelfRunAsync(") == 3 &&
                  partN.Contains("_ = ShelfRunAsync(ShelfActions.CopyFilesId, _shSelection.Keys(_shModel.Items));") &&
                  partN.Contains("string param = actionId == ShelfActions.CopyFilesId ? ShelfActions.ElementsParam : ShelfActions.ElementParam;"));

            // ---- the smoke test
            string sp = SmokeSrc("SmokeShelf.cs");
            string body = MethodBody(sp, "private static void Shelf()") ?? "";
            Check("SH34", "Testul de fum P23 rulează o singură dată (doar cu activity-manager oprit) și o spune; fișier local creat de test, „smoke-shelf-add”, elementul prin UI Automation, „Copiază calea” → exact calea în clipboard, „Golește” → gol, fișierul rămâne, nimic în log; repune starea",
                  sp.Contains("if (!_activityOn) Run(step = \"Raft") && sp.Contains("SKIP  Raft") && Src("tests/WinNotch.Smoke/WinNotch.Smoke.csproj").Contains("<Compile Include=\"SmokeShelf.cs\" />") &&
                  Count(body, "Command(\"toggle feature \" + ShelfFeature);") == 3 && body.Contains("Command(\"smoke-shelf-add \" + file);") && body.Contains("File.WriteAllText(file,") &&
                  body.Contains("toggle.AsButton().Invoke();") && body.Contains("copy.AsButton().Invoke();") && body.Contains("WaitFor(GetClipboardText, t => t == expected,") &&
                  body.Contains("ShelfButton(SmokeMode.ShelfClearAutomationId).AsButton().Invoke();") && body.Contains("WaitFor(() => ShelfItems().Count, n => n == 0,") &&
                  body.Contains("if (!File.Exists(file)) Fail(") && body.Contains("LogCount(ShelfMarker) > 0") && body.Contains("Directory.Delete(dir, true)") &&
                  body.Contains("\"Acțiune shelf.copy-path (UI): reușită\"") && body.Contains("\"Acțiune shelf.clear (UI): reușită\""));
        }
    }
}
