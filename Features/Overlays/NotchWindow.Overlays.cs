using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using WinNotch.Core.Flags;
using WinNotch.Core.Ui;
using WinNotch.Services;

namespace WinNotch
{
    /// <summary>
    /// P51: the notch's side of <see cref="OverlayStack"/> — a panel closes through its own button, a click anywhere
    /// outside it, Esc, another panel opening, the notch closing or edit mode starting. The decisions are all in the
    /// stack (pure, tested); here we only say where a click landed and read Esc, and only while something is open.
    /// With the switch off nothing is registered and every panel behaves exactly as before.
    /// </summary>
    public partial class NotchWindow
    {
        private readonly OverlayStack _overlays = new OverlayStack();
        private readonly Dictionary<string, FrameworkElement> _overlayEls = new Dictionary<string, FrameworkElement>(StringComparer.Ordinal);
        private bool _overlayOn, _overlayHooked;
        private Action<string> _overlayFlagHandler;

        /// <summary>Ids: the same names the safety net uses for the overlays (NotchGuardOverlays), so nothing is reported as "alte-N".</summary>
        internal const string OvGallery = "galerie", OvSizes = "mărimi", OvNote = "notă", OvShelf = "raft",
                             OvAudio = "ieșire-audio", OvQuickActions = "quick-actions";

        private void StartOverlays()
        {
            _overlayOn = FeatureFlags.Current?.IsEnabled(OverlayStack.FeatureId) ?? false;
            if (_overlayFlagHandler == null)
            {
                _overlayFlagHandler = id =>
                {
                    if (!string.Equals(id, OverlayStack.FeatureId, StringComparison.Ordinal)) return;
                    Dispatcher.InvokeAsync(() =>
                    {
                        bool on = FeatureFlags.Current?.IsEnabled(OverlayStack.FeatureId) ?? false;
                        if (!on) { _overlays.CloseAll(OverlayClose.Switch); _overlayEls.Clear(); }
                        _overlayOn = on;
                        OverlayHook(on);
                    });
                };
                if (FeatureFlags.Current != null) FeatureFlags.Current.Changed += _overlayFlagHandler;
            }
            OverlayHook(_overlayOn);
        }

        private void StopOverlays()
        {
            if (_overlayFlagHandler != null && FeatureFlags.Current != null) FeatureFlags.Current.Changed -= _overlayFlagHandler;
            _overlayFlagHandler = null;
            OverlayHook(false);
            _overlays.CloseAll(OverlayClose.NotchClosed);
            _overlayEls.Clear();
        }

        /// <summary>Clicks inside the window reach WPF while the notch is open: one handler, only while the feature is on.</summary>
        private void OverlayHook(bool on)
        {
            if (on == _overlayHooked) return;
            _overlayHooked = on;
            if (on) AddHandler(PreviewMouseDownEvent, (MouseButtonEventHandler)OnOverlayPreviewMouseDown, true);
            else RemoveHandler(PreviewMouseDownEvent, (MouseButtonEventHandler)OnOverlayPreviewMouseDown);
        }

        /// <summary>An overlay has just been added to <c>OverlayHost</c>: it now closes like all the others.</summary>
        private void OverlayRegister(string id, OverlayLevel level, FrameworkElement element, Action close)
        {
            if (!_overlayOn || close == null) return;
            _overlays.Register(id, level, why =>
            {
                _overlayEls.Remove(id);
                try { close(); } catch (Exception ex) { FeatureFlags.Current?.ReportError(OverlayStack.FeatureId, ex); }
                if (why != OverlayClose.Button) App.Log("Panou închis: " + id + " (" + OverlayWhy(why) + ").");
            });
            _overlayEls[id] = element;
        }

        /// <summary>The caller closed the overlay itself (its button, a choice made, the page changed).</summary>
        private void OverlayUnregister(string id)
        {
            _overlayEls.Remove(id);
            _overlays.Close(id, OverlayClose.Button);
        }

        private void OverlayCloseAll(OverlayClose why)
        {
            if (!_overlayOn) return;
            _overlays.CloseAll(why);
        }

        private static string OverlayWhy(OverlayClose why) => why switch
        {
            OverlayClose.OutsideClick => "click în afară",
            OverlayClose.Escape => "Esc",
            OverlayClose.OtherPanel => "alt panou",
            OverlayClose.NotchClosed => "notch închis",
            OverlayClose.EditMode => "editare",
            OverlayClose.Switch => "comutator oprit",
            _ => "buton",
        };

        private void OnOverlayPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!_overlayOn || _overlays.Count == 0 || e.ChangedButton != MouseButton.Left) return;
            _overlays.OnOutsideClick(OverlayHitId(e));
        }

        /// <summary>Which open overlay the click landed in (null: the page, the tabs, the pill — anywhere else).</summary>
        private string OverlayHitId(MouseButtonEventArgs e)
        {
            foreach (var id in _overlays.Ids)
            {
                if (!_overlayEls.TryGetValue(id, out var el) || el == null || !el.IsVisible) continue;
                var p = e.GetPosition(el);
                if (p.X >= 0 && p.Y >= 0 && p.X <= el.ActualWidth && p.Y <= el.ActualHeight) return id;
            }
            return null;
        }

        /// <summary>
        /// Called from PollTick (30 ms, existing): a click outside the window never reaches WPF (the window is
        /// NOACTIVATE + TRANSPARENT), and Esc needs no focus. Both are read only while something is open.
        /// </summary>
        private void OverlayPollTick(Native.POINT cursor, Native.RECT pill)
        {
            // Nothing open (or the Command Bar has the keyboard): the flags are cleared, so the next press counts as new.
            if (!_overlayOn || _overlays.Count == 0 || CommandBarOpen)
            {
                _overlayEscDown = _overlayClickDown = false;
                return;
            }
            // Esc only while a panel is open: a hint alone never takes the key from the application the user types in.
            if (_overlays.NeedsEscape && Native.GetAsyncKeyState(0x1B) < 0)
            {
                if (!_overlayEscDown) { _overlayEscDown = true; _overlays.OnEscape(); }
                return;
            }
            _overlayEscDown = false;
            if (Native.GetAsyncKeyState(0x01) >= 0) { _overlayClickDown = false; return; }
            if (_overlayClickDown) return;                      // one press, one close
            _overlayClickDown = true;
            if (!Inside(pill, cursor, 8)) _overlays.OnOutsideClick(null);
        }

        private bool _overlayEscDown, _overlayClickDown;
    }
}
