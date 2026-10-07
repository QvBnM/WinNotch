using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using WinNotch.Core.Actions;
using WinNotch.Core.Context;
using WinNotch.Core.Flags;
using WinNotch.Features.QuickActions;
using WinNotch.Features.Smoke;

namespace WinNotch
{
    /// <summary>
    /// The notch's side of Quick Actions (P20, ADR 0009). With the "quick-actions" switch off nothing here runs and nothing
    /// is subscribed. With it on: when the notch opens (hover or Win+Alt+N, on both Activity Manager paths) the rule for
    /// the context engine's snapshot gives 2–4 small buttons under the content (a row in OverlayHost, like the edit-mode
    /// note), each started only through <see cref="ActionRegistry.InvokeAsync"/> as <see cref="ActionInvoker.QuickAction"/>.
    /// With the Activity Manager on too, a rule that starts to match may be suggested unasked: a Low peek through the
    /// manager, at most one in 10 minutes, with „Nu mai arăta” (per rule, saved) in the row. The logic is in QuickActions.cs.
    /// </summary>
    public partial class NotchWindow
    {
        /// <summary>Room for the row under the page (unscaled px): the row (26) and a small gap.</summary>
        private const double QuickActionsRowHeight = 32;

        private Action<string> _qaFlagHandler;
        private EventHandler<ContextChangedEventArgs> _qaContextHandler;
        private ContextEngine _qaEngine;
        /// <summary>UI-thread copy of the switch.</summary>
        private bool _qaOn;
        private StackPanel _qaRow;
        /// <summary>The last click's result, at the end of the row, for <see cref="QuickActionsMessageTime"/>.</summary>
        private TextBlock _qaMessage;
        /// <summary>One-shot (stopped on its first tick, and when the row goes): hides the result. Nothing ticks otherwise.</summary>
        private DispatcherTimer _qaMessageTimer;
        private static readonly TimeSpan QuickActionsMessageTime = TimeSpan.FromSeconds(4);
        private const int QuickActionsMessageMax = 60;
        private readonly QuickActionSuggester _qaSuggester = new QuickActionSuggester();
        /// <summary>For the smoke status: buttons that ran with success, suggestions posted.</summary>
        private int _qaInvoked, _qaSuggested;

        private static bool QuickActionsEnabled() => FeatureFlags.Current?.IsEnabled(QuickActionRules.FeatureId) ?? false;

        /// <summary>Called once at startup (App.StartApp), after the actions and the context engine. UI thread.</summary>
        internal void StartQuickActions()
        {
            if (_qaFlagHandler != null) return;
            _qaFlagHandler = id =>
            {
                if (id == QuickActionRules.FeatureId) Dispatcher.InvokeAsync(ApplyQuickActionsSwitch);      // any thread → UI
            };
            if (FeatureFlags.Current != null) FeatureFlags.Current.Changed += _qaFlagHandler;
            ApplyQuickActionsSwitch();
        }

        private void StopQuickActions()
        {
            _qaMessageTimer?.Stop();
            if (_qaFlagHandler != null && FeatureFlags.Current != null) FeatureFlags.Current.Changed -= _qaFlagHandler;
            _qaFlagHandler = null;
            UnsubscribeQuickActionsContext();
            _qaOn = false;
        }

        /// <summary>The switch changed (or startup): read it again (two changes can arrive in any order).</summary>
        private void ApplyQuickActionsSwitch()
        {
            if (_qaFlagHandler == null) return;                 // stopped meanwhile
            bool on = QuickActionsEnabled();
            if (on == _qaOn) return;
            _qaOn = on;
            if (on) SubscribeQuickActionsContext();
            else
            {
                UnsubscribeQuickActionsContext();
                if (_qaRow != null) { RemoveQuickActionsRow(); RelayoutPanel(); }
            }
            App.Log("Quick Actions: " + (on ? "pornit." : "oprit."));
        }

        /// <summary>Only for the unasked suggestions; the row itself reads the snapshot when the notch opens.</summary>
        private void SubscribeQuickActionsContext()
        {
            if (_qaEngine != null) return;
            _qaEngine = ContextEngine.Current;
            if (_qaEngine == null) return;
            var fields = QuickActionRules.SuggestionFields();
            _qaContextHandler = (sender, e) =>
            {
                // a timer thread: only the fields a suggestion reads go on, to the UI thread
                if (e == null || (e.Fields & fields) == 0) return;
                if (e.Has(ContextField.UsbDrive)) QuickActionsReadDrives();
                Dispatcher.InvokeAsync(() => QuickActionsContextChanged(e));
            };
            _qaEngine.Changed += _qaContextHandler;
            _ = Task.Run(QuickActionsReadDrives);        // the drives' list read once off the UI thread, before the first open
        }

        /// <summary>
        /// A stick came or went (timer thread, R1): the registry's list of drive actions is read again here, off the UI thread
        /// (a sleeping stick can take seconds), so the row built on open uses the cached list and never reads the drives.
        /// </summary>
        private static void QuickActionsReadDrives()
        {
            try
            {
                var reg = ActionRegistry.Current;
                if (reg == null) return;
                reg.Refresh();
                _ = reg.All;
            }
            catch (Exception ex) { FeatureFlags.Current?.ReportError(QuickActionRules.FeatureId, ex); }
        }

        private void UnsubscribeQuickActionsContext()
        {
            if (_qaEngine != null && _qaContextHandler != null) _qaEngine.Changed -= _qaContextHandler;
            _qaEngine = null;
            _qaContextHandler = null;
        }

        private static IQuickActionCatalog QuickActionsCatalog()
        {
            var reg = ActionRegistry.Current;
            return reg == null ? null : new RegistryQuickActionCatalog(reg, FeatureFlags.Current);
        }

        // ------------------------------------------------------------------ the row (on open)

        /// <summary>
        /// Hook in Expand, before the panel is laid out (so the room for the row is there from the first frame). UI thread.
        /// The switch and the snapshot are read now; Empty (engine off, --safe-mode) matches nothing. Errors go to the switch.
        /// </summary>
        private void QuickActionsOnOpen()
        {
            try
            {
                RemoveQuickActionsRow();
                if (!_qaOn || !QuickActionsEnabled() || Editing) return;
                var snapshot = ContextEngine.Current?.Snapshot ?? ContextSnapshot.Empty;
                var reg = ActionRegistry.Current;
                if (reg == null) return;
                // no Refresh here: the drives' list is read again off the UI thread when the context says a stick came (R1)
                var choice = QuickActionRules.ForHover(true, snapshot, new RegistryQuickActionCatalog(reg, FeatureFlags.Current));
                if (choice == null) return;
                ShowQuickActionsRow(choice);
            }
            catch (Exception ex)
            {
                RemoveQuickActionsRow();
                FeatureFlags.Current?.ReportError(QuickActionRules.FeatureId, ex);
            }
        }

        /// <summary>Hook in Collapse: the row goes with the panel (nothing of it stays in the tree or in UI Automation).</summary>
        private void QuickActionsOnClose() => RemoveQuickActionsRow();

        /// <summary>Hook in UpdateHeader: edit mode has its own note at the bottom, the row makes room for it.</summary>
        private void QuickActionsHeaderChanged()
        {
            if (Editing && _qaRow != null) RemoveQuickActionsRow();
        }

        /// <summary>Hook in PanelH: the room the row takes under the page (0 without a row).</summary>
        private double QuickActionsExtraHeight() => _qaRow != null ? QuickActionsRowHeight : 0;

        private void ShowQuickActionsRow(QuickActionChoice choice)
        {
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom,
                Height = 26, Margin = new Thickness(0, 0, 0, -6),
            };
            AutomationProperties.SetAutomationId(row, SmokeMode.QuickActionsRowAutomationId);
            AutomationProperties.SetName(row, "Quick Actions");
            foreach (var item in choice.Items) row.Children.Add(QuickActionButton(item));
            var rule = choice.Rule;
            // „Nu mai arăta” only where a suggestion can come: the Activity Manager on, a rule that suggests, not hidden yet
            if (_activityOn && rule.Suggest && !(S.QuickActionsHidden?.Contains(rule.Id) ?? false)) row.Children.Add(QuickActionsHideButton(rule));
            _qaMessage = ThemedText("", 11, "DimBrush");
            _qaMessage.Margin = new Thickness(8, 0, 2, 0);
            _qaMessage.Visibility = Visibility.Collapsed;
            AutomationProperties.SetAutomationId(_qaMessage, SmokeMode.QuickActionMessageAutomationId);
            row.Children.Add(_qaMessage);
            _qaRow = row;
            OverlayHost.Children.Add(row);
            OverlayRegister(OvQuickActions, Core.Ui.OverlayLevel.Hint, row, () => { RemoveQuickActionsRow(); RelayoutPanel(); });   // P51 hook (Features/Overlays)
            PaneHost.Margin = new Thickness(0, 0, 0, QuickActionsRowHeight);      // the page keeps its own height above the row
            ExpLayer.Height = PanelH();
            UpdateSmokeStatusIfOn();
        }

        private void RemoveQuickActionsRow()
        {
            _qaMessageTimer?.Stop();
            _qaMessage = null;
            OverlayUnregister(OvQuickActions);   // P51 hook (Features/Overlays)
            if (_qaRow == null) return;
            OverlayHost.Children.Remove(_qaRow);
            _qaRow = null;
            PaneHost.Margin = new Thickness(0);
            ExpLayer.Height = PanelH();
        }

        private Button QuickActionButton(QuickActionItem item)
        {
            var icon = new TextBlock { Text = item.Icon ?? "", FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
            icon.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
            icon.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
            var text = ThemedText(item.Label, 11.5, "InkBrush");
            text.MaxWidth = 170;
            var b = new Button { Style = Ui.S("GhostPill"), Margin = new Thickness(3, 0, 3, 0), ToolTip = item.Title, Content = Ui.H(6, icon, text) };
            AutomationProperties.SetAutomationId(b, SmokeMode.QuickActionAutomationPrefix + item.ActionId);
            AutomationProperties.SetName(b, item.Label);
            string id = item.ActionId;
            var args = item.Args;
            b.Click += (o, e) =>
            {
                e.Handled = true;
                _ = RunQuickActionAsync(id, args);
            };
            return b;
        }

        private Button QuickActionsHideButton(QuickActionRule rule)
        {
            var b = new Button
            {
                Style = Ui.S("GhostPill"), Margin = new Thickness(8, 0, 3, 0), Content = ThemedText("Nu mai arăta", 11, "DimBrush"),
                ToolTip = "Fără sugestii nesolicitate pentru „" + rule.Title + "” (butoanele rămân la deschidere)",
            };
            AutomationProperties.SetAutomationId(b, SmokeMode.QuickActionHideAutomationPrefix + rule.Id);
            AutomationProperties.SetName(b, "Nu mai arăta");
            string ruleId = rule.Id;
            b.Click += (o, e) =>
            {
                e.Handled = true;
                try
                {
                    S.QuickActionsHidden = S.WithQuickActionHidden(ruleId);
                    S.Save();
                    _qaRow?.Children.Remove(b);
                    App.Log("Quick Actions: sugestiile pentru „" + ruleId + "” nu mai apar.");       // the rule id only
                }
                catch (Exception ex) { FeatureFlags.Current?.ReportError(QuickActionRules.FeatureId, ex); }
            };
            return b;
        }

        /// <summary>Always through the registry (its checks, its log without values), never ExecuteAsync. Safe actions only.</summary>
        private async Task RunQuickActionAsync(string id, IReadOnlyDictionary<string, string> args)
        {
            try
            {
                var reg = ActionRegistry.Current;
                if (reg == null) return;
                var r = await reg.InvokeAsync(id, args, ActionInvoker.QuickAction, CancellationToken.None);
                await Dispatcher.InvokeAsync(() =>
                {
                    if (r != null && r.Success) _qaInvoked++;
                    QuickActionsShowResult(r);
                    UpdateSmokeStatusIfOn();
                });
            }
            catch (Exception ex) { FeatureFlags.Current?.ReportError(QuickActionRules.FeatureId, ex); }
        }

        /// <summary>
        /// What the click did, in the row for a few seconds (R1: "Microfon oprit", "Unitatea nu mai e conectată."): the action's
        /// own short sentence, never logged. Gone with the row (notch closed) → nothing to show.
        /// </summary>
        private void QuickActionsShowResult(ActionResult r)
        {
            if (_qaRow == null || _qaMessage == null || r == null) return;
            string text = QuickActionRules.ShortMessage(r.Message, r.Success, QuickActionsMessageMax);
            _qaMessage.Text = text;
            _qaMessage.SetResourceReference(TextBlock.ForegroundProperty, r.Success ? "DimBrush" : "WarnBrush");
            _qaMessage.Visibility = Visibility.Visible;
            AutomationProperties.SetName(_qaMessage, text);
            if (_qaMessageTimer == null)
            {
                _qaMessageTimer = new DispatcherTimer { Interval = QuickActionsMessageTime };
                _qaMessageTimer.Tick += (o, e) =>
                {
                    _qaMessageTimer.Stop();                    // one shot
                    if (_qaMessage != null) _qaMessage.Visibility = Visibility.Collapsed;
                };
            }
            _qaMessageTimer.Stop();
            _qaMessageTimer.Start();
        }

        // ------------------------------------------------------------------ unasked suggestions (Activity Manager only)

        /// <summary>A relevant part of the context changed (UI thread): perhaps one suggestion, as a Low peek.</summary>
        private void QuickActionsContextChanged(ContextChangedEventArgs e)
        {
            try
            {
                if (!_qaOn || !QuickActionsEnabled()) return;
                bool manager = _activityOn && _activity != null;
                var catalog = QuickActionsCatalog();
                if (catalog == null) return;
                var outcome = _qaSuggester.Consider(true, manager, e.Old, e.New, e.Fields, catalog, S.QuickActionsHidden, out var choice);
                if (outcome == SuggestionOutcome.TooSoon) { App.Log("Quick Actions: sugestie amânată (" + choice.Rule.Id + "; cel mult una la 10 minute)."); return; }
                if (outcome != SuggestionOutcome.Suggest) return;
                var posted = _activity.Post(new Core.Activity.Activity
                {
                    Id = QuickActionRules.SuggestionActivityId, Priority = Core.Activity.ActivityPriority.Low,
                    Duration = Core.Activity.ActivityManager.PeekDuration, Title = QuickActionRules.SuggestionTitle(choice.Rule),
                    Glyph = QuickActionsActions.GQuick,
                });
                if (!QuickActionSuggestions.ConsumesInterval(posted)) return;     // not on screen (dropped, queued, grouped): the 10 minutes don't start
                _qaSuggester.Shown();
                _qaSuggested++;
                App.Log("Quick Actions: sugestie " + choice.Rule.Id + ".");     // the rule id only, never the context
                UpdateSmokeStatusIfOn();
            }
            catch (Exception ex) { FeatureFlags.Current?.ReportError(QuickActionRules.FeatureId, ex); }
        }
    }

    /// <summary>"quick-actions.show-hidden" in the app: the notch's settings, saved.</summary>
    internal sealed class NotchQuickActionsHost : IQuickActionsHost
    {
        private readonly NotchWindow _n;
        public NotchQuickActionsHost(NotchWindow notch) { _n = notch; }

        public int ShowHiddenAgain()
        {
            int n = _n.S.QuickActionsHidden?.Count ?? 0;
            _n.S.QuickActionsHidden = new List<string>();
            _n.S.Save();
            return n;
        }
    }
}
