using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using WinNotch.Core.Actions;
using WinNotch.Core.Flags;
using WinNotch.Features.CommandBar;
using WinNotch.Features.Smoke;
using WinNotch.Services;

namespace WinNotch
{
    /// <summary>
    /// The notch's side of the Command Bar (P14, ADR 0007). With the "command-bar" switch off nothing here runs and no
    /// shortcut is registered. With it on, Win+Alt+Space (or Win+Alt+K, Settings) turns the pill into a search box over
    /// the action registry. While it is open the notch is in its "open" mode (Mode.Expanded, without the panel): alerts
    /// are held exactly as with the open notch, on both paths (the legacy ShowLive refuses them; the Activity Manager keeps
    /// Critical and persistent ones until it closes, and drops the rest). Keyboard focus is taken and given back the way
    /// the launcher does it (EnableTyping / StopTyping in Collapse, to LastForeground). The logic is in CommandBarModel.cs.
    /// </summary>
    public partial class NotchWindow
    {
        /// <summary>RegisterHotKey id (1–3: Win+Alt+N / S / T).</summary>
        private const int CommandBarHotkeyId = 4;

        private CommandBarHotkey _cmdHotkey;
        private Action<string> _cmdFlagHandler;
        /// <summary>UI-thread copy of the switch.</summary>
        private bool _cmdOn;
        private bool _cmdOpen;
        private bool _cmdAlertPending, _cmdAlertQueued;
        private CommandBarKey _cmdAlertKey;
        private CommandBarSession _cmdSession;
        private Border _cmdLayer;
        private ScaleTransform _cmdScale;
        private TextBox _cmdBox;
        private TextBlock _cmdHint, _cmdMessage;
        private StackPanel _cmdList;
        private double _cmdShownH = -1, _cmdShownScale = -1;
        /// <summary>The context engine's reading of the window in front (never started: no hook, just Read()).</summary>
        private Features.Context.ForegroundSource _cmdForeground;

        private static bool CommandBarEnabled() => FeatureFlags.Current?.IsEnabled(CommandBarRules.FeatureId) ?? false;

        /// <summary>The bar is open (for the smoke status).</summary>
        internal bool CommandBarOpen => _cmdOpen;

        /// <summary>For Settings: the shortcut that is taken by another app, or null.</summary>
        internal string CommandBarHotkeyProblem() =>
            _cmdHotkey?.Conflict is CommandBarKey k ? CommandBarHotkeys.ConflictTitle(k) + ". " + CommandBarHotkeys.ConflictHint(k) + "." : null;

        /// <summary>Called once at startup (App.StartApp), after the actions are registered. UI thread.</summary>
        internal void StartCommandBar()
        {
            if (_cmdHotkey != null || _hwnd == IntPtr.Zero) return;
            _cmdHotkey = new CommandBarHotkey(
                vk => Native.RegisterHotKey(_hwnd, CommandBarHotkeyId, Native.MOD_WIN | Native.MOD_ALT | Native.MOD_NOREPEAT, vk),
                () => Native.UnregisterHotKey(_hwnd, CommandBarHotkeyId));
            _cmdFlagHandler = id =>
            {
                if (id == CommandBarRules.FeatureId) Dispatcher.InvokeAsync(ApplyCommandBarSwitch);       // any thread → UI
            };
            if (FeatureFlags.Current != null) FeatureFlags.Current.Changed += _cmdFlagHandler;
            Deactivated += OnCommandBarDeactivated;
            ApplyCommandBarSwitch();
        }

        private void StopCommandBar()
        {
            if (_cmdFlagHandler != null && FeatureFlags.Current != null) FeatureFlags.Current.Changed -= _cmdFlagHandler;
            _cmdFlagHandler = null;
            Deactivated -= OnCommandBarDeactivated;
            if (_cmdOpen) TearDownCommandBar();
            try { _cmdHotkey?.Apply(false, CommandBarKey.Space); } catch (Exception) { /* closing anyway */ }
            _cmdHotkey = null;
            _cmdOn = false;
            _cmdAlertPending = false;
        }

        /// <summary>The switch changed (or startup): read it again, (un)register the shortcut.</summary>
        private void ApplyCommandBarSwitch()
        {
            if (_cmdHotkey == null) return;
            bool on = CommandBarEnabled();
            if (on != _cmdOn)
            {
                _cmdOn = on;
                App.Log("Command Bar: " + (on ? "pornit." : "oprit."));
                if (!on) { CloseCommandBar(); _cmdAlertPending = false; }
            }
            ApplyCommandBarHotkey();
        }

        /// <summary>P14 hook, last line of ApplySettings: the chosen shortcut may have changed (Settings saved).</summary>
        private void CommandBarSettingsChanged()
        {
            if (_cmdHotkey != null) ApplyCommandBarHotkey();
        }

        private void ApplyCommandBarHotkey()
        {
            HotkeyResult r;
            try { r = _cmdHotkey.Apply(_cmdOn, CommandBarHotkeys.Parse(S.CommandBarKey)); }
            catch (Exception ex) { FeatureFlags.Current?.ReportError(CommandBarRules.FeatureId, ex); return; }
            string label = CommandBarHotkeys.Label(r.Key);
            switch (r.Change)
            {
                case HotkeyChange.Registered: App.Log("Command Bar: scurtătura " + label + " e activă."); _cmdAlertPending = false; break;
                case HotkeyChange.Unregistered: App.Log("Command Bar: scurtătura " + label + " a fost eliberată."); break;
                case HotkeyChange.Failed: App.Log("Command Bar: scurtătura " + label + " e folosită de altă aplicație."); break;
            }
            if (r.ShowAlert)
            {
                _cmdAlertKey = r.Key;
                _cmdAlertPending = true;
                _cmdAlertQueued = true;                    // after the current work (at startup: once the notch is laid out)
                Dispatcher.BeginInvoke(new Action(ShowCommandBarConflict), DispatcherPriority.Background);
            }
        }

        /// <summary>
        /// The one conflict alert: the shortcut is taken, the other one is proposed and Settings is named. Through the notch's
        /// Alert (the Activity Manager when it is on). Not shown now (notch open, fullscreen): tried again when the pill is
        /// back in standby (<see cref="CommandBarApplyMode"/>).
        /// </summary>
        private void ShowCommandBarConflict()
        {
            _cmdAlertQueued = false;
            if (!_cmdAlertPending || !_cmdOn || _cmdOpen || !IsLoaded) return;
            var key = _cmdAlertKey;
            var row = LiveRow(LiveIcon(Ui.GWarn, CWarn), CommandBarHotkeys.ConflictTitle(key), CommandBarHotkeys.ConflictHint(key), null);
            if (Alert(CommandBarRules.ConflictAlertId, row, 540, 54, 7000)) _cmdAlertPending = false;
        }

        // ------------------------------------------------------------------ the shortcut

        /// <summary>WM_HOTKEY (P14 hook in WndProc) and the smoke command "open-command-bar": the same path.</summary>
        private void OnCommandBarShortcut()
        {
            try
            {
                bool full = false;
                if (_cmdOn && !_cmdOpen) AlertInterrupt(Core.Ui.UserIntent.CommandBar);   // P51b hook (Features/AlertInterrupt)
                if (_cmdOn && !_cmdOpen)
                {
                    _cmdForeground ??= new Features.Context.ForegroundSource(work => work());
                    var context = Core.Context.ContextEngine.Current?.Snapshot?.Fullscreen ?? Core.Context.FullscreenKind.None;
                    full = CommandBarRules.IsFullscreen(_cmdForeground.Read(), context);
                }
                switch (CommandBarRules.OnShortcut(_cmdOn, _cmdOpen, full, _hidden, _toolBusy || Editing))
                {
                    case ShortcutDecision.Open: OpenCommandBar(); break;
                    case ShortcutDecision.Close: CloseCommandBar(); break;
                    default:
                        App.Log(!_cmdOn ? "Command Bar: comutatorul e oprit; nu se deschide."
                                : full || _hidden ? "Command Bar: nu se deschide peste o aplicație pe tot ecranul."
                                : "Command Bar: notch-ul e ocupat; nu se deschide.");
                        break;
                }
            }
            catch (Exception ex) { FeatureFlags.Current?.ReportError(CommandBarRules.FeatureId, ex); }
        }

        /// <summary>The same rule PollTick uses for "the last real app window" (not the notch, not the desktop or taskbar).</summary>
        private void RememberForegroundForCommandBar()
        {
            var fg = Native.GetForegroundWindow();
            if (fg != IntPtr.Zero && fg != _hwnd && !ShellClasses.Contains(Native.ClassName(fg))) LastForeground = fg;
        }

        private void OpenCommandBar()
        {
            if (_cmdOpen) return;
            RememberForegroundForCommandBar();            // where the keyboard goes back to
            if (_mode == Mode.Expanded) Collapse();        // the open panel makes room
            try { ActionRegistry.Current?.Refresh(); }     // workspaces, USB drives: read again
            catch (Exception ex) { FeatureFlags.Current?.ReportError(CommandBarRules.FeatureId, ex); }
            BuildCommandBar();
            _cmdSession = new CommandBarSession();
            _cmdOpen = true;
            // as in Expand: whatever the pill showed gives way; the notch counts as open for the alerts
            _dwellStart = null;
            Fade(Pill, 1, 120);
            EndLiveInteractive();
            _liveTimer.Stop();
            _mode = Mode.Expanded;
            _pinned = true;
            _mouseWasInside = false;
            _leaveStart = null;
            SetClickThrough(false);
            UpdateCommandResults();                        // the recent actions; lays the bar out (ApplyMode)
            ApplyHidden();
            EnableTyping(_cmdBox);                         // the launcher's way: the notch takes the keyboard, Collapse gives it back
            if (!IsActive) Native.ForceForeground(_hwnd);  // Windows refused Activate (no click, as with the launcher): the way OpenEditor brings a window up
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_cmdOpen && _cmdBox != null) { _cmdBox.Focus(); Keyboard.Focus(_cmdBox); }
            }), DispatcherPriority.Input);
            App.Log("Command Bar: deschis.");
            UpdateSmokeStatusIfOn();
        }

        /// <summary>Esc, Enter, a click outside, the shortcut again, the switch turned off.</summary>
        private void CloseCommandBar()
        {
            if (!_cmdOpen) return;
            if (_mode == Mode.Expanded) Collapse();        // StopTyping gives the keyboard back; held activities come in
            else TearDownCommandBar();
        }

        /// <summary>A click in another window: that window keeps the keyboard (it becomes LastForeground), the bar closes.</summary>
        private void OnCommandBarDeactivated(object sender, EventArgs e)
        {
            if (!_cmdOpen) return;
            RememberForegroundForCommandBar();
            CloseCommandBar();
        }

        /// <summary>
        /// P14 hook, first line of ApplyMode: while the bar is open it lays the pill out itself (true: ApplyMode stops
        /// there). Closed by Collapse (from anywhere) → its UI goes. A conflict alert that couldn't be shown is tried again in standby.
        /// </summary>
        private bool CommandBarApplyMode()
        {
            if (_cmdOpen && _mode != Mode.Expanded) TearDownCommandBar();
            if (_cmdAlertPending && !_cmdAlertQueued && _cmdOn && _mode == Mode.Idle && !_hidden)
            {
                _cmdAlertQueued = true;
                Dispatcher.BeginInvoke(new Action(ShowCommandBarConflict), DispatcherPriority.Background);
            }
            if (!_cmdOpen || _cmdLayer == null) return false;
            LayoutCommandBar();
            NotchGuardLaidOut();                           // B1 hook (Features/NotchGuard): the bar checked once it is laid out
            return true;
        }

        private void TearDownCommandBar()
        {
            bool was = _cmdOpen;
            _cmdOpen = false;
            _cmdSession = null;
            if (_cmdBox != null)
            {
                _cmdBox.PreviewKeyDown -= OnCommandBarKey;
                _cmdBox.TextChanged -= OnCommandBarText;
            }
            if (_cmdLayer != null)
            {
                _cmdLayer.BeginAnimation(OpacityProperty, null);
                _cmdLayer.Opacity = 0;
                _cmdLayer.Child = null;                    // nothing of it stays in the tree (or in UI Automation) while closed
                _cmdLayer.Visibility = Visibility.Collapsed;
            }
            _cmdBox = null;
            _cmdList = null;
            _cmdHint = null;
            _cmdMessage = null;
            _cmdShownH = _cmdShownScale = -1;
            if (was)
            {
                App.Log("Command Bar: închis.");
                UpdateSmokeStatusIfOn();
            }
        }

        // ------------------------------------------------------------------ the UI (theme brushes, radius 18)

        private void BuildCommandBar()
        {
            if (_cmdLayer == null)
            {
                _cmdScale = new ScaleTransform(1, 1);
                _cmdLayer = new Border
                {
                    Width = CommandBarLayout.Width, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
                    Opacity = 0, Visibility = Visibility.Collapsed, LayoutTransform = _cmdScale,
                };
                Panel.SetZIndex(_cmdLayer, 30);
                Inner.Children.Add(_cmdLayer);
            }

            _cmdBox = new TextBox
            {
                Style = Ui.S("DarkBox"), FontSize = 15, MaxLength = CommandBarSearch.MaxTextLength,
                VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(0),
            };
            _cmdBox.SetResourceReference(TextBox.SelectionBrushProperty, "AccentBrush");
            AutomationProperties.SetAutomationId(_cmdBox, SmokeMode.CommandBoxAutomationId);
            AutomationProperties.SetName(_cmdBox, "Command Bar");
            _cmdBox.PreviewKeyDown += OnCommandBarKey;
            _cmdBox.TextChanged += OnCommandBarText;

            _cmdHint = ThemedText("Scrie o comandă: „volum 30”, „captură”, „setări poziție”…", 14, "DimBrush");
            _cmdHint.IsHitTestVisible = false;
            _cmdHint.Margin = new Thickness(2, 0, 0, 0);
            var field = new Grid();
            field.Children.Add(_cmdHint);
            field.Children.Add(_cmdBox);

            var esc = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(7, 2, 7, 2), VerticalAlignment = VerticalAlignment.Center, Child = ThemedText("Esc", 11, "DimBrush") };
            esc.SetResourceReference(Border.BackgroundProperty, "ChipBrush");

            var head = Ui.Cols(Ui.Px(34), Ui.Star(), Ui.Auto);
            head.Height = CommandBarLayout.BoxHeight;
            head.Put(CommandGlyph(Ui.GSearch, 15, "MutedBrush"));
            head.Put(field, 1);
            head.Put(esc, 2);

            _cmdList = new StackPanel { Margin = new Thickness(0, 0, 0, CommandBarLayout.ListPadding), Visibility = Visibility.Collapsed };
            _cmdMessage = ThemedText("", 12, "MutedBrush");
            _cmdMessage.Height = CommandBarLayout.MessageHeight;
            _cmdMessage.Margin = new Thickness(8, 0, 8, 0);
            _cmdMessage.Visibility = Visibility.Collapsed;

            var root = new StackPanel { Margin = new Thickness(10, 0, 10, 0) };
            root.Children.Add(head);
            root.Children.Add(_cmdList);
            root.Children.Add(_cmdMessage);
            _cmdLayer.Child = root;
            _cmdLayer.Visibility = Visibility.Visible;
            _cmdShownH = _cmdShownScale = -1;
        }

        private static TextBlock CommandGlyph(string glyph, double size, string brush)
        {
            var t = new TextBlock { Text = glyph ?? "", FontSize = size, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            t.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
            t.SetResourceReference(TextBlock.ForegroundProperty, brush);
            return t;
        }

        /// <summary>The pill follows the rows (sizes from CommandBarLayout, scaled like the open notch).</summary>
        private void LayoutCommandBar()
        {
            double k = UiScale;
            int rows = _cmdSession?.Results.Count ?? 0;
            double h = CommandBarLayout.Height(rows, _cmdSession?.Message != null);
            _slimApplied = false;
            _miniApplied = false;
            if (Math.Abs(h - _cmdShownH) < 0.5 && Math.Abs(k - _cmdShownScale) < 0.001) return;
            bool first = _cmdShownH < 0;
            _cmdShownH = h;
            _cmdShownScale = k;
            _cmdScale.ScaleX = _cmdScale.ScaleY = k;
            var dur = TimeSpan.FromMilliseconds(first ? 420 : 200);
            Pill.BeginAnimation(WidthProperty, new DoubleAnimation(CommandBarLayout.Width * k, dur) { EasingFunction = Spring });
            Pill.BeginAnimation(HeightProperty, new DoubleAnimation(h * k, dur) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            // the radius follows the scale too (UiScale may change while the bar is open)
            BeginAnimation(RadiusProperty, new DoubleAnimation(CommandBarLayout.Radius * k, TimeSpan.FromMilliseconds(first ? 380 : 200)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            if (!first) return;
            Pill.BeginAnimation(MarginProperty, new ThicknessAnimation(new Thickness(0, 8, 0, 0), TimeSpan.FromMilliseconds(300)));
            FadeLayer(IdleLayer, false, 0);
            FadeLayer(MiniLayer, false, 0);
            FadeLayer(LiveLayer, false, 0);
            ExpLayer.BeginAnimation(OpacityProperty, null);
            ExpLayer.Opacity = 0;
            ExpLayer.Visibility = Visibility.Collapsed;
            _cmdLayer.Visibility = Visibility.Visible;
            Fade(_cmdLayer, 1, 180, 80);
        }

        private void OnCommandBarText(object sender, TextChangedEventArgs e) => UpdateCommandResults();

        /// <summary>The text changed: search again (the text is never logged).</summary>
        private void UpdateCommandResults()
        {
            if (_cmdSession == null) return;
            string text = _cmdBox?.Text ?? "";
            try
            {
                var reg = ActionRegistry.Current;
                _cmdSession.SetResults(text, reg == null ? Array.Empty<CommandItem>() : CommandBarSearch.Find(reg, text));
            }
            catch (Exception ex)
            {
                FeatureFlags.Current?.ReportError(CommandBarRules.FeatureId, ex);
                _cmdSession.SetResults(text, null);
            }
            DrawCommandResults();
        }

        private void DrawCommandResults()
        {
            if (_cmdList == null || _cmdSession == null) return;
            _cmdList.Children.Clear();
            var items = _cmdSession.Results;
            for (int i = 0; i < items.Count; i++) _cmdList.Children.Add(CommandRow(items[i], i, i == _cmdSession.Selected));
            _cmdList.Visibility = items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            string msg = _cmdSession.Message;
            _cmdMessage.Text = msg ?? "";
            _cmdMessage.Visibility = msg == null ? Visibility.Collapsed : Visibility.Visible;
            _cmdMessage.SetResourceReference(TextBlock.ForegroundProperty, msg == CommandBarSession.ConfirmMessage ? "WarnBrush" : "MutedBrush");
            _cmdHint.Visibility = string.IsNullOrEmpty(_cmdBox?.Text) ? Visibility.Visible : Visibility.Collapsed;
            ApplyMode();                                   // the pill follows the number of rows
        }

        private UIElement CommandRow(CommandItem item, int index, bool selected)
        {
            var a = item.Action;
            var g = Ui.Cols(Ui.Px(30), Ui.Star(), Ui.Auto);
            g.Put(CommandGlyph(string.IsNullOrEmpty(a.Icon) ? Ui.GSettings : a.Icon, 13, selected ? "AccentBrush" : "MutedBrush"));
            var title = ThemedText(a.Title, 13, "InkBrush", selected);
            title.TextTrimming = TextTrimming.CharacterEllipsis;     // a long title ends in "…" instead of being cut
            g.Put(title, 1);

            var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            string detail = item.Detail;
            if (detail.Length > 0)
            {
                var d = ThemedText(detail, 12.5, "AccentBrush", true);
                d.TextTrimming = TextTrimming.CharacterEllipsis;
                d.MaxWidth = 200;
                d.Margin = new Thickness(10, 0, 0, 0);
                right.Children.Add(d);
            }
            if (item.NeedsConfirm)
            {
                var tag = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(7, 1, 7, 1), Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Child = ThemedText("cere confirmare", 11, "WarnBrush") };
                tag.SetResourceReference(Border.BackgroundProperty, "ChipBrush");
                right.Children.Add(tag);
            }
            if (!string.IsNullOrEmpty(a.Category))
            {
                var c = ThemedText(a.Category, 11, "DimBrush");
                c.Margin = new Thickness(10, 0, 0, 0);
                right.Children.Add(c);
            }
            g.Put(right, 2);

            var row = new CommandResultRow
            {
                Height = CommandBarLayout.RowHeight - 2, Margin = new Thickness(0, 1, 0, 1), CornerRadius = new CornerRadius(10),
                Padding = new Thickness(6, 0, 10, 0), Child = g, Cursor = Cursors.Hand,
            };
            if (selected) row.SetResourceReference(Border.BackgroundProperty, "ChipHoverBrush");
            else row.Background = Brushes.Transparent;            // still takes clicks
            AutomationProperties.SetAutomationId(row, SmokeMode.CommandResultAutomationPrefix + a.Id);
            AutomationProperties.SetName(row, a.Title);
            AutomationProperties.SetItemStatus(row, selected ? "selected" : "");
            int i = index;
            row.MouseLeftButtonUp += (o, e) =>
            {
                e.Handled = true;
                if (_cmdSession != null && _cmdSession.Select(i)) CommandBarEnter();
            };
            return row;
        }

        // ------------------------------------------------------------------ keys and running the action

        private void OnCommandBarKey(object sender, KeyEventArgs e)
        {
            if (!_cmdOpen || _cmdSession == null) return;
            switch (e.Key)
            {
                case Key.Escape:
                    e.Handled = true;
                    CloseCommandBar();
                    break;
                case Key.Down:
                    e.Handled = true;
                    if (_cmdSession.Move(1)) DrawCommandResults();
                    break;
                case Key.Up:
                    e.Handled = true;
                    if (_cmdSession.Move(-1)) DrawCommandResults();
                    break;
                case Key.Enter:
                    e.Handled = true;
                    CommandBarEnter();
                    break;
                case Key.Tab:
                    e.Handled = true;                      // the keyboard stays in the box
                    break;
            }
        }

        /// <summary>Enter (or a click on a row): a value missing, a confirmation to ask, or the action.</summary>
        private void CommandBarEnter()
        {
            if (_cmdSession == null) return;
            var d = _cmdSession.Enter();
            if (d.Outcome != EnterOutcome.Invoke || d.Item == null) { DrawCommandResults(); return; }
            var item = d.Item;
            CloseCommandBar();                             // first: the keyboard goes back to your app (the action may act on it)
            _ = RunCommandAsync(item.Id, item.Args, d.Confirmed);
        }

        /// <summary>Always through the registry (its checks, its log without values); a failure is told in a short alert.</summary>
        private async Task RunCommandAsync(string id, IReadOnlyDictionary<string, string> args, bool confirmed)
        {
            try
            {
                var reg = ActionRegistry.Current;
                if (reg == null) return;
                var r = await reg.InvokeAsync(id, args, ActionInvoker.CommandBar, CancellationToken.None, confirmed);
                if (r != null && !r.Success) await Dispatcher.InvokeAsync(() => ShowCommandResult(r.Message));
            }
            catch (Exception ex) { FeatureFlags.Current?.ReportError(CommandBarRules.FeatureId, ex); }
        }

        private void ShowCommandResult(string message)
        {
            var row = LiveRow(LiveIcon(Ui.GWarn, CWarn), "Comanda nu a mers", string.IsNullOrEmpty(message) ? null : message, null);
            Alert(CommandBarRules.ResultAlertId, row, 440, 54, 3500);
        }
    }

    /// <summary>A Command Bar result row, visible to UI Automation as a list item (the smoke tests find it by its id).</summary>
    internal sealed class CommandResultRow : Border
    {
        protected override AutomationPeer OnCreateAutomationPeer() => new RowPeer(this);

        private sealed class RowPeer : FrameworkElementAutomationPeer
        {
            public RowPeer(CommandResultRow owner) : base(owner) { }
            protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.ListItem;
            protected override string GetClassNameCore() => nameof(CommandResultRow);
            protected override bool IsControlElementCore() => true;
        }
    }
}
