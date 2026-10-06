using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using WinNotch.Core.Actions;
using WinNotch.Core.Flags;
using WinNotch.Features.Shelf;

namespace WinNotch
{
    public static partial class T
    {
        static IDictionary<string, bool> OnStoreShelf()
        {
            var s = AppSettings.NewFeatures();
            s[ShelfActions.FeatureId] = true;
            return s;
        }

        /// <summary>The fixes of review R1 (P23): links on the path, unplugged sticks, unreadable images and zips, unhandled drags.</summary>
        static void ShelfR1Tests()
        {
            // symlinks / junctions: refused, never followed
            var lfs = new FakeShelfFs().File(@"C:\a.txt");
            lfs.Entries[@"C:\link\x.txt"] = ShelfEntry.Link;
            string dir = NewTempDir();
            bool realOk;
            try
            {
                string real = Path.Combine(dir, "real.txt"), sub = Path.Combine(dir, "sub");
                File.WriteAllText(real, "x");
                Directory.CreateDirectory(sub);
                File.WriteAllText(Path.Combine(sub, "y.txt"), "y");
                bool made = true;
                try { File.CreateSymbolicLink(Path.Combine(dir, "link.txt"), real); Directory.CreateSymbolicLink(Path.Combine(dir, "linkdir"), sub); }
                catch (Exception) { made = false; }       // Windows without the privilege: only the plain cases
                realOk = !LocalShelfFileSystem.LinkOnPath(real) && !LocalShelfFileSystem.LinkOnPath(Path.Combine(sub, "y.txt")) &&
                         (!made || LocalShelfFileSystem.LinkOnPath(Path.Combine(dir, "link.txt")) && LocalShelfFileSystem.LinkOnPath(Path.Combine(dir, "linkdir", "y.txt")) &&
                                   LocalShelfFileSystem.Instance.Kind(Path.Combine(dir, "linkdir", "y.txt")) == ShelfEntry.Link) &&
                         LocalShelfFileSystem.Instance.Kind(real) == ShelfEntry.File;
            }
            finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
            Check("SH35", "R1: o legătură simbolică sau un junction (elementul însuși sau un folder din cale) nu intră în raft (ar putea duce spre \\\\server); legătura e citită (LinkTarget), nu urmată",
                  ShelfPaths.Check(@"C:\link\x.txt", lfs, out var lp, out _) == ShelfRefusal.UnsafeShortcut && lp == null && ShelfPaths.Check(@"C:\a.txt", lfs, out _, out _) == ShelfRefusal.None && realOk);

            // an unplugged stick keeps its items
            var fs = new FakeShelfFs().File(@"C:\p\a.txt").File(@"E:\s\b.txt");
            bool keep = !ShelfPaths.IsGone(@"C:\p\a.txt", fs) && !ShelfPaths.IsGone(@"E:\s\b.txt", fs) && ShelfPaths.IsGone(@"C:\p\lipsa.txt", fs) &&
                        ShelfPaths.IsGone(@"Z:\x.txt", fs) && ShelfPaths.IsGone(@"\\srv\x", fs);
            fs.Drives.Remove(@"E:\");
            fs.Entries.Remove(@"E:\s\b.txt");
            var host = new FakeShelfHost { Files = fs };
            host.Model.TryAdd(@"E:\s\b.txt", false);
            var reg = new ActionRegistry(new FeatureFlags(OnStoreShelf()), new InlineUiDispatcher());
            ShelfActions.Register(reg, host);
            var unplugged = ShelfRun(reg, ShelfActions.CopyPathId, "1");
            Check("SH36", "R1: un element dispare doar dacă unitatea e prezentă și fișierul lipsește; cu stick-ul scos (unitate lipsă) rămâne, iar acțiunea spune de ce",
                  keep && !ShelfPaths.IsGone(@"E:\s\b.txt", fs) && !unplugged.Success && unplugged.Message.Contains("nu e conectată") && host.Model.Count == 1 &&
                  Norm(NoComments(Src("Features/Shelf/NotchWindow.Shelf.cs"))).Contains("items.Where(i => ShelfPaths.IsGone(i.Path, LocalShelfFileSystem.Instance))"));

            // images that can't be read / OCR that fails: a fixed message, never a feature error
            var ifs = new FakeShelfFs().File(@"C:\p\x.png");
            var ihost = new FakeShelfHost { Files = ifs, OcrError = "Recunoașterea textului nu a reușit pentru această imagine." };
            ihost.Model.TryAdd(@"C:\p\x.png", false);
            var flags = new FeatureFlags(OnStoreShelf());
            var ireg = new ActionRegistry(flags, new InlineUiDispatcher());
            ShelfActions.Register(ireg, ihost);
            var results = Enumerable.Range(0, 4).Select(_ => ShelfRun(ireg, ShelfActions.OcrId, "1")).ToList();
            string images = Norm(NoComments(Src("Features/Shelf/ShelfImages.cs"))), part = Norm(NoComments(Src("Features/Shelf/NotchWindow.Shelf.cs")));
            Check("SH37", "R1: o imagine blocată sau un OCR care aruncă dau un mesaj fix (ShelfImageException), nu o eroare de funcție: de 4 ori la rând raftul rămâne pornit",
                  results.All(r => !r.Success && r.Message == "Recunoașterea textului nu a reușit pentru această imagine.") && flags.IsEnabled(ShelfActions.FeatureId) &&
                  images.Contains("try { bytes = File.ReadAllBytes(path); }") && images.Contains("catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is NotSupportedException)") &&
                  part.Contains("try { return await Services.ScreenTools.RecognizeAsync(img); } catch (Exception ex) when (ex is COMException || ex is System.IO.IOException"));

            // a zip of files that can't be read: no empty zip
            string zdir = NewTempDir();
            try
            {
                string proj = Path.Combine(zdir, "P");
                Directory.CreateDirectory(proj);
                File.WriteAllText(Path.Combine(proj, "a.txt"), "a");
                File.WriteAllText(Path.Combine(proj, "b.txt"), "b");
                var none = ShelfArchive.Zip(proj, true, CancellationToken.None, open: f => throw new IOException("blocat"));
                var half = ShelfArchive.Zip(proj, true, CancellationToken.None, open: f => f.EndsWith("a.txt", StringComparison.Ordinal) ? throw new IOException("blocat") : File.OpenRead(f));
                Check("SH38", "R1: zip în care niciun fișier nu poate fi citit → eșec, fără zip gol lăsat în folder; cu o parte citită → zip-ul, cu „1 sărite”",
                      !none.Success && none.Message.StartsWith("Niciun fișier nu a putut fi citit", StringComparison.Ordinal) && half.Success && half.Files == 1 && half.Skipped == 1 &&
                      Directory.GetFiles(zdir, "*.zip").Select(Path.GetFileName).SequenceEqual(new[] { "P.zip" }));
            }
            finally { try { Directory.Delete(zdir, true); } catch (IOException) { } }

            // drags nobody handled: refused, never the default (Move)
            string src = Src("Features/Shelf/NotchWindow.Shelf.cs"), pn = Norm(NoComments(src));
            string refuse = Norm(NoComments(MethodBody(src, "private void ShelfRefuseUnhandled(")));
            string onOpen = Norm(NoComments(MethodBody(src, "private void ShelfOnOpen()")));
            string hover = Norm(NoComments(MethodBody(src, "private bool ShelfDragHover(bool inside)")));
            string smoke = MethodBody(SmokeSrc("SmokeShelf.cs"), "private static void Shelf()") ?? "";
            Check("SH39", "R1: tragerile netratate (text, editare, alerte) refuzate de handler-e bubbling pe pastilă (Effects = None); overlay-ul doar la DragEnter cu fișiere; butonul principal citit la pornire / închidere, nu la fiecare tick; fumul oprește comutatorul și la eșec",
                  refuse == "{ if (e.Handled) return; e.Effects = DragDropEffects.None; e.Handled = true; }" &&
                  pn.Contains("Pill.AddHandler(DragEnterEvent, _shRefuse);") && pn.Contains("Pill.AddHandler(DragOverEvent, _shRefuse);") && pn.Contains("Pill.AddHandler(DropEvent, _shRefuse);") &&
                  pn.Contains("Pill.RemoveHandler(DropEvent, _shRefuse);") && !onOpen.Contains("ShelfShowPanel") && !hover.Contains("GetSystemMetrics") &&
                  Count(pn, "ShelfReadPrimaryButton();") == 2 && smoke.Contains("if (switchedOn && !_app.HasExited)"));
        }
    }
}
