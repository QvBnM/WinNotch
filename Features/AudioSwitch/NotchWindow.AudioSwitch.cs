using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using WinNotch.Core.Actions;
using WinNotch.Core.Flags;
using WinNotch.Features.AudioSwitch;
using WinNotch.Features.Smoke;

namespace WinNotch
{
    /// <summary>
    /// The notch's side of "Căști/boxe" (P30, ADR 0012). With the "audio-switch" switch off nothing here runs: no button,
    /// no subscription, no list, no actions. With it on: a small button beside the volume on the Home page opens a short,
    /// rounded list of the active outputs over the page (the default one with a check); a click runs
    /// "audio.output-&lt;key&gt;" through <see cref="ActionRegistry.InvokeAsync"/>. The list follows Windows' device
    /// notifications (the service's debounce, no polling). The first failure of the undocumented change turns the switch
    /// off and says so in the pill (through Alert: the Activity Manager when it is on), after the notch closes if it is open.
    /// Only counters go to the log: never a device name.
    /// </summary>
    public partial class NotchWindow
    {
        private static readonly TimeSpan AudioSwitchMessageTime = TimeSpan.FromSeconds(5);

        private readonly AudioSwitchService _asService = new AudioSwitchService(new PolicyConfigSwitcher(), new AudioEndpointEvents(), () => FeatureFlags.Current, App.Log);
        private Action<string> _asFlagHandler;
        private Action _asChanged;
        private Action<string, string> _asFailed;
        /// <summary>UI-thread copy of the switch.</summary>
        private bool _asOn;
        private Button _asToggle;
        private Border _asPanel;
        private StackPanel _asList;
        private TextBlock _asMessage;
        private DispatcherTimer _asMessageTimer;
        private bool _asRunning;
        /// <summary>A failure to tell once the pill is free (the notch was open): title and hint.</summary>
        private (string Title, string Hint)? _asPendingFailure;

        /// <summary>For the "audio.output-*" actions (App.RegisterActions).</summary>
        internal AudioSwitchService AudioOutputs => _asService;

        private static bool AudioSwitchEnabled() => FeatureFlags.Current?.IsEnabled(AudioOutputRules.FeatureId) ?? false;

        /// <summary>Called once at startup (App.StartApp), after the actions. UI thread.</summary>
        internal void StartAudioSwitch()
        {
            if (_asFlagHandler != null) return;
            _asFlagHandler = id =>
            {
                if (id == AudioOutputRules.FeatureId) Dispatcher.InvokeAsync(ApplyAudioSwitch);      // any thread → UI
            };
            _asChanged = () => Dispatcher.InvokeAsync(AudioSwitchRedraw);
            _asFailed = (title, hint) => Dispatcher.InvokeAsync(() => AudioSwitchShowFailure(title, hint));
            _asService.Changed += _asChanged;
            _asService.Failed += _asFailed;
            if (FeatureFlags.Current != null) FeatureFlags.Current.Changed += _asFlagHandler;
            ApplyAudioSwitch();
        }

        private void StopAudioSwitch()
        {
            _asMessageTimer?.Stop();
            if (_asFlagHandler != null && FeatureFlags.Current != null) FeatureFlags.Current.Changed -= _asFlagHandler;
            if (_asChanged != null) _asService.Changed -= _asChanged;
            if (_asFailed != null) _asService.Failed -= _asFailed;
            _asFlagHandler = null; _asChanged = null; _asFailed = null;
            if (_asOn) AudioSwitchOff();
            _asOn = false;
            try { _asService.Stop(); } catch (Exception ex) { App.Log("Ieșire audio: oprirea a eșuat (" + ex.GetType().Name + ")."); }
        }

        /// <summary>The switch changed (or startup): read it again (two changes can arrive in any order).</summary>
        private void ApplyAudioSwitch()
        {
            if (_asFlagHandler == null) return;                // stopped meanwhile
            bool on = AudioSwitchEnabled();
            if (on == _asOn) return;
            _asOn = on;
            try
            {
                if (on)
                {
                    _asService.Start();                        // subscription and first read off the UI thread
                    AudioSwitchPlaceToggle();
                }
                else
                {
                    AudioSwitchOff();
                    _asService.Stop();
                }
            }
            catch (Exception ex) { FeatureFlags.Current?.ReportError(AudioOutputRules.FeatureId, ex); }
        }

        private void AudioSwitchOff()
        {
            // turned off by its own failure while the list is open: the list stays, emptied, with the reason, until the notch closes
            if (_asPanel != null && _asService.HasFailed)
            {
                _asList?.Children.Clear();
                AudioSwitchShowMessage(AudioOutputRules.FailureTitle + ". " + AudioOutputRules.FailureHint, false);
            }
            else AudioSwitchHidePanel();
            if (_asToggle?.Parent is Border slot) slot.Child = null;
            _asToggle = null;
        }

        // ------------------------------------------------------------------ hooks

        /// <summary>Hook in ShowPane: the list belongs to the Home page's volume; after a theme change the button moves to the new Home.</summary>
        private void AudioSwitchOnPaneChanged()
        {
            if (!_asOn && _asPanel == null) return;
            if (_pane != _home) AudioSwitchHidePanel();
            AudioSwitchPlaceToggle();
        }

        /// <summary>Hook in Collapse: the list goes with the panel; a failure that came while it was open is told now.</summary>
        private void AudioSwitchOnClose()
        {
            if (_asPanel != null) AudioSwitchHidePanel();
            if (_asPendingFailure is { } f)
                Dispatcher.BeginInvoke(new Action(() => AudioSwitchShowFailure(f.Title, f.Hint)), DispatcherPriority.Background);
        }

        /// <summary>Hook in UpdateHeader: edit mode has its own tools; the list steps aside.</summary>
        private void AudioSwitchHeaderChanged()
        {
            if (Editing && _asPanel != null) AudioSwitchHidePanel();
        }

        // ------------------------------------------------------------------ the button and the list

        private void AudioSwitchPlaceToggle()
        {
            if (!_asOn || _home == null) return;
            if (_asToggle == null)
            {
                var icon = new TextBlock { Text = Ui.GHeadphones, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
                icon.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
                icon.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
                _asToggle = new Button { Style = Ui.S("IconButton"), Width = 26, Height = 26, Margin = new Thickness(4, 0, 0, 0), Content = icon, ToolTip = "Ieșire audio: căști, boxe…" };
                AutomationProperties.SetAutomationId(_asToggle, SmokeMode.AudioOutputsToggleAutomationId);
                AutomationProperties.SetName(_asToggle, "Ieșire audio");
                _asToggle.Click += (o, e) =>
                {
                    e.Handled = true;
                    if (_asPanel != null) AudioSwitchHidePanel();
                    else AudioSwitchShowPanel();
                };
            }
            if (_home.OutputSlot.Child == _asToggle) return;
            if (_asToggle.Parent is Border old) old.Child = null;        // the Home page was rebuilt (theme)
            _home.OutputSlot.Child = _asToggle;
        }

        /// <summary>The click on the volume's button (and the smoke command): Home shown, the list over it.</summary>
        private void AudioSwitchShowPanel()
        {
            if (_asPanel != null || !_asOn || Editing || _mode != Mode.Expanded) return;
            var title = ThemedText("Ieșire audio", 13, "InkBrush", true);
            var close = new Button { Style = Ui.S("GhostPill"), Content = "Închide" };
            close.Click += (o, e) => { e.Handled = true; AudioSwitchHidePanel(); };
            var head = Ui.Cols(Ui.Star(), Ui.Auto);
            head.Put(title);
            head.Put(close, 1);

            _asMessage = ThemedText("", 11, "DimBrush");
            _asMessage.TextWrapping = TextWrapping.Wrap;
            _asMessage.Visibility = Visibility.Collapsed;
            _asMessage.Margin = new Thickness(2, 6, 2, 0);
            AutomationProperties.SetAutomationId(_asMessage, SmokeMode.AudioOutputsMessageAutomationId);
            _asList = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
            var scroll = new ScrollViewer { Style = Ui.S("SlimScroll"), Content = _asList, MaxHeight = 168 };
            var body = Ui.Rows(Ui.Auto, Ui.Auto, Ui.Auto);
            body.Put(head);
            body.Put(_asMessage, 0, 1);
            body.Put(scroll, 0, 2);

            _asPanel = new Border
            {
                Width = 300, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(8, 0, 0, 8), Padding = new Thickness(12, 10, 12, 8), CornerRadius = new CornerRadius(16),
                BorderThickness = new Thickness(1), Child = body,
            };
            _asPanel.SetResourceReference(Border.BackgroundProperty, "NotchBrush");
            _asPanel.SetResourceReference(Border.BorderBrushProperty, "TrackBrush");
            AutomationProperties.SetAutomationId(_asPanel, SmokeMode.AudioOutputsPanelAutomationId);
            OverlayHost.Children.Add(_asPanel);
            AudioSwitchRedraw();
            Task.Run(() => _asService.RefreshNow());           // fresh, off the UI thread; Changed redraws it
            App.Log("Ieșire audio: lista deschisă (" + _asService.Outputs.Count + ").");
        }

        private void AudioSwitchHidePanel()
        {
            _asMessageTimer?.Stop();
            if (_asPanel == null) return;
            OverlayHost.Children.Remove(_asPanel);
            _asPanel = null;
            _asList = null;
            _asMessage = null;
        }

        /// <summary>The list again (UI thread): the default one first marked with a check; "Nicio ieșire audio" when empty.</summary>
        private void AudioSwitchRedraw()
        {
            if (_asPanel == null || _asList == null) return;
            _asList.Children.Clear();
            if (!_asOn) return;                                // switched off: nothing to choose
            var outputs = _asService.Outputs;
            if (outputs.Count == 0)
            {
                var empty = ThemedText(AudioOutputRules.EmptyText, 12, "DimBrush");
                empty.Margin = new Thickness(2, 2, 2, 6);
                AutomationProperties.SetAutomationId(empty, SmokeMode.AudioOutputsEmptyAutomationId);
                AutomationProperties.SetName(empty, AudioOutputRules.EmptyText);
                _asList.Children.Add(empty);
                return;
            }
            foreach (var o in outputs) _asList.Children.Add(AudioSwitchRow(o));
        }

        private Button AudioSwitchRow(AudioOutput o)
        {
            var check = new TextBlock { Text = o.IsDefault ? "" : "", FontSize = 11, Width = 16, VerticalAlignment = VerticalAlignment.Center };
            check.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
            check.SetResourceReference(TextBlock.ForegroundProperty, "OkBrush");
            var name = ThemedText(o.Name, 12, o.IsDefault ? "InkBrush" : "MutedBrush", o.IsDefault);
            var row = Ui.Cols(Ui.Auto, Ui.Star());
            row.Put(check);
            row.Put(name, 1);
            var b = new Button { Style = Ui.S("RowButton"), HorizontalContentAlignment = HorizontalAlignment.Stretch, Content = row, ToolTip = o.IsDefault ? "Ieșirea audio de acum" : "Folosește această ieșire" };
            b.SetResourceReference(Control.FontFamilyProperty, "UiFont");
            AutomationProperties.SetAutomationId(b, SmokeMode.AudioOutputItemPrefix + o.Key);
            AutomationProperties.SetName(b, o.Name);
            AutomationProperties.SetItemStatus(b, o.IsDefault ? "default" : "");
            string id = o.ActionId;
            b.Click += (s, e) => { e.Handled = true; _ = AudioSwitchRunAsync(id); };
            return b;
        }

        /// <summary>A click on an output: through the registry (never ExecuteAsync), one at a time.</summary>
        private async Task AudioSwitchRunAsync(string actionId)
        {
            if (_asRunning) return;
            var reg = ActionRegistry.Current;
            if (reg == null) return;
            _asRunning = true;
            try
            {
                var r = await reg.InvokeAsync(actionId, null, ActionInvoker.UI);
                await Dispatcher.InvokeAsync(() => { if (r != null) AudioSwitchShowMessage(r.Message, r.Success); });
            }
            catch (Exception ex) { FeatureFlags.Current?.ReportError(AudioOutputRules.FeatureId, ex); }
            finally { await Dispatcher.InvokeAsync(() => _asRunning = false); }
        }

        /// <summary>A short sentence under the list's title for a few seconds (never logged).</summary>
        private void AudioSwitchShowMessage(string text, bool ok)
        {
            if (_asMessage == null || string.IsNullOrEmpty(text)) return;
            if (text.Length > 160) text = text.Substring(0, 159) + "…";
            _asMessage.Text = text;
            _asMessage.SetResourceReference(TextBlock.ForegroundProperty, ok ? "DimBrush" : "WarnBrush");
            _asMessage.Visibility = Visibility.Visible;
            AutomationProperties.SetName(_asMessage, text);
            if (_asMessageTimer == null)
            {
                _asMessageTimer = new DispatcherTimer { Interval = AudioSwitchMessageTime };
                _asMessageTimer.Tick += (o, e) =>
                {
                    _asMessageTimer.Stop();                    // one shot
                    if (_asMessage != null) _asMessage.Visibility = Visibility.Collapsed;
                };
            }
            _asMessageTimer.Stop();
            if (ok) _asMessageTimer.Start();                   // a failure stays until the list closes
        }

        /// <summary>
        /// The switch turned itself off (first failure): the pill says it through Alert (the Activity Manager when it is
        /// on); with the notch open, the list says it now and the pill once the notch closes.
        /// </summary>
        private void AudioSwitchShowFailure(string title, string hint)
        {
            if (_mode == Mode.Expanded)
            {
                _asPendingFailure = (title, hint);
                AudioSwitchShowMessage(title + ". " + hint, false);
                return;
            }
            _asPendingFailure = null;
            Alert(AudioSwitchFailureAlertId, LiveRow(LiveIcon(Ui.GWarn, CWarn), title, hint, null), 560, 58, 6000, true);
        }

        /// <summary>The Activity Manager's key for the failure alert.</summary>
        internal const string AudioSwitchFailureAlertId = "audio-switch-failed";

        /// <summary>Smoke command "smoke-audio-outputs": Home shown and the list opened, as the button does (the test then clicks the button itself).</summary>
        private void SmokeAudioOutputs()
        {
            if (!SmokeMode.On) return;
            if (!_asOn) { App.Log("Test de fum: ieșirea audio e oprită; comanda e ignorată."); return; }
            if (_mode != Mode.Expanded) { App.Log("Test de fum: notch-ul e închis; lista ieșirilor audio nu se deschide."); return; }
            ShowHome();
            AudioSwitchPlaceToggle();
            if (_asPanel == null) AudioSwitchShowPanel();
            App.Log("Test de fum: lista ieșirilor audio.");
        }
    }
}
