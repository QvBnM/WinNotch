using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace WinNotch
{
    /// <summary>
    /// Full-screen overlay over all monitors with a frozen picture of the screen: drag a rectangle, Esc or right-click cancels.
    /// Returns the selected part of the picture (physical pixels), or null.
    /// </summary>
    internal sealed class RegionPicker : Window
    {
        private readonly BitmapSource _shot;
        private readonly Int32Rect _area;
        private readonly Canvas _canvas = new Canvas();
        private readonly Path _dim;
        private readonly Rectangle _frame;
        private readonly Border _size;
        private Point? _start;
        private Border _tip;
        private readonly TaskCompletionSource<BitmapSource> _done = new TaskCompletionSource<BitmapSource>();

        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int hgt, uint flags);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);

        public static Task<BitmapSource> PickAsync(string hint)
        {
            var area = Services.ScreenTools.VirtualScreen();
            var shot = Services.ScreenTools.Capture(area);
            var w = new RegionPicker(shot, area, hint);
            w.Show();
            return w._done.Task;
        }

        private RegionPicker(BitmapSource shot, Int32Rect area, string hint)
        {
            _shot = shot;
            _area = area;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            Cursor = Cursors.Cross;
            Background = Brushes.Black;
            Left = -32000; Top = -32000; Width = 10; Height = 10;
            WindowStartupLocation = WindowStartupLocation.Manual;

            var root = new Grid();
            root.Children.Add(new Image { Source = shot, Stretch = Stretch.Fill });
            _dim = new Path { Fill = new SolidColorBrush(Color.FromArgb(0x88, 0, 0, 0)) };
            _canvas.Children.Add(_dim);
            _frame = new Rectangle { Stroke = Brushes.White, StrokeThickness = 1.5, Visibility = Visibility.Collapsed };
            _canvas.Children.Add(_frame);
            _size = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x10, 0x11, 0x14)), CornerRadius = new CornerRadius(6), Padding = new Thickness(7, 3, 7, 3),
                Child = new TextBlock { Foreground = Brushes.White, FontSize = 12, FontFamily = new FontFamily("Cascadia Mono, Consolas") },
                Visibility = Visibility.Collapsed
            };
            _canvas.Children.Add(_size);
            root.Children.Add(_canvas);

            var tip = _tip = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xF0, 0x0B, 0x0C, 0x0E)), CornerRadius = new CornerRadius(16), Padding = new Thickness(16, 8, 16, 8),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 24, 0, 0), IsHitTestVisible = false,
                Child = new TextBlock { Text = hint + "   ·   Esc anulează", Foreground = Brushes.White, FontSize = 14, FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI") }
            };
            root.Children.Add(tip);
            Content = root;

            SourceInitialized += (o, e) =>
            {
                var h = new WindowInteropHelper(this).Handle;
                // Spanning monitors with different scaling: keep one size, don't let WPF resize the window on DPI change.
                HwndSource.FromHwnd(h).AddHook((IntPtr hwnd, int msg, IntPtr wp, IntPtr lp, ref bool handled) =>
                {
                    if (msg == 0x02E0) handled = true;     // WM_DPICHANGED
                    return IntPtr.Zero;
                });
                SetWindowPos(h, new IntPtr(-1), _area.X, _area.Y, _area.Width, _area.Height, 0x0040);   // HWND_TOPMOST, SHOWWINDOW
            };
            Loaded += (o, e) =>
            {
                Activate();
                SetForegroundWindow(new WindowInteropHelper(this).Handle);
                Focus();
                Keyboard.Focus(this);
                UpdateDim(Rect.Empty);
                // The hint goes at the top of the monitor you're on, not in the middle of all monitors (often the seam).
                var mon = Services.ScreenTools.MonitorUnderMouse();
                double sx = ActualWidth / Math.Max(1, _area.Width), sy = ActualHeight / Math.Max(1, _area.Height);
                _tip.HorizontalAlignment = HorizontalAlignment.Left;
                _tip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                _tip.Margin = new Thickness(Math.Max(8, (mon.X - _area.X + mon.Width / 2.0) * sx - _tip.DesiredSize.Width / 2), (mon.Y - _area.Y) * sy + 24, 0, 0);
            };
            SizeChanged += (o, e) => UpdateDim(Rect.Empty);
            KeyDown += (o, e) => { if (e.Key == Key.Escape) Finish(null); };
            // Closed any other way (Alt+F4, the system): still answer, so the notch comes back and the tools keep working.
            Closed += (o, e) => _done.TrySetResult(null);
            LostMouseCapture += (o, e) => { if (_start != null && Mouse.LeftButton != MouseButtonState.Pressed) { _start = null; Finish(null); } };
            MouseRightButtonUp += (o, e) => Finish(null);
            Deactivated += (o, e) => { if (!_done.Task.IsCompleted && _start == null) Finish(null); };
            MouseLeftButtonDown += (o, e) => { _start = e.GetPosition(this); CaptureMouse(); };
            MouseMove += (o, e) => { if (_start != null) ShowSel(Sel(e.GetPosition(this))); };
            MouseLeftButtonUp += (o, e) =>
            {
                if (_start == null) return;
                var r = Sel(e.GetPosition(this));
                _start = null;                 // before releasing capture, so LostMouseCapture doesn't treat it as a cancel
                ReleaseMouseCapture();
                if (r.Width < 4 || r.Height < 4) { Finish(null); return; }
                Finish(Crop(r));
            };
        }

        private Rect Sel(Point p) => new Rect(_start.Value, p);

        private void ShowSel(Rect r)
        {
            _frame.Visibility = Visibility.Visible;
            Canvas.SetLeft(_frame, r.X); Canvas.SetTop(_frame, r.Y);
            _frame.Width = r.Width; _frame.Height = r.Height;
            UpdateDim(r);
            var px = ToPixels(r);
            ((TextBlock)_size.Child).Text = px.Width + " × " + px.Height;
            _size.Visibility = Visibility.Visible;
            _size.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double lw = _size.DesiredSize.Width, lh = _size.DesiredSize.Height;
            Canvas.SetLeft(_size, Math.Clamp(r.X, 0, Math.Max(0, ActualWidth - lw)));
            Canvas.SetTop(_size, r.Y >= lh + 6 ? r.Y - lh - 6 : r.Bottom + 6 + lh <= ActualHeight ? r.Bottom + 6 : r.Y + 6);
        }

        private void UpdateDim(Rect hole)
        {
            var all = new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight));
            _dim.Data = hole.IsEmpty ? (Geometry)all : new CombinedGeometry(GeometryCombineMode.Exclude, all, new RectangleGeometry(hole));
        }

        /// <summary>Window coordinates → picture pixels (the picture is stretched over the window, so a ratio is enough).</summary>
        private Int32Rect ToPixels(Rect r)
        {
            double sx = _shot.PixelWidth / Math.Max(1, ActualWidth), sy = _shot.PixelHeight / Math.Max(1, ActualHeight);
            int x = (int)Math.Round(r.X * sx), y = (int)Math.Round(r.Y * sy);
            int w = (int)Math.Round(r.Width * sx), h = (int)Math.Round(r.Height * sy);
            x = Math.Clamp(x, 0, _shot.PixelWidth - 1);
            y = Math.Clamp(y, 0, _shot.PixelHeight - 1);
            w = Math.Clamp(w, 1, _shot.PixelWidth - x);
            h = Math.Clamp(h, 1, _shot.PixelHeight - y);
            return new Int32Rect(x, y, w, h);
        }

        private BitmapSource Crop(Rect r)
        {
            var c = new CroppedBitmap(_shot, ToPixels(r));
            c.Freeze();
            return c;
        }

        private void Finish(BitmapSource result)
        {
            if (_done.Task.IsCompleted) return;
            _done.TrySetResult(result);
            Close();
        }
    }
}
