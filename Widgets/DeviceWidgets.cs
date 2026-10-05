using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WinNotch.Panes;
using WinNotch.Services;

namespace WinNotch.Widgets
{
    /// <summary>Microfon și cameră: who uses them now and for how long; 2×2 also has the mic on/off button.</summary>
    internal sealed class PrivacyWidget : Widget
    {
        private readonly TextBlock _mic, _micWho, _cam, _camWho;
        private readonly Button _micBtn;

        public PrivacyWidget(NotchWindow w, WidgetSlot s) : base(w, s)
        {
            _mic = Ui.T("", 12.5, "InkBrush", true);
            _micWho = Ui.T("", 11.5, "MutedBrush");
            _cam = Ui.T("", 12.5, "InkBrush", true);
            _camWho = Ui.T("", 11.5, "MutedBrush");
            var micRow = Ui.Cols(Ui.Auto, Ui.Px(8), Ui.Star());
            micRow.Put(Ui.Icon(Ui.GMic, 15)); micRow.Put(Ui.V(0, _mic, _micWho), 2);
            if (!Tall) { Children.Add(micRow.Also(r => r.VerticalAlignment = VerticalAlignment.Center)); Refresh(); return; }
            var camRow = Ui.Cols(Ui.Auto, Ui.Px(8), Ui.Star());
            camRow.Put(Ui.Icon(Ui.GCam, 15)); camRow.Put(Ui.V(0, _cam, _camWho), 2);
            _micBtn = Ui.PillBtn("Oprește microfonul", () => { W.Audio.MicMuted = !W.Audio.MicMuted; Refresh(); });
            _micBtn.HorizontalAlignment = HorizontalAlignment.Left;
            Children.Add(Ui.V(8, micRow, camRow, _micBtn));
            Refresh();
        }

        public override void Refresh()
        {
            var mic = PrivacyService.Microphone();
            bool muted = W.Audio.MicMuted;
            _mic.Text = mic.InUse ? (muted ? "Microfon în uz · fără sunet" : "Microfon pornit") : muted ? "Microfon oprit" : "Microfon neutilizat";
            _mic.Foreground = mic.InUse ? Ui.B("WarnBrush") : Ui.B("InkBrush");
            _micWho.Text = mic.InUse ? string.Join(", ", mic.Apps) + " · de " + DevicesPane.Since(mic.Since)
                                     : mic.LastApp != null ? "ultima dată: " + mic.LastApp : "";
            if (_micBtn == null) return;
            var cam = PrivacyService.Camera();
            _cam.Text = cam.InUse ? "Cameră pornită" : "Cameră oprită";
            _cam.Foreground = cam.InUse ? Ui.B("OkBrush") : Ui.B("InkBrush");
            _camWho.Text = cam.InUse ? string.Join(", ", cam.Apps) + " · de " + DevicesPane.Since(cam.Since) : cam.LastApp != null ? "ultima dată: " + cam.LastApp : "";
            _micBtn.Content = muted ? "Pornește microfonul" : "Oprește microfonul";
        }
    }

    /// <summary>Audio: the default output and input devices.</summary>
    internal sealed class AudioDevicesWidget : Widget
    {
        private readonly TextBlock _out, _in;
        public AudioDevicesWidget(NotchWindow w, WidgetSlot s) : base(w, s)
        {
            _out = Ui.T("", 12); _in = Ui.T("", 12, "MutedBrush");
            var g = Ui.Cols(Ui.Auto, Ui.Px(8), Ui.Star());
            g.RowDefinitions.Add(new RowDefinition { Height = Ui.Auto }); g.RowDefinitions.Add(new RowDefinition { Height = Ui.Auto });
            g.Put(Ui.Icon(Ui.GHeadphones, 13, Ui.B("MutedBrush"))); g.Put(_out, 2);
            g.Put(Ui.Icon(Ui.GMic, 13, Ui.B("MutedBrush")), 0, 1); g.Put(_in, 2, 1);
            g.VerticalAlignment = VerticalAlignment.Center;
            Children.Add(g);
            Refresh();
        }
        private int _tick;
        public override void Refresh()
        {
            if (_tick++ % 3 != 0) return;      // device names don't change often
            _out.Text = W.Audio.OutputName;
            _in.Text = W.Audio.InputName;
        }
    }

    /// <summary>Conectate: Bluetooth, USB, monitors and drives, scrollable, with "Scoate" for drives.</summary>
    internal sealed class ConnectedWidget : Widget
    {
        private readonly StackPanel _list = new StackPanel();
        private readonly TextBlock _cap;
        private DateTime _scanAt = DateTime.MinValue;
        private string _sig;
        private bool _scanning;

        public ConnectedWidget(NotchWindow w, WidgetSlot s) : base(w, s)
        {
            _cap = Cap("CONECTATE");
            Children.Add(Ui.Rows(Ui.Auto, Ui.Px(6), Ui.Star()).Also(g => { g.Put(_cap); g.Put(new ScrollViewer { Style = Ui.S("SlimScroll"), Content = _list }, 0, 2); }));
            Refresh();
        }

        public override async void Refresh()
        {
            if (_scanning || DateTime.Now - _scanAt < TimeSpan.FromSeconds(6)) return;
            _scanning = true;
            List<ConnectedDevice> devices;
            try { devices = await DeviceService.ScanAsync(); }
            catch { devices = new List<ConnectedDevice>(); }
            finally { _scanning = false; _scanAt = DateTime.Now; }
            string sig = string.Join("|", devices.Select(d => d.Kind + d.Name + d.Detail));
            if (sig == _sig) return;
            _sig = sig;
            _cap.Text = "CONECTATE · " + devices.Count;
            _list.Children.Clear();
            foreach (var d in devices.OrderBy(d => DevicesPane.Order(d.Kind)))
            {
                var g = Ui.Cols(Ui.Px(22), Ui.Px(8), Ui.Star(), Ui.Auto);
                g.Put(new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(6), Background = Ui.B("HoverBrush"), Child = Ui.Icon(DevicesPane.Glyph(d.Kind), 11, Ui.B("MutedBrush")) });
                g.Put(Ui.V(0, Ui.T(d.Name, 12), Ui.T(d.Detail, 11, "DimBrush")).Also(v => v.VerticalAlignment = VerticalAlignment.Center), 2);
                if (d.Kind == DeviceKind.Drive && d.DriveRoot != null)
                {
                    var root = d.DriveRoot;
                    g.Put(Ui.PillBtn("Scoate", () => { if (DeviceService.Eject(root)) { _scanAt = DateTime.MinValue; _sig = null; } }), 3);
                }
                _list.Children.Add(new Border { Background = Ui.B("ChipHoverBrush"), CornerRadius = new CornerRadius(8), Padding = new Thickness(7, 4, 7, 4), Margin = new Thickness(0, 0, 4, 4), Child = g });
            }
            if (devices.Count == 0) _list.Children.Add(Ui.T("Nu am găsit dispozitive conectate", 12, "DimBrush"));
        }
    }
}
