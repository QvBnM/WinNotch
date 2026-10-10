using System;

namespace WinNotch.Core.Ui
{
    /// <summary>
    /// How big the standby pill is. Until 0.6.24 only the open panel and the alerts followed the window's scale, and
    /// the pill kept its 34 px and its 11 px text whatever the monitor — on a 1440p or 4K screen the time and the date
    /// were barely readable, which is exactly what the author reported. The pill now follows the same scale.
    /// Pure (no WPF): the window multiplies by these.
    /// </summary>
    internal static class PillSize
    {
        /// <summary>The pill's height in standby, and the small form's, before scaling.</summary>
        internal const double Standby = 34, Mini = 22;

        /// <summary>Their corner radius before scaling (half the height: the pill is a capsule).</summary>
        internal const double StandbyRadius = Standby / 2, MiniRadius = Mini / 2;

        /// <summary>The scale the pill is drawn at: the window's, never under 1 and never past the window's own limit.</summary>
        internal static double Scale(double uiScale) => Math.Clamp(uiScale <= 0 ? 1 : uiScale, 1, 1.75);

        /// <summary>The pill's height on screen (standby, or the small form).</summary>
        internal static double Height(double uiScale, bool mini) => (mini ? Mini : Standby) * Scale(uiScale);

        /// <summary>The pill's corner radius on screen.</summary>
        internal static double Radius(double uiScale, bool mini) => (mini ? MiniRadius : StandbyRadius) * Scale(uiScale);

        /// <summary>
        /// The pill's width on screen: the width its content asked for (measured unscaled, because the scale is a
        /// transform on the layer) multiplied by the scale.
        /// </summary>
        internal static double Width(double contentWidth, double uiScale) => Math.Max(0, contentWidth) * Scale(uiScale);
    }
}
