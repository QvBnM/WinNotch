using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WinNotch
{
    public partial class SettingsWindow : Window
    {
        private readonly AppSettings _s;
        private readonly List<string> _order;          // all widget ids, selected ones first (in their order)
        private readonly HashSet<string> _selected;
        private string _accent;

        private static readonly (string Hex, string Name)[] Accents =
        {
            ("", "Culoarea temei"), ("#F5A524", "Chihlimbar"), ("#3DDC84", "Verde"), ("#5AA9FF", "Albastru"),
            ("#FF6B8A", "Roz"), ("#A78BFA", "Mov"), ("#E8E8E8", "Alb")
        };

        public event Action Saved;
        /// <summary>"Anulează" on the embedded page: the host reloads it from the saved settings.</summary>
        public event Action Reverted;
        private bool _embedded;

        /// <summary>
        /// The settings are a page of the main WinNotch window: its content moves there (this window is never shown).
        /// Save keeps the page open and says "Salvat"; Cancel reloads the saved values.
        /// </summary>
        internal FrameworkElement TakeContent()
        {
            _embedded = true;
            var content = (FrameworkElement)Content;
            Content = null;
            _content = content;
            content.Resources.MergedDictionaries.Add(Resources);
            // in the big window Esc and Enter must not cancel or save the settings behind your back
            void Walk(object o)
            {
                if (o is Button b) { b.IsCancel = false; b.IsDefault = false; }
                if (o is DependencyObject d) foreach (var c in LogicalTreeHelper.GetChildren(d)) Walk(c);
            }
            Walk(content);
            return content;
        }

        private FrameworkElement _content;
        private const string CmdKeyHintText = "Pentru Command Bar (îl pornești din „Funcții noi”, mai jos). Dacă scurtătura e folosită de altă aplicație, alege-o pe cealaltă.";

        /// <summary>
        /// P14 ("settings.*" actions): brings the option named <paramref name="name"/> (its x:Name) into view and focuses it
        /// when it can take the keyboard (a list opens at its section). After the layout, so it works on a page just built.
        /// </summary>
        internal void Reveal(string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                var el = FindName(name) as FrameworkElement ?? LogicalTreeHelper.FindLogicalNode((DependencyObject)_content ?? this, name) as FrameworkElement;
                if (el == null) return;
                el.BringIntoView();
                if (el.Focusable && el.IsEnabled && el.IsVisible) el.Focus();
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        /// <summary>The Command Bar's shortcut taken by another app: said under the option.</summary>
        private void UpdateCmdKeyHint()
        {
            string problem = NotchWindow.Current?.CommandBarHotkeyProblem();
            CmdKeyHint.Text = problem ?? CmdKeyHintText;
            CmdKeyHint.Foreground = problem != null ? new SolidColorBrush(Color.FromRgb(0xB0, 0x4A, 0x00)) : new SolidColorBrush(Color.FromRgb(0x5B, 0x62, 0x6D));
        }

        /// <summary>Stops the extension-status timer of an embedded page (the window itself is never closed).</summary>
        internal void Detach()
        {
            _extTimer.Stop();
            try { Close(); } catch { }        // never shown: closing just releases it
        }

        private void Done()
        {
            if (!_embedded) { Close(); return; }
            Msg.Foreground = new SolidColorBrush(Color.FromRgb(0x1A, 0x8A, 0x4A));
            Msg.Text = "✓ Salvat";
        }
        private List<(Services.Workspace Ws, string Name)> _workspaces;

        private void BuildWorkspaces()
        {
            WorkspaceRows.Children.Clear();
            if (_workspaces.Count == 0)
            {
                WorkspaceRows.Children.Add(new TextBlock { Text = "Niciun spațiu salvat încă.", Foreground = new SolidColorBrush(Color.FromRgb(0x5B, 0x62, 0x6D)) });
                return;
            }
            for (int i = 0; i < _workspaces.Count; i++)
            {
                int idx = i;
                var g = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var name = new TextBox { Text = _workspaces[i].Name, Padding = new Thickness(4, 3, 4, 3) };
                name.TextChanged += (o, e) => _workspaces[idx] = (_workspaces[idx].Ws, name.Text);
                g.Children.Add(name);
                var count = new TextBlock { Text = _workspaces[i].Ws.Windows.Count + " aplicații", Foreground = new SolidColorBrush(Color.FromRgb(0x5B, 0x62, 0x6D)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
                Grid.SetColumn(count, 1);
                g.Children.Add(count);
                var del = new Button { Content = "Șterge", Padding = new Thickness(8, 2, 8, 2) };
                del.Click += (o, e) => { _workspaces.RemoveAt(idx); BuildWorkspaces(); };
                Grid.SetColumn(del, 2);
                g.Children.Add(del);
                WorkspaceRows.Children.Add(g);
            }
        }

        public SettingsWindow(AppSettings s)
        {
            _s = s;
            InitializeComponent();
            Height = Math.Min(680, SystemParameters.WorkArea.Height - 24);       // small screens: buttons stay visible

            var all = AppSettings.Widgets.Select(w => w.Id).ToList();
            _selected = new HashSet<string>(s.Standby.Where(all.Contains));
            _order = s.Standby.Where(all.Contains).Concat(all.Where(id => !s.Standby.Contains(id))).ToList();
            BuildRows();

            DwellSlider.Value = s.DwellMs;
            DwellText.Text = s.DwellMs + " ms";
            SelectTag(PosBox, s.Position);
            SelectTag(FsBox, s.Fullscreen);
            SelectTag(MiniBox, s.MiniAfterSec.ToString());
            SelectTag(ScaleBox, s.UiScale <= 0 ? "0" : s.UiScale.ToString(CultureInfo.InvariantCulture));
            SelectTag(CmdKeyBox, Features.CommandBar.CommandBarHotkeys.ToSetting(Features.CommandBar.CommandBarHotkeys.Parse(s.CommandBarKey)));
            UpdateCmdKeyHint();
            SlimBox.IsChecked = s.SlimOverMaximized;
            TempsBox.IsChecked = s.Temperatures;
            TempsHint.Visibility = Services.TempService.PawnIOInstalled ? Visibility.Collapsed : Visibility.Visible;
            StartBox.IsChecked = AppSettings.StartWithWindows;
            UpdateBox.IsChecked = s.AutoUpdate;
            BetaBox.IsChecked = s.BetaChannel;
            UpdateStatus.Text = "Versiunea ta: " + Services.Updater.Current + (Services.Updater.Configured ? "" : " · actualizările automate nu sunt încă configurate");
            UpdateStartHint();
            CityBox.Text = s.City;
            LatBox.Text = s.Lat.ToString(CultureInfo.InvariantCulture);
            LonBox.Text = s.Lon.ToString(CultureInfo.InvariantCulture);

            _accent = s.Accent;
            BuildAccents();
            LyricsBox.IsChecked = s.Lyrics;
            EyeBox.IsChecked = s.EyeBreak;
            RamAlertBox.IsChecked = s.RamAlert;
            SelectTag(RamPctBox, s.RamAlertPercent.ToString());
            IcsBox.Text = s.CalendarIcs ?? "";
            _workspaces = s.Workspaces.Select(x => (x, x.Name)).ToList();
            BuildWorkspaces();
            TabsBox.IsChecked = s.BrowserTabs;
            BuildContextPages();                // P27 (Features/ContextPages)
            ClipPeekBox.IsChecked = s.SmartClipboardPeek;       // P21 (Features/SmartClipboard)
            BuildFeatures();
            UpdateExtStatus();
            _extTimer.Tick += (o, e) => UpdateExtStatus();
            _extTimer.Start();
            Closed += (o, e) => _extTimer.Stop();
        }

        private readonly System.Windows.Threading.DispatcherTimer _extTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };

        private void UpdateExtStatus()
        {
            var bridge = NotchWindow.Current?.Bridge;
            var list = bridge?.ConnectedBrowsers() ?? new List<string>();
            if (list.Count == 0)
            {
                ExtStatus.Text = "Extensia nu e conectată în niciun browser.";
                ExtStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xB0, 0x4A, 0x00));
            }
            else
            {
                ExtStatus.Text = "✓ Conectată: " + string.Join(", ", list.Select(Services.AudioSessionsService.Friendly));
                ExtStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x1A, 0x8A, 0x4A));
            }
        }

        private static void CopyExtPath()
        {
            Services.BrowserBridge.WriteExtension();
            try { Clipboard.SetText(Services.BrowserBridge.ExtensionFolder); } catch { }
        }

        private void Run(string exe, string args)
        {
            // as administrator a browser would start elevated too: copy the address instead
            if (App.IsAdmin)
            {
                try { Clipboard.SetText(args); } catch { }
                Msg.Text = "Am copiat „" + args + "”: lipește-l în bara de adrese a browserului.";
                return;
            }
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, args) { UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show("Nu am putut porni " + exe + ": " + ex.Message, "WinNotch"); }
        }

        private void OpenChromeExt_Click(object sender, RoutedEventArgs e) { CopyExtPath(); Run("chrome.exe", "chrome://extensions/"); }
        private void OpenEdgeExt_Click(object sender, RoutedEventArgs e) { CopyExtPath(); Run("msedge.exe", "edge://extensions/"); }
        private void OpenExtFolder_Click(object sender, RoutedEventArgs e) { CopyExtPath(); Services.Shell.Open(Services.BrowserBridge.ExtensionFolder); }

        private static void SelectTag(ComboBox box, string tag)
        {
            foreach (ComboBoxItem it in box.Items)
                if ((string)it.Tag == tag) { box.SelectedItem = it; return; }
            box.SelectedIndex = 0;
        }

        private static string TagOf(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag as string;

        private void BuildRows()
        {
            WidgetRows.Children.Clear();
            int pos = 0;
            foreach (var id in _order)
            {
                string name = AppSettings.Widgets.First(w => w.Id == id).Name;
                bool on = _selected.Contains(id);
                if (on) pos++;

                var g = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var num = new TextBlock
                {
                    Text = on ? pos.ToString() : "",
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xB0, 0x6A, 0x00)),
                    VerticalAlignment = VerticalAlignment.Center
                };
                g.Children.Add(num);

                var cb = new CheckBox { Content = name, IsChecked = on, VerticalAlignment = VerticalAlignment.Center };
                string capturedId = id;
                cb.Click += (o, e) => Toggle(capturedId, cb.IsChecked == true);
                Grid.SetColumn(cb, 1);
                g.Children.Add(cb);

                var btns = new StackPanel { Orientation = Orientation.Horizontal, Visibility = on ? Visibility.Visible : Visibility.Hidden };
                var up = new Button { Content = "▲", Width = 28, Height = 22, Margin = new Thickness(0, 0, 4, 0), ToolTip = "Mută mai în față" };
                var down = new Button { Content = "▼", Width = 28, Height = 22, ToolTip = "Mută mai în spate" };
                up.Click += (o, e) => Move(capturedId, -1);
                down.Click += (o, e) => Move(capturedId, +1);
                btns.Children.Add(up);
                btns.Children.Add(down);
                Grid.SetColumn(btns, 2);
                g.Children.Add(btns);

                WidgetRows.Children.Add(g);
            }
        }

        private void Toggle(string id, bool on)
        {
            Msg.Text = "";
            if (on)
            {
                if (_selected.Count >= AppSettings.MaxStandby)
                {
                    Msg.Foreground = new SolidColorBrush(Color.FromRgb(0xB0, 0x4A, 0x00));
                    Msg.Text = "Maxim " + AppSettings.MaxStandby + ". Debifează unul ca să adaugi altul.";
                    BuildRows();
                    return;
                }
                _selected.Add(id);
                // newly picked items go to the end of the selected block
                _order.Remove(id);
                _order.Insert(_order.Count(x => _selected.Contains(x)), id);
            }
            else
            {
                _selected.Remove(id);
                _order.Remove(id);
                _order.Add(id);
            }
            BuildRows();
        }

        private void Move(string id, int dir)
        {
            var sel = _order.Where(_selected.Contains).ToList();
            int i = sel.IndexOf(id), j = i + dir;
            if (i < 0 || j < 0 || j >= sel.Count) return;
            (sel[i], sel[j]) = (sel[j], sel[i]);
            var rest = _order.Where(x => !_selected.Contains(x)).ToList();
            _order.Clear();
            _order.AddRange(sel);
            _order.AddRange(rest);
            BuildRows();
        }

        private void BuildAccents()
        {
            AccentPanel.Children.Clear();
            foreach (var (hex, name) in Accents)
            {
                Brush fill = hex.Length == 0
                    ? new LinearGradientBrush(Color.FromRgb(0xF5, 0xA5, 0x24), Color.FromRgb(0x5A, 0xA9, 0xFF), 45)
                    : new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
                bool on = string.Equals(hex, _accent ?? "", StringComparison.OrdinalIgnoreCase);
                var b = new Button
                {
                    Width = 30, Height = 30, Margin = new Thickness(0, 0, 10, 0), ToolTip = name, Cursor = System.Windows.Input.Cursors.Hand,
                    Template = SwatchTemplate(),
                    Background = fill,
                    BorderBrush = on ? Brushes.Black : Brushes.Transparent,
                };
                string h = hex;
                b.Click += (o, e) => { _accent = h; BuildAccents(); };
                AccentPanel.Children.Add(b);
            }
        }

        private static ControlTemplate SwatchTemplate()
        {
            var t = new ControlTemplate(typeof(Button));
            var outer = new FrameworkElementFactory(typeof(Border));
            outer.SetValue(Border.CornerRadiusProperty, new CornerRadius(15));
            outer.SetValue(Border.BorderThicknessProperty, new Thickness(2));
            outer.SetValue(Border.PaddingProperty, new Thickness(2));
            outer.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            var inner = new FrameworkElementFactory(typeof(Border));
            inner.SetValue(Border.CornerRadiusProperty, new CornerRadius(11));
            inner.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            inner.SetValue(Border.BorderBrushProperty, new SolidColorBrush(Color.FromArgb(0x33, 0, 0, 0)));     // white swatch visible on white
            inner.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            outer.AppendChild(inner);
            t.VisualTree = outer;
            return t;
        }

        private void DwellSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (DwellText != null) DwellText.Text = Math.Round(e.NewValue) + " ms";
        }

        private static bool TryNum(string text, out double v) =>
            double.TryParse((text ?? "").Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v);

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (!TryNum(LatBox.Text, out double lat) || lat < -90 || lat > 90 ||
                !TryNum(LonBox.Text, out double lon) || lon < -180 || lon > 180)
            {
                Msg.Foreground = new SolidColorBrush(Color.FromRgb(0xB0, 0x4A, 0x00));
                Msg.Text = "Coordonatele nu sunt valide. Exemplu: 44.4268 și 26.1025.";
                return;
            }

            _s.Standby = _order.Where(_selected.Contains).ToList();
            _s.DwellMs = (int)Math.Round(DwellSlider.Value);
            _s.Position = TagOf(PosBox) ?? "center";
            _s.Fullscreen = TagOf(FsBox) ?? "alerts";
            _s.MiniAfterSec = int.TryParse(TagOf(MiniBox), out int mini) ? mini : 10;
            _s.SlimOverMaximized = SlimBox.IsChecked == true;
            _s.UiScale = TryNum(TagOf(ScaleBox), out double sc) ? sc : 0;
            _s.CommandBarKey = TagOf(CmdKeyBox) ?? Features.CommandBar.CommandBarHotkeys.SpaceSetting;
            // P51c: asking for temperatures again clears the abrupt-closure count that switched the in-process read off.
            // Also when the box was already ticked: the safety stop does not untick it, so that is the state the user is
            // in when following the hint on the Sistem page ("tick Temperaturi again").
            if (TempsBox.IsChecked == true && (!_s.Temperatures || NotchWindow.Current?.Temps.BlockedBySafety == true))
                App.Guard?.ClearUnexplained();
            _s.Temperatures = TempsBox.IsChecked == true;
            _s.Accent = _accent;
            _s.City = string.IsNullOrWhiteSpace(CityBox.Text) ? "Orașul meu" : CityBox.Text.Trim();
            _s.Lat = lat;
            _s.Lon = lon;
            _s.Lyrics = LyricsBox.IsChecked == true;
            _s.EyeBreak = EyeBox.IsChecked == true;
            _s.RamAlert = RamAlertBox.IsChecked == true;
            _s.AutoUpdate = UpdateBox.IsChecked == true;
            _s.BetaChannel = BetaBox.IsChecked == true;
            _s.RamAlertPercent = int.TryParse(TagOf(RamPctBox), out int rp) ? rp : 80;
            _s.CalendarIcs = (IcsBox.Text ?? "").Trim();
            _s.BrowserTabs = TabsBox.IsChecked == true;
            foreach (var (ws, nm) in _workspaces) ws.Name = string.IsNullOrWhiteSpace(nm) ? ws.Name : nm.Trim();
            _s.Workspaces = _workspaces.Select(x => x.Ws).ToList();
            _s.ContextPages = ContextPagesChosen();         // P27 (Features/ContextPages)
            _s.SmartClipboardPeek = ClipPeekBox.IsChecked == true;     // P21 (Features/SmartClipboard)
            var flags = Core.Flags.FeatureFlags.Current;
            // only the switches you changed here (one switched off automatically meanwhile stays off); they start or stop right away
            flags?.ApplyChoices(_featuresShown, _features);
            _s.Save();
            BuildFeatures();                    // the page stays open: current states and reasons

            bool start = StartBox.IsChecked == true;
            if (start != AppSettings.StartWithWindows) AppSettings.StartWithWindows = start;

            Saved?.Invoke();                    // the notch applies it (also the Command Bar's shortcut)
            UpdateCmdKeyHint();
            Done();
        }

        private readonly Dictionary<string, bool> _features = new Dictionary<string, bool>();          // what the switches show now
        private readonly Dictionary<string, bool> _featuresShown = new Dictionary<string, bool>();     // what they showed when built

        /// <summary>One switch per entry of the feature catalog, with its stage; applied on "Salvează".</summary>
        private void BuildFeatures()
        {
            FeatureRows.Children.Clear();
            var flags = Core.Flags.FeatureFlags.Current;
            if (flags == null) return;
            SafeModeNote.Visibility = flags.SafeMode ? Visibility.Visible : Visibility.Collapsed;
            var muted = new SolidColorBrush(Color.FromRgb(0x5B, 0x62, 0x6D));
            foreach (var f in flags.Catalog)
            {
                string id = f.Id;
                _features[id] = _featuresShown[id] = flags.IsSaved(id);
                var (fg, bg) = f.Stage switch
                {
                    Core.Flags.FeatureStage.Experimental => (Color.FromRgb(0xB0, 0x4A, 0x00), Color.FromRgb(0xFD, 0xEB, 0xD9)),
                    Core.Flags.FeatureStage.Beta => (Color.FromRgb(0x1F, 0x5F, 0xB8), Color.FromRgb(0xDF, 0xEC, 0xFB)),
                    _ => (Color.FromRgb(0x1A, 0x8A, 0x4A), Color.FromRgb(0xDD, 0xF3, 0xE6)),
                };
                var tag = new Border
                {
                    CornerRadius = new CornerRadius(8), Padding = new Thickness(7, 1, 7, 1), Margin = new Thickness(8, 0, 0, 0),
                    Background = new SolidColorBrush(bg), VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock { Text = Core.Flags.FeatureCatalog.StageName(f.Stage), FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(fg) }
                };
                var head = new StackPanel { Orientation = Orientation.Horizontal };
                head.Children.Add(new TextBlock { Text = f.Name, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
                head.Children.Add(tag);
                var cb = new CheckBox { Content = head, IsChecked = _features[id], Margin = new Thickness(0, 6, 0, 0) };
                cb.Click += (o, e) => _features[id] = cb.IsChecked == true;
                FeatureRows.Children.Add(cb);
                FeatureRows.Children.Add(new TextBlock { Text = f.Description, Foreground = muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(22, 2, 0, 0) });
                string why = flags.DisabledReason(id);
                if (why != null)
                    FeatureRows.Children.Add(new TextBlock { Text = "Oprită automat: " + why, Foreground = new SolidColorBrush(fg), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(22, 2, 0, 0) });
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            if (_embedded) Reverted?.Invoke();
            else Close();
        }

        /// <summary>
        /// Start-with-Windows (as admin) runs a protected copy in Program Files. After a new build it is older than this
        /// exe; it's updated only when you click here, as administrator, never automatically.
        /// </summary>
        /// <summary>The CPU-temperature helper: activate it once (UAC), or update it after a new build.</summary>
        private bool _oldTask;

        private void UpdateStartHint()
        {
            // the scheduled-task query takes a moment: done in the background, then the hint is refreshed
            System.Threading.Tasks.Task.Run(() => Services.TempHelper.OldTaskExists).ContinueWith(t =>
            {
                if (t.Status == System.Threading.Tasks.TaskStatus.RanToCompletion && t.Result != _oldTask) { _oldTask = t.Result; ShowStartHint(); }
            }, System.Threading.Tasks.TaskScheduler.FromCurrentSynchronizationContext());
            ShowStartHint();
        }

        private void ShowStartHint()
        {
            bool installed = Services.TempHelper.Installed, outdated = Services.TempHelper.Outdated, old = _oldTask;
            StartUpdatePanel.Visibility = !installed || outdated || old ? Visibility.Visible : Visibility.Collapsed;
            StartUpdateBtn.Content = old ? "Înlocuiește cu serviciul de temperatură" : installed ? "Actualizează serviciul de temperatură" : "Activează temperatura procesorului";
            StartUpdateHint.Text = old
                ? "Pornirea veche „ca administrator” (până la 0.6.4) e încă activă. Înlocuiește-o cu serviciul de temperatură: WinNotch va rula cu drepturi normale, iar temperatura procesorului se citește în continuare."
                : installed
                ? "Serviciul de temperatură e de la o versiune mai veche a WinNotch."
                : "Temperatura procesorului se citește cu drepturi de administrator. WinNotch nu mai rulează ca administrator: un mic serviciu fără fereastră (contul SYSTEM, din Program Files) citește doar temperaturile și i le dă notch-ului. Se activează o singură dată, cu o confirmare Windows.";
        }

        private async void UpdateNow_Click(object sender, RoutedEventArgs e)
        {
            UpdateNowBtn.IsEnabled = false;
            UpdateStatus.Text = "Caut…";
            try { UpdateStatus.Text = NotchWindow.Current != null ? await NotchWindow.Current.CheckUpdateNow() : ""; }
            finally { UpdateNowBtn.IsEnabled = true; }
        }

        private void StartUpdate_Click(object sender, RoutedEventArgs e)
        {
            StartUpdateBtn.IsEnabled = false;            // until the install (with its UAC prompt) is over
            ((App)Application.Current).InstallTempHelper(done: () => { StartUpdateBtn.IsEnabled = true; UpdateStartHint(); });
        }
    }
}
