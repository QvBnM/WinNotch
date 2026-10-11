using System;
using System.Collections.Generic;
using System.Linq;
using WinNotch.Core.Context;

namespace WinNotch.Features.StatusDot
{
    /// <summary>What the dot is showing. The order is the priority order: the first one that applies wins.</summary>
    public enum DotKind
    {
        /// <summary>Nothing is going on: no dot at all.</summary>
        None = 0,
        /// <summary>The microphone or the camera is in use. Always first: a privacy state is never hidden by music.</summary>
        Listening,
        /// <summary>Something the user started is running: an update, a speed test, an action.</summary>
        Working,
        /// <summary>Something is playing.</summary>
        Playing,
        /// <summary>The charger is in.</summary>
        Charging,
    }

    /// <summary>
    /// P73: a small disc beside the pill that says what is going on right now, with one glyph — music, charger,
    /// microphone, a task in progress. Asked for by the author while reviewing the notch's design: on the small form
    /// the pill is only the time and the date, so there is nothing left to tell him his microphone is live.
    ///
    /// Pure (no WPF): what to show, in what order, with what ring. Everything it needs is read from the context engine's
    /// snapshot — the feature never looks at the microphone or at the media itself.
    /// </summary>
    public static class StatusDotRules
    {
        /// <summary>Same id as the entry in FeatureCatalog (the tests check they match).</summary>
        public const string FeatureId = "bulina-de-stare";

        /// <summary>The disc, its distance from the pill and the glyph inside it, before scaling.</summary>
        public const double Diameter = 20, Gap = 8, GlyphSize = 11;

        /// <summary>The progress ring around the disc: its thickness and how far outside the disc it sits.</summary>
        public const double RingThickness = 1.5, RingGap = 1;

        /// <summary>Glyphs from the icon font the app already uses, one per state.</summary>
        public const string GlyphListening = "", GlyphWorking = "",
                            GlyphPlaying = "", GlyphCharging = "";

        /// <summary>
        /// What the dot shows, in priority order: a privacy state first (it is the one the user cannot otherwise see),
        /// then what he started himself, then what is merely playing, then the charger. Never two at once: a row of
        /// coloured dots is a traffic light nobody reads.
        /// </summary>
        public static DotKind Pick(ContextSnapshot s, bool working)
        {
            var all = All(s, working);
            return all.Count > 0 ? all[0] : DotKind.None;
        }

        /// <summary>Everything that applies, in priority order — what the hover shows, all of it at once.</summary>
        public static IReadOnlyList<DotKind> All(ContextSnapshot s, bool working)
        {
            var list = new List<DotKind>();
            if (s != null && (s.MicrophoneInUse || s.CameraInUse)) list.Add(DotKind.Listening);
            if (working) list.Add(DotKind.Working);
            if (s != null && s.MediaPlaying) list.Add(DotKind.Playing);
            if (s != null && s.HasBattery && !s.OnBattery) list.Add(DotKind.Charging);
            return list;
        }

        /// <summary>
        /// The ring around the disc: the same thread that runs along the pill's bottom edge, bent into a circle. It is
        /// the song's progress, the battery's level or the task's progress; a negative value means no ring at all —
        /// listening has no progress, and that missing ring is what tells it apart at a glance.
        /// </summary>
        public static double Ring(DotKind kind, ContextSnapshot s, double mediaProgress, double workProgress) => kind switch
        {
            DotKind.Playing => Fraction(mediaProgress),
            DotKind.Working => Fraction(workProgress),
            DotKind.Charging => s != null && s.BatteryPercent >= 0 ? Math.Clamp(s.BatteryPercent / 100.0, 0, 1) : -1,
            _ => -1,
        };

        private static double Fraction(double v) => v >= 0 && v <= 1 ? v : -1;

        /// <summary>
        /// Where the ring ends, on a circle of this radius, starting at the top and going clockwise. The caller draws
        /// one arc from the top to this point; <c>Large</c> says whether it is the long way round.
        /// </summary>
        public static (double X, double Y, bool Large) RingEnd(double progress, double radius)
        {
            double p = Math.Clamp(progress, 0, 1);
            double a = p * 2 * Math.PI - Math.PI / 2;
            return (radius + radius * Math.Cos(a), radius + radius * Math.Sin(a), p > 0.5);
        }

        /// <summary>
        /// Whether the dot is shown at all. It belongs to the resting notch: with the notch open the same facts are
        /// inside it, and over an alert it would be a second thing asking to be read.
        /// </summary>
        public static bool Visible(DotKind kind, bool notchOpen, bool alertShowing, bool hidden) =>
            kind != DotKind.None && !notchOpen && !alertShowing && !hidden;

        /// <summary>
        /// Anchored, the dot is not a floating disc beside the pill but a glyph inside the drawer's right end: with the
        /// shape growing out of the bezel, a circle hovering next to it is the only thing in the scene not touching the
        /// edge, and the strip of wallpaper between the two reads as a notch of its own. Reported by the author on the
        /// first build: the dot "does not borrow the notch's behaviour and is not stuck to the screen".
        /// </summary>
        public static bool Fused(bool anchored) => anchored;

        /// <summary>
        /// How wide the disc is beside a pill of this height. It borrows the pill's own behaviour instead of keeping
        /// one size: the pill shrinks to the small form, so the disc shrinks with it, and the pair keeps its balance.
        /// </summary>
        public static double Size(double pillHeight) =>
            pillHeight <= 0 ? Diameter : Math.Clamp(pillHeight - 14, MinDiameter, Diameter);

        /// <summary>The disc never goes under this, or the glyph inside it stops being a glyph.</summary>
        public const double MinDiameter = 14;

        /// <summary>
        /// Fused, the glyph sits this far inside the pill's right edge, with a hairline before it — the same spacing
        /// the pill already keeps between its own items.
        /// </summary>
        public const double FusedInset = 12;

        /// <summary>The slack around the glyph inside its box: the hairline on the left and the air on the right.</summary>
        public const double GlyphPad = 12;

        /// <summary>
        /// How much room the pill has to leave empty at its right end while the glyph is fused into it. Without this
        /// the glyph is simply painted over whatever the pill already shows there (reported by the author: the
        /// microphone landed on top of the weather). Zero when the dot stands beside the pill as a satellite, and zero
        /// when there is nothing to show — otherwise the pill would keep a wide empty strip for no reason.
        /// </summary>
        public static double FusedRoom(bool fused, bool visible, double pillHeight) =>
            fused && visible ? Size(pillHeight) + GlyphPad + FusedInset : 0;

        /// <summary>The glyph for a state, from the icon font the app already uses.</summary>
        public static string Glyph(DotKind kind) => kind switch
        {
            DotKind.Listening => GlyphListening,
            DotKind.Working => GlyphWorking,
            DotKind.Playing => GlyphPlaying,
            DotKind.Charging => GlyphCharging,
            _ => "",
        };

        /// <summary>The theme brush a state is painted with (the key in Theme.xaml, never a colour of its own).</summary>
        public static string BrushKey(DotKind kind) => kind switch
        {
            DotKind.Listening => "HotBrush",
            DotKind.Working => "AccentBrush",
            DotKind.Playing => "AccentBrush",
            DotKind.Charging => "InfoBrush",
            _ => "MutedBrush",
        };

        /// <summary>What the hover says for a state, in Romanian. Never an app's name: that is the user's business.</summary>
        public static string Label(DotKind kind) => kind switch
        {
            DotKind.Listening => "te ascultă",
            DotKind.Working => "lucrează",
            DotKind.Playing => "cântă",
            DotKind.Charging => "se încarcă",
            _ => "",
        };

        /// <summary>The whole hover line: every state that applies, in priority order, separated by a middle dot.</summary>
        public static string HoverText(ContextSnapshot s, bool working) =>
            string.Join(" · ", All(s, working).Select(Label));
    }
}
