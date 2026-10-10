using System;
using System.Windows;
using System.Windows.Input;
using WinNotch.Panes;

namespace WinNotch.Core.Ui
{
    /// <summary>
    /// Press and move: the widget is dragged out of wherever it is shown; press and release on the same spot: a click.
    /// Extracted out of the gallery so the widget library of the WinNotch window can offer the very same gesture
    /// without a second copy of it — the two draw their cards differently, but a widget is dragged the one way.
    /// </summary>
    internal static class WidgetDrag
    {
        /// <summary>How far the mouse must move before a press becomes a drag instead of a click.</summary>
        internal const double Threshold = 6;

        /// <summary>
        /// Binds the gesture to one card. <paramref name="data"/> is what lands on the page: the widget's type, or
        /// "type|W|H" when a size was picked. <paramref name="before"/> and <paramref name="after"/> run around the
        /// drag itself (the gallery hides its size pop-up while you drag).
        /// </summary>
        internal static void Bind(FrameworkElement el, string data, Action click, Action before = null, Action after = null)
        {
            Point? down = null;
            el.PreviewMouseLeftButtonDown += (o, e) => down = e.GetPosition(el);
            el.MouseLeave += (o, e) => down = null;
            el.PreviewMouseMove += (o, e) =>
            {
                if (down == null || e.LeftButton != MouseButtonState.Pressed) { down = null; return; }
                var d = e.GetPosition(el) - down.Value;
                if (Math.Abs(d.X) + Math.Abs(d.Y) < Threshold) return;
                down = null;
                before?.Invoke();
                try { DragDrop.DoDragDrop(el, new DataObject(WidgetPage.DragFormat, data), DragDropEffects.Copy); }
                catch (Exception ex) { App.Log("Tragerea unui widget: " + ex.Message); }
                after?.Invoke();
            };
            el.MouseLeftButtonUp += (o, e) =>
            {
                if (down == null) return;
                down = null;
                e.Handled = true;
                click?.Invoke();
            };
        }
    }
}
