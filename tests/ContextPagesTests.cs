using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using WinNotch.Core.Context;
using WinNotch.Core.Flags;
using WinNotch.Features.CommandBar;
using WinNotch.Features.ContextPages;
using WinNotch.Features.Smoke;

namespace WinNotch
{
    public static partial class T
    {
        /// <summary>
        /// P27 "Pagina după context": the pure decision (category → page, „—”, hidden / deleted pages, the 10-minute manual
        /// window, switch off, empty snapshot), the real engine as the only source, the settings, the smoke-test commands and
        /// the WPF hooks pinned in the source.
        /// </summary>
        static void ContextPagesTests()
        {
            var t0 = new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);
            var all = new[] { "home", "system", "devices", "tools", "a1b2c3" };
            Dictionary<string, string> M(params (string Cat, string Page)[] rows) => rows.ToDictionary(r => r.Cat, r => r.Page);

            // ---- the switch
            var info = FeatureCatalog.Find(ContextPageRules.FeatureId);
            var normal = new FeatureFlags(AppSettings.NewFeatures());
            var safe = new FeatureFlags(AppSettings.NewFeatures(), safeMode: true);
            var onStore = AppSettings.NewFeatures(); onStore[ContextPageRules.FeatureId] = true;
            Check("CP1", "Comutatorul „context-pages”: în catalog, Experimental, oprit implicit, oprit în mod sigur chiar dacă e pornit; același id ca FeatureCatalog.ContextPages",
                  info != null && FeatureCatalog.ContextPages == ContextPageRules.FeatureId && info.Stage == FeatureStage.Experimental && !info.DefaultOn &&
                  !normal.IsEnabled(ContextPageRules.FeatureId) && !new FeatureFlags(onStore, safeMode: true).IsEnabled(ContextPageRules.FeatureId) &&
                  new FeatureFlags(onStore).IsEnabled(ContextPageRules.FeatureId) && !safe.IsEnabled(ContextPageRules.FeatureId) && info.Name == "Pagina după context");

            // ---- categories
            var expected = Enum.GetValues(typeof(AppCategory)).Cast<AppCategory>().Where(c => c != AppCategory.Other).ToList();
            Check("CP2", "Categoriile: toate cele ale motorului de context, fără Other (Dev, Browser, Meeting, Game, Media, Office, Creator); cheia e numele, citită fără majuscule",
                  ContextPageRules.Categories.SequenceEqual(expected) && ContextPageRules.Categories.Count == 7 &&
                  ContextPageRules.Categories.All(c => ContextPageRules.ParseKey(ContextPageRules.Key(c)) == c && ContextPageRules.ParseKey(ContextPageRules.Key(c).ToLowerInvariant()) == c) &&
                  ContextPageRules.ParseKey("Other") == null && ContextPageRules.ParseKey("nimic") == null && ContextPageRules.ParseKey(null) == null && ContextPageRules.ParseKey(" ") == null &&
                  ContextPageRules.Key(AppCategory.Dev) == "Dev");

            // ---- each category → its page
            var pages = new[] { "home", "system", "devices", "tools", "a1b2c3", "home", "system" };
            var full = ContextPageRules.Categories.Select((c, i) => (ContextPageRules.Key(c), pages[i])).ToArray();
            var fullMap = M(full);
            var perCat = ContextPageRules.Categories.Select((c, i) => ContextPageRules.Resolve(c, fullMap, all, null, t0) == pages[i]).ToList();
            Check("CP3", "Fiecare categorie deschide pagina ei (inclusiv o pagină a ta, după id)", perCat.All(x => x) && perCat.Count == 7);
            foreach (var c in ContextPageRules.Categories)
            {
                var only = M((ContextPageRules.Key(c), "devices"));
                bool mine = ContextPageRules.Resolve(c, only, all, null, t0) == "devices";
                bool others = ContextPageRules.Categories.Where(o => o != c).All(o => ContextPageRules.Resolve(o, only, all, null, t0) == null);
                Check("CP3." + ContextPageRules.Key(c), "Doar „" + ContextPageRules.Key(c) + "” are pagină: ea se deschide pe „devices”, celelalte categorii nu schimbă nimic", mine && others);
            }

            // ---- „—” and Other
            Check("CP4", "„—” (cheie lipsă, text gol sau spații) și categoria Other → nicio schimbare (null)",
                  ContextPageRules.Resolve(AppCategory.Dev, M(), all, null, t0) == null &&
                  ContextPageRules.Resolve(AppCategory.Dev, M(("Dev", "")), all, null, t0) == null &&
                  ContextPageRules.Resolve(AppCategory.Dev, M(("Dev", "   ")), all, null, t0) == null &&
                  ContextPageRules.Resolve(AppCategory.Dev, M(("Dev", null)), all, null, t0) == null &&
                  ContextPageRules.Resolve(AppCategory.Other, M(("Other", "home")), all, null, t0) == null &&
                  ContextPageRules.Resolve(AppCategory.Browser, M(("dev", "home")), all, null, t0) == null);

            // ---- hidden / deleted pages
            var visible = new[] { "home", "tools" };          // "system" hidden, "devices" hidden, "a1b2c3" deleted
            bool noThrow = true; string r5 = "x";
            try { r5 = ContextPageRules.Resolve(AppCategory.Dev, M(("Dev", "system")), visible, null, t0); } catch { noThrow = false; }
            Check("CP5", "Pagină ascunsă sau ștearsă → ignorată în tăcere (null, fără excepție); mapare sau listă lipsă → null; id-ul se compară exact",
                  noThrow && r5 == null && ContextPageRules.Resolve(AppCategory.Dev, M(("Dev", "a1b2c3")), visible, null, t0) == null &&
                  ContextPageRules.Resolve(AppCategory.Dev, M(("Dev", "tools")), visible, null, t0) == "tools" &&
                  ContextPageRules.Resolve(AppCategory.Dev, null, visible, null, t0) == null && ContextPageRules.Resolve(AppCategory.Dev, M(("Dev", "tools")), null, null, t0) == null &&
                  ContextPageRules.Resolve(AppCategory.Dev, M(("Dev", "Tools")), visible, null, t0) == null && ContextPageRules.Resolve(AppCategory.Dev, M(("Dev", "home")), Array.Empty<string>(), null, t0) == null);

            // ---- the 10-minute window
            var dev = M(("Dev", "system"));
            string At(TimeSpan after) => ContextPageRules.Resolve(AppCategory.Dev, dev, all, t0, t0 + after);
            Check("CP6", "Alegerea manuală: respectată la 0 s, 5 min și 9:59; la 10:00 fix și după, contextul alege din nou; fără alegere manuală, alege mereu",
                  At(TimeSpan.Zero) == null && At(TimeSpan.FromMinutes(5)) == null && At(new TimeSpan(0, 9, 59)) == null &&
                  At(TimeSpan.FromMilliseconds(599_999)) == null && At(TimeSpan.FromMinutes(10)) == "system" && At(new TimeSpan(0, 10, 1)) == "system" &&
                  At(TimeSpan.FromHours(3)) == "system" && ContextPageRules.Resolve(AppCategory.Dev, dev, all, null, t0) == "system" &&
                  ContextPageRules.ManualHold == TimeSpan.FromMinutes(10));
            Check("CP7", "ManualChoiceHolds: null → nu; 9:59 → da; 10:00 → nu; ceasul dat înapoi (acum înaintea alegerii) → da",
                  !ContextPageRules.ManualChoiceHolds(null, t0) && ContextPageRules.ManualChoiceHolds(t0, t0 + new TimeSpan(0, 9, 59)) &&
                  !ContextPageRules.ManualChoiceHolds(t0, t0 + TimeSpan.FromMinutes(10)) && ContextPageRules.ManualChoiceHolds(t0, t0 - TimeSpan.FromMinutes(1)));

            // ---- the chooser with an injected clock
            var now = t0;
            var chooser = new ContextPageChooser(() => now);
            var devSnap = new ContextSnapshot { ForegroundProcess = "code", ForegroundCategory = AppCategory.Dev };
            string first = chooser.OnOpen(true, devSnap, dev, all);
            now = t0.AddMinutes(1); chooser.ManualChoice();
            now = t0.AddMinutes(1) + new TimeSpan(0, 9, 59); string held = chooser.OnOpen(true, devSnap, dev, all);
            now = t0.AddMinutes(11); string back = chooser.OnOpen(true, devSnap, dev, all);
            Check("CP8", "ContextPageChooser (ceas injectat): alege la prima deschidere; după un click pe tab ține 9:59; la 10:00 alege iar; ora alegerii e cea a ceasului",
                  first == "system" && held == null && back == "system" && chooser.LastManualUtc == t0.AddMinutes(1) && new ContextPageChooser().LastManualUtc == null);

            // ---- switch off, empty snapshot
            Check("CP9", "Comutator oprit → nicio schimbare; snapshot Empty (motor oprit / mod sigur) sau null → nicio schimbare",
                  ContextPageRules.Choose(false, devSnap, dev, all, null, t0) == null && ContextPageRules.Choose(true, devSnap, dev, all, null, t0) == "system" &&
                  ContextPageRules.Choose(true, ContextSnapshot.Empty, M(full), all, null, t0) == null && ContextPageRules.Choose(true, null, M(full), all, null, t0) == null &&
                  chooser.OnOpen(false, devSnap, dev, all) == null);

            // ---- which category the snapshot means
            var meetInBrowser = new ContextSnapshot { ForegroundProcess = "chrome", ForegroundCategory = AppCategory.Browser, MeetingActive = true, MeetingApp = "Meet" };
            var unknownGame = new ContextSnapshot { ForegroundProcess = "joc", ForegroundCategory = AppCategory.Other, Fullscreen = FullscreenKind.Game };
            var video = new ContextSnapshot { ForegroundProcess = "chrome", ForegroundCategory = AppCategory.Browser, Fullscreen = FullscreenKind.Video };
            Check("CP10", "Categoria: o întâlnire în curs câștigă (și Meet în browser), apoi un joc pe tot ecranul necunoscut, apoi aplicația din față; Empty → Other",
                  ContextPageRules.EffectiveCategory(meetInBrowser) == AppCategory.Meeting && ContextPageRules.EffectiveCategory(unknownGame) == AppCategory.Game &&
                  ContextPageRules.EffectiveCategory(video) == AppCategory.Browser && ContextPageRules.EffectiveCategory(devSnap) == AppCategory.Dev &&
                  ContextPageRules.EffectiveCategory(ContextSnapshot.Empty) == AppCategory.Other && ContextPageRules.EffectiveCategory(null) == AppCategory.Other &&
                  ContextPageRules.Choose(true, meetInBrowser, M(("Meeting", "devices"), ("Browser", "home")), all, null, t0) == "devices");

            // ---- the real context engine is the only source
            var rig = new Rig().Started();
            rig.Fg.Set(Fg("Code.exe", "secret.cs - Visual Studio Code"));
            rig.Clock.Ms(400);
            string fromEngine = ContextPageRules.Choose(true, rig.Engine.Snapshot, dev, all, null, t0);
            rig.Fg.Set(Fg("chrome.exe"));
            rig.Clock.Ms(400);
            string browser = ContextPageRules.Choose(true, rig.Engine.Snapshot, dev, all, null, t0);
            rig.Flags.Set(ContextEngine.FeatureId, false);
            string engineOff = ContextPageRules.Choose(true, rig.Engine.Snapshot, M(full), all, null, t0);
            Check("CP11", "Cu motorul real: VS Code în față → Dev → pagina lui; Chrome → Browser fără pagină → null; „context-engine” oprit → snapshot Empty → nicio schimbare",
                  fromEngine == "system" && browser == null && engineOff == null && rig.Engine.Snapshot == ContextSnapshot.Empty);
            rig.Engine.Dispose();
            var rigOff = new Rig(on: false).Started();
            Check("CP12", "Motorul pornit oprit (sau în mod sigur): snapshot Empty → nicio schimbare, oricare ar fi maparea",
                  ContextPageRules.Choose(true, rigOff.Engine.Snapshot, M(full), all, null, t0) == null &&
                  ContextPageRules.Choose(true, new Rig(safeMode: true).Started().Engine.Snapshot, M(full), all, null, t0) == null);
            rigOff.Engine.Dispose();

            // ---- the smoke test's injection goes through the engine's normal flush
            var rs = new Rig().Started();
            rs.Fg.Set(Fg("chrome.exe"));
            rs.Clock.Ms(400);
            rs.Events.Clear();
            bool applied = rs.Engine.ForceCategoryForSmoke(AppCategory.Dev);
            var beforeFlush = rs.Engine.Snapshot.ForegroundCategory;
            rs.Clock.Ms(400);
            var forcedCat = rs.Engine.Snapshot.ForegroundCategory;
            bool raised = rs.Events.Count == 1 && rs.Events[0].Has(ContextField.Foreground) && rs.Events[0].New.ForegroundCategory == AppCategory.Dev;
            rs.Fg.Set(Fg("explorer.exe"));                 // the real app in front changes: the forced category stays
            rs.Clock.Ms(400);
            var stays = rs.Engine.Snapshot.ForegroundCategory;
            rs.Engine.ForceCategoryForSmoke(null);
            rs.Clock.Ms(400);
            var real = rs.Engine.Snapshot.ForegroundCategory;
            rs.Flags.Set(ContextEngine.FeatureId, false);
            bool offFalse = !rs.Engine.ForceCategoryForSmoke(AppCategory.Game) && rs.Engine.Snapshot == ContextSnapshot.Empty;
            rs.Flags.Set(ContextEngine.FeatureId, true);
            rs.Clock.Ms(400);
            var afterStart = rs.Engine.Snapshot.ForegroundCategory;
            Check("CP13", "Injecția testului de fum (ForceCategoryForSmoke): trece prin debounce-ul normal (Changed cu Foreground), rămâne peste aplicația reală, null revine la cea reală; motor oprit → false, aplicată la pornire",
                  applied && beforeFlush == AppCategory.Browser && forcedCat == AppCategory.Dev && raised && stays == AppCategory.Dev && real == AppCategory.Other &&
                  offFalse && afterStart == AppCategory.Game && !rs.Log.Any(l => l.Contains("secret")));
            rs.Engine.Dispose();

            // ---- settings
            var fresh = new AppSettings();
            var old = JsonSerializer.Deserialize<AppSettings>("{\"Standby\":[\"music\"],\"Features\":{\"command-bar\":true}}"); old.NormalizeContextPages();
            var nul = JsonSerializer.Deserialize<AppSettings>("{\"ContextPages\":null}"); nul.NormalizeContextPages();
            Check("CP14", "Setări implicite: maparea e goală (totul „—”); settings.json vechi fără „ContextPages” sau cu null → gol, fără eroare",
                  fresh.ContextPages != null && fresh.ContextPages.Count == 0 && old.ContextPages != null && old.ContextPages.Count == 0 &&
                  nul.ContextPages != null && nul.ContextPages.Count == 0 &&
                  ContextPageRules.Categories.All(c => ContextPageRules.Resolve(c, fresh.ContextPages, all, null, t0) == null));
            var saved = new AppSettings();
            saved.ContextPages = saved.WithContextPage(AppCategory.Dev, "system");
            saved.ContextPages = saved.WithContextPage(AppCategory.Meeting, "a1b2c3");
            string json = JsonSerializer.Serialize(saved);
            var loaded = JsonSerializer.Deserialize<AppSettings>(json); loaded.NormalizeContextPages();
            Check("CP15", "Salvare și citire: id-urile paginilor (nu indici) sub numele categoriei; după citire, aceleași pagini",
                  json.Contains("\"ContextPages\":{\"Dev\":\"system\",\"Meeting\":\"a1b2c3\"}") && loaded.ContextPages.Count == 2 &&
                  loaded.ContextPages["Dev"] == "system" && loaded.ContextPages["Meeting"] == "a1b2c3" &&
                  ContextPageRules.Resolve(AppCategory.Meeting, loaded.ContextPages, all, null, t0) == "a1b2c3", json);
            var dirty = JsonSerializer.Deserialize<AppSettings>("{\"ContextPages\":{\"dev\":\" tools \",\"Other\":\"home\",\"Nimic\":\"home\",\"Game\":\"\",\"Media\":null,\"Office\":\"" + new string('x', 80) + "\"}}");
            dirty.NormalizeContextPages();
            var before = saved.ContextPages;
            var removed = saved.WithContextPage(AppCategory.Dev, null);
            var blank = saved.WithContextPage(AppCategory.Dev, "  ");
            Check("CP16", "Curățarea la citire: cheile scrise oricum devin canonice („dev” → „Dev”), Other / necunoscute / goale / prea lungi dispar; „—” scoate cheia fără să schimbe maparea veche",
                  dirty.ContextPages.Count == 1 && dirty.ContextPages["Dev"] == "tools" &&
                  !removed.ContainsKey("Dev") && removed.ContainsKey("Meeting") && !blank.ContainsKey("Dev") &&
                  before.ContainsKey("Dev") && !ReferenceEquals(before, removed),
                  string.Join(",", dirty.ContextPages.Select(kv => kv.Key + "=" + kv.Value)));

            // ---- the smoke-test commands and status
            Check("CP17", "Comenzi de fum P27: „fake-context <categorie|none>”, „set-context-page <categorie> <pagină|none>”; majusculele nu contează; numele categoriilor sunt cele din AppCategory",
                  SmokeMode.Parse("fake-context dev") is { Kind: SmokeCommandKind.FakeContext, Argument: "Dev" } &&
                  SmokeMode.Parse("  FAKE-CONTEXT   Meeting ") is { Kind: SmokeCommandKind.FakeContext, Argument: "Meeting" } &&
                  SmokeMode.Parse("fake-context none") is { Kind: SmokeCommandKind.FakeContext, Argument: "" } &&
                  SmokeMode.Parse("set-context-page dev devices") is { Kind: SmokeCommandKind.SetContextPage, Argument: "Dev", Page: "devices" } &&
                  SmokeMode.Parse("set-context-page office 0123456789abcdef0123456789abcdef") is { Kind: SmokeCommandKind.SetContextPage, Page: "0123456789abcdef0123456789abcdef" } &&
                  SmokeMode.Parse("set-context-page dev none") is { Kind: SmokeCommandKind.SetContextPage, Argument: "Dev", Page: "" } &&
                  SmokeMode.ContextCategories.Values.OrderBy(v => v).SequenceEqual(ContextPageRules.Categories.Select(ContextPageRules.Key).OrderBy(v => v)) &&
                  SmokeMode.ContextCategories.All(kv => kv.Key == kv.Value.ToLowerInvariant()));
            Check("CP18", "Comenzi de fum P27 invalide ignorate: Other, categorii necunoscute, căi, semne, prea lungi, cuvinte lipsă sau în plus",
                  new[] { "fake-context", "fake-context other", "fake-context joc", "fake-context dev now", "set-context-page dev", "set-context-page other home",
                          "set-context-page dev ../home", "set-context-page dev home/x", "set-context-page dev " + new string('a', 41), "set-context-page dev home now",
                          "set-context-page dev -home", "fake context dev" }.All(l => SmokeMode.Parse(l) == null));
            bool s1 = SmokeMode.TryParseStatus(SmokeMode.Status("Expanded", 720, 310, page: "devices", ctx: "Dev"), out var sm1, out _, out _, out var sx1, out var pg1, out var cx1);
            bool s2 = SmokeMode.TryParseStatus(SmokeMode.Status("Idle", 180, 32, cmd: 1, page: "0123456789abcdef0123456789abcdef", ctx: "Other"), out _, out _, out _, out var sx2, out var pg2, out var cx2);
            bool s3 = SmokeMode.TryParseStatus("mode=Idle;pill=180x32", out _, out _, out _, out _, out var pg3, out var cx3);
            Check("CP19", "Starea pentru UI Automation: „;page=<id>;ctx=<categorie>” la final, se citesc înapoi; un text care nu e id e lăsat afară; formatele vechi merg mai departe",
                  s1 && sm1 == "Expanded" && pg1 == "devices" && cx1 == "Dev" && s2 && sx2["cmd"] == 1 && pg2.Length == 32 && cx2 == "Other" && s3 && pg3 == "" && cx3 == "" &&
                  SmokeMode.Status("Expanded", 720, 310, page: "devices", ctx: "Dev") == "mode=Expanded;pill=720x310;page=devices;ctx=Dev" &&
                  SmokeMode.Status("Idle", 180, 32, page: "a;b=c", ctx: "x y") == "mode=Idle;pill=180x32" && SmokeMode.Status("Idle", 180, 32, page: null) == "mode=Idle;pill=180x32" &&
                  !SmokeMode.TryParseStatus("mode=Idle;pill=180x32;ctx=Dev;page=home", out _, out _, out _) && SmokeMode.TryParseStatus("mode=Idle;pill=180x32;cmd=1;page=home", out _, out _, out _) &&
                  sx1["cmd"] == 0);

            // ---- the WPF side, pinned in the source
            string notch = Src("NotchWindow.xaml.cs");
            string expand = Norm(NoComments(MethodBody(notch, "private void Expand()")));
            string part = Src("Features/ContextPages/NotchWindow.ContextPages.cs");
            string partN = Norm(NoComments(part));
            Check("CP20", "Legătura din Expand e un rând, chiar înainte de „_mode = Mode.Expanded;” (pagina schimbată înainte să se arate); NotchWindow.xaml.cs nu mai are altceva P27",
                  expand != null && expand.Contains("_liveTimer.Stop(); ContextPagesOnOpen(); _mode = Mode.Expanded;") && Count(NoComments(notch), "ContextPages") == 1 &&
                  !Norm(NoComments(MethodBody(notch, "private void Collapse()"))).Contains("ContextPages"));
            string pagesSrc = Norm(Src("NotchWindow.Pages.cs"));
            Check("CP21", "Alegerea manuală: click-ul pe un tab (după ShowPane, când pagina chiar se schimbă) și o pagină nouă (NewPage) o marchează; NotchWindow.Pages.cs nu are alt cod P27",
                  pagesSrc.Contains("rb.Checked += (o, e) => { if (_pane != target && !(target == _home && _pane == _sources)) { ShowPane(target); ContextPagesManualChoice(); } };") &&
                  Norm(NoComments(MethodBody(Src("NotchWindow.Pages.cs"), "private void NewPage("))).Contains("ShowPane(UserPane(pg)); ContextPagesManualChoice();") &&
                  Count(pagesSrc, "ContextPages") == 2);
            string onOpen = Norm(NoComments(MethodBody(part, "private void ContextPagesOnOpen()")));
            Check("CP22", "La deschidere: comutatorul citit atunci, apoi doar ContextEngine.Current?.Snapshot (null → Empty); paginile ascunse nu sunt oferite; erorile → ReportError(\"context-pages\")",
                  onOpen.Contains("flags.IsEnabled(ContextPageRules.FeatureId)") && onOpen.Contains("ContextEngine.Current?.Snapshot ?? ContextSnapshot.Empty") &&
                  onOpen.Contains("_contextPages.OnOpen(true, snapshot, S.ContextPages, ContextPagesVisibleIds())") &&
                  onOpen.Contains("catch (Exception ex) { FeatureFlags.Current?.ReportError(ContextPageRules.FeatureId, ex); }") &&
                  partN.Contains("!S.HiddenPages.Contains(p.Id)") && partN.Contains("S.Pages.Where(p => !p.Hidden)") && partN.Contains("p.Id == id && !p.Hidden"));
            var srcFiles = new[] { "Features/ContextPages/NotchWindow.ContextPages.cs", "Features/ContextPages/ContextPages.cs", "Features/ContextPages/SettingsWindow.ContextPages.cs" };
            var forbidden = new[] { "Changed +=", "SetWinEventHook", "GetForegroundWindow", "ForegroundSource", "DispatcherTimer", "new Timer", "Thread.Sleep", "Task.Delay", "GetLastInputInfo", "Process.GetProcess" };
            var found = srcFiles.SelectMany(f => forbidden.Where(x => NoComments(Src(f)).Contains(x)).Select(x => f + ": " + x)).ToList();
            Check("CP23", "Fără surse proprii: nicio abonare, niciun hook, timer sau polling, nicio citire a ferestrei din față (doar snapshot-ul motorului)", found.Count == 0, string.Join(" | ", found));
            var logs = part.Split('\n').Where(l => l.Contains("App.Log(", StringComparison.Ordinal)).ToList();
            Check("CP24", "Log minim: un singur rând, doar categoria (fără titlu, proces sau id de pagină)",
                  logs.Count == 1 && logs[0].Contains("ContextPageRules.EffectiveCategory(snapshot)") && !Regex.IsMatch(logs[0], @"Title|ForegroundProcess|\bid\b|MediaApp"), string.Join(" | ", logs.Select(Norm)));

            // settings page and its action
            string xaml = Src("SettingsWindow.xaml");
            string settingsCs = Norm(Src("SettingsWindow.xaml.cs"));
            var opt = SettingsActions.Find("settings.context-pages");
            Check("CP25", "Setări: secțiunea „Pagina după context” cu ContextPageRows; construită în constructor, salvată la „Salvează”; acțiunea settings.context-pages se deschide la secțiune",
                  Regex.IsMatch(Norm(xaml), "Text=\"Pagina după context\"/> <TextBlock [^>]*/> <StackPanel x:Name=\"ContextPageRows\"/>") &&
                  Count(settingsCs, "BuildContextPages();") == 1 && Count(settingsCs, "_s.ContextPages = ContextPagesChosen();") == 1 &&
                  opt != null && opt.Target == "ContextPageRows" && opt.SectionOnly && opt.Section == "Pagina după context" &&
                  Norm(Src("AppSettings.cs")).Contains("s.NormalizeFeatures(); s.NormalizeContextPages();"));
            string settingsPart = Norm(NoComments(Src("Features/ContextPages/SettingsWindow.ContextPages.cs")));
            Check("CP26", "Setări: „—” (gol) e prima alegere; fiecare categorie are rândul ei cu numele în română; paginile după id (Catalog.Standard + paginile tale), cele ascunse marcate",
                  settingsPart.Contains("box.Items.Add(new ComboBoxItem { Tag = \"\", Content = \"—\" });") && settingsPart.Contains("foreach (var cat in ContextPageRules.Categories)") &&
                  settingsPart.Contains("Features.Context.ContextActions.CategoryName(cat)") && settingsPart.Contains("Tag = id") && settingsPart.Contains("(ascunsă)") &&
                  settingsPart.Contains("Catalog.Standard.Select(") && settingsPart.Contains("_s.Pages.Select(") && settingsPart.Contains("return AppSettings.CleanContextPages(map);"));

            // the smoke test: the injection only from smoke mode, the step once
            var appSources = Directory.EnumerateFiles(RepoRoot(), "*.cs", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(RepoRoot(), f).Replace('\\', '/'))
                .Where(f => !f.StartsWith("tests/") && !f.Contains("/obj/") && !f.Contains("/bin/") && !f.StartsWith("obj/") && !f.StartsWith(".dotnet/") && !f.StartsWith("publish/"))
                .ToList();
            var callers = appSources.Where(f => Src(f).Contains("ForceCategoryForSmoke(")).ToList();
            string smokeSide0 = Src("Features/Smoke/NotchWindow.Smoke.cs");
            string smokeSide = Norm(NoComments(smokeSide0));
            string smokeProgram = Src("tests/WinNotch.Smoke/SmokeProgram.cs");
            Check("CP27", "Injecția de context: definită în motor (internal), apelată doar din NotchWindow.Smoke.cs (comenzi citite doar cu --smoke); starea arată pagina și categoria",
                  callers.OrderBy(f => f).SequenceEqual(new[] { "Core/Context/ContextEngine.cs", "Features/Smoke/NotchWindow.Smoke.cs" }) &&
                  Src("Core/Context/ContextEngine.cs").Contains("internal bool ForceCategoryForSmoke(AppCategory? category)") &&
                  smokeSide.Contains("case SmokeCommandKind.FakeContext: SmokeFakeContext(c.Argument); break;") &&
                  smokeSide.Contains("case SmokeCommandKind.SetContextPage: SmokeSetContextPage(c.Argument, c.Page); break;") &&
                  smokeSide.Contains("CurrentPageId(), ctx)") && smokeSide.Contains("ContextPageRules.EffectiveCategory(snap).ToString()") &&
                  Norm(NoComments(MethodBody(smokeSide0, "private void SmokeFakeContext("))).Contains("if (!SmokeMode.On) return;") &&
                  Norm(NoComments(MethodBody(smokeSide0, "private void SmokeSetContextPage("))).Contains("if (!SmokeMode.On) return;"),
                  string.Join(",", callers));
            Check("CP28", "Testul de fum P27 rulează o singură dată (doar cu activity-manager oprit) și o spune; deschide cu Win+Alt+N și cere pagina mapată; repune starea",
                  smokeProgram.Contains("if (!_activityOn) Run(step = \"Pagina după context") && smokeProgram.Contains("SKIP  Pagina după context") &&
                  smokeProgram.Contains("Command(\"fake-context dev\")") && smokeProgram.Contains("Command(\"set-context-page dev \" + ContextTargetPage)") &&
                  smokeProgram.Contains("Command(\"toggle feature \" + ContextPagesFeature)") && smokeProgram.Contains("Command(\"fake-context none\")") &&
                  smokeProgram.Contains("Command(\"set-context-page dev none\")") && smokeProgram.Contains("s.Page == ContextTargetPage"));
        }
    }
}
