using System;
using System.Collections.Generic;
using System.Linq;

namespace WinNotch.Core.Ui
{
    /// <summary>What an open overlay is, which decides what closes what.</summary>
    public enum OverlayLevel
    {
        /// <summary>A panel the user opened on purpose (audio outputs, the shelf, the gallery, the sizes). Opening one closes the others.</summary>
        Panel,
        /// <summary>A hint that sits beside the content (quick actions, the note of a standard page). Not closed by a panel.</summary>
        Hint,
        /// <summary>Takes the keyboard while it is open (the Command Bar). Always on top.</summary>
        Modal,
    }

    /// <summary>Why an overlay was closed, for the log and for the caller's own cleanup.</summary>
    public enum OverlayClose { Button, OutsideClick, Escape, OtherPanel, NotchClosed, EditMode, Switch }

    /// <summary>
    /// P51: every panel closes the same way — its own "Închide" button, a click anywhere outside it, Esc, another panel
    /// of the same level opening, the notch closing, edit mode starting. This class only decides <em>what</em> closes:
    /// no WPF, no timers, no hit-testing (the caller says where a click landed). The window's side is in
    /// <c>Features/Overlays/NotchWindow.Overlays.cs</c>.
    /// </summary>
    public sealed class OverlayStack
    {
        /// <summary>Same id as the entry in FeatureCatalog (the tests check they match).</summary>
        public const string FeatureId = "overlay-dismiss";

        private sealed class Entry
        {
            public string Id;
            public OverlayLevel Level;
            public Action<OverlayClose> Close;
        }

        private readonly List<Entry> _open = new List<Entry>();
        private readonly HashSet<string> _closing = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Every open overlay, oldest first.</summary>
        public IReadOnlyList<string> Ids => _open.Select(e => e.Id).ToList();

        public int Count => _open.Count;

        /// <summary>The one a click outside or Esc closes first: the newest Modal, else the newest of all.</summary>
        public string Topmost
        {
            get
            {
                var modal = _open.LastOrDefault(e => e.Level == OverlayLevel.Modal);
                return (modal ?? _open.LastOrDefault())?.Id;
            }
        }

        /// <summary>True only while something is open: the keyboard (Esc) is read only then, never otherwise.</summary>
        public bool NeedsKeyboard => _open.Count > 0;

        public bool IsOpen(string id) => id != null && _open.Any(e => string.Equals(e.Id, id, StringComparison.Ordinal));

        public OverlayLevel? LevelOf(string id) =>
            _open.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.Ordinal))?.Level;

        /// <summary>
        /// An overlay has just opened. A <see cref="OverlayLevel.Panel"/> closes the other panels (not the hints);
        /// registering the same id twice replaces the entry (no duplicate). <paramref name="close"/> is the caller's own
        /// closing routine — it is called at most once, and calling <see cref="Close(string, OverlayClose)"/> from
        /// inside it does nothing.
        /// </summary>
        public void Register(string id, OverlayLevel level, Action<OverlayClose> close)
        {
            if (string.IsNullOrEmpty(id) || close == null) return;
            Close(id, OverlayClose.Button);
            if (level == OverlayLevel.Panel)
                foreach (var other in _open.Where(e => e.Level == OverlayLevel.Panel).Select(e => e.Id).ToList())
                    Close(other, OverlayClose.OtherPanel);
            _open.Add(new Entry { Id = id, Level = level, Close = close });
        }

        /// <summary>Closes one overlay. Unknown or already closing: nothing happens, nothing throws.</summary>
        public bool Close(string id, OverlayClose why)
        {
            if (string.IsNullOrEmpty(id) || _closing.Contains(id)) return false;
            int at = _open.FindIndex(e => string.Equals(e.Id, id, StringComparison.Ordinal));
            if (at < 0) return false;
            var entry = _open[at];
            _open.RemoveAt(at);
            _closing.Add(id);
            try { entry.Close(why); }
            finally { _closing.Remove(id); }
            return true;
        }

        /// <summary>Closes every overlay of a level, newest first.</summary>
        public int CloseAll(OverlayLevel level, OverlayClose why)
        {
            int n = 0;
            foreach (var id in _open.Where(e => e.Level == level).Select(e => e.Id).Reverse().ToList())
                if (Close(id, why)) n++;
            return n;
        }

        /// <summary>Closes everything, newest first (the notch closed, edit mode started, the switch went off).</summary>
        public int CloseAll(OverlayClose why)
        {
            int n = 0;
            foreach (var id in _open.Select(e => e.Id).Reverse().ToList())
                if (Close(id, why)) n++;
            return n;
        }

        /// <summary>
        /// A click landed somewhere. <paramref name="insideId"/> is the overlay it landed in, or null for anywhere else
        /// (the page, the pill, another window). Closes only the one on top, and nothing if the click was in it.
        /// Returns the id closed, or null.
        /// </summary>
        public string OnOutsideClick(string insideId)
        {
            var top = Topmost;
            if (top == null || string.Equals(top, insideId, StringComparison.Ordinal)) return null;
            return Close(top, OverlayClose.OutsideClick) ? top : null;
        }

        /// <summary>Esc: closes the one on top, one press at a time. Returns the id closed, or null if nothing was open.</summary>
        public string OnEscape()
        {
            var top = Topmost;
            return top != null && Close(top, OverlayClose.Escape) ? top : null;
        }
    }
}
