using System;
using System.Collections.Generic;
using System.Linq;
using WinNotch.Core.Context;
using WinNotch.Core.Flags;
using WinNotch.Features.ContextPages;
using WinNotch.Panes;
using WinNotch.Widgets;

namespace WinNotch
{
    /// <summary>
    /// P27 "Pagina după context" (ADR 0008): when the notch opens, the page mapped in Settings to what you are doing now.
    /// Reads only <see cref="ContextEngine.Current"/>'s snapshot, once, at open time: no subscription, no timer, nothing
    /// while the notch is closed. A page picked by hand is kept for 10 minutes. The switch is read at that moment
    /// (nothing to start or stop), so turning "context-pages" off works from the next open.
    /// </summary>
    public partial class NotchWindow
    {
        private readonly ContextPageChooser _contextPages = new ContextPageChooser();

        /// <summary>
        /// Hook in Expand, before the mode changes (so the page is swapped without being shown twice). UI thread. A mapped
        /// page that is hidden or deleted is skipped silently; any error goes to the feature's switch, never further.
        /// </summary>
        private void ContextPagesOnOpen()
        {
            try
            {
                var flags = FeatureFlags.Current;
                if (flags == null || !flags.IsEnabled(ContextPageRules.FeatureId) || Editing) return;
                var snapshot = ContextEngine.Current?.Snapshot ?? ContextSnapshot.Empty;
                string id = _contextPages.OnOpen(true, snapshot, S.ContextPages, ContextPagesVisibleIds());
                if (id == null) return;
                var pane = ContextPagesPane(id);
                if (pane == null || pane == _pane) return;
                ShowPane(pane);
                App.Log("Pagina după context: " + ContextPageRules.EffectiveCategory(snapshot) + ".");        // the category only, never the app or its title
            }
            catch (Exception ex) { FeatureFlags.Current?.ReportError(ContextPageRules.FeatureId, ex); }
        }

        /// <summary>Hook in the tab bar: you picked a page yourself, so the context leaves it alone for 10 minutes.</summary>
        private void ContextPagesManualChoice() => _contextPages.ManualChoice();

        /// <summary>Id of the page shown now: "home" (also for the audio sources under it), "system", "devices", "tools" or yours.</summary>
        internal string CurrentPageId() => (_pane as WidgetPage)?.Page.Id ?? StandardId(_pane);

        /// <summary>The pages the notch shows now (standard ones not hidden, then yours not hidden), by id.</summary>
        private List<string> ContextPagesVisibleIds() =>
            Catalog.Standard.Where(p => !S.HiddenPages.Contains(p.Id)).Select(p => p.Id)
                   .Concat(S.Pages.Where(p => !p.Hidden).Select(p => p.Id)).ToList();

        private Pane ContextPagesPane(string id)
        {
            if (S.HiddenPages.Contains(id)) return null;
            var standard = StandardPane(id);
            if (standard != null) return standard;
            var page = S.Pages.FirstOrDefault(p => p.Id == id && !p.Hidden);
            return page != null ? UserPane(page) : null;
        }
    }
}
