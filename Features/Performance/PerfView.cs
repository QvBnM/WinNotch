using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using WinNotch.Core.Perf;
using WinNotch.Features.WindowV2;

namespace WinNotch.Features.Performance
{
    /// <summary>
    /// P60: the Performanță tab of the WinNotch window. It only draws; every number comes from
    /// <see cref="PerfMonitor"/>, which is also the only thing that measures.
    /// <para>The tab says what each number means and refuses to show one it does not have (a "—", not a zero). That is
    /// the whole point of this section: anything it claims, it can show before and after, with a figure.</para>
    /// </summary>
    internal sealed class PerfView : Grid
    {
        private readonly Action<string> _say;
        private readonly List<Gauge> _gauges = new List<Gauge>();
        private StackPanel _body;
        private TextBlock _commit, _cadence, _watched;
        private StackPanel _top, _leaks;
        private PerfMetric _sortBy = PerfMetric.Cpu;
        private Action<PerfSample> _onSample;
        private bool _open;

        public PerfView(Action<string> say)
        {
            _say = say ?? (_ => { });
            Build();
        }

        // ------------------------------------------------------------------ coming and going

        /// <summary>
        /// The tab was opened: ask the monitor to run (it does not, otherwise) and start listening. Paired with
        /// <see cref="Detach"/> — the monitor counts its viewers, so an unbalanced call would leave a timer behind.
        /// </summary>
        internal void Open()
        {
            if (_open) return;
            _open = true;
            var monitor = PerfMonitor.Current;
            if (monitor == null) return;
            _onSample = s => Dispatcher.InvokeAsync(() => { if (_open) Paint(s); });
            monitor.Sampled += _onSample;
            monitor.AddViewer();
            Paint(monitor.Last);
        }

        /// <summary>The tab was left or the window closed: stop listening and let the monitor stop measuring.</summary>
        internal void Detach()
        {
            if (!_open) return;
            _open = false;
            var monitor = PerfMonitor.Current;
            if (monitor == null) return;
            if (_onSample != null) monitor.Sampled -= _onSample;
            _onSample = null;
            monitor.RemoveViewer();
        }

        // ------------------------------------------------------------------ the page

        private void Build()
        {
            _body = new StackPanel { Margin = new Thickness(LayoutRules.Pad) };

            var row = Ui.Cols(Ui.Star(), Ui.Star(), Ui.Star(), Ui.Star());
            row.Margin = new Thickness(0, 0, 0, LayoutRules.Gap + 2);
            row.Put(Tile("Procesor", "cpu", true), 0);
            row.Put(Tile("Memorie", "ram", true), 1);
            row.Put(Tile("Placă video", "gpu", true), 2);
            row.Put(Tile("Memorie video", "vram", false), 3);
            _body.Children.Add(row);

            _commit = Ui.T("—", 13, "InkBrush");
            _commit.TextWrapping = TextWrapping.Wrap;
            _body.Children.Add(V2Controls.Card("Memorie angajată", 
                "Cât au cerut toate programele de la Windows, RAM plus fișierul de paginare. Ăsta e numărul care spune dacă mașina chiar a rămas fără memorie — nu „RAM liber”, care pe 32 GB e un număr fără sens: memoria nefolosită e memorie irosită.",
                _commit));

            _top = new StackPanel();
            var sort = Ui.Cols(Ui.Auto, Ui.Auto, Ui.Auto, Ui.Star());
            sort.Margin = new Thickness(0, 0, 0, 8);
            sort.Put(SortButton("Procesor", PerfMetric.Cpu), 0);
            sort.Put(SortButton("Placă video", PerfMetric.Gpu), 1);
            sort.Put(SortButton("Memorie", PerfMetric.Memory), 2);
            var topBody = V2Controls.Stack(sort, _top);
            _body.Children.Add(V2Controls.Card("Cine consumă acum",
                "Procesele sunt adunate după nume: un browser cu patruzeci de procese e un singur program, nu patruzeci de lucruri mici. Procentul de procesor e din toată mașina, ca în Task Manager.",
                topBody));

            _leaks = new StackPanel();
            _watched = V2Controls.Hint("Urmăresc de prea puțin timp.");
            _body.Children.Add(V2Controls.Card("Ce crește în timp",
                "O scurgere de memorie crește drept și constant, ore în șir — spre deosebire de un program folosit normal, care urcă și coboară. Apare aici doar ce crește cu peste 50 MB pe oră, de cel puțin 20 de minute, cu cel puțin 150 MB adunați.",
                V2Controls.Stack(_leaks, _watched)));

            _cadence = V2Controls.Hint("");
            _body.Children.Add(_cadence);

            var scroll = new ScrollViewer { Style = Ui.S("SlimScroll"), Content = _body };
            Children.Add(scroll);
        }

        private Button SortButton(string text, PerfMetric metric)
        {
            var btn = new Button { Style = Ui.S("NavButton"), Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 0) };
            var label = Ui.T(text, 12, metric == _sortBy ? "InkBrush" : "MutedBrush", metric == _sortBy);
            btn.Content = label;
            btn.Click += (o, e) =>
            {
                _sortBy = metric;
                foreach (var b in _sortButtons) b.Paint(_sortBy);
                var monitor = PerfMonitor.Current;
                if (monitor != null) Paint(monitor.Last);
            };
            _sortButtons.Add(new SortLabel(label, metric));
            return btn;
        }

        private readonly List<SortLabel> _sortButtons = new List<SortLabel>();

        private sealed class SortLabel
        {
            private readonly TextBlock _t;
            private readonly PerfMetric _m;
            public SortLabel(TextBlock t, PerfMetric m) { _t = t; _m = m; }
            public void Paint(PerfMetric current)
            {
                _t.SetResourceReference(TextBlock.ForegroundProperty, _m == current ? "InkBrush" : "MutedBrush");
                _t.FontWeight = _m == current ? FontWeights.SemiBold : FontWeights.Normal;
            }
        }

        // ------------------------------------------------------------------ one tile

        /// <summary>A number, a bar and a caption. The bar's colour follows the value, through the theme's own tokens.</summary>
        private sealed class Gauge
        {
            public string Key;
            public TextBlock Value;
            public Border Fill;
            public Grid Track;
            /// <summary>Video memory has no bar: we read how much is used, not how much the card has, and a bar
            /// without a maximum would be an invented number.</summary>
            public bool HasBar;

            public void Set(string text, double percent, bool known)
            {
                Value.Text = known ? text : "—";
                Track.Visibility = HasBar ? Visibility.Visible : Visibility.Collapsed;
                if (!HasBar) return;
                double p = known ? Math.Clamp(percent, 0, 100) : 0;
                Fill.Width = Math.Max(0, Track.ActualWidth * p / 100.0);
                Fill.SetResourceReference(Border.BackgroundProperty,
                    !known ? "TrackBrush" : p >= 90 ? "HotBrush" : p >= 75 ? "WarnBrush" : "AccentBrush");
            }
        }

        private FrameworkElement Tile(string caption, string key, bool withBar)
        {
            var value = Ui.T("—", 24, "InkBrush", true);
            var label = Ui.T(caption, 11.5, "MutedBrush");
            label.Margin = new Thickness(0, 2, 0, 8);

            var fill = new Border { CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left, Height = 6 };
            fill.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
            var track = new Grid { Height = 6 };
            var back = new Border { CornerRadius = new CornerRadius(3) };
            back.SetResourceReference(Border.BackgroundProperty, "TrackBrush");
            track.Children.Add(back);
            track.Children.Add(fill);

            var sp = V2Controls.Stack(value, label, track);
            var gauge = new Gauge { Key = key, Value = value, Fill = fill, Track = track, HasBar = withBar };
            _gauges.Add(gauge);
            // The bar is drawn in pixels, so it is re-laid-out when the column changes width.
            track.SizeChanged += (o, e) =>
            {
                bool known = _last.TryGetValue(key, out var v);
                gauge.Set(known ? v.Text : "—", known ? v.Percent : 0, known);
            };

            var card = Ui.Card(sp, 16, 14, LayoutRules.CardRadius);
            card.Margin = new Thickness(0, 0, LayoutRules.Gap, 0);
            return card;
        }

        private readonly Dictionary<string, (double Percent, string Text)> _last = new Dictionary<string, (double, string)>(StringComparer.Ordinal);

        // ------------------------------------------------------------------ one sample

        private void Paint(PerfSample s)
        {
            if (s == null) s = PerfSample.Empty;
            bool measured = s.RamTotalGb > 0;

            Show("cpu", N(s.CpuPercent) + "%", s.CpuPercent, measured);
            Show("ram", s.RamUsedGb.ToString("0.0", CultureInfo.InvariantCulture) + " GB", s.RamPercent, measured);
            Show("gpu", N(s.GpuPercent) + "%", s.GpuPercent, s.HasGpu);
            Show("vram", N(s.VramUsedMb) + " MB", 0, s.VramUsedMb >= 0);

            _commit.Text = measured
                ? s.CommitUsedGb.ToString("0.0", CultureInfo.InvariantCulture) + " GB din " +
                  s.CommitLimitGb.ToString("0.0", CultureInfo.InvariantCulture) + " GB (" + N(s.CommitPercent) + "%), " +
                  "iar în RAM " + s.RamUsedGb.ToString("0.0", CultureInfo.InvariantCulture) + " GB din " +
                  s.RamTotalGb.ToString("0.0", CultureInfo.InvariantCulture) + " GB"
                : "—";

            if (s.HasProcesses) PaintTop(s);
            PaintLeaks();

            var monitor = PerfMonitor.Current;
            var cadence = monitor?.Cadence ?? PerfCadence.Off;
            int seconds = PerfRules.SecondsFor(cadence);
            _cadence.Text = cadence == PerfCadence.Off
                ? "Nu măsor nimic acum."
                : "Măsor la " + seconds + " s cât ai fila deschisă" + (cadence == PerfCadence.Game ? " (joc pornit)" : "") +
                  "; procesele la " + PerfRules.ProcessSeconds + " s. Când închizi fereastra, se oprește cu totul.";
        }

        private void Show(string key, string text, double percent, bool known)
        {
            if (known) _last[key] = (percent, text); else _last.Remove(key);
            foreach (var g in _gauges) if (g.Key == key) g.Set(text, percent, known);
        }

        private static string N(double v) => Math.Round(v).ToString(CultureInfo.InvariantCulture);

        private void PaintTop(PerfSample s)
        {
            _top.Children.Clear();
            var list = ProcessRollup.Top(s.Top, 7, _sortBy);
            if (list.Count == 0)
            {
                _top.Children.Add(V2Controls.Hint("Nimic demn de raportat: nicio aplicație nu consumă măsurabil acum."));
                return;
            }
            foreach (var u in list)
            {
                var name = Ui.T(u.Name + (u.Processes > 1 ? "  (" + u.Processes + ")" : ""), 13, "InkBrush");
                var numbers = Ui.T(
                    u.CpuPercent.ToString("0.0", CultureInfo.InvariantCulture) + "% CPU   " +
                    (u.GpuPercent > 0 ? u.GpuPercent.ToString("0.0", CultureInfo.InvariantCulture) + "% GPU   " : "") +
                    N(u.PrivateMb) + " MB", 12, "MutedBrush");
                numbers.HorizontalAlignment = HorizontalAlignment.Right;
                _top.Children.Add(V2Controls.ListRow(null, name, numbers));
            }
        }

        private void PaintLeaks()
        {
            _leaks.Children.Clear();
            var monitor = PerfMonitor.Current;
            var leaks = monitor?.Leaks() ?? new List<(string, MemoryTrendResult)>();
            foreach (var (name, trend) in leaks.Take(5))
            {
                var label = Ui.T(name, 13, "InkBrush");
                var detail = Ui.T("+" + N(trend.MbPerHour) + " MB/h, de " + N(trend.Minutes) + " min (acum " + N(trend.GrowthMb) + " MB mai mult)", 12, "WarnBrush");
                detail.HorizontalAlignment = HorizontalAlignment.Right;
                _leaks.Children.Add(V2Controls.ListRow(null, label, detail));
            }
            var history = monitor?.History();
            double minutes = history != null && history.Count > 1 ? (history[history.Count - 1].TimeUtc - history[0].TimeUtc).TotalMinutes : 0;
            _watched.Text = leaks.Count > 0
                ? "Un program care crește așa merită repornit; după repornire numărul de aici trebuie să cadă."
                : minutes < MemoryTrend.MinMinutes
                    ? "Urmăresc de " + N(minutes) + " min; sub " + N(MemoryTrend.MinMinutes) + " nu spun nimic, ca să nu dau alarme false."
                    : "Nimic nu crește suspect de " + N(minutes) + " min încoace.";
        }
    }
}
