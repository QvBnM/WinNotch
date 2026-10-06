using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace WinNotch.Features.SmartClipboard
{
    /// <summary>
    /// Password managers (KeePass, Bitwarden, 1Password…) mark copied secrets so clipboard tools don't record them;
    /// Windows' own clipboard history respects the same markers. The check the notch made inline before P21, now pure
    /// (the same logic): the clipboard history and Smart Clipboard both go through it, and a private text is neither
    /// recorded nor analyzed.
    /// </summary>
    public static class ClipboardPrivacy
    {
        /// <summary>Present at all = private.</summary>
        public static readonly IReadOnlyList<string> PrivateFormats = new[] { "ExcludeClipboardContentFromMonitorProcessing", "Clipboard Viewer Ignore" };
        /// <summary>A DWORD 0 in one of these = "don't keep it in the history / don't sync it" = private.</summary>
        public static readonly IReadOnlyList<string> ZeroMeansPrivate = new[] { "CanIncludeInClipboardHistory", "CanUploadToCloudClipboard" };

        /// <summary>
        /// <paramref name="isPresent"/> and <paramref name="getData"/> read the clipboard's data object (a MemoryStream or
        /// bytes for the DWORD formats). Any exception counts as private: the clipboard busy right after a password
        /// manager wrote to it means "when in doubt, don't record".
        /// </summary>
        public static bool IsPrivate(Func<string, bool> isPresent, Func<string, object> getData)
        {
            if (isPresent == null || getData == null) return true;
            try
            {
                foreach (var fmt in PrivateFormats) if (isPresent(fmt)) return true;
                foreach (var fmt in ZeroMeansPrivate)
                {
                    if (!isPresent(fmt)) continue;
                    var o = getData(fmt);
                    byte[] b = o is MemoryStream ms ? ms.ToArray() : o as byte[];
                    if (b != null && b.Length >= 4 && BitConverter.ToInt32(b, 0) == 0) return true;
                }
            }
            catch { return true; }
            return false;
        }

        /// <summary>
        /// The same check over a list of format names (ignoring case, like Windows' registered formats) and the values of
        /// the DWORD formats (the tests, and anything without a data object).
        /// </summary>
        public static bool IsPrivate(IEnumerable<string> formats, IReadOnlyDictionary<string, object> values = null)
        {
            if (formats == null) return true;
            var set = new HashSet<string>(formats.Where(f => f != null), StringComparer.OrdinalIgnoreCase);
            return IsPrivate(set.Contains, f => values != null && values.TryGetValue(f, out var v) ? v : null);
        }
    }
}
