using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using WinNotch.Core.Perf;
using WinNotch.Widgets;

namespace WinNotch.Features.GameMode
{
    /// <summary>
    /// P61: "Ultimul joc" in the notch — the summary where the author actually looks, instead of only in an alert
    /// that lasts ten seconds. 3×1 shows the headline and one line; 3×2 adds the rest of the report.
    /// <para>With the switch off, or before the first measured session, it says so plainly rather than showing an
    /// empty card with zeros in it.</para>
    /// </summary>
    internal sealed class GameWidget : Widget
    {
        private readonly TextBlock _title, _first;
        private readonly StackPanel _rest;
        private DateTime _shown = DateTime.MinValue;

        public GameWidget(NotchWindow w, WidgetSlot s) : base(w, s)
        {
            _title = Ui.T("—", Tiny ? 13 : 14, "InkBrush", true);
            _first = Ui.T("", 11.5, "MutedBrush");
            _first.TextWrapping = TextWrapping.Wrap;
            _rest = new StackPanel();
            var stack = new StackPanel();
            stack.Children.Add(Cap("ULTIMUL JOC"));
            stack.Children.Add(_title);
            stack.Children.Add(_first);
            if (Tall) stack.Children.Add(_rest);
            Children.Add(stack);
            Refresh();
        }

        public override void Refresh()
        {
            var report = W.LastGameReport;
            // Rebuilt only when the session shown changes: this runs about once a second while the page is open.
            if (report != null && report.StartedUtc == _shown) return;
            _shown = report?.StartedUtc ?? DateTime.MinValue;
            _rest.Children.Clear();

            if (report == null || !report.Measured)
            {
                _title.Text = W.GameRunning ? "Se joacă acum" : "Nicio sesiune încă";
                _first.Text = W.GameRunning
                    ? "Rezumatul apare la ieșirea din joc."
                    : "Apare după un joc care a ținut peste două minute.";
                return;
            }

            _title.Text = report.Headline();
            var lines = report.Lines();
            _first.Text = lines.Count > 0 ? lines[0] : "";
            if (!Tall) return;
            foreach (string line in lines.Skip(1))
            {
                var t = Ui.T(line, 11.5, "DimBrush");
                t.TextWrapping = TextWrapping.Wrap;
                t.Margin = new Thickness(0, 2, 0, 0);
                _rest.Children.Add(t);
            }
        }
    }
}
