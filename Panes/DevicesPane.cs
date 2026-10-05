using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WinNotch.Services;

namespace WinNotch.Panes
{
    /// <summary>Dispozitive: who uses the microphone / camera, audio in and out, everything connected (scrollable).</summary>
    internal sealed class DevicesPane : Pane
    {
        public override double PanelHeight => 330;

        private readonly Border _micBox, _camBox;
        private readonly TextBlock _micState, _micWho, _camState, _camWho, _out, _in;
        private readonly Button _micBtn;
        private readonly StackPanel _list = new StackPanel();
        private readonly TextBlock _listCap;
        private DateTime _scanAt = DateTime.MinValue;
        private bool _scanning;
        private string _listSig;

        public DevicesPane(NotchWindow w) : base(w)
        {
            ColumnDefinitions.Add(new ColumnDefinition { Width = Ui.Px(252) });
            ColumnDefinitions.Add(new ColumnDefinition { Width = Ui.Px(10) });
            ColumnDefinitions.Add(new ColumnDefinition { Width = Ui.Star() });

            // ---------------- privacy ----------------
            _micState = Ui.T("", 11, "DimBrush");
            _micWho = Ui.T("", 11.5, "MutedBrush");
            _micBtn = Ui.PillBtn("Oprește", ToggleMic, true);
            var micHead = Ui.Cols(Ui.Auto, Ui.Px(8), Ui.Star(), Ui.Auto);
            micHead.Put(Ui.Icon(Ui.GMic, 15));
            micHead.Put(Ui.T("Microfon", 12.5, "InkBrush", true), 2);
            micHead.Put(_micState, 3);
            var micFoot = Ui.Cols(Ui.Star(), Ui.Auto);
            micFoot.Put(_micWho);
            micFoot.Put(_micBtn, 1);
            _micBox = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(10, 9, 10, 9), BorderThickness = new Thickness(1), Child = Ui.V(6, micHead, micFoot) };

            _camState = Ui.T("", 11, "DimBrush");
            _camWho = Ui.T("", 11, "DimBrush");
            var camHead = Ui.Cols(Ui.Auto, Ui.Px(8), Ui.Star(), Ui.Auto);
            camHead.Put(Ui.Icon(Ui.GCam, 15));
            camHead.Put(Ui.T("Cameră", 12.5, "InkBrush", true), 2);
            camHead.Put(_camState, 3);
            _camBox = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(10, 9, 10, 9), BorderThickness = new Thickness(1), Child = Ui.V(4, camHead, _camWho) };

            _out = Ui.T("", 11);
            _in = Ui.T("", 11);
            var audio = Ui.Cols(Ui.Auto, Ui.Px(8), Ui.Star());
            audio.RowDefinitions.Add(new RowDefinition { Height = Ui.Px(18) });
            audio.RowDefinitions.Add(new RowDefinition { Height = Ui.Px(18) });
            audio.Put(Ui.T("Ieșire audio", 11, "DimBrush"));
            audio.Put(_out, 2);
            audio.Put(Ui.T("Intrare audio", 11, "DimBrush"), 0, 1);
            audio.Put(_in, 2, 1);
            _out.HorizontalAlignment = _in.HorizontalAlignment = HorizontalAlignment.Right;

            var privacy = Ui.Rows(Ui.Auto, Ui.Px(8), Ui.Auto, Ui.Px(8), Ui.Auto, Ui.Star(), Ui.Auto);
            privacy.Put(Ui.Cap("CONFIDENȚIALITATE"));
            privacy.Put(_micBox, 0, 2);
            privacy.Put(_camBox, 0, 4);
            privacy.Put(audio, 0, 6);
            this.Put(Ui.Card(privacy, 12, 10));

            // ---------------- connected devices ----------------
            _listCap = Ui.Cap("CONECTATE");
            var head = Ui.Cols(Ui.Star(), Ui.Auto);
            head.Put(_listCap);
            head.Put(Ui.T("Bluetooth · USB · monitoare · stick-uri", 10.5, "DimBrush"), 1);
            var right = Ui.Rows(Ui.Auto, Ui.Px(6), Ui.Star());
            right.Put(head);
            right.Put(new ScrollViewer { Style = Ui.S("SlimScroll"), Content = _list }, 0, 2);
            this.Put(Ui.Card(right, 12, 10), 2);
        }

        public override void Shown()
        {
            _scanAt = DateTime.MinValue;
            Refresh();
        }

        public override void Refresh()
        {
            var mic = PrivacyService.Microphone();
            var cam = PrivacyService.Camera();
            bool micMuted = W.Audio.MicMuted;

            if (mic.InUse)
            {
                _micBox.Background = Ui.Rgb(0xFF, 0x9F, 0x43, 0x1F);
                _micBox.BorderBrush = Ui.Rgb(0xFF, 0x9F, 0x43, 0x73);
                _micState.Text = micMuted ? "în uz · fără sunet" : "● în uz";
                _micState.Foreground = Ui.B("WarnBrush");
                _micWho.Text = string.Join(", ", mic.Apps) + " · de " + Since(mic.Since);
            }
            else
            {
                _micBox.Background = Ui.B("ChipHoverBrush");
                _micBox.BorderBrush = Brushes.Transparent;
                _micState.Text = micMuted ? "oprit" : "neutilizat";
                _micState.Foreground = Ui.B("DimBrush");
                _micWho.Text = mic.LastApp != null ? "Ultima dată: " + mic.LastApp + ", " + When(mic.LastUsed) : "Nicio aplicație nu l-a folosit încă";
            }
            _micBtn.Content = micMuted ? "Pornește" : "Oprește";
            _micBtn.Style = Ui.S(micMuted ? "GhostPill" : "AccentPill");

            if (cam.InUse)
            {
                _camBox.Background = Ui.Rgb(0x3D, 0xDC, 0x84, 0x1F);
                _camBox.BorderBrush = Ui.Rgb(0x3D, 0xDC, 0x84, 0x73);
                _camState.Text = "● pornită";
                _camState.Foreground = Ui.B("OkBrush");
                _camWho.Text = string.Join(", ", cam.Apps) + " · de " + Since(cam.Since);
            }
            else
            {
                _camBox.Background = Ui.B("ChipHoverBrush");
                _camBox.BorderBrush = Brushes.Transparent;
                _camState.Text = "oprită";
                _camState.Foreground = Ui.B("DimBrush");
                _camWho.Text = cam.LastApp != null ? "Ultima dată: " + cam.LastApp + ", " + When(cam.LastUsed) : "Nicio aplicație nu a folosit-o încă";
            }

            _out.Text = W.Audio.OutputName;
            _in.Text = W.Audio.InputName;

            if (!_scanning && DateTime.Now - _scanAt > TimeSpan.FromSeconds(6)) Scan();
        }

        private void ToggleMic()
        {
            W.Audio.MicMuted = !W.Audio.MicMuted;
            Refresh();
        }

        private async void Scan()
        {
            _scanning = true;
            _scanAt = DateTime.Now;
            List<ConnectedDevice> devices;
            try { devices = await DeviceService.ScanAsync(); }
            catch (Exception ex) { App.Log("Dispozitive: " + ex.Message); _scanning = false; return; }
            _scanning = false;
            string sig = string.Join("|", devices.Select(d => d.Name + d.Detail));
            if (sig == _listSig) return;
            _listSig = sig;
            _listCap.Text = "CONECTATE · " + devices.Count;
            _list.Children.Clear();
            foreach (var d in devices.OrderBy(d => Order(d.Kind))) _list.Children.Add(Row(d));
            if (devices.Count == 0) _list.Children.Add(Ui.T("Nu am găsit dispozitive conectate", 12, "DimBrush"));
        }

        internal static int Order(DeviceKind k) => k switch
        {
            DeviceKind.Headphones => 0, DeviceKind.Mouse => 1, DeviceKind.Keyboard => 2, DeviceKind.Gamepad => 3,
            DeviceKind.Phone => 4, DeviceKind.Bluetooth => 5, DeviceKind.Monitor => 6, DeviceKind.Drive => 7, _ => 8
        };

        internal static string Glyph(DeviceKind k) => k switch
        {
            DeviceKind.Headphones => Ui.GHeadphones, DeviceKind.Mouse => Ui.GMouse, DeviceKind.Keyboard => Ui.GKeyboard,
            DeviceKind.Gamepad => Ui.GGamepad, DeviceKind.Phone => Ui.GPhone, DeviceKind.Monitor => Ui.GMonitor,
            DeviceKind.Drive => Ui.GDrive, DeviceKind.Camera => Ui.GCam, DeviceKind.Printer => Ui.GPrinter,
            DeviceKind.Bluetooth => Ui.GBluetooth, _ => Ui.GUsb
        };

        private FrameworkElement Row(ConnectedDevice d)
        {
            var g = Ui.Cols(Ui.Px(24), Ui.Px(10), Ui.Star(), Ui.Auto);
            g.Put(new Border { Width = 24, Height = 24, CornerRadius = new CornerRadius(7), Background = Ui.B("HoverBrush"), Child = Ui.Icon(Glyph(d.Kind), 12, Ui.B("MutedBrush")) });
            var texts = Ui.V(0, Ui.T(d.Name, 12), Ui.T(d.Detail, 10.5, "DimBrush"));
            texts.VerticalAlignment = VerticalAlignment.Center;
            g.Put(texts, 2);
            if (d.Kind == DeviceKind.Drive && d.DriveRoot != null)
            {
                var root = d.DriveRoot;
                var eject = Ui.PillBtn("Scoate", () => { if (DeviceService.Eject(root)) { _scanAt = DateTime.MinValue; _listSig = null; } });
                eject.BorderThickness = new Thickness(1);
                eject.BorderBrush = Ui.B("TrackBrush");
                g.Put(eject, 3);
            }
            return new Border { Background = Ui.B("ChipHoverBrush"), CornerRadius = new CornerRadius(9), Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(0, 0, 4, 5), Child = g };
        }

        internal static string Since(DateTime t)
        {
            var d = DateTime.Now - t;
            if (d.TotalMinutes < 1) return "câteva secunde";
            if (d.TotalHours < 1) return (int)d.TotalMinutes + " min";
            return (int)d.TotalHours + " h " + d.Minutes + " min";
        }

        internal static string When(DateTime t)
        {
            if (t.Date == DateTime.Today) return "azi la " + t.ToString("HH:mm");
            if (t.Date == DateTime.Today.AddDays(-1)) return "ieri la " + t.ToString("HH:mm");
            return t.ToString("d MMM", NotchWindow.Ro);
        }
    }
}
