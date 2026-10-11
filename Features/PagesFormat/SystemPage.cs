using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using WinNotch.Panes;
using WinNotch.Services;

namespace WinNotch.Features.PagesFormat
{
    /// <summary>
    /// P75, „Sistem”, after `docs/design/03-pagina-sistem.png`: the processor with its graph fills the left column, the
    /// internet and what is eating the machine stand in the right one. Two accents, each with one meaning — amber for
    /// what is moving right now, ice for cold data (temperatures, the processes' share).
    ///
    /// The old <see cref="SystemPane"/> stays exactly as it is and is what the notch shows while the switch is off.
    /// </summary>
    internal sealed class SystemPage : Pane
    {
        public override double PanelHeight => PageFormat.SystemHeight;

        private readonly TextBlock _cpu, _cpuTemp, _ram, _ramSub, _gpu, _gpuSub, _bat, _batWord, _ping, _down, _up, _speedTest;
        private readonly ScaleTransform _ramBar, _gpuBar, _batBar, _downBar, _upBar;
        private readonly Canvas _chart;
        private readonly StackPanel _top;
        private string _conn = "";
        private DateTime _connAt = DateTime.MinValue;

        public SystemPage(NotchWindow w) : base(w)
        {
            var cols = Deck.Columns(PageFormat.SystemSplit, out var left, out var right);
            Children.Add(cols);

            // ---------- left: the processor, then memory / graphics, then the battery ----------
            _cpu = Deck.T(Ink.Big, "—");
            _cpuTemp = Deck.T(Ink.Value, "—", PageFormat.Reading.Cold);
            var cpuHead = new Grid();
            cpuHead.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            cpuHead.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            cpuHead.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            cpuHead.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var eyebrow = Deck.T(Ink.Eyebrow, "Procesor");
            cpuHead.Children.Add(eyebrow);
            _cpu.Margin = new Thickness(10, 0, 0, 0);
            Grid.SetColumn(_cpu, 1);
            cpuHead.Children.Add(_cpu);
            Grid.SetColumn(_cpuTemp, 3);
            cpuHead.Children.Add(_cpuTemp);

            _chart = new Canvas { Height = 60, Margin = new Thickness(0, 8, 0, 0), ClipToBounds = true };
            _chart.SizeChanged += (o, e) => DrawChart();

            var legend = new Grid();
            legend.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            legend.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            legend.Margin = new Thickness(0, 7, 0, 0);
            legend.Children.Add(Deck.Row(14, Key("AccentBrush", "încărcare", false), Key("InfoBrush", "temperatură", true)));
            var last = Deck.T(Ink.Eyebrow, "Ultimul minut");
            last.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(last, 1);
            legend.Children.Add(last);

            var cpuCard = Deck.Card(Deck.Stack(0, cpuHead, _chart, legend));

            _ram = Deck.T(Ink.Medium, "—");
            _ramSub = Deck.T(Ink.Note, "");
            _gpu = Deck.T(Ink.Medium, "—");
            _gpuSub = Deck.T(Ink.Note, "", PageFormat.Reading.Cold);
            var ramCard = Deck.Card(Deck.Stack(8, Deck.T(Ink.Eyebrow, "Memorie"),
                                               Deck.Row(9, _ram, _ramSub), Deck.Bar(out _ramBar)));
            var gpuCard = Deck.Card(Deck.Stack(8, Deck.T(Ink.Eyebrow, "Placă video"),
                                               Deck.Row(9, _gpu, _gpuSub), Deck.Bar(out _gpuBar, "InfoBrush")));
            var pair = new Grid { Margin = new Thickness(0, PageFormat.CardGap, 0, 0) };
            pair.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            pair.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(PageFormat.CardGap) });
            pair.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            pair.Children.Add(ramCard);
            Grid.SetColumn(gpuCard, 2);
            pair.Children.Add(gpuCard);

            _bat = Deck.T(Ink.Value, "—");
            _batWord = Deck.T(Ink.Label, "");
            var batGrid = new Grid();
            batGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            batGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            batGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            batGrid.Children.Add(Deck.T(Ink.Eyebrow, "Baterie"));
            var batBar = Deck.Bar(out _batBar);
            batBar.Margin = new Thickness(14, 0, 14, 0);
            Grid.SetColumn(batBar, 1);
            batGrid.Children.Add(batBar);
            var batRight = Deck.Row(7, _bat, _batWord);
            Grid.SetColumn(batRight, 2);
            batGrid.Children.Add(batRight);
            var batCard = Deck.Card(batGrid);
            batCard.Padding = new Thickness(PageFormat.CardPadH, 7, PageFormat.CardPadH, 7);
            batCard.Margin = new Thickness(0, PageFormat.CardGap, 0, 0);

            var leftStack = new Grid();
            leftStack.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            leftStack.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            leftStack.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            leftStack.Children.Add(cpuCard);
            Grid.SetRow(pair, 1);
            leftStack.Children.Add(pair);
            Grid.SetRow(batCard, 2);
            leftStack.Children.Add(batCard);
            left.Children.Add(leftStack);

            // ---------- right: the internet, then who is eating the machine ----------
            _ping = Deck.T(Ink.Value, "—");
            var netHead = Deck.Head("Internet", _ping);
            var downRow = Line("descărcare", out _down, out _downBar);
            var upRow = Line("încărcare", out _up, out _upBar);
            _speedTest = Deck.T(Ink.Value, "—");
            var testChip = Deck.Chip(Deck.Row(9, Deck.T(Ink.Eyebrow, "Test"), _speedTest), 10, 5);
            testChip.HorizontalAlignment = HorizontalAlignment.Stretch;
            testChip.Margin = new Thickness(0, 12, 0, 0);
            // Rămas de legat: pornirea testului trece azi prin pagina veche (OpenSpeedTest arată _system), deci un
            // click de aici ar sări înapoi la ea. Chip-ul arată deocamdată doar ultimul rezultat.
            var netCard = Deck.Card(Deck.Stack(0, netHead, Pad(downRow, 12), Pad(upRow, 10), testChip));

            _top = new StackPanel { Margin = new Thickness(0, 7, 0, 0) };
            var topCard = Deck.Card(Deck.Stack(0, Deck.T(Ink.Eyebrow, "Consumă acum"), _top));
            topCard.Margin = new Thickness(0, PageFormat.CardGap, 0, 0);

            var rightStack = new Grid();
            rightStack.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            rightStack.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            rightStack.Children.Add(netCard);
            Grid.SetRow(topCard, 1);
            rightStack.Children.Add(topCard);
            right.Children.Add(rightStack);
        }

        private static FrameworkElement Pad(FrameworkElement e, double top)
        {
            e.Margin = new Thickness(0, top, 0, 0);
            return e;
        }

        /// <summary>A legend key: the line's colour as a 10 px dash, then the word.</summary>
        private static StackPanel Key(string brush, string word, bool dashed)
        {
            var dash = new Rectangle { Width = 10, Height = 2, VerticalAlignment = VerticalAlignment.Center };
            dash.SetResourceReference(Shape.FillProperty, brush);
            if (dashed) dash.Opacity = 0.75;
            var t = Deck.T(Ink.Label, word);
            t.Margin = new Thickness(5, 0, 0, 0);
            var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(dash);
            sp.Children.Add(t);
            return sp;
        }

        /// <summary>One line of the internet card: the word, the number with its unit, and the bar under both.</summary>
        private static Grid Line(string word, out TextBlock value, out ScaleTransform bar)
        {
            var head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.Children.Add(Deck.T(Ink.Label, word));
            var v = Deck.Value(out value, "Mb/s", Ink.Value);
            Grid.SetColumn(v, 1);
            head.Children.Add(v);
            var g = new Grid();
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.Children.Add(head);
            var b = Deck.Bar(out bar);
            b.Margin = new Thickness(0, 5, 0, 0);
            Grid.SetRow(b, 1);
            g.Children.Add(b);
            return g;
        }

        public override void Shown()
        {
            _connAt = DateTime.MinValue;
            W.Procs.RefreshAsync();
        }

        public override void Refresh()
        {
            var st = W.Stats;
            var t = W.Temps;

            _cpu.Text = Math.Round(st.Cpu) + "%";
            if (t.Cpu != null)
            {
                _cpuTemp.Text = Math.Round(t.Cpu.Value) + " °C";
                Deck.Paint(_cpuTemp, t.Cpu >= 85 ? "HotBrush" : t.Cpu >= 72 ? "WarnBrush" : "InfoBrush");
            }
            else
            {
                _cpuTemp.Text = W.S.Temperatures ? "—" : "";
                Deck.Paint(_cpuTemp, "DimBrush");
            }
            DrawChart();

            double ramPct = st.RamTotalGb > 0 ? st.RamUsedGb / st.RamTotalGb * 100 : 0;
            _ram.Text = Math.Round(ramPct) + "%";
            _ramSub.Text = st.RamUsedGb.ToString("0.0", NotchWindow.Ro) + " / " + Math.Round(st.RamTotalGb) + " GB";
            _ramBar.ScaleX = Math.Clamp(ramPct / 100, 0, 1);

            if (t.GpuLoad != null)
            {
                _gpu.Text = Math.Round(t.GpuLoad.Value) + "%";
                _gpuBar.ScaleX = Math.Clamp(t.GpuLoad.Value / 100, 0, 1);
            }
            else { _gpu.Text = "—"; _gpuBar.ScaleX = 0; }
            _gpuSub.Text = t.Gpu != null ? Math.Round(t.Gpu.Value) + " °C" : "";

            if (st.HasBattery && st.BatteryPercent >= 0)
            {
                _bat.Text = st.BatteryPercent + "%";
                _batBar.ScaleX = st.BatteryPercent / 100.0;
                _batWord.Text = st.Charging ? "se încarcă" :
                                st.BatterySecondsLeft > 0 ? st.BatterySecondsLeft / 3600 + " h " + st.BatterySecondsLeft % 3600 / 60 + " min" : "";
            }
            else { _bat.Text = "—"; _batBar.ScaleX = 0; _batWord.Text = "fără baterie"; }

            if (DateTime.Now - _connAt > TimeSpan.FromSeconds(15)) { _connAt = DateTime.Now; _conn = NetService.ConnectionType(); }
            var s = W.S;
            _ping.Text = s.SpeedPingMs > 0 ? s.SpeedPingMs + " ms" : "—";
            _down.Text = Mbps(st.NetDownMbps);
            _up.Text = Mbps(st.NetUpMbps);
            _downBar.ScaleX = s.SpeedDownMbps > 0 ? Math.Clamp(st.NetDownMbps / s.SpeedDownMbps, 0, 1) : 0;
            _upBar.ScaleX = s.SpeedUpMbps > 0 ? Math.Clamp(st.NetUpMbps / s.SpeedUpMbps, 0, 1) : 0;
            var lastTest = s.SpeedHistory?.LastOrDefault();
            _speedTest.Text = lastTest != null && lastTest.Ok
                ? Math.Round(lastTest.Down) + " / " + Math.Round(lastTest.Up) + " Mb/s"
                : "pornește testul";

            W.Procs.RefreshAsync();
            _top.Children.Clear();
            foreach (var p in W.Procs.Top.Take(4))
            {
                var g = new Grid { Margin = new Thickness(0, 5, 0, 0) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(78) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
                g.Children.Add(Deck.T(Ink.Name, p.Name));
                var bar = Deck.Bar(out var fill, "InfoBrush");
                bar.Margin = new Thickness(8, 0, 10, 0);
                fill.ScaleX = Math.Clamp(p.Cpu / 100, 0, 1);
                Grid.SetColumn(bar, 1);
                g.Children.Add(bar);
                var pct = Deck.T(Ink.Value, Math.Round(p.Cpu) + "%");
                pct.FontSize = 10;
                pct.TextAlignment = TextAlignment.Right;
                Deck.Paint(pct, "MutedBrush");
                Grid.SetColumn(pct, 2);
                g.Children.Add(pct);
                _top.Children.Add(g);
            }
            if (W.Procs.Top.Count == 0) _top.Children.Add(Deck.T(Ink.Label, "se calculează…"));
        }

        private static string Mbps(double v) => v >= 100 ? Math.Round(v).ToString() : v.ToString("0.0", NotchWindow.Ro);

        /// <summary>
        /// The last minute: the load as a filled amber line, the temperature as a dotted ice one. Same two readings the
        /// card's head names, at the same two colours — the graph adds no third thing to learn.
        /// </summary>
        private void DrawChart()
        {
            _chart.Children.Clear();
            double w = _chart.ActualWidth, h = _chart.ActualHeight;
            if (w <= 0 || h <= 0) return;
            for (int k = 1; k <= 2; k++)
            {
                var line = new Line { X1 = 0, X2 = w, Y1 = h * k / 3, Y2 = h * k / 3, StrokeThickness = 1, Opacity = 0.6 };
                line.SetResourceReference(Shape.StrokeProperty, "TrackBrush");
                _chart.Children.Add(line);
            }

            var cpu = W.CpuHist;
            if (cpu.Count >= 2)
            {
                var poly = new Polyline { StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round };
                poly.SetResourceReference(Shape.StrokeProperty, "AccentBrush");
                int start = 60 - cpu.Count;
                for (int i = 0; i < cpu.Count; i++)
                    poly.Points.Add(new Point((start + i) / 59.0 * w, h - 1 - Math.Clamp(cpu[i], 0, 100) / 100 * (h - 2)));
                _chart.Children.Add(poly);
            }

            var temp = W.TempHist;
            if (temp.Count >= 2 && temp.Any(v => !double.IsNaN(v)))
            {
                var poly = new Polyline
                {
                    StrokeThickness = 1.5,
                    StrokeDashArray = new DoubleCollection { 3, 3 },
                    StrokeLineJoin = PenLineJoin.Round,
                };
                poly.SetResourceReference(Shape.StrokeProperty, "InfoBrush");
                int start = 60 - temp.Count;
                for (int i = 0; i < temp.Count; i++)
                {
                    if (double.IsNaN(temp[i])) continue;
                    poly.Points.Add(new Point((start + i) / 59.0 * w, h - 1 - Math.Clamp(temp[i], 0, 100) / 100 * (h - 2)));
                }
                if (poly.Points.Count >= 2) _chart.Children.Add(poly);
            }
        }
    }
}
