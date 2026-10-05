using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using WinNotch.Services;

namespace WinNotch.Panes
{
    /// <summary>
    /// Speed test screen inside Sistem: live speed while it runs, then "now vs. last test", router and internet ping,
    /// a plain verdict (router restart or not) and the history of the last tests. Stays open until "Înapoi".
    /// </summary>
    internal sealed class SpeedView : Grid
    {
        private readonly NotchWindow W;
        private readonly Action _close;
        private readonly TextBlock _status, _phase, _big, _unit, _sub, _lastHead, _verdict, _verdictIcon, _histNote;
        private readonly ScaleTransform _bar;
        private readonly Button _again;
        private readonly Border _verdictBox, _verdictStripe;
        private readonly Grid _table;
        private readonly Canvas _hist;
        private readonly TextBlock[,] _cells = new TextBlock[5, 2];

        public bool Running { get; private set; }
        private SpeedRecord _now;          // result of the test just run (or the newest from history)
        private SpeedRecord _last;         // the test before it

        public SpeedView(NotchWindow w, Action close)
        {
            W = w;
            _close = close;
            Background = Ui.B("NotchBrush");

            var root = Ui.Rows(Ui.Auto, Ui.Px(10), Ui.Star(), Ui.Px(8), Ui.Auto);

            // header
            var head = Ui.Cols(Ui.Auto, Ui.Px(10), Ui.Auto, Ui.Star(), Ui.Auto, Ui.Px(8), Ui.Auto);
            var back = new Button { Style = Ui.S("TileButton"), Width = 28, Height = 28, Content = Ui.Icon(Ui.GBack, 12), ToolTip = "Înapoi la Sistem" };
            back.Click += (o, e) => _close();
            head.Put(back);
            head.Put(Ui.Cap("TEST DE VITEZĂ"), 2);
            _status = Ui.T("", 12, "MutedBrush");
            head.Put(_status, 4);
            _again = Ui.PillBtn("Testează din nou", () => _ = StartAsync(), true);
            head.Put(_again, 6);
            root.Put(head);

            // body: live gauge | comparison | history
            var body = Ui.Cols(Ui.Px(210), Ui.Px(12), Ui.Star(), Ui.Px(12), Ui.Px(170));
            root.Put(body, 0, 2);

            _phase = Ui.T("Descărcare", 12.5, "MutedBrush");
            _big = Ui.T("—", 42, "InkBrush", true);
            _unit = Ui.T(" Mb/s", 14, "MutedBrush");
            _unit.VerticalAlignment = VerticalAlignment.Bottom;
            _unit.Margin = new Thickness(0, 0, 0, 9);
            var bigRow = new StackPanel { Orientation = Orientation.Horizontal };
            bigRow.Children.Add(_big);
            bigRow.Children.Add(_unit);
            var bar = Ui.Bar(out _bar, Ui.B("InfoBrush"), 6);
            bar.Margin = new Thickness(0, 6, 0, 8);
            _sub = Ui.T("", 12, "MutedBrush");
            _sub.TextWrapping = TextWrapping.Wrap;
            _sub.TextTrimming = TextTrimming.None;
            var gauge = Ui.V(0, _phase, bigRow, bar, _sub);
            gauge.VerticalAlignment = VerticalAlignment.Center;
            body.Put(Ui.Card(gauge, 14, 12), 0);

            _table = Ui.Cols(Ui.Star(1.3), Ui.Star(), Ui.Star());
            for (int r = 0; r < 6; r++) _table.RowDefinitions.Add(new RowDefinition { Height = Ui.Star() });
            _table.Put(Ui.T("", 11), 0, 0);
            var nowHead = Ui.T("ACUM", 11, "MutedBrush", true);
            nowHead.HorizontalAlignment = HorizontalAlignment.Right;
            _table.Put(nowHead, 1, 0);
            _lastHead = Ui.T("ULTIMUL", 11, "MutedBrush", true);
            _lastHead.HorizontalAlignment = HorizontalAlignment.Right;
            _lastHead.TextTrimming = TextTrimming.None;
            _lastHead.TextAlignment = TextAlignment.Right;
            nowHead.VerticalAlignment = VerticalAlignment.Bottom;
            _lastHead.VerticalAlignment = VerticalAlignment.Bottom;
            _table.Put(_lastHead, 2, 0);
            _table.RowDefinitions[0].Height = GridLength.Auto;      // header may take two lines (ULTIMUL / AZI 14:32)
            string[] labels = { "Descărcare", "Încărcare", "Ping internet", "Ping router", "Pierderi" };
            for (int r = 0; r < 5; r++)
            {
                _table.Put(Ui.T(labels[r], 12.5, "MutedBrush"), 0, r + 1);
                for (int c = 0; c < 2; c++)
                {
                    var t = Ui.T("—", 13.5, c == 0 ? "InkBrush" : "MutedBrush", c == 0, true);
                    t.HorizontalAlignment = HorizontalAlignment.Right;
                    _cells[r, c] = t;
                    _table.Put(t, c + 1, r + 1);
                }
            }
            body.Put(Ui.Card(_table, 14, 8), 2);

            _hist = new Canvas { ClipToBounds = true };
            _hist.SizeChanged += (o, e) => DrawHistory();
            _histNote = Ui.T("", 11, "DimBrush");
            var histGrid = Ui.Rows(Ui.Auto, Ui.Px(6), Ui.Star(), Ui.Px(4), Ui.Auto);
            histGrid.Put(Ui.Cap("ISTORIC · DESCĂRCARE"));
            histGrid.Put(_hist, 0, 2);
            histGrid.Put(_histNote, 0, 4);
            body.Put(Ui.Card(histGrid, 12, 10), 4);

            // verdict
            _verdictStripe = new Border { Width = 3, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 0, 10, 0) };
            _verdictIcon = Ui.Icon(Ui.GWarn, 15);
            _verdictIcon.Margin = new Thickness(0, 0, 10, 0);
            _verdict = Ui.T("", 12.5, "InkBrush");
            _verdict.TextWrapping = TextWrapping.Wrap;
            _verdict.TextTrimming = TextTrimming.None;
            var vg = Ui.Cols(Ui.Auto, Ui.Auto, Ui.Star());
            vg.Put(_verdictStripe);
            vg.Put(_verdictIcon, 1);
            vg.Put(_verdict, 2);
            _verdictBox = Ui.Card(vg, 12, 9, 12);
            root.Put(_verdictBox, 0, 4);

            Children.Add(root);
            ShowStored();
        }

        private List<SpeedRecord> History => W.S.SpeedHistory ??= new List<SpeedRecord>();

        /// <summary>Opened without running: shows the newest stored result.</summary>
        public void ShowStored()
        {
            if (Running) return;
            var ok = History.Where(h => h != null).ToList();
            _now = ok.LastOrDefault();
            _last = ok.Count >= 2 ? ok[ok.Count - 2] : null;
            Render();
        }

        public async Task StartAsync()
        {
            if (Running) return;
            Running = true;
            _last = History.LastOrDefault(h => h.Ok);
            _now = null;
            _again.IsEnabled = false;
            _again.Opacity = 0.5;
            _status.Text = "se testează… (~20 s)";
            _phase.Text = "Ping";
            _big.Text = "…";
            _unit.Text = "";
            _bar.ScaleX = 0;
            _sub.Text = "Verific routerul și legătura spre internet.";
            _verdictBox.Visibility = Visibility.Collapsed;
            FillTable();

            var progress = new Progress<SpeedProgress>(p =>
            {
                _bar.ScaleX = p.Fraction;
                if (p.Phase == "ping") return;
                _phase.Text = p.Phase == "down" ? "↓ Descărcare" : "↑ Încărcare";
                _big.Text = p.Mbps >= 100 ? Math.Round(p.Mbps).ToString() : p.Mbps.ToString("0.0", NotchWindow.Ro);
                _unit.Text = " Mb/s";
                _sub.Text = p.Phase == "down" ? "Măsor viteza de descărcare…" : "Măsor viteza de încărcare…";
            });

            var r = await NetService.SpeedTestAsync(progress);
            Running = false;
            _again.IsEnabled = true;
            _again.Opacity = 1;

            History.Add(r);
            while (History.Count > 20) History.RemoveAt(0);
            var good = History.Where(h => h.Ok).ToList();
            if (r.Ok)
            {
                W.S.SpeedDownMbps = good.Max(h => h.Down);
                W.S.SpeedUpMbps = good.Max(h => h.Up);
                W.S.SpeedPingMs = r.Ping;
                W.S.SpeedTestedAt = r.At;
            }
            W.S.Save();
            _now = r;
            Render();
        }

        private void Render()
        {
            if (Running) return;
            if (_now == null)
            {
                _status.Text = "niciun test încă";
                _phase.Text = "Descărcare";
                _big.Text = "—";
                _unit.Text = " Mb/s";
                _bar.ScaleX = 0;
                _sub.Text = "Apasă „Testează din nou”. Durează ~20 de secunde și folosește în jur de 100–300 MB.";
                _verdictBox.Visibility = Visibility.Collapsed;
            }
            else
            {
                _status.Text = Day(_now.At) + ", " + _now.At.ToString("HH:mm") + (string.IsNullOrEmpty(_now.Conn) ? "" : " · " + _now.Conn);
                _phase.Text = "↓ Descărcare";
                _big.Text = _now.Ok ? Num(_now.Down) : "—";
                _unit.Text = " Mb/s";
                _bar.ScaleX = W.S.SpeedDownMbps > 0 && _now.Ok ? Math.Clamp(_now.Down / W.S.SpeedDownMbps, 0, 1) : 0;
                _sub.Text = _now.Ok
                    ? "↑ " + (_now.Up > 0 ? Num(_now.Up) + " Mb/s" : "—") + "   ·   ping " + Ms(_now.Ping)
                    : "Testul nu a reușit.";
                var best = History.Where(h => h.Ok && h != _now).OrderByDescending(h => h.Down).FirstOrDefault();
                var (text, level) = NetService.Verdict(_now, best);
                _verdict.Text = text;
                var brush = level == "ok" ? Ui.B("OkBrush") : level == "warn" ? Ui.B("WarnBrush") : Ui.B("HotBrush");
                _verdictStripe.Background = brush;
                _verdictIcon.Foreground = brush;
                _verdictIcon.Text = level == "ok" ? "" : Ui.GWarn;
                _verdictBox.Visibility = Visibility.Visible;
            }
            FillTable();
            DrawHistory();
        }

        private void FillTable()
        {
            _lastHead.Text = _last != null ? "ULTIMUL\n" + Day(_last.At).ToUpperInvariant() + " " + _last.At.ToString("HH:mm") : "ULTIMUL";
            Fill(0, _now?.Ok == true ? Num(_now.Down) + " Mb/s" : "—", _last?.Ok == true ? Num(_last.Down) + " Mb/s" : "—", Worse(_now?.Down, _last?.Down, true));
            Fill(1, _now?.Up > 0 ? Num(_now.Up) + " Mb/s" : "—", _last?.Up > 0 ? Num(_last.Up) + " Mb/s" : "—", Worse(_now?.Up, _last?.Up, true));
            Fill(2, _now != null ? Ms(_now.Ping) : "—", _last != null ? Ms(_last.Ping) : "—", Worse(_now?.Ping, _last?.Ping, false));
            Fill(3, _now != null ? Ms(_now.Router) : "—", _last != null ? Ms(_last.Router) : "—", Worse(_now?.Router, _last?.Router, false));
            Fill(4, _now != null ? Math.Max(_now.RouterLoss, _now.NetLoss) + "%" : "—", _last != null ? Math.Max(_last.RouterLoss, _last.NetLoss) + "%" : "—",
                _now != null && Math.Max(_now.RouterLoss, _now.NetLoss) >= 5);
        }

        private void Fill(int row, string now, string last, bool worse)
        {
            _cells[row, 0].Text = now;
            _cells[row, 0].Foreground = worse ? Ui.B("WarnBrush") : Ui.B("InkBrush");
            _cells[row, 1].Text = last;
        }

        /// <summary>True when "now" is clearly worse than "last" (30% for speed, +20 ms and 2x for ping).</summary>
        private static bool Worse(double? now, double? last, bool higherIsBetter)
        {
            if (now == null || last == null || now < 0 || last <= 0) return false;
            return higherIsBetter ? now < last * 0.7 : now > last * 2 && now - last > 20;
        }

        private void DrawHistory()
        {
            _hist.Children.Clear();
            double w = _hist.ActualWidth, h = _hist.ActualHeight;
            var items = History.Where(x => x != null).TakeLast(10).ToList();
            if (items.Count == 0) { _histNote.Text = "apare după primul test"; return; }
            double max = Math.Max(1, items.Max(x => x.Down));
            _histNote.Text = "maxim " + Num(max) + " Mb/s · " + items.Count + (items.Count == 1 ? " test" : " teste");
            if (w <= 0 || h <= 0) return;
            double slot = w / 10, bw = Math.Max(4, slot - 5);
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                double frac = it.Ok ? Math.Max(0.04, it.Down / max) : 0.04;
                bool current = it == _now;
                var rect = new Rectangle
                {
                    Width = bw, Height = Math.Max(3, frac * (h - 2)), RadiusX = 2, RadiusY = 2,
                    Fill = !it.Ok ? Ui.B("HotBrush") : current ? Ui.B("InfoBrush") : Ui.Rgb(0x4A, 0x6F, 0x99),
                    ToolTip = Day(it.At) + " " + it.At.ToString("HH:mm") + "\n" + (it.Ok ? "↓ " + Num(it.Down) + "  ↑ " + Num(it.Up) + " Mb/s\nrouter " + Ms(it.Router) + " · internet " + Ms(it.Ping) : "nereușit")
                };
                Canvas.SetLeft(rect, (10 - items.Count + i) * slot + 2);
                Canvas.SetTop(rect, h - rect.Height);
                _hist.Children.Add(rect);
            }
        }

        private static string Num(double v) => v >= 100 ? Math.Round(v).ToString() : v.ToString("0.0", NotchWindow.Ro);
        private static string Ms(int v) => v < 0 ? "—" : v == 0 ? "<1 ms" : v + " ms";
        private static string Day(DateTime d) => d.Date == DateTime.Today ? "azi" : d.Date == DateTime.Today.AddDays(-1) ? "ieri" : d.ToString("d MMM", NotchWindow.Ro);
    }
}
