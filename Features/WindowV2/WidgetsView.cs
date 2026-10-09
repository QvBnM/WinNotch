using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using WinNotch.Widgets;

namespace WinNotch.Features.WindowV2
{
    /// <summary>
    /// P52, the Widgeturi tab: the whole catalogue, at full height — the same <see cref="Gallery"/> the notch and the
    /// workspace use, with its own categories down the left, so this tab needs no column of the window's own. What you
    /// pick here lands on the page the Workspace is on; when there is no page of yours yet, it says so instead of
    /// pretending.
    /// </summary>
    internal sealed class WidgetsView : Grid
    {
        private readonly Func<WorkspaceView> _workspace;
        private readonly Action<string> _say;
        private readonly StackPanel _head = new StackPanel();
        private readonly Border _host = new Border { Padding = new Thickness(0, LayoutRules.Gap, 0, 0) };
        private readonly Grid _overlay = new Grid();
        private Gallery _gallery;

        internal WidgetsView(Func<WorkspaceView> workspace, Action<string> say)
        {
            _workspace = workspace; _say = say;
            RowDefinitions.Add(new RowDefinition { Height = Ui.Auto });
            RowDefinitions.Add(new RowDefinition { Height = Ui.Star() });
            Margin = new Thickness(LayoutRules.Pad, LayoutRules.Pad, LayoutRules.Pad, LayoutRules.Pad);
            this.Put(_head);
            this.Put(_host, 0, 1);
            System.Windows.Controls.Panel.SetZIndex(_overlay, 50);
            SetRowSpan(_overlay, 2);
            Children.Add(_overlay);
        }

        internal void Open()
        {
            _head.Children.Clear();
            _overlay.Children.Clear();
            _head.Children.Add(Ui.T("Widgeturi", 20, "InkBrush", true));
            var page = _workspace()?.Page;
            var line = Ui.T(page != null
                    ? "Click pe un widget pentru toate mărimile lui; ce alegi se adaugă pe pagina „" + page.Page.Name + "”, din Workspace."
                    : "Fă-ți întâi o pagină în Workspace: widget-urile se adaugă pe o pagină de-a ta, nu pe cele standard.",
                13, "MutedBrush");
            line.TextWrapping = TextWrapping.Wrap;
            line.Margin = new Thickness(0, 4, 0, 0);
            _head.Children.Add(line);

            _gallery = new Gallery((type, size) =>
            {
                var target = _workspace()?.Page;
                if (target == null) { _say("Fă-ți o pagină în Workspace, apoi adaugă widget-uri pe ea."); return; }
                if (target.Add(type, size)) { _gallery?.Message(""); _say("Adăugat pe pagina „" + target.Page.Name + "”."); }
                else { _gallery?.Message("Pagina e plină: scoate sau micșorează un widget."); _say("Pagina e plină: scoate sau micșorează un widget."); }
            }, null)
            {
                Popup = fe => Gallery.ShowPopupIn(_overlay, fe, new Thickness(0), 780),
            };
            var box = new Border { CornerRadius = new CornerRadius(LayoutRules.CardRadius), Padding = new Thickness(14), Child = _gallery };
            box.SetResourceReference(Border.BackgroundProperty, "ChipBrush");
            _host.Child = box;
        }
    }
}
