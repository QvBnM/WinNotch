using System;
using System.Diagnostics;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace WinNotch
{
    /// <summary>Icon next to the clock: open settings, run as admin (CPU temperatures), quit.</summary>
    internal sealed class TrayIcon : IDisposable
    {
        private readonly Forms.NotifyIcon _icon;
        private readonly Drawing.Icon _img;

        public TrayIcon(App app)
        {
            _img = MakeIcon();
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("Deschide notch-ul  (Win+Alt+N)", null, (s, e) => app.ToggleNotch());
            menu.Items.Add("Pagini, teme și setări…", null, (s, e) => app.OpenEditor());
            // CPU temperature through the small SYSTEM helper; WinNotch itself never needs to run as administrator
            if (!App.IsAdmin && !Services.TempHelper.Installed)
                menu.Items.Add("Activează temperatura procesorului…", null, (s, e) => app.InstallTempHelper());
            menu.Items.Add("Deschide folderul cu setări și log", null, (s, e) => Services.Shell.Open(AppSettings.Folder));
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("Ieșire", null, (s, e) => app.ExitApp());

            _icon = new Forms.NotifyIcon
            {
                Icon = _img,
                Text = "WinNotch",
                Visible = true,
                ContextMenuStrip = menu
            };
            _icon.DoubleClick += (s, e) => app.OpenSettings();
        }

        /// <summary>Draws the tray icon: a small black pill with a light outline and an amber dot.</summary>
        private static Drawing.Icon MakeIcon()
        {
            using var bmp = new Drawing.Bitmap(32, 32);
            using (var g = Drawing.Graphics.FromImage(bmp))
            {
                g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(Drawing.Color.Transparent);
                using var path = new Drawing.Drawing2D.GraphicsPath();
                var r = new Drawing.Rectangle(2, 9, 28, 14);
                int d = 14;
                path.AddArc(r.X, r.Y, d, d, 90, 180);
                path.AddArc(r.Right - d, r.Y, d, d, 270, 180);
                path.CloseFigure();
                using var fill = new Drawing.SolidBrush(Drawing.Color.Black);
                using var pen = new Drawing.Pen(Drawing.Color.FromArgb(220, 230, 230, 230), 1.6f);
                g.FillPath(fill, path);
                g.DrawPath(pen, path);
                using var dot = new Drawing.SolidBrush(Drawing.Color.FromArgb(245, 165, 36));
                g.FillEllipse(dot, 20, 13, 6, 6);
            }
            return Drawing.Icon.FromHandle(bmp.GetHicon());
        }

        public void Dispose()
        {
            _icon.Visible = false;
            _icon.Dispose();
            _img.Dispose();
        }
    }
}
