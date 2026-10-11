using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WinNotch.Features.PagesV2
{
    /// <summary>
    /// P74: a line of text that puts itself in the monospaced face whenever it is holding a measurement, and back in
    /// the reading face when it is holding words.
    ///
    /// It has to decide again on every change, not once when the page is built: almost every value in the notch is
    /// written into a block that already exists ("—" at build, "28%" a second later), so a decision made at build time
    /// would be the wrong one for the whole life of the page. The check runs on the text that was just set, costs one
    /// pass over a handful of characters, and the rule itself lives in <see cref="PageStyle"/> (pure, tested).
    /// </summary>
    internal sealed class MeasureText : TextBlock
    {
        private FontFamily _words, _numbers;

        internal MeasureText(FontFamily words, FontFamily numbers)
        {
            _words = words; _numbers = numbers;
        }

        protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);
            if (e.Property != TextProperty || _numbers == null) return;
            var want = PageStyle.IsMeasurement(Text) ? _numbers : _words;
            if (want != null && !ReferenceEquals(FontFamily, want)) FontFamily = want;
        }
    }
}
