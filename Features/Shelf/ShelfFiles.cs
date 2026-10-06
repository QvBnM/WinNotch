using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;

namespace WinNotch.Features.Shelf
{
    /// <summary>The kind of drive a path is on, as the shelf needs it.</summary>
    public enum ShelfDrive { Local, Removable, Network, Missing }

    /// <summary>Link = the path itself or a folder on it is a symbolic link or a junction (R1: never followed, never on the shelf).</summary>
    public enum ShelfEntry { None, File, Folder, Link }

    /// <summary>
    /// The little disk access the checks need (the real one in the app, a fake in the tests). Every call may be slow (a
    /// sleeping USB stick): the shelf only calls it off the UI thread.
    /// </summary>
    public interface IShelfFileSystem
    {
        /// <summary>"C:\" → its kind; a letter mapped to a share is <see cref="ShelfDrive.Network"/> (asked before the path is touched).</summary>
        ShelfDrive DriveOf(string root);
        ShelfEntry Kind(string path);
        /// <summary>At most <paramref name="max"/> bytes from the start of a file; null when it can't be read.</summary>
        byte[] ReadHead(string path, int max);
        /// <summary>%VAR% expansion (for the environment blocks of a .lnk).</summary>
        string Expand(string text);
    }

    /// <summary>The real disk. No WPF, no Shell: DriveInfo, File/Directory.Exists and a FileStream.</summary>
    public sealed class LocalShelfFileSystem : IShelfFileSystem
    {
        public static readonly LocalShelfFileSystem Instance = new LocalShelfFileSystem();

        public ShelfDrive DriveOf(string root)
        {
            try
            {
                return new DriveInfo(root).DriveType switch
                {
                    DriveType.Fixed or DriveType.Ram => ShelfDrive.Local,
                    DriveType.Removable or DriveType.CDRom => ShelfDrive.Removable,
                    DriveType.Network => ShelfDrive.Network,
                    _ => ShelfDrive.Missing,
                };
            }
            catch (Exception ex) when (ex is ArgumentException || ex is IOException || ex is UnauthorizedAccessException) { return ShelfDrive.Missing; }
        }

        public ShelfEntry Kind(string path)
        {
            try
            {
                var kind = File.Exists(path) ? ShelfEntry.File : Directory.Exists(path) ? ShelfEntry.Folder : ShelfEntry.None;
                return kind != ShelfEntry.None && LinkOnPath(path) ? ShelfEntry.Link : kind;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException) { return ShelfEntry.None; }
        }

        /// <summary>
        /// R1: the path or one of its folders is a symbolic link or a junction (LinkTarget read, the link never followed): a
        /// local name could lead to \\server\share. Other reparse points (OneDrive's cloud files) have no LinkTarget: fine.
        /// </summary>
        public static bool LinkOnPath(string path)
        {
            for (string p = path; !string.IsNullOrEmpty(p); p = Path.GetDirectoryName(p))
            {
                FileSystemInfo info = Directory.Exists(p) ? new DirectoryInfo(p) : new FileInfo(p);
                if (!info.Exists) continue;
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0 && info.LinkTarget != null) return true;
            }
            return false;
        }

        public byte[] ReadHead(string path, int max)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var buf = new byte[(int)Math.Min(max, Math.Max(0, fs.Length))];
                int read = 0;
                while (read < buf.Length)
                {
                    int n = fs.Read(buf, read, buf.Length - read);
                    if (n <= 0) break;
                    read += n;
                }
                if (read < buf.Length) Array.Resize(ref buf, read);
                return buf;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException) { return null; }
        }

        public string Expand(string text) => Environment.ExpandEnvironmentVariables(text ?? "");
    }

    /// <summary>The result of a file the shelf made (a zip, a converted image): its path and a short Romanian sentence.</summary>
    public sealed class ShelfFileResult
    {
        public bool Success { get; init; }
        public string Path { get; init; }
        public string Message { get; init; } = "";
        public int Files { get; init; }
        public int Skipped { get; init; }

        public static ShelfFileResult Fail(string message) => new ShelfFileResult { Success = false, Message = message };
    }

    /// <summary>
    /// New files beside a shelf item, never over an existing one: each candidate name is created with FileMode.CreateNew
    /// (the system refuses it if it exists, so there's no check-then-write race); taken → the next name. If writing fails
    /// or is cancelled, the half-written file (ours: we just created it) is deleted.
    /// </summary>
    public static class ShelfFiles
    {
        public static ShelfFileResult CreateNewBeside(string folder, string baseName, string extension, Action<Stream, CancellationToken> write, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(folder) || write == null) return ShelfFileResult.Fail("Nu am unde scrie fișierul nou.");
            foreach (var name in ShelfNames.Candidates(baseName, extension))
            {
                ct.ThrowIfCancellationRequested();
                string path = Path.Combine(folder, name);
                FileStream fs;
                try { fs = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None); }
                catch (IOException) when (File.Exists(path) || Directory.Exists(path)) { continue; }      // taken: the next name
                bool done = false;
                try
                {
                    using (fs) write(fs, ct);
                    done = true;
                    return new ShelfFileResult { Success = true, Path = path };
                }
                finally
                {
                    if (!done) { try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
                }
            }
            return ShelfFileResult.Fail("Există deja prea multe fișiere cu numele acesta în folder.");
        }
    }

    /// <summary>
    /// "shelf.zip": a file or a folder into a new zip in the same folder ("x.zip", "x (2).zip"…), never over an existing
    /// file. Off the UI thread, cancellable, with limits (entries and bytes) checked before anything is written. Links and
    /// junctions inside a folder are skipped (no loops, nothing from outside the folder); unreadable files are skipped and
    /// counted.
    /// </summary>
    public static class ShelfArchive
    {
        public const int MaxEntries = 10_000;
        public const long MaxBytes = 4L << 30;

        private sealed class Entry { public string Full, Name; public bool Folder; }

        /// <param name="open">Opens a file to read (tests: one that fails); null = a FileStream that lets others read and write.</param>
        public static ShelfFileResult Zip(string source, bool isFolder, CancellationToken ct, int maxEntries = MaxEntries, long maxBytes = MaxBytes, Func<string, Stream> open = null)
        {
            // System.IO.Path, not ShelfPaths: the same code runs on the tests' temporary folders
            string folder = string.IsNullOrEmpty(source) ? null : Path.GetDirectoryName(source);
            string top = string.IsNullOrEmpty(source) ? "" : Path.GetFileName(source);
            if (string.IsNullOrEmpty(folder) || string.IsNullOrEmpty(top)) return ShelfFileResult.Fail("Nu arhivez o unitate întreagă.");
            bool exists = isFolder ? Directory.Exists(source) : File.Exists(source);
            if (!exists) return ShelfFileResult.Fail("Elementul nu mai există.");

            // what goes in, counted before the zip is created
            var entries = new List<Entry>();
            long total = 0;
            int skipped = 0;
            if (!isFolder)
            {
                total = new FileInfo(source).Length;
                entries.Add(new Entry { Full = source, Name = top });
            }
            else
            {
                var stack = new Stack<(string Dir, string Rel)>();
                stack.Push((source, top));
                while (stack.Count > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    var (dir, rel) = stack.Pop();
                    FileSystemInfo[] children;
                    try { children = new DirectoryInfo(dir).GetFileSystemInfos(); }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { skipped++; continue; }
                    if (children.Length == 0) entries.Add(new Entry { Name = rel + "/", Folder = true });
                    Array.Sort(children, (a, b) => string.CompareOrdinal(a.Name, b.Name));
                    foreach (var c in children)
                    {
                        if ((c.Attributes & FileAttributes.ReparsePoint) != 0) { skipped++; continue; }       // links, junctions: not followed
                        string name = rel + "/" + c.Name;
                        if ((c.Attributes & FileAttributes.Directory) != 0) { stack.Push((c.FullName, name)); continue; }
                        total += ((FileInfo)c).Length;
                        entries.Add(new Entry { Full = c.FullName, Name = name });
                        if (entries.Count > maxEntries) return ShelfFileResult.Fail("Folderul are peste " + maxEntries + " de fișiere; nu îl arhivez.");
                        if (total > maxBytes) return ShelfFileResult.Fail("Folderul e prea mare pentru o arhivă (peste " + (maxBytes >> 30) + " GB).");
                    }
                }
            }
            if (entries.Count > maxEntries) return ShelfFileResult.Fail("Prea multe fișiere; nu arhivez.");
            if (total > maxBytes) return ShelfFileResult.Fail("Fișierul e prea mare pentru o arhivă (peste " + (maxBytes >> 30) + " GB).");

            int files = 0;
            ShelfFileResult made;
            try
            {
                made = ShelfFiles.CreateNewBeside(folder, ShelfNames.ZipBase(top, isFolder), ".zip", (stream, token) =>
                {
                    long written = 0;
                    using var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
                    var buffer = new byte[81920];
                    foreach (var e in entries)
                    {
                        token.ThrowIfCancellationRequested();
                        if (e.Folder) { zip.CreateEntry(e.Name); continue; }
                        Stream input;
                        try { input = open != null ? open(e.Full) : new FileStream(e.Full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); }
                        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { skipped++; continue; }
                        using (input)
                        {
                            var entry = zip.CreateEntry(e.Name, CompressionLevel.Optimal);
                            try
                            {
                                var t = File.GetLastWriteTime(e.Full);
                                if (t.Year >= 1980 && t.Year <= 2107) entry.LastWriteTime = t;
                            }
                            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException) { }
                            using var output = entry.Open();
                            int n;
                            while ((n = input.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                token.ThrowIfCancellationRequested();
                                written += n;
                                if (written > maxBytes) throw new IOException("limit");            // a file grew meanwhile: stop, the zip is deleted
                                output.Write(buffer, 0, n);
                            }
                        }
                        files++;
                    }
                }, ct);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException)
            {
                return ShelfFileResult.Fail("Arhiva nu a putut fi scrisă (fișierul început a fost șters).");
            }
            if (!made.Success) return made;
            if (files == 0 && entries.Exists(x => !x.Folder))
            {
                // R1: nothing could be read (all locked): no empty zip passed off as done; it's ours, just created
                try { File.Delete(made.Path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                return ShelfFileResult.Fail("Niciun fișier nu a putut fi citit (sunt folosite de alte aplicații?); nu am făcut arhiva.");
            }
            return new ShelfFileResult
            {
                Success = true, Path = made.Path, Files = files, Skipped = skipped,
                Message = "Arhivă creată: " + Path.GetFileName(made.Path) + " (" + (files == 1 ? "un fișier" : files + " fișiere") + (skipped > 0 ? ", " + skipped + " sărite" : "") + ")",
            };
        }
    }
}
