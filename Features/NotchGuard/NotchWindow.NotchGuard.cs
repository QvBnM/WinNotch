using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using WinNotch.Core.Activity;
using WinNotch.Core.Flags;
using WinNotch.Features.NotchGuard;
using WinNotch.Features.Smoke;

namespace WinNotch
{
    /// <summary>
    /// B1, the notch's safety net (ADR 0013). After each opening (300 ms) and after each change of the pill (450 ms, when
    /// its fades are over) what is really on screen is read into a <see cref="NotchView"/> and checked by
    /// <see cref="NotchContentRules"/>. Empty open notch: the panel and the current page are rebuilt; still empty 300 ms
    /// later: every page is rebuilt and Acasă shown; still empty: written in the log and tried again at the next opening.
    /// Empty pill (standby, small form without the date, alert): the right layer shown again, at most 3 times a minute.
    /// Every repair writes one "B1 recover" line (page, switches on, current activity, overlays; never a title or a name).
    /// Nothing runs while nothing changes: two one-shot timers, started by the hooks in Expand and ApplyMode. The switch
    /// "notch-guard" (Stable, on) is read when a check runs; off = the hooks do nothing (the fade fix in FadeLayer stays).
    /// </summary>
    public partial class NotchWindow
    {
        /// <summary>B1 cause 1: only the newest fade of a layer may collapse it (FadeLayer, ApplyMode).</summary>
        private static readonly FadeTokens LayerFades = new FadeTokens();

        private DispatcherTimer _ngOpen, _ngSettle;
        /// <summary>Which step of the open notch's repair comes next (0: none tried since this opening).</summary>
        private int _ngAttempt;
        private RecoveryStep _ngLastStep;
        private bool _ngRepairing, _ngBudgetLogged, _ngWaited;
        /// <summary>Repairs done since start (the smoke status: ";b1r=").</summary>
        private int _ngRecoveries;
        private readonly RecoveryBudget _ngPillBudget = new RecoveryBudget(NotchGuardInfo.PillBudget, NotchGuardInfo.BudgetWindow);
        private readonly RecoveryBudget _ngPanelBudget = new RecoveryBudget(NotchGuardInfo.PanelBudget, NotchGuardInfo.BudgetWindow);

        private static bool NotchGuardEnabled() => FeatureFlags.Current?.IsEnabled(NotchGuardInfo.FeatureId) ?? false;

        /// <summary>Hook, last line of Expand: the panel is checked 300 ms after it opened.</summary>
        private void NotchGuardOpened()
        {
            if (_ngRepairing || !NotchGuardEnabled()) return;
            _ngAttempt = 0;
            _ngWaited = false;
            _ngSettle?.Stop();
            _ngOpen = NotchGuardRestart(_ngOpen, NotchGuardInfo.OpenCheckMs, NotchGuardOpenCheck);
        }

        /// <summary>Hook, last line of ApplyMode (and of the Command Bar's layout): the pill is checked when its fades are over.</summary>
        private void NotchGuardLaidOut()
        {
            if (_ngRepairing || !IsLoaded || (_mode == Mode.Expanded && !_cmdOpen)) return;    // a repair reads its own result; the open panel: checked after the opening
            if (!NotchGuardEnabled()) return;
            _ngSettle = NotchGuardRestart(_ngSettle, NotchGuardInfo.SettleCheckMs, NotchGuardSettleCheck);
        }

        private DispatcherTimer NotchGuardRestart(DispatcherTimer t, int ms, Action check)
        {
            if (t == null)
            {
                t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
                t.Tick += (o, e) => { ((DispatcherTimer)o).Stop(); check(); };       // one shot
            }
            t.Stop();
            t.Interval = TimeSpan.FromMilliseconds(ms);
            t.Start();
            return t;
        }

        private void StopNotchGuard()
        {
            _ngOpen?.Stop();
            _ngSettle?.Stop();
        }

        // ------------------------------------------------------------------ the checks

        /// <summary>300 ms after an opening (and after each step of the repair).</summary>
        private void NotchGuardOpenCheck()
        {
            if (!NotchGuardEnabled() || _toolHidden || _mode != Mode.Expanded || _cmdOpen) { _ngAttempt = 0; return; }
            try
            {
                var v = NotchViewNow();
                var p = NotchContentRules.Check(v);
                if (_ngAttempt > 0) App.Log(NotchGuardLog.ResultLine(p, _ngLastStep));
                if (p == NotchProblem.None) { _ngAttempt = 0; return; }
                if (_ngAttempt == 0 && !_ngWaited && NotchContentRules.OnlyFading(p))
                {
                    _ngWaited = true;                   // R1: a fade-in still running (slow machine): one more look before repairing
                    _ngOpen?.Start();
                    return;
                }
                if (_ngAttempt == 0 && !_ngPanelBudget.TryTake())
                {
                    if (!_ngBudgetLogged) App.Log(NotchGuardLog.Prefix + NotchContentRules.Names(p) + " · prea multe reparații ale panoului într-un minut; aștept.");
                    _ngBudgetLogged = true;
                    return;
                }
                var step = NotchRecovery.Next(v, p, _ngAttempt);
                App.Log(NotchGuardLog.Line(v, p, CurrentPageId(), NotchGuardFlagsOn(), NotchGuardActivity(), NotchGuardOverlays(), step));
                if (step == RecoveryStep.GiveUp || step == RecoveryStep.None) { _ngAttempt = 0; return; }
                _ngAttempt++;
                _ngLastStep = step;
                NotchGuardRun(step);
                _ngOpen?.Start();                       // the step's result, 300 ms later
            }
            catch (Exception ex) { _ngAttempt = 0; FeatureFlags.Current?.ReportError(NotchGuardInfo.FeatureId, ex); }
        }

        /// <summary>450 ms after the pill changed: standby, small form, alert, Command Bar.</summary>
        private void NotchGuardSettleCheck()
        {
            if (!NotchGuardEnabled() || _toolHidden || !IsLoaded || (_mode == Mode.Expanded && !_cmdOpen)) return;
            try
            {
                var v = NotchViewNow();
                var p = NotchContentRules.Check(v);
                if (p == NotchProblem.None) return;
                if (!_ngPillBudget.TryTake())
                {
                    if (!_ngBudgetLogged) App.Log(NotchGuardLog.Prefix + NotchContentRules.Names(p) + " · prea multe reparații ale pastilei într-un minut; aștept.");
                    _ngBudgetLogged = true;
                    return;
                }
                _ngBudgetLogged = false;
                var step = NotchRecovery.Next(v, p, 0);
                App.Log(NotchGuardLog.Line(v, p, CurrentPageId(), NotchGuardFlagsOn(), NotchGuardActivity(), NotchGuardOverlays(), step));
                NotchGuardRun(step);
                // the repaired pill has no animation left: read it again once the layout is done
                Dispatcher.InvokeAsync(() =>
                {
                    try { App.Log(NotchGuardLog.ResultLine(NotchContentRules.Check(NotchViewNow()), step)); }
                    catch (Exception ex) { FeatureFlags.Current?.ReportError(NotchGuardInfo.FeatureId, ex); }
                }, DispatcherPriority.Loaded);
            }
            catch (Exception ex) { FeatureFlags.Current?.ReportError(NotchGuardInfo.FeatureId, ex); }
        }

        // ------------------------------------------------------------------ the repairs

        private void NotchGuardRun(RecoveryStep step)
        {
            _ngRecoveries++;
            _ngRepairing = true;
            try
            {
                switch (step)
                {
                    case RecoveryStep.RebuildPage: NotchGuardRebuildPage(); break;
                    case RecoveryStep.OpenHome: NotchGuardOpenHome(); break;
                    case RecoveryStep.CloseCommandBar: CloseCommandBar(); break;
                    case RecoveryStep.RepairPill: NotchGuardRepairPill(); break;
                }
            }
            finally { _ngRepairing = false; }
        }

        /// <summary>Step 1: the whole pill and the panel's layer on screen, the current page put back from scratch, the tabs again.</summary>
        private void NotchGuardRebuildPage()
        {
            NotchGuardShowPanelLayer();
            var p = _pane ?? _home;
            _pane = null;                              // ShowPane puts it back: the panel's content, the tabs' check, Shown, Refresh, ApplyMode
            PaneHost.Content = null;
            p.Visibility = Visibility.Visible;
            ShowPane(p);
            RebuildTabs();
            RelayoutPanel();
        }

        /// <summary>Step 2: every page made again (as after a theme change), then Acasă.</summary>
        private void NotchGuardOpenHome()
        {
            RebuildUi();
            if (_pane != _home) ShowPane(_home);
            RebuildTabs();
            NotchGuardShowPanelLayer();
            RelayoutPanel();
        }

        private void NotchGuardShowPanelLayer()
        {
            Pill.BeginAnimation(OpacityProperty, null);
            Pill.Opacity = 1;
            NotchGuardShow(ExpLayer);
            NotchGuardHide(IdleLayer);
            NotchGuardHide(MiniLayer);
            NotchGuardHide(LiveLayer);
        }

        /// <summary>The pill: standby rebuilt, the layer of the current mode shown at once, the others hidden at once.</summary>
        private void NotchGuardRepairPill()
        {
            if (_mode == Mode.Live && LiveLayer.Content == null) { EndLive(); return; }      // nothing left to show: back to standby
            if (_mode == Mode.Idle)
            {
                _idleSig = null;
                BuildIdle();
                UpdateIdleValues();
            }
            ApplyMode();                               // sizes and the fades again (each takes a new token)
            UIElement want = _mode == Mode.Live ? LiveLayer : _mode == Mode.Idle ? (_miniApplied ? (UIElement)MiniLayer : IdleLayer) : null;
            foreach (var layer in new UIElement[] { IdleLayer, MiniLayer, LiveLayer })
            {
                if (layer == want) NotchGuardShow(layer);
                else NotchGuardHide(layer);
            }
            if (_mode != Mode.Expanded) NotchGuardHide(ExpLayer);
        }

        private static void NotchGuardShow(UIElement layer)
        {
            LayerFades.Next(layer);                    // a fade still running can't collapse it afterwards
            layer.BeginAnimation(OpacityProperty, null);
            layer.Opacity = 1;
            layer.Visibility = Visibility.Visible;
        }

        private static void NotchGuardHide(UIElement layer)
        {
            LayerFades.Next(layer);
            layer.BeginAnimation(OpacityProperty, null);
            layer.Opacity = 0;
            layer.Visibility = Visibility.Collapsed;
        }

        // ------------------------------------------------------------------ what is on screen

        private NotchView NotchViewNow() => new NotchView
        {
            Mode = _mode == Mode.Expanded ? NotchMode.Expanded : _mode == Mode.Live ? NotchMode.Live : NotchMode.Idle,
            Mini = _mode == Mode.Idle && _miniApplied,
            CommandBar = _cmdOpen,
            CommandBarShown = _cmdLayer != null && _cmdLayer.Child != null && NotchGuardLayer(_cmdLayer).Shown,
            PillOpacity = Pill.Opacity,
            Panel = NotchGuardLayer(ExpLayer),
            Tabs = TabBar.Children.Count,
            PageAttached = _pane != null && ReferenceEquals(PaneHost.Content, _pane),
            PageShown = _pane != null && _pane.Visibility == Visibility.Visible,
            Idle = NotchGuardLayer(IdleLayer),
            IdleItems = IdleLeft.Children.Count + IdleRight.Children.Count,
            Small = NotchGuardLayer(MiniLayer),
            SmallDate = MiniDate.Text ?? "",
            SmallClipped = MiniLayer.ActualWidth > 0 && MiniRow.ActualWidth > MiniLayer.ActualWidth + 2,
            Live = NotchGuardLayer(LiveLayer),
            LiveHasContent = LiveLayer.Content != null,
        };

        private static LayerState NotchGuardLayer(UIElement e) => new LayerState(e.Visibility == Visibility.Visible, e.Opacity);

        /// <summary>The ids of the switches that are on now (catalog ids only).</summary>
        private static List<string> NotchGuardFlagsOn()
        {
            var flags = FeatureFlags.Current;
            return flags == null ? new List<string>() : FeatureCatalog.All.Where(f => flags.IsEnabled(f.Id)).Select(f => f.Id).ToList();
        }

        /// <summary>What the Activity Manager shows (kind / priority), "CaleVeche" for an alert of the old path, null for none.</summary>
        private string NotchGuardActivity()
        {
            var v = _activityOn ? _activity?.View : null;
            if (v != null && v.Kind != ActivityViewKind.None) return v.Kind + (v.Primary != null ? "/" + v.Primary.Priority : "");
            return _mode == Mode.Live ? "CaleVeche" : null;
        }

        /// <summary>What lies over the page in the open notch, by fixed names.</summary>
        private List<string> NotchGuardOverlays()
        {
            var list = new List<string>();
            var known = new List<UIElement>();
            void Add(UIElement e, string name)
            {
                if (e == null) return;
                list.Add(name);
                known.Add(e);
            }
            Add(_gallery, "galerie");
            Add(_sizes, "mărimi");
            Add(_banner, "notă");
            Add(_shPanel, "raft");
            Add(_asPanel, "ieșire-audio");
            Add(_qaRow, "quick-actions");
            int others = OverlayHost.Children.Cast<UIElement>().Count(c => !known.Contains(c));
            if (others > 0) list.Add("alte-" + others);
            if (_cmdOpen) list.Add("command-bar");
            if (Editing) list.Add("editare");
            return list;
        }

        /// <summary>For the smoke status: what is wrong on screen now (";b1=") and how many repairs so far (";b1r=").</summary>
        private string NotchGuardSmokeStatus() => SmokeMode.NotchGuardStatus((int)NotchContentRules.Check(NotchViewNow()), _ngRecoveries);
    }
}
