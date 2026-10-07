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
    /// the shortcuts) and end the alert at once. With the switch off nothing here does anything.
    /// </summary>
    public partial class NotchWindow
    {
        private readonly InterruptMemory _aiMemory = new InterruptMemory();
        /// <summary>The same pure drag detector the shelf uses (a drag carried in from outside, not a click on the pill).</summary>
        private readonly ShelfDragHover _aiDrag = new ShelfDragHover();
        /// <summary>The alert on the pill now (for the anti-loop memory); null when none.</summary>
        private string _aiCurrentId;

        private static bool AlertInterruptOn => FeatureFlags.Current?.IsEnabled(InterruptRules.FeatureId) ?? false;

        /// <summary>
        /// Hook at the top of <c>Alert</c>: an alert pushed aside a moment ago is not offered again right away, so it
        /// cannot interrupt the very action it stepped aside for. Also remembers which alert is on the pill.
        /// </summary>
        private bool AlertInterruptAllows(string id)
        {
            if (!AlertInterruptOn) return true;
            if (_aiMemory.Suppressed(id, DateTime.Now)) return false;
            _aiCurrentId = id;
            return true;
        }

        /// <summary>Hook in EndLive: nothing is on the pill any more.</summary>
        private void AlertInterruptEnded() => _aiCurrentId = null;

        /// <summary>
        /// Hook in PollTick while an alert is on the pill: a drag carried onto the pill ends it immediately, so the
        /// notch can open as a drop target (the shelf). Hover is left to the usual dwell, which the rules allow only
        /// for alerts without buttons.
        /// </summary>
        private void AlertInterruptPoll(bool inside)
        {
            if (!AlertInterruptOn) { _aiDrag.Reset(); return; }
            // Fed on every tick, not only during an alert: a drag that started before the alert appeared is still
            // recognized as carried in from outside.
            bool dragging = _aiDrag.Update(inside, Native.GetAsyncKeyState(_shPrimaryButton) < 0);
            if (dragging && _mode == Mode.Live) AlertInterrupt(UserIntent.FileDrag);
        }

        /// <summary>
        /// Ends the alert now if this intention wins (the shortcuts, the tray, a panel opening, a drag). Returns true
        /// when an alert was pushed aside. Safe to call with no alert on screen.
        /// </summary>
        private bool AlertInterrupt(UserIntent intent)
        {
            if (!AlertInterruptOn || _mode != Mode.Live) return false;
            var alert = _aiCurrentId == null ? null : LegacyAlerts.Find(_aiCurrentId);
            bool interactive = alert?.Interactive ?? _liveInteractive;
            if (!InterruptRules.Interrupts(intent, interactive)) return false;
            string id = _aiCurrentId;
            _aiMemory.Note(id, DateTime.Now);
            App.Log("Alertă întreruptă: " + (id ?? "necunoscută") + " (" + AlertInterruptWhy(intent) + ").");
            EndLive();                  // no exit animation: the action comes first
            return true;
        }

        private static string AlertInterruptWhy(UserIntent intent) => intent switch
        {
            UserIntent.FileDrag => "tragere de fișiere",
            UserIntent.Shortcut => "scurtătură",
            UserIntent.CommandBar => "Command Bar",
            UserIntent.Tray => "meniul iconiței",
            UserIntent.OpenPanel => "panou deschis",
            UserIntent.Hover => "hover",
            _ => "acțiune",
        };
    }
}
