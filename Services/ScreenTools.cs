using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WinNotch.Services
{
    /// <summary>Screenshots (whole monitor or a dragged area) and text recognition from the screen (Windows' built-in OCR).</summary>
    public static class ScreenTools
    {
        // ------------------------------------------------------------------ capture

        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int w, int h);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, int rop);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
        [DllImport("user32.dll")] private static extern int GetSystemMetrics(int i);
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(POINT p, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr mon, ref MONITORINFO mi);

        [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }

        private const int SRCCOPY = 0x00CC0020, CAPTUREBLT = 0x40000000;

        /// <summary>Screen area in physical pixels (virtual-desktop coordinates).</summary>
        public static BitmapSource Capture(Int32Rect r)
        {
            IntPtr screen = GetDC(IntPtr.Zero), mem = CreateCompatibleDC(screen), bmp = CreateCompatibleBitmap(screen, r.Width, r.Height);
            IntPtr old = SelectObject(mem, bmp);
            try
            {
                BitBlt(mem, 0, 0, r.Width, r.Height, screen, r.X, r.Y, SRCCOPY | CAPTUREBLT);
                SelectObject(mem, old);
                var src = Imaging.CreateBitmapSourceFromHBitmap(bmp, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                src.Freeze();
                return src;
            }
            finally
            {
                DeleteObject(bmp);
                DeleteDC(mem);
                ReleaseDC(IntPtr.Zero, screen);
            }
        }

        /// <summary>All monitors together.</summary>
        public static Int32Rect VirtualScreen() =>
            new Int32Rect(GetSystemMetrics(76), GetSystemMetrics(77), GetSystemMetrics(78), GetSystemMetrics(79));

        /// <summary>The monitor the mouse is on.</summary>
        public static Int32Rect MonitorUnderMouse()
        {
            GetCursorPos(out var p);
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            GetMonitorInfo(MonitorFromPoint(p, 2), ref mi);
            var m = mi.rcMonitor;
            return new Int32Rect(m.Left, m.Top, m.Right - m.Left, m.Bottom - m.Top);
        }

        public static string Folder
        {
            get
            {
                string f = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");
                Directory.CreateDirectory(f);
                return f;
            }
        }

        /// <summary>Saves as PNG in Pictures\Screenshots and puts the image on the clipboard. Returns the file path.</summary>
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetFinalPathNameByHandle(Microsoft.Win32.SafeHandles.SafeFileHandle h, System.Text.StringBuilder path, uint len, uint flags);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr sa, uint disposition, uint flags, IntPtr template);

        /// <summary>The real location of a folder (after junctions) is inside your profile.</summary>
        private static bool FolderInsideProfile(string dir)
        {
            using var h = CreateFile(dir, 0x80 /* FILE_READ_ATTRIBUTES */, 7, IntPtr.Zero, 3 /* OPEN_EXISTING */, 0x02000000 /* BACKUP_SEMANTICS */, IntPtr.Zero);
            return !h.IsInvalid && InsideProfile(h);
        }

        private static bool InsideProfile(FileStream fs) => InsideProfile(fs.SafeFileHandle);

        private static bool InsideProfile(Microsoft.Win32.SafeHandles.SafeFileHandle handle)
        {
            var sb = new System.Text.StringBuilder(1024);
            uint n = GetFinalPathNameByHandle(handle, sb, (uint)sb.Capacity, 0);
            if (n == 0 || n >= sb.Capacity) return false;
            string final = sb.ToString();
            if (final.StartsWith(@"\\?\")) final = final.Substring(4);
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd('\\') + "\\";
            return final.StartsWith(profile, StringComparison.OrdinalIgnoreCase);
        }

        public static string SaveAndCopy(BitmapSource img)
        {
            // (No reparse-point check here: Pictures is often in OneDrive, whose folders are reparse points.)
            string path = Path.Combine(Folder, "WinNotch " + DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss") + ".png");
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(img));
            if (App.IsAdmin && !FolderInsideProfile(Folder)) throw new IOException("Folderul Screenshots duce în afara profilului tău; captura nu a fost salvată.");
            using (var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                // As administrator: where did the file really land? Pictures may be in OneDrive (a reparse point, fine),
                // but a junction pointing outside your profile would make an elevated write land in a system folder.
                if (App.IsAdmin && !InsideProfile(fs)) { fs.Close(); try { File.Delete(path); } catch { } throw new IOException("Folderul Screenshots duce în afara profilului tău; captura nu a fost salvată."); }
                enc.Save(fs);
            }
            for (int i = 0; i < 3; i++)
            {
                try { Clipboard.SetImage(img); break; }
                catch { System.Threading.Thread.Sleep(60); }     // clipboard busy for a moment
            }
            return path;
        }

        public static void ShowInFolder(string path)
        {
            try
            {
                // as administrator: just the folder, through Shell.Open (normal rights); otherwise Explorer by full path
                if (App.IsAdmin) { Shell.Open(Path.GetDirectoryName(path)); return; }
                Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), "/select,\"" + path + "\"") { UseShellExecute = false });
            }
            catch { }
        }

        // ------------------------------------------------------------------ text recognition

        /// <summary>
        /// Text in the image, line by line, with Windows' own OCR (works offline). Uses Romanian when that language is
        /// installed in Windows, otherwise the first available one. Returns null when Windows has no OCR language.
        /// </summary>
        public static async Task<string> RecognizeAsync(BitmapSource img)
        {
            var engine = Engine();
            if (engine == null) return null;

            // Small areas are read better when enlarged.
            double scale = img.PixelHeight < 120 || img.PixelWidth < 500 ? 2.5 : img.PixelHeight < 400 ? 1.6 : 1;
            int max = (int)global::Windows.Media.Ocr.OcrEngine.MaxImageDimension;
            scale = Math.Min(scale, Math.Min((double)max / img.PixelWidth, (double)max / img.PixelHeight));
            BitmapSource src = Math.Abs(scale - 1) > 0.01 ? new TransformedBitmap(img, new ScaleTransform(scale, scale)) : img;

            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(src));
            var ms = new MemoryStream();
            enc.Save(ms);
            ms.Position = 0;

            using var ras = ms.AsRandomAccessStream();
            var decoder = await global::Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(ras);
            using var sb = await decoder.GetSoftwareBitmapAsync(global::Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, global::Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied);
            var result = await engine.RecognizeAsync(sb);
            return string.Join(Environment.NewLine, result.Lines.Select(l => l.Text)).Trim();
        }

        private static global::Windows.Media.Ocr.OcrEngine Engine()
        {
            try
            {
                var langs = global::Windows.Media.Ocr.OcrEngine.AvailableRecognizerLanguages;
                var ro = langs.FirstOrDefault(l => l.LanguageTag.StartsWith("ro", StringComparison.OrdinalIgnoreCase));
                if (ro != null) return global::Windows.Media.Ocr.OcrEngine.TryCreateFromLanguage(ro);
                return global::Windows.Media.Ocr.OcrEngine.TryCreateFromUserProfileLanguages()
                       ?? (langs.Count > 0 ? global::Windows.Media.Ocr.OcrEngine.TryCreateFromLanguage(langs[0]) : null);
            }
            catch (Exception ex) { App.Log("OCR: " + ex.Message); return null; }
        }
    }

    /// <summary>Frees RAM: trims what apps keep in memory without using it (and, as administrator, the standby cache).</summary>
    public static class MemoryTools
    {
        [DllImport("psapi.dll")] private static extern bool EmptyWorkingSet(IntPtr hProcess);
        [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);
        [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern bool LookupPrivilegeValue(string system, string name, out long luid);
        [DllImport("advapi32.dll", SetLastError = true)] private static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll, ref TOKEN_PRIVILEGES state, int len, IntPtr prev, IntPtr retLen);
        [DllImport("ntdll.dll")] private static extern int NtSetSystemInformation(int infoClass, ref int info, int length);

        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength, dwMemoryLoad;
            public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct TOKEN_PRIVILEGES { public int Count; public long Luid; public int Attributes; }

        /// <summary>RAM in use, in bytes, and total.</summary>
        public static (ulong Used, ulong Total) Status()
        {
            var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            if (!GlobalMemoryStatusEx(ref m)) return (0, 0);
            return (m.ullTotalPhys - m.ullAvailPhys, m.ullTotalPhys);
        }

        /// <summary>Returns bytes freed and how many apps were trimmed.</summary>
        public static (long Freed, int Apps, bool Standby) Optimize(IProgress<(int Done, int Total)> progress = null)
        {
            var before = Status().Used;
            int count = 0, done = 0;
            var procs = Process.GetProcesses();
            foreach (var p in procs)
            {
                if (++done % 4 == 0) progress?.Report((done, procs.Length));
                try
                {
                    if (p.Id <= 4) continue;
                    IntPtr h = OpenProcess(0x0100 | 0x1000, false, p.Id);    // SET_QUOTA | QUERY_LIMITED_INFORMATION
                    if (h == IntPtr.Zero) continue;
                    if (EmptyWorkingSet(h)) count++;
                    CloseHandle(h);
                }
                catch { }
                finally { p.Dispose(); }
            }
            progress?.Report((procs.Length, procs.Length));
            bool standby = App.IsAdmin && PurgeStandby();
            System.Threading.Thread.Sleep(400);
            var after = Status().Used;
            return ((long)before - (long)after, count, standby);
        }

        /// <summary>Empties the standby list (file cache). Needs administrator rights.</summary>
        private static bool PurgeStandby()
        {
            try
            {
                if (!OpenProcessToken(GetCurrentProcess(), 0x0020 | 0x0008, out var token)) return false;    // ADJUST_PRIVILEGES | QUERY
                try
                {
                    if (!LookupPrivilegeValue(null, "SeProfileSingleProcessPrivilege", out long luid)) return false;
                    var tp = new TOKEN_PRIVILEGES { Count = 1, Luid = luid, Attributes = 2 };              // SE_PRIVILEGE_ENABLED
                    AdjustTokenPrivileges(token, false, ref tp, Marshal.SizeOf<TOKEN_PRIVILEGES>(), IntPtr.Zero, IntPtr.Zero);
                }
                finally { CloseHandle(token); }
                int cmd = 4;                                                                                // MemoryPurgeStandbyList
                return NtSetSystemInformation(80, ref cmd, sizeof(int)) == 0;                               // SystemMemoryListInformation
            }
            catch (Exception ex) { App.Log("RAM standby: " + ex.Message); return false; }
        }
    }
}
