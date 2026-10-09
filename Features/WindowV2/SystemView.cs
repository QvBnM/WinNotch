using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WinNotch.Core.Actions;
using WinNotch.Core.Flags;
using WinNotch.Services;

namespace WinNotch.Features.WindowV2
{
    /// <summary>
    /// P52, the Sistem tab: everything that is not the workspace — what the app can do (cards built from the actions
    /// that really exist), the settings and the release notes. Its column on the left lists this tab's sections; it is
    /// not a second navigation of the window, it belongs to this tab alone.
    /// <para>The settings page and the notes are not rewritten here: they are the same content the classic window
    /// shows, hosted by <see cref="EmbeddedPages"/>.</para>
    /// </summary>
    internal sealed class SystemView : Grid
    {
        private readonly AppSettings _s;
        private readonly NotchWindow _notch;
        private readonly Func<Window> _owner;
        private readonly Action<string> _say;
        private readonly EmbeddedPages _pages = new EmbeddedPages();

        private readonly StackPanel _left = new StackPanel();
        private readonly StackPanel _body = new StackPanel();
        private readonly Border _leftHost;
        private readonly ScrollViewer _bodyScroll;
        private string _section = LayoutRules.Sections[0].Id;
        private int _cols = -1;
        private double _width = LayoutRules.MinWidth;      // the window's width, as Relayout last saw it

        internal SystemView(AppSettings s, NotchWindow notch, Func<Window> owner, Action<string> say)
        {
            _s = s; _notch = notch; _owner = owner; _say = say;
            ColumnDefinitions.Add(new ColumnDefinition { Width = Ui.Px(LayoutRules.LeftWidth) });
            ColumnDefinitions.Add(new ColumnDefinition { Width = Ui.Star() });

            _leftHost = new Border
            {
                Padding = new Thickness(LayoutRules.Gap, LayoutRules.Gap, LayoutRules.Gap, LayoutRules.Pad),
                Child = new ScrollViewer { Style = Ui.S("SlimScroll"), Content = _left },
            };
            _leftHost.SetResourceReference(Border.BackgroundProperty, "ChipBrush");
            this.Put(_leftHost);

            _bodyScroll = new ScrollViewer
            {
                Style = Ui.S("SlimScroll"), Content = _body,
                Padding = new Thickness(LayoutRules.Pad, LayoutRules.Pad, LayoutRules.Pad, LayoutRules.Pad),
            };
            this.Put(_bodyScroll, 1);
        }

        /// <summary>P14 ("settings.*" actions): the settings page, scrolled to one option and focused.</summary>
        internal void Reveal(string target) => _pages.Reveal(target);

        /// <summary>Lets go of a hosted page (the settings keep a timer of their own).</summary>
        internal void Detach() => _pages.Detach();

        internal void Open(string sectionId, double width)
        {
            _section = LayoutRules.FindSection(sectionId) != null ? sectionId : LayoutRules.Sections[0].Id;
            _width = width;
            BuildLeft();
            BuildBody(width);
        }

        internal void Relayout(double width)
        {
            _width = width;
            bool left = LayoutRules.ShowLeftPanel(LayoutRules.System, width);
            ColumnDefinitions[0].Width = left ? Ui.Px(LayoutRules.LeftWidth) : new GridLength(0);
            _leftHost.Visibility = left ? Visibility.Visible : Visibility.Collapsed;
            // a hosted page fills the height instead of growing; the cards only care about the number of columns
            if (!LayoutRules.IsPageSection(_section) && LayoutRules.Columns(width) != _cols) BuildBody(width);
        }

        private void BuildLeft()
        {
            _left.Children.Clear();
            _left.Children.Add(Ui.Cap("SISTEM"));
            foreach (var sec in LayoutRules.Sections)
            {
                var s = sec;
                bool on = string.Equals(sec.Id, _section, StringComparison.Ordinal);
                var mark = new Border { Width = 3, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 2, 6, 2) };
                if (on) mark.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
                var row = Ui.Cols(Ui.Px(3), Ui.Auto, Ui.Star());
                row.Put(mark);
                row.Put(Ui.Icon(sec.Glyph, 13, Ui.B(on ? "InkBrush" : "MutedBrush")), 1);
                var label = Ui.T(sec.Title, 13, on ? "InkBrush" : "MutedBrush", on);
                label.Margin = new Thickness(8, 0, 0, 0);
                label.VerticalAlignment = VerticalAlignment.Center;
                row.Put(label, 2);
                var host = new Button
                {
                    Style = Ui.S("NavButton"), Content = row, Padding = new Thickness(4, 7, 8, 7),
                    Cursor = Cursors.Hand, Margin = new Thickness(0, 1, 0, 1),
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                };
                System.Windows.Automation.AutomationProperties.SetName(host, sec.Title);
                if (on) host.SetResourceReference(Control.BackgroundProperty, "ChipHoverBrush");
                host.Click += (o, e) => { if (!on) { _section = s.Id; BuildLeft(); BuildBody(_width); } };
                _left.Children.Add(host);
            }
        }

        private void BuildBody(double width)
        {
            _pages.Detach();
            _body.Children.Clear();
            _bodyScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            var sec = LayoutRules.FindSection(_section);

            if (_section == "setari") { _body.Children.Add(_pages.Settings(_s, _notch, _bodyScroll, () => BuildBody(width))); return; }
            if (_section == "noutati") { _body.Children.Add(_pages.News()); return; }

            _body.Children.Add(Ui.T(sec?.Title ?? "Acțiuni", 20, "InkBrush", true));
            var line = Ui.T("Butoanele de aici pornesc acțiunile pe care aplicația le are chiar acum. Pornești funcții noi din Setări.", 13, "MutedBrush");
            line.TextWrapping = TextWrapping.Wrap;
            line.Margin = new Thickness(0, 4, 0, LayoutRules.Pad);
            _body.Children.Add(line);

            _cols = LayoutRules.Columns(width);
            var actions = VisibleActions()
                .Where(a => string.Equals(LayoutRules.SectionForAction(a.Category), _section, StringComparison.Ordinal))
                .ToList();
            if (actions.Count == 0)
            {
                _body.Children.Add(Ui.T("Nimic aici deocamdată: pornește funcțiile din Setări → funcții noi.", 12.5, "MutedBrush"));
                return;
            }
            var grid = new Grid();
            for (int i = 0; i < _cols; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = Ui.Star() });
            for (int i = 0; i < actions.Count; i++)
            {
                int row = i / _cols;
                if (grid.RowDefinitions.Count <= row) grid.RowDefinitions.Add(new RowDefinition { Height = Ui.Auto });
                var card = ActionCard(actions[i]);
                SetColumn(card, i % _cols);
                SetRow(card, row);
                grid.Children.Add(card);
            }
            _body.Children.Add(grid);
        }

        private static IReadOnlyList<ActionDescriptor> VisibleActions()
        {
            var reg = ActionRegistry.Current;
            if (reg == null) return Array.Empty<ActionDescriptor>();
            // every registered action, in the registry's own order; it refuses the ones that are off at invoke time
            try { return reg.All.Where(a => (a.AllowedInvokers & ActionInvoker.UI) == ActionInvoker.UI).ToList(); }
            catch { return Array.Empty<ActionDescriptor>(); }
        }

        private UIElement ActionCard(ActionDescriptor a)
        {
            var icon = new Border { Width = 38, Height = 38, CornerRadius = new CornerRadius(LayoutRules.ChipRadius), Child = Ui.Icon(string.IsNullOrEmpty(a.Icon) ? "" : a.Icon, 16) };
            icon.SetResourceReference(Border.BackgroundProperty, "TrackBrush");
            icon.HorizontalAlignment = HorizontalAlignment.Left;
            var title = Ui.T(a.Title, 13.5, "InkBrush", true);
            title.TextWrapping = TextWrapping.Wrap;
            var sub = Ui.T(a.Category ?? "", 12, "MutedBrush");
            sub.TextWrapping = TextWrapping.Wrap;
            var run = Ui.PillBtn(a.Safety == ActionSafety.Safe ? "Pornește" : "Pornește…", () => Run(a), true);
            run.HorizontalAlignment = HorizontalAlignment.Left;
            run.Margin = new Thickness(0, LayoutRules.Gap, 0, 0);
            var card = Ui.Card(Ui.V(8, icon, title, sub, run), 16, 14, LayoutRules.CardRadius);
            card.Margin = new Thickness(0, 0, LayoutRules.Gap, LayoutRules.Gap);
            return card;
        }

        private void Run(ActionDescriptor a)
        {
            // The registry does the checking: a Confirm/Dangerous action is asked about first, and only then confirmed.
            bool confirmed = a.Safety == ActionSafety.Safe ||
                             MessageBox.Show(_owner(), a.Title + "?", "WinNotch", MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK;
            if (!confirmed) return;
            var task = ActionRegistry.Current?.InvokeAsync(a.Id, null, ActionInvoker.UI, default, confirmed);
            if (task == null) return;
            // the registry says why it refused (a switch off, a missing parameter, nothing available): the user sees it
            _ = task.ContinueWith(t =>
            {
                string msg = t.IsCompletedSuccessfully ? t.Result?.Message : null;
                if (!string.IsNullOrEmpty(msg)) Dispatcher.InvokeAsync(() => _say(msg));
            }, System.Threading.Tasks.TaskScheduler.Default);
        }
    }
}
