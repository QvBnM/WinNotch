namespace WinNotch
{
    /// <summary>The notch's side of the context engine (P12): only the "context.show" alert.</summary>
    public partial class NotchWindow
    {
        /// <summary>Shows what the context engine sees (debugging). UI thread.</summary>
        internal void ShowContextAlert(string title, string detail) =>
            ShowLive(LiveRow(LiveIcon(Features.Context.ContextActions.GInfo, CWhite), title, detail, null), 620, 58, 6000, true);
    }
}
