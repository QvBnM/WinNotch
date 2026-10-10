using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using WinNotch.Features.NotchAnchored;

namespace WinNotch.Features.WindowV2
{
    /// <summary>
    /// P70: the window's signature — a slice of a monitor at the top of the window, with the notch on it. It is not a
    /// picture: it is drawn with the real outline (<see cref="AnchoredShape.Silhouette"/>, the same translator the
    /// notch itself draws through), at the real corner radius, and it moves and shrinks as you touch the settings
    /// under it. Hover "Poziție" and the pill slides; hover "Se micșorează singur" and it shrinks to the small form.
    /// <para>Where the pill lands is <see cref="PreviewModel"/>'s job (pure, tested); this only draws it.</para>
    /// </summary>
    internal sealed class NotchPreview : Border
    {
        private readonly Canvas _canvas = new Canvas
        {
            Width = PreviewModel.FrameWidth, Height = PreviewModel.FrameHeight, ClipToBounds = true,
        };
        private readonly Grid _pillHost = new Grid { VerticalAlignment = VerticalAlignment.Top };
        private readonly Path _shape = new Path { IsHitTestVisible = false };
        private readonly Border _dot = new Border { Width = 6, Height = 6, CornerRadius = new CornerRadius(3), VerticalAlignment = VerticalAlignment.Center };
        private readonly TextBlock _items = Ui.T("", 11, "MutedBrush");
        private readonly TextBlock _clock = Ui.T("--:--", 11, "MutedBrush", false, true);
        private readonly Grid _inside = new Grid();

        internal NotchPreview()
        {
            Width = PreviewModel.FrameWidth;
            Height = PreviewModel.FrameHeight;
            CornerRadius = new CornerRadius(12, 12, 4, 4);
            BorderThickness = new Thickness(1);
            SnapsToDevicePixels = true;
            SetResourceReference(BackgroundProperty, "SegBrush");
            SetResourceReference(BorderBrushProperty, "BorderBrush");
            System.Windows.Automation.AutomationProperties.SetName(this, "Previzualizarea notch-ului");

            _shape.SetResourceReference(Shape.FillProperty, "NotchBrush");
            _shape.SetResourceReference(Shape.StrokeProperty, "BorderBrush");
            _shape.StrokeThickness = 1;
            _dot.SetResourceReference(Border.BackgroundProperty, "TrackBrush");

            _items.VerticalAlignment = VerticalAlignment.Center;
            _items.TextTrimming = TextTrimming.CharacterEllipsis;
            _clock.VerticalAlignment = VerticalAlignment.Center;
            _clock.HorizontalAlignment = HorizontalAlignment.Right;

            _inside.ColumnDefinitions.Add(new ColumnDefinition { Width = Ui.Auto });
            _inside.ColumnDefinitions.Add(new ColumnDefinition { Width = Ui.Star() });
            _inside.ColumnDefinitions.Add(new ColumnDefinition { Width = Ui.Auto });
            _inside.Put(_dot);
            _items.Margin = new Thickness(9, 0, 9, 0);
            _inside.Put(_items, 1);
            _inside.Put(_clock, 2);

            _pillHost.Children.Add(_shape);
            _pillHost.Children.Add(_inside);
            _canvas.Children.Add(_pillHost);
            Child = _canvas;
        }

        /// <summary>The time in the pill; the caller owns the clock, this only writes it.</summary>
        internal void SetClock(string text) => _clock.Text = text ?? "";

        /// <summary>
        /// Draws the pill for a position and a form. <paramref name="cornerRadius"/> is the user's own setting, so the
        /// preview rounds exactly like the real notch; <paramref name="anchored"/> adds the fillets of P50 when that
        /// feature is on. <paramref name="items"/> is what the user keeps in standby, written out.
        /// </summary>
        internal void Render(string position, bool mini, double cornerRadius, bool anchored, string items)
        {
            var box = PreviewModel.Pill(position, mini);
            double r = AnchoredGeometry.Radius(cornerRadius);
            // The preview is about a third of the real pill, so the radius is scaled with it; otherwise a 28 px corner
            // on a 34 px tall preview swallows the whole shape.
            r = Math.Max(4, Math.Min(r * 0.4, box.Height / 2));
            double e = anchored ? AnchoredGeometry.Ear(cornerRadius, box.Width, PreviewModel.FrameWidth, box.Height) * 0.4 : 0;

            _shape.Data = AnchoredShape.Silhouette(box.Width, box.Height, r, e);
            _pillHost.Width = box.Width + 2 * Math.Max(0, e);
            _pillHost.Height = box.Height;
            _inside.Margin = new Thickness(Math.Max(0, e) + 11, 0, Math.Max(0, e) + 11, 0);
            _inside.Height = box.Height;

            _items.Visibility = mini ? Visibility.Collapsed : Visibility.Visible;
            _items.Text = items ?? "";

            Canvas.SetLeft(_pillHost, Math.Max(0, box.X - Math.Max(0, e)));
            Canvas.SetTop(_pillHost, 0);
        }

        /// <summary>What the user keeps in standby, written for the preview: "Muzică · Ora · Vremea".</summary>
        internal static string ItemsLine(IEnumerable<string> ids)
        {
            if (ids == null) return "";
            var names = new List<string>();
            foreach (var id in ids)
            {
                foreach (var (wid, name) in AppSettings.Widgets)
                    if (string.Equals(wid, id, StringComparison.Ordinal)) { names.Add(name); break; }
                if (names.Count >= 3) break;
            }
            return string.Join(" · ", names);
        }
    }
}
