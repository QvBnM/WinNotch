using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace WinNotch.Features.NotchGuard
{
    /// <summary>
    /// B1 (ADR 0013): the notch's safety net, without WPF (tested in tests/NotchGuardTests.cs). The notch side
    /// (NotchWindow.NotchGuard.cs) reads what is really on screen into a <see cref="NotchView"/>; <see cref="NotchContentRules"/>
    /// says what is wrong, <see cref="NotchRecovery"/> what to do, <see cref="NotchGuardLog"/> writes the "B1 recover" line.
    /// </summary>
    public static class NotchGuardInfo
    {
        /// <summary>Same id as FeatureCatalog.NotchGuard (the tests check they match).</summary>
        public const string FeatureId = "notch-guard";
        /// <summary>After each opening (Expand), the panel is checked this much later.</summary>
        public const int OpenCheckMs = 300;
        /// <summary>After the pill changes (standby, small form, alert, Command Bar): its fades are over by then (≤ 340 ms).</summary>
        public const int SettleCheckMs = 450;
        /// <summary>At most this many repairs of the pill in <see cref="BudgetWindow"/> (a stuck state must not fill the log).</summary>
        public const int PillBudget = 3;
        /// <summary>R1: at most this many repairs of the open notch in <see cref="BudgetWindow"/> (step 2 rebuilds every page).</summary>
        public const int PanelBudget = 4;
        public static readonly TimeSpan BudgetWindow = TimeSpan.FromMinutes(1);
    }

    /// <summary>What is wrong with what the notch shows (a set; None = the content is visible).</summary>
    [Flags]
    public enum NotchProblem
    {
        None = 0,
        /// <summary>The open notch: the whole pill is (nearly) transparent.</summary>
        PillTransparent = 1,
        /// <summary>The open notch: the panel's layer (tabs + page) is collapsed.</summary>
        PanelHidden = 2,
        /// <summary>The open notch: the panel's layer is there but (nearly) transparent.</summary>
        PanelTransparent = 4,
        /// <summary>The open notch: no tab in the tab bar.</summary>
        NoTabs = 8,
        /// <summary>The open notch: no page, or the page shown isn't the current one.</summary>
        NoPage = 16,
        /// <summary>The open notch: the current page itself is hidden.</summary>
        PageHidden = 32,
        /// <summary>The Command Bar counts as open but its layer isn't on screen.</summary>
        CommandBarHidden = 64,
        /// <summary>Standby with items to show, but its layer isn't on screen.</summary>
        IdleEmpty = 128,
        /// <summary>The small form, but its layer isn't on screen.</summary>
        MiniEmpty = 256,
        /// <summary>The small form shows the time without the date (empty or cut off).</summary>
        MiniWithoutDate = 512,
        /// <summary>An alert, but its layer is hidden or has nothing in it.</summary>
        LiveEmpty = 1024,
        /// <summary>A layer of another mode is on screen too (e.g. the standby items over the small form).</summary>
        WrongLayer = 2048,
    }

    /// <summary>One layer of the pill as it is on screen now.</summary>
    public readonly struct LayerState
    {
        public LayerState(bool visible, double opacity) { Visible = visible; Opacity = opacity; }
        /// <summary>Visibility == Visible.</summary>
        public bool Visible { get; }
        public double Opacity { get; }
        /// <summary>Really seen: visible and not (nearly) transparent.</summary>
        public bool Shown => Visible && Opacity >= NotchContentRules.MinOpacity;
        public static readonly LayerState Hidden = new LayerState(false, 0);
        public static readonly LayerState Full = new LayerState(true, 1);
    }

    /// <summary>The notch's mode, as NotchWindow names it.</summary>
    public enum NotchMode { Idle, Live, Expanded }

    /// <summary>What the notch shows right now (read on the UI thread; plain values, no WPF).</summary>
    public sealed class NotchView
    {
        public NotchMode Mode { get; init; }
        /// <summary>Standby in its small form ("ora · data").</summary>
        public bool Mini { get; init; }
        /// <summary>The Command Bar is open (Expanded without the panel).</summary>
        public bool CommandBar { get; init; }
        /// <summary>The Command Bar's layer is on screen with its content.</summary>
        public bool CommandBarShown { get; init; }
        public double PillOpacity { get; init; } = 1;
        public LayerState Panel { get; init; }
        public int Tabs { get; init; }
        /// <summary>A page is current and it is the one in the panel.</summary>
        public bool PageAttached { get; init; }
        /// <summary>The current page itself is visible.</summary>
        public bool PageShown { get; init; }
        public LayerState Idle { get; init; }
        /// <summary>How many standby items there are (0: an empty standby is allowed to be empty).</summary>
        public int IdleItems { get; init; }
        public LayerState Small { get; init; }
        public string SmallDate { get; init; } = "";
        /// <summary>The small form's row is wider than the pill (the date would be cut off).</summary>
        public bool SmallClipped { get; init; }
        public LayerState Live { get; init; }
        public bool LiveHasContent { get; init; }
    }

    /// <summary>Is the visible content of the notch empty (or wrong)? Pure.</summary>
    public static class NotchContentRules
    {
        /// <summary>Below this opacity a layer counts as not seen.</summary>
        public const double MinOpacity = 0.05;

        public static NotchProblem Check(NotchView v)
        {
            if (v == null) return NotchProblem.None;
            var p = NotchProblem.None;
            switch (v.Mode)
            {
                case NotchMode.Expanded when v.CommandBar:
                    if (!v.CommandBarShown) p |= NotchProblem.CommandBarHidden;
                    if (v.Idle.Shown || v.Small.Shown || v.Live.Shown || v.Panel.Shown) p |= NotchProblem.WrongLayer;
                    break;
                case NotchMode.Expanded:
                    if (v.PillOpacity < MinOpacity) p |= NotchProblem.PillTransparent;
                    if (!v.Panel.Visible) p |= NotchProblem.PanelHidden;
                    else if (v.Panel.Opacity < MinOpacity) p |= NotchProblem.PanelTransparent;
                    if (v.Tabs <= 0) p |= NotchProblem.NoTabs;
                    if (!v.PageAttached) p |= NotchProblem.NoPage;
                    else if (!v.PageShown) p |= NotchProblem.PageHidden;
                    if (v.Idle.Shown || v.Small.Shown || v.Live.Shown) p |= NotchProblem.WrongLayer;
                    break;
                case NotchMode.Live:
                    if (!v.Live.Shown || !v.LiveHasContent) p |= NotchProblem.LiveEmpty;
                    if (v.Idle.Shown || v.Small.Shown || v.Panel.Shown) p |= NotchProblem.WrongLayer;
                    break;
                default:
                    if (v.Mini)
                    {
                        if (!v.Small.Shown) p |= NotchProblem.MiniEmpty;
                        else if (string.IsNullOrWhiteSpace(v.SmallDate) || v.SmallClipped) p |= NotchProblem.MiniWithoutDate;
                        if (v.Idle.Shown) p |= NotchProblem.WrongLayer;
                    }
                    else
                    {
                        if (v.IdleItems > 0 && !v.Idle.Shown) p |= NotchProblem.IdleEmpty;
                        if (v.Small.Shown) p |= NotchProblem.WrongLayer;
                    }
                    if (v.Live.Shown || v.Panel.Shown) p |= NotchProblem.WrongLayer;
                    break;
            }
            return p;
        }

        /// <summary>
        /// R1: only "transparent" (the panel or the whole pill), the layer visible: at the first check its fade-in (120 ms
        /// delay + 220 ms) may still be running on a slow machine, so it gets one more check before anything is repaired.
        /// </summary>
        public static bool OnlyFading(NotchProblem p) =>
            p != NotchProblem.None && (p & ~(NotchProblem.PanelTransparent | NotchProblem.PillTransparent)) == NotchProblem.None;

        /// <summary>The problems by name, for the log ("PanelTransparent, NoTabs"; "—" for none).</summary>
        public static string Names(NotchProblem p)
        {
            if (p == NotchProblem.None) return "—";
            return string.Join(", ", Enum.GetValues(typeof(NotchProblem)).Cast<NotchProblem>().Where(f => f != NotchProblem.None && (p & f) == f).Select(f => f.ToString()));
        }
    }

    public enum RecoveryStep
    {
        /// <summary>Nothing wrong.</summary>
        None,
        /// <summary>The open notch, first try: the panel's layer shown again, the tabs and the current page rebuilt.</summary>
        RebuildPage,
        /// <summary>The open notch, second try: every page rebuilt and Acasă shown.</summary>
        OpenHome,
        /// <summary>The Command Bar can't be seen: closed (the pill goes back to standby).</summary>
        CloseCommandBar,
        /// <summary>The pill (standby, small form, alert): the right layer shown again, the others hidden, standby rebuilt.</summary>
        RepairPill,
        /// <summary>Tried everything this time: written in the log, tried again at the next opening.</summary>
        GiveUp,
    }

    /// <summary>What the safety net does for a problem, by attempt (0 = the first check after the change). Pure.</summary>
    public static class NotchRecovery
    {
        public static RecoveryStep Next(NotchView v, NotchProblem p, int attempt)
        {
            if (v == null || p == NotchProblem.None) return RecoveryStep.None;
            if (v.Mode == NotchMode.Expanded)
            {
                if (v.CommandBar) return attempt == 0 ? RecoveryStep.CloseCommandBar : RecoveryStep.GiveUp;
                return attempt switch { 0 => RecoveryStep.RebuildPage, 1 => RecoveryStep.OpenHome, _ => RecoveryStep.GiveUp };
            }
            return attempt == 0 ? RecoveryStep.RepairPill : RecoveryStep.GiveUp;
        }
    }

    /// <summary>At most N uses in a sliding window (the clock injected). Not thread-safe: UI thread only.</summary>
    public sealed class RecoveryBudget
    {
        private readonly int _max;
        private readonly TimeSpan _window;
        private readonly Func<DateTime> _now;
        private readonly Queue<DateTime> _used = new Queue<DateTime>();

        public RecoveryBudget(int max, TimeSpan window, Func<DateTime> now = null)
        {
            _max = Math.Max(1, max);
            _window = window;
            _now = now ?? (() => DateTime.UtcNow);
        }

        public bool TryTake()
        {
            var now = _now();
            while (_used.Count > 0 && now - _used.Peek() >= _window) _used.Dequeue();
            if (_used.Count >= _max) return false;
            _used.Enqueue(now);
            return true;
        }
    }

    /// <summary>
    /// B1 cause 1: a layer's fade-out ends with "collapse it if it is transparent" (Completed). A newer fade-in started in
    /// the meantime waits its delay (120–140 ms) at the opacity it found, so a fade-out started on an already hidden layer
    /// (0 → 0) and replaced 60–200 ms later still collapsed the layer that was being shown: the standby, the small form or
    /// an alert stayed empty. Now each fade takes a token and only the newest one may collapse. UI thread only.
    /// </summary>
    public sealed class FadeTokens
    {
        private sealed class Counter { public int Value; }
        private readonly ConditionalWeakTable<object, Counter> _map = new ConditionalWeakTable<object, Counter>();

        /// <summary>A new fade of <paramref name="layer"/> starts: every older one is stale from now on.</summary>
        public int Next(object layer) => layer == null ? 0 : ++_map.GetOrCreateValue(layer).Value;

        public bool IsLatest(object layer, int token) => layer != null && _map.TryGetValue(layer, out var c) && c.Value == token;

        /// <summary>What a fade-out's Completed may do: collapse only when it is still the newest fade and the layer is transparent.</summary>
        public static bool MayCollapse(bool latest, double opacity) => latest && opacity < 0.01;
    }

    /// <summary>The rules of the pill's small form ("ora · data") and its colors. Pure.</summary>
    public static class PillRules
    {
        /// <summary>Over a maximized window (not a busy, fullscreen one), if Settings allow it.</summary>
        public static bool OverMaximized(bool settingOn, bool maximized, bool busy) => settingOn && maximized && !busy;

        /// <summary>
        /// The small form: over a maximized window, or after <paramref name="miniAfterSec"/> seconds without activity on the
        /// notch (hover, an alert, closing it); 0 = never by time. Any activity brings the full standby back.
        /// </summary>
        public static bool IsMini(bool overMaximized, int miniAfterSec, DateTime lastActive, DateTime now) =>
            overMaximized || miniAfterSec > 0 && (now - lastActive).TotalSeconds >= miniAfterSec;

        /// <summary>The date in the small form: "mar 6 oct" (never empty: the time alone is a bug).</summary>
        public static string MiniDate(DateTime now, CultureInfo culture)
        {
            string d = now.ToString("ddd d MMM", culture ?? CultureInfo.InvariantCulture).Replace(".", "").Trim();
            return d.Length > 0 ? d : now.ToString("d MMM", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// The weather icon in standby, on the Home page and in the widget: the light themes take a theme color (the fixed
        /// light yellow / light lilac of the dark themes can't be seen on a light pill); the dark themes keep their colors (null).
        /// </summary>
        public static string WeatherBrushKey(bool lightTheme, bool ok, int code, bool isDay) =>
            !lightTheme ? null : !ok ? "MutedBrush" : code <= 1 && isDay ? "WarnBrush" : "InfoBrush";
    }

    /// <summary>The "B1 recover" line: fixed words, ids and counters only (no title, no page name, no path).</summary>
    public static class NotchGuardLog
    {
        public const string Prefix = "B1 recover: ";
        private static readonly Regex PlainId = new Regex("^[a-zăâîșț0-9]+(-[a-zăâîșț0-9]+)*$", RegexOptions.CultureInvariant);
        private static readonly string[] StandardPages = { "home", "system", "devices", "tools", "sources" };

        /// <summary>A standard page by its id; a page of yours only as "proprie" (its name is yours); none as "niciuna".</summary>
        public static string PageLabel(string id) =>
            string.IsNullOrEmpty(id) ? "niciuna" : StandardPages.Contains(id, StringComparer.Ordinal) ? id : "proprie";

        public static string StepLabel(RecoveryStep s) => s switch
        {
            RecoveryStep.RebuildPage => "pas 1: panoul și pagina curentă refăcute",
            RecoveryStep.OpenHome => "pas 2: toate paginile refăcute, Acasă deschisă",
            RecoveryStep.CloseCommandBar => "Command Bar închis (nu se vedea)",
            RecoveryStep.RepairPill => "pastila refăcută (stratul potrivit arătat, celelalte ascunse)",
            RecoveryStep.GiveUp => "nereușit; se încearcă din nou la următoarea deschidere",
            _ => "nimic de făcut",
        };

        /// <summary>
        /// "B1 recover: PanelTransparent, NoTabs · mod Expanded · pagina home · comutatoare activity-manager, shelf ·
        /// activitate Single/Normal · overlay-uri raft · pas 1: …". Unknown words (not plain ids) are left out.
        /// </summary>
        public static string Line(NotchView v, NotchProblem p, string pageId, IEnumerable<string> flagsOn, string activity, IEnumerable<string> overlays, RecoveryStep step)
        {
            string flags = string.Join(", ", (flagsOn ?? Array.Empty<string>()).Where(f => f != null && PlainId.IsMatch(f)).Distinct(StringComparer.Ordinal));
            string over = string.Join(", ", (overlays ?? Array.Empty<string>()).Where(o => o != null && PlainId.IsMatch(o)));
            string act = activity != null && Regex.IsMatch(activity, "^[A-Za-z]{1,20}(/[A-Za-z]{1,20})?$") ? activity : "niciuna";
            string mode = v == null ? "?" : v.Mode + (v.Mode == NotchMode.Idle && v.Mini ? " (mică)" : "") + (v.CommandBar ? " (Command Bar)" : "");
            return Prefix + NotchContentRules.Names(p) + " · mod " + mode + " · " + Layers(v) + " · pagina " + PageLabel(pageId) +
                   " · comutatoare " + (flags.Length > 0 ? flags : "—") + " · activitate " + act +
                   " · overlay-uri " + (over.Length > 0 ? over : "—") + " · " + StepLabel(step) + ".";
        }

        /// <summary>
        /// What the layers really were (for the diagnosis): the open notch "panou ascuns 0.00 · tab-uri 0"; the pill
        /// "standby 1.00 · mică ascuns · alertă ascuns" (the opacity of a visible layer, "ascuns" for a collapsed one).
        /// </summary>
        public static string Layers(NotchView v)
        {
            if (v == null) return "straturi ?";
            static string L(LayerState l) => l.Visible ? l.Opacity.ToString("0.00", CultureInfo.InvariantCulture) : "ascuns";
            if (v.Mode == NotchMode.Expanded && v.CommandBar) return "bara " + (v.CommandBarShown ? "vizibilă" : "invizibilă") + " · panou " + L(v.Panel);
            if (v.Mode == NotchMode.Expanded)
                return "panou " + L(v.Panel) + " · tab-uri " + Math.Max(0, v.Tabs) + " · pastila " + v.PillOpacity.ToString("0.00", CultureInfo.InvariantCulture);
            return "standby " + L(v.Idle) + " (" + Math.Max(0, v.IdleItems) + " elemente) · mică " + L(v.Small) + (v.Mini && string.IsNullOrWhiteSpace(v.SmallDate) ? " fără dată" : "") +
                   (v.SmallClipped ? " tăiată" : "") + " · alertă " + L(v.Live);
        }

        /// <summary>After a step: is the content visible now?</summary>
        public static string ResultLine(NotchProblem after, RecoveryStep step) =>
            Prefix + (after == NotchProblem.None ? "după „" + StepLabel(step) + "” conținutul se vede." : "după „" + StepLabel(step) + "” tot gol (" + NotchContentRules.Names(after) + ").");
    }
}
