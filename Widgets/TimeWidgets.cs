using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using WinNotch.Services;

namespace WinNotch.Widgets
{
    /// <summary>Ceas: 1×1 the time · 2×1 time and date · 2×2 large, with seconds, date and week.</summary>
    internal sealed class ClockWidget : Widget
    {
        private readonly TextBlock _time, _sub, _sec;

        public ClockWidget(NotchWindow w, WidgetSlot s) : base(w, s)
        {
            _time = Big("--:--", Tall ? 38 : Tiny ? 20 : 28);
            _sub = Ui.T("", Tall ? 12.5 : 11.5, "MutedBrush");
            _sec = Ui.T("", 14, "DimBrush", false, true);
            if (Tall)
            {
                var top = Ui.H(4, _time, _sec);
                _sec.VerticalAlignment = VerticalAlignment.Bottom;
                _sec.Margin = new Thickness(4, 0, 0, 7);
                Children.Add(Stack(top, _sub));
            }
            else Children.Add(Tiny ? (UIElement)Stack(_time) : Stack(_time, _sub));
            Refresh();
        }

        public override void Refresh()
        {
            var now = DateTime.Now;
            _time.Text = now.ToString("HH:mm");
            _sec.Text = now.ToString("ss");
            string date = NotchWindow.Ro.TextInfo.ToTitleCase(now.ToString(Tall ? "dddd, d MMMM" : "ddd, d MMM", NotchWindow.Ro));
            int week = ISOWeek.GetWeekOfYear(now);
            _sub.Text = Tall ? date + "\nsăptămâna " + week : date;
        }
    }

    /// <summary>Calendar: 2×1 the next event · 3×2 the next ones. From the iCal link in Settings.</summary>
    internal sealed class CalendarWidget : Widget
    {
        private readonly StackPanel _list = new StackPanel();
        private string _sig;

        public CalendarWidget(NotchWindow w, WidgetSlot s) : base(w, s)
        {
            Children.Add(Tall ? (UIElement)Ui.V(6, Cap("URMĂTOARELE"), _list) : _list);
            _list.VerticalAlignment = Tall ? VerticalAlignment.Top : VerticalAlignment.Center;
            Refresh();
        }

        public override void Refresh()
        {
            bool linked = !string.IsNullOrWhiteSpace(W.S.CalendarIcs);
            var evs = W.Events.Take(Tall ? Ch + 1 : 1).ToList();
            string sig = linked + string.Join("|", evs.Select(e => e.Title + e.Start.Ticks)) + DateTime.Now.ToString("HHmm");
            if (sig == _sig) return;
            _sig = sig;
            _list.Children.Clear();
            if (!linked) { _list.Children.Add(Row("Leagă calendarul", "Setări › Calendar", Ui.B("DimBrush")).OnClick(() => ((App)Application.Current).OpenSettings())); return; }
            if (evs.Count == 0) { _list.Children.Add(Ui.T("Nimic în următoarele 7 zile", 12, "MutedBrush")); return; }
            var colors = new[] { "InfoBrush", "AccentBrush", "OkBrush", "WarnBrush" };
            for (int i = 0; i < evs.Count; i++) _list.Children.Add(Row(evs[i].Title, When(evs[i]), Ui.B(colors[i % colors.Length])));
        }

        private static FrameworkElement Row(string title, string when, System.Windows.Media.Brush color)
        {
            var g = Ui.Cols(Ui.Px(3), Ui.Px(8), Ui.Star());
            g.Put(new Border { Width = 3, CornerRadius = new CornerRadius(2), Background = color });
            g.Put(Ui.V(0, Ui.T(title, 12.5, "InkBrush", true), Ui.T(when, 11.5, "MutedBrush")), 2);
            g.Margin = new Thickness(0, 0, 0, 6);
            return g;
        }

        private static string When(CalendarEvent ev)
        {
            var d = ev.Start.Date;
            string day = d == DateTime.Today ? "azi" : d == DateTime.Today.AddDays(1) ? "mâine" : ev.Start.ToString("dddd", NotchWindow.Ro);
            if (ev.AllDay) return day + ", toată ziua";
            var left = ev.Start - DateTime.Now;
            string inn = left.TotalMinutes > 0 && left.TotalHours < 3 ? " · în " + (left.TotalMinutes < 60 ? (int)left.TotalMinutes + " min" : (int)left.TotalHours + " h " + left.Minutes + " min") : "";
            return day + ", " + ev.Start.ToString("HH:mm") + inn;
        }
    }

    /// <summary>Vremea: 2×1 now · 2×2 / 3×2 plus the next hours.</summary>
    internal sealed class WeatherWidget : Widget
    {
        private readonly TextBlock _glyph, _temp, _place;
        private readonly UniformGrid _hours;
        private string _sig;

        public WeatherWidget(NotchWindow w, WidgetSlot s) : base(w, s)
        {
            _glyph = Ui.Icon(Ui.GSun, Tiny ? 16 : 20);
            _temp = Big("—", Tiny ? 18 : 22);
            _place = Ui.T("", 11.5, "MutedBrush");
            var top = Ui.Cols(Ui.Auto, Ui.Auto, Ui.Star());
            top.Put(_glyph); _temp.Margin = new Thickness(7, 0, 7, 0); top.Put(_temp, 1); top.Put(_place, 2);
            if (!Tall) { Children.Add(top); top.VerticalAlignment = VerticalAlignment.Center; }
            else
            {
                _hours = new UniformGrid { Rows = 1, Columns = Cw >= 3 ? 6 : 4, VerticalAlignment = VerticalAlignment.Bottom };
                var g = Ui.Rows(Ui.Auto, Ui.Star());
                g.Put(top); g.Put(_hours, 0, 1);
                Children.Add(g);
            }
            Refresh();
        }

        public override void Refresh()
        {
            var wx = W.Weather;
            _glyph.Text = W.WeatherGlyph();
            _glyph.Foreground = W.WeatherBrush();
            _temp.Text = wx.Ok ? Math.Round(wx.Temp) + "°" : "—";
            _place.Text = Tiny ? "" : W.S.City + "\n" + (wx.Ok ? Math.Round(wx.Min) + "° / " + Math.Round(wx.Max) + "°" : "fără date");
            if (_hours == null) return;
            string sig = string.Join(",", wx.Hours.Select(h => h.Time.ToString("HH") + Math.Round(h.Temp)));
            if (sig == _sig) return;
            _sig = sig;
            _hours.Children.Clear();
            foreach (var (t, temp) in wx.Hours.Take(_hours.Columns))
            {
                var box = Ui.V(1, Ui.T(t.ToString("HH"), 11, "DimBrush"), Ui.T(Math.Round(temp) + "°", 12));
                foreach (UIElement c in box.Children) ((TextBlock)c).HorizontalAlignment = HorizontalAlignment.Center;
                _hours.Children.Add(box);
            }
        }
    }

    /// <summary>Ceas alt oraș: the time in another time zone (option: zone, label).</summary>
    internal sealed class WorldClockWidget : Widget
    {
        private readonly TextBlock _time, _label;
        private readonly TimeZoneInfo _tz;

        public WorldClockWidget(NotchWindow w, WidgetSlot s) : base(w, s)
        {
            _tz = FindZone(s.Opt("zone", "America/New_York"));
            string label = s.Opt("label", "");
            if (string.IsNullOrWhiteSpace(label)) label = _tz?.Id.Split('/').Last().Replace('_', ' ') ?? "Fus orar necunoscut";
            _time = Big("--:--", Tiny ? 20 : 24);
            _label = Ui.T(label, 11.5, "MutedBrush");
            Children.Add(Stack(Cap(label.ToUpperInvariant()), _time, Tiny ? null : _label));
            _label.Text = "";
            Refresh();
        }

        internal static TimeZoneInfo FindZone(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            if (TimeZoneInfo.TryFindSystemTimeZoneById(id.Trim(), out var tz)) return tz;
            if (TimeZoneInfo.TryConvertIanaIdToWindowsId(id.Trim(), out var win) && TimeZoneInfo.TryFindSystemTimeZoneById(win, out tz)) return tz;
            return null;
        }

        public override void Refresh()
        {
            if (_tz == null) { _time.Text = "?"; return; }
            var t = TimeZoneInfo.ConvertTime(DateTime.Now, _tz);
            _time.Text = t.ToString("HH:mm");
            if (!Tiny)
            {
                var diff = _tz.GetUtcOffset(DateTime.UtcNow) - TimeZoneInfo.Local.GetUtcOffset(DateTime.UtcNow);
                string d = t.Date == DateTime.Today ? "azi" : t.Date > DateTime.Today ? "mâine" : "ieri";
                _label.Text = d + ", " + (diff >= TimeSpan.Zero ? "+" : "−") + Math.Abs(diff.TotalHours).ToString("0.#", CultureInfo.InvariantCulture) + " h";
            }
        }
    }
}
