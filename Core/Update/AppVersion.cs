using System;
using System.Linq;

namespace WinNotch.Core.Update
{
    /// <summary>
    /// A WinNotch version: major.minor.patch with an optional pre-release suffix ("0.7.0-rc.2"). Compared number by
    /// number (0.6.10 &gt; 0.6.9); a version with a suffix is older than the same version without one
    /// (0.7.0-rc.1 &lt; 0.7.0-rc.2 &lt; 0.7.0). Build metadata after "+" is ignored.
    /// </summary>
    public sealed class AppVersion : IComparable<AppVersion>, IEquatable<AppVersion>
    {
        public int Major { get; }
        public int Minor { get; }
        public int Patch { get; }
        /// <summary>The part after "-" ("rc.2"), or "" for a final version.</summary>
        public string Pre { get; }

        public AppVersion(int major, int minor, int patch, string pre = "")
        {
            Major = major; Minor = minor; Patch = patch; Pre = pre ?? "";
        }

        public bool IsPreRelease => Pre.Length > 0;

        public static readonly AppVersion Zero = new AppVersion(0, 0, 0);

        /// <summary>
        /// "0.6.9", "v0.7.0-rc.1", "1.0", "0.6.9.0" (a 4th number, as in assembly versions, is ignored), "0.7.0+abc".
        /// Anything else is refused without an exception.
        /// </summary>
        public static bool TryParse(string text, out AppVersion version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(text) || text.Length > 64) return false;
            string s = text.Trim();
            if (s[0] == 'v' || s[0] == 'V') s = s.Substring(1);
            int plus = s.IndexOf('+');
            if (plus >= 0) s = s.Substring(0, plus);
            string pre = "";
            int dash = s.IndexOf('-');
            if (dash >= 0)
            {
                pre = s.Substring(dash + 1);
                s = s.Substring(0, dash);
                var ids = pre.Split('.');
                if (ids.Any(id => id.Length == 0 || !id.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'))) return false;
            }
            var parts = s.Split('.');
            if (parts.Length < 1 || parts.Length > 4) return false;
            var nums = new int[3];
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length == 0 || parts[i].Length > 9 || !parts[i].All(char.IsAsciiDigit)) return false;
                if (i < 3) nums[i] = int.Parse(parts[i], System.Globalization.CultureInfo.InvariantCulture);
            }
            version = new AppVersion(nums[0], nums[1], nums[2], pre);
            return true;
        }

        public static AppVersion ParseOrZero(string text) => TryParse(text, out var v) ? v : Zero;

        public int CompareTo(AppVersion other)
        {
            if (other is null) return 1;
            int c = Major.CompareTo(other.Major);
            if (c == 0) c = Minor.CompareTo(other.Minor);
            if (c == 0) c = Patch.CompareTo(other.Patch);
            if (c != 0) return c;
            if (Pre.Length == 0 && other.Pre.Length == 0) return 0;
            if (Pre.Length == 0) return 1;              // final > any pre-release of it
            if (other.Pre.Length == 0) return -1;
            return ComparePre(Pre, other.Pre);
        }

        /// <summary>Semantic-versioning order of the suffixes: numbers numerically, numbers before words, shorter first.</summary>
        private static int ComparePre(string a, string b)
        {
            var x = a.Split('.');
            var y = b.Split('.');
            for (int i = 0; i < Math.Min(x.Length, y.Length); i++)
            {
                bool nx = x[i].All(char.IsAsciiDigit), ny = y[i].All(char.IsAsciiDigit);
                int c;
                if (nx && ny) c = x[i].Length != y[i].Length ? x[i].Length.CompareTo(y[i].Length) : string.CompareOrdinal(x[i], y[i]);
                else if (nx != ny) c = nx ? -1 : 1;
                else c = string.CompareOrdinal(x[i], y[i]);
                if (c != 0) return c;
            }
            return x.Length.CompareTo(y.Length);
        }

        public bool Equals(AppVersion other) => other is not null && CompareTo(other) == 0;
        public override bool Equals(object obj) => obj is AppVersion v && Equals(v);
        public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, Pre);

        public static bool operator ==(AppVersion a, AppVersion b) => a is null ? b is null : a.Equals(b);
        public static bool operator !=(AppVersion a, AppVersion b) => !(a == b);
        public static bool operator >(AppVersion a, AppVersion b) => a is not null && a.CompareTo(b) > 0;
        public static bool operator <(AppVersion a, AppVersion b) => b is not null && b.CompareTo(a) > 0;
        public static bool operator >=(AppVersion a, AppVersion b) => a == b || a > b;
        public static bool operator <=(AppVersion a, AppVersion b) => a == b || a < b;

        /// <summary>"0.6.9" or "0.7.0-rc.1": the same text as the release tag (without "v") and the signed message.</summary>
        public override string ToString() => Major + "." + Minor + "." + Patch + (Pre.Length > 0 ? "-" + Pre : "");
    }
}
