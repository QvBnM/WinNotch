using System;

namespace WinNotch.Features.PagesV2
{
    /// <summary>
    /// P74: the four standard pages, redrawn in the notch's own language — engraved labels instead of bold captions,
    /// every measurement in the monospaced face so columns of numbers line up and no row twitches while a value
    /// changes, thinner bars, and one thread along the bottom edge for the single thing that is running.
    ///
    /// Pure (no WPF): the metrics and the one rule that decides what counts as a measurement. The builders in
    /// <c>Ui</c> read this, so all four pages change in one place instead of being rewritten one by one.
    /// </summary>
    public static class PageStyle
    {
        /// <summary>Same id as the entry in FeatureCatalog (the tests check they match).</summary>
        public const string FeatureId = "pagini-redesenate";

        /// <summary>A card's corner, a bar's thickness and the thread under the open panel.</summary>
        public const double CardRadius = 16, BarHeight = 3, ThreadThickness = 2, ThreadInset = 20;

        /// <summary>A group's label: the label face, in capitals, small and quiet.</summary>
        public const double EyebrowSize = 11;

        /// <summary>The characters a measurement may hold besides digits and its unit.</summary>
        private const string Symbols = " .,:/%°×—-+~<>()";

        /// <summary>The longest unit tail still read as part of a measurement ("MB/s", "min", "GB", "°C", "Hz").</summary>
        public const int MaxUnitLetters = 5;

        /// <summary>
        /// Whether a piece of text is a measurement, and so belongs in the monospaced face: it has at least one digit,
        /// no word before the first digit (so "CPU 28%" stays a sentence while "28%" is a number), and at most a short
        /// unit after it. Hand-written, like the clipboard's reader: no regular expressions, no allocation per call.
        /// </summary>
        public static bool IsMeasurement(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            bool digit = false;
            int letters = 0;
            foreach (char c in text)
            {
                if (char.IsDigit(c)) { digit = true; continue; }
                if (char.IsLetter(c))
                {
                    if (!digit) return false;                  // a word before the first digit: this is a sentence
                    if (++letters > MaxUnitLetters) return false;
                    continue;
                }
                if (Symbols.IndexOf(c) < 0) return false;
            }
            return digit;
        }

        /// <summary>A group's name as it is drawn: in capitals, nothing else changed.</summary>
        public static string Eyebrow(string text) => (text ?? "").ToUpperInvariant();
    }
}
