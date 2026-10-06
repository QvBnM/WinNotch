using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WinNotch.Core.Actions;

namespace WinNotch.Features.Shelf
{
    /// <summary>What the "shelf.*" actions need from the app (the notch in the app, a fake in the tests).</summary>
    public interface IShelfHost
    {
        ShelfModel Model { get; }
        IShelfFileSystem Files { get; }
        /// <summary>UI thread: the text into the clipboard as WinNotch's own write; false when the clipboard is busy.</summary>
        bool SetText(string text);
        /// <summary>UI thread: opens a local folder (already checked) through Shell.Open; null when done, else a short Romanian reason.</summary>
        string OpenFolder(string folder);
        /// <summary>The text in a local image (Windows' own OCR, as Win+Alt+T); null = Windows has no OCR language; "" = no text.</summary>
        Task<string> RecognizeTextAsync(string imagePath, CancellationToken ct);
        /// <summary>A PNG as a new JPEG beside it, or a JPEG as a new PNG (never over an existing file); off the UI thread.</summary>
        Task<ShelfFileResult> ConvertImageAsync(string imagePath, ShelfImageFormat to, CancellationToken ct);
        /// <summary>The shelf changed (any thread): save it and redraw it, on the UI thread.</summary>
        void Changed();
    }

    /// <summary>
    /// P23 as actions ("shelf.*", ADR 0011). Every action on one item takes the parameter "element": its position as shown
    /// ("1"…"20") or its stable id (12 hex digits); never the path, so nothing of it reaches the registry's log. All Safe
    /// (nothing is ever overwritten or deleted on the disk: new files get new names, "remove" and "clear" only drop
    /// references), behind the "shelf" switch. Before acting on an item its path is checked again off the UI thread: gone
    /// (or no longer local) → it leaves the shelf quietly and the action says so.
    /// </summary>
    public static class ShelfActions
    {
        public const string FeatureId = "shelf";
        public const string Category = "Raft";
        public const string ElementParam = "element";

        public const string CopyPathId = "shelf.copy-path", OpenFolderId = "shelf.open-folder", ZipId = "shelf.zip", OcrId = "shelf.ocr",
            ConvertId = "shelf.convert-image", RemoveId = "shelf.remove", ClearId = "shelf.clear";

        public static readonly IReadOnlyList<string> AllIds = new[] { CopyPathId, OpenFolderId, ZipId, OcrId, ConvertId, RemoveId, ClearId };

        /// <summary>Segoe Fluent / MDL2 glyphs (the app's icon font).</summary>
        internal const string GShelf = "", GCopy = "", GFolder = "", GZip = "", GText = "", GConvert = "",
            GRemove = "", GClear = "", GFile = "", GFolderItem = "", GImage = "", GDocument = "";

        public static readonly TimeSpan ZipTimeout = TimeSpan.FromMinutes(10), ImageTimeout = TimeSpan.FromSeconds(60);

        private const string Busy = "Clipboard-ul e folosit de altă aplicație; încearcă din nou.";
        private const string Gone = "Elementul nu mai e în raft.";
        private const string Empty = "Raftul e gol.";

        public static string GlyphFor(ShelfItemKind k) => k switch
        {
            ShelfItemKind.Folder => GFolderItem, ShelfItemKind.Image => GImage, ShelfItemKind.Archive => GZip, ShelfItemKind.Document => GDocument, _ => GFile,
        };

        private static ActionParameter Element() => ActionParameter.Text(ElementParam, "Elementul din raft", 16);

        public static IReadOnlyList<ActionDescriptor> Create(IShelfHost host)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            bool Any() => host.Model.Count > 0;

            ActionDescriptor One(string id, string title, string icon, string[] aliases, bool ui, TimeSpan? timeout,
                                 Func<ShelfItem, string, bool, CancellationToken, Task<ActionResult>> run) =>
                new ActionDescriptor(id, title, async (args, ct) =>
                {
                    var item = host.Model.Find(args.GetText(ElementParam));
                    if (item == null) return ActionResult.Failed(Gone);
                    // checked again off the UI thread (a sleeping stick can take seconds); the UI thread comes back after
                    var (path, folder) = await Task.Run(() => Recheck(item, host.Files), ct);
                    if (path == null)
                    {
                        if (host.Model.Remove(item.Id)) host.Changed();
                        return ActionResult.Failed("Elementul nu mai există pe acest PC; l-am scos din raft.");
                    }
                    return await run(item, path, folder, ct);
                }, Any)
                {
                    Aliases = aliases, Category = Category, Icon = icon, FeatureId = FeatureId, RequiresUiThread = ui, Timeout = timeout,
                    Parameters = new[] { Element() }, UnavailableMessage = Empty,
                };

            return new[]
            {
                One(CopyPathId, "Raft: copiază calea elementului", GCopy, new[] { "copiază calea", "calea fișierului", "copy path" }, true, null,
                    (item, path, folder, ct) => Task.FromResult(host.SetText(path) ? ActionResult.Ok("Calea, în clipboard") : ActionResult.Failed(Busy))),
                One(OpenFolderId, "Raft: deschide folderul elementului", GFolder, new[] { "deschide folderul", "arată în explorer", "open folder", "show in explorer" }, true, null,
                    (item, path, folder, ct) =>
                    {
                        // a folder opens itself; a file only the folder it is in (never the file: that would run it)
                        string target = folder ? path : ShelfPaths.FolderOf(path);
                        if (target == null) return Task.FromResult(ActionResult.Failed("Folderul nu a putut fi găsit."));
                        string why = host.OpenFolder(target);
                        return Task.FromResult(why == null ? ActionResult.Ok("Folder deschis") : ActionResult.Failed(why));
                    }),
                One(ZipId, "Raft: arhivează elementul (zip, alături)", GZip, new[] { "arhivează", "fă zip", "comprimă", "zip", "compress" }, false, ZipTimeout,
                    async (item, path, folder, ct) =>
                    {
                        var r = await Task.Run(() => ShelfArchive.Zip(path, folder, ct), ct);
                        if (!r.Success) return ActionResult.Failed(r.Message);
                        return ActionResult.Ok(r.Message + AddResult(host, r.Path));
                    }),
                One(OcrId, "Raft: copiază textul din imagine (OCR)", GText, new[] { "text din imagine", "ocr", "recunoaște textul", "image to text" }, true, ImageTimeout,
                    async (item, path, folder, ct) =>
                    {
                        if (folder || !ShelfPaths.IsOcrImage(path)) return ActionResult.Failed("Elementul nu e o imagine (PNG, JPG, BMP, GIF, TIFF).");
                        string text;
                        try { text = await host.RecognizeTextAsync(path, ct); }
                        catch (ShelfImageException ex) { return ActionResult.Failed(ex.Message); }
                        if (text == null) return ActionResult.Failed("Windows nu are recunoaștere de text (Setări › Ora și limba › Limbă).");
                        if (string.IsNullOrWhiteSpace(text)) return ActionResult.Failed("Nu am găsit text în imagine.");
                        if (!host.SetText(text)) return ActionResult.Failed(Busy);
                        int lines = text.Split('\n').Length;
                        return ActionResult.Ok("Text copiat · " + (lines == 1 ? "un rând" : lines + " rânduri"));
                    }),
                One(ConvertId, "Raft: convertește imaginea (PNG ↔ JPG, fișier nou alături)", GConvert, new[] { "convertește", "png în jpg", "jpg în png", "convert image", "png to jpg" }, false, ImageTimeout,
                    async (item, path, folder, ct) =>
                    {
                        var to = folder ? ShelfImageFormat.None : ShelfPaths.ConvertTarget(path);
                        if (to == ShelfImageFormat.None) return ActionResult.Failed("Convertesc doar imagini PNG și JPG.");
                        ShelfFileResult r;
                        try { r = await host.ConvertImageAsync(path, to, ct); }
                        catch (ShelfImageException ex) { return ActionResult.Failed(ex.Message); }
                        if (r == null || !r.Success) return ActionResult.Failed(r?.Message ?? "Imaginea nu a putut fi convertită.");
                        string shown = ShelfPaths.TryNormalize(r.Path, out var made, out _) == ShelfRefusal.None ? ShelfPaths.NameOf(made) : System.IO.Path.GetFileName(r.Path);
                        return ActionResult.Ok((to == ShelfImageFormat.Jpeg ? "JPG creat: " : "PNG creat: ") + shown + AddResult(host, r.Path));
                    }),
                new ActionDescriptor(RemoveId, "Raft: scoate elementul (fișierul rămâne pe disc)", (args, ct) =>
                {
                    var item = host.Model.Find(args.GetText(ElementParam));
                    if (item == null || !host.Model.Remove(item.Id)) return Task.FromResult(ActionResult.Failed(Gone));
                    host.Changed();
                    return ActionResult.OkTask("Scos din raft");
                }, Any)
                {
                    Aliases = new[] { "scoate din raft", "remove from shelf" }, Category = Category, Icon = GRemove, FeatureId = FeatureId,
                    Parameters = new[] { Element() }, UnavailableMessage = Empty,
                },
                new ActionDescriptor(ClearId, "Raft: golește raftul (fișierele rămân pe disc)", (args, ct) =>
                {
                    int n = host.Model.Clear();
                    if (n == 0) return Task.FromResult(ActionResult.Failed(Empty));
                    host.Changed();
                    return ActionResult.OkTask(n == 1 ? "Raft golit (un element)" : "Raft golit (" + n + " elemente)");
                }, Any)
                {
                    Aliases = new[] { "golește raftul", "șterge raftul", "clear shelf", "empty shelf" }, Category = Category, Icon = GClear, FeatureId = FeatureId,
                    UnavailableMessage = Empty,
                },
            };
        }

        /// <summary>The item's path checked again like a drop (drive, existence, shortcut); null when it no longer passes.</summary>
        private static (string Path, bool Folder) Recheck(ShelfItem item, IShelfFileSystem fs) =>
            ShelfPaths.Check(item.Path, fs, out var p, out bool folder) == ShelfRefusal.None ? (p, folder) : (null, false);

        /// <summary>A file the shelf made goes on it too, if there's room (a short tail for the message).</summary>
        private static string AddResult(IShelfHost host, string path)
        {
            if (ShelfPaths.TryNormalize(path, out var p, out _) != ShelfRefusal.None) return "";
            var r = host.Model.TryAdd(p, false);
            if (r == ShelfRefusal.None) { host.Changed(); return ", adăugat în raft"; }
            return r == ShelfRefusal.Full ? " (raftul e plin)" : "";
        }

        /// <summary>Registered once at startup, from App.RegisterActions.</summary>
        public static void Register(ActionRegistry registry, IShelfHost host)
        {
            foreach (var a in Create(host)) registry.Register(a);
        }
    }
}
