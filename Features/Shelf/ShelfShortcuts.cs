using System;
using System.Text;
using WinNotch.Features.SmartClipboard;

namespace WinNotch.Features.Shelf
{
    /// <summary>
    /// Shortcuts on the shelf: a .lnk or .url may lead to a network share (or make Explorer fetch an icon from one), so
    /// it goes on only when its own bytes say, locally, that everything in it is on this PC. Nothing is resolved (no
    /// IShellLink::Resolve, no Shell icon, no network): the file is read as bytes (MS-SHLLINK) or as INI text. Anything
    /// that can't be verified this way (no LinkInfo, a broken file) is refused: the safe side.
    /// </summary>
    public static class ShelfShortcuts
    {
        private const uint HasLinkTargetIdList = 0x1, HasLinkInfo = 0x2, HasName = 0x4, HasRelativePath = 0x8, HasWorkingDir = 0x10,
                           HasArguments = 0x20, HasIconLocation = 0x40, IsUnicode = 0x80;
        private const uint VolumeIdAndLocalBasePath = 0x1, CommonNetworkRelativeLink = 0x2;
        private const uint EnvironmentBlock = 0xA0000001, IconEnvironmentBlock = 0xA0000007;
        private const int MaxString = 4096;

        private static readonly byte[] LinkClsid = { 0x01, 0x14, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0xC0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x46 };

        /// <summary>
        /// A .lnk whose target is on a local drive: a LinkInfo with a local base path (no network part, a volume that isn't
        /// "remote"), the target's drive local or removable now too, and no relative path, working folder, icon or
        /// environment target that is a network path. The arguments aren't looked at (the shelf never starts the link).
        /// </summary>
        /// <param name="expand">%VAR% expansion for the environment blocks (Environment.ExpandEnvironmentVariables in the app).</param>
        /// <param name="driveOf">The drive's kind now ("C:\" → local), so a letter mapped to a share since is caught too.</param>
        public static bool IsLocalLink(byte[] b, Func<string, string> expand, Func<string, ShelfDrive> driveOf)
        {
            try
            {
                if (b == null || b.Length < 0x4C || U32(b, 0) != 0x4C) return false;
                for (int i = 0; i < 16; i++) if (b[4 + i] != LinkClsid[i]) return false;
                uint flags = U32(b, 0x14);
                int pos = 0x4C;
                if ((flags & HasLinkTargetIdList) != 0)
                {
                    int idSize = U16(b, pos);
                    pos = Advance(b, pos, 2 + idSize);
                }
                if ((flags & HasLinkInfo) == 0) return false;                                  // nothing to verify locally
                int info = pos;
                int infoSize = (int)U32(b, info);
                if (infoSize < 0x1C || info + infoSize > b.Length) return false;
                int headerSize = (int)U32(b, info + 4);
                uint infoFlags = U32(b, info + 8);
                if ((infoFlags & CommonNetworkRelativeLink) != 0 || (infoFlags & VolumeIdAndLocalBasePath) == 0) return false;
                int volume = info + (int)U32(b, info + 12);
                if (volume + 8 > info + infoSize) return false;
                uint driveType = U32(b, volume + 4);                                          // 2 removable, 3 fixed, 5 CD, 6 RAM disk
                if (driveType != 2 && driveType != 3 && driveType != 5 && driveType != 6) return false;
                string basePath = AnsiZ(b, info + (int)U32(b, info + 16), info + infoSize);
                string suffix = AnsiZ(b, info + (int)U32(b, info + 24), info + infoSize);
                if (headerSize >= 0x24)
                {
                    int ub = (int)U32(b, info + 28), us = (int)U32(b, info + 32);
                    if (ub > 0) basePath = UnicodeZ(b, info + ub, info + infoSize);
                    if (us > 0) suffix = UnicodeZ(b, info + us, info + infoSize);
                }
                if (basePath == null || suffix == null) return false;
                string target = basePath.Length > 0 && suffix.Length > 0 && !basePath.EndsWith("\\", StringComparison.Ordinal) ? basePath + "\\" + suffix : basePath + suffix;
                if (ShelfPaths.TryNormalize(target, out var local, out _) != ShelfRefusal.None) return false;
                var now = driveOf?.Invoke(local.Substring(0, 3)) ?? ShelfDrive.Missing;
                if (now != ShelfDrive.Local && now != ShelfDrive.Removable) return false;
                pos = info + infoSize;

                // the strings: name, relative path, working folder, arguments, icon (counted, in this order)
                bool unicode = (flags & IsUnicode) != 0;
                foreach (uint f in new[] { HasName, HasRelativePath, HasWorkingDir, HasArguments, HasIconLocation })
                {
                    if ((flags & f) == 0) continue;
                    int count = U16(b, pos);
                    if (count > MaxString) return false;
                    int bytes = count * (unicode ? 2 : 1);
                    if (pos + 2 + bytes > b.Length) return false;
                    string value = unicode ? Encoding.Unicode.GetString(b, pos + 2, bytes) : Encoding.Latin1.GetString(b, pos + 2, bytes);
                    pos += 2 + bytes;
                    if (f == HasName || f == HasArguments) continue;
                    if (IsNetwork(value, expand)) return false;
                }

                // extra data: the environment targets (a %VAR% path Windows prefers over LinkInfo) and the icon's
                while (pos + 4 <= b.Length)
                {
                    int size = (int)U32(b, pos);
                    if (size < 4) break;                                                       // the terminal block
                    if (size < 8 || pos + size > b.Length) return false;
                    uint sig = U32(b, pos + 4);
                    if ((sig == EnvironmentBlock || sig == IconEnvironmentBlock) && size >= 8 + 260 + 520)
                    {
                        string ansi = AnsiZ(b, pos + 8, pos + 8 + 260);
                        string wide = UnicodeZ(b, pos + 8 + 260, pos + 8 + 260 + 520);
                        if (ansi == null || wide == null || IsNetwork(ansi, expand) || IsNetwork(wide, expand)) return false;
                    }
                    pos += size;
                }
                return true;
            }
            catch (ArgumentException) { return false; }
            catch (IndexOutOfRangeException) { return false; }
        }

        /// <summary>
        /// A .url (INI text: [InternetShortcut] URL=…, IconFile=…, WorkingDirectory=…): refused when any value is a
        /// network path or a file:// link with a host (an icon fetched from \\server is the classic way to leak a login).
        /// </summary>
        public static bool IsLocalUrlFile(byte[] b)
        {
            if (b == null) return false;
            string text = b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE ? Encoding.Unicode.GetString(b, 2, b.Length - 2)
                        : b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF ? Encoding.UTF8.GetString(b, 3, b.Length - 3)
                        : Encoding.Latin1.GetString(b);
            if (text.IndexOf('\0') >= 0) return false;
            bool any = false;
            foreach (var line in text.Split('\n'))
            {
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string value = line.Substring(eq + 1).Trim().Trim('"');
                any = true;
                if (IsNetwork(value, null)) return false;
            }
            return any;
        }

        /// <summary>A network path as written, or after %VAR% expansion (%LOGONSERVER% is "\\DC01").</summary>
        private static bool IsNetwork(string value, Func<string, string> expand)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            if (SmartClipRecognizer.IsNetworkPath(value)) return true;
            if (expand == null || value.IndexOf('%') < 0) return false;
            string e;
            try { e = expand(value); } catch (ArgumentException) { return true; }
            return e != null && (SmartClipRecognizer.IsNetworkPath(e) || e.IndexOf('%') >= 0 && e.TrimStart().StartsWith("%", StringComparison.Ordinal));
        }

        private static uint U32(byte[] b, int i) => i < 0 || i + 4 > b.Length ? throw new ArgumentException() : BitConverter.ToUInt32(b, i);
        private static int U16(byte[] b, int i) => i < 0 || i + 2 > b.Length ? throw new ArgumentException() : BitConverter.ToUInt16(b, i);

        private static int Advance(byte[] b, int pos, int by)
        {
            int next = pos + by;
            if (by < 0 || next > b.Length) throw new ArgumentException();
            return next;
        }

        /// <summary>A zero-terminated single-byte string between <paramref name="start"/> and <paramref name="end"/>; null when it doesn't end there.</summary>
        private static string AnsiZ(byte[] b, int start, int end)
        {
            if (start < 0 || start >= end || end > b.Length) return null;
            int z = Array.IndexOf(b, (byte)0, start, end - start);
            if (z < 0 || z - start > MaxString) return null;
            return Encoding.Latin1.GetString(b, start, z - start);
        }

        private static string UnicodeZ(byte[] b, int start, int end)
        {
            if (start < 0 || start >= end || end > b.Length) return null;
            for (int i = start; i + 1 < end; i += 2)
                if (b[i] == 0 && b[i + 1] == 0) return i - start > MaxString * 2 ? null : Encoding.Unicode.GetString(b, start, i - start);
            return null;
        }
    }
}
