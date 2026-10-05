using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using WinNotch.Panes;
using WinNotch.Services;

namespace WinNotch
{
    internal sealed class ClipItem
    {
        public string Text;
        public DateTime At;
        public bool Pinned;
    }

    public partial class NotchWindow : Window
    {
        private enum Mode { Idle, Live, Expanded }

        // ---------------- shared with the panes ----------------
        internal readonly AppSettings S;
        internal readonly SystemStats Stats = new SystemStats();
        internal readonly TempService Temps = new TempService();
        internal readonly AudioService Audio = new AudioService();
        internal readonly MediaService Media = new MediaService();
        internal readonly AudioSessionsService Sessions = new AudioSessionsService();
        internal readonly Visualizer Viz = new Visualizer();
        internal readonly ProcessMonitor Procs = new ProcessMonitor();
        internal readonly BrowserBridge Bridge = new BrowserBridge();
        internal NowPlaying Now;
        internal Weather Weather = new Weather();
        internal List<CalendarEvent> Events = new List<CalendarEvent>();
        internal readonly List<ClipItem> Clips = new List<ClipItem>();
        internal readonly List<double> CpuHist = new List<double>();
        internal readonly List<double> TempHist = new List<double>();
        internal IntPtr LastForeground = IntPtr.Zero;
        internal IntPtr Hwnd => _hwnd;
        internal static readonly CultureInfo Ro = new CultureInfo("ro-RO");

        // ---------------- panes ----------------
        private HomePane _home;
        private SystemPane _system;
        private DevicesPane _devices;
        private ToolsPane _tools;
        private SourcesPane _sources;
        private Pane _pane;
        private readonly List<RadioButton> _tabs = new List<RadioButton>();

        // ---------------- state ----------------
        private IntPtr _hwnd;
        private HwndSource _src;
        private Mode _mode = Mode.Idle;
        private bool _slim, _hidden, _pinned, _mouseWasInside, _typing, _liveInteractive;
        private DateTime _lastActive = DateTime.Now;
        private DateTime? _dwellStart, _leaveStart;
        private IntPtr _curMon = IntPtr.Zero;
        private MonitorState _target;
        private string _monLog = "";

        private readonly DispatcherTimer _poll = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(30) };
        private readonly DispatcherTimer _sec = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        private readonly DispatcherTimer _mon = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        private readonly DispatcherTimer _audioTick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        private readonly DispatcherTimer _liveTimer = new DispatcherTimer();
        private double _liveW = 300, _liveH = 40;
        private double _lastIdleW = -1;
        private int _tick;
        private DateTime _lastFrame = DateTime.MinValue;

        private readonly Dictionary<string, TextBlock> _idleVals = new Dictionary<string, TextBlock>();
        private string _idleSig;
        private Border _idleBattFill;

        private bool _themeLight, _dodging, _clickedThrough, _overMax;
        private int _lastVol = -1;
        private bool _lastMuted;
        private bool? _lastCharging;
        private DateTime _lastHotAlert = DateTime.MinValue, _lastRamAlert = DateTime.MinValue;
        private DateTime? _ramHighSince;
        private int _lastBatAlert = 101;
        private DateTime _volSetByUs = DateTime.MinValue;
        private bool _ignoreClip;
        private int _activeSeconds;
        private DateTime _weatherAt = DateTime.MinValue, _calendarAt = DateTime.MinValue;
        private string _weatherFor = "";

        public static readonly DependencyProperty RadiusProperty = DependencyProperty.Register(
            nameof(Radius), typeof(double), typeof(NotchWindow),
            new PropertyMetadata(17.0, (d, e) => ((NotchWindow)d).ApplyRadius()));
        public double Radius { get => (double)GetValue(RadiusProperty); set => SetValue(RadiusProperty, value); }

        internal static NotchWindow Current { get; private set; }

        public NotchWindow(AppSettings settings)
        {
            Current = this;
            S = settings;
            Now = new NowPlaying(Media, Bridge, () => S.BrowserTabs);
            InitializeComponent();
            foreach (var p in S.PinnedClips) Clips.Add(new ClipItem { Text = p, At = DateTime.Now, Pinned = true });

            _home = new HomePane(this);
            _system = new SystemPane(this);
            _devices = new DevicesPane(this);
            _tools = new ToolsPane(this);
            _sources = new SourcesPane(this);
            BuildHeaderButtons();
            ShowPane(_home);
            RebuildTabs();
            UpdateHeader();

            Pill.SizeChanged += (o, e) => UpdateClip();
            SourceInitialized += OnSourceInitialized;
            Loaded += OnLoaded;
            DpiChanged += (o, e) => Dispatcher.InvokeAsync(() => { if (_target != null) { _target.Scale = e.NewDpi.DpiScaleX; MoveToMonitor(_target, false); } }, DispatcherPriority.Background);
            _liveTimer.Tick += (o, e) => EndLive();
        }

        // =====================================================================
        //  Startup / shutdown
        // =====================================================================
        private void OnSourceInitialized(object sender, EventArgs e)
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            _src = HwndSource.FromHwnd(_hwnd);
            _src.AddHook(WndProc);
            long ex = Native.GetExStyle(_hwnd);
            ex |= Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TRANSPARENT | Native.WS_EX_LAYERED;
            Native.SetExStyle(_hwnd, ex);
            if (!Native.RegisterHotKey(_hwnd, 1, Native.MOD_WIN | Native.MOD_ALT | Native.MOD_NOREPEAT, 0x4E))
                App.Log("Scurtătura Win+Alt+N e folosită de altă aplicație.");
            if (!Native.RegisterHotKey(_hwnd, 2, Native.MOD_WIN | Native.MOD_ALT | Native.MOD_NOREPEAT, 0x53))      // S: area screenshot
                App.Log("Scurtătura Win+Alt+S e folosită de altă aplicație.");
            if (!Native.RegisterHotKey(_hwnd, 3, Native.MOD_WIN | Native.MOD_ALT | Native.MOD_NOREPEAT, 0x54))      // T: text from screen
                App.Log("Scurtătura Win+Alt+T e folosită de altă aplicație.");
            Native.AddClipboardFormatListener(_hwnd);
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            Stats.Sample();
            Audio.Start();
            Audio.Changed += (v, m) => Dispatcher.InvokeAsync(() => OnSystemVolume(v, m));
            Now.Changed += t => Dispatcher.InvokeAsync(() => OnMedia(t));
            BrowserBridge.WriteExtension();
            Bridge.Changed += () => Dispatcher.InvokeAsync(() => { if (_mode == Mode.Expanded && _pane == _sources) _pane.Refresh(); });
            Bridge.Start();
            _ = Media.StartAsync();
            Launcher.IndexAsync();

            _poll.Tick += (o, a) => PollTick();
            _sec.Tick += (o, a) => SecondTick();
            _mon.Tick += (o, a) => MonitorTick();
            _audioTick.Tick += (o, a) => Sessions.Scan();
            _poll.Start(); _sec.Start(); _mon.Start();
            // Per-frame work and the 400 ms per-app audio scan only run while the notch is open (see Expand/Collapse).

            ApplySettings();
        }

        /// <summary>Called at start and whenever the settings window saves.</summary>
        public void ApplySettings()
        {
            bool themeChanged = ThemeManager.Apply(S);
            _themeLight = ThemeManager.IsLight(S);
            if (themeChanged && _home != null) RebuildUi();
            if (S.Temperatures) Temps.Start();
            _idleSig = null;
            _curMon = IntPtr.Zero;
            MonitorTick();
            BuildIdle();
            UpdateIdleValues();
            ApplyMode();
            string key = S.Lat.ToString(CultureInfo.InvariantCulture) + "," + S.Lon.ToString(CultureInfo.InvariantCulture);
            if (key != _weatherFor) RefreshWeather();
            _calendarAt = DateTime.MinValue;
            _tools?.Rebuild();
            Now?.Refresh();
        }

        public void Cleanup()
        {
            _poll.Stop(); _sec.Stop(); _mon.Stop(); _audioTick.Stop();
            CompositionTarget.Rendering -= OnFrame;
            S.PinnedClips = Clips.Where(c => c.Pinned).Select(c => c.Text).ToList();
            try
            {
                Native.UnregisterHotKey(_hwnd, 1);
                Native.UnregisterHotKey(_hwnd, 2);
                Native.UnregisterHotKey(_hwnd, 3);
                Native.RemoveClipboardFormatListener(_hwnd);
            }
            catch { }
            Viz.Dispose();
            Sessions.Dispose();
            Bridge.Dispose();
            Audio.Dispose();
            Temps.Dispose();
            Close();
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == Native.WM_HOTKEY && wParam.ToInt32() == 1) { ToggleByHotkey(); handled = true; }
            else if (msg == Native.WM_HOTKEY && wParam.ToInt32() == 2) { ScreenshotArea(); handled = true; }
            else if (msg == Native.WM_HOTKEY && wParam.ToInt32() == 3) { TextFromScreen(); handled = true; }
            else if (msg == Native.WM_CLIPBOARDUPDATE) OnClipboard();
            return IntPtr.Zero;
        }

        // =====================================================================
        //  Tabs and panes
        // =====================================================================
        internal void ShowPane(Pane p)
        {
            if (_pane == p) return;
            _pane?.Hidden();
            _pane = p;
            PaneHost.Content = p;
            foreach (var t in _tabs)
                t.IsChecked = t.Tag == p || (p == _sources && t.Tag == _home);
            if (_editBar != null)
            {
                CloseOverlays();
                ApplyEditToPane();
                UpdateHeader();
            }
            ExpLayer.Height = PanelH();
            if (_mode == Mode.Expanded)
            {
                p.Shown();
                p.Refresh();
                ApplyMode();
            }
            UpdateVisualizer();
        }

        internal void ShowSources() => ShowPane(_sources);
        internal void ShowHome() => ShowPane(_home);

        private void UpdateVisualizer()
        {
            bool want = _mode == Mode.Expanded && _pane == _home;
            if (want && !Viz.Running) Viz.Start();
            else if (!want && Viz.Running) Viz.Stop();
        }

        private void OnFrame(object sender, EventArgs e)
        {
            if (_mode != Mode.Expanded || _pane == null) return;
            var now = DateTime.Now;
            if ((now - _lastFrame).TotalMilliseconds < 33) return;
            _lastFrame = now;
            _pane.Fast();
        }

        private void SettingsBtn_Click(object sender, RoutedEventArgs e)
        {
            Collapse();
            ((App)Application.Current).OpenSettings();
        }

        /// <summary>Lets a text box get keyboard focus (the notch normally never takes focus away from your app).</summary>
        internal void EnableTyping(Control box)
        {
            if (!_typing)
            {
                _typing = true;
                SetNoActivate(false);
                Activate();
            }
            box.Focus();
            Keyboard.Focus(box);
        }

        private void StopTyping()
        {
            if (!_typing) return;
            _typing = false;
            Keyboard.ClearFocus();
            SetNoActivate(true);
            S.Save();
            // give the keyboard back to the app you were using
            if (LastForeground != IntPtr.Zero) Native.SetForegroundWindow(LastForeground);
        }

        // =====================================================================
        //  Click-through, hover with delay, expand / collapse
        // =====================================================================
        private void SetClickThrough(bool on)
        {
            if (_hwnd == IntPtr.Zero) return;
            long ex = Native.GetExStyle(_hwnd);
            Native.SetExStyle(_hwnd, on ? ex | Native.WS_EX_TRANSPARENT : ex & ~Native.WS_EX_TRANSPARENT);
        }

        private void SetNoActivate(bool on)
        {
            long ex = Native.GetExStyle(_hwnd);
            Native.SetExStyle(_hwnd, on ? ex | Native.WS_EX_NOACTIVATE : ex & ~Native.WS_EX_NOACTIVATE);
        }

        private Native.RECT PillScreenRect()
        {
            try
            {
                var tl = Pill.PointToScreen(new Point(0, 0));
                var br = Pill.PointToScreen(new Point(Pill.ActualWidth, Pill.ActualHeight));
                return new Native.RECT { Left = (int)tl.X, Top = (int)tl.Y, Right = (int)br.X, Bottom = (int)br.Y };
            }
            catch { return default; }
        }

        private static bool Inside(Native.RECT r, Native.POINT p, int pad) =>
            p.X >= r.Left - pad && p.X <= r.Right + pad && p.Y >= r.Top - pad && p.Y <= r.Bottom + pad;

        private static readonly HashSet<string> ShellClasses = new HashSet<string> { "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd" };

        private void PollTick()
        {
            if (!IsLoaded || _hwnd == IntPtr.Zero) return;
            // A screen tool hid the notch (capture, region picker): hovering the invisible pill must not open or show it.
            if (_toolHidden) { ClearDwell(); return; }

            // remember the last real app window, for "Fereastra activă"
            var fg = Native.GetForegroundWindow();
            if (fg != _lastFg)                      // class name looked up only when the foreground window changes
            {
                _lastFg = fg;
                if (fg != IntPtr.Zero && fg != _hwnd && !ShellClasses.Contains(Native.ClassName(fg))) LastForeground = fg;
            }

            Native.GetCursorPos(out var p);
            var r = PillScreenRect();

            if (_mode == Mode.Expanded)
            {
                if (Editing) { _leaveStart = null; return; }      // edit mode stays open until "Gata"
                bool typingNow = _typing && Keyboard.FocusedElement is TextBox;
                if (typingNow || Mouse.LeftButton == MouseButtonState.Pressed && Inside(r, p, 60)) { _leaveStart = null; return; }
                if (Inside(r, p, 8)) { _leaveStart = null; _mouseWasInside = true; return; }
                if (_pinned && !_mouseWasInside) return;
                _leaveStart ??= DateTime.Now;
                if ((DateTime.Now - _leaveStart.Value).TotalMilliseconds > 300) Collapse();
                return;
            }

            if (_hidden && _mode != Mode.Live) { ClearDwell(); return; }
            if (_liveInteractive) return;

            bool ins = Inside(r, p, _miniApplied && _mode == Mode.Idle ? 10 : 4);

            // Over a maximized window the notch sits on its tab strip / title bar (a browser's "+" button, for instance).
            // There it gets out of the way: it vanishes while the mouse is over it and the click goes to the window below.
            // It opens only when the mouse is pushed all the way up, against the top edge of the screen.
            bool atEdge = _target != null && p.Y <= _target.Bounds.Top + 1;
            if (ins && _overMax && _mode == Mode.Idle && !atEdge)
            {
                if (!_dodging) { _dodging = true; _dwellStart = null; Fade(Pill, 0, 90); }
                return;
            }
            if (_dodging) { _dodging = false; if (!ins) Fade(Pill, 1, 220); }

            // A click while the mouse rests on the notch was meant for the window below: don't open on top of it.
            if (ins && (Native.GetAsyncKeyState(0x01) < 0 || Native.GetAsyncKeyState(0x02) < 0)) _clickedThrough = true;   // left / right button
            if (!ins) _clickedThrough = false;
            if (_clickedThrough) { ClearDwell(); return; }

            if (ins)
            {
                if (_dwellStart == null)
                {
                    _dwellStart = DateTime.Now;
                    Touch();
                    Fade(Pill, 0.18, 140);
                }
                else if ((DateTime.Now - _dwellStart.Value).TotalMilliseconds >= S.DwellMs) Expand();
            }
            else if (_dwellStart != null) ClearDwell();
        }

        private void ClearDwell()
        {
            if (_dwellStart == null) return;
            _dwellStart = null;
            Fade(Pill, 1, 160);
        }

        public void ToggleByHotkey()
        {
            if (_mode == Mode.Expanded) { Collapse(); return; }
            _pinned = true;
            _mouseWasInside = false;
            Expand();
        }

        private void Expand()
        {
            CompositionTarget.Rendering -= OnFrame;
            CompositionTarget.Rendering += OnFrame;
            Sessions.Scan();
            _audioTick.Start();
            _dwellStart = null;
            Fade(Pill, 1, 120);
            EndLiveInteractive();
            _liveTimer.Stop();
            _mode = Mode.Expanded;
            _leaveStart = null;
            SetClickThrough(false);
            if (_pane == _sources) ShowPane(_home);
            _pane.Shown();
            _pane.Refresh();
            HeaderClock.Text = DateTime.Now.ToString("HH:mm");
            ApplyMode();
            ApplyHidden();
            UpdateVisualizer();
        }

        private void Collapse()
        {
            if (Editing) ExitEdit();
            CompositionTarget.Rendering -= OnFrame;
            _audioTick.Stop();
            _lastActive = DateTime.Now;
            _mode = Mode.Idle;
            _pinned = false;
            _mouseWasInside = false;
            _leaveStart = null;
            StopTyping();
            _pane?.Hidden();
            SetClickThrough(true);
            ApplyMode();
            ApplyHidden();
            UpdateVisualizer();
        }

        // =====================================================================
        //  Morphing
        // =====================================================================
        private static readonly IEasingFunction Spring = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.28 };
        private bool _slimApplied, _miniApplied;

        private void ApplyMode()
        {
            double w, h, r, top;
            bool mini = _mode == Mode.Idle && MiniNow();
            switch (_mode)
            {
                case Mode.Expanded: FitWindowHeight(PanelH()); w = 720 * UiScale; h = PanelH() * UiScale; r = Math.Clamp(S.CornerRadius, 8, 40) * UiScale; top = 8; break;
                case Mode.Live: w = _liveW * UiScale; h = _liveH * UiScale; r = _liveH > 64 ? 26 * UiScale : h / 2; top = 8; break;
                default:
                    if (mini) { w = MiniWidth(); h = 22; r = 11; top = 0; }
                    else { w = IdleWidth(); h = 34; r = 17; top = 8; }
                    break;
            }
            if (_mode == Mode.Idle && !mini) _lastIdleW = w;
            if (_mode != Mode.Expanded && _tallWindow)          // shrink back once the closing animation is over
            {
                if (_shrink == null) { _shrink = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(520) }; _shrink.Tick += ShrinkTick; }
                _shrink.Stop();
                _shrink.Start();
            }
            _slimApplied = mini;
            _miniApplied = mini;

            var dur = TimeSpan.FromMilliseconds(460);
            Pill.BeginAnimation(WidthProperty, new DoubleAnimation(w, dur) { EasingFunction = Spring });
            Pill.BeginAnimation(HeightProperty, new DoubleAnimation(h, dur) { EasingFunction = Spring });
            BeginAnimation(RadiusProperty, new DoubleAnimation(r, TimeSpan.FromMilliseconds(380)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            Pill.BeginAnimation(MarginProperty, new ThicknessAnimation(new Thickness(0, top, 0, 0), TimeSpan.FromMilliseconds(300)));

            FadeLayer(IdleLayer, _mode == Mode.Idle && !mini, 120);
            FadeLayer(MiniLayer, mini, 140);
            FadeLayer(LiveLayer, _mode == Mode.Live, 120);

            if (_mode == Mode.Expanded)
            {
                ExpLayer.Visibility = Visibility.Visible;
                Fade(ExpLayer, 1, 220, 120);
            }
            else
            {
                var a = new DoubleAnimation(0, TimeSpan.FromMilliseconds(140));
                a.Completed += (o, e) => { if (_mode != Mode.Expanded) ExpLayer.Visibility = Visibility.Collapsed; };
                ExpLayer.BeginAnimation(OpacityProperty, a);
            }
        }

        private bool MiniNow()
        {
            if (_slim) return true;
            return S.MiniAfterSec > 0 && (DateTime.Now - _lastActive).TotalSeconds >= S.MiniAfterSec;
        }

        private void Touch()
        {
            _lastActive = DateTime.Now;
            if (_mode == Mode.Idle && _miniApplied && !_slim) ApplyMode();
        }

        private double MiniWidth()
        {
            MiniRow.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return Math.Max(196, Math.Ceiling(MiniRow.DesiredSize.Width) + 30);
        }

        private void ApplyRadius()
        {
            double r = Radius;
            Pill.CornerRadius = _slimApplied ? new CornerRadius(0, 0, r, r) : new CornerRadius(r);
            UpdateClip();
        }

        private void UpdateClip()
        {
            double w = Pill.ActualWidth, h = Pill.ActualHeight;
            if (w <= 0 || h <= 0) return;
            double r = Math.Min(Radius, Math.Min(w, h) / 2);
            Inner.Clip = new RectangleGeometry(new Rect(0, 0, w, h), r, r);
        }

        private void ApplyHidden()
        {
            bool hide = _hidden && _mode == Mode.Idle;
            double y = hide ? -(Pill.ActualHeight + 30) : 0;
            PillShift.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(y, TimeSpan.FromMilliseconds(320)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }

        /// <summary>Fades a layer in or out; a faded-out layer is collapsed, so nothing inside it animates or renders.</summary>
        private static void FadeLayer(UIElement el, bool show, int delayMs)
        {
            if (show)
            {
                el.Visibility = Visibility.Visible;
                Fade(el, 1, 200, delayMs);
                return;
            }
            var a = new DoubleAnimation(0, TimeSpan.FromMilliseconds(200));
            a.Completed += (o, e) => { if (el.Opacity < 0.01) el.Visibility = Visibility.Collapsed; };
            el.BeginAnimation(OpacityProperty, a);
        }

        internal static void Fade(UIElement el, double to, int ms, int delayMs = 0)
        {
            el.BeginAnimation(OpacityProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { BeginTime = TimeSpan.FromMilliseconds(delayMs) });
        }

        // =====================================================================
        //  Monitors: follow the mouse, skip busy (fullscreen) monitors
        // =====================================================================
        private void MonitorTick()
        {
            if (_hwnd == IntPtr.Zero) return;
            List<MonitorState> mons;
            try { mons = MonitorService.Scan(_hwnd); } catch (Exception ex) { App.Log("Monitoare: " + ex.Message); return; }
            if (mons.Count == 0) return;

            IntPtr under = MonitorService.MonitorUnderCursor();
            var m = mons.FirstOrDefault(x => x.Handle == under) ?? mons[0];
            bool always = S.Fullscreen == "visible";
            var t = m;
            if (!always && m.Busy)
            {
                var free = mons.FirstOrDefault(x => !x.Busy);
                if (free != null) t = free;
            }

            string log = string.Join(" | ", mons.Select((x, i) => "M" + (i + 1) + (x.Handle == t.Handle ? "*" : "") + (x.Busy ? " ocupat" : x.Maximized ? " maximizat" : " liber") + " <- " + x.Decider));
            if (log != _monLog) { _monLog = log; App.Log("Monitoare: " + log); }

            bool hidden = !always && t.Busy;
            bool slim = S.SlimOverMaximized && t.Maximized && !t.Busy;
            _overMax = t.Maximized && !t.Busy;

            if (_toolHidden) return;
            if (_mode != Mode.Expanded && (t.Handle != _curMon || _target == null || !SameRect(t.Bounds, _target.Bounds) || Math.Abs(t.Scale - _target.Scale) > 0.01))
                MoveToMonitor(t, _curMon != IntPtr.Zero && t.Handle != _curMon);

            if (hidden != _hidden) { _hidden = hidden; ApplyHidden(); }
            if (slim != _slim) { _slim = slim; if (_mode == Mode.Idle) ApplyMode(); }
        }

        /// <summary>
        /// The window is a transparent layered window: Windows redraws all of it on every animation frame, so it stays
        /// as small as the normal pages need and only grows while the gallery / sizes are open in edit mode.
        /// </summary>
        private const double BaseHeight = 420;
        private bool _tallWindow;

        private DispatcherTimer _shrink;

        private void ShrinkTick(object sender, EventArgs e)
        {
            _shrink.Stop();
            if (_mode != Mode.Expanded) FitWindowHeight(0);
        }

        private void FitWindowHeight(double panel)
        {
            double need = (panel + 40) * UiScale;
            if (need > Height + 0.5) { Height = need; _tallWindow = true; }
            else if (_tallWindow && need <= BaseHeight * UiScale) { Height = BaseHeight * UiScale; _tallWindow = false; }
        }

        private static bool SameRect(Native.RECT a, Native.RECT b) =>
            a.Left == b.Left && a.Top == b.Top && a.Right == b.Right && a.Bottom == b.Bottom;

        /// <summary>
        /// Size of the open panel and the alerts: the user's choice, or automatic: bigger on 1440p+ monitors that run
        /// at 100% Windows scaling, where 11–12 px text gets tiny. The small standby pill is never enlarged.
        /// </summary>
        internal double UiScale { get; private set; } = 1;

        private void ApplyUiScale(MonitorState t)
        {
            double k = S.UiScale > 0.5 ? S.UiScale : t == null ? 1 : AutoScale(t);
            k = Math.Clamp(k, 1, 1.75);
            if (Math.Abs(k - UiScale) < 0.001 && Math.Abs(Width - 820 * k) < 1) return;
            _tallWindow = false;
            UiScale = k;
            ExpScale.ScaleX = ExpScale.ScaleY = k;
            LiveScale.ScaleX = LiveScale.ScaleY = k;
            Width = 820 * k;
            Height = BaseHeight * k;
            // Display-mode text snaps to pixels: crisper at 100%, but uneven when scaled, so use Ideal then.
            TextOptions.SetTextFormattingMode(this, k > 1.001 ? TextFormattingMode.Ideal : TextFormattingMode.Display);
            if (_mode != Mode.Idle) ApplyMode();          // an alert or the open panel was sized for the old scale
        }

        private static double AutoScale(MonitorState t)
        {
            // physical height / Windows scale = height in "effective" pixels. 1080 → 1.0, 1440 → 1.2, 2160 → 1.35
            double eff = t.Bounds.Height / Math.Max(1, t.Scale);
            if (eff >= 2000) return 1.35;
            if (eff >= 1400) return 1.2;
            if (eff >= 1200) return 1.1;
            return 1;
        }

        private void MoveToMonitor(MonitorState t, bool animate)
        {
            _target = t;
            _curMon = t.Handle;
            ApplyUiScale(t);
            int wpx = (int)Math.Round(Width * t.Scale), hpx = (int)Math.Round(Height * t.Scale);
            double frac = S.Position == "left" ? 0.22 : S.Position == "right" ? 0.78 : 0.5;
            int cx = t.Bounds.Left + (int)(t.Bounds.Width * frac);
            int x = cx - wpx / 2;
            if (x + wpx > t.Bounds.Right) x = t.Bounds.Right - wpx;
            if (x < t.Bounds.Left) x = t.Bounds.Left;
            if (animate) { Pill.BeginAnimation(OpacityProperty, null); Pill.Opacity = 0; Fade(Pill, 1, 220, 60); }
            Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, x, t.Bounds.Top, wpx, hpx, Native.SWP_NOACTIVATE);
        }

        // =====================================================================
        //  Standby (idle) content
        // =====================================================================
        private double IdleWidth()
        {
            var inf = new Size(double.PositiveInfinity, double.PositiveInfinity);
            IdleLeft.Measure(inf);
            IdleRight.Measure(inf);
            double l = IdleLeft.DesiredSize.Width, rr = IdleRight.DesiredSize.Width;
            if (l <= 0 && rr <= 0) return 150;
            double w = l + rr + (rr > 0 ? 44 : 0) + 22;
            return Math.Min(Math.Max(150, Math.Ceiling(w)), 640);
        }

        private void BuildIdle()
        {
            var ids = new List<string>(S.Standby.Take(AppSettings.MaxStandby));
            var mi = Now.Info;
            string sig = string.Join(",", ids) + "|" + mi.HasSession + mi.Playing + mi.Title + (mi.Art != null) + "|" + S.Temperatures + Weather.IsDay + Weather.Code;
            if (sig == _idleSig) return;
            _idleSig = sig;

            _idleVals.Clear();
            _idleBattFill = null;
            var items = ids.Select(MakeWidget).Where(x => x != null).ToList();
            int cut = items.Count <= 1 ? items.Count : (items.Count + 1) / 2;
            IdleLeft.Children.Clear();
            IdleRight.Children.Clear();
            for (int i = 0; i < items.Count; i++)
            {
                bool left = i < cut;
                items[i].Margin = new Thickness((left ? i : i - cut) == 0 ? 0 : 12, 0, 0, 0);
                (left ? IdleLeft : IdleRight).Children.Add(items[i]);
            }
            IdleLeft.HorizontalAlignment = items.Count == 1 ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        }

        private FrameworkElement MakeWidget(string id)
        {
            switch (id)
            {
                case "music":
                    {
                        var mi = Now.Info;
                        if (!mi.HasSession) return null;
                        var art = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(6), VerticalAlignment = VerticalAlignment.Center };
                        art.Background = mi.Art != null ? new ImageBrush(mi.Art) { Stretch = Stretch.UniformToFill } : (Brush)new LinearGradientBrush(Color.FromRgb(0xFF, 0x7A, 0x45), Color.FromRgb(0x2B, 0x10, 0x55), 45);
                        if (!mi.Playing) art.Opacity = 0.55;
                        return Row(art, mi.Playing ? Equalizer() : (UIElement)Ui.Icon(Ui.GPause, 11, Ui.B("DimBrush")));
                    }
                case "clock": return Row(Val("clock"));
                case "date": { var v = Val("date"); v.Foreground = Ui.B("MutedBrush"); return Row(v); }
                case "weather": return Row(Ui.Icon(WeatherGlyph(), 12, WeatherBrush()), Val("weather"));
                case "cpu": return Row(Lbl("CPU"), Val("cpu"));
                case "ram": return Row(Lbl("RAM"), Val("ram"));
                case "ctemp": return S.Temperatures ? Row(Lbl("CPU"), Val("ctemp")) : null;
                case "gtemp": return S.Temperatures ? Row(Lbl("GPU"), Val("gtemp")) : null;
                case "stemp": return S.Temperatures ? Row(Lbl("SSD"), Val("stemp")) : null;
                case "bat":
                    {
                        var shell = new Border { Width = 18, Height = 9, BorderThickness = new Thickness(1.5), BorderBrush = Ui.B("MutedBrush"), CornerRadius = new CornerRadius(3), Padding = new Thickness(1), VerticalAlignment = VerticalAlignment.Center };
                        _idleBattFill = new Border { Background = Ui.B("OkBrush"), CornerRadius = new CornerRadius(1), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
                        shell.Child = _idleBattFill;
                        return Row(shell, Val("bat"));
                    }
                case "vol": return Row(Ui.Icon(Ui.GVol, 12, Ui.B("MutedBrush")), Val("vol"));
                case "net": return Row(Lbl("↓"), Val("net"));
            }
            return null;
        }

        internal string WeatherGlyph() => !Weather.Ok ? Ui.GCloud : Weather.Code <= 1 ? (Weather.IsDay ? Ui.GSun : Ui.GMoon) : Ui.GCloud;
        internal Brush WeatherBrush() => Weather.Code <= 1 && Weather.IsDay ? Ui.Rgb(0xFF, 0xD2, 0x7A) : Ui.Rgb(0xCF, 0xD3, 0xFF);

        private static StackPanel Row(params UIElement[] kids) => Ui.H(5, kids);

        private TextBlock Val(string key)
        {
            var t = new TextBlock { FontFamily = Ui.Mono, FontSize = 12, Foreground = Ui.B("InkBrush"), VerticalAlignment = VerticalAlignment.Center };
            if (key is "cpu" or "ram" or "net" or "vol" or "bat" or "ctemp" or "gtemp" or "stemp") t.MinWidth = key == "net" ? 52 : 30;   // steady width
            _idleVals[key] = t;
            return t;
        }

        private static TextBlock Lbl(string text) => new TextBlock
        {
            Text = text, FontSize = 11, Foreground = Ui.B("MutedBrush"), VerticalAlignment = VerticalAlignment.Center
        };

        internal static FrameworkElement Equalizer()
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Height = 14, VerticalAlignment = VerticalAlignment.Center };
            double[] durs = { 0.52, 0.41, 0.6, 0.47 };
            for (int i = 0; i < 4; i++)
            {
                var rect = new Rectangle { Width = 3, Height = 14, RadiusX = 1.5, RadiusY = 1.5, Margin = new Thickness(i == 0 ? 0 : 2, 0, 0, 0), RenderTransformOrigin = new Point(0.5, 0.5) };
                rect.SetResourceReference(Shape.FillProperty, "AccentBrush");
                var st = new ScaleTransform(1, 0.3);
                rect.RenderTransform = st;
                Ui.Loop(rect, st, ScaleTransform.ScaleYProperty, new DoubleAnimation(0.25, 1, TimeSpan.FromSeconds(durs[i]))
                {
                    AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromSeconds(i * 0.13),
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                });
                sp.Children.Add(rect);
            }
            return sp;
        }

        private void SetIdle(string key, string text, Brush color = null)
        {
            if (!_idleVals.TryGetValue(key, out var t)) return;
            t.Text = text;
            if (color != null) t.Foreground = color;
        }

        internal Brush HeatBrush(float? t) =>
            t == null ? Ui.B("InkBrush") : t >= 85 ? Ui.B("HotBrush") : t >= 72 ? Ui.B("WarnBrush") : Ui.B("InkBrush");

        internal static string Deg(float? t) => t == null ? "—" : Math.Round(t.Value) + "°";

        private void UpdateIdleValues()
        {
            var now = DateTime.Now;
            SetIdle("clock", now.ToString("HH:mm"));
            SetIdle("date", now.ToString("ddd d MMM", Ro).Replace(".", ""));
            SetIdle("weather", Weather.Ok ? Math.Round(Weather.Temp) + "°" : "—");
            SetIdle("cpu", Math.Round(Stats.Cpu) + "%");
            SetIdle("ram", Stats.RamUsedGb.ToString("0.0", Ro) + " GB");
            SetIdle("ctemp", Deg(Temps.Cpu), HeatBrush(Temps.Cpu));
            SetIdle("gtemp", Deg(Temps.Gpu), HeatBrush(Temps.Gpu));
            SetIdle("stemp", Deg(Temps.Ssd), HeatBrush(Temps.Ssd));
            SetIdle("bat", Stats.BatteryPercent >= 0 ? Stats.BatteryPercent + "%" : "—");
            if (_idleBattFill != null) _idleBattFill.Width = Math.Max(0, Stats.BatteryPercent) / 100.0 * 13;
            SetIdle("vol", Audio.Muted ? "—" : Audio.Volume.ToString());
            SetIdle("net", Stats.NetDownMbps.ToString(Stats.NetDownMbps >= 10 ? "0" : "0.0", Ro) + " Mb/s");

            // small pill: time · date, song progress, a marker while the microphone is in use
            MiniTime.Text = now.ToString("HH:mm");
            MiniDate.Text = now.ToString("ddd d MMM", Ro).Replace(".", "");
            var mi = Now.Info;
            bool prog = mi.HasSession && mi.Playing && mi.Duration > TimeSpan.Zero;
            MiniProgress.Visibility = prog ? Visibility.Visible : Visibility.Collapsed;
            if (prog) MiniProgScale.ScaleX = Math.Clamp(mi.LivePosition.TotalSeconds / mi.Duration.TotalSeconds, 0, 1);
            bool micOn = PrivacyService.Microphone().InUse;
            if (micOn != (MiniExtra.Children.Count > 0))
            {
                MiniExtra.Children.Clear();
                if (micOn)
                {
                    MiniExtra.Children.Add(new Border { Width = 1, Height = 11, Background = Ui.B("TrackBrush"), Margin = new Thickness(9, 0, 7, 0) });
                    MiniExtra.Children.Add(new Ellipse { Width = 6, Height = 6, Fill = Ui.B("WarnBrush"), VerticalAlignment = VerticalAlignment.Center });
                    MiniExtra.Children.Add(new TextBlock { Text = "mic", FontSize = 11, Foreground = Ui.B("WarnBrush"), Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
                }
                if (_mode == Mode.Idle && _miniApplied) ApplyMode();
            }

            if (_mode == Mode.Idle && MiniNow() != _miniApplied) ApplyMode();
            else if (_mode == Mode.Idle && !_miniApplied)
            {
                double w = IdleWidth();
                // Values like CPU % change width every second: only animate the width, and only for a real change.
                if (Math.Abs(w - _lastIdleW) > 6)
                {
                    _lastIdleW = w;
                    Pill.BeginAnimation(WidthProperty, new DoubleAnimation(w, TimeSpan.FromMilliseconds(300)) { EasingFunction = Spring });
                }
            }
        }

        // =====================================================================
        //  Live activities
        // =====================================================================
        private bool ShowLive(UIElement content, double w, double h, int ms, bool important = false)
        {
            if (_mode == Mode.Expanded) return false;
            if (_hidden && !important) return false;
            EndLiveInteractive();
            LiveLayer.Content = content;
            _liveW = w; _liveH = h;
            _mode = Mode.Live;
            ClearDwell();
            ApplyMode();
            ApplyHidden();
            _liveTimer.Stop();
            _liveTimer.Interval = TimeSpan.FromMilliseconds(ms);
            _liveTimer.Start();
            return true;
        }

        private void EndLive()
        {
            _liveTimer.Stop();
            EndLiveInteractive();
            if (_mode != Mode.Live) return;
            _lastActive = DateTime.Now;
            _mode = Mode.Idle;
            ApplyMode();
            ApplyHidden();
            var shown = LiveLayer.Content;
            var clear = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            clear.Tick += (o, e) => { clear.Stop(); if (_mode != Mode.Live && LiveLayer.Content == shown) LiveLayer.Content = null; };
            clear.Start();
        }

        private void EndLiveInteractive()
        {
            if (!_liveInteractive) return;
            _liveInteractive = false;
            LiveLayer.IsHitTestVisible = false;
            if (_mode != Mode.Expanded) SetClickThrough(true);
        }

        // =====================================================================
        //  Screen tools: the notch gets out of the way first
        // =====================================================================
        private bool _toolBusy, _toolHidden;
        private IntPtr _lastFg;

        /// <summary>Closes and hides the notch, waits until it's really gone from the screen, runs the tool, shows the notch again.</summary>
        private async System.Threading.Tasks.Task RunHidden(Func<System.Threading.Tasks.Task> work)
        {
            if (_toolBusy) return;
            _toolBusy = true;
            _toolHidden = true;
            try
            {
                if (_mode != Mode.Idle) Collapse();
                ClearDwell();
                Pill.BeginAnimation(OpacityProperty, null);
                Pill.Opacity = 0;
                await System.Threading.Tasks.Task.Delay(260);          // let the screen repaint without the notch
                await work();
            }
            catch (Exception ex) { App.Log("Unelte ecran: " + ex); }
            finally
            {
                _toolHidden = false;
                Fade(Pill, 1, 200);
                _toolBusy = false;
            }
        }

        internal async void ScreenshotFull()
        {
            System.Windows.Media.Imaging.BitmapSource img = null;
            await RunHidden(async () =>
            {
                img = ScreenTools.Capture(ScreenTools.MonitorUnderMouse());
                await System.Threading.Tasks.Task.CompletedTask;
            });
            if (img != null) ShowCaptureResult(img);
        }

        internal async void ScreenshotArea()
        {
            System.Windows.Media.Imaging.BitmapSource img = null;
            await RunHidden(async () => img = await RegionPicker.PickAsync("Trage peste zona pe care vrei s-o salvezi"));
            if (img != null) ShowCaptureResult(img);
        }

        private void ShowCaptureResult(System.Windows.Media.Imaging.BitmapSource img)
        {
            string path;
            try { path = ScreenTools.SaveAndCopy(img); }
            catch (Exception ex) { App.Log("Captură: " + ex.Message); ToolAlert(Ui.GWarn, CWarn, "Captura nu a putut fi salvată", ex.Message); return; }
            var open = Ui.PillBtn("Deschide", () => { try { Shell.Open(path); } catch { } EndLive(); });
            var folder = Ui.PillBtn("Folder", () => { ScreenTools.ShowInFolder(path); EndLive(); });
            var btns = Ui.V(6, open, folder);
            var row = PreviewRow(img, "Captură salvată", img.PixelWidth + " × " + img.PixelHeight + " · copiată, lipește cu Ctrl+V", null, btns);
            ShowInteractive(row, 540, 100, 5000);
        }

        internal async void TextFromScreen()
        {
            System.Windows.Media.Imaging.BitmapSource img = null;
            await RunHidden(async () => img = await RegionPicker.PickAsync("Trage peste textul pe care vrei să-l copiezi"));
            if (img == null) return;

            // Visible right away: the picked area and "reading…", while Windows recognizes the text.
            var status = Ui.T("Citesc textul…", 12, "MutedBrush");
            ShowLive(PreviewRow(img, "Text din ecran", null, status, Spinner()), 470, 100, 30000, true);
            string text = null;
            try { text = await ScreenTools.RecognizeAsync(img); }
            catch (Exception ex) { App.Log("OCR: " + ex); }

            if (text == null) { ToolAlert(Ui.GWarn, CWarn, "Windows nu are recunoaștere de text", "Setări › Ora și limba › Limbă › Română › Opțiuni › Recunoaștere optică", 560); return; }
            if (string.IsNullOrWhiteSpace(text)) { ToolAlert(Ui.GWarn, CWarn, "Nu am găsit text în zona aleasă", "Încearcă o zonă mai mare sau mai clară"); return; }
            try { Clipboard.SetText(text); } catch { }
            int lines = text.Split('\n').Length;
            var preview = Ui.T(text.Replace("\r", "").Replace("\n", "  ·  "), 12, "InkBrush");
            preview.TextWrapping = TextWrapping.Wrap;
            preview.TextTrimming = TextTrimming.CharacterEllipsis;
            preview.MaxHeight = 34;
            ShowLive(PreviewRow(img, "Text copiat · " + lines + (lines == 1 ? " rând" : " rânduri"), null, preview, LiveIcon("\uE73E", COk)), 470, 100, 5000, true);
        }

        /// <summary>
        /// RAM over the limit for 20 seconds in a row: an alert with the apps that hold the most and an "Optimizează"
        /// button. At most once every 15 minutes; it waits while a game or video is fullscreen or the notch is open.
        /// </summary>
        private void CheckRam()
        {
            if (!S.RamAlert || Stats.RamTotalGb <= 0) { _ramHighSince = null; return; }
            double pct = Stats.RamUsedGb * 100 / Stats.RamTotalGb;
            if (pct < Math.Clamp(S.RamAlertPercent, 50, 98)) { _ramHighSince = null; return; }
            var now = DateTime.Now;
            _ramHighSince ??= now;
            if ((now - _lastRamAlert).TotalMinutes < 15) return;
            if (_tick % 5 == 0 || Procs.TopRam.Count == 0) Procs.RefreshAsync();      // who holds the memory, ready by the time we show it
            if ((now - _ramHighSince.Value).TotalSeconds < 20 || _mode != Mode.Idle || _hidden || _toolBusy || Procs.TopRam.Count == 0) return;
            if (ShowRamAlert(pct)) _lastRamAlert = now;
        }

        private bool ShowRamAlert(double pct)
        {
            var top = Procs.TopRam.Take(4).ToList();
            var optimize = Ui.PillBtn("Optimizează", null, true);
            var later = Ui.PillBtn("Mai târziu", null);
            later.Margin = new Thickness(0, 0, 6, 0);
            var head = LiveRow(LiveIcon(Ui.GMemory, pct >= 90 ? CHot : CWarn), "Memorie folosită: " + Math.Round(pct) + "%",
                               Stats.RamUsedGb.ToString("0.0", Ro) + " din " + Stats.RamTotalGb.ToString("0", Ro) + " GB · consumă cel mai mult:", Ui.H(0, later, optimize));
            var list = new StackPanel { Margin = new Thickness(40, 8, 4, 0) };
            double max = Math.Max(1, top.Max(p => (double)p.RamBytes));
            foreach (var p in top)
            {
                var row = Ui.Cols(Ui.Auto, Ui.Px(8), Ui.Px(130), Ui.Star(), Ui.Px(64));
                row.Put(Ui.AppBadge(p.Name, 18));
                row.Put(Ui.T(p.Name, 12, "InkBrush"), 2);
                var bar = Ui.Bar(out var fill, Ui.B(pct >= 90 ? "HotBrush" : "WarnBrush"), 4);
                fill.ScaleX = p.RamBytes / max;
                bar.Margin = new Thickness(6, 0, 10, 0);
                row.Put(bar, 3);
                var gb = Ui.T(p.RamBytes >= 1L << 30 ? (p.RamBytes / (double)(1L << 30)).ToString("0.0", Ro) + " GB" : (p.RamBytes >> 20) + " MB", 11.5, "MutedBrush", false, true);
                gb.HorizontalAlignment = HorizontalAlignment.Right;
                row.Put(gb, 4);
                row.Margin = new Thickness(0, 0, 0, 5);
                list.Children.Add(row);
            }
            var content = Ui.V(0, head, list);
            if (!ShowLive(content, 470, 66 + top.Count * 23, 15000)) return false;
            _liveInteractive = true;                    // the buttons take clicks
            LiveLayer.IsHitTestVisible = true;
            SetClickThrough(false);
            later.Click += (o, e) => EndLive();
            optimize.Click += (o, e) => { EndLive(); OptimizeMemory(); };
            return true;
        }

        internal async void OptimizeMemory()
        {
            if (_toolBusy) return;
            _toolBusy = true;
            try
            {
                if (_mode == Mode.Expanded) Collapse();
                var (u0, total) = MemoryTools.Status();
                double pct0 = total > 0 ? u0 * 100.0 / total : 0;

                // Live progress in the notch while it works.
                var sub = Ui.T(Math.Round(pct0) + "% folosit · pornesc…", 12, "MutedBrush");
                var bar = Ui.Bar(out var fill, Ui.B("InfoBrush"), 5);
                bar.Width = 110;
                var content = LiveRow(Spinner(), "Eliberez memoria…", null, bar);
                ((StackPanel)((Grid)content).Children[1]).Children.Add(sub);
                ShowLive(content, 430, 60, 60000, true);

                var started = DateTime.Now;
                var progress = new Progress<(int Done, int Total)>(p =>
                {
                    fill.ScaleX = p.Total > 0 ? (double)p.Done / p.Total : 0;
                    var (u, t) = MemoryTools.Status();
                    sub.Text = (t > 0 ? Math.Round(u * 100.0 / t) + "% folosit" : "") + " · " + p.Done + " / " + p.Total + " aplicații";
                });
                var (freed, apps, standby) = await System.Threading.Tasks.Task.Run(() => MemoryTools.Optimize(progress));
                var wait = TimeSpan.FromMilliseconds(1100) - (DateTime.Now - started);    // long enough to see it working
                if (wait > TimeSpan.Zero) await System.Threading.Tasks.Task.Delay(wait);

                var (u1, _) = MemoryTools.Status();
                double pct1 = total > 0 ? u1 * 100.0 / total : 0;
                string title = freed > 50L << 20 ? "Eliberat " + (freed / (double)(1L << 30)).ToString("0.0", Ro) + " GB" : "Memoria era deja în ordine";
                var after = Ui.Bar(out var afterFill, Ui.B("OkBrush"), 5);
                after.Width = 110;
                afterFill.ScaleX = pct0 / 100;
                afterFill.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(pct0 / 100, pct1 / 100, TimeSpan.FromMilliseconds(900)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                var done = LiveRow(LiveIcon(Ui.GMemory, COk), title, Math.Round(pct0) + "% → " + Math.Round(pct1) + "% folosit · " + apps + " aplicații" + (standby ? " · cache golit" : ""), after);
                ShowLive(done, 520, 60, 4500, true);
            }
            catch (Exception ex) { App.Log("RAM: " + ex.Message); EndLive(); }
            finally { _toolBusy = false; }
        }

        /// <summary>Thumbnail of the picture (with a camera flash), title, a line under it and something on the right.</summary>
        private Grid PreviewRow(System.Windows.Media.Imaging.BitmapSource img, string title, string sub, UIElement body, UIElement right)
        {
            double h = 72, w = Math.Clamp(h * img.PixelWidth / Math.Max(1.0, img.PixelHeight), 56, 128);
            var flash = new Border { Background = Brushes.White, Opacity = 0.85 };
            flash.BeginAnimation(OpacityProperty, new DoubleAnimation(0.85, 0, TimeSpan.FromMilliseconds(420)) { BeginTime = TimeSpan.FromMilliseconds(120) });
            var thumbGrid = new Grid();
            thumbGrid.Children.Add(new Image { Source = img, Stretch = Stretch.UniformToFill });
            thumbGrid.Children.Add(flash);
            var thumb = new Border
            {
                Width = w, Height = h, CornerRadius = new CornerRadius(10), ClipToBounds = true, Child = thumbGrid,
                BorderBrush = Ui.B("TrackBrush"), BorderThickness = new Thickness(1), Background = Ui.B("ChipBrush")
            };
            thumb.Clip = new RectangleGeometry(new Rect(0, 0, w, h), 10, 10);
            var pop = new ScaleTransform(0.85, 0.85);
            thumb.RenderTransformOrigin = new Point(0.5, 0.5);
            thumb.RenderTransform = pop;
            var ease = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 };
            pop.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(380)) { EasingFunction = ease });
            pop.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(380)) { EasingFunction = ease });

            var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 10, 0) };
            texts.Children.Add(Ui.T(title, 13.5, "InkBrush", true));
            if (!string.IsNullOrEmpty(sub)) texts.Children.Add(Ui.T(sub, 12, "MutedBrush"));
            if (body != null) { if (body is FrameworkElement fb) fb.Margin = new Thickness(0, 3, 0, 0); texts.Children.Add(body); }

            var g = Ui.Cols(Ui.Auto, Ui.Star(), Ui.Auto);
            g.Margin = new Thickness(4, 0, 4, 0);
            g.Put(thumb);
            g.Put(texts, 1);
            if (right is FrameworkElement fr) { fr.VerticalAlignment = VerticalAlignment.Center; g.Put(fr, 2); }
            return g;
        }

        private static FrameworkElement Spinner()
        {
            var rot = new RotateTransform();
            var icon = Ui.Icon("\uE895", 15, Ui.B("InfoBrush"));
            icon.RenderTransformOrigin = new Point(0.5, 0.5);
            icon.RenderTransform = rot;
            Ui.Loop(icon, rot, RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1)) { RepeatBehavior = RepeatBehavior.Forever });
            return new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Background = Ui.Rgb(0x5A, 0xA9, 0xFF, 0x29), Child = icon };
        }

        private void ShowInteractive(UIElement content, double w, double h, int ms)
        {
            if (!ShowLive(content, w, h, ms, true)) return;     // notch open: no interactive alert, so hover keeps working
            _liveInteractive = true;
            LiveLayer.IsHitTestVisible = true;
            SetClickThrough(false);
        }

        private void ToolAlert(string glyph, Color c, string title, string sub, double width = 440)
        {
            Dispatcher.InvokeAsync(() => ShowLive(LiveRow(LiveIcon(glyph, c), title, sub, null), width, 58, 4200, true), System.Windows.Threading.DispatcherPriority.Background);
        }

        internal Grid LiveRow(UIElement icon, string title, string sub, UIElement extra)
        {
            var g = Ui.Cols(Ui.Auto, Ui.Star(), Ui.Auto);
            g.Put(icon);
            var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 10, 0) };
            texts.Children.Add(Ui.T(title, 13, "InkBrush", true));
            if (!string.IsNullOrEmpty(sub)) texts.Children.Add(Ui.T(sub, 12, "MutedBrush"));
            g.Put(texts, 1);
            if (extra is FrameworkElement fe) { fe.VerticalAlignment = VerticalAlignment.Center; g.Put(fe, 2); }
            return g;
        }

        internal static Border LiveIcon(string glyph, Color fg) => new Border
        {
            Width = 30, Height = 30, CornerRadius = new CornerRadius(15),
            Background = new SolidColorBrush(Color.FromArgb(0x29, fg.R, fg.G, fg.B)),
            Child = Ui.Icon(glyph, 15, new SolidColorBrush(fg))
        };

        private static readonly Color CWhite = Color.FromRgb(0xF1, 0xF2, 0xF4);
        private static readonly Color COk = Color.FromRgb(0x3D, 0xDC, 0x84);
        private static readonly Color CWarn = Color.FromRgb(0xFF, 0x9F, 0x43);
        private static readonly Color CHot = Color.FromRgb(0xFF, 0x5C, 0x5C);

        private Grid _volLive;
        private ScaleTransform _volFill;
        private TextBlock _volNum, _volIcon;

        private void LiveVolume(int v, bool muted)
        {
            // Dragging the Windows volume sends dozens of changes a second: update the alert in place instead of rebuilding it.
            if (_mode == Mode.Live && LiveLayer.Content == _volLive && _volLive != null)
            {
                _volFill.ScaleX = muted ? 0 : v / 100.0;
                _volNum.Text = muted ? "—" : v.ToString();
                _volIcon.Text = muted ? Ui.GMute : Ui.GVol;
                _liveTimer.Stop();
                _liveTimer.Start();
                return;
            }
            var g = Ui.Cols(Ui.Auto, Ui.Star(), Ui.Px(30));
            _volIcon = Ui.Icon(muted ? Ui.GMute : Ui.GVol, 13);
            g.Put(new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Background = Ui.B("ChipHoverBrush"), Child = _volIcon });
            var bar = Ui.Bar(out _volFill, Ui.B("InkBrush"));
            bar.Margin = new Thickness(10, 0, 10, 0);
            _volFill.ScaleX = muted ? 0 : v / 100.0;
            g.Put(bar, 1);
            _volNum = Ui.T(muted ? "—" : v.ToString(), 12, "InkBrush", false, true);
            _volNum.TextAlignment = TextAlignment.Right;
            g.Put(_volNum, 2);
            _volLive = g;
            ShowLive(g, 270, 40, 1600);
        }

        /// <summary>20-20-20: look at something far away for 20 seconds. Has a "Sari" button, so it accepts clicks.</summary>
        private void ShowEyeBreak()
        {
            int left = 20;
            var count = Ui.T("0:20", 13, "OkBrush", true, true);
            var skip = Ui.PillBtn("Sari", null);
            var extra = Ui.H(10, count, skip);
            var content = LiveRow(LiveIcon(Ui.GEye, COk), "Pauză pentru ochi", "Privește ceva departe, 20 de secunde", extra);
            if (!ShowLive(content, 360, 48, 21000, true)) return;
            _liveInteractive = true;
            LiveLayer.IsHitTestVisible = true;
            SetClickThrough(false);
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            t.Tick += (o, e) =>
            {
                left--;
                count.Text = "0:" + Math.Max(0, left).ToString("00");
                if (left <= 0 || LiveLayer.Content != content) t.Stop();
            };
            skip.Click += (o, e) => { t.Stop(); EndLive(); };
            t.Start();
        }

        // =====================================================================
        //  Timers
        // =====================================================================
        private void SecondTick()
        {
            _tick++;
            Stats.Sample();
            CpuHist.Add(Stats.Cpu);
            TempHist.Add(Temps.Cpu ?? double.NaN);
            if (CpuHist.Count > 60) CpuHist.RemoveAt(0);
            if (TempHist.Count > 60) TempHist.RemoveAt(0);
            bool tempsShown = _mode == Mode.Expanded || S.Standby.Any(x => x is "ctemp" or "gtemp" or "stemp");
            if (S.Temperatures && _tick % (tempsShown ? 2 : 15) == 0) Temps.RefreshAsync();     // 15 s is still enough for the heat alert
            if (_tick % 3 == 0) Media.Poll();       // safety net: some apps don't announce pause/next
            if (_tick % 3 == 1) Audio.CheckDevice(); // headphones / Bluetooth became the default output
            if (_tick % 5 == 2 && S.ThemeMode == "auto" && ThemeManager.IsLight(S) != _themeLight) ApplySettings();   // Windows switched light/dark

            if (Stats.HasBattery)
            {
                if (_lastCharging != null && _lastCharging.Value != Stats.Charging)
                {
                    var pct = Ui.T(Stats.BatteryPercent + "%", 12, Stats.Charging ? "OkBrush" : "InkBrush", false, true);
                    ShowLive(LiveRow(LiveIcon(Stats.Charging ? Ui.GBolt : Ui.GBattery, Stats.Charging ? COk : CWhite), Stats.Charging ? "Se încarcă" : "Pe baterie", null, pct), 260, 40, 2600);
                }
                _lastCharging = Stats.Charging;
                int b = Stats.BatteryPercent;
                if (Stats.Charging) _lastBatAlert = 101;
                else if (b >= 0 && ((b <= 10 && _lastBatAlert > 10) || (b <= 20 && _lastBatAlert > 20)))
                {
                    _lastBatAlert = b <= 10 ? 10 : 20;
                    ShowLive(LiveRow(LiveIcon(Ui.GBatteryLow, b <= 10 ? CHot : CWarn), "Baterie descărcată: " + b + "%", "Conectează încărcătorul", null), 330, 54, 5000, true);
                }
            }

            float hot = Math.Max(Temps.Cpu ?? 0, Temps.Gpu ?? 0);
            if (S.Temperatures && hot >= 88 && (DateTime.Now - _lastHotAlert).TotalMinutes > 5)
            {
                _lastHotAlert = DateTime.Now;
                ShowLive(LiveRow(LiveIcon(Ui.GWarn, CHot), "Temperatură ridicată", "CPU " + Deg(Temps.Cpu) + "C · GPU " + Deg(Temps.Gpu) + "C", null), 360, 54, 5000, true);
            }

            CheckRam();
            UpdateTick();

            // eye break: count active minutes at the PC, reset after a real pause; never over a game or video
            if (S.EyeBreak)
            {
                double idle = Native.IdleSeconds();
                if (idle > 300) _activeSeconds = 0;
                else if (idle < 60) _activeSeconds++;
                if (_activeSeconds >= Math.Max(5, S.EyeBreakMinutes) * 60 && _mode == Mode.Idle && !_hidden)
                {
                    _activeSeconds = 0;
                    ShowEyeBreak();
                }
            }

            if (DateTime.Now - _weatherAt > TimeSpan.FromMinutes(20)) RefreshWeather();
            if (!string.IsNullOrWhiteSpace(S.CalendarIcs) && DateTime.Now - _calendarAt > TimeSpan.FromMinutes(15)) RefreshCalendar();

            BuildIdle();
            UpdateIdleValues();
            if (_mode == Mode.Expanded)
            {
                HeaderClock.Text = DateTime.Now.ToString("HH:mm");
                _pane?.Refresh();
            }
        }

        private async void RefreshWeather()
        {
            _weatherAt = DateTime.Now;
            _weatherFor = S.Lat.ToString(CultureInfo.InvariantCulture) + "," + S.Lon.ToString(CultureInfo.InvariantCulture);
            var w = await WeatherService.GetAsync(S.Lat, S.Lon);
            if (w.Ok) Weather = w;
            else _weatherAt = DateTime.Now - TimeSpan.FromMinutes(17);
            _idleSig = null;
            BuildIdle();
            UpdateIdleValues();
        }

        private async void RefreshCalendar()
        {
            _calendarAt = DateTime.Now;
            var evs = await CalendarService.GetUpcomingAsync(S.CalendarIcs);
            if (evs != null) Events = evs;          // null: the previous refresh was still running
        }

        // =====================================================================
        //  Media, volume, clipboard
        // =====================================================================
        private void OnMedia(bool trackChanged)
        {
            BuildIdle();
            UpdateIdleValues();
            _home.OnMediaChanged();
            (_pane as WidgetPage)?.MediaChanged();
            if (_mode == Mode.Expanded && _pane == _sources) _pane.Refresh();
            var mi = Now.Info;
            if (trackChanged && mi.Playing && _mode != Mode.Expanded && !SourceInFront(mi))
            {
                var art = new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(8), Background = mi.Art != null ? new ImageBrush(mi.Art) { Stretch = Stretch.UniformToFill } : Ui.B("TrackBrush") };
                ShowLive(LiveRow(art, mi.Title, mi.Artist, Equalizer()), 360, 54, 3200);
            }
        }

        /// <summary>
        /// Is the app (or browser) that plays this the window you're using right now? Then you already see that it
        /// started, and the "now playing" alert would only get in the way.
        /// </summary>
        private static bool SourceInFront(MediaInfo mi)
        {
            try
            {
                string path = Native.ProcessPath(Native.GetForegroundWindow());
                if (string.IsNullOrEmpty(path)) return false;
                string exe = System.IO.Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
                if (exe.Length < 3 || exe == "applicationframehost" || exe == "explorer") return false;
                if (mi.Tab != null) return string.Equals(mi.Tab.Browser, exe, StringComparison.OrdinalIgnoreCase);
                string app = ((mi.AppId ?? "") + " " + (mi.App ?? "")).ToLowerInvariant();
                return app.Contains(exe);
            }
            catch { return false; }
        }

        private void OnSystemVolume(int v, bool muted)
        {
            SetIdle("vol", muted ? "—" : v.ToString());
            _home.SyncVolume(v, muted);
            // Windows also reports "volume" when nothing changed (a device re-checked, another app touching it): no alert then.
            bool same = _lastVol < 0 || (v == _lastVol && muted == _lastMuted);
            _lastVol = v; _lastMuted = muted;
            if (!same && _mode != Mode.Expanded && (DateTime.Now - _volSetByUs).TotalMilliseconds > 600) LiveVolume(v, muted);
        }

        internal void SetMasterVolume(int v)
        {
            _volSetByUs = DateTime.Now;
            Audio.Volume = v;
        }

        internal void ToggleMasterMute()
        {
            _volSetByUs = DateTime.Now;
            Audio.Muted = !Audio.Muted;
            _home.SyncVolume(Audio.Volume, Audio.Muted);
        }

        private void OnClipboard()
        {
            if (_ignoreClip) { _ignoreClip = false; return; }
            try
            {
                if (!Clipboard.ContainsText()) return;
                if (IsPrivateClip()) return;           // passwords from password managers are not recorded
                string t = Clipboard.GetText();
                if (string.IsNullOrWhiteSpace(t)) return;
                var existing = Clips.FirstOrDefault(c => c.Text == t);
                if (existing != null) { existing.At = DateTime.Now; if (!existing.Pinned) { Clips.Remove(existing); Clips.Insert(0, existing); } }
                else Clips.Insert(0, new ClipItem { Text = t, At = DateTime.Now });
                var unpinned = Clips.Where(c => !c.Pinned).ToList();
                foreach (var old in unpinned.Skip(20)) Clips.Remove(old);
                _tools.ClipsChanged();
            }
            catch { /* clipboard busy in another app */ }
        }

        /// <summary>
        /// Password managers (KeePass, Bitwarden, 1Password…) mark copied secrets so clipboard tools don't record them;
        /// Windows' own clipboard history respects the same markers.
        /// </summary>
        private static bool IsPrivateClip()
        {
            try
            {
                var d = Clipboard.GetDataObject();
                if (d == null) return false;
                if (d.GetDataPresent("ExcludeClipboardContentFromMonitorProcessing") || d.GetDataPresent("Clipboard Viewer Ignore")) return true;
                foreach (var fmt in new[] { "CanIncludeInClipboardHistory", "CanUploadToCloudClipboard" })
                {
                    if (!d.GetDataPresent(fmt)) continue;
                    var o = d.GetData(fmt);
                    byte[] b = o is System.IO.MemoryStream ms ? ms.ToArray() : o as byte[];
                    if (b != null && b.Length >= 4 && BitConverter.ToInt32(b, 0) == 0) return true;
                }
            }
            catch { return true; }        // clipboard busy right after a password manager wrote to it: when in doubt, don't record
            return false;
        }

        internal void CopyToClipboard(string text)
        {
            try { _ignoreClip = true; Clipboard.SetText(text); }
            catch { _ignoreClip = false; }
        }

        internal void SavePins()
        {
            S.PinnedClips = Clips.Where(c => c.Pinned).Select(c => c.Text).ToList();
            S.Save();
        }
    }
}
