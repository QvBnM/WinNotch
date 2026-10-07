using System;
using WinNotch.Core.Flags;
using WinNotch.Core.Ui;
using WinNotch.Features.Activity;
using WinNotch.Features.Shelf;
using WinNotch.Services;

namespace WinNotch
{
    /// <summary>
    /// P51b: an alert must never stand in the way of something the user is doing. The decisions are in
    /// <see cref="InterruptRules"/> (pure, tested); here we only notice the intention (files dragged over the pill,
    /// the shortcuts, a panel opening) and end the alert at once, through the existing <c>EndLive</c>. With the switch
    /// off nothing here runs: an alert holds the pill until its time is up, exactly as before.
    /// </summary>
    public partial class NotchWindow
    {
        private readonly InterruptMemory _aiMemory = new InterruptMemory();
        /// <summary>The same pure drag detector the shelf uses (a drag carried in from outside, not a click on the pill).</summary>
        private readonly ShelfDragHover _aiDrag = new ShelfDragHover();
        /// <summary>UI-thread copy of the switch (read on every tick).</summary>
        private bool _aiOn;
        private Action<string> _aiFlagHandler;
        /// <summary>The alert on the pill now, and the one about to be shown (promoted only once it really is on screen).</summary>
        private string _aiCurrentId, _aiPendingId;
        private DateTime _aiLastInterrupt = DateTime.MinValue;

        private static bool AlertInterruptEnabled() => FeatureFlags.Current?.IsEnabled(InterruptRules.FeatureId) ?? false;

        /// <summary>True while a drag carried in from outside is over the pill (one detector for the shelf and for here).</summary>
        internal bool AlertInterruptDragging => _aiOn && _aiDrag.CarriedIn;

        private void StartAlertInterrupt()
        {
            _aiOn = AlertInterruptEnabled();
            ShelfReadPrimaryButton();           // the primary button may be swapped, and the shelf may be off
            if (_aiFlagHandler != null) return;
            _aiFlagHandler = id =>
            {
                if (!string.Equals(id, InterruptRules.FeatureId, StringComparison.Ordinal)) return;
                Dispatcher.InvokeAsync(() =>
                {
                    _aiOn = AlertInterruptEnabled();
                    if (!_aiOn) { _aiMemory.Clear(); _aiDrag.Reset(); _aiCurrentId = _aiPendingId = null; }
                    else ShelfReadPrimaryButton();
                });
            };
            if (FeatureFlags.Current != null) FeatureFlags.Current.Changed += _aiFlagHandler;
        }

        private void StopAlertInterrupt()
        {
            if (_aiFlagHandler != null && FeatureFlags.Current != null) FeatureFlags.Current.Changed -= _aiFlagHandler;
            _aiFlagHandler = null;
            _aiOn = false;
            _aiMemory.Clear();
            _aiDrag.Reset();
            _aiCurrentId = _aiPendingId = null;
        }

        /// <summary>
        /// Hook at the top of <c>Alert</c>: an alert pushed aside a moment ago is not offered again right away, so it
        /// cannot interrupt the very action it stepped aside for. The id is only remembered as "about to be shown";
        /// the tick promotes it once the pill really carries it.
        /// </summary>
        private bool AlertInterruptAllows(string id)
        {
            if (!_aiOn) return true;
            if (_aiMemory.Suppressed(AlertInterruptKey(id), DateTime.Now)) return false;
            _aiPendingId = id;
            return true;
        }

        /// <summary>Alerts of one flow share a key (OCR, memory, the update steps): the memory works on the key.</summary>
        private static string AlertInterruptKey(string id) => id == null ? null : LegacyAlerts.Find(id)?.Key ?? id;

        /// <summary>
        /// Hook in PollTick: keeps the drag detector fed (on every tick, so a drag that started before the alert is
        /// still seen as carried in) and, while an alert is on the pill, ends it so the notch can open as a drop target.
        /// </summary>
        private void AlertInterruptPoll(bool inside)
        {
            try
            {
                if (_mode == Mode.Live) { if (_aiPendingId != null) { _aiCurrentId = _aiPendingId; _aiPendingId = null; } }
                else { _aiCurrentId = _aiPendingId = null; }
                if (!_aiOn) { _aiDrag.Reset(); return; }
                bool dragging = _aiDrag.Update(inside, Native.GetAsyncKeyState(_shPrimaryButton) < 0);
                if (dragging && _mode == Mode.Live) AlertInterrupt(UserIntent.FileDrag);
            }
            catch (Exception ex) { FeatureFlags.Current?.ReportError(InterruptRules.FeatureId, ex); }
        }

        /// <summary>
        /// True while the alert on the pill keeps the hover for itself (it has buttons the user is meant to press).
        /// With the switch off every alert keeps it, as before.
        /// </summary>
        private bool AlertInterruptHoverBlocked()
        {
            if (!_aiOn) return true;
            return !InterruptRules.Interrupts(UserIntent.Hover, AlertInterruptInteractive());
        }

        private bool AlertInterruptInteractive() =>
            (_aiCurrentId == null ? null : LegacyAlerts.Find(_aiCurrentId))?.Interactive ?? _liveInteractive;

        /// <summary>
        /// Ends the alert now if this intention wins. Returns true when an alert was pushed aside. Safe to call with no
        /// alert on screen, and quiet for a moment afterwards so a redrawn activity cannot make a loop.
        /// </summary>
        private bool AlertInterrupt(UserIntent intent)
        {
            if (!_aiOn || _mode != Mode.Live) return false;
            try
            {
                var now = DateTime.Now;
                if (now - _aiLastInterrupt < InterruptRules.Quiet && now >= _aiLastInterrupt) return false;
                if (!InterruptRules.Interrupts(intent, AlertInterruptInteractive())) return false;
                _aiLastInterrupt = now;
                string id = _aiCurrentId;
                if (id != null)
                {
                    _aiMemory.Note(AlertInterruptKey(id), now);
                    App.Log("Alertă întreruptă: " + id + " (" + AlertInterruptWhy(intent) + ").");
                }
                ActivityDismissShown();     // P13: the manager forgets it instead of drawing it again
                EndLive();                  // the existing routine, no exit animation: the action comes first
                return true;
            }
            catch (Exception ex) { FeatureFlags.Current?.ReportError(InterruptRules.FeatureId, ex); return false; }
        }

        private static string AlertInterruptWhy(UserIntent intent) => intent switch
        {
            UserIntent.FileDrag => "tragere de fișiere",
            UserIntent.Shortcut => "scurtătură",
            UserIntent.CommandBar => "Command Bar",
            UserIntent.OpenPanel => "panou deschis",
            UserIntent.Hover => "hover",
            _ => "acțiune",
        };
    }
}
