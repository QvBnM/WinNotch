using System;
using System.Collections.Generic;
using System.Linq;

namespace WinNotch.Widgets
{
    /// <summary>A page you made (or duplicated from a standard one): a grid of widgets, 6 columns × up to 4 rows.</summary>
    public sealed class UserPage
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "Pagina mea";
        public string Icon { get; set; } = "star";
        public bool Hidden { get; set; }
        public List<WidgetSlot> Widgets { get; set; } = new List<WidgetSlot>();
    }

    /// <summary>One widget on a page: which kind, where (column/row), how big (cells) and its own options.</summary>
    public sealed class WidgetSlot
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Type { get; set; } = "";
        public int Col { get; set; }
        public int Row { get; set; }
        public int W { get; set; } = 2;
        public int H { get; set; } = 1;
        public Dictionary<string, string> Options { get; set; } = new Dictionary<string, string>();

        public string Opt(string key, string fallback = "") =>
            Options != null && Options.TryGetValue(key, out var v) && v != null ? v : fallback;

        public WidgetSlot Clone() => new WidgetSlot
        {
            Type = Type, Col = Col, Row = Row, W = W, H = H,
            Options = Options == null ? new Dictionary<string, string>() : new Dictionary<string, string>(Options)
        };
    }

    /// <summary>Placing widgets on the 6 × 4 grid without overlaps.</summary>
    public static class Layout
    {
        public const int Cols = 6, MaxRows = 4;

        public static bool Overlaps(WidgetSlot a, int col, int row, int w, int h) =>
            col < a.Col + a.W && a.Col < col + w && row < a.Row + a.H && a.Row < row + h;

        public static bool Fits(int col, int row, int w, int h) =>
            col >= 0 && row >= 0 && col + w <= Cols && row + h <= MaxRows;

        public static bool IsFree(IEnumerable<WidgetSlot> slots, int col, int row, int w, int h, WidgetSlot ignore = null) =>
            Fits(col, row, w, h) && !slots.Any(s => s != ignore && Overlaps(s, col, row, w, h));

        /// <summary>First free place (top to bottom, left to right) for a w × h widget, or null when the page is full.</summary>
        public static (int Col, int Row)? FindFree(IEnumerable<WidgetSlot> slots, int w, int h, WidgetSlot ignore = null)
        {
            var list = slots.ToList();
            for (int r = 0; r + h <= MaxRows; r++)
                for (int c = 0; c + w <= Cols; c++)
                    if (IsFree(list, c, r, w, h, ignore)) return (c, r);
            return null;
        }

        public static int UsedRows(IEnumerable<WidgetSlot> slots) => slots.Select(s => s.Row + s.H).DefaultIfEmpty(0).Max();

        /// <summary>
        /// Puts <paramref name="moving"/> at (col,row) with size w×h. Widgets in the way are moved to the nearest free
        /// place. Returns false (and changes nothing) when it can't all fit.
        /// </summary>
        public static bool TryPlace(List<WidgetSlot> slots, WidgetSlot moving, int col, int row, int w, int h)
        {
            if (!Fits(col, row, w, h)) return false;
            var backup = slots.ToDictionary(s => s, s => (s.Col, s.Row, s.W, s.H));
            moving.Col = col; moving.Row = row; moving.W = w; moving.H = h;
            var displaced = slots.Where(s => s != moving && Overlaps(s, col, row, w, h)).ToList();
            foreach (var d in displaced) { d.Col = -100; d.Row = -100; }       // out of the way while searching
            foreach (var d in displaced.OrderByDescending(d => d.W * d.H))
            {
                var spot = FindFree(slots.Where(s => s.Col >= 0), d.W, d.H, d);
                if (spot == null)
                {
                    foreach (var kv in backup) (kv.Key.Col, kv.Key.Row, kv.Key.W, kv.Key.H) = kv.Value;
                    return false;
                }
                (d.Col, d.Row) = spot.Value;
            }
            return true;
        }
    }
}
