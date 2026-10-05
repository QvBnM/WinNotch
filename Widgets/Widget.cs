using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WinNotch.Widgets
{
    /// <summary>
    /// Content of one widget. The page draws the card around it (background, corners, edit handles); the widget only
    /// fills it, laid out for its current size (Slot.W × Slot.H cells).
    /// </summary>
    internal abstract class Widget : Grid
    {
        protected readonly NotchWindow W;
        protected readonly WidgetSlot Slot;

        protected Widget(NotchWindow w, WidgetSlot slot)
        {
            W = w;
            Slot = slot;
            ClipToBounds = true;
            Background = System.Windows.Media.Brushes.Transparent;     // the whole card reacts to clicks
        }

        protected int Cw => Slot.W;
        protected int Ch => Slot.H;
        /// <summary>1 × 1: room for one number.</summary>
        protected bool Tiny => Slot.W == 1 && Slot.H == 1;
        protected bool Tall => Slot.H >= 2;

        /// <summary>About once a second while the page is visible.</summary>
        public virtual void Refresh() { }

        /// <summary>Every frame (~30/s) while visible; only widgets that animate (music progress) use it.</summary>
        public virtual void Fast() { }

        /// <summary>Media changed (song, play/pause, tabs).</summary>
        public virtual void MediaChanged() { }

        /// <summary>Card padding for this widget (some fill to the edges).</summary>
        public virtual Thickness CardPadding => new Thickness(12, 9, 12, 9);

        // -------- shared helpers --------
        protected static TextBlock Cap(string text) => Ui.Cap(text);

        protected static TextBlock Big(string text, double size = 22, string brush = "InkBrush")
        {
            var t = Ui.T(text, size, brush, true);
            t.TextTrimming = TextTrimming.CharacterEllipsis;
            return t;
        }

        protected static Grid Stack(params UIElement[] rows)
        {
            var g = new Grid { VerticalAlignment = VerticalAlignment.Center };
            for (int i = 0; i < rows.Length; i++)
            {
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                if (rows[i] == null) continue;
                Grid.SetRow(rows[i], i);
                g.Children.Add(rows[i]);
            }
            return g;
        }

        protected void SetContent(UIElement e)
        {
            Children.Clear();
            if (e != null) Children.Add(e);
        }

        // -------- file and app icons (shortcuts) --------
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon; public int iIcon; public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
        }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SHGetFileInfo(string path, uint attr, ref SHFILEINFO info, uint size, uint flags);
        [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr h);

        /// <summary>
        /// \\server\share, //server, file://server/…: opening it (even just to read its icon) makes Windows log in to
        /// that server with your account, which would leak your credentials' hash. Never touched.
        /// </summary>
        public static bool IsNetworkPath(string path)
        {
            var p = (path ?? "").Trim().Trim('"');
            // any two leading separators (\\, //, \/, /\) are a network path for Windows
            if (p.Length >= 2 && (p[0] == '\\' || p[0] == '/') && (p[1] == '\\' || p[1] == '/')) return true;
            return Uri.TryCreate(p, UriKind.Absolute, out var u) && u.IsFile && (u.IsUnc || !string.IsNullOrEmpty(u.Host));
        }

        /// <summary>The icon Windows shows for a file, app or folder (32 px), or null.</summary>
        public static ImageSource FileIcon(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || path.Contains("://") || IsNetworkPath(path)) return null;
                var info = new SHFILEINFO();
                SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), 0x100 /*ICON*/);
                if (info.hIcon == IntPtr.Zero) return null;
                try
                {
                    var src = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    src.Freeze();
                    return src;
                }
                finally { DestroyIcon(info.hIcon); }
            }
            catch { return null; }
        }
    }

    /// <summary>Shown when a page refers to a widget type this version doesn't know.</summary>
    internal sealed class MissingWidget : Widget
    {
        public MissingWidget(NotchWindow w, WidgetSlot s) : base(w, s)
        {
            SetContent(Stack(Cap("WIDGET NECUNOSCUT"), Ui.T(s.Type, 11, "DimBrush")));
        }
    }
}
