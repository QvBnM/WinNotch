using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using WinNotch.Core.Actions;
using WinNotch.Core.Flags;
using WinNotch.Features.Smoke;

namespace WinNotch.Features.SmartClipboard
{
    /// <summary>
    /// The chips of the Clipboard widget (P21): for the text copied last, its kind and 1–3 small buttons ("Formatează",
    /// "Curăță link-ul", a colour swatch…). Collapsed (zero height: the widget looks exactly as before) with the switch
    /// off or for plain text. Redrawn only when the text or the switch changes (the widget's own refresh, about once a
    /// second while the page is visible: no timer of its own). A click starts the action only through
    /// <see cref="ActionRegistry.InvokeAsync"/>; its result shows briefly at the end of the row, never in the log.
    /// Theme brushes only; the swatch's colour is the copied colour itself (content, not theme).
    /// </summary>
    internal sealed class SmartClipChips : Border
    {
        private static readonly TimeSpan MessageTime = TimeSpan.FromSeconds(3);
        private const int MessageMax = 48;

        private readonly NotchWindow _w;
        private readonly StackPanel _row = new StackPanel { Orientation = Orientation.Horizontal };
        private TextBlock _message;
        /// <summary>One-shot (stopped on its first tick, on a redraw and when the widget goes): hides the result.</summary>
        private DispatcherTimer _messageTimer;
        private bool _drawnOn;
        private string _drawnFor;

        public SmartClipChips(NotchWindow w)
        {
            _w = w;
            Child = _row;
            ClipToBounds = true;
            Visibility = Visibility.Collapsed;
            Margin = new Thickness(0, 0, 0, 6);
            AutomationProperties.SetAutomationId(this, SmokeMode.SmartClipRowAutomationId);
            Unloaded += (o, e) => _messageTimer?.Stop();
        }

        /// <summary>From the widget's Refresh (UI thread). Cheap when nothing changed: two comparisons.</summary>
        public void Refresh()
        {
            try
            {
                bool on = _w.SmartClipboardOn;
                string text = on ? _w.SmartClipboardText : null;
                if (on == _drawnOn && ReferenceEquals(text, _drawnFor)) return;
                _drawnOn = on;
                _drawnFor = text;
                Draw(on && text != null ? _w.SmartClipboardCurrent() : SmartClip.None);
            }
            catch (Exception ex)
            {
                Clear();
                FeatureFlags.Current?.ReportError(SmartClipboardActions.FeatureId, ex);
            }
        }

        private void Clear()
        {
            _messageTimer?.Stop();
            _message = null;
            _row.Children.Clear();
            Visibility = Visibility.Collapsed;
        }

        private void Draw(SmartClip clip)
        {
            Clear();
            var chips = SmartClipboardActions.ChipsFor(clip);
            if (chips.Count == 0) return;
            var kind = Text(SmartClipboardActions.KindLabel(clip.Kind), 11, "DimBrush");
            kind.Margin = new Thickness(0, 0, 6, 0);
            _row.Children.Add(kind);
            foreach (var chip in chips) _row.Children.Add(ChipButton(chip, clip));
            _message = Text("", 11, "DimBrush");
            _message.Margin = new Thickness(4, 0, 0, 0);
            _message.Visibility = Visibility.Collapsed;
            AutomationProperties.SetAutomationId(_message, SmokeMode.SmartClipMessageAutomationId);
            _row.Children.Add(_message);
            Visibility = Visibility.Visible;
        }

        private Button ChipButton(SmartChip chip, SmartClip clip)
        {
            var label = Text(chip.Label, 11, "InkBrush");
            UIElement content = label;
            if (chip.Swatch)
            {
                var swatch = new Border
                {
                    Width = 10, Height = 10, CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(1),
                    Background = Ui.Rgb(clip.R, clip.G, clip.B, clip.A), VerticalAlignment = VerticalAlignment.Center,
                };
                swatch.SetResourceReference(Border.BorderBrushProperty, "TrackBrush");
                content = Ui.H(5, swatch, label);
            }
            var b = new Button { Style = Ui.S("GhostPill"), Margin = new Thickness(0, 0, 4, 0), Content = content };
            AutomationProperties.SetAutomationId(b, SmokeMode.SmartClipChipPrefix + chip.ActionId);
            AutomationProperties.SetName(b, chip.Label);
            string id = chip.ActionId;
            b.Click += (o, e) =>
            {
                e.Handled = true;
                _ = RunAsync(id);
            };
            return b;
        }

        /// <summary>Always through the registry (its checks, its log without content), never ExecuteAsync. Safe actions only.</summary>
        private async Task RunAsync(string id)
        {
            try
            {
                var reg = ActionRegistry.Current;
                if (reg == null) return;
                var r = await reg.InvokeAsync(id, null, ActionInvoker.UI);
                await Dispatcher.InvokeAsync(() => ShowResult(r));
            }
            catch (Exception ex) { FeatureFlags.Current?.ReportError(SmartClipboardActions.FeatureId, ex); }
        }

        /// <summary>The chips follow the new clipboard text first (a formatted JSON offers "Compactează"), then the result.</summary>
        private void ShowResult(ActionResult r)
        {
            Refresh();
            if (_message == null || r == null) return;
            string text = (r.Message ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
            if (text.Length == 0) text = r.Success ? "Gata" : "Nu a mers";
            if (text.Length > MessageMax) text = text.Substring(0, MessageMax - 1) + "…";
            _message.Text = text;
            _message.SetResourceReference(TextBlock.ForegroundProperty, r.Success ? "DimBrush" : "WarnBrush");
            _message.Visibility = Visibility.Visible;
            AutomationProperties.SetName(_message, text);
            if (_messageTimer == null)
            {
                _messageTimer = new DispatcherTimer { Interval = MessageTime };
                _messageTimer.Tick += (o, e) =>
                {
                    _messageTimer.Stop();                    // one shot
                    if (_message != null) _message.Visibility = Visibility.Collapsed;
                };
            }
            _messageTimer.Stop();
            _messageTimer.Start();
        }

        private static TextBlock Text(string text, double size, string brush)
        {
            var t = new TextBlock { Text = text ?? "", FontSize = size, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            t.SetResourceReference(TextBlock.ForegroundProperty, brush);
            return t;
        }
    }
}
