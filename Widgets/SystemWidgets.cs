using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using WinNotch.Services;

namespace WinNotch.Widgets
{
    /// <summary>Line chart of a 0..1 series, drawn into a Canvas that fills its space.</summary>
    internal sealed class Spark : Canvas
    {
        private IReadOnlyList<double> _data = Array.Empty<double>();
        private Brush _stroke;
        private double _limit = double.NaN;     // dashed line (warning threshold), 0..1

        public Spark() { ClipToBounds = true; SizeChanged += (o, e) => Draw(); }

        public void Set(IReadOnlyList<double> data, Brush stroke, double limit = double.NaN)
        {
            _data = data; _stroke = stroke; _limit = limit;
            Draw();
        }

        private void Draw()
        {
            Children.Clear();
            double w = ActualWidth, h = ActualHeight;
            if (w <= 0 || h <= 0 || _data.Count < 2) return;
            if (!double.IsNaN(_limit))
                Children.Add(new Line { X1 = 0, X2 = w, Y1 = h - _limit * (h - 2) - 1, Y2 = h - _limit * (h - 2) - 1, Stroke = Ui.B("DimBrush"), StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 3, 3 } });
            var pl = new Polyline { Stroke = _stroke, StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round };
            int n = _data.Count;
            for (int i = 0; i < n; i++)
            {
                if (double.IsNaN(_data[i])) continue;
                pl.Points.Add(new Point(i / (double)(n - 1) * w, h - 1 - Math.Clamp(_data[i], 0, 1) * (h - 2)));
            }
            Children.Add(pl);
        }
    }

    /// <summary>Procesor: 1×1 % · 2×1 % and temperature · 3×2 with the last minute as a chart.</summary>
    internal sealed class CpuWidget : Widget
    {
        private readonly TextBlock _pct, _temp;
        private readonly Spark _chart;

        public CpuWidget(NotchWindow w, WidgetSlot s) : base(w, s)
        {
            _pct = Big("0%", Tiny ? 20 : 22);
            _temp = Big("", Tiny ? 13 : 20, "OkBrush");
            if (Tiny) Children.Add(Stack(Cap("CPU"), _pct));
            else if (!Tall)
            {
                var g = Ui.Cols(Ui.Star(), Ui.Auto);
                g.Put(Stack(Cap("PROCESOR"), _pct)); g.Put(_temp, 1);
                Children.Add(g);
            }
            else
            {
                _chart = new Spark { Margin = new Thickness(0, 6, 0, 0) };
                var head = Ui.Cols(Ui.Auto, Ui.Px(10), Ui.Auto, Ui.Px(10), Ui.Auto);
                head.Put(Cap("PROCESOR")); head.Put(_pct, 2); head.Put(_temp, 4);
                var g = Ui.Rows(Ui.Auto, Ui.Star());
                g.Put(head); g.Put(_chart, 0, 1);
                Children.Add(g);
            }
            Refresh();
        }

        public override void Refresh()
        {
            _pct.Text = Math.Round(W.Stats.Cpu) + "%";
            var t = W.Temps.Cpu;
            _temp.Text = t != null ? Math.Round(t.Value) + "°" : "";
            _temp.Foreground = t >= 85 ? Ui.B("HotBrush") : t >= 72 ? Ui.B("WarnBrush") : Ui.B("OkBrush");
            _chart?.Set(W.CpuHist.Select(v => v / 100).ToList(), Ui.B("AccentBrush"));
        }
    }

    /// <summary>Memorie: 1×1 % · 2×1 % with a bar and GB.</summary>
    internal sealed class MemoryWidget : Widget
    {
        private readonly TextBlock _pct, _gb;
        private readonly ScaleTransform _bar;

        public MemoryWidget(NotchWindow w, WidgetSlot s) : base(w, s)
        {
            _pct = Big("0%", Tiny ? 20 : 16);
            _gb = Ui.T("", 11.5, "MutedBrush", false, true);
            // "Optimizează": frees the memory apps hold without using it (same as Unelte › Eliberează RAM, with progress)
            var opt = new Button { Style = Ui.S("IconButton"), Width = Tiny ? 24 : 28, Height = Tiny ? 24 : 28, ToolTip = "Optimizează memoria (eliberează RAM-ul ținut degeaba)",
                                   VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(Tiny ? 0 : 6, 0, 0, 0) };
            var optBg = new Border { Width = Tiny ? 24 : 28, Height = Tiny ? 24 : 28, CornerRadius = new CornerRadius(Tiny ? 12 : 14), Child = Ui.Icon("\uE945", Tiny ? 11 : 13) };
            optBg.SetResourceReference(Border.BackgroundProperty, "ChipHoverBrush");
            ((TextBlock)optBg.Child).SetResourceReference(TextBlock.ForegroundProperty, "InfoBrush");
            opt.Content = optBg;
            opt.Click += (o, e) => W.OptimizeMemory();
            if (Tiny)
            {
                var g1 = Stack(Cap("RAM"), _pct);
                opt.HorizontalAlignment = HorizontalAlignment.Right; opt.VerticalAlignment = VerticalAlignment.Top; opt.Margin = new Thickness(0, -2, -4, 0);
                Children.Add(g1);
                Children.Add(opt);
            }
            else
            {
                var bar = Ui.Bar(out _bar, Ui.B("InfoBrush"), 6);
                var head = Ui.Cols(Ui.Auto, Ui.Px(8), Ui.Star());       // the right text shortens itself instead of being cut
                head.Put(Cap("MEMORIE")); head.Put(_gb, 2);
                _gb.HorizontalAlignment = HorizontalAlignment.Right; _gb.TextTrimming = TextTrimming.CharacterEllipsis;
                bar.VerticalAlignment = VerticalAlignment.Center;
                var g = Ui.V(6, head, Ui.Cols(Ui.Star(), Ui.Px(10), Ui.Auto, Ui.Auto).Also(r => { r.Put(bar); r.Put(_pct, 2); r.Put(opt, 3); }));
                g.VerticalAlignment = VerticalAlignment.Center;
                Children.Add(g);
            }
            Refresh();
        }

        public override void Refresh()
        {
            var st = W.Stats;
            double pct = st.RamTotalGb > 0 ? st.RamUsedGb / st.RamTotalGb * 100 : 0;
            _pct.Text = Math.Round(pct) + "%";
            _gb.Text = st.RamUsedGb.ToString("0.0", NotchWindow.Ro) + " / " + Math.Round(st.RamTotalGb) + " GB";
            if (_bar != null) _bar.ScaleX = pct / 100;
        }
    }

    /// <summary>Placă video: temperature and load.</summary>
    internal sealed class GpuWidget : Widget
    {
        private readonly TextBlock _temp, _load;
        public GpuWidget(NotchWindow w, WidgetSlot s) : base(w, s)
        {
            _temp = Big("—", Tiny ? 20 : 22);
            _load = Ui.T("", 11.5, "MutedBrush");
            if (Tiny) Children.Add(Stack(Cap("GPU"), _temp));
            else { var g = Ui.Cols(Ui.Star(), Ui.Auto); g.Put(Stack(Cap("PLACĂ VIDEO"), _load)); g.Put(_temp, 1); Children.Add(g); }
            Refresh();
        }
        public override void Refresh()
        {
            var t = W.Temps;
            _temp.Text = t.Gpu != null ? Math.Round(t.Gpu.Value) + "°" : "—";
            _temp.Foreground = W.HeatBrush(t.Gpu);
            _load.Text = t.GpuLoad != null ? Math.Round(t.GpuLoad.Value) + "% încărcare" : W.S.Temperatures ? "" : "temp. oprite";
        }
    }

    /// <summary>Baterie: % with a bar, charging or time left.</summary>
    internal sealed class BatteryWidget : Widget
    {
        private readonly TextBlock _pct, _sub;
        private readonly ScaleTransform _bar;
        public BatteryWidget(NotchWindow w, WidgetSlot s) : base(w, s)
        {
            _pct = Big("—", Tiny ? 20 : 16);
            _sub = Ui.T("", 11.5, "MutedBrush");
            _sub.TextTrimming = TextTrimming.CharacterEllipsis;
            if (Tiny) Children.Add(Stack(Cap("BATERIE"), _pct));
            else
            {
                var bar = Ui.Bar(out _bar, Ui.B("OkBrush"), 6);
                var head = Ui.Cols(Ui.Auto, Ui.Px(8), Ui.Star());       // the right text shortens itself instead of being cut
                head.Put(Cap("BATERIE")); head.Put(_sub, 2);
                _sub.HorizontalAlignment = HorizontalAlignment.Right; _sub.TextTrimming = TextTrimming.CharacterEllipsis;
                bar.VerticalAlignment = VerticalAlignment.Center;
                Children.Add(Ui.V(6, head, Ui.Cols(Ui.Star(), Ui.Px(10), Ui.Auto).Also(r => { r.Put(bar); r.Put(_pct, 2); })).Also(v => v.VerticalAlignment = VerticalAlignment.Center));
            }
            Refresh();
        }
        public override void Refresh()
        {
            var st = W.Stats;
            if (!st.HasBattery || st.BatteryPercent < 0) { _pct.Text = "—"; _sub.Text = "PC fără baterie"; if (_bar != null) _bar.ScaleX = 0; return; }
            _pct.Text = st.BatteryPercent + "%";
            _pct.Foreground = st.Charging ? Ui.B("OkBrush") : st.BatteryPercent <= 15 ? Ui.B("HotBrush") : Ui.B("InkBrush");
            _sub.Text = st.Charging ? "se încarcă" : st.BatterySecondsLeft > 0 ? st.BatterySecondsLeft / 3600 + " h " + st.BatterySecondsLeft % 3600 / 60 + " min" : "";
            if (_bar != null) _bar.ScaleX = st.BatteryPercent / 100.0;
        }
    }

    /// <summary>Consumă acum: the apps using the most CPU and memory.</summary>
    internal sealed class TopAppsWidget : Widget
    {
        private readonly Grid _list = new Grid();
        public TopAppsWidget(NotchWindow w, WidgetSlot s) : base(w, s)
        {
            Children.Add(Ui.Rows(Ui.Auto, Ui.Px(4), Ui.Star()).Also(g => { g.Put(Cap("CONSUMĂ ACUM · CPU · RAM")); g.Put(_list, 0, 2); }));
            W.Procs.RefreshAsync();
            Refresh();
        }
        public override void Refresh()
        {
            W.Procs.RefreshAsync();
            _list.Children.Clear(); _list.RowDefinitions.Clear(); _list.ColumnDefinitions.Clear();
            var top = W.Procs.Top.Take(Ch >= 2 ? 4 : 3).ToList();
            if (top.Count == 0) { _list.Children.Add(Ui.T("se calculează…", 11.5, "DimBrush")); return; }
            bool horizontal = Ch == 1;
            for (int i = 0; i < top.Count; i++)
            {
                var p = top[i];
                var g = Ui.Cols(Ui.Star(), Ui.Px(6), Ui.Auto, Ui.Px(6), Ui.Auto);
                g.Put(Ui.T(p.Name, 12));
                g.Put(Ui.T(Math.Round(p.Cpu) + "%", 11.5, "InkBrush", false, true), 2);
                g.Put(Ui.T(p.RamBytes >= 1L << 30 ? (p.RamBytes / (double)(1L << 30)).ToString("0.0", NotchWindow.Ro) + " GB" : p.RamBytes / (1 << 20) + " MB", 11.5, "DimBrush", false, true), 4);
                if (horizontal) { _list.ColumnDefinitions.Add(new ColumnDefinition { Width = Ui.Star() }); g.Margin = new Thickness(i == 0 ? 0 : 12, 0, 0, 0); Grid.SetColumn(g, i); }
                else { _list.RowDefinitions.Add(new RowDefinition { Height = Ui.Auto }); g.Margin = new Thickness(0, 0, 0, 3); Grid.SetRow(g, i); }
                _list.Children.Add(g);
            }
        }
    }

    /// <summary>Internet: 2×1 speed now · 3×2 bars against the measured maximum, ping and the speed test.</summary>
    internal sealed class InternetWidget : Widget
    {
        private readonly TextBlock _down, _up, _info;
        private readonly ScaleTransform _dBar, _uBar;
        public InternetWidget(NotchWindow w, WidgetSlot s) : base(w, s)
        {
            _down = Ui.T("↓ 0", 13, "InkBrush", true, true);
            _up = Ui.T("↑ 0", 13, "MutedBrush", false, true);
            _info = Ui.T("", 11.5, "MutedBrush");
            if (!Tall)
            {
                Children.Add(Stack(Cap("INTERNET"), Ui.H(12, _down, _up)));
                return;
            }
            var d = Ui.Bar(out _dBar, Ui.B("InfoBrush"), 6);
            var u = Ui.Bar(out _uBar, Ui.Rgb(0xA7, 0x8B, 0xFA), 6);
            var rows = Ui.Cols(Ui.Px(78), Ui.Star());
            rows.RowDefinitions.Add(new RowDefinition { Height = Ui.Px(22) });
            rows.RowDefinitions.Add(new RowDefinition { Height = Ui.Px(22) });
            rows.Put(_down); rows.Put(d, 1); rows.Put(_up, 0, 1); rows.Put(u, 1, 1);
            var test = Ui.PillBtn("Test viteză", () => W.OpenSpeedTest(), true);
            var head = Ui.Cols(Ui.Auto, Ui.Star(), Ui.Auto);
            head.Put(Cap("INTERNET")); head.Put(test, 2);
            Children.Add(Ui.Rows(Ui.Auto, Ui.Px(6), Ui.Auto, Ui.Star()).Also(g => { g.Put(head); g.Put(rows, 0, 2); _info.VerticalAlignment = VerticalAlignment.Bottom; g.Put(_info, 0, 3); }));
            Refresh();
        }
        private static string M(double v) => v >= 100 ? Math.Round(v).ToString() : v.ToString("0.0", NotchWindow.Ro);
        public override void Refresh()
        {
            var st = W.Stats;
            _down.Text = "↓ " + M(st.NetDownMbps);
            _up.Text = "↑ " + M(st.NetUpMbps);
            if (_dBar == null) return;
            _dBar.ScaleX = W.S.SpeedDownMbps > 0 ? Math.Clamp(st.NetDownMbps / W.S.SpeedDownMbps, 0, 1) : 0;
            _uBar.ScaleX = W.S.SpeedUpMbps > 0 ? Math.Clamp(st.NetUpMbps / W.S.SpeedUpMbps, 0, 1) : 0;
            var last = W.S.SpeedHistory?.LastOrDefault(h => h.Ok);
            _info.Text = last != null ? "maxim " + Math.Round(W.S.SpeedDownMbps) + " / " + Math.Round(W.S.SpeedUpMbps) + " Mb/s · router " + (last.Router < 0 ? "—" : last.Router + " ms")
                                      : "fă un test ca să afli viteza maximă";
        }
    }

    /// <summary>
    /// Senzor (personalizat): any value as a number, bar or chart, with its own title, color and warning threshold.
    /// </summary>
    internal sealed class SensorWidget : Widget
    {
        internal static readonly (string Value, string Label)[] Metrics =
        {
            ("cpu", "Procesor · încărcare (%)"), ("cputemp", "Procesor · temperatură (°C)"), ("gputemp", "Placă video · temperatură (°C)"),
            ("gpuload", "Placă video · încărcare (%)"), ("ssdtemp", "SSD · temperatură (°C)"), ("ram", "Memorie folosită (%)"),
            ("down", "Internet · descărcare (Mb/s)"), ("up", "Internet · încărcare (Mb/s)"), ("battery", "Baterie (%)"), ("volume", "Volum (%)")
        };

        private readonly string _metric, _display;
        private readonly double _warn, _max;
        private readonly TextBlock _value;
        private readonly ScaleTransform _bar;
        private readonly Spark _chart;
        private readonly List<double> _hist = new List<double>();
        private readonly Brush _color;

        public SensorWidget(NotchWindow w, WidgetSlot s) : base(w, s)
        {
            _metric = s.Opt("metric", "gputemp");
            _display = Tiny ? "number" : s.Opt("display", Tall ? "chart" : "bar");
            _warn = double.TryParse(s.Opt("warn", ""), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var wv) ? wv : double.NaN;
            _max = _metric is "down" or "up" ? 0 : _metric.EndsWith("temp") ? 100 : 100;
            _color = TryBrush(s.Opt("color", "")) ?? Ui.B("AccentBrush");
            string title = s.Opt("title", "");
            if (string.IsNullOrWhiteSpace(title)) title = Metrics.FirstOrDefault(m => m.Value == _metric).Label?.Split('(')[0].Trim() ?? "Senzor";
            _value = Big("—", Tiny ? 20 : 20);
            if (Tiny) { Children.Add(Stack(Cap(title.ToUpperInvariant()), _value)); Refresh(); return; }
            var head = Ui.Cols(Ui.Star(), Ui.Auto);
            head.Put(Cap(title.ToUpperInvariant())); head.Put(_value, 1);
            if (_display == "number") Children.Add(Stack(head));
            else if (_display == "chart" && Tall)
            {
                _chart = new Spark { Margin = new Thickness(0, 6, 0, 0) };
                Children.Add(Ui.Rows(Ui.Auto, Ui.Star()).Also(g => { g.Put(head); g.Put(_chart, 0, 1); }));
            }
            else
            {
                var bar = Ui.Bar(out _bar, _color, 6);
                Children.Add(Ui.V(6, head, bar).Also(v => v.VerticalAlignment = VerticalAlignment.Center));
            }
            Refresh();
        }

        private static Brush TryBrush(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) return null;
            try { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); b.Freeze(); return b; } catch { return null; }
        }

        private double? Read()
        {
            var st = W.Stats; var t = W.Temps;
            switch (_metric)
            {
                case "cpu": return st.Cpu;
                case "cputemp": return t.Cpu;
                case "gputemp": return t.Gpu;
                case "gpuload": return t.GpuLoad;
                case "ssdtemp": return t.Ssd;
                case "ram": return st.RamTotalGb > 0 ? st.RamUsedGb / st.RamTotalGb * 100 : (double?)null;
                case "down": return st.NetDownMbps;
                case "up": return st.NetUpMbps;
                case "battery": return st.HasBattery && st.BatteryPercent >= 0 ? st.BatteryPercent : (double?)null;
                case "volume": return W.Audio.Volume;
                default: return null;
            }
        }

        public override void Refresh()
        {
            var v = Read();
            string unit = _metric.EndsWith("temp") ? "°" : _metric is "down" or "up" ? "" : "%";
            _value.Text = v == null ? "—" : (v.Value >= 100 || unit != "" ? Math.Round(v.Value).ToString() : v.Value.ToString("0.0", NotchWindow.Ro)) + unit;
            bool hot = v != null && !double.IsNaN(_warn) && v.Value >= _warn;
            _value.Foreground = hot ? Ui.B("WarnBrush") : _display == "number" ? _color : Ui.B("InkBrush");
            double max = _max > 0 ? _max : Math.Max(1, Math.Max(_metric == "down" ? W.S.SpeedDownMbps : W.S.SpeedUpMbps, _hist.DefaultIfEmpty(0).Max()));
            double frac = v == null ? 0 : Math.Clamp(v.Value / max, 0, 1);
            if (_bar != null) _bar.ScaleX = frac;
            if (_chart != null)
            {
                _hist.Add(v ?? double.NaN);
                if (_hist.Count > 60) _hist.RemoveAt(0);
                _chart.Set(_hist.Select(x => double.IsNaN(x) ? double.NaN : Math.Clamp(x / max, 0, 1)).ToList(), hot ? Ui.B("WarnBrush") : _color, double.IsNaN(_warn) ? double.NaN : Math.Clamp(_warn / max, 0, 1));
            }
        }
    }
}
