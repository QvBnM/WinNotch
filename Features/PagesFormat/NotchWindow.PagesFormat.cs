using System;
using WinNotch.Core.Flags;
using WinNotch.Features.PagesFormat;
using WinNotch.Panes;

namespace WinNotch
{
    /// <summary>
    /// P75: the switch that decides which of the two forms a standard page has. The pages themselves live in
    /// <c>Features/PagesFormat/</c> and the old ones in <c>Panes/</c> are not touched at all — with the switch off the
    /// notch shows exactly what it showed before, and the new form is one page object away.
    /// </summary>
    public partial class NotchWindow
    {
        private HomePage _fmtHome;
        private SystemPage _fmtSystem;
        private DevicesPage _fmtDevices;
        private ToolsPage _fmtTools;
        private Action<string> _fmtFlagHandler;

        private static bool PageFormatEnabled() => FeatureFlags.Current?.IsEnabled(PageFormat.FeatureId) ?? false;

        private void StartPagesFormat()
        {
            if (_fmtFlagHandler != null) return;
            _fmtFlagHandler = id =>
            {
                if (!string.Equals(id, PageFormat.FeatureId, StringComparison.Ordinal)) return;
                // The pages are built from the form that was on at the time, so they are built again — on the UI
                // thread, and only once there is something to rebuild (at startup this arrives before the first page).
                Dispatcher.InvokeAsync(() =>
                {
                    try { ForgetFormatPanes(); if (_home != null) RebuildUi(); }
                    catch (Exception ex) { FeatureFlags.Current?.ReportError(PageFormat.FeatureId, ex); }
                });
            };
            if (FeatureFlags.Current != null) FeatureFlags.Current.Changed += _fmtFlagHandler;
        }

        private void StopPagesFormat()
        {
            if (_fmtFlagHandler != null && FeatureFlags.Current != null) FeatureFlags.Current.Changed -= _fmtFlagHandler;
            _fmtFlagHandler = null;
        }

        /// <summary>
        /// Runs a new page's own work with the feature's net under it. A page that throws must not take the notch with
        /// it: the error is counted, three of them in ten minutes switch the form off by itself, and the pages fall
        /// back to the ones that were there before. The constructor has the same net, in <see cref="FormatPane"/>.
        /// </summary>
        internal static void GuardFormat(Action body)
        {
            try { body(); }
            catch (Exception ex) { FeatureFlags.Current?.ReportError(PageFormat.FeatureId, ex); }
        }

        /// <summary>Drops the built pages, so the next one is built in whatever form is on now.</summary>
        internal void ForgetFormatPanes() { _fmtHome = null; _fmtSystem = null; _fmtDevices = null; _fmtTools = null; }

        /// <summary>
        /// The page in the new form, or null when the switch is off or that page has not been redrawn yet — the
        /// caller then falls back to the one it has always shown, so the two forms can live side by side while the
        /// four pages are done one at a time.
        /// </summary>
        private Pane FormatPane(string id)
        {
            if (!PageFormatEnabled()) return null;
            try
            {
                return id switch
                {
                    "home" => _fmtHome ??= new HomePage(this),
                    "system" => _fmtSystem ??= new SystemPage(this),
                    "devices" => _fmtDevices ??= new DevicesPage(this),
                    "tools" => _fmtTools ??= new ToolsPage(this),
                    _ => null,
                };
            }
            catch (Exception ex) { FeatureFlags.Current?.ReportError(PageFormat.FeatureId, ex); return null; }
        }
    }
}
