using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using WinNotch.Core.Actions;
using WinNotch.Core.Flags;
using WinNotch.Features.NotchAnchored;

namespace WinNotch.Features.WindowV2
{
    /// <summary>
    /// P52: the WinNotch window. Its signature is the header — the same silhouette as the anchored notch (P50), so the
    /// window reads as the notch, unfolded. Under it there is <b>one</b> navigation: four tabs (Workspace, Widgeturi,
    /// Teme, Sistem), each a whole area of the app. A tab may bring a column of its own on the left (its pages, its
    /// sections) and an inspector on the right, but those belong to the tab, not to the window. At the bottom, the
    /// command field — the same one the Command Bar uses.
    /// <para>Every decision about sizes and tabs is in <see cref="LayoutRules"/> (pure, tested). Nothing here invents a
    /// feature: the pages, the widgets, the themes and the settings are the ones the app already has.</para>
    /// </summary>
    public sealed class WindowV2 : Window
    {
        private readonly AppSettings _s;
        private readonly NotchWindow _notch;

        private readonly Path _headerShape = new Path { IsHitTestVisible = false, UseLayoutRounding = false, SnapsToDevicePixels = false };
        private readonly StackPanel _tabs = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        private readonly TextBlock _clock = Ui.T("--:--", 12, "MutedBrush", false, true);
        private readonly Border _bodyHost = new Border();
        private readonly TextBox _command = new TextBox { FontSize = 13.5, BorderThickness = new Thickness(0), Background = Brushes.Transparent };
        private readonly TextBlock _hint = Ui.T("", 12, "MutedBrush");

        private readonly DispatcherTimer _clockTick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        private readonly DispatcherTimer _live = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        private readonly DispatcherTimer _soonTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        private Action _pending;
        private int _ticks;

        private WorkspaceView _workspace;
        private WidgetsView _widgets;
        private SystemView _system;
        private ScrollViewer _themesHost;
        private readonly EmbeddedPages _themes = new EmbeddedPages();

        private string _tab = LayoutRules.DefaultTab;
        private string _pageId, _slotId;
        private Action<string> _flagHandler;

        public WindowV2(AppSettings s, NotchWindow notch)
        {
            _s = s; _notch = notch;
            Title = "WinNotch";
            MinWidth = LayoutRules.MinWidth;
            MinHeight = LayoutRules.MinHeight;
            var wa = SystemParameters.WorkArea;
            Width = Math.Min(1440, wa.Width * 0.92);
            Height = Math.Min(920, wa.Height * 0.9);
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;
            FontFamily = (FontFamily)Application.Current.FindResource("UiFont");
            SetResourceReference(ForegroundProperty, "InkBrush");
            SetResourceReference(BackgroundProperty, "SegBrush");       // opaque: NotchBrush carries the pill's transparency

            Content = BuildShell();
            SizeChanged += (o, e) => Relayout();
            PreviewKeyDown += OnKey;
            _clockTick.Tick += (o, e) => OnClock();
            _live.Tick += (o, e) => LiveTick();
            _soonTimer.Tick += (o, e) => { _soonTimer.Stop(); var a = _pending; _pending = null; Guarded(() => a?.Invoke()); };
            Loaded += (o, e) => { OnClock(); _clockTick.Start(); _live.Start(); Relayout(); };
            StateChanged += (o, e) =>
            {
                bool off = WindowState == WindowState.Minimized;
                if (off) { _clockTick.Stop(); _live.Stop(); }
                else { OnClock(); _clockTick.Start(); _live.Start(); }
            };
            Closed += (o, e) =>
            {
                _clockTick.Stop();
                _live.Stop();
                _soonTimer.Stop();
                Flush();                                   // a name or a slider still pending is saved, not lost
                _themes.Detach();
                _system?.Detach();
                if (Application.Current is App app && ReferenceEquals(app.HostedWorkspace, _workspace)) app.HostedWorkspace = null;
                if (_flagHandler != null && FeatureFlags.Current != null) FeatureFlags.Current.Changed -= _flagHandler;
                _flagHandler = null;
                _s.Save();
            };
            // The switch can be turned off from Settings while the window is open: then it closes, like any feature stopping.
            _flagHandler = id =>
            {
                if (!string.Equals(id, LayoutRules.FeatureId, StringComparison.Ordinal)) return;
                Dispatcher.InvokeAsync(() => { if (!(FeatureFlags.Current?.IsEnabled(LayoutRules.FeatureId) ?? false)) Close(); });
            };
            if (FeatureFlags.Current != null) FeatureFlags.Current.Changed += _flagHandler;
        }

        /// <summary>Opens the window on a tab; the old window's ids ("themes", "settings", "news") and page ids still work.</summary>
        public void Open(string pageId = null, string slotId = null)
        {
            Guarded(() =>
            {
                string tab = LayoutRules.TabFor(pageId);
                bool samePage = tab == LayoutRules.Workspace && pageId == _pageId && slotId == _slotId;
                _pageId = tab == LayoutRules.Workspace ? pageId : null;
                _slotId = _pageId != null ? slotId : null;
                // Opening the window again where it already is must not rebuild it: a form keeps what is typed in it.
                if (tab == _tab && (tab != LayoutRules.Workspace ? ShowsSection(pageId) : samePage) && _bodyHost.Child != null)
                {
                    Relayout();
                    return;
                }
                _tab = tab;
                ShowTab(pageId);
            });
        }

        /// <summary>True when the Sistem tab already shows the section this request asks for.</summary>
        private bool ShowsSection(string pageId) => _tab != LayoutRules.System || _system == null || pageId == null;

        /// <summary>P14 ("settings.*" actions): the settings page, scrolled to one option and focused.</summary>
        internal void RevealSetting(string target) => Guarded(() =>
        {
            Open("settings");
            _system?.Reveal(target);
        });

        /// <summary>A page was edited in the notch: the workspace, if open on it, shows the new layout.</summary>
        internal void PageChangedElsewhere(string pageId) => Guarded(() => _workspace?.PageChangedElsewhere(pageId));

        internal void PagesChangedElsewhere() => Guarded(() => _workspace?.PagesChangedElsewhere());

        /// <summary>P51: Esc closes the open pop-up (a widget's sizes) before the window.</summary>
        internal bool CloseOpenPopup() => _workspace?.CloseOpenPopup() ?? false;

        // ------------------------------------------------------------------ the shell

        private UIElement BuildShell()
        {
            var root = Ui.Rows(Ui.Px(LayoutRules.HeaderHeight), Ui.Star(), Ui.Auto);
            root.Put(BuildHeader());
            root.Put(_bodyHost, 0, 1);
            root.Put(BuildCommandBar(), 0, 2);
            return root;
        }

        /// <summary>The header is the notch, unfolded: the same silhouette as P50, with the window's four tabs on it.</summary>
        private UIElement BuildHeader()
        {
            var host = new Grid();
            _headerShape.SetResourceReference(Shape.FillProperty, "NotchBrush");
            _headerShape.HorizontalAlignment = HorizontalAlignment.Center;
            _headerShape.VerticalAlignment = VerticalAlignment.Top;
            host.Children.Add(_headerShape);

            var row = Ui.Cols(Ui.Auto, Ui.Star(), Ui.Auto);
            row.Margin = new Thickness(LayoutRules.Pad, 0, LayoutRules.Pad, 0);
            row.VerticalAlignment = VerticalAlignment.Center;
            row.Put(_tabs);
            var right = Ui.H(10, Ui.IconBtn("", ToggleTheme, "Schimbă tema", 28, 13), _clock);
            right.VerticalAlignment = VerticalAlignment.Center;
            row.Put(right, 2);
            host.Children.Add(row);
            BuildTabs();
            return host;
        }

        private void BuildTabs()
        {
            _tabs.Children.Clear();
            foreach (var t in LayoutRules.Tabs)
            {
                var tab = t;
                bool on = string.Equals(t.Id, _tab, StringComparison.Ordinal);
                // NavButton, not IconButton: that one forces 30x30 and would cut the titles down to two letters
                var btn = new Button
                {
                    Style = Ui.S("NavButton"), Padding = new Thickness(12, 6, 12, 6), Cursor = Cursors.Hand,
                    Margin = new Thickness(0, 0, 4, 0),
                };
                var content = Ui.H(7, Ui.Icon(t.Glyph, 13, Ui.B(on ? "InkBrush" : "MutedBrush")),
                                      Ui.T(t.Title, 13, on ? "InkBrush" : "MutedBrush", on));
                content.VerticalAlignment = VerticalAlignment.Center;
                btn.Content = content;
                System.Windows.Automation.AutomationProperties.SetName(btn, t.Title);
                if (on) btn.SetResourceReference(Control.BackgroundProperty, "ChipHoverBrush");
                btn.Click += (o, e) => { if (!on) { _tab = tab.Id; ShowTab(null); } };
                _tabs.Children.Add(btn);
            }
        }

        private UIElement BuildCommandBar()
        {
            var bar = new Border { Padding = new Thickness(LayoutRules.Pad, 10, LayoutRules.Pad, 12), BorderThickness = new Thickness(0, 1, 0, 0) };
            bar.SetResourceReference(Border.BackgroundProperty, "ChipBrush");
            bar.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");

            var field = Ui.Cols(Ui.Auto, Ui.Star(), Ui.Auto);
            field.Put(Ui.Icon("", 13, Ui.B("MutedBrush")));
            _command.Margin = new Thickness(8, 0, 8, 0);
            _command.SetResourceReference(Control.ForegroundProperty, "InkBrush");
            _command.SetResourceReference(TextBoxBase.CaretBrushProperty, "InkBrush");
            _command.VerticalAlignment = VerticalAlignment.Center;
            field.Put(_command, 1);
            field.Put(Ui.Chip(Ui.T("Win + Alt + Space", 11, "DimBrush"), 8, 3, LayoutRules.ChipRadius), 2);
            var box = new Border { Child = field, Padding = new Thickness(12, 8, 8, 8), CornerRadius = new CornerRadius(LayoutRules.ChipRadius), MaxWidth = 720 };
            box.SetResourceReference(Border.BackgroundProperty, "TrackBrush");

            var rows = Ui.Rows(Ui.Auto, Ui.Auto);
            rows.Put(box);
            _hint.Margin = new Thickness(4, 6, 0, 0);
            _hint.TextWrapping = TextWrapping.Wrap;
            rows.Put(_hint, 0, 1);
            bar.Child = rows;
            return bar;
        }

        // ------------------------------------------------------------------ the tabs

        private void ShowTab(string pageId)
        {
            Guarded(() =>
            {
                BuildTabs();
                switch (_tab)
                {
                    case LayoutRules.Workspace:
                        EnsureWorkspace();
                        _workspace.Open(pageId ?? _pageId, _slotId);
                        _bodyHost.Child = _workspace;
                        break;
                    case LayoutRules.Widgets:
                        // the workspace owns the page the widgets are added to, even if you never opened that tab
                        if (_workspace == null) { EnsureWorkspace(); _workspace.Open(_pageId, _slotId); }
                        _widgets ??= new WidgetsView(() => _workspace, Say);
                        _widgets.Open();
                        _bodyHost.Child = _widgets;
                        break;
                    case LayoutRules.Themes:
                        _themesHost ??= new ScrollViewer
                        {
                            Style = Ui.S("SlimScroll"),
                            Padding = new Thickness(LayoutRules.Pad, LayoutRules.Pad, LayoutRules.Pad, LayoutRules.Pad),
                        };
                        _themesHost.Content = _themes.Themes(_s, _notch, ReloadThemes, Soon);
                        _bodyHost.Child = _themesHost;
                        break;
                    default:
                        _system ??= new SystemView(_s, _notch, () => this, Say);
                        _system.Open(LayoutRules.SectionFor(pageId), BodyWidth());
                        _bodyHost.Child = _system;
                        break;
                }
                if (_tab != LayoutRules.System) _system?.Detach();
                Relayout();
            });
        }

        /// <summary>
        /// The workspace is built once and kept: it owns the open page, which the other tabs ask about. Building it
        /// does not open a page — the caller does that, so a page is never built twice in a row.
        /// </summary>
        private void EnsureWorkspace()
        {
            if (_workspace != null) return;
            _workspace = new WorkspaceView(_s, _notch, () => this, Soon, Flush, Say);
            if (Application.Current is App app) app.HostedWorkspace = _workspace;
        }

        /// <summary>Rebuilds the themes page where it is, keeping the place you had scrolled to.</summary>
        private void ReloadThemes()
        {
            double off = _themesHost?.VerticalOffset ?? 0;
            if (_themesHost == null) return;
            _themesHost.Content = _themes.Themes(_s, _notch, ReloadThemes, Soon);
            Dispatcher.InvokeAsync(() => _themesHost.ScrollToVerticalOffset(off), DispatcherPriority.Loaded);
        }

        /// <summary>Light ↔ dark, through the existing theme manager (the window takes the app's theme, like everything else).</summary>
        private void ToggleTheme()
        {
            _s.ThemeMode = string.Equals(_s.ThemeMode, "light", StringComparison.Ordinal) ? "dark" : "light";
            _s.Save();
            ThemeManager.Apply(_s);
            _notch?.ApplySettings();
            if (_tab == LayoutRules.Themes) ReloadThemes();
        }

        // ------------------------------------------------------------------ time, timers, messages

        private void OnClock() => Guarded(() =>
        {
            if (!IsVisible || WindowState == WindowState.Minimized) return;
            _clock.Text = DateTime.Now.ToString("HH:mm");
        });

        /// <summary>The live page in the workspace keeps itself fresh, as it does in the notch.</summary>
        private void LiveTick() => Guarded(() =>
        {
            var page = _workspace?.Page;
            if (page == null || _tab != LayoutRules.Workspace || !IsVisible || WindowState == WindowState.Minimized) return;
            page.Fast();
            if (++_ticks % 10 == 0) { page.MediaChanged(); page.Refresh(); }
        });

        /// <summary>Runs a change shortly after the last keystroke (a page's name, a slider).</summary>
        private void Soon(Action a)
        {
            _pending = a;
            _soonTimer.Stop();
            _soonTimer.Start();
        }

        private void Flush()
        {
            if (_pending == null) return;
            _soonTimer.Stop();
            var a = _pending; _pending = null;
            Guarded(a);
        }

        /// <summary>One line in the bottom bar: what an action answered, or why something did not happen.</summary>
        private void Say(string text) => _hint.Text = text;

        private void Guarded(Action a)
        {
            try { a(); }
            catch (Exception ex) { FeatureFlags.Current?.ReportError(LayoutRules.FeatureId, ex); }
        }

        // ------------------------------------------------------------------ layout

        private double BodyWidth() => ActualWidth > 0 ? ActualWidth : Width;

        private void Relayout() => Guarded(() =>
        {
            double w = BodyWidth();
            _hint.Text = LayoutRules.Hint(FeatureFlags.Current?.IsEnabled(Features.CommandBar.CommandBarRules.FeatureId) ?? false);
            _workspace?.Relayout(w);
            _system?.Relayout(w);
            DrawHeader(w);
        });

        /// <summary>The header's silhouette: the anchored notch's shape (P50), through the one shared translator.</summary>
        private void DrawHeader(double windowWidth)
        {
            double w = Math.Max(240, windowWidth - 2 * LayoutRules.Pad);
            double h = LayoutRules.HeaderHeight;
            double r = AnchoredGeometry.Radius(_s.CornerRadius);
            double e = AnchoredGeometry.Ear(_s.CornerRadius, w, windowWidth, h);
            _headerShape.Width = w + 2 * Math.Max(0, e);
            _headerShape.Height = h;
            _headerShape.Data = AnchoredShape.Silhouette(w, h, r, e);
        }

        private void OnKey(object sender, KeyEventArgs e)
        {
            // P51: the pop-up of a page closes first; Esc only closes the window when nothing is open over it
            if (e.Key == Key.Escape) { if (!CloseOpenPopup()) Close(); e.Handled = true; }
            else if (e.Key == Key.K && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                _command.Focus();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && _command.IsKeyboardFocusWithin && !string.IsNullOrWhiteSpace(_command.Text))
            {
                RunTyped();
                e.Handled = true;
            }
        }

        /// <summary>The command field starts an action by name, through the registry — no second way of running things.</summary>
        private void RunTyped() => Guarded(() =>
        {
            var reg = ActionRegistry.Current;
            if (reg == null) { Say("Acțiunile nu sunt pornite."); return; }
            string text = _command.Text.Trim();
            var hit = reg.All.FirstOrDefault(a => (a.AllowedInvokers & ActionInvoker.UI) == ActionInvoker.UI &&
                                                  (a.Title ?? "").Contains(text, StringComparison.OrdinalIgnoreCase));
            if (hit == null) { Say("Nu am găsit nicio acțiune pentru „" + text + "”."); return; }
            bool confirmed = hit.Safety == ActionSafety.Safe ||
                             MessageBox.Show(this, hit.Title + "?", "WinNotch", MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK;
            if (!confirmed) return;
            var task = reg.InvokeAsync(hit.Id, null, ActionInvoker.UI, default, confirmed);
            _command.Clear();
            if (task == null) return;
            _ = task.ContinueWith(t =>
            {
                string msg = t.IsCompletedSuccessfully ? t.Result?.Message : null;
                if (!string.IsNullOrEmpty(msg)) Dispatcher.InvokeAsync(() => Say(msg));
            }, System.Threading.Tasks.TaskScheduler.Default);
        });
    }
}
