using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using WinNotch.Core.Flags;
using WinNotch.Features.NotchGuard;
using WinNotch.Features.Smoke;

namespace WinNotch
{
    public static partial class T
    {
        /// <summary>
        /// B1 (0.6.18, ADR 0013): the notch opened empty / the pill showed only the time. The causes found (each with a test
        /// that failed on 0.6.17), the rules of the small form, the safety net's decisions and its log line, the hooks.
        /// </summary>
        static void NotchGuardTests()
        {
            NotchGuardCauses();
            NotchGuardContentRules();
            NotchGuardPillRules();
            NotchGuardRecoveryAndLog();
            NotchGuardSourcePins();
        }

        // ------------------------------------------------------------------ the causes

        /// <summary>
        /// WPF's semantics, enough for the race: a fade-out (to 0, 200 ms) raises Completed at its end even when a newer
        /// animation replaced it; a fade-in with a delay keeps, during its delay, the opacity it found (the "sticky snapshot").
        /// </summary>
        sealed class FadeSim
        {
            public double Opacity;
            public bool Collapsed;
            private readonly List<(double At, Action Run)> _due = new List<(double, Action)>();
            private double _from, _to, _start, _delay, _dur;
            public double Now;

            public void Hide(Func<FadeSim, bool> mayCollapse)
            {
                Snapshot();
                Animate(0, 0, 200);
                _due.Add((Now + 200, () => { if (mayCollapse(this)) Collapsed = true; }));
            }

            public void Show(int delay) { Snapshot(); Collapsed = false; Animate(1, delay, 200); }

            private void Snapshot() => Opacity = ValueAt(Now);
            private void Animate(double to, double delay, double dur) { _from = Opacity; _to = to; _start = Now; _delay = delay; _dur = dur; }
            private double ValueAt(double t)
            {
                if (_dur <= 0) return Opacity;
                double p = (t - _start - _delay) / _dur;
                return p <= 0 ? _from : p >= 1 ? _to : _from + (_to - _from) * p;
            }

            public void RunTo(double t)
            {
                foreach (var d in _due.Where(x => x.At <= t).OrderBy(x => x.At).ToList())
                {
                    Now = d.At;
                    Opacity = ValueAt(Now);
                    d.Run();
                    _due.Remove(d);
                }
                Now = t;
                Opacity = ValueAt(t);
            }
        }

        static void NotchGuardCauses()
        {
            // cause 1: the layer is hidden; ApplyMode hides it again (0 → 0) and 100 ms later shows it (delay 120 ms)
            bool Race(bool withTokens)
            {
                var tokens = new FadeTokens();
                var layer = new object();
                var sim = new FadeSim { Opacity = 0, Collapsed = true };
                int hideToken = tokens.Next(layer);
                sim.Hide(s => withTokens ? FadeTokens.MayCollapse(tokens.IsLatest(layer, hideToken), s.Opacity) : s.Opacity < 0.01);   // 0.6.17: only the opacity
                sim.RunTo(100);
                tokens.Next(layer);
                sim.Show(120);
                sim.RunTo(1000);
                return !sim.Collapsed && sim.Opacity > 0.99;        // the layer is seen
            }
            Check("NG1", "Cauza 1 (strat gol): o ascundere înlocuită de o afișare cu întârziere nu mai colapsează stratul (pe 0.6.17 îl colapsa: pastila, forma mică sau alerta rămâneau goale)",
                  !Race(false) && Race(true));

            var t = new FadeTokens();
            object a = new object(), b = new object();
            int a1 = t.Next(a), b1 = t.Next(b), a2 = t.Next(a);
            Check("NG2", "Jetoanele de animație: doar cea mai nouă poate colapsa, per strat; un strat ascuns de ultima lui animație se colapsează în continuare",
                  !t.IsLatest(a, a1) && t.IsLatest(a, a2) && t.IsLatest(b, b1) && !FadeTokens.MayCollapse(false, 0) && FadeTokens.MayCollapse(true, 0) &&
                  !FadeTokens.MayCollapse(true, 0.5) && t.Next(null) == 0 && !t.IsLatest(null, 0) && !t.IsLatest(new object(), 1));

            string notch = Src("NotchWindow.xaml.cs"), notchN = Norm(NoComments(notch));
            string fade = Norm(NoComments(MethodBody(notch, "private static void FadeLayer(UIElement el, bool show, int delayMs)")));
            string applyMode = Norm(NoComments(MethodBody(notch, "private void ApplyMode()")));
            Check("NG3", "Cauza 1 în cod: FadeLayer ia un jeton și colapsează doar cu FadeTokens.MayCollapse(cel mai nou, opacitate); la fel panoul în ApplyMode; nicio regulă veche „doar opacitatea”",
                  fade != null && fade.StartsWith("{ int token = LayerFades.Next(el); if (show)", StringComparison.Ordinal) &&
                  fade.Contains("a.Completed += (o, e) => { if (Features.NotchGuard.FadeTokens.MayCollapse(LayerFades.IsLatest(el, token), el.Opacity)) el.Visibility = Visibility.Collapsed; };") &&
                  applyMode.Contains("int expFade = LayerFades.Next(ExpLayer);") && applyMode.Contains("if (_mode != Mode.Expanded && LayerFades.IsLatest(ExpLayer, expFade)) ExpLayer.Visibility = Visibility.Collapsed;") &&
                  !notchN.Contains("if (el.Opacity < 0.01)") && Count(Norm(NoComments(Src("Features/NotchGuard/NotchWindow.NotchGuard.cs"))), "private static readonly FadeTokens LayerFades = new FadeTokens();") == 1);

            // cause 2: the weather icon on a light theme
            Check("NG4", "Cauza 2 („doar ora”): pe tema luminoasă iconița vremii ia o culoare a temei (pe 0.6.17: galben/lila deschis fix, invizibile pe pastila deschisă); temele întunecate rămân la fel",
                  PillRules.WeatherBrushKey(false, true, 0, true) == null && PillRules.WeatherBrushKey(false, false, 3, false) == null &&
                  PillRules.WeatherBrushKey(true, true, 0, true) == "WarnBrush" && PillRules.WeatherBrushKey(true, true, 1, false) == "InfoBrush" &&
                  PillRules.WeatherBrushKey(true, true, 61, true) == "InfoBrush" && PillRules.WeatherBrushKey(true, false, 0, true) == "MutedBrush" &&
                  notchN.Contains("internal Brush WeatherBrush() => Features.NotchGuard.PillRules.WeatherBrushKey(_themeLight, Weather.Ok, Weather.Code, Weather.IsDay) is string key ? Ui.B(key) : Weather.Code <= 1 && Weather.IsDay ? Ui.Rgb(0xFF, 0xD2, 0x7A) : Ui.Rgb(0xCF, 0xD3, 0xFF);"));
        }

        // ------------------------------------------------------------------ what counts as empty

        static NotchView Panel(Func<NotchView, NotchView> change = null)
        {
            var v = new NotchView { Mode = NotchMode.Expanded, Panel = LayerState.Full, Tabs = 4, PageAttached = true, PageShown = true, PillOpacity = 1 };
            return change == null ? v : change(v);
        }

        static NotchView With(NotchView v, NotchMode? mode = null, bool? mini = null, bool? bar = null, bool? barShown = null, double? pill = null, LayerState? panel = null,
                              int? tabs = null, bool? attached = null, bool? pageShown = null, LayerState? idle = null, int? items = null, LayerState? small = null,
                              string date = null, bool? clipped = null, LayerState? live = null, bool? liveContent = null) => new NotchView
        {
            Mode = mode ?? v.Mode, Mini = mini ?? v.Mini, CommandBar = bar ?? v.CommandBar, CommandBarShown = barShown ?? v.CommandBarShown, PillOpacity = pill ?? v.PillOpacity,
            Panel = panel ?? v.Panel, Tabs = tabs ?? v.Tabs, PageAttached = attached ?? v.PageAttached, PageShown = pageShown ?? v.PageShown, Idle = idle ?? v.Idle,
            IdleItems = items ?? v.IdleItems, Small = small ?? v.Small, SmallDate = date ?? v.SmallDate, SmallClipped = clipped ?? v.SmallClipped, Live = live ?? v.Live,
            LiveHasContent = liveContent ?? v.LiveHasContent,
        };

        static void NotchGuardContentRules()
        {
            var ok = Panel();
            Check("NG5", "Notch deschis cu panoul, tab-urile și pagina vizibile: nimic de reparat",
                  NotchContentRules.Check(ok) == NotchProblem.None && NotchContentRules.Check(null) == NotchProblem.None);
            Check("NG6", "Notch deschis „gol” (ce a văzut autorul): panoul colapsat, transparent, fără tab-uri, fără pagină sau cu pagina ascunsă, pastila transparentă",
                  NotchContentRules.Check(With(ok, panel: LayerState.Hidden)) == NotchProblem.PanelHidden &&
                  NotchContentRules.Check(With(ok, panel: new LayerState(true, 0))) == NotchProblem.PanelTransparent &&
                  NotchContentRules.Check(With(ok, panel: new LayerState(true, 0.049))) == NotchProblem.PanelTransparent &&
                  NotchContentRules.Check(With(ok, panel: new LayerState(true, 0.05))) == NotchProblem.None &&
                  NotchContentRules.Check(With(ok, tabs: 0)) == NotchProblem.NoTabs &&
                  NotchContentRules.Check(With(ok, attached: false)) == NotchProblem.NoPage &&
                  NotchContentRules.Check(With(ok, pageShown: false)) == NotchProblem.PageHidden &&
                  NotchContentRules.Check(With(ok, pill: 0.01)) == NotchProblem.PillTransparent &&
                  NotchContentRules.Check(With(ok, panel: LayerState.Hidden, tabs: 0)) == (NotchProblem.PanelHidden | NotchProblem.NoTabs) &&
                  NotchContentRules.Check(With(ok, idle: LayerState.Full)) == NotchProblem.WrongLayer);
            var bar = With(ok, bar: true, barShown: true, panel: LayerState.Hidden, tabs: 4);
            Check("NG7", "Command Bar deschis: panoul ascuns e corect; bara care nu se vede (sau panoul peste ea) e o problemă",
                  NotchContentRules.Check(bar) == NotchProblem.None && NotchContentRules.Check(With(bar, barShown: false)) == NotchProblem.CommandBarHidden &&
                  NotchContentRules.Check(With(bar, panel: LayerState.Full)) == NotchProblem.WrongLayer);

            var idle = new NotchView { Mode = NotchMode.Idle, Idle = LayerState.Full, IdleItems = 2 };
            var small = new NotchView { Mode = NotchMode.Idle, Mini = true, Small = LayerState.Full, SmallDate = "mar 6 oct" };
            var live = new NotchView { Mode = NotchMode.Live, Live = LayerState.Full, LiveHasContent = true };
            Check("NG8", "Pastila: standby-ul, forma mică „ora · data” și alerta vizibile sunt în regulă; un standby fără elemente poate fi gol",
                  NotchContentRules.Check(idle) == NotchProblem.None && NotchContentRules.Check(small) == NotchProblem.None && NotchContentRules.Check(live) == NotchProblem.None &&
                  NotchContentRules.Check(With(idle, idle: LayerState.Hidden, items: 0)) == NotchProblem.None);
            Check("NG9", "Pastila goală: standby colapsat, formă mică fără strat, alertă ascunsă sau fără conținut, un strat din alt mod deasupra",
                  NotchContentRules.Check(With(idle, idle: LayerState.Hidden)) == NotchProblem.IdleEmpty &&
                  NotchContentRules.Check(With(idle, idle: new LayerState(true, 0))) == NotchProblem.IdleEmpty &&
                  NotchContentRules.Check(With(small, small: LayerState.Hidden)) == NotchProblem.MiniEmpty &&
                  NotchContentRules.Check(With(live, live: LayerState.Hidden)) == NotchProblem.LiveEmpty &&
                  NotchContentRules.Check(With(live, liveContent: false)) == NotchProblem.LiveEmpty &&
                  NotchContentRules.Check(With(idle, small: LayerState.Full)) == NotchProblem.WrongLayer &&
                  NotchContentRules.Check(With(small, idle: LayerState.Full)) == NotchProblem.WrongLayer &&
                  NotchContentRules.Check(With(live, panel: LayerState.Full)) == NotchProblem.WrongLayer);
            Check("NG10", "Ora singură în forma mică e o problemă: data goală sau tăiată de marginea pastilei",
                  NotchContentRules.Check(With(small, date: "")) == NotchProblem.MiniWithoutDate &&
                  NotchContentRules.Check(With(small, date: "  ")) == NotchProblem.MiniWithoutDate &&
                  NotchContentRules.Check(With(small, clipped: true)) == NotchProblem.MiniWithoutDate &&
                  NotchContentRules.Names(NotchProblem.MiniWithoutDate | NotchProblem.WrongLayer) == "MiniWithoutDate, WrongLayer" && NotchContentRules.Names(NotchProblem.None) == "—");
        }

        // ------------------------------------------------------------------ the small form ("ora · data")

        static void NotchGuardPillRules()
        {
            var now = new DateTime(2026, 10, 6, 12, 0, 0);
            Check("NG11", "Forma mică după inactivitatea setată: 9,9 s nu, 10 s da; „niciodată” (0) nu se strânge din timp",
                  !PillRules.IsMini(false, 10, now.AddSeconds(-9.9), now) && PillRules.IsMini(false, 10, now.AddSeconds(-10), now) &&
                  PillRules.IsMini(false, 5, now.AddSeconds(-6), now) && !PillRules.IsMini(false, 0, now.AddHours(-5), now) && !PillRules.IsMini(false, 60, now.AddSeconds(-59), now));
            Check("NG12", "Forma mică peste o fereastră maximizată (doar cu setarea pornită, nu peste un joc pe tot ecranul), oricât de recentă e activitatea",
                  PillRules.IsMini(PillRules.OverMaximized(true, true, false), 10, now, now) && !PillRules.OverMaximized(false, true, false) &&
                  !PillRules.OverMaximized(true, true, true) && !PillRules.OverMaximized(true, false, false) && PillRules.IsMini(true, 0, now, now));
            Check("NG13", "La activitate (hover, o alertă, închiderea notch-ului) revine standby-ul complet, dacă nu e o fereastră maximizată",
                  PillRules.IsMini(false, 10, now.AddMinutes(-3), now) && !PillRules.IsMini(false, 10, now, now) && !PillRules.IsMini(false, 10, now.AddSeconds(-1), now));
            var ro = new CultureInfo("ro-RO");
            var bad = new List<string>();
            for (int d = 0; d < 366; d++)
            {
                var day = new DateTime(2028, 1, 1, 9, 30, 0).AddDays(d);
                string text = PillRules.MiniDate(day, ro), old = day.ToString("ddd d MMM", ro).Replace(".", "");
                if (text.Length == 0 || text.Contains('.') || !text.Contains(day.Day.ToString(CultureInfo.InvariantCulture)) || text != old.Trim()) bad.Add(day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ": " + text);
            }
            Check("NG14", "Data din forma mică („ora · data”) nu e niciodată goală, în fiecare zi a unui an bisect, la fel ca înainte („mar 6 oct”)",
                  bad.Count == 0 && PillRules.MiniDate(now, null).Length > 0, string.Join("; ", bad.Take(3)));
            string notchN = Norm(NoComments(Src("NotchWindow.xaml.cs")));
            Check("NG15", "Notch-ul folosește regulile: MiniNow, fereastra maximizată din MonitorTick, data din forma mică și elementul „Data” din standby",
                  notchN.Contains("private bool MiniNow() => Features.NotchGuard.PillRules.IsMini(_slim, S.MiniAfterSec, _lastActive, DateTime.Now);") &&
                  notchN.Contains("bool slim = Features.NotchGuard.PillRules.OverMaximized(S.SlimOverMaximized, t.Maximized, t.Busy);") &&
                  notchN.Contains("MiniDate.Text = Features.NotchGuard.PillRules.MiniDate(now, Ro);") &&
                  notchN.Contains("SetIdle(\"date\", Features.NotchGuard.PillRules.MiniDate(now, Ro));") && !notchN.Contains("ToString(\"ddd d MMM\""));
        }

        // ------------------------------------------------------------------ what the safety net does, and its log

        static void NotchGuardRecoveryAndLog()
        {
            var empty = With(Panel(), panel: LayerState.Hidden, tabs: 0);
            var bar = With(Panel(), bar: true, barShown: false, panel: LayerState.Hidden);
            var pill = new NotchView { Mode = NotchMode.Idle, Idle = LayerState.Hidden, IdleItems = 2 };
            Check("NG16", "Plasa de siguranță: notch-ul gol → întâi pagina curentă refăcută, apoi Acasă, apoi renunță (până la următoarea deschidere); bara invizibilă → închisă; pastila → refăcută o dată",
                  NotchRecovery.Next(empty, NotchContentRules.Check(empty), 0) == RecoveryStep.RebuildPage &&
                  NotchRecovery.Next(empty, NotchContentRules.Check(empty), 1) == RecoveryStep.OpenHome &&
                  NotchRecovery.Next(empty, NotchContentRules.Check(empty), 2) == RecoveryStep.GiveUp &&
                  NotchRecovery.Next(bar, NotchContentRules.Check(bar), 0) == RecoveryStep.CloseCommandBar && NotchRecovery.Next(bar, NotchProblem.CommandBarHidden, 1) == RecoveryStep.GiveUp &&
                  NotchRecovery.Next(pill, NotchContentRules.Check(pill), 0) == RecoveryStep.RepairPill && NotchRecovery.Next(pill, NotchProblem.IdleEmpty, 1) == RecoveryStep.GiveUp &&
                  NotchRecovery.Next(Panel(), NotchProblem.None, 0) == RecoveryStep.None && NotchRecovery.Next(null, NotchProblem.NoTabs, 0) == RecoveryStep.None);

            var clock = new DateTime(2026, 10, 6, 12, 0, 0);
            var budget = new RecoveryBudget(3, TimeSpan.FromMinutes(1), () => clock);
            bool first3 = budget.TryTake() && budget.TryTake() && budget.TryTake();
            bool fourth = budget.TryTake();
            clock = clock.AddSeconds(59);
            bool still = budget.TryTake();
            clock = clock.AddSeconds(2);
            bool later = budget.TryTake();
            Check("NG17", "Pastila e reparată de cel mult 3 ori pe minut (o stare blocată nu umple log-ul)", first3 && !fourth && !still && later &&
                  NotchGuardInfo.PillBudget == 3 && NotchGuardInfo.BudgetWindow == TimeSpan.FromMinutes(1) && NotchGuardInfo.OpenCheckMs == 300 && NotchGuardInfo.SettleCheckMs == 450);

            string line = NotchGuardLog.Line(empty, NotchContentRules.Check(empty), "home", new[] { "activity-manager", "shelf", "Titlu Fereastră", null, "shelf" }, "Single/Normal",
                                             new[] { "raft", "quick-actions", "C:\\Users\\x" }, RecoveryStep.RebuildPage);
            string own = NotchGuardLog.Line(pill, NotchProblem.IdleEmpty, "0f3a9c1b2d4e4f5a8b6c7d8e9f0a1b2c", null, "Spotify - Piesă", null, RecoveryStep.RepairPill);
            Check("NG18", "Rândul „B1 recover”: problemele, modul, pagina, comutatoarele pornite, activitatea, overlay-urile și pasul; fără titluri, nume de pagini sau căi",
                  line == "B1 recover: PanelHidden, NoTabs · mod Expanded · panou ascuns · tab-uri 0 · pastila 1.00 · pagina home · comutatoare activity-manager, shelf · activitate Single/Normal · overlay-uri raft, quick-actions · pas 1: panoul și pagina curentă refăcute." &&
                  own.StartsWith("B1 recover: IdleEmpty · mod Idle · standby ascuns (2 elemente) · mică ascuns · alertă ascuns · pagina proprie · comutatoare — · activitate niciuna · overlay-uri — · pastila refăcută", StringComparison.Ordinal) &&
                  NotchGuardLog.Layers(new NotchView { Mode = NotchMode.Idle, Mini = true, Small = new LayerState(true, 1), SmallDate = "" }) == "standby ascuns (0 elemente) · mică 1.00 fără dată · alertă ascuns" &&
                  !own.Contains("0f3a9c") && !own.Contains("Spotify") && NotchGuardLog.PageLabel(null) == "niciuna" && NotchGuardLog.PageLabel("sources") == "sources" &&
                  NotchGuardLog.ResultLine(NotchProblem.None, RecoveryStep.OpenHome) == "B1 recover: după „pas 2: toate paginile refăcute, Acasă deschisă” conținutul se vede." &&
                  NotchGuardLog.ResultLine(NotchProblem.NoTabs, RecoveryStep.RebuildPage).EndsWith("tot gol (NoTabs).", StringComparison.Ordinal), line + " | " + own);

            Check("NG19", "Comutatorul „notch-guard”: Stabil, pornit implicit (anunțat în 0.6.18), rămâne pornit în --safe-mode",
                  FeatureCatalog.Find(NotchGuardInfo.FeatureId) is { Stage: FeatureStage.Stable, DefaultOn: true } && FeatureCatalog.NotchGuard == NotchGuardInfo.FeatureId &&
                  new FeatureFlags(new Dictionary<string, bool>(), FeatureCatalog.All, safeMode: true).IsEnabled(NotchGuardInfo.FeatureId) &&
                  !new FeatureFlags(new Dictionary<string, bool> { [NotchGuardInfo.FeatureId] = false }, FeatureCatalog.All).IsEnabled(NotchGuardInfo.FeatureId));

            Check("NG20", "Starea pentru testele de fum: „;b1=” (problemele de acum) și „;b1r=” (reparațiile), citite înapoi; stările vechi se citesc la fel",
                  SmokeMode.NotchGuardStatus(0, 0) == "" && SmokeMode.NotchGuardStatus(10, 2) == ";b1=10;b1r=2" &&
                  SmokeMode.TryParseStatus("mode=Expanded;pill=720x310;page=home;qa=1;b1=10;b1r=2", out var m, out _, out _, out var x, out var pg, out _) &&
                  m == "Expanded" && pg == "home" && x["b1"] == 10 && x["b1r"] == 2 && x["qa"] == 1 &&
                  SmokeMode.TryParseStatus("mode=Idle;pill=196x22;b1r=3", out _, out _, out _, out var y, out _, out _) && y["b1"] == 0 && y["b1r"] == 3 &&
                  SmokeMode.TryParseStatus("mode=Idle;pill=180x32", out _, out _, out _, out var z, out _, out _) && z["b1"] == 0 && z["b1r"] == 0 &&
                  !SmokeMode.TryParseStatus("mode=Idle;pill=180x32;b1r=1;qa=1", out _, out _, out _) &&
                  SmokeMode.Parse("smoke-empty-panel")?.Kind == SmokeCommandKind.EmptyPanel && SmokeMode.Parse(" Smoke-Empty-Pill ")?.Kind == SmokeCommandKind.EmptyPill &&
                  SmokeMode.Parse("smoke-empty-panel acum") == null);
        }

        // ------------------------------------------------------------------ the WPF side, pinned in the source

        static void NotchGuardSourcePins()
        {
            const string Part = "Features/NotchGuard/NotchWindow.NotchGuard.cs";
            string notch0 = Src("NotchWindow.xaml.cs"), part = Src(Part), partN = Norm(NoComments(part));
            string expand = Norm(NoComments(MethodBody(notch0, "private void Expand()")));
            string applyMode = Norm(NoComments(MethodBody(notch0, "private void ApplyMode()")));
            string barApply = Norm(NoComments(MethodBody(Src("Features/CommandBar/NotchWindow.CommandBar.cs"), "private bool CommandBarApplyMode()")));
            Check("NG21", "Legăturile sunt câte un rând: la finalul Expand (după raft), la finalul ApplyMode, în aranjarea Command Bar, StopNotchGuard în Cleanup",
                  expand.EndsWith("ShelfOnOpen(); NotchGuardOpened(); }", StringComparison.Ordinal) && applyMode.EndsWith("NotchGuardLaidOut(); }", StringComparison.Ordinal) &&
                  barApply.Contains("LayoutCommandBar(); NotchGuardLaidOut(); return true;") &&
                  Norm(NoComments(MethodBody(notch0, "public void Cleanup()"))).Contains("StopAudioSwitch(); StopNotchGuard();") &&
                  Count(Norm(NoComments(notch0)), "NotchGuardOpened();") == 1 && Count(Norm(NoComments(notch0)), "NotchGuardLaidOut();") == 1);
            var timers = Regex.Matches(partN, @"Interval = TimeSpan\.FromMilliseconds\((\w+)\)").Cast<Match>().Select(mm => mm.Groups[1].Value).ToList();
            Check("NG22", "Fără polling: doi cronometre one-shot (oprite la primul tick), pornite doar de legături; niciunul sub 300 ms; nimic cu notch-ul neschimbat",
                  partN.Contains("t.Tick += (o, e) => { ((DispatcherTimer)o).Stop(); check(); };") && timers.Count == 2 && timers.All(v => v == "ms") &&
                  partN.Contains("NotchGuardRestart(_ngOpen, NotchGuardInfo.OpenCheckMs, NotchGuardOpenCheck)") &&
                  partN.Contains("NotchGuardRestart(_ngSettle, NotchGuardInfo.SettleCheckMs, NotchGuardSettleCheck)") &&
                  Count(partN, "new DispatcherTimer") == 1 && !partN.Contains("CompositionTarget.Rendering"));
            var logs = part.Split('\n').Where(l => l.Contains("App.Log(", StringComparison.Ordinal)).ToList();
            Check("NG23", "Comutatorul e citit la fiecare verificare și legătură; erorile → ReportError(\"notch-guard\"); în log doar rândurile NotchGuardLog (fără titluri sau nume)",
                  Count(partN, "NotchGuardEnabled()") >= 4 && partN.Contains("FeatureFlags.Current?.ReportError(NotchGuardInfo.FeatureId, ex);") &&
                  logs.Count >= 4 && logs.All(l => l.Contains("NotchGuardLog.", StringComparison.Ordinal)) && !partN.Contains(".Title") && !partN.Contains("Page.Name"), string.Join(" | ", logs));
            Check("NG24", "Reparațiile: pasul 1 pune panoul și pagina curentă înapoi (ShowPane din nou, tab-urile); pasul 2 reface paginile (RebuildUi) și arată Acasă; pastila: stratul modului arătat, celelalte ascunse",
                  partN.Contains("var p = _pane ?? _home; _pane = null; PaneHost.Content = null; p.Visibility = Visibility.Visible; ShowPane(p); RebuildTabs(); RelayoutPanel();") &&
                  partN.Contains("RebuildUi(); if (_pane != _home) ShowPane(_home); RebuildTabs(); NotchGuardShowPanelLayer(); RelayoutPanel();") &&
                  partN.Contains("if (layer == want) NotchGuardShow(layer); else NotchGuardHide(layer);") &&
                  partN.Contains("case RecoveryStep.CloseCommandBar: CloseCommandBar(); break;"));
            string smoke0 = Src("Features/Smoke/NotchWindow.Smoke.cs");
            Check("NG25", "Comenzile de fum care golesc intenționat panoul și pastila verifică singure SmokeMode.On; starea de fum primește „;b1=”/„;b1r=”",
                  Norm(NoComments(MethodBody(smoke0, "private void SmokeEmptyPanel()"))).StartsWith("{ if (!SmokeMode.On) return;", StringComparison.Ordinal) &&
                  Norm(NoComments(MethodBody(smoke0, "private void SmokeEmptyPill()"))).StartsWith("{ if (!SmokeMode.On) return;", StringComparison.Ordinal) &&
                  Norm(NoComments(smoke0)).Contains("case SmokeCommandKind.EmptyPanel: SmokeEmptyPanel(); break;") &&
                  Norm(NoComments(smoke0)).Contains("SmokeMode.QuickActionsStatus(_qaInvoked, _qaSuggested) + NotchGuardSmokeStatus()"));
            string sp = SmokeSrc("SmokeB1.cs"), b1Body = Norm(NoComments(MethodBody(sp, "private static void B1EmptyNotch()") ?? ""));
            Check("NG26", "Fumul B1 rulează pe ambele drumuri ale activity-manager: plasa repară un panou și o pastilă golite intenționat, apoi 20 de cicluri (alertă, Command Bar, raft, pagina după context) cu toate comutatoarele noi pornite, fără nicio reparație",
                  Regex.IsMatch(Norm(NoComments(sp)), @"Run\(step = ""B1[^""]*"", B1EmptyNotch\);") && !Regex.IsMatch(Norm(NoComments(sp)), @"if \(!_activityOn\) Run\(step = ""B1") &&
                  b1Body.Contains("for (int i = 1; i <= B1Cycles; i++)") && sp.Contains("private const int B1Cycles = 20;") &&
                  B1FeaturesInSmoke(sp).SetEquals(new[] { "command-bar", "context-pages", "quick-actions", "smart-clipboard", "shelf", "audio-switch" }) &&
                  b1Body.Contains("Command(\"smoke-empty-panel\");") && b1Body.Contains("Command(\"smoke-empty-pill\");"));
        }

        static HashSet<string> B1FeaturesInSmoke(string src)
        {
            var m = Regex.Match(src ?? "", @"B1Features = \{([^}]*)\}");
            return new HashSet<string>(Regex.Matches(m.Success ? m.Groups[1].Value : "", "\"([a-z-]+)\"").Cast<Match>().Select(x => x.Groups[1].Value));
        }
    }
}
