using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WinNotch.Features.PagesV2
{
    /// <summary>
    /// P74: a line of text that puts itself in the monospaced face whenever it is holding a measurement, and leaves the
    /// reading face alone when it is holding words.
    ///
    /// It has to decide again on every change, not once when the page is built: almost every value in the notch is
    /// written into a block that already exists ("—" at build, "28%" a second later), so a decision made at build time
    /// would be the wrong one for the whole life of the page. The hook is the metadata of <c>Text</c>, overridden for
    /// this type — <c>TextBlock.OnPropertyChanged</c> is sealed, and a <c>DependencyPropertyDescriptor</c> would keep
    /// every block alive forever. The rule itself lives in <see cref="PageStyle"/> (pure, tested).
    /// </summary>
    internal sealed class MeasureText : TextBlock
    {
        static MeasureText()
        {
            // The base callback still runs (WPF chains them): this one only adds the choice of face.
            TextProperty.OverrideMetadata(typeof(MeasureText), new FrameworkPropertyMetadata(
                string.Empty,
                FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender,
                (d, e) => ((MeasureText)d).PickFace()));
        }

        private readonly FontFamily _words, _numbers;

        internal MeasureText(FontFamily words, FontFamily numbers)
        {
            _words = words; _numbers = numbers;
        }

        /// <summary>One pass over a handful of characters, on every change of the text.</summary>
        private void PickFace()
        {
            if (_numbers == null) return;
            var want = PageStyle.IsMeasurement(Text) ? _numbers : _words;
            if (want == null) { ClearValue(FontFamilyProperty); return; }   // words: whatever the window uses
            if (!ReferenceEquals(FontFamily, want)) FontFamily = want;
        }
    }
}
