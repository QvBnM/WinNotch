using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using WinNotch.Services;

namespace WinNotch.Panes
{
    /// <summary>Sistem: CPU load + temperature over the last minute, internet usage vs. maximum speed, memory, GPU, battery, top apps.</summary>
    internal sealed class SystemPane : Pane
    {
        public override double PanelHeight => 320;

        private readonly TextBlock _cpuNow, _cpuTemp;
        private readonly Canvas _chart;
        private readonly TextBlock _netInfo, _downNow, _upNow, _downMax, _upMax, _netFoot;
        private readonly ScaleTransform _downBar, _upBar;
        private readonly Button _testBtn;
        private readonly TextBlock _ram, _ramSub, _gpu, _gpuSub, _bat, _batSub;
        private readonly ScaleTransform _batBar;
        private readonly FrameworkElement _batRow;
        private readonly StackPanel _top;
        private readonly SpeedView _speed;
        private bool _testing => _speed.Running;
        private string _conn = "";
        private DateTime _connAt = DateTime.MinValue;

        public SystemPane(NotchWindow w) : base(w)
        {
            ColumnDefinitions.Add(new ColumnDefinition { Width = Ui.Star() });
            ColumnDefinitions.Add(new ColumnDefinition { Width = Ui.Px(10) });
            ColumnDefinitions.Add(new ColumnDefinition { Width = Ui.Px(230) });

            // ---------------- left: CPU chart + internet ----------------
            var left = Ui.Rows(Ui.Star(), Ui.Px(10), Ui.Auto);
            this.Put(left);

            _cpuNow = Ui.T("0%", 20, "InkBrush", true);
            _cpuTemp = Ui.T("—", 20, "OkBrush", true);
            var legend = Ui.H(12, LegendItem("încărcare", "AccentBrush"), LegendItem("temperatură", "OkBrush"));
            var head = Ui.Cols(Ui.Auto, Ui.Star(), Ui.Auto);
            head.Put(Ui.H(12, Ui.T("Procesor", 12, "MutedBrush"), _cpuNow, _cpuTemp));
            head.Put(legend, 2);
            _chart = new Canvas { ClipToBounds = true, Margin = new Thickness(0, 6, 0, 0) };
            _chart.SizeChanged += (o, e) => DrawChart();
            var chartGrid = Ui.Rows(Ui.Auto, Ui.Star());
            chartGrid.Put(head);
            chartGrid.Put(_chart, 0, 1);
            left.Put(Ui.Card(chartGrid, 14, 10));

            _netInfo = Ui.T("", 11, "MutedBrush");
            _testBtn = new Button { Style = Ui.S("GhostPill"), Content = "Test viteză", BorderThickness = new Thickness(1) };
            _testBtn.SetResourceReference(Control.BorderBrushProperty, "AccentBrush");
            _testBtn.SetResourceReference(Control.ForegroundProperty, "AccentBrush");
            _testBtn.Click += (o, e) => { OpenSpeed(); _ = _speed.StartAsync(); };
            var netHead = Ui.Cols(Ui.Auto, Ui.Px(8), Ui.Star(), Ui.Auto);
            netHead.Put(Ui.Cap("INTERNET"));
            netHead.Put(_netInfo, 2);
            netHead.Put(_testBtn, 3);

            _downNow = Ui.T("0 Mb/s", 13, "InkBrush", true, true);
            _upNow = Ui.T("0 Mb/s", 13, "InkBrush", true, true);
            _downMax = Ui.T("", 10.5, "DimBrush", false, true);
            _upMax = Ui.T("", 10.5, "DimBrush", false, true);
            _downMax.TextAlignment = _upMax.TextAlignment = TextAlignment.Right;
            var dBar = Ui.Bar(out _downBar, Ui.B("InfoBrush"), 6);
            var uBar = Ui.Bar(out _upBar, Ui.Rgb(0xA7, 0x8B, 0xFA), 6);
            var rows = Ui.Cols(Ui.Px(92), Ui.Px(86), Ui.Star(), Ui.Px(60));
            rows.RowDefinitions.Add(new RowDefinition { Height = Ui.Px(24) });
            rows.RowDefinitions.Add(new RowDefinition { Height = Ui.Px(24) });
            rows.Put(Ui.T("↓ Descărcare", 11.5, "MutedBrush"));
            rows.Put(_downNow, 1);
            rows.Put(dBar, 2);
            rows.Put(_downMax, 3);
            rows.Put(Ui.T("↑ Încărcare", 11.5, "MutedBrush"), 0, 1);
            rows.Put(_upNow, 1, 1);
            rows.Put(uBar, 2, 1);
            rows.Put(_upMax, 3, 1);
            _netFoot = Ui.T("", 11, "MutedBrush");
            _netFoot.OnClick(OpenSpeed);
            _netFoot.ToolTip = "Deschide rezultatele și istoricul testelor";
            var netStack = Ui.V(6, netHead, rows, _netFoot);
            left.Put(Ui.Card(netStack, 14, 10), 0, 2);

            // ---------------- right: fixed rows so nothing overlaps ----------------
            var right = Ui.Rows(Ui.Px(74), Ui.Px(8), Ui.Px(40), Ui.Px(8), Ui.Star());
            this.Put(right, 2);

            var tiles = Ui.Cols(Ui.Star(), Ui.Px(8), Ui.Star());
            _ram = Ui.T("—", 18, "InkBrush", true);
            _ramSub = Ui.T("", 10, "DimBrush", false, true);
            _gpu = Ui.T("—", 18, "InkBrush", true);
            _gpuSub = Ui.T("", 10, "DimBrush", false, true);
            tiles.Put(Tile("Memorie", _ram, _ramSub));
            tiles.Put(Tile("Placă video", _gpu, _gpuSub), 2);
            right.Put(tiles);

            _bat = Ui.T("—", 13, "InkBrush", true);
            _batSub = Ui.T("", 10, "DimBrush", false, true);
            var batBar = Ui.Bar(out _batBar, Ui.B("OkBrush"), 6);
            batBar.Margin = new Thickness(10, 0, 10, 0);
            var batGrid = Ui.Cols(Ui.Auto, Ui.Star(), Ui.Auto);
            batGrid.Put(Ui.T("Baterie", 11, "MutedBrush"));
            batGrid.Put(batBar, 1);
            batGrid.Put(Ui.H(4, _bat, _batSub), 2);
            _batRow = Ui.Card(batGrid, 11, 0, 12);
            right.Put(_batRow, 0, 2);

            _top = new StackPanel();
            var topHead = Ui.Cols(Ui.Star(), Ui.Auto);
            topHead.Put(Ui.Cap("CONSUMĂ ACUM"));
            topHead.Put(Ui.T("CPU · RAM", 9.5, "DimBrush", false, true), 1);
            var topStack = Ui.V(6, topHead, _top);
            right.Put(Ui.Card(topStack, 11, 9, 12), 0, 4);

            // speed test screen, on top of everything until "Înapoi"
            _speed = new SpeedView(w, () => _speed.Visibility = Visibility.Collapsed) { Visibility = Visibility.Collapsed };
            Panel.SetZIndex(_speed, 10);
            this.Put(_speed, 0, 0, 3);
        }

        /// <summary>From the Internet widget: open the speed test screen and run a test.</summary>
        internal void StartSpeedTest()
        {
            OpenSpeed();
            _ = _speed.StartAsync();
        }

        private void OpenSpeed()
        {
            _speed.ShowStored();
            _speed.Visibility = Visibility.Visible;
        }

        private static FrameworkElement LegendItem(string text, string brushKey)
        {
            var sw = new Border { Width = 10, Height = 3, CornerRadius = new CornerRadius(2) };
            sw.SetResourceReference(Border.BackgroundProperty, brushKey);      // follows an accent change
            return Ui.H(5, sw, Ui.T(text, 10.5, "MutedBrush"));
        }

        private static FrameworkElement Tile(string label, TextBlock value, TextBlock sub)
        {
            var g = Ui.Rows(Ui.Auto, Ui.Star(), Ui.Auto);
            g.Put(Ui.T(label, 11, "MutedBrush"));
            g.Put(value, 0, 1);
            g.Put(sub, 0, 2);
            return Ui.Card(g, 11, 9, 12);
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
            _cpuNow.Inlines.Clear();
            _cpuNow.Inlines.Add(new System.Windows.Documents.Run(Math.Round(st.Cpu).ToString()));
            _cpuNow.Inlines.Add(new System.Windows.Documents.Run("%") { FontSize = 12, Foreground = Ui.B("MutedBrush") });
            _cpuTemp.Inlines.Clear();
            if (t.Cpu != null)
            {
                _cpuTemp.Foreground = TempBrush(t.Cpu);
                _cpuTemp.Inlines.Add(new System.Windows.Documents.Run(Math.Round(t.Cpu.Value).ToString()));
                _cpuTemp.Inlines.Add(new System.Windows.Documents.Run("°C") { FontSize = 12, Foreground = Ui.B("MutedBrush") });
            }
            else
            {
                _cpuTemp.Foreground = Ui.B("DimBrush");
                // P51c: say it when the reading was given up after repeated abrupt closures, instead of showing a silent "—"
                _cpuTemp.Inlines.Add(new System.Windows.Documents.Run(!W.S.Temperatures ? "" : W.Temps.BlockedBySafety ? "temp: oprite" : !App.IsAdmin && !TempHelper.Installed ? "temp: activează" : !TempService.PawnIOInstalled ? "temp: PawnIO" : "—") { FontSize = 11 });
                _cpuTemp.ToolTip = W.Temps.BlockedBySafety ? "Citirea temperaturilor s-a oprit: WinNotch s-a închis brusc de două ori la rând. Bifează din nou „Temperaturi” în Setări ca să încerci iar." :
                                   !App.IsAdmin && !TempHelper.Installed ? "Temperatura procesorului: meniul iconiței › Activează temperatura procesorului (o singură dată, cere confirmare de administrator)." :
                                   !TempService.PawnIOInstalled ? "Instalează driverul gratuit PawnIO de pe pawnio.eu, apoi repornește PC-ul." : null;
            }
            DrawChart();

            // internet
            if (DateTime.Now - _connAt > TimeSpan.FromSeconds(15)) { _connAt = DateTime.Now; _conn = NetService.ConnectionType(); }
            var s = W.S;
            _netInfo.Text = _conn + (s.SpeedPingMs > 0 ? " · ping " + s.SpeedPingMs + " ms" : "");
            double down = st.NetDownMbps, up = st.NetUpMbps;
            _downNow.Text = Mbps(down);
            _upNow.Text = Mbps(up);
            var last = s.SpeedHistory?.LastOrDefault();
            if (s.SpeedDownMbps > 0)
            {
                _downMax.Text = "din " + Math.Round(s.SpeedDownMbps);
                _upMax.Text = "din " + Math.Round(s.SpeedUpMbps);
                _downBar.ScaleX = Math.Clamp(down / s.SpeedDownMbps, 0, 1);
                _upBar.ScaleX = s.SpeedUpMbps > 0 ? Math.Clamp(up / s.SpeedUpMbps, 0, 1) : 0;
            }
            else
            {
                _downMax.Text = _upMax.Text = "";
                _downBar.ScaleX = _upBar.ScaleX = 0;
            }
            if (_testing) _netFoot.Text = "Test în curs…  click ca să-l urmărești";
            else if (last != null && last.Ok)
                _netFoot.Text = "Ultimul test " + Day(last.At) + " " + last.At.ToString("HH:mm") + ": ↓" + Math.Round(last.Down) + " ↑" + Math.Round(last.Up) +
                                " Mb/s · router " + (last.Router < 0 ? "—" : last.Router == 0 ? "<1" : last.Router.ToString()) + " ms  ›";
            else if (last != null) _netFoot.Text = "Ultimul test nu a reușit  ›  click pentru detalii";
            else _netFoot.Text = "Apasă „Test viteză” ca să afli viteza maximă și dacă routerul e în regulă.";

            // memory, GPU, battery
            double ramPct = st.RamTotalGb > 0 ? st.RamUsedGb / st.RamTotalGb * 100 : 0;
            _ram.Text = Math.Round(ramPct) + "%";
            _ramSub.Text = st.RamUsedGb.ToString("0.0", NotchWindow.Ro) + " / " + Math.Round(st.RamTotalGb) + " GB";
            if (t.Gpu != null)
            {
                _gpu.Text = Math.Round(t.Gpu.Value) + "°C";
                _gpu.Foreground = W.HeatBrush(t.Gpu);
            }
            else { _gpu.Text = "—"; _gpu.Foreground = Ui.B("InkBrush"); }
            _gpuSub.Text = t.GpuLoad != null ? Math.Round(t.GpuLoad.Value) + "% încărcare" : W.S.Temperatures ? "" : "temp. oprite";

            if (st.HasBattery && st.BatteryPercent >= 0)
            {
                _batRow.Opacity = 1;
                _bat.Text = st.BatteryPercent + "%";
                _batBar.ScaleX = st.BatteryPercent / 100.0;
                _batSub.Text = st.Charging ? "· se încarcă" : st.BatterySecondsLeft > 0 ? "· " + st.BatterySecondsLeft / 3600 + " h " + st.BatterySecondsLeft % 3600 / 60 : "";
            }
            else
            {
                _batRow.Opacity = 0.55;
                _bat.Text = "—";
                _batBar.ScaleX = 0;
                _batSub.Text = "PC fără baterie";
            }

            // top apps
            W.Procs.RefreshAsync();
            _top.Children.Clear();
            var colors = new[] { Ui.B("InfoBrush"), Ui.Rgb(0x8C, 0x7B, 0xFF), Ui.Rgb(0x9A, 0xA0, 0xA8), Ui.B("OkBrush") };
            int i = 0;
            foreach (var p in W.Procs.Top)
            {
                var g = Ui.Cols(Ui.Px(8), Ui.Px(8), Ui.Star(), Ui.Px(38), Ui.Px(54));
                g.Margin = new Thickness(0, 0, 0, 4);
                g.Put(new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(2), Background = colors[i++ % colors.Length], VerticalAlignment = VerticalAlignment.Center });
                g.Put(Ui.T(p.Name, 11.5), 2);
                var cpu = Ui.T(Math.Round(p.Cpu) + "%", 10.5, "InkBrush", false, true);
                cpu.TextAlignment = TextAlignment.Right;
                g.Put(cpu, 3);
                var ram = Ui.T(Bytes(p.RamBytes), 10.5, "DimBrush", false, true);
                ram.TextAlignment = TextAlignment.Right;
                g.Put(ram, 4);
                _top.Children.Add(g);
            }
            if (W.Procs.Top.Count == 0) _top.Children.Add(Ui.T("se calculează…", 11, "DimBrush"));
        }

        private Brush TempBrush(float? t) => t == null ? Ui.B("DimBrush") : t >= 85 ? Ui.B("HotBrush") : t >= 72 ? Ui.B("WarnBrush") : Ui.B("OkBrush");

        private static string Mbps(double v) => (v >= 100 ? Math.Round(v).ToString() : v.ToString("0.0", NotchWindow.Ro)) + " Mb/s";
        private static string Bytes(long b) => b >= 1L << 30 ? (b / (double)(1L << 30)).ToString("0.0", NotchWindow.Ro) + " GB" : b / (1 << 20) + " MB";
        private static string Day(DateTime d) => d.Date == DateTime.Today ? "azi" : d.Date == DateTime.Today.AddDays(-1) ? "ieri" : d.ToString("d MMM", NotchWindow.Ro);

        private void DrawChart()
        {
            _chart.Children.Clear();
            double w = _chart.ActualWidth, h = _chart.ActualHeight;
            if (w <= 0 || h <= 0) return;
            for (int k = 1; k <= 2; k++)
                _chart.Children.Add(new Line { X1 = 0, X2 = w, Y1 = h * k / 3, Y2 = h * k / 3, Stroke = Ui.B("TrackBrush"), StrokeThickness = 1 });

            var cpu = W.CpuHist;
            if (cpu.Count >= 2)
            {
                var line = new Polyline { StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round };
                line.SetResourceReference(Shape.StrokeProperty, "AccentBrush");
                var area = new Polygon();
                var accent = (Ui.B("AccentBrush") as SolidColorBrush)?.Color ?? Colors.Orange;
                area.Fill = new SolidColorBrush(Color.FromArgb(0x29, accent.R, accent.G, accent.B));
                int start = 60 - cpu.Count;
                for (int i = 0; i < cpu.Count; i++)
                {
                    var pt = new Point((start + i) / 59.0 * w, h - 1 - cpu[i] / 100 * (h - 2));
                    line.Points.Add(pt);
                    area.Points.Add(pt);
                }
                area.Points.Add(new Point(w, h));
                area.Points.Add(new Point(start / 59.0 * w, h));
                _chart.Children.Add(area);
                _chart.Children.Add(line);
            }

            var temp = W.TempHist;
            if (temp.Count(x => !double.IsNaN(x)) >= 2)
            {
                var tl = new Polyline { StrokeThickness = 2, StrokeDashArray = new DoubleCollection { 3, 2 } };
                tl.SetResourceReference(Shape.StrokeProperty, "OkBrush");
                int start = 60 - temp.Count;
                for (int i = 0; i < temp.Count; i++)
                {
                    if (double.IsNaN(temp[i])) continue;
                    double frac = Math.Clamp((temp[i] - 20) / 80, 0, 1);       // 20..100 °C
                    tl.Points.Add(new Point((start + i) / 59.0 * w, h - 1 - frac * (h - 2)));
                }
                _chart.Children.Add(tl);
            }
        }
    }
}
