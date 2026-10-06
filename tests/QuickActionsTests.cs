using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using WinNotch.Core.Actions;
using WinNotch.Core.Context;
using WinNotch.Core.Flags;
using WinNotch.Features.Actions;
using WinNotch.Features.Activity;
using WinNotch.Features.CommandBar;
using WinNotch.Features.Context;
using WinNotch.Features.QuickActions;
using WinNotch.Features.Smoke;

namespace WinNotch
{
    public static partial class T
    {
        /// <summary>A catalog over a hand-built list (for the filtering tests): availability per id, all on by default.</summary>
        sealed class FakeQuickCatalog : IQuickActionCatalog
        {
            public readonly Dictionary<string, ActionDescriptor> Actions = new Dictionary<string, ActionDescriptor>(StringComparer.Ordinal);
            public readonly HashSet<string> Unavailable = new HashSet<string>(StringComparer.Ordinal);
            public FakeQuickCatalog Add(ActionDescriptor a) { Actions[a.Id] = a; return this; }
            public ActionDescriptor Get(string id) => id != null && Actions.TryGetValue(id, out var a) ? a : null;
            public IEnumerable<ActionDescriptor> WithPrefix(string p) => Actions.Values.Where(a => a.Id.StartsWith(p, StringComparison.Ordinal)).OrderBy(a => a.Id, StringComparer.Ordinal);
            public readonly HashSet<string> Off = new HashSet<string>(StringComparer.Ordinal);
            public readonly List<string> AvailabilityAsked = new List<string>();
            public bool FeatureOn(ActionDescriptor a) => a != null && !Off.Contains(a.Id);
            public bool IsAvailable(ActionDescriptor a) { AvailabilityAsked.Add(a?.Id); return a != null && !Unavailable.Contains(a.Id); }
        }

        sealed class FakeQuickHost : IQuickActionsHost
        {
            public int Calls;
            public int ShowHiddenAgain() { Calls++; return 2; }
        }

        /// <summary>
        /// P20 "Quick Actions": the rule table (data), the pure evaluator (only usable Safe actions, the first matching rule,
        /// Empty / switch off → nothing), the unasked suggestions (Activity Manager only, one in 10 minutes, „Nu mai arăta”),
        /// the new actions, the settings, the smoke-test commands and the WPF hooks pinned in the source.
        /// </summary>
        static void QuickActionsTests()
        {
            var t0 = new DateTime(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc);
            ActionDescriptor Act(string id, ActionSafety safety = ActionSafety.Safe, ActionInvoker? inv = null, string feature = null, params ActionParameter[] ps)
            {
                var d = new ActionDescriptor(id, "Test " + id, (a, ct) => ActionResult.OkTask()) { Safety = safety, FeatureId = feature, Parameters = ps };
                return inv == null ? d : new ActionDescriptor(id, "Test " + id, (a, ct) => ActionResult.OkTask()) { Safety = safety, FeatureId = feature, Parameters = ps, AllowedInvokers = inv.Value };
            }

            // ---- the switch
            var info = FeatureCatalog.Find(QuickActionRules.FeatureId);
            var onStore = AppSettings.NewFeatures(); onStore[QuickActionRules.FeatureId] = true;
            Check("QA1", "Comutatorul „quick-actions”: în catalog, Experimental, oprit implicit, oprit în mod sigur chiar dacă e pornit; același id ca FeatureCatalog.QuickActions",
                  info != null && FeatureCatalog.QuickActions == QuickActionRules.FeatureId && info.Stage == FeatureStage.Experimental && !info.DefaultOn &&
                  !new FeatureFlags(AppSettings.NewFeatures()).IsEnabled(QuickActionRules.FeatureId) && new FeatureFlags(onStore).IsEnabled(QuickActionRules.FeatureId) &&
                  !new FeatureFlags(onStore, safeMode: true).IsEnabled(QuickActionRules.FeatureId) && info.Name == "Quick Actions" && info.Description.Contains("căști"));

            // ---- the table
            var table = QuickActionRules.Table;
            Check("QA2", "Tabelul: id-uri unice și stabile (litere mici, cratime), titlu în română, condiție, câmpurile citite, 2–4 acțiuni, fiecare cu id SAU prefix",
                  table.Count == 4 && table.Select(r => r.Id).Distinct().Count() == table.Count && table.All(r => QuickActionRules.IsValidRuleId(r.Id)) &&
                  table.All(r => !string.IsNullOrWhiteSpace(r.Title) && r.When != null && r.Fields != ContextField.None && r.Actions.Count >= 2 && r.Actions.Count <= 4) &&
                  table.SelectMany(r => r.Actions).All(a => string.IsNullOrEmpty(a.Id) != string.IsNullOrEmpty(a.Prefix)) &&
                  table.Select(r => r.Id).SequenceEqual(new[] { "meeting-headphones", "battery-low", "media-playing", "usb-drive" }) &&
                  table.All(r => QuickActionRules.Find(r.Id) == r) && QuickActionRules.Find("nimic") == null,
                  string.Join(",", table.Select(r => r.Id + ":" + r.Actions.Count)));

            var host = new FakeBuiltInHost { Drives = new List<DriveItem> { new DriveItem { Root = "E:\\", Name = "SanDisk" } } };
            var flagsOn = new FeatureFlags(onStore);
            var logs = new List<string>();
            var reg = new ActionRegistry(flagsOn, log: logs.Add);
            BuiltInActions.Register(reg, host);
            var cat = new RegistryQuickActionCatalog(reg, flagsOn);
            var missing = table.SelectMany(r => r.Actions.Select(a => (r.Id, a))).Where(x => x.a.Id != null ? reg.Get(x.a.Id) == null : !cat.WithPrefix(x.a.Prefix).Any())
                               .Select(x => x.Item1 + "/" + (x.a.Id ?? x.a.Prefix + "*")).ToList();
            var badArgs = table.SelectMany(r => r.Actions).Where(a => a.Id != null && a.Args.Count > 0)
                               .Where(a => a.Args.Any(kv => !(reg.Get(a.Id).Parameters.FirstOrDefault(p => p.Name == kv.Key)?.TryConvert(kv.Value, out _, out _) ?? false))).Select(a => a.Id).ToList();
            Check("QA3", "Fiecare acțiune din tabel există în BuiltInActions (prefixele: „device.open-” / „device.eject-” pentru un stick); parametrii dați sunt valizi („Volum 40%”)",
                  missing.Count == 0 && badArgs.Count == 0 && table[0].Actions[1].Args["valoare"] == "40",
                  string.Join(", ", missing.Concat(badArgs)));

            // ---- contexts: each rule matches its own and none of the others'
            var meeting = new ContextSnapshot { MeetingActive = true, MeetingApp = "Teams", AudioOutput = AudioOutputKind.Headphones };
            var meetingBt = meeting with { AudioOutput = AudioOutputKind.Bluetooth };
            var meetingSpk = meeting with { AudioOutput = AudioOutputKind.Speakers };
            var battery = new ContextSnapshot { HasBattery = true, OnBattery = true, BatteryPercent = 15 };
            var media = new ContextSnapshot { MediaPlaying = true, MediaApp = "Spotify" };
            var usb = new ContextSnapshot { UsbDriveConnected = true };
            var own = new Dictionary<string, ContextSnapshot> { ["meeting-headphones"] = meeting, ["battery-low"] = battery, ["media-playing"] = media, ["usb-drive"] = usb };
            foreach (var rule in table)
            {
                bool mine = QuickActionRules.Matches(rule, own[rule.Id]);
                var others = own.Where(kv => kv.Key != rule.Id && QuickActionRules.Matches(rule, kv.Value)).Select(kv => kv.Key).ToList();
                Check("QA4." + rule.Id, "Regula „" + rule.Id + "” se potrivește pe contextul ei și pe niciun altul", mine && others.Count == 0, string.Join(",", others));
            }
            Check("QA5", "Limitele condițiilor: căști Bluetooth da, boxe nu; baterie 19% da, 20% / la priză / necunoscută / PC fără baterie nu; o condiție care aruncă = nu",
                  QuickActionRules.Matches(table[0], meetingBt) && !QuickActionRules.Matches(table[0], meetingSpk) &&
                  !QuickActionRules.Matches(table[0], new ContextSnapshot { AudioOutput = AudioOutputKind.Headphones }) &&
                  QuickActionRules.Matches(table[1], battery with { BatteryPercent = 19 }) && !QuickActionRules.Matches(table[1], battery with { BatteryPercent = 20 }) &&
                  !QuickActionRules.Matches(table[1], battery with { OnBattery = false }) && !QuickActionRules.Matches(table[1], battery with { BatteryPercent = -1 }) &&
                  !QuickActionRules.Matches(table[1], new ContextSnapshot { OnBattery = true, BatteryPercent = 5 }) &&
                  !QuickActionRules.Matches(new QuickActionRule { Id = "x", When = s => throw new InvalidOperationException() }, meeting) &&
                  !QuickActionRules.Matches(table[0], null) && !QuickActionRules.Matches(null, meeting));

            // ---- what each context shows, with the real built-in actions
            string Ids(QuickActionChoice c) => c == null ? "-" : c.Rule.Id + ":" + string.Join("+", c.Items.Select(i => i.ActionId));
            var cMeeting = QuickActionRules.ForHover(true, meeting, cat);
            Check("QA6", "Întâlnire cu căști → „Mută / pornește microfonul” (audio.mute-mic) și „Volum 40%” (audio.volume-set cu valoare=40)",
                  Ids(cMeeting) == "meeting-headphones:audio.mute-mic+audio.volume-set" && cMeeting.Items[0].Label == "Mută / pornește microfonul" && cMeeting.Items[1].Label == "Volum 40%" &&
                  cMeeting.Items[1].Args["valoare"] == "40" && cMeeting.Items.All(i => i.Icon.Length > 0 && i.Title.Length > 0), Ids(cMeeting));
            Check("QA7", "Baterie sub 20% → economisire și luminozitate; media → pauză și următoarea; stick → doar „Deschide” (scoaterea cere confirmare, deci nu e Quick Action)",
                  Ids(QuickActionRules.ForHover(true, battery, cat)) == "battery-low:settings.battery-saver+settings.display" &&
                  Ids(QuickActionRules.ForHover(true, media, cat)) == "media-playing:media.play-pause+media.next" &&
                  Ids(QuickActionRules.ForHover(true, usb, cat)) == "usb-drive:device.open-e" &&
                  QuickActionRules.ForHover(true, usb, cat).Items[0].Label == "Deschide SanDisk (E:)",
                  Ids(QuickActionRules.ForHover(true, usb, cat)));
            var both = meeting with { MediaPlaying = true, UsbDriveConnected = true };
            host.HasMedia = false;
            var noSong = QuickActionRules.ForHover(true, media with { UsbDriveConnected = true }, cat);
            var onlyMedia = QuickActionRules.ForHover(true, media, cat);
            host.HasMedia = true;
            Check("QA8", "Prioritatea din tabel: întâlnirea câștigă în fața muzicii și a stick-ului; o regulă fără nicio acțiune disponibilă cedează locul următoarei (sau nimic)",
                  Ids(QuickActionRules.ForHover(true, both, cat)) == "meeting-headphones:audio.mute-mic+audio.volume-set" && Ids(noSong) == "usb-drive:device.open-e" && onlyMedia == null,
                  Ids(noSong) + " / " + Ids(onlyMedia));

            // ---- filtering: missing, unavailable, Confirm / Dangerous, invokers, feature, parameters
            var fc = new FakeQuickCatalog().Add(Act("audio.mute-mic")).Add(Act("audio.volume-set", ps: ActionParameter.Percent("valoare", "Volum")));
            var only1 = QuickActionRules.ForHover(true, meeting, new FakeQuickCatalog().Add(Act("audio.mute-mic")));
            fc.Unavailable.Add("audio.mute-mic");
            var unav = QuickActionRules.ForHover(true, meeting, fc);
            fc.Unavailable.Add("audio.volume-set");
            var none = QuickActionRules.ForHover(true, meeting, fc);
            Check("QA9", "Acțiuni lipsă sau indisponibile nu apar; regula rămâne cu ce e disponibil (minim 1); cu nimic disponibil → niciun rând",
                  Ids(only1) == "meeting-headphones:audio.mute-mic" && Ids(unav) == "meeting-headphones:audio.volume-set" && none == null &&
                  QuickActionRules.MinActions == 1 && QuickActionRules.MaxActions == 4 && QuickActionRules.ForHover(true, meeting, new FakeQuickCatalog()) == null);
            var risky = new FakeQuickCatalog().Add(Act("audio.mute-mic", ActionSafety.Confirm)).Add(Act("audio.volume-set", ActionSafety.Dangerous, ps: ActionParameter.Percent("valoare", "Volum")));
            var ejectOnly = new FakeQuickCatalog().Add(Act("device.eject-e", ActionSafety.Confirm)).Add(Act("device.eject-f", ActionSafety.Dangerous));
            var pct = ActionParameter.Percent("valoare", "Volum");
            Check("QA10", "Confirm și Dangerous nu sunt niciodată Quick Actions (filtrate în evaluator, chiar disponibile): nici „Scoate stick-ul”",
                  QuickActionRules.ForHover(true, meeting, risky) == null && QuickActionRules.ForHover(true, usb, ejectOnly) == null &&
                  !QuickActionRules.Usable(Act("x.a", ActionSafety.Confirm), null, risky) && !QuickActionRules.Usable(Act("x.a", ActionSafety.Dangerous), null, risky) &&
                  QuickActionRules.Usable(Act("x.a"), null, risky) && !QuickActionRules.Resolve(table[3], cat).Any(i => i.ActionId.StartsWith("device.eject-", StringComparison.Ordinal)) &&
                  reg.Get("device.eject-e")?.Safety == ActionSafety.Confirm);
            var flagsOff = new FeatureFlags(AppSettings.NewFeatures());
            var regOff = new ActionRegistry(flagsOff);
            regOff.Register(Act("audio.mute-mic", feature: QuickActionRules.FeatureId));
            Check("QA11", "Doar ce poate porni Quick Actions: fără ActionInvoker.QuickAction nu; comutatorul acțiunii oprit nu; parametri lipsă, necunoscuți sau invalizi nu",
                  !QuickActionRules.Usable(Act("x.a", inv: ActionInvoker.UI | ActionInvoker.CommandBar), null, risky) &&
                  QuickActionRules.Usable(Act("x.a", inv: ActionInvoker.QuickAction), null, risky) &&
                  !new RegistryQuickActionCatalog(regOff, flagsOff).FeatureOn(regOff.Get("audio.mute-mic")) && new RegistryQuickActionCatalog(regOff, flagsOn).FeatureOn(regOff.Get("audio.mute-mic")) &&
                  QuickActionRules.ForHover(true, meeting, new RegistryQuickActionCatalog(regOff, flagsOff)) == null &&
                  !QuickActionRules.Usable(Act("x.v", ps: pct), null, risky) &&
                  QuickActionRules.Usable(Act("x.v", ps: pct), new Dictionary<string, string> { ["valoare"] = "40" }, risky) &&
                  !QuickActionRules.Usable(Act("x.v", ps: pct), new Dictionary<string, string> { ["valoare"] = "140" }, risky) &&
                  !QuickActionRules.Usable(Act("x.v", ps: pct), new Dictionary<string, string> { ["valoare"] = "40", ["alt"] = "1" }, risky) &&
                  !QuickActionRules.Usable(Act("x.n"), new Dictionary<string, string> { ["valoare"] = "40" }, risky) &&
                  !QuickActionRules.Usable(null, null, risky) && !QuickActionRules.Usable(Act("x.a"), null, null));
            var many = new QuickActionRule
            {
                Id = "multe", Title = "Multe", Fields = ContextField.Media, When = s => true,
                Actions = new[] { "a.a", "a.b", "a.a", "a.c", "a.d", "a.e" }.Select(i => new QuickActionRef { Id = i }).ToList(),
            };
            var manyCat = new FakeQuickCatalog();
            foreach (var i in new[] { "a.a", "a.b", "a.c", "a.d", "a.e" }) manyCat.Add(Act(i));
            var manyItems = QuickActionRules.Resolve(many, manyCat);
            Check("QA12", "Cel mult 4 butoane, fără dubluri, în ordinea din tabel; eticheta lipsă = titlul acțiunii",
                  manyItems.Select(i => i.ActionId).SequenceEqual(new[] { "a.a", "a.b", "a.c", "a.d" }) && manyItems[0].Label == "Test a.a");

            // ---- Empty, null, switch off
            Check("QA13", "Context Empty (motor oprit, mod sigur) sau null → niciun rând; nicio regulă nu se potrivește pe Empty",
                  QuickActionRules.ForHover(true, ContextSnapshot.Empty, cat) == null && QuickActionRules.ForHover(true, null, cat) == null &&
                  table.All(r => !QuickActionRules.Matches(r, ContextSnapshot.Empty)) && QuickActionRules.ForHover(true, meeting, null) == null);
            var rigOff = new Rig(on: false).Started();
            var rigSafe = new Rig(safeMode: true).Started();
            Check("QA14", "Comutatorul oprit → nimic (nici rând, nici sugestie); motorul de context oprit sau în mod sigur → snapshot Empty → nimic",
                  QuickActionRules.ForHover(false, meeting, cat) == null &&
                  QuickActionRules.NewlyMatching(false, true, ContextSnapshot.Empty, meeting, ContextField.Meeting, cat, null) == null &&
                  QuickActionRules.ForHover(true, rigOff.Engine.Snapshot, cat) == null && QuickActionRules.ForHover(true, rigSafe.Engine.Snapshot, cat) == null);
            rigOff.Engine.Dispose(); rigSafe.Engine.Dispose();

            // ---- unasked suggestions
            var mf = ContextField.Meeting | ContextField.AudioOutput;
            Check("QA15", "Sugestiile doar cu Activity Manager pornit: oprit → nimic, pornit → regula întâlnirii",
                  QuickActionRules.NewlyMatching(true, false, ContextSnapshot.Empty, meeting, mf, cat, null) == null &&
                  QuickActionRules.NewlyMatching(true, true, ContextSnapshot.Empty, meeting, mf, cat, null)?.Rule.Id == "meeting-headphones" &&
                  QuickActionRules.NewlyMatching(true, true, null, meeting, mf, cat, null)?.Rule.Id == "meeting-headphones");
            Check("QA16", "Doar o regulă care abia începe să se potrivească, pentru câmpurile ei; muzica și bateria (care au deja alerta lor) nu sunt sugerate; stick-ul da",
                  QuickActionRules.NewlyMatching(true, true, meetingSpk, meeting, ContextField.AudioOutput, cat, null)?.Rule.Id == "meeting-headphones" &&
                  QuickActionRules.NewlyMatching(true, true, meetingBt, meeting, ContextField.AudioOutput, cat, null) == null &&
                  QuickActionRules.NewlyMatching(true, true, ContextSnapshot.Empty, meeting, ContextField.Foreground, cat, null) == null &&
                  QuickActionRules.NewlyMatching(true, true, ContextSnapshot.Empty, media, ContextField.Media, cat, null) == null &&
                  QuickActionRules.NewlyMatching(true, true, ContextSnapshot.Empty, battery, ContextField.Power, cat, null) == null &&
                  QuickActionRules.NewlyMatching(true, true, ContextSnapshot.Empty, usb, ContextField.UsbDrive, cat, null)?.Rule.Id == "usb-drive" &&
                  QuickActionRules.SuggestionFields() == (ContextField.Meeting | ContextField.AudioOutput | ContextField.UsbDrive) &&
                  table.Where(r => r.Suggest).Select(r => r.Id).SequenceEqual(new[] { "meeting-headphones", "usb-drive" }));

            var now = t0;
            var sug = new QuickActionSuggester(() => now);
            var o1 = sug.Consider(true, true, ContextSnapshot.Empty, meeting, mf, cat, null, out var ch1);
            var o1b = sug.Consider(true, true, ContextSnapshot.Empty, meeting, mf, cat, null, out _);        // not shown (dropped): still free
            sug.Shown();
            now = t0 + new TimeSpan(0, 9, 59);
            var o2 = sug.Consider(true, true, ContextSnapshot.Empty, usb, ContextField.UsbDrive, cat, null, out var ch2);
            now = t0 + TimeSpan.FromMinutes(10);
            var o3 = sug.Consider(true, true, ContextSnapshot.Empty, usb, ContextField.UsbDrive, cat, null, out _);
            var o4 = sug.Consider(true, false, ContextSnapshot.Empty, usb, ContextField.UsbDrive, cat, null, out var ch4);
            Check("QA17", "Cel mult o sugestie la 10 minute (ceas injectat), pentru orice regulă: 9:59 → amânată, 10:00 → da; una aruncată (nepostată) nu pornește cele 10 minute",
                  o1 == SuggestionOutcome.Suggest && ch1?.Rule.Id == "meeting-headphones" && o1b == SuggestionOutcome.Suggest && sug.LastShownUtc == t0 &&
                  o2 == SuggestionOutcome.TooSoon && ch2?.Rule.Id == "usb-drive" && o3 == SuggestionOutcome.Suggest && o4 == SuggestionOutcome.None && ch4 == null &&
                  QuickActionRules.SuggestionInterval == TimeSpan.FromMinutes(10) && QuickActionRules.IntervalOver(null, t0) &&
                  !QuickActionRules.IntervalOver(t0, t0 + TimeSpan.FromMilliseconds(599_999)) && QuickActionRules.IntervalOver(t0, t0 + TimeSpan.FromMinutes(10)) &&
                  !QuickActionRules.IntervalOver(t0, t0 - TimeSpan.FromMinutes(1)) && new QuickActionSuggester().LastShownUtc == null);

            // ---- „Nu mai arăta”
            var hidden = new List<string> { "meeting-headphones" };
            Check("QA18", "„Nu mai arăta” per regulă: regula ascunsă nu mai e sugerată (celelalte da), dar butoanele ei rămân la deschidere",
                  QuickActionRules.NewlyMatching(true, true, ContextSnapshot.Empty, meeting, mf, cat, hidden) == null &&
                  QuickActionRules.NewlyMatching(true, true, ContextSnapshot.Empty, usb, ContextField.UsbDrive, cat, hidden)?.Rule.Id == "usb-drive" &&
                  Ids(QuickActionRules.ForHover(true, meeting, cat)) == "meeting-headphones:audio.mute-mic+audio.volume-set" &&
                  new QuickActionSuggester(() => t0).Consider(true, true, ContextSnapshot.Empty, meeting, mf, cat, hidden, out _) == SuggestionOutcome.None);
            var fresh = new AppSettings();
            var oldJson = JsonSerializer.Deserialize<AppSettings>("{\"Standby\":[\"music\"]}"); oldJson.NormalizeQuickActions();
            var nulJson = JsonSerializer.Deserialize<AppSettings>("{\"QuickActionsHidden\":null}"); nulJson.NormalizeQuickActions();
            var dirty = JsonSerializer.Deserialize<AppSettings>("{\"QuickActionsHidden\":[\" usb-drive \",\"usb-drive\",\"Rău\",\"../x\",\"\",null,\"" + new string('a', 41) + "\",\"meeting-headphones\"]}");
            dirty.NormalizeQuickActions();
            var saved = new AppSettings();
            var before = saved.QuickActionsHidden;
            saved.QuickActionsHidden = saved.WithQuickActionHidden("meeting-headphones");
            saved.QuickActionsHidden = saved.WithQuickActionHidden("meeting-headphones");
            var bad = saved.WithQuickActionHidden("Nu e bun!");
            string json = JsonSerializer.Serialize(saved);
            var loaded = JsonSerializer.Deserialize<AppSettings>(json); loaded.NormalizeQuickActions();
            Check("QA19", "Setări: implicit nimic ascuns; settings.json vechi sau cu null → gol; la citire id-urile invalide și dublurile dispar; salvat ca „QuickActionsHidden”; lista nu e schimbată pe loc",
                  fresh.QuickActionsHidden.Count == 0 && oldJson.QuickActionsHidden.Count == 0 && nulJson.QuickActionsHidden != null && nulJson.QuickActionsHidden.Count == 0 &&
                  dirty.QuickActionsHidden.SequenceEqual(new[] { "usb-drive", "meeting-headphones" }) && before.Count == 0 && !ReferenceEquals(before, saved.QuickActionsHidden) &&
                  saved.QuickActionsHidden.SequenceEqual(new[] { "meeting-headphones" }) && bad.SequenceEqual(new[] { "meeting-headphones" }) &&
                  json.Contains("\"QuickActionsHidden\":[\"meeting-headphones\"]") && loaded.QuickActionsHidden.SequenceEqual(new[] { "meeting-headphones" }) &&
                  AppSettings.CleanQuickActionsHidden(Enumerable.Range(0, 80).Select(i => "r" + i)).Count == AppSettings.MaxQuickActionsHidden &&
                  Norm(Src("AppSettings.cs")).Contains("s.NormalizeContextPages(); // P27 (Features/ContextPages) s.NormalizeQuickActions();"), json);

            // ---- through the real registry, as QuickAction
            logs.Clear();
            host.MicMuted = false;
            var micR = reg.InvokeAsync(cMeeting.Items[0].ActionId, cMeeting.Items[0].Args, ActionInvoker.QuickAction).GetAwaiter().GetResult();
            var volR = reg.InvokeAsync(cMeeting.Items[1].ActionId, cMeeting.Items[1].Args, ActionInvoker.QuickAction).GetAwaiter().GetResult();
            var ejR = reg.InvokeAsync("device.eject-e", null, ActionInvoker.QuickAction).GetAwaiter().GetResult();
            Check("QA20", "Pornite prin registru ca QuickAction: microfonul se oprește, volumul devine 40; în log doar id-ul (fără valoare); „Scoate” fără confirmare e refuzată",
                  micR.Success && host.MicMuted && volR.Success && host.Volume == 40 && !ejR.Success && !host.Calls.Contains("eject E:\\") &&
                  logs.Count == 3 && logs.All(l => l.Contains("(QuickAction)")) && !logs.Any(l => l.Contains("40")), string.Join(" / ", logs));

            // ---- the new actions
            host.Calls.Clear();
            var openR = reg.InvokeAsync("device.open-e", null, ActionInvoker.QuickAction).GetAwaiter().GetResult();
            var saverR = reg.InvokeAsync("settings.battery-saver", null, ActionInvoker.QuickAction).GetAwaiter().GetResult();
            host.Drives.Clear();
            reg.Refresh();
            var gone = reg.InvokeAsync("device.open-e", null, ActionInvoker.QuickAction).GetAwaiter().GetResult();
            var usbGone = QuickActionRules.ForHover(true, usb, cat);
            Check("QA21", "Acțiuni noi: „device.open-<literă>” (sigură, deschide rădăcina prin Shell.Open) și „settings.battery-saver” (ms-settings:batterysaver); stick scos → indisponibil, rândul dispare",
                  openR.Success && host.Calls.Contains("uri E:\\") && saverR.Success && host.Calls.Contains("uri ms-settings:batterysaver") && !gone.Success && usbGone == null &&
                  reg.Get("settings.battery-saver")?.Safety == ActionSafety.Safe && reg.Get("settings.battery-saver").Title == "Economisire baterie");
            host.Drives.Add(new DriveItem { Root = "E:\\", Name = "SanDisk" });
            reg.Refresh();

            var qh = new FakeQuickHost();
            var regQa = new ActionRegistry(flagsOff);
            QuickActionsActions.Register(regQa, qh);
            var offR = regQa.InvokeAsync(QuickActionsActions.ShowHiddenId, null, ActionInvoker.CommandBar).GetAwaiter().GetResult();
            var regQaOn = new ActionRegistry(flagsOn);
            QuickActionsActions.Register(regQaOn, qh);
            var onR = regQaOn.InvokeAsync(QuickActionsActions.ShowHiddenId, null, ActionInvoker.CommandBar).GetAwaiter().GetResult();
            var sh = regQaOn.Get(QuickActionsActions.ShowHiddenId);
            var together = new ActionRegistry();
            bool allOk = true;
            try
            {
                BuiltInActions.Register(together, new FakeBuiltInHost());
                ContextActions.Register(together, () => null, new FakeShowHost());
                ActivityActions.Register(together, new FakeActivityHost());
                SettingsActions.Register(together, new FakeSettingsHost());
                QuickActionsActions.Register(together, new FakeQuickHost());
            }
            catch { allOk = false; }
            Check("QA22", "„quick-actions.show-hidden”: sigură, ține de comutator (oprit → refuzată), titlu și alias-uri în română și engleză; se înregistrează cu toate celelalte fără dubluri",
                  !offR.Success && onR.Success && qh.Calls == 1 && onR.Message.Contains("2") && sh.Safety == ActionSafety.Safe && sh.FeatureId == QuickActionRules.FeatureId &&
                  ActionRegistry.IsValidId(sh.Id) && sh.Title.Contains("sugestiile") && sh.Aliases.Contains("acțiuni rapide") && sh.Aliases.Contains("quick actions") && sh.Icon.Length > 0 &&
                  sh.RequiresUiThread && allOk && together.Get(QuickActionsActions.ShowHiddenId) != null &&
                  Count(Norm(Src("App.xaml.cs")), "Features.QuickActions.QuickActionsActions.Register(registry, new NotchQuickActionsHost(_notch));") == 1);
            Check("QA23", "Sugestia: un peek cu titlul regulii (fără date personale), sub 120 de caractere; id-ul activității e valid",
                  QuickActionRules.SuggestionTitle(table[0]) == "Întâlnire cu căști · acțiuni rapide la hover" &&
                  table.All(r => QuickActionRules.SuggestionTitle(r).Length <= Core.Activity.Activity.MaxTitle) &&
                  new Core.Activity.Activity { Id = QuickActionRules.SuggestionActivityId, Priority = Core.Activity.ActivityPriority.Low, Title = "x" }.IsValid);

            // ---- the smoke-test injection and commands
            var rs = new Rig().Started();
            bool applied = rs.Engine.ForceMeetingForSmoke(AudioOutputKind.Headphones);
            rs.Clock.Ms(400);
            var forced = rs.Engine.Snapshot;
            bool raised = rs.Events.Count == 1 && rs.Events[0].Has(ContextField.Meeting) && rs.Events[0].Has(ContextField.AudioOutput);
            var fromEngine = QuickActionRules.ForHover(true, forced, cat);
            rs.Engine.ForceMeetingForSmoke(null);
            rs.Clock.Ms(400);
            var real = rs.Engine.Snapshot;
            rs.Flags.Set(ContextEngine.FeatureId, false);
            bool offFalse = !rs.Engine.ForceMeetingForSmoke(AudioOutputKind.Speakers) && rs.Engine.Snapshot == ContextSnapshot.Empty;
            rs.Engine.Dispose();
            Check("QA24", "Injecția testului de fum (ForceMeetingForSmoke): o întâlnire „Test” cu căștile date, prin debounce-ul normal (Changed cu Meeting și AudioOutput); null → contextul real; motor oprit → false",
                  applied && forced.MeetingActive && forced.MeetingApp == "Test" && forced.AudioOutput == AudioOutputKind.Headphones && raised &&
                  Ids(fromEngine) == "meeting-headphones:audio.mute-mic+audio.volume-set" && !real.MeetingActive && real.AudioOutput == AudioOutputKind.Speakers && offFalse);
            Check("QA25", "Comanda de fum „fake-meeting <headphones|speakers|bluetooth|none>” (majusculele nu contează); restul ignorat; numele sunt cele din AudioOutputKind",
                  SmokeMode.Parse("fake-meeting headphones") is { Kind: SmokeCommandKind.FakeMeeting, Argument: "Headphones" } &&
                  SmokeMode.Parse("  FAKE-MEETING  Bluetooth ") is { Kind: SmokeCommandKind.FakeMeeting, Argument: "Bluetooth" } &&
                  SmokeMode.Parse("fake-meeting none") is { Kind: SmokeCommandKind.FakeMeeting, Argument: "" } &&
                  new[] { "fake-meeting", "fake-meeting unknown", "fake-meeting casti", "fake-meeting headphones now", "fake meeting headphones" }.All(l => SmokeMode.Parse(l) == null) &&
                  SmokeMode.MeetingOutputs.Values.All(v => Enum.TryParse<AudioOutputKind>(v, out var k) && k != AudioOutputKind.Unknown && k.ToString() == v) &&
                  SmokeMode.MeetingOutputs.All(kv => kv.Key == kv.Value.ToLowerInvariant()));
            bool st1 = SmokeMode.TryParseStatus(SmokeMode.Status("Expanded", 720, 342, cmd: 1, page: "home", ctx: "Meeting") + SmokeMode.QuickActionsStatus(2, 1),
                                                out _, out _, out _, out var sx1, out var pg1, out var cx1);
            bool st2 = SmokeMode.TryParseStatus("mode=Idle;pill=180x32;qs=3", out _, out _, out _, out var sx2);
            Check("QA26", "Starea pentru UI Automation: „;qa=<n>;qs=<n>” la sfârșit, citite înapoi; zero = lipsesc; formatele vechi merg mai departe; altă ordine e refuzată",
                  st1 && sx1["qa"] == 2 && sx1["qs"] == 1 && sx1["cmd"] == 1 && pg1 == "home" && cx1 == "Meeting" && st2 && sx2["qs"] == 3 && sx2["qa"] == 0 &&
                  SmokeMode.Status("Expanded", 720, 342, page: "home") + SmokeMode.QuickActionsStatus(2, 1) == "mode=Expanded;pill=720x342;page=home;qa=2;qs=1" &&
                  SmokeMode.QuickActionsStatus(0, 0) == "" && SmokeMode.TryParseStatus("mode=Idle;pill=180x32;cmd=1;page=home", out _, out _, out _) &&
                  !SmokeMode.TryParseStatus("mode=Idle;pill=180x32;qa=1;page=home", out _, out _, out _) && !SmokeMode.TryParseStatus("mode=Idle;pill=180x32;qa=1;cmd=1", out _, out _, out _) &&
                  SmokeMode.QuickActionAutomationPrefix + "audio.mute-mic" == "qa-audio.mute-mic" && SmokeMode.QuickActionHideAutomationPrefix + "x" == "qa-hide-x");

            var appSources = Directory.EnumerateFiles(RepoRoot(), "*.cs", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(RepoRoot(), f).Replace('\\', '/'))
                .Where(f => !f.StartsWith("tests/") && !f.Contains("/obj/") && !f.Contains("/bin/") && !f.StartsWith("obj/") && !f.StartsWith(".dotnet/") && !f.StartsWith("publish/"))
                .ToList();
            var callers = appSources.Where(f => Src(f).Contains("ForceMeetingForSmoke(")).OrderBy(f => f).ToList();
            string smoke0 = Src("Features/Smoke/NotchWindow.Smoke.cs");
            Check("QA27", "Injecția de întâlnire: în motor (internal), apelată doar din NotchWindow.Smoke.cs, doar cu --smoke; starea are contoarele Quick Actions",
                  callers.SequenceEqual(new[] { "Core/Context/ContextEngine.cs", "Features/Smoke/NotchWindow.Smoke.cs" }) &&
                  Src("Core/Context/ContextEngine.cs").Contains("internal bool ForceMeetingForSmoke(AudioOutputKind? output)") &&
                  Norm(NoComments(MethodBody(smoke0, "private void SmokeFakeMeeting("))).Contains("if (!SmokeMode.On) return;") &&
                  Norm(NoComments(smoke0)).Contains("case SmokeCommandKind.FakeMeeting: SmokeFakeMeeting(c.Argument); break;") &&
                  Norm(NoComments(smoke0)).Contains("CurrentPageId(), ctx) + SmokeMode.QuickActionsStatus(_qaInvoked, _qaSuggested) + NotchGuardSmokeStatus());") /* B1: the safety net's fields after them */, string.Join(",", callers));

            // ---- R1
            var drives = new FakeQuickCatalog().Add(Act("device.open-e")).Add(Act("device.eject-e", ActionSafety.Confirm));
            drives.Unavailable.Add("device.open-e");                 // would need a drive read: never asked when the context says a stick is there
            var trusted = QuickActionRules.ForHover(true, usb, drives);
            bool notAsked = drives.AvailabilityAsked.Count == 0;
            drives.Off.Add("device.open-e");
            var featureOff = QuickActionRules.ForHover(true, usb, drives);
            var exact = new FakeQuickCatalog().Add(Act("audio.mute-mic")).Add(Act("audio.volume-set", ps: pct));
            QuickActionRules.ForHover(true, meeting, exact);
            Check("QA37", "R1: butonul stick-ului se bazează pe contextul motorului (UsbDrive), fără IsAvailable (care ar citi unitățile pe firul UI); comutatorul acțiunii tot contează; restul acțiunilor își cer disponibilitatea",
                  Ids(trusted) == "usb-drive:device.open-e" && notAsked && featureOff == null && table[3].Actions.All(a => a.TrustContext) &&
                  table.Take(3).SelectMany(r => r.Actions).All(a => !a.TrustContext) && exact.AvailabilityAsked.SequenceEqual(new[] { "audio.mute-mic", "audio.volume-set" }) &&
                  !Norm(NoComments(MethodBody(Src("Features/QuickActions/NotchWindow.QuickActions.cs"), "private void QuickActionsOnOpen()"))).Contains("Refresh()") &&
                  Norm(NoComments(Src("Features/QuickActions/NotchWindow.QuickActions.cs"))).Contains("if (e.Has(ContextField.UsbDrive)) QuickActionsReadDrives();"),
                  string.Join(",", drives.AvailabilityAsked));
            Check("QA38", "R1: doar o sugestie chiar afișată (Shown / Updated) pornește cele 10 minute; Queued, Grouped, Dropped nu",
                  QuickActionSuggestions.ConsumesInterval(Core.Activity.PostResult.Shown) && QuickActionSuggestions.ConsumesInterval(Core.Activity.PostResult.Updated) &&
                  !QuickActionSuggestions.ConsumesInterval(Core.Activity.PostResult.Queued) && !QuickActionSuggestions.ConsumesInterval(Core.Activity.PostResult.Grouped) &&
                  !QuickActionSuggestions.ConsumesInterval(Core.Activity.PostResult.Dropped) &&
                  Norm(NoComments(Src("Features/QuickActions/NotchWindow.QuickActions.cs"))).Contains("if (!QuickActionSuggestions.ConsumesInterval(posted)) return; _qaSuggester.Shown();"));
            Check("QA39", "R1: rezultatul click-ului în rând: propoziția acțiunii pe un rând, tăiată la 60 de caractere cu „…”; gol → „Gata” / „Nu a mers”; arătat și la eșec, cu pensula temei",
                  QuickActionRules.ShortMessage("Microfon oprit (în toate aplicațiile)", true) == "Microfon oprit (în toate aplicațiile)" &&
                  QuickActionRules.ShortMessage("", true) == "Gata" && QuickActionRules.ShortMessage(null, false) == "Nu a mers" &&
                  QuickActionRules.ShortMessage("a\nb", true) == "a b" && QuickActionRules.ShortMessage(new string('x', 100), false).Length == 60 &&
                  QuickActionRules.ShortMessage(new string('x', 100), false).EndsWith("…") &&
                  Norm(NoComments(Src("Features/QuickActions/NotchWindow.QuickActions.cs"))).Contains("QuickActionsShowResult(r);") &&
                  Norm(NoComments(Src("Features/QuickActions/NotchWindow.QuickActions.cs"))).Contains("r.Success ? \"DimBrush\" : \"WarnBrush\""));

            QuickActionsSourcePins();
        }

        /// <summary>The WPF side (no compiler here): small hooks in the big files, the registry as the only way in, no polling, no log of the context.</summary>
        static void QuickActionsSourcePins()
        {
            const string Part = "Features/QuickActions/NotchWindow.QuickActions.cs";
            string notch0 = Src(LegacyAlerts.Notch), notch = Norm(NoComments(notch0)), part = Src(Part), partN = Norm(NoComments(part));
            string expand = Norm(NoComments(MethodBody(notch0, "private void Expand()")));
            string collapse = Norm(NoComments(MethodBody(notch0, "private void Collapse()")));
            Check("QA28", "Legăturile din NotchWindow.xaml.cs sunt câte un rând: în Expand după _pane.Refresh() (înainte de ApplyMode), în Collapse după _pane?.Hidden(), StopQuickActions în Cleanup",
                  expand != null && expand.Contains("_pane.Shown(); _pane.Refresh(); QuickActionsOnOpen(); HeaderClock.Text") &&
                  expand.IndexOf("QuickActionsOnOpen();", StringComparison.Ordinal) < expand.IndexOf("ApplyMode();", StringComparison.Ordinal) &&
                  collapse.Contains("_pane?.Hidden(); QuickActionsOnClose(); SetClickThrough(true);") &&
                  Norm(NoComments(MethodBody(notch0, "public void Cleanup()"))).Contains("StopCommandBar(); StopQuickActions();") &&
                  Count(notch, "QuickActions") == 3);
            string pages = Norm(NoComments(Src("NotchWindow.Pages.cs")));
            Check("QA29", "NotchWindow.Pages.cs: un rând în PanelH (loc pentru rând) și unul la finalul UpdateHeader (modul de editare ia locul rândului); App pornește funcția după motorul de context",
                  Norm(NoComments(MethodBody(Src("NotchWindow.Pages.cs"), "private double PanelH()"))).Contains("if (_banner != null) h += 22; h += QuickActionsExtraHeight(); return h;") &&
                  Norm(NoComments(MethodBody(Src("NotchWindow.Pages.cs"), "private void UpdateHeader()"))).EndsWith("QuickActionsHeaderChanged(); }", StringComparison.Ordinal) &&
                  Count(pages, "QuickActions") == 3 &&
                  Norm(NoComments(MethodBody(Src("NotchWindow.Pages.cs"), "internal void ExitEdit()"))).Contains("S.Save(); if (_mode == Mode.Expanded) QuickActionsOnOpen(); RelayoutPanel();") &&
                  Norm(NoComments(Src("App.xaml.cs"))).Contains("Features.Context.ContextStartup.Start(_notch, Log); _notch.StartQuickActions();"));
            Check("QA30", "Abonările au dezabonare: comutatorul (FeatureFlags.Changed) și motorul de context (Changed); UI prin Dispatcher; comutatorul citit în handler",
                  partN.Contains("FeatureFlags.Current.Changed += _qaFlagHandler;") && partN.Contains("FeatureFlags.Current.Changed -= _qaFlagHandler;") &&
                  partN.Contains("_qaEngine.Changed += _qaContextHandler;") && partN.Contains("_qaEngine.Changed -= _qaContextHandler;") &&
                  partN.Contains("Dispatcher.InvokeAsync(ApplyQuickActionsSwitch)") && partN.Contains("Dispatcher.InvokeAsync(() => QuickActionsContextChanged(e))") &&
                  Norm(NoComments(MethodBody(part, "private void ApplyQuickActionsSwitch()"))).Contains("bool on = QuickActionsEnabled();") &&
                  Norm(NoComments(MethodBody(part, "private void StopQuickActions()"))).Contains("UnsubscribeQuickActionsContext();"));
            Check("QA31", "Acțiunile pornesc doar prin ActionRegistry.Current.InvokeAsync ca QuickAction (fără confirmare, fără ExecuteAsync); fără async void; erorile → ReportError(\"quick-actions\")",
                  Regex.Matches(partN, @"(?<!Dispatcher)\.InvokeAsync\(").Count == 1 && partN.Contains("reg.InvokeAsync(id, args, ActionInvoker.QuickAction, CancellationToken.None)") &&
                  !partN.Contains("ExecuteAsync(") && !partN.Contains("confirmed") && !partN.Contains("async void") && partN.Contains("_ = RunQuickActionAsync(id, args);") &&
                  Count(partN, "FeatureFlags.Current?.ReportError(QuickActionRules.FeatureId, ex);") >= 4);
            var forbidden = new[] { "new Timer", "Thread.Sleep", "Task.Delay", "GetForegroundWindow", "ForegroundSource", "SetWinEventHook", "Process.GetProcess", "ShowLive(", "Alert(" };
            var files = new[] { Part, "Features/QuickActions/QuickActions.cs", "Features/QuickActions/QuickActionsActions.cs", "Features/QuickActions/QuickActionsSettings.cs" };
            var found = files.SelectMany(f => forbidden.Where(x => NoComments(Src(f)).Contains(x)).Select(x => f + ": " + x)).ToList();
            Check("QA32", "Fără timer, polling sau hook propriu, fără citirea ferestrei din față; sugestia doar prin ActivityManager (Post, Low), nu prin alertele vechi",
                  found.Count == 0 && !NoComments(Src("Features/QuickActions/QuickActions.cs")).Contains("DispatcherTimer") && Count(partN, "new DispatcherTimer") == 1 &&
                  partN.Contains("_qaMessageTimer.Tick += (o, e) => { _qaMessageTimer.Stop();") && partN.Contains("_qaMessageTimer?.Stop(); _qaMessage = null;") &&
                  partN.Contains("_activity.Post(new Core.Activity.Activity") && partN.Contains("Priority = Core.Activity.ActivityPriority.Low") &&
                  partN.Contains("bool manager = _activityOn && _activity != null;"), string.Join(" | ", found));
            Check("QA33", "Fără culori scrise în cod: pensulele temei (SetResourceReference / ThemedText) și stilul GhostPill; id-uri UI Automation stabile",
                  !Regex.IsMatch(part, @"Color\.From|#[0-9A-Fa-f]{6}|new SolidColorBrush|Brushes\.") && Count(partN, "SetResourceReference(") >= 2 && Count(partN, "Ui.S(\"GhostPill\")") == 2 &&
                  partN.Contains("SmokeMode.QuickActionAutomationPrefix + item.ActionId") && partN.Contains("SmokeMode.QuickActionHideAutomationPrefix + rule.Id"));
            var logLines = part.Split('\n').Where(l => l.Contains("App.Log(", StringComparison.Ordinal)).ToList();
            Check("QA34", "În log doar id-uri de reguli și texte fixe: niciun titlu de fereastră, proces, aplicație media, etichetă sau parametru",
                  logLines.Count >= 4 && logLines.All(l => !Regex.IsMatch(l, @"Title|ForegroundProcess|MediaApp|MeetingApp|Label|args|Args|snapshot|e\.New|e\.Old")),
                  string.Join(" | ", logLines.Select(Norm)));
            string hover = Norm(NoComments(MethodBody(part, "private void QuickActionsOnOpen()")));
            Check("QA35", "La deschidere: comutatorul citit atunci, apoi doar ContextEngine.Current?.Snapshot (null → Empty); nimic în modul de editare; „Nu mai arăta” doar cu Activity Manager pornit",
                  hover.Contains("if (!_qaOn || !QuickActionsEnabled() || Editing) return;") && hover.Contains("ContextEngine.Current?.Snapshot ?? ContextSnapshot.Empty") &&
                  hover.Contains("QuickActionRules.ForHover(true, snapshot, new RegistryQuickActionCatalog(reg, FeatureFlags.Current))") &&
                  partN.Contains("if (_activityOn && rule.Suggest && !(S.QuickActionsHidden?.Contains(rule.Id) ?? false))"));
            string smokeProgram = SmokeSrc("SmokeContext.cs");
            Check("QA36", "Testul de fum P20 rulează pe ambele căi (activity-manager oprit și pornit): oprit → niciun buton; pornit → butoanele, click prin registru (de două ori), sugestia doar cu Activity Manager; repune starea",
                  Regex.IsMatch(smokeProgram, @"\n\s*Run\(step = _activityOn\s*\?\s*""Quick Actions") && smokeProgram.Contains("QuickActions);") &&
                  smokeProgram.Contains("FakeMeeting(\"headphones\");") && smokeProgram.Contains("FakeMeeting(\"none\");") &&
                  Count(smokeProgram, "Command(\"toggle feature \" + QuickActionsFeature);") == 2 && Count(smokeProgram, ".AsButton().Invoke();") >= 3 &&
                  smokeProgram.Contains("st.Qa == qa + 2") && smokeProgram.Contains("st.Qs == 1") && smokeProgram.Contains("ReadStatus().Qs != 0") &&
                  smokeProgram.Contains("Acțiune audio.mute-mic (QuickAction): reușită") && smokeProgram.Contains("if (hide != _activityOn)"));
        }
    }
}
