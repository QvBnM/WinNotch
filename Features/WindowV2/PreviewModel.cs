using System;
using System.Globalization;

namespace WinNotch.Features.WindowV2
{
    /// <summary>Where the preview pill sits inside the ghost monitor, in device-independent pixels.</summary>
    public readonly struct PillBox
    {
        public PillBox(double x, double width, double height)
        {
            X = x; Width = width; Height = height;
        }

        public double X { get; }
        public double Width { get; }
        public double Height { get; }
    }

    /// <summary>
    /// P70: the maths behind the window's signature — the little monitor in the header with a live notch on it, which
    /// shows what the setting under the mouse will actually do. Pure (no WPF) so every number is tested: the drawing
    /// is in <see cref="NotchPreview"/> and owns no rule of its own.
    /// <para>Nothing here reads settings or the screen. The caller passes the position ("left" / "center" / "right",
    /// the values <c>AppSettings.Position</c> already uses) and whether the pill is shrunk, and gets back a box.</para>
    /// </summary>
    public static class PreviewModel
    {
        /// <summary>The ghost monitor: a slice of the top of a screen, wide enough for the three positions to differ.</summary>
        public const double FrameWidth = 560, FrameHeight = 96;

        /// <summary>How far from the monitor's edge a side-hugging pill stops.</summary>
        public const double Margin = 20;

        /// <summary>The open pill, and the small one it shrinks to.</summary>
        public const double FullWidth = 300, FullHeight = 34, MiniWidth = 148, MiniHeight = 24;

        /// <summary>The pill's box for a position, at the preview's own scale. Unknown positions read as centred.</summary>
        public static PillBox Pill(string position, bool mini, double frameWidth = FrameWidth)
        {
            double frame = frameWidth > 0 ? frameWidth : FrameWidth;
            double w = mini ? MiniWidth : FullWidth;
            double h = mini ? MiniHeight : FullHeight;
            // Never wider than the monitor it sits on, and never past its margins.
            double max = Math.Max(40, frame - 2 * Margin);
            if (w > max) w = max;
            double x;
            switch (Normalize(position))
            {
                case "left": x = Margin; break;
                case "right": x = frame - w - Margin; break;
                default: x = (frame - w) / 2; break;
            }
            if (x < Margin) x = Margin;
            double right = frame - w - Margin;
            if (x > right) x = right;
            return new PillBox(x, w, h);
        }

        /// <summary>"left" / "center" / "right"; anything else (including null) is "center".</summary>
        public static string Normalize(string position)
        {
            string p = (position ?? "").Trim().ToLowerInvariant();
            return p == "left" || p == "right" ? p : "center";
        }

        /// <summary>The position written for a person, as it appears in the footer.</summary>
        public static string PositionName(string position) => Normalize(position) switch
        {
            "left" => "stânga",
            "right" => "dreapta",
            _ => "centru",
        };

        /// <summary>
        /// The footer's line after a change: "poziție → centru". The setting's name and the new value come from the
        /// caller, so nothing here has to know what exists in Settings.
        /// </summary>
        public static string LastChange(string setting, string value)
        {
            string s = (setting ?? "").Trim();
            string v = (value ?? "").Trim();
            if (s.Length == 0) return "";
            return v.Length == 0 ? s : s + " → " + v;
        }

        /// <summary>How many of the page's four rows are used, for the meter under the page editor.</summary>
        public static string RowsLine(int used, int max)
        {
            if (max <= 0) return "";
            int u = Math.Clamp(used, 0, max);
            return u == 1
                ? "1 rând din " + max.ToString(CultureInfo.InvariantCulture) + " folosit"
                : u.ToString(CultureInfo.InvariantCulture) + " rânduri din " + max.ToString(CultureInfo.InvariantCulture) + " folosite";
        }

        /// <summary>A number the way the window writes numbers: "400 ms", "10 s", "80%".</summary>
        public static string Value(double number, string unit)
        {
            string n = Math.Abs(number % 1) < 0.001
                ? ((long)Math.Round(number)).ToString(CultureInfo.InvariantCulture)
                : number.ToString("0.##", CultureInfo.InvariantCulture);
            if (string.IsNullOrEmpty(unit)) return n;
            return unit == "%" ? n + unit : n + " " + unit;
        }
    }
}
