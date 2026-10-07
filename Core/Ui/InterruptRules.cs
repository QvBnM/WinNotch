using System;
using System.Collections.Generic;

namespace WinNotch.Core.Ui
{
    /// <summary>What the user is clearly trying to do while an alert is on the pill.</summary>
    public enum UserIntent
    {
        /// <summary>The mouse simply moved across the screen: not an intention.</summary>
        MouseMove,
        /// <summary>The mouse rested on the pill for the hover delay.</summary>
        Hover,
        /// <summary>Files are being dragged over the pill (a drag carried in from outside).</summary>
        FileDrag,
        /// <summary>Win+Alt+N, or „Deschide notch-ul” in the tray (the same path).</summary>
        Shortcut,
        /// <summary>The Command Bar shortcut.</summary>
        CommandBar,
        /// <summary>A panel was opened (audio outputs, the shelf).</summary>
        OpenPanel,
        /// <summary>Typing in another application.</summary>
        Typing,
        /// <summary>Another alert arrived (the activity queue decides those, not this rule).</summary>
        OtherAlert,
    }

    /// <summary>
    /// P51b: an alert is information, not a state. Any clear intention of the user wins and the alert steps aside at
    /// once. Pure rules (no WPF): which intention interrupts which alert, and the short memory that keeps an
    /// interrupted alert from coming straight back and interrupting the very action it stepped aside for.
    /// </summary>
    public static class InterruptRules
    {
        /// <summary>Same id as the entry in FeatureCatalog (the tests check they match).</summary>
        public const string FeatureId = "alert-interrupt";

        /// <summary>
        /// An interrupted alert is not offered again for this long: just over the gesture it stepped aside for, never so
        /// long that an alert the user asks for (the volume OSD, a new track) is swallowed.
        /// </summary>
        public static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(2);

        /// <summary>After one interruption, the next one waits this long: a persistent activity redrawn by the manager cannot make a loop.</summary>
        public static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(1500);

        /// <summary>
        /// Does this intention push the alert aside? Dragging files, the shortcuts and opening a panel always do.
        /// Hover does too, except for an alert the user is meant to press something on (update, memory full, eye break,
        /// confirmations): there hover keeps it, and only a click outside or one of the explicit intentions ends it.
        /// Moving the mouse, typing elsewhere and another alert never interrupt.
        /// </summary>
        public static bool Interrupts(UserIntent intent, bool interactive) => intent switch
        {
            UserIntent.FileDrag or UserIntent.Shortcut or UserIntent.CommandBar or UserIntent.OpenPanel => true,
            UserIntent.Hover => !interactive,
            _ => false,
        };
    }

    /// <summary>
    /// The alerts pushed aside a moment ago: the same one is not shown again right away (an alert interrupted by a drag
    /// must not come back on the next tick and interrupt the drag). Pure; the caller gives the clock, and a clock that
    /// jumps backwards (summer time) only forgets sooner, it never blocks an alert for an hour.
    /// </summary>
    public sealed class InterruptMemory
    {
        /// <summary>More kinds than this and the oldest is forgotten: it never grows.</summary>
        public const int MaxKinds = 12;

        private readonly List<string> _order = new List<string>();
        private readonly Dictionary<string, DateTime> _at = new Dictionary<string, DateTime>(StringComparer.Ordinal);

        public int Count => _order.Count;

        /// <summary>This alert (its flow key) was pushed aside now.</summary>
        public void Note(string key, DateTime now)
        {
            if (string.IsNullOrEmpty(key)) return;
            if (!_at.ContainsKey(key)) _order.Add(key);
            _at[key] = now;
            if (_order.Count > MaxKinds) { _at.Remove(_order[0]); _order.RemoveAt(0); }
        }

        /// <summary>True while the alert must not be shown again (within <see cref="InterruptRules.Cooldown"/>).</summary>
        public bool Suppressed(string key, DateTime now)
        {
            if (string.IsNullOrEmpty(key) || !_at.TryGetValue(key, out var at)) return false;
            var since = now - at;
            if (since >= TimeSpan.Zero && since < InterruptRules.Cooldown) return true;
            _at.Remove(key);
            _order.Remove(key);
            return false;
        }

        public void Clear() { _order.Clear(); _at.Clear(); }
    }
}
