using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WinNotch.Panes;
using WinNotch.Services;

namespace WinNotch.Features.PagesFormat
{
    /// <summary>
    /// P75, „Dispozitive”, after `docs/design/04-pagina-dispozitive.png`: privacy first, in two cards you cannot miss —
    /// the microphone and the camera, each with who is using it and for how long — then the default audio pair; on the
    /// right, one tall card with everything that is plugged in, each line ending in what we know about it, engraved.
    ///
    /// The old <see cref="DevicesPane"/> stays exactly as it is and is what the notch shows while the switch is off.
    /// </summary>
    internal sealed class DevicesPage : Pane
    {
        public override double PanelHeight => PageFormat.DevicesHeight;

        private readonly Border _micIcon, _camIcon, _micBtn;
        private readonly TextBlock _micTitle, _micWho, _camTitle, _camWho, _micBtnText, _out, _in, _count;
        private readonly StackPanel _list;
        private bool _scanning;
        private DateTime _scanAt = DateTime.MinValue;
        private string _listSig = "";

        public DevicesPage(NotchWindow w) : base(w)
        {
            var cols = Deck.Columns(PageFormat.DevicesSplit, out var left, out var right);
            Children.Add(cols);

            // ---------- left: the two privacy cards, then the audio pair ----------
            _micTitle = Deck.T(Ink.Title, "Microfonul");
            _micWho = Deck.T(Ink.Sub, "");
            _micBtnText = Deck.T(Ink.Name, "Oprește pentru toate");
            _micBtn = Deck.Chip(_micBtnText, 11, 4);
            _micBtn.Cursor = Cursors.Hand;
            _micBtn.MouseLeftButtonUp += (o, e) => { W.Audio.MicMuted = !W.Audio.MicMuted; Refresh(); e.Handled = true; };
            var micCard = Deck.Card(Capability(out _micIcon, Ui.GMic, _micTitle, _micWho, _micBtn));

            _camTitle = Deck.T(Ink.Title, "Camera");
            _camWho = Deck.T(Ink.Sub, "");
            var camCard = Deck.Card(Capability(out _camIcon, Ui.GCam, _camTitle, _camWho, null));
            camCard.Margin = new Thickness(0, PageFormat.CardGap, 0, 0);

            _out = Deck.T(Ink.Title, "—");
            _out.FontSize = 12;
            _in = Deck.T(Ink.Sub, "—");
            _in.FontSize = 12;
            var audioCard = Deck.Card(Deck.Stack(0,
                Deck.T(Ink.Eyebrow, "Audio"),
                Pad(AudioLine("ieșire", _out, Change()), 10),
                Pad(AudioLine("intrare", _in, null), 8)));
            audioCard.Margin = new Thickness(0, PageFormat.CardGap, 0, 0);

            var leftStack = new Grid();
            leftStack.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            leftStack.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            leftStack.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            leftStack.Children.Add(micCard);
            Grid.SetRow(camCard, 1);
            leftStack.Children.Add(camCard);
            Grid.SetRow(audioCard, 2);
            leftStack.Children.Add(audioCard);
            left.Children.Add(leftStack);

            // ---------- right: one tall card with everything that is plugged in ----------
            _count = Deck.T(Ink.Unit, "0");
            _list = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
            // A Grid with a star row, not a StackPanel: inside a StackPanel the available height is infinite, so a
            // ScrollViewer never gets a bound, never scrolls, and the card keeps growing with the list — the content
            // never settles and the notch's own safety net (B1) sees a broken panel and rebuilds it, over and over.
            var listGrid = new Grid();
            listGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            listGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            listGrid.Children.Add(Deck.Head("Conectate", _count));
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = _list,
            };
            Grid.SetRow(scroll, 1);
            listGrid.Children.Add(scroll);
            right.Children.Add(Deck.Card(listGrid));
        }

        private static FrameworkElement Pad(FrameworkElement e, double top)
        {
            e.Margin = new Thickness(0, top, 0, 0);
            return e;
        }

        private Border Change()
        {
            var b = Deck.Chip(Deck.T(Ink.Name, "Schimbă"), 11, 3);
            b.Cursor = Cursors.Hand;
            b.MouseLeftButtonUp += (o, e) => { Shell.Open("ms-settings:sound"); e.Handled = true; };
            return b;
        }

        /// <summary>A privacy card: the square glyph, the title and who is using it, and whatever button it has.</summary>
        private static Grid Capability(out Border icon, string glyph, TextBlock title, TextBlock who, UIElement button)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var mark = Ui.Icon(glyph, 15);
            icon = new Border
            {
                Width = 34, Height = 34, CornerRadius = new CornerRadius(11),
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
                Child = mark,
            };
            icon.SetResourceReference(Border.BackgroundProperty, "ChipHoverBrush");
            g.Children.Add(icon);

            var texts = Deck.Stack(2, title, who);
            texts.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(texts, 1);
            g.Children.Add(texts);

            if (button != null)
            {
                Grid.SetColumn(button, 2);
                g.Children.Add(button);
            }
            return g;
        }

        /// <summary>One line of the audio card: the engraved word, the device, and the button that changes it.</summary>
        private static Grid AudioLine(string word, TextBlock name, UIElement button)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(54) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.Children.Add(Deck.T(Ink.Label, word));
            Grid.SetColumn(name, 1);
            g.Children.Add(name);
            if (button != null)
            {
                Grid.SetColumn(button, 2);
                g.Children.Add(button);
            }
            return g;
        }

        public override void Shown()
        {
            _scanAt = DateTime.MinValue;
            Refresh();
        }

        public override void Refresh() => NotchWindow.GuardFormat(RefreshCore);

        private void RefreshCore()
        {
            var mic = PrivacyService.Microphone();
            var cam = PrivacyService.Camera();
            bool micMuted = W.Audio.MicMuted;

            // In use, the square lights up in the privacy colour; free, it is as quiet as any other chip. The plates
            // say it in one move: what is live gets the accent, the rest stays grey.
            _micTitle.Text = mic.InUse ? (micMuted ? "Microfonul e folosit · fără sunet" : "Microfonul e folosit") : "Microfonul e liber";
            _micWho.Text = mic.InUse
                ? string.Join(", ", mic.Apps) + " · de " + DevicesPane.Since(mic.Since)
                : mic.LastApp != null ? "ultima dată: " + mic.LastApp + ", " + DevicesPane.When(mic.LastUsed) : "nicio aplicație nu l-a folosit încă";
            _micIcon.SetResourceReference(Border.BackgroundProperty, mic.InUse ? "AccentBrush" : "ChipHoverBrush");
            _micBtnText.Text = micMuted ? "Pornește microfonul" : "Oprește pentru toate";

            _camTitle.Text = cam.InUse ? "Camera e pornită" : "Camera e liberă";
            _camWho.Text = cam.InUse
                ? string.Join(", ", cam.Apps) + " · de " + DevicesPane.Since(cam.Since)
                : cam.LastApp != null ? "ultima dată: " + cam.LastApp + ", " + DevicesPane.When(cam.LastUsed) : "nicio aplicație nu a folosit-o încă";
            _camIcon.SetResourceReference(Border.BackgroundProperty, cam.InUse ? "OkBrush" : "ChipHoverBrush");

            _out.Text = W.Audio.OutputName;
            _in.Text = "Microfon " + W.Audio.InputName;

            if (!_scanning && DateTime.Now - _scanAt > TimeSpan.FromSeconds(6)) Scan();
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
            _count.Text = devices.Count.ToString();
            _list.Children.Clear();
            foreach (var d in devices) _list.Children.Add(DeviceRow(d));
            if (devices.Count == 0) _list.Children.Add(Deck.T(Ink.Label, "Nimic conectat"));
        }

        /// <summary>
        /// One device: the square, the name, and what we know about it at the right end, engraved — the resolution,
        /// the bus, the free space. A battery is the one reading that moves, so it is the one that gets the accent.
        /// </summary>
        private FrameworkElement DeviceRow(ConnectedDevice d)
        {
            var g = new Grid { Height = 30 };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(29) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var square = new Border
            {
                Width = 20, Height = 20, CornerRadius = new CornerRadius(7),
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
                Child = Ui.Icon(Glyph(d.Kind), 10),
            };
            bool battery = Battery(d.Detail) != null;
            square.SetResourceReference(Border.BackgroundProperty, battery ? "AccentBrush" : "ChipHoverBrush");
            g.Children.Add(square);

            var name = Deck.T(Ink.Name, d.Name);
            Grid.SetColumn(name, 1);
            g.Children.Add(name);

            string batt = Battery(d.Detail);
            var detail = Deck.T(Ink.Unit, batt ?? Shorten(d.Detail), batt != null ? PageFormat.Reading.Live : PageFormat.Reading.Plain);
            detail.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(detail, 2);
            g.Children.Add(detail);
            return g;
        }

        /// <summary>The battery percentage out of a device's detail line, when it has one; null otherwise.</summary>
        private static string Battery(string detail)
        {
            if (string.IsNullOrEmpty(detail)) return null;
            int pct = detail.IndexOf('%');
            if (pct <= 0) return null;
            int i = pct - 1;
            while (i >= 0 && char.IsDigit(detail[i])) i--;
            return i + 1 < pct ? detail.Substring(i + 1, pct - i) : null;
        }

        /// <summary>The detail line is already short; this only keeps it from pushing the name off its row.</summary>
        private static string Shorten(string detail)
        {
            string s = (detail ?? "").Trim();
            return s.Length <= 20 ? s : s.Substring(0, 19) + "…";
        }

        private static string Glyph(DeviceKind kind) => kind switch
        {
            DeviceKind.Headphones => Ui.GHeadphones,
            DeviceKind.Mouse => Ui.GMouse,
            DeviceKind.Keyboard => Ui.GKeyboard,
            DeviceKind.Gamepad => Ui.GGamepad,
            DeviceKind.Phone => Ui.GPhone,
            DeviceKind.Monitor => Ui.GMonitor,
            DeviceKind.Drive => Ui.GDrive,
            DeviceKind.Camera => Ui.GCam,
            DeviceKind.Printer => Ui.GPrinter,
            DeviceKind.Bluetooth => Ui.GBluetooth,
            _ => Ui.GUsb,
        };
    }
}
