using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using WinNotch.Core.Activity;
using WinNotch.Features.Activity;

namespace WinNotch
{
    public static partial class T
    {
        // ------------------------------------------------------------------ the repo's sources (pinning the legacy alert code)

        static string _repoRoot;

        /// <summary>The folder with WinNotch.csproj, found by walking up from the test binary.</summary>
        static string RepoRoot()
        {
            if (_repoRoot != null) return _repoRoot;
            for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
                if (File.Exists(Path.Combine(d.FullName, "WinNotch.csproj"))) return _repoRoot = d.FullName;
            throw new InvalidOperationException("WinNotch.csproj nu a fost găsit deasupra " + AppContext.BaseDirectory);
        }

        static readonly Dictionary<string, string> SrcCache = new Dictionary<string, string>();
        static string Src(string rel) => SrcCache.TryGetValue(rel, out var s) ? s : SrcCache[rel] = File.ReadAllText(Path.Combine(RepoRoot(), rel.Replace('/', Path.DirectorySeparatorChar)));

        /// <summary>
        /// The smoke test's sources (tests/WinNotch.Smoke, split by area): SmokeProgram.cs (Main, the order of the checks, the
        /// shared helpers) plus the given area files, e.g. "SmokeContext.cs". A file not compiled by WinNotch.Smoke.csproj counts as empty.
        /// </summary>
        static string SmokeSrc(params string[] areas)
        {
            string proj = Src("tests/WinNotch.Smoke/WinNotch.Smoke.csproj");
            return string.Join("\n", new[] { "SmokeProgram.cs" }.Concat(areas)
                .Select(f => proj.Contains("<Compile Include=\"" + f + "\" />") ? Src("tests/WinNotch.Smoke/" + f) : ""));
        }

        /// <summary>Whitespace runs → one space (indentation and line breaks don't matter).</summary>
        static string Norm(string s) => Regex.Replace(s ?? "", @"\s+", " ").Trim();

        /// <summary>Without // comments (only for method bodies: their strings hold no "//").</summary>
        static string NoComments(string s) => Regex.Replace(s ?? "", @"//[^\n]*", "");

        /// <summary>The block body of the method whose declaration starts with <paramref name="signature"/>, braces included.</summary>
        static string MethodBody(string src, string signature)
        {
            int i = src.IndexOf(signature, StringComparison.Ordinal);
            if (i < 0) return null;
            int j = src.IndexOf('{', i), depth = 0;
            for (int k = j; k < src.Length; k++)
            {
                if (src[k] == '{') depth++;
                else if (src[k] == '}' && --depth == 0) return src.Substring(j, k - j + 1);
            }
            return null;
        }

        static int Count(string text, string part)
        {
            int n = 0;
            for (int i = text.IndexOf(part, StringComparison.Ordinal); i >= 0; i = text.IndexOf(part, i + part.Length, StringComparison.Ordinal)) n++;
            return n;
        }

        /// <summary>The source lines of <paramref name="rel"/> that contain <paramref name="part"/>.</summary>
        static List<string> LinesWith(string rel, string part) =>
            Src(rel).Split('\n').Where(l => l.Contains(part, StringComparison.Ordinal)).ToList();

        // ------------------------------------------------------------------ how an alert ends up on screen

        /// <summary>
        /// One way of showing the notch's alerts, driven by a fake clock: the legacy path (a model of ShowLive / EndLive /
        /// the live timer) and, with the "activity-manager" switch on, the Activity Manager. The characterization tests run on
        /// each of them and expect the same visible outcome.
        /// </summary>
        abstract class AlertSink
        {
            public DateTime Now = new DateTime(2026, 10, 5, 12, 0, 0);
            /// <summary>The notch is open (Mode.Expanded).</summary>
            public bool Open;
            /// <summary>The pill is hidden over a fullscreen app (_hidden).</summary>
            public bool Hidden;
            public abstract string Name { get; }
            /// <summary>Shows the alert like its call site does; false when it was not shown (or queued).</summary>
            public abstract bool Show(LegacyAlert a, int rows = 0);
            /// <summary>LiveVolume: updates the volume alert in place while it is shown, otherwise shows it.</summary>
            public abstract void Volume();
            /// <summary>The visible alert's id, or null in standby.</summary>
            public abstract string ShownId { get; }
            public abstract double ShownW { get; }
            public abstract double ShownH { get; }
            /// <summary>A button of the alert ("Mai târziu", "Sari"…): EndLive.</summary>
            public abstract void EndByUser();
            public abstract void Advance(TimeSpan d);
            public void Ms(int ms) => Advance(TimeSpan.FromMilliseconds(ms));
            public void Sec(double s) => Advance(TimeSpan.FromSeconds(s));
            /// <summary>Standby: no alert and the notch closed (the legacy "_mode == Mode.Idle").</summary>
            public bool Idle => ShownId == null && !Open;
        }

        /// <summary>The legacy path, modelled from ShowLive / EndLive (pinned below): the newest alert replaces the current one.</summary>
        sealed class LegacySink : AlertSink
        {
            private LegacyAlert _cur;
            private DateTime _until;
            private double _h;
            public override string Name => "calea veche";

            public override bool Show(LegacyAlert a, int rows = 0)
            {
                if (!LegacyAlertRules.ShowLiveGate(Open, Hidden, a.Important)) return false;
                _cur = a; _h = a.HeightFor(rows);
                _until = Now.AddMilliseconds(a.DurationMs);           // _liveTimer restarted with the alert's duration
                return true;
            }

            public override void Volume()
            {
                if (_cur?.Id == LegacyAlerts.Volume && !Open) { _until = Now.AddMilliseconds(_cur.DurationMs); return; }   // in place: timer restarted
                Show(LegacyAlerts.Find(LegacyAlerts.Volume));
            }

            public override string ShownId => Open ? null : _cur?.Id;
            public override double ShownW => _cur?.Width ?? 0;
            public override double ShownH => _cur == null ? 0 : _h;
            public override void EndByUser() => _cur = null;

            public override void Advance(TimeSpan d)
            {
                Now += d;
                if (Open) _cur = null;                               // Expand: the live timer stops, the alert is gone
                if (_cur != null && Now >= _until) _cur = null;
            }
        }

        /// <summary>
        /// The "activity-manager" path: the alert posted as the notch posts it (ActivityRouting.FromAlert), the manager with a
        /// fake clock, the notch's state as its environment, and the notch's hooks (Collapse → NotchClosed, EndLive → Dismiss,
        /// LiveVolume in place → Touch).
        /// </summary>
        sealed class ActivitySink : AlertSink, IActivityEnvironment
        {
            private readonly FakeClock _clock = new FakeClock();
            public readonly ActivityManager M;
            private bool _wasOpen;
            public ActivitySink() { M = new ActivityManager(_clock, this); }
            public override string Name => "Activity Manager";
            bool IActivityEnvironment.NotchOpen => Open;
            bool IActivityEnvironment.Fullscreen => Hidden;

            /// <summary>The notch was closed since the last step: Collapse's hook.</summary>
            private void Sync()
            {
                if (_wasOpen && !Open) M.NotchClosed();
                _wasOpen = Open;
            }

            public override bool Show(LegacyAlert a, int rows = 0)
            {
                Sync();
                var act = ActivityRouting.FromAlert(a.Id, a.Width, a.HeightFor(rows), a.DurationMs, a.Important, new object());
                var r = M.Post(act);
                return ActivityRouting.Accepted(r, act.Interactive, !Open && M.View.Primary?.Key == act.Key);
            }

            public override void Volume()
            {
                Sync();
                if (ShownId == LegacyAlerts.Volume && M.Touch(LegacyAlerts.Volume)) return;
                Show(LegacyAlerts.Find(LegacyAlerts.Volume));
            }

            public override string ShownId
            {
                get
                {
                    var v = M.View;
                    if (Open || v.Kind == ActivityViewKind.None) return null;
                    return v.Kind == ActivityViewKind.Group ? ActivityManager.GroupId : v.Primary.Id;
                }
            }
            public override double ShownW => M.View.Primary?.Width ?? 0;
            public override double ShownH => M.View.Primary?.Height ?? 0;
            public override void EndByUser() { var k = M.View.Primary?.Key; if (k != null) M.Dismiss(k); }
            public override void Advance(TimeSpan d) { Sync(); Now += d; _clock.Advance(d); Sync(); }
        }

        /// <summary>Source snippets of the legacy decisions (whitespace-insensitive), each expected exactly once in its file.</summary>
        static readonly (string Id, string File, string Snippet)[] LegacyPins =
        {
            ("ram-prag", LegacyAlerts.Notch, "if (pct < Math.Clamp(S.RamAlertPercent, 50, 98)) { _ramHighSince = null; return; }"),
            ("ram-15min", LegacyAlerts.Notch, "if ((now - _lastRamAlert).TotalMinutes < 15) return;"),
            ("ram-20s", LegacyAlerts.Notch, "if ((now - _ramHighSince.Value).TotalSeconds < 20 || _mode != Mode.Idle || _hidden || _toolBusy || Procs.TopRam.Count == 0) return;"),
            ("ram-doar-afisata", LegacyAlerts.Notch, "if (ShowRamAlert(pct)) _lastRamAlert = now;"),
            ("temp-5min", LegacyAlerts.Notch, "if (S.Temperatures && hot >= 88 && (DateTime.Now - _lastHotAlert).TotalMinutes > 5) { _lastHotAlert = DateTime.Now;"),
            ("temp-max", LegacyAlerts.Notch, "float hot = Math.Max(Temps.Cpu ?? 0, Temps.Gpu ?? 0);"),
            ("baterie-incarcare", LegacyAlerts.Notch, "if (Stats.Charging) _lastBatAlert = 101;"),
            ("baterie-20-10", LegacyAlerts.Notch, "else if (b >= 0 && ((b <= 10 && _lastBatAlert > 10) || (b <= 20 && _lastBatAlert > 20))) { _lastBatAlert = b <= 10 ? 10 : 20;"),
            ("incarcator", LegacyAlerts.Notch, "if (_lastCharging != null && _lastCharging.Value != Stats.Charging)"),
            ("ochi-pauza", LegacyAlerts.Notch, "if (idle > 300) _activeSeconds = 0; else if (idle < 60) _activeSeconds++;"),
            ("ochi-prag", LegacyAlerts.Notch, "if (_activeSeconds >= Math.Max(5, S.EyeBreakMinutes) * 60 && _mode == Mode.Idle && !_hidden) { _activeSeconds = 0; ShowEyeBreak(); }"),
            ("piesa-poarta", LegacyAlerts.Notch, "if (trackChanged && mi.Playing && _mode != Mode.Expanded && !SourceInFront(mi)) ShowTrackAlert(mi);"),
            ("piesa-15min", "Services/NowPlaying.cs", "foreach (var old in _announced.Where(k => (now - k.Value).TotalMinutes > 15).Select(k => k.Key).ToList()) _announced.Remove(old);"),
            ("piesa-10s", "Services/NowPlaying.cs", "if (_announced.ContainsKey(key) || (now - _lastAnnounce).TotalSeconds < 10) return false;"),
            ("volum-schimbare", LegacyAlerts.Notch, "bool same = _lastVol < 0 || (v == _lastVol && muted == _lastMuted);"),
            ("volum-poarta", LegacyAlerts.Notch, "if (!same && _mode != Mode.Expanded && (DateTime.Now - _volSetByUs).TotalMilliseconds > 600) LiveVolume(v, muted);"),
            ("volum-pe-loc", LegacyAlerts.Notch, "if (_mode == Mode.Live && LiveLayer.Content == _volLive && _volLive != null)"),
            ("oferta-poarta", LegacyAlerts.Updates, "if (_update != null && !_updateOffered && _mode == Mode.Idle && !_hidden && DateTime.Now >= S.UpdateSnoozeUntil && !_liveInteractive) if (ShowUpdateOffer(_update)) _updateOffered = true;"),
            ("oferta-24h", LegacyAlerts.Updates, "S.UpdateSnoozeUntil = DateTime.Now.AddHours(24);"),
            ("extensie-poarta", LegacyAlerts.Updates, "if (Bridge.OldExtensionSeen && !_oldExtShown && _mode == Mode.Idle && !_hidden) { _oldExtShown = true; ShowOldExtension(); return; }"),
            ("extensie-reincearca", LegacyAlerts.Updates, "{ _oldExtShown = false; return; }"),
            ("dupa-actualizare", LegacyAlerts.Updates, "if (App.JustUpdated && !_afterUpdateShown && _tick > 3 && _mode == Mode.Idle)"),
            ("revenire", LegacyAlerts.Updates, "if (App.RollbackMessage != null && _tick > 3 && _mode == Mode.Idle)"),
        };

        /// <summary>The legacy funnels, comments and whitespace aside.</summary>
        const string LegacyShowLive = "{ if (_mode == Mode.Expanded) return false; if (_hidden && !important) return false; EndLiveInteractive(); LiveLayer.Content = content; _liveW = w; _liveH = h; _mode = Mode.Live; ClearDwell(); ApplyMode(); ApplyHidden(); _liveTimer.Stop(); _liveTimer.Interval = TimeSpan.FromMilliseconds(ms); _liveTimer.Start(); return true; }";
        const string LegacyEndLive = "{ _liveTimer.Stop(); EndLiveInteractive(); if (_mode != Mode.Live) return; _lastActive = DateTime.Now; _mode = Mode.Idle; ApplyMode(); ApplyHidden(); var shown = LiveLayer.Content; var clear = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) }; clear.Tick += (o, e) => { clear.Stop(); if (_mode != Mode.Live && LiveLayer.Content == shown) LiveLayer.Content = null; }; clear.Start(); }";
        const string LegacyEndLiveInteractive = "{ if (!_liveInteractive) return; _liveInteractive = false; LiveLayer.IsHitTestVisible = false; if (_mode != Mode.Expanded) SetClickThrough(true); }";

        /// <summary>
        /// P13, before the Activity Manager: characterization of every existing alert (duration, size, "important",
        /// blocked with the notch open and over fullscreen, the repeat limits), pinned to the source and checked on each path.
        /// </summary>
        static void AlertCharacterizationTests()
        {
            var all = LegacyAlerts.All;
            Check("AC1", "Tabelul alertelor: 28, id-uri unice, cu literă mică și cratime",
                  all.Count == 28 && all.Select(a => a.Id).Distinct().Count() == 28 && all.All(a => Regex.IsMatch(a.Id, "^[a-z]+(-[a-z]+)*$")),
                  all.Count + " alerte");

            // ---- source pins: each call still passes the table's literal size, duration and "important"
            foreach (var a in all)
            {
                var lines = LinesWith(a.File, a.Anchor);
                bool one = lines.Count == 1;
                bool tail = one && Norm(lines[0]).Contains(Norm(a.Tail), StringComparison.Ordinal);
                Check("AC-src-" + a.Id, "Sursa: „" + a.Id + "” (" + a.File + ") e apelată cu " + a.Width + "×" + a.Height + (a.HeightPerRow > 0 ? "+" + a.HeightPerRow + "/rând" : "") +
                      ", " + a.DurationMs + " ms" + (a.Important ? ", importantă" : ""), one && tail,
                      one ? "linia: " + Norm(lines[0]) : lines.Count + " linii cu „" + a.Anchor + "”");
            }

            // every alert call in the notch's files is one of the table's (a new alert must be added to the table)
            var calls = new List<string>();
            foreach (var f in new[] { LegacyAlerts.Notch, LegacyAlerts.Updates, LegacyAlerts.Context, LegacyAlerts.Diagnostics })
                foreach (var l in Src(f).Split('\n'))
                {
                    string n = Norm(NoComments(l));
                    if (!Regex.IsMatch(n, @"\b(ShowLive|ShowInteractive|ToolAlert|Alert)\(")) continue;
                    if (Regex.IsMatch(n, @"^(private|internal|public)\b")) continue;                    // the declarations
                    if (n.Contains("content, w, h, ms, true)") || n.Contains("width, 58, 4200, true)")) continue;    // the funnels' own bodies
                    if (!all.Any(a => a.File == f && l.Contains(a.Anchor, StringComparison.Ordinal))) calls.Add(f + ": " + n);
                }
            Check("AC2", "Fiecare apel de alertă din notch e în tabel (nicio alertă necunoscută)", calls.Count == 0, string.Join(" | ", calls));

            // ---- the funnels: the legacy ShowLive / EndLive bodies, byte for byte (whitespace and comments aside)
            string notch = Src(LegacyAlerts.Notch);
            Check("AC3", "ShowLive (calea veche) e neschimbat: blocat cu notch-ul deschis, peste ecran complet doar „important”, cronometrul cu durata",
                  Norm(NoComments(MethodBody(notch, "private bool ShowLive(UIElement content, double w, double h, int ms, bool important = false)"))) == LegacyShowLive);
            Check("AC4", "EndLive și EndLiveInteractive (calea veche) sunt neschimbate",
                  LegacyEndLiveBody(notch) == LegacyEndLive &&
                  Norm(NoComments(MethodBody(notch, "private void EndLiveInteractive()"))) == LegacyEndLiveInteractive);
            string toolBody = Norm(MethodBody(notch, "private void ToolAlert("));
            string interBody = Norm(NoComments(MethodBody(notch, "private void ShowInteractive(")));
            Check("AC5", "ToolAlert: 58 px, 4,2 s, importantă, pe Dispatcher (Background); ShowInteractive: importantă, apoi primește click-uri",
                  toolBody.Contains("width, 58, 4200, true)") && toolBody.Contains("DispatcherPriority.Background") &&
                  interBody.Contains("content, w, h, ms, true)) return;") &&
                  interBody.Contains("_liveInteractive = true; LiveLayer.IsHitTestVisible = true; SetClickThrough(false);") &&
                  Regex.IsMatch(Norm(notch), @"private void ToolAlert\([^)]*double width = 440\)"));
            var interactiveSet = all.Where(a => a.Interactive).Select(a => a.Id).OrderBy(x => x).ToList();
            Check("AC6", "Alertele cu butoane (click-through oprit): RAM, captură, pauză ochi, ofertă, noutăți, serviciu temperatură, extensie, raportul închiderilor",
                  string.Join(",", interactiveSet) == "capture-result,eye-break,helper-update,old-extension,ram,shutdown-unexplained,update-offer,whats-new" &&
                  Count(Norm(Src(LegacyAlerts.Notch)), "_liveInteractive = true;") == 3 && Count(Norm(Src(LegacyAlerts.Updates)), "_liveInteractive = true;") == 4);

            // ---- the legacy decisions, pinned to the source
            foreach (var (id, file, snippet) in LegacyPins)
            {
                int n = Count(Norm(Src(file)), Norm(snippet));
                Check("AC-lim-" + id, "Sursa: limita „" + id + "” e neschimbată (" + file + ")", n == 1, n + " apariții");
            }

            CharacterizePath(new LegacySink(), () => new LegacySink());
            LegacyOnlyCharacterization();
            CharacterizePath(new ActivitySink(), () => new ActivitySink());       // the same, with the "activity-manager" switch on
            RoutingPins();
        }

        /// <summary>The one line P13 adds at the end of EndLive (a notification; a no-op with the switch off).</summary>
        const string EndLiveHook = "ActivityLiveEnded();";

        /// <summary>EndLive's body without the P13 hook (which must be its last statement).</summary>
        static string LegacyEndLiveBody(string notch)
        {
            string b = Norm(NoComments(MethodBody(notch, "private void EndLive()")));
            string tail = " " + EndLiveHook + " }";
            return b.EndsWith(tail, StringComparison.Ordinal) ? b.Substring(0, b.Length - tail.Length) + " }" : b;
        }

        /// <summary>
        /// Per alert and per repeat limit: the visible outcome on one path. <paramref name="fresh"/> makes a new sink of the
        /// same path for each case.
        /// </summary>
        static void CharacterizePath(AlertSink first, Func<AlertSink> fresh)
        {
            string p = " [" + first.Name + "]";
            foreach (var a in LegacyAlerts.All)
            {
                int rows = a.HeightPerRow > 0 ? 3 : 0;
                var problems = new List<string>();

                var s = fresh();                                   // standby: shown with its size, for exactly its duration
                if (!s.Show(a, rows)) problems.Add("nu apare în standby");
                else
                {
                    if (s.ShownId != a.Id) problems.Add("arată " + s.ShownId);
                    if (s.ShownW != a.Width || s.ShownH != a.HeightFor(rows)) problems.Add("mărime " + s.ShownW + "×" + s.ShownH);
                    s.Ms(a.DurationMs - 1);
                    if (s.ShownId != a.Id) problems.Add("dispare înainte de " + a.DurationMs + " ms");
                    s.Ms(1);
                    if (s.ShownId != null) problems.Add("rămâne după " + a.DurationMs + " ms");
                }

                var o = fresh(); o.Open = true;                    // notch open: dropped, nothing afterwards
                if (o.Show(a, rows)) problems.Add("apare cu notch-ul deschis");
                o.Open = false; o.Ms(10);
                if (o.ShownId != null) problems.Add("apare după închiderea notch-ului");

                var h = fresh(); h.Hidden = true;                  // fullscreen: only the important ones
                bool shownHidden = h.Show(a, rows);
                if (shownHidden != a.Important) problems.Add(a.Important ? "nu apare peste ecran complet" : "apare peste ecran complet");

                if (a.Interactive)                                 // a button ends it right away
                {
                    var b = fresh();
                    b.Show(a, rows); b.Ms(100); b.EndByUser();
                    if (b.ShownId != null) problems.Add("butonul nu o închide");
                }
                Check("AC-" + a.Id + (first is LegacySink ? "" : "-nou"), "„" + a.Id + "”: " + a.DurationMs + " ms, " + a.Width + "×" + a.HeightFor(rows) +
                      (a.Important ? ", importantă (și peste ecran complet)" : ", nu apare peste ecran complet") + ", niciodată cu notch-ul deschis" + p,
                      problems.Count == 0, string.Join("; ", problems));
            }

            string sfx = first is LegacySink ? "" : "-nou";

            // ---- RAM: 20 s over the limit, then at most once in 15 minutes; waits while open, hidden or another alert shows
            {
                var s = fresh(); var ram = LegacyAlerts.Find(LegacyAlerts.Ram);
                DateTime? since = null; var last = DateTime.MinValue; var shown = new List<int>();
                for (int t = 0; t < 2400; t++)
                {
                    s.Open = t >= 10 && t < 40;                    // the notch open when the first alert is due: it waits
                    if (LegacyAlertRules.RamShouldTry(true, 14.4, 16, 80, s.Now, ref since, last, s.Idle, s.Hidden, false, 4) && s.Show(ram, 4)) { last = s.Now; shown.Add(t); }
                    s.Sec(1);
                }
                var hid = fresh(); hid.Hidden = true; DateTime? since2 = null; var last2 = DateTime.MinValue; int n2 = 0;
                for (int t = 0; t < 120; t++) { if (LegacyAlertRules.RamShouldTry(true, 15, 16, 80, hid.Now, ref since2, last2, hid.Idle, hid.Hidden, false, 4) && hid.Show(ram, 4)) { last2 = hid.Now; n2++; } hid.Sec(1); }
                Check("AC-lim-ram" + sfx, "RAM: după 20 s peste prag, cel mult o dată la 15 minute; așteaptă cât notch-ul e deschis; nu apare peste ecran complet" + p,
                      string.Join(",", shown) == "40,940,1840" && n2 == 0, string.Join(",", shown) + " / ascuns: " + n2);
            }

            // ---- heat: at most once in 5 minutes (the wait starts even when the alert was dropped)
            {
                var s = fresh(); var hot = LegacyAlerts.Find(LegacyAlerts.TempHot);
                var last = DateTime.MinValue; var shown = new List<int>(); var tried = new List<int>();
                for (int t = 0; t < 1200; t++)
                {
                    s.Open = t < 5;
                    if (LegacyAlertRules.HotShouldShow(true, 91, 70, s.Now, ref last)) { tried.Add(t); if (s.Show(hot)) shown.Add(t); }
                    s.Sec(1);
                }
                var h = fresh(); h.Hidden = true; var lh = DateTime.MinValue;
                bool overFull = LegacyAlertRules.HotShouldShow(true, 70, 88, h.Now, ref lh) && h.Show(hot);
                var cool = DateTime.MinValue;
                bool under = LegacyAlertRules.HotShouldShow(true, 87.9f, 87.9f, h.Now, ref cool) || LegacyAlertRules.HotShouldShow(false, 95, 95, h.Now, ref cool);
                Check("AC-lim-temp" + sfx, "Temperatură: peste 88 °C, cel mult o dată la 5 minute (pauza curge și dacă alerta n-a apărut); importantă, apare peste ecran complet" + p,
                      string.Join(",", tried) == "0,301,602,903" && string.Join(",", shown) == "301,602,903" && overFull && !under, string.Join(",", tried) + " / " + string.Join(",", shown));
            }

            // ---- battery: at 20% and at 10%, again after charging
            {
                var s = fresh(); var low = LegacyAlerts.Find(LegacyAlerts.BatteryLow);
                int last = 101; var at = new List<int>();
                void Step(bool charging, int b) { if (LegacyAlertRules.BatteryLowStep(charging, b, ref last) && s.Show(low)) at.Add(b); s.Sec(10); }
                for (int b = 30; b >= 5; b--) Step(false, b);
                Step(true, 5); Step(true, 15);
                for (int b = 15; b >= 9; b--) Step(false, b);
                s.Hidden = true; last = 101; Step(false, 20);
                Check("AC-lim-baterie" + sfx, "Baterie: o dată la 20% și o dată la 10%; încărcarea le resetează; apare și peste ecran complet" + p,
                      string.Join(",", at) == "20,10,15,10,20", string.Join(",", at));
            }

            // ---- charger: only a change, not the first reading
            {
                var s = fresh(); var pw = LegacyAlerts.Find(LegacyAlerts.Power);
                bool? last = null; int n = 0;
                foreach (bool c in new[] { true, true, false, false, true })
                {
                    if (LegacyAlertRules.PowerChanged(last, c) && s.Show(pw)) n++;
                    last = c; s.Sec(5);
                }
                Check("AC-lim-incarcator" + sfx, "Încărcător: doar la schimbare (nu la prima citire)" + p, n == 2, n.ToString());
            }

            // ---- eye break: after N active minutes, never over fullscreen (it waits), reset after 5 minutes away
            {
                var s = fresh(); var eye = LegacyAlerts.Find(LegacyAlerts.EyeBreak);
                int active = 0; var at = new List<int>();
                for (int t = 0; t < 2000; t++)
                {
                    s.Hidden = t >= 1700 && t < 1750;
                    double idle = t >= 400 && t < 410 ? 30 : t >= 500 && t < 520 ? 400 : 1;   // a short pause counts, a long one resets
                    if (LegacyAlertRules.EyeBreakStep(idle, 20, s.Idle, s.Hidden, ref active) && s.Show(eye)) at.Add(t);
                    s.Sec(1);
                }
                Check("AC-lim-ochi" + sfx, "Pauză pentru ochi: după 20 de minute active; o pauză de peste 5 minute resetează; peste ecran complet așteaptă" + p,
                      string.Join(",", at) == "1750", string.Join(",", at));
            }

            // ---- volume: only real changes, not while open, not right after WinNotch changed it; updated in place while shown
            {
                var s = fresh();
                int lv = -1; bool lm = false; var setByUs = DateTime.MinValue; int n = 0;
                void Vol(int v, bool m) { if (LegacyAlertRules.VolumeShould(v, m, ref lv, ref lm, s.Open, s.Now, setByUs)) { s.Volume(); n++; } }
                Vol(40, false);                                    // first reading: nothing
                bool firstQuiet = s.ShownId == null;
                for (int i = 0; i < 30; i++) { Vol(41 + i, false); s.Ms(100); }      // a drag of 3 s: one alert, kept alive
                bool kept = s.ShownId == LegacyAlerts.Volume;      // last change 100 ms ago
                s.Ms(1499);
                bool stillAt1500 = s.ShownId == LegacyAlerts.Volume;
                s.Ms(1);
                bool goneAfter = s.ShownId == null;
                Vol(75, false); bool again = s.ShownId == LegacyAlerts.Volume;
                s.Ms(2000); int before = n;
                Vol(75, false);                                    // same value again: nothing
                setByUs = s.Now; s.Ms(300); Vol(20, false);        // WinNotch set it 300 ms ago: nothing
                s.Ms(400); s.Open = true; Vol(25, false);          // the notch is open: nothing
                bool quiet = s.ShownId == null;
                Check("AC-lim-volum" + sfx, "Volum: doar schimbări reale; nu cu notch-ul deschis, nu la 600 ms după o schimbare făcută de WinNotch; tras continuu = o singură alertă, ține 1,6 s după ultima schimbare" + p,
                      firstQuiet && kept && stillAt1500 && goneAfter && again && quiet && n == 31 && before == 31,
                      $"{firstQuiet} {kept} {stillAt1500} {goneAfter} {again} {quiet} n={n} before={before}");
            }

            // ---- the flows: the next step replaces the previous one at once (OCR, RAM, update)
            {
                var s = fresh();
                s.Show(LegacyAlerts.Find(LegacyAlerts.OcrReading)); s.Sec(2);
                s.Show(LegacyAlerts.Find(LegacyAlerts.OcrDone));
                bool ocr = s.ShownId == LegacyAlerts.OcrDone;
                s.Ms(4999); bool ocrKept = s.ShownId == LegacyAlerts.OcrDone; s.Ms(1);
                bool ocrGone = s.ShownId == null;
                s.Show(LegacyAlerts.Find(LegacyAlerts.UpdateDownload)); s.Sec(30);
                s.Show(LegacyAlerts.Find(LegacyAlerts.UpdateRefused));
                bool upd = s.ShownId == LegacyAlerts.UpdateRefused;
                s.Show(LegacyAlerts.Find(LegacyAlerts.RamProgress)); s.Sec(3);
                s.Show(LegacyAlerts.Find(LegacyAlerts.RamDone));
                bool ram = s.ShownId == LegacyAlerts.RamDone && s.ShownW == 520;
                Check("AC-flux" + sfx, "Pașii unui flux se înlocuiesc imediat: OCR „Citesc…” → „Text copiat”, descărcare → „refuzată”, RAM progres → rezultat" + p,
                      ocr && ocrKept && ocrGone && upd && ram, $"{ocr} {ocrKept} {ocrGone} {upd} {ram}");
            }

            // ---- update offer / old extension / after update / rollback: their gates
            {
                var s = fresh(); var offer = LegacyAlerts.Find(LegacyAlerts.UpdateOffer);
                bool offered = false; var snooze = DateTime.MinValue; var at = new List<int>();
                for (int t = 0; t < 60; t++)
                {
                    s.Hidden = t < 5;
                    if (t == 20) { s.EndByUser(); snooze = s.Now.AddHours(24); offered = false; }            // "Mai târziu"
                    if (LegacyAlertRules.UpdateOfferGate(true, offered, s.Idle, s.Hidden, s.Now, snooze, false) && s.Show(offer, 4)) { offered = true; at.Add(t); }
                    s.Sec(1);
                }
                s.Advance(TimeSpan.FromHours(24));
                bool again = LegacyAlertRules.UpdateOfferGate(true, offered, s.Idle, s.Hidden, s.Now, snooze, false) && s.Show(offer, 4);
                Check("AC-lim-oferta" + sfx, "Oferta de actualizare: o dată, în standby, nu peste ecran complet; „Mai târziu” o amână 24 de ore" + p,
                      string.Join(",", at) == "5" && again, string.Join(",", at) + " " + again);

                var e = fresh(); var ext = LegacyAlerts.Find(LegacyAlerts.OldExtension);
                bool extShown = false; int tries = 0, shows = 0;
                for (int t = 0; t < 10; t++)
                {
                    e.Open = t < 3;
                    if (LegacyAlertRules.OldExtensionGate(true, extShown, !e.Open && e.ShownId == null, e.Hidden))
                    {
                        tries++; extShown = true;
                        if (e.Show(ext)) shows++; else extShown = false;          // not shown: tried again later
                    }
                    e.Sec(1);
                }
                Check("AC-lim-extensie" + sfx, "Extensia veche: o singură dată, când notch-ul e liber" + p, tries == 1 && shows == 1, tries + "/" + shows);

                bool after = !LegacyAlertRules.AfterUpdateGate(true, false, 3, true) && LegacyAlertRules.AfterUpdateGate(true, false, 4, true) &&
                             !LegacyAlertRules.AfterUpdateGate(true, true, 9, true) && !LegacyAlertRules.AfterUpdateGate(true, false, 9, false);
                bool roll = !LegacyAlertRules.RollbackGate(true, 3, true) && LegacyAlertRules.RollbackGate(true, 4, true) && !LegacyAlertRules.RollbackGate(true, 9, false);
                var w = fresh();
                bool whats = w.Show(LegacyAlerts.Find(LegacyAlerts.WhatsNew), 6) && w.ShownH == 64 + 6 * 22;
                Check("AC-lim-pornire" + sfx, "După actualizare și după revenire: o dată, din a 4-a secundă, doar în standby" + p, after && roll && whats);
            }
        }

        /// <summary>
        /// P13 routing, in the source: every alert goes through Alert(LegacyAlerts.X, …) with its own table id; ShowLive is called
        /// only by Alert (switch off) and the presenter; the hooks are single lines.
        /// </summary>
        static void RoutingPins()
        {
            var wrong = new List<string>();
            foreach (var a in LegacyAlerts.All)
            {
                var line = LinesWith(a.File, a.Anchor).FirstOrDefault() ?? "";
                string field = typeof(LegacyAlerts).GetFields().First(f => f.IsLiteral && (string)f.GetRawConstantValue() == a.Id && f.Name != "OcrKey").Name;
                string call = Norm(line).Contains("ShowInteractive(") ? "ShowInteractive(LegacyAlerts." + field : Norm(line).Contains("ToolAlert(") ? "ToolAlert(LegacyAlerts." + field : "Alert(LegacyAlerts." + field;
                if (a.Id == LegacyAlerts.ContextShow) call = "Alert(Features.Activity.LegacyAlerts." + field;
                if (!Norm(line).Contains(call + ",")) wrong.Add(a.Id);
            }
            Check("AR-P13-1", "Fiecare alertă trece prin Alert / ToolAlert / ShowInteractive cu id-ul ei din tabel", wrong.Count == 0, string.Join(", ", wrong));

            string notch = Src(LegacyAlerts.Notch), act = Src("Features/Activity/NotchWindow.Activity.cs");
            int direct = Regex.Matches(Norm(NoComments(notch)) + Norm(NoComments(Src(LegacyAlerts.Updates))) + Norm(NoComments(Src(LegacyAlerts.Context))), @"\bShowLive\(").Count;
            string alertBody = Norm(NoComments(MethodBody(act, "private bool Alert(")));
            Check("AR-P13-2", "ShowLive e apelat doar de Alert (comutator oprit: primul rând, neschimbat) și de prezentator",
                  direct == 1 /* its declaration */ && alertBody.StartsWith("{ if (!_activityOn || _activity == null) return ShowLive(content, w, h, ms, important);", StringComparison.Ordinal) &&
                  Regex.Matches(Norm(NoComments(act)), @"\bShowLive\(").Count == 3, direct + " / " + alertBody);
            string endLive = Norm(NoComments(MethodBody(notch, "private void EndLive()")));
            string collapse = Norm(NoComments(MethodBody(notch, "private void Collapse()")));
            string volume = Norm(NoComments(MethodBody(notch, "private void LiveVolume(int v, bool muted)")));
            Check("AR-P13-3", "Legăturile P13 din notch sunt câte un rând: la finalul EndLive și Collapse, în LiveVolume înainte de cronometrul vechi, după schimbarea ecranului complet; fără ele, corpurile vechi rămân",
                  Count(endLive, EndLiveHook) == 1 && endLive.EndsWith(EndLiveHook + " }", StringComparison.Ordinal) &&
                  Count(collapse, "ActivityNotchClosed();") == 1 && collapse.EndsWith("UpdateVisualizer(); ActivityNotchClosed(); }", StringComparison.Ordinal) &&
                  volume.Contains("if (ActivityTouch(LegacyAlerts.Volume)) return; _liveTimer.Stop(); _liveTimer.Start(); return; }") &&
                  Norm(NoComments(notch)).Contains("if (hidden != _hidden) { _hidden = hidden; ApplyHidden(); ActivityFullscreenChanged(); }") &&
                  Count(Norm(NoComments(notch)), "Activity") == 5 /* the using line and the four hooks */);
        }

        /// <summary>What only the legacy path does (the Activity Manager has its own rules for it, see ActivityTests).</summary>
        static void LegacyOnlyCharacterization()
        {
            // the newest alert always replaces the current one, whatever it is
            var s = new LegacySink();
            s.Show(LegacyAlerts.Find(LegacyAlerts.BatteryLow)); s.Sec(1);
            s.Show(LegacyAlerts.Find(LegacyAlerts.Track));
            Check("AC-vechi-inlocuire", "Calea veche: alerta nouă o înlocuiește imediat pe cea afișată (și o baterie descărcată cu o piesă)", s.ShownId == LegacyAlerts.Track);

            // the song dedupe (NowPlaying), the same on both paths since it decides before any alert
            var announced = new Dictionary<string, DateTime>();
            var last = DateTime.MinValue; var t0 = new DateTime(2026, 10, 5, 12, 0, 0);
            bool a1 = LegacyAlertRules.TrackAnnounce("a|x", t0, announced, ref last);
            bool a2 = LegacyAlertRules.TrackAnnounce("b|x", t0.AddSeconds(5), announced, ref last);
            bool a3 = LegacyAlertRules.TrackAnnounce("b|x", t0.AddSeconds(11), announced, ref last);
            bool a4 = LegacyAlertRules.TrackAnnounce("a|x", t0.AddMinutes(10), announced, ref last);
            bool a5 = LegacyAlertRules.TrackAnnounce("a|x", t0.AddMinutes(15).AddSeconds(1), announced, ref last);
            bool gate = LegacyAlertRules.TrackGate(true, true, false, false) && !LegacyAlertRules.TrackGate(true, false, false, false) &&
                        !LegacyAlertRules.TrackGate(true, true, true, false) && !LegacyAlertRules.TrackGate(true, true, false, true) && !LegacyAlertRules.TrackGate(false, true, false, false);
            Check("AC-lim-piesa", "Piesă nouă: aceeași piesă cel mult o dată în 15 minute, nimic mai des de 10 s; doar dacă cântă, notch-ul închis, sursa nu e fereastra din față",
                  a1 && !a2 && a3 && !a4 && a5 && gate, $"{a1} {a2} {a3} {a4} {a5} {gate}");
        }
    }
}
