using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace WinNotch.Features.Shelf
{
    /// <summary>
    /// The shelf's image work (WPF imaging), always off the UI thread, on a short-lived STA thread of its own: a PNG as a
    /// new JPEG (over white, JPEG has no transparency) or a JPEG as a new PNG, and an image read for OCR. Limits before
    /// any pixel is decoded: at most 100 MB on disk and 50 megapixels (a tiny file can claim a huge size), the format
    /// checked from the bytes, not the name. The new file never replaces one: <see cref="ShelfFiles.CreateNewBeside"/>.
    /// </summary>
    internal static class ShelfImages
    {
        public const long MaxFileBytes = 100L << 20;
        public const long MaxPixels = 50_000_000;
        /// <summary>The longest side given to OCR (Windows' engine reads less above that, and the UI thread encodes it).</summary>
        public const int OcrSide = 3000;

        private static Task<T> OnSta<T>(Func<T> work)
        {
            var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            var t = new Thread(() =>
            {
                try { done.TrySetResult(work()); }
                catch (Exception ex) { done.TrySetException(ex); }
                finally { Dispatcher.FromThread(Thread.CurrentThread)?.InvokeShutdown(); }     // WPF imaging gave this thread a dispatcher
            }) { IsBackground = true, Name = "Raft: imagine" };
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            return done.Task;
        }

        /// <summary>The first frame, with the limits checked from the header; <paramref name="expect"/> None = any format WPF reads.</summary>
        private static BitmapFrame Decode(string path, ShelfImageFormat expect, out MemoryStream keep)
        {
            keep = null;
            var info = new FileInfo(path);
            if (!info.Exists) throw new ShelfImageException("Imaginea nu mai există.");
            if (info.Length > MaxFileBytes) throw new ShelfImageException("Imaginea e prea mare (peste 100 MB).");
            byte[] bytes;
            try { bytes = File.ReadAllBytes(path); }                       // read once: no lock stays on the file
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is NotSupportedException)
            {
                throw new ShelfImageException("Imaginea nu a putut fi citită (e folosită de altă aplicație sau nu ai acces).");     // R1: not a feature error
            }
            if (expect != ShelfImageFormat.None && ShelfPaths.Sniff(bytes) != expect)
                throw new ShelfImageException(expect == ShelfImageFormat.Png ? "Fișierul nu e un PNG valid." : "Fișierul nu e un JPG valid.");
            keep = new MemoryStream(bytes, false);
            try
            {
                // lazy: only the header is read here, the pixels when they're copied (after the size check)
                var dec = BitmapDecoder.Create(keep, BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None);
                if (dec.Frames.Count == 0) throw new ShelfImageException("Imaginea nu a putut fi citită.");
                var f = dec.Frames[0];
                long px = (long)f.PixelWidth * f.PixelHeight;
                if (f.PixelWidth <= 0 || f.PixelHeight <= 0) throw new ShelfImageException("Imaginea nu a putut fi citită.");
                if (px > MaxPixels) throw new ShelfImageException("Imaginea e prea mare (peste 50 de megapixeli).");
                return f;
            }
            catch (Exception ex) when (ex is NotSupportedException || ex is FileFormatException || ex is ArgumentException || ex is System.Runtime.InteropServices.COMException || ex is OverflowException)
            {
                throw new ShelfImageException("Imaginea nu a putut fi citită.");
            }
        }

        private static byte[] Bgra(BitmapSource src, out int w, out int h)
        {
            w = src.PixelWidth; h = src.PixelHeight;
            var conv = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
            var px = new byte[w * 4 * h];
            conv.CopyPixels(px, w * 4, 0);
            return px;
        }

        private static double Dpi(double d) => d >= 1 && d <= 10000 ? d : 96;

        /// <summary>For "shelf.ocr": the image, scaled down to <see cref="OcrSide"/> if bigger, frozen (usable on the UI thread).</summary>
        public static Task<BitmapSource> LoadForOcrAsync(string path) => OnSta<BitmapSource>(() =>
        {
            var frame = Decode(path, ShelfImageFormat.None, out var keep);
            using (keep)
            {
                try
                {
                    BitmapSource src = frame;
                    double k = Math.Min(1.0, OcrSide / (double)Math.Max(frame.PixelWidth, frame.PixelHeight));
                    if (k < 1) src = new TransformedBitmap(frame, new ScaleTransform(k, k));
                    var px = Bgra(src, out int w, out int h);
                    var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, w * 4);
                    bmp.Freeze();
                    return bmp;
                }
                catch (Exception ex) when (ex is NotSupportedException || ex is FileFormatException || ex is ArgumentException || ex is System.Runtime.InteropServices.COMException || ex is OverflowException)
                {
                    throw new ShelfImageException("Imaginea nu a putut fi citită.");
                }
            }
        });

        /// <summary>For "shelf.convert-image": PNG → a new JPEG (quality 92, over white) or JPEG → a new PNG, beside the original.</summary>
        public static Task<ShelfFileResult> ConvertAsync(string path, ShelfImageFormat to, CancellationToken ct) => OnSta(() =>
        {
            if (to == ShelfImageFormat.None) throw new ShelfImageException("Convertesc doar imagini PNG și JPG.");
            var frame = Decode(path, to == ShelfImageFormat.Jpeg ? ShelfImageFormat.Png : ShelfImageFormat.Jpeg, out var keep);
            using (keep)
            {
                BitmapEncoder enc;
                try
                {
                    ct.ThrowIfCancellationRequested();
                    var px = Bgra(frame, out int w, out int h);
                    BitmapSource img;
                    if (to == ShelfImageFormat.Jpeg)
                    {
                        ShelfPaths.FlattenOnWhite(px);
                        img = BitmapSource.Create(w, h, Dpi(frame.DpiX), Dpi(frame.DpiY), PixelFormats.Bgr32, null, px, w * 4);
                        enc = new JpegBitmapEncoder { QualityLevel = 92 };
                    }
                    else
                    {
                        img = BitmapSource.Create(w, h, Dpi(frame.DpiX), Dpi(frame.DpiY), PixelFormats.Bgra32, null, px, w * 4);
                        enc = new PngBitmapEncoder();
                    }
                    enc.Frames.Add(BitmapFrame.Create(img));
                }
                catch (Exception ex) when (ex is NotSupportedException || ex is FileFormatException || ex is ArgumentException || ex is System.Runtime.InteropServices.COMException || ex is OverflowException)
                {
                    throw new ShelfImageException("Imaginea nu a putut fi citită.");
                }
                ct.ThrowIfCancellationRequested();
                try
                {
                    return ShelfFiles.CreateNewBeside(Path.GetDirectoryName(path), Path.GetFileNameWithoutExtension(path), ShelfNames.ExtensionFor(to), (s, t) => enc.Save(s), ct);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is NotSupportedException || ex is System.Runtime.InteropServices.COMException)
                {
                    throw new ShelfImageException("Fișierul nou nu a putut fi scris (cel început a fost șters).");
                }
            }
        });
    }
}
