using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace WinNotch
{
    /// <summary>Small builders so the panels can be written in code with the theme from Theme.xaml.</summary>
    internal static class Ui
    {
        // Segoe Fluent Icons / MDL2 glyphs
        public const string GPlay = "", GPause = "", GPrev = "", GNext = "", GVol = "", GMute = "";
        public const string GBolt = "", GBattery = "", GBatteryLow = "", GWarn = "", GClock = "";
        public const string GSun = "", GCloud = "", GMoon = "", GMusic = "", GSettings = "";
        public const string GSearch = "", GCopy = "", GPin = "", GUnpin = "", GBack = "";
        public const string GMic = "", GCam = "", GHeadphones = "", GMouse = "", GKeyboard = "";
        public const string GGamepad = "", GPhone = "", GMonitor = "", GDrive = "", GPrinter = "";
        public const string GBluetooth = "", GUsb = "", GEye = "", GHome = "", GSystem = "";
        public const string GDevices = "", GTools = "", GApp = "", GFolder = "", GCalc = "";
        public const string GCamera = "", GCrop = "", GText = "", GMemory = "";

        public static Brush B(string key) => (Brush)Application.Current.FindResource(key);
        public static FontFamily Mono => (FontFamily)Application.Current.FindResource("MonoFont");
        public static FontFamily IconFont => (FontFamily)Application.Current.FindResource("IconFont");
        public static Style S(string key) => (Style)Application.Current.FindResource(key);

        /// <summary>A frozen (immutable, cheaper to render) brush.</summary>
        public static SolidColorBrush Rgb(byte r, byte g, byte b, byte a = 255)
        {
            var br = new SolidColorBrush(Color.FromArgb(a, r, g, b));
            br.Freeze();
            return br;
        }

        public static TextBlock T(string text, double size = 12, string brush = "InkBrush", bool bold = false, bool mono = false)
        {
            if (size < 11) size = 11;         // smaller text is hard to read on the transparent window
            var t = new TextBlock
            {
                Text = text ?? "", FontSize = size, Foreground = B(brush),
                TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center
            };
            if (bold) t.FontWeight = FontWeights.SemiBold;
            if (mono) t.FontFamily = Mono;
            return t;
        }

        public static TextBlock Cap(string text) => new TextBlock
        {
            Text = text, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = B("DimBrush"),
            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis
        };

        public static TextBlock Icon(string glyph, double size = 14, Brush brush = null) => new TextBlock
        {
            Text = glyph, FontFamily = IconFont, FontSize = size, Foreground = brush ?? B("InkBrush"),
            VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center
        };

        public static Border Card(UIElement child, double padH = 12, double padV = 10, double radius = 16) => new Border
        {
            Background = B("ChipBrush"), CornerRadius = new CornerRadius(radius),
            Padding = new Thickness(padH, padV, padH, padV), Child = child, ClipToBounds = true
        };

        public static Border Chip(UIElement child, double padH = 8, double padV = 4, double radius = 10) => new Border
        {
            Background = B("ChipHoverBrush"), CornerRadius = new CornerRadius(radius),
            Padding = new Thickness(padH, padV, padH, padV), Child = child
        };

        public static Button IconBtn(string glyph, Action click, string tip = null, double size = 30, double glyphSize = 14, Brush fg = null)
        {
            var b = new Button { Style = S("IconButton"), Width = size, Height = size, Content = Icon(glyph, glyphSize, fg ?? B("InkBrush")) };
            if (tip != null) b.ToolTip = tip;
            if (click != null) b.Click += (o, e) => click();
            return b;
        }

        public static Button PillBtn(string text, Action click, bool accent = false)
        {
            var b = new Button { Style = S(accent ? "AccentPill" : "GhostPill"), Content = text };
            if (click != null) b.Click += (o, e) => click();
            return b;
        }

        public static Slider MiniSlider() => new Slider { Style = S("MiniSlider") };

        /// <summary>Thin progress bar; set fill.ScaleX to 0..1.</summary>
        public static Grid Bar(out ScaleTransform fill, Brush fillBrush, double height = 4)
        {
            var g = new Grid { Height = height, VerticalAlignment = VerticalAlignment.Center };
            g.Children.Add(new Border { Background = B("TrackBrush"), CornerRadius = new CornerRadius(height / 2) });
            fill = new ScaleTransform(0, 1);
            g.Children.Add(new Border { Background = fillBrush, CornerRadius = new CornerRadius(height / 2), RenderTransform = fill });
            return g;
        }

        public static Grid Cols(params GridLength[] cols)
        {
            var g = new Grid();
            foreach (var c in cols) g.ColumnDefinitions.Add(new ColumnDefinition { Width = c });
            return g;
        }

        public static Grid Rows(params GridLength[] rows)
        {
            var g = new Grid();
            foreach (var r in rows) g.RowDefinitions.Add(new RowDefinition { Height = r });
            return g;
        }

        public static TE Put<TE>(this Grid g, TE e, int col = 0, int row = 0, int colSpan = 1) where TE : UIElement
        {
            Grid.SetColumn(e, col);
            Grid.SetRow(e, row);
            if (colSpan > 1) Grid.SetColumnSpan(e, colSpan);
            g.Children.Add(e);
            return e;
        }

        public static GridLength Star(double v = 1) => new GridLength(v, GridUnitType.Star);
        public static GridLength Px(double v) => new GridLength(v);
        public static GridLength Auto => GridLength.Auto;

        public static StackPanel H(double gap, params UIElement[] kids)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            for (int i = 0; i < kids.Length; i++)
            {
                if (kids[i] == null) continue;
                if (i > 0 && kids[i] is FrameworkElement fe) fe.Margin = new Thickness(gap, fe.Margin.Top, fe.Margin.Right, fe.Margin.Bottom);
                sp.Children.Add(kids[i]);
            }
            return sp;
        }

        public static StackPanel V(double gap, params UIElement[] kids)
        {
            var sp = new StackPanel { Orientation = Orientation.Vertical };
            for (int i = 0; i < kids.Length; i++)
            {
                if (kids[i] == null) continue;
                if (i > 0 && kids[i] is FrameworkElement fe) fe.Margin = new Thickness(fe.Margin.Left, gap, fe.Margin.Right, fe.Margin.Bottom);
                sp.Children.Add(kids[i]);
            }
            return sp;
        }

        /// <summary>Colored rounded square with a letter, used as an app icon.</summary>
        public static Border AppBadge(string name, double size = 22)
        {
            return new Border
            {
                Width = size, Height = size, CornerRadius = new CornerRadius(size * 0.28),
                Background = AppColor(name),
                Child = new TextBlock
                {
                    Text = string.IsNullOrEmpty(name) ? "?" : name.Substring(0, 1).ToUpperInvariant(),
                    FontSize = size * 0.46, FontWeight = FontWeights.Bold, Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                }
            };
        }

        public static Brush AppColor(string name)
        {
            string n = (name ?? "").ToLowerInvariant();
            if (n.Contains("spotify")) return Rgb(0x1d, 0xb9, 0x54);
            if (n.Contains("youtube")) return Rgb(0xff, 0x00, 0x33);
            if (n.Contains("chrome")) return Rgb(0x42, 0x85, 0xf4);
            if (n.Contains("edge")) return Rgb(0x00, 0x78, 0xd4);
            if (n.Contains("firefox")) return Rgb(0xff, 0x71, 0x39);
            if (n.Contains("discord")) return Rgb(0x58, 0x65, 0xf2);
            if (n.Contains("teams")) return Rgb(0x62, 0x64, 0xa7);
            if (n.Contains("netflix")) return Rgb(0xe5, 0x09, 0x14);
            if (n.Contains("steam")) return Rgb(0x1b, 0x28, 0x38);
            if (n.Contains("vlc")) return Rgb(0xff, 0x88, 0x00);
            return Rgb(0x3a, 0x3e, 0x46);
        }

        /// <summary>
        /// Runs a looping animation only while <paramref name="owner"/> is actually on screen: it stops when the element is
        /// hidden, collapsed or removed, so forever-animations don't keep the app rendering in the background.
        /// </summary>
        public static void Loop(FrameworkElement owner, System.Windows.Media.Animation.IAnimatable target, DependencyProperty prop, System.Windows.Media.Animation.AnimationTimeline anim)
        {
            void Sync()
            {
                if (owner.IsVisible) target.BeginAnimation(prop, anim);
                else target.BeginAnimation(prop, null);
            }
            owner.IsVisibleChanged += (o, e) => Sync();
            owner.Unloaded += (o, e) => target.BeginAnimation(prop, null);
            Sync();
        }

        /// <summary>Makes any element clickable (hand cursor + left click).</summary>
        public static TE OnClick<TE>(this TE e, Action a) where TE : FrameworkElement
        {
            e.Cursor = Cursors.Hand;
            e.MouseLeftButtonUp += (o, ev) => { a(); ev.Handled = true; };
            return e;
        }

        public static string Ago(DateTime t)
        {
            var d = DateTime.Now - t;
            if (d.TotalMinutes < 1) return "acum";
            if (d.TotalMinutes < 60) return (int)d.TotalMinutes + " min";
            if (d.TotalHours < 24) return (int)d.TotalHours + " h";
            return (int)d.TotalDays + " z";
        }

        public static string Fmt(TimeSpan t)
        {
            if (t < TimeSpan.Zero) t = TimeSpan.Zero;
            return (int)t.TotalMinutes + ":" + t.Seconds.ToString("00");
        }
    }
}
