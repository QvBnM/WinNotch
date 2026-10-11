using System;

namespace WinNotch.Features.PagesFormat
{
    /// <summary>What a piece of text is for. The face, the size and the colour all follow from this, in one place.</summary>
    public enum Ink
    {
        /// <summary>A heading of a row or a card ("Microfonul e folosit").</summary>
        Title,
        /// <summary>A name inside a list ("Dell U2723QE").</summary>
        Name,
        /// <summary>The line under a title ("Microsoft Teams · de 12 min").</summary>
        Sub,
        /// <summary>The word in front of a value ("descărcare", "ieșire").</summary>
        Label,
        /// <summary>A group's name, engraved: monospaced capitals, spaced out, quiet ("PROCESOR").</summary>
        Eyebrow,
        /// <summary>The unit after a number, engraved at the same weight as it ("MB/S", "4K · 60 HZ").</summary>
        Unit,
        /// <summary>The big reading of a card ("28%").</summary>
        Big,
        /// <summary>The second reading of a card ("41%" on memory).</summary>
        Medium,
        /// <summary>A value on a row ("48,2", "84%", "12 ms").</summary>
        Value,
        /// <summary>The quiet reading beside a big one ("13,1 / 32 GB"): monospaced, but not engraved.</summary>
        Note,
    }

    /// <summary>
    /// P75: the form of the four standard pages, as the design plates draw them (`docs/design`, `docs/BRIEF-PAGINI.md`).
    ///
    /// Pure (no WPF): the type scale, the geometry and the one rule that says which of the two accents a number gets.
    /// P74 gave the pages the notch's typography; this gives them its shape — two columns of cards instead of one list
    /// of rows — so the switch below owns the form and brings its own typography with it.
    /// </summary>
    public static class PageFormat
    {
        /// <summary>Same id as the entry in FeatureCatalog (the tests check they match).</summary>
        public const string FeatureId = "pagini-format";

        /// <summary>The plates are drawn at this width of the open notch; every other number here is measured on them.</summary>
        public const double PlateWidth = 720;

        /// <summary>The panel's own padding, the gap under the header and the gutter between the two columns.</summary>
        public const double Pad = 18, HeaderGap = 12, Gutter = 10, CardGap = 10;

        /// <summary>A card's corner and the padding inside it; a chip is a capsule, so its corner is half its height.</summary>
        public const double CardRadius = 16, CardPadH = 13, CardPadV = 11;

        /// <summary>A reading bar: thin, because a bar is a reading and not a block of colour.</summary>
        public const double BarHeight = 3;

        /// <summary>
        /// How much of the room the left column takes on each page, measured on the plates. The proportion is of the
        /// space the two columns share — the content **minus** the gutter — because the gutter is a fixed width that
        /// does not grow with the window; dividing by the whole content would make every column a few pixels short.
        /// </summary>
        public const double Shared = 684 - Gutter;
        public const double HomeSplit = 440.0 / Shared, SystemSplit = 394.0 / Shared,
                            DevicesSplit = 374.0 / Shared, ToolsSplit = 394.0 / Shared;

        /// <summary>How tall the open notch is on each page, at the plates' width.</summary>
        public const double HomeHeight = 310, SystemHeight = 320, DevicesHeight = 330, ToolsHeight = 360;

        /// <summary>The size of a piece of text, by what it is for.</summary>
        public static double Size(Ink ink) => ink switch
        {
            Ink.Big => 20,
            Ink.Medium => 17,
            Ink.Value => 12.5,
            Ink.Note => 10.5,
            Ink.Title => 13,
            Ink.Name => 11.5,
            Ink.Sub => 11.5,
            Ink.Label => 10.5,
            Ink.Eyebrow => 9.5,
            Ink.Unit => 9.5,
            _ => 11.5,
        };

        /// <summary>Whether this piece of text is set in the monospaced face: every number, and every engraved label.</summary>
        public static bool Mono(Ink ink) =>
            ink is Ink.Big or Ink.Medium or Ink.Value or Ink.Note or Ink.Eyebrow or Ink.Unit;

        /// <summary>Whether it is set in the heavier weight (600 for words, 500 for numbers).</summary>
        public static bool Strong(Ink ink) =>
            ink is Ink.Title or Ink.Name or Ink.Big or Ink.Medium or Ink.Value or Ink.Note or Ink.Unit;

        /// <summary>The theme brush for a piece of text, by what it is for. Never a colour of its own.</summary>
        public static string BrushKey(Ink ink) => ink switch
        {
            Ink.Title or Ink.Name or Ink.Big or Ink.Medium or Ink.Value => "InkBrush",
            Ink.Sub or Ink.Label => "MutedBrush",
            Ink.Note => "DimBrush",
            _ => "DimBrush",
        };

        /// <summary>Letters are spaced out only where the plates engrave them.</summary>
        public static double Tracking(Ink ink) => ink is Ink.Eyebrow or Ink.Unit ? 0.085 : 0;

        /// <summary>A group's name as it is drawn: in capitals, nothing else changed.</summary>
        public static string Eyebrow(string text) => (text ?? "").ToUpperInvariant();

        /// <summary>What a number is about. It decides which of the two accents it gets — and most get neither.</summary>
        public enum Reading
        {
            /// <summary>Neither moving nor cold: it stays plain.</summary>
            Plain,
            /// <summary>Something that is moving right now: the song, the download, the charge going up.</summary>
            Live,
            /// <summary>Cold data: a temperature, a process's share.</summary>
            Cold,
            /// <summary>Something wrong: too hot, too full, the microphone open.</summary>
            Alarm,
        }

        /// <summary>
        /// The plates use exactly two accents, each with one meaning: amber for what is moving now, ice for cold data.
        /// A number that is neither stays plain — that is what keeps the two readable.
        /// </summary>
        public static string ReadingBrush(Reading r) => r switch
        {
            Reading.Live => "AccentBrush",
            Reading.Cold => "InfoBrush",
            Reading.Alarm => "HotBrush",
            _ => "InkBrush",
        };

        /// <summary>
        /// The two columns of a page, at the width the notch actually has. The plates are drawn at
        /// <see cref="PlateWidth"/>, but the open notch is as wide as the user chose, so the split is a proportion and
        /// the gutter is fixed: a gutter that grew with the window would stop reading as one.
        /// </summary>
        public static (double Left, double Right) Columns(double contentWidth, double split)
        {
            double w = Math.Max(0, contentWidth - Gutter);
            double left = Math.Round(w * Math.Clamp(split, 0.2, 0.8));
            return (left, Math.Max(0, w - left));
        }

        /// <summary>The content width inside the panel's padding.</summary>
        public static double Content(double panelWidth) => Math.Max(0, panelWidth - 2 * Pad);
    }
}
