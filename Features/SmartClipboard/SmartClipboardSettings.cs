namespace WinNotch
{
    public sealed partial class AppSettings
    {
        /// <summary>
        /// P21: a short peek in the pill when you copy a JSON to format, a link with tracking or a JWT ("JSON copiat ·
        /// Formatează"; a fixed text, never the content). Off by default; works only with the "smart-clipboard" and
        /// "activity-manager" switches on. Missing (a settings.json from before P21) = off.
        /// </summary>
        public bool SmartClipboardPeek { get; set; }
    }
}
