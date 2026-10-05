using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using WinNotch.Core.Actions;
using WinNotch.Core.Context;
using WinNotch.Core.Flags;
using WinNotch.Features.Actions;
using WinNotch.Features.Activity;
using WinNotch.Features.CommandBar;
using WinNotch.Features.Context;
using WinNotch.Features.Smoke;

namespace WinNotch
{
    public static partial class T
    {
        sealed class FakeSettingsHost : ISettingsHost
        {
            public readonly List<string> Targets = new List<string>();
            public void OpenSettingsAt(string target) => Targets.Add(target);
        }

        /// <summary>
        /// P14: the Command Bar without WPF (shortcut, when it opens, search with parameters, Enter / confirmation, sizes),
        /// the "settings.*" actions, and the notch's WPF side pinned in the source (hooks, focus, registry, log).
        /// </summary>
        static void CommandBarTests()
        {
            // ---- the switch
            var info = FeatureCatalog.Find(CommandBarRules.FeatureId);
            var safe = new FeatureFlags(AppSettings.NewFeatures(), safeMode: true);
            var normal = new FeatureFlags(AppSettings.NewFeatures());
            Check("CB1", "Comutatorul „command-bar”: în catalog, Experimental, oprit implicit (fără 0.7.0), oprit în mod sigur; același id ca CommandBarRules",
                  info != null && FeatureCatalog.CommandBar == CommandBarRules.FeatureId && info.Stage == FeatureStage.Experimental && !info.DefaultOn &&
                  !normal.IsEnabled(CommandBarRules.FeatureId) && !safe.IsEnabled(CommandBarRules.FeatureId) && info.Name.Length > 0 && info.Description.Contains("Win+Alt+Space"));

            // ---- the shortcut
            Check("CB2", "Scurtătura: „k” → Win+Alt+K, orice altceva → Win+Alt+Space (implicit); tastele 0x20 / 0x4B; propune-o pe cealaltă",
                  CommandBarHotkeys.Parse(null) == CommandBarKey.Space && CommandBarHotkeys.Parse("") == CommandBarKey.Space && CommandBarHotkeys.Parse(" K ") == CommandBarKey.K &&
                  CommandBarHotkeys.Parse("ctrl+shift+x") == CommandBarKey.Space && CommandBarHotkeys.VirtualKey(CommandBarKey.Space) == 0x20 && CommandBarHotkeys.VirtualKey(CommandBarKey.K) == 0x4B &&
                  CommandBarHotkeys.Label(CommandBarKey.Space) == "Win+Alt+Space" && CommandBarHotkeys.Label(CommandBarKey.K) == "Win+Alt+K" &&
                  CommandBarHotkeys.ToSetting(CommandBarHotkeys.Parse("k")) == "k" && CommandBarHotkeys.ToSetting(CommandBarKey.Space) == "space" &&
                  CommandBarHotkeys.Other(CommandBarKey.Space) == CommandBarKey.K && CommandBarHotkeys.Other(CommandBarKey.K) == CommandBarKey.Space);
            Check("CB3", "Alerta de conflict numește scurtătura ocupată, o propune pe Win+Alt+K și locul din Setări",
                  CommandBarHotkeys.ConflictTitle(CommandBarKey.Space).Contains("Win+Alt+Space") && CommandBarHotkeys.ConflictHint(CommandBarKey.Space).Contains("Win+Alt+K") &&
                  CommandBarHotkeys.ConflictHint(CommandBarKey.Space).Contains("Setări") && CommandBarHotkeys.ConflictHint(CommandBarKey.K).Contains("Win+Alt+Space"));

            {
                var calls = new List<string>();
                bool free = true;
                var hk = new CommandBarHotkey(vk => { calls.Add("reg " + vk); return free; }, () => calls.Add("unreg"));
                var off = hk.Apply(false, CommandBarKey.Space);
                bool offNothing = off.Change == HotkeyChange.None && calls.Count == 0 && hk.Active == null;
                var on = hk.Apply(true, CommandBarKey.Space);
                var again = hk.Apply(true, CommandBarKey.Space);
                bool onOnce = on.Change == HotkeyChange.Registered && again.Change == HotkeyChange.None && hk.Active == CommandBarKey.Space && calls.SequenceEqual(new[] { "reg 32" });
                var swap = hk.Apply(true, CommandBarKey.K);
                bool swapped = swap.Change == HotkeyChange.Registered && hk.Active == CommandBarKey.K && calls.SequenceEqual(new[] { "reg 32", "unreg", "reg 75" });
                var stop = hk.Apply(false, CommandBarKey.K);
                Check("CB4", "Scurtătura e înregistrată doar cu comutatorul pornit, o singură dată; schimbarea tastei o mută; oprit → eliberată",
                      offNothing && onOnce && swapped && stop.Change == HotkeyChange.Unregistered && hk.Active == null && calls.Last() == "unreg",
                      string.Join(",", calls));

                calls.Clear();
                free = false;
                var f1 = hk.Apply(true, CommandBarKey.Space);
                var f2 = hk.Apply(true, CommandBarKey.Space);         // Settings saved again: tried again, nothing new to say
                hk.Apply(false, CommandBarKey.Space);
                var f3 = hk.Apply(true, CommandBarKey.Space);         // switched off and on: still only one alert in this run
                free = true;
                var k = hk.Apply(true, CommandBarKey.K);
                Check("CB5", "Conflict: o singură alertă pe tastă și rulare (nu la fiecare salvare sau repornire a comutatorului); Win+Alt+K liberă → activă, conflictul dispare",
                      f1.Change == HotkeyChange.Failed && f1.ShowAlert && f2.Change == HotkeyChange.None && !f2.ShowAlert &&
                      f3.Change == HotkeyChange.Failed && !f3.ShowAlert && k.Change == HotkeyChange.Registered && !k.ShowAlert && hk.Conflict == null && hk.Active == CommandBarKey.K,
                      $"{f1.Change}/{f1.ShowAlert} {f2.Change} {f3.Change}/{f3.ShowAlert} {k.Change}");
                var throwing = new CommandBarHotkey(vk => throw new InvalidOperationException("win32"), () => throw new InvalidOperationException("win32"));
                bool noThrow = true; HotkeyResult tr = default;
                try { tr = throwing.Apply(true, CommandBarKey.Space); throwing.Apply(false, CommandBarKey.Space); } catch { noThrow = false; }
                Check("CB6", "Înregistrarea care aruncă e tratată ca un conflict, fără excepție", noThrow && tr.Change == HotkeyChange.Failed && tr.ShowAlert);
            }

            // ---- when the shortcut opens it (fullscreen: unit-tested, not smoke-tested)
            Check("CB7", "Scurtătura: comutator oprit → nimic; deschis → se închide; peste ecran complet, pastilă ascunsă, unealtă sau editare → nimic; altfel se deschide",
                  CommandBarRules.OnShortcut(false, false, false, false, false) == ShortcutDecision.Ignore &&
                  CommandBarRules.OnShortcut(false, true, false, false, false) == ShortcutDecision.Ignore &&
                  CommandBarRules.OnShortcut(true, true, true, true, true) == ShortcutDecision.Close &&
                  CommandBarRules.OnShortcut(true, false, true, false, false) == ShortcutDecision.Ignore &&
                  CommandBarRules.OnShortcut(true, false, false, true, false) == ShortcutDecision.Ignore &&
                  CommandBarRules.OnShortcut(true, false, false, false, true) == ShortcutDecision.Ignore &&
                  CommandBarRules.OnShortcut(true, false, false, false, false) == ShortcutDecision.Open);
            Check("CB8", "Ecran complet: fereastra din față acoperă monitorul sau e Direct3D exclusiv, ori motorul de context spune ecran complet; maximizată sau desktop → nu",
                  CommandBarRules.IsFullscreen(new ForegroundInfo { Process = "game.exe", CoversMonitor = true }, FullscreenKind.None) &&
                  CommandBarRules.IsFullscreen(new ForegroundInfo { Process = "game.exe", ExclusiveFullscreen = true }, FullscreenKind.None) &&
                  CommandBarRules.IsFullscreen(new ForegroundInfo { Process = "code.exe" }, FullscreenKind.Video) &&
                  !CommandBarRules.IsFullscreen(new ForegroundInfo { Process = "code.exe" }, FullscreenKind.None) &&
                  !CommandBarRules.IsFullscreen(ForegroundInfo.None, FullscreenKind.None) && !CommandBarRules.IsFullscreen(null, FullscreenKind.None));

            // ---- search over the real built-in actions, with parameters
            var host = new FakeBuiltInHost { Drives = new List<DriveItem> { new DriveItem { Root = "E:\\", Name = "SanDisk" } } };
            var logs = new List<string>();
            var reg = new ActionRegistry(log: logs.Add);
            BuiltInActions.Register(reg, host);
            var settingsHost = new FakeSettingsHost();
            SettingsActions.Register(reg, settingsHost);
            reg.Register(new ActionDescriptor("test.wipe", "Șterge tot de test", (a, ct) => ActionResult.OkTask()) { Safety = ActionSafety.Dangerous, Aliases = new[] { "volum periculos" } });
            reg.Register(new ActionDescriptor("test.ui-only", "Doar din interfață volum", (a, ct) => ActionResult.OkTask()) { AllowedInvokers = ActionInvoker.UI });
            reg.Register(new ActionDescriptor("test.note", "Notiță de test", (a, ct) => ActionResult.OkTask(a.GetText("text")))
                { Parameters = new[] { ActionParameter.Text("text", "Text", 30) } });
            reg.Register(new ActionDescriptor("test.side", "Partea de test", (a, ct) => ActionResult.OkTask())
                { Parameters = new[] { ActionParameter.Enum("parte", "Partea", "stanga", "dreapta"), ActionParameter.Int("nr", "Numărul", 1, 9) } });

            var v30 = CommandBarSearch.Find(reg, "volum 30");
            var first = v30.FirstOrDefault();
            Check("CB9", "„volum 30” → „Setează volumul” cu valoarea 30 (cuvintele de la final sunt parametrii)",
                  first?.Id == "audio.volume-set" && first.Args.TryGetValue("valoare", out var val) && val == "30" && first.MissingParameter == null && first.Detail == "30%",
                  string.Join(",", v30.Select(i => i.Id + "[" + string.Join(";", i.Args.Select(kv => kv.Key + "=" + kv.Value)) + "]")));
            var pct = CommandBarSearch.Find(reg, "  VOLUM   45%  ").FirstOrDefault();
            var bad = CommandBarSearch.Find(reg, "volum 150");
            Check("CB10", "„volum 45%” (majuscule, spații) merge; „volum 150” nu leagă o valoare invalidă",
                  pct?.Id == "audio.volume-set" && pct.Args["valoare"] == "45%" && !bad.Any(i => i.Args.Count > 0), string.Join(",", bad.Select(i => i.Id)));
            var noVal = CommandBarSearch.Find(reg, "volum");
            var nv = noVal.FirstOrDefault(i => i.Id == "audio.volume-set");
            Check("CB11", "„volum” fără valoare: acțiunea apare, dar Enter cere valoarea (nu o pornește)",
                  nv != null && nv.Args.Count == 0 && nv.MissingParameter?.Name == "valoare");
            Check("CB12", "Acțiunile periculoase și cele care nu pot fi pornite din Command Bar nu apar niciodată",
                  !CommandBarSearch.Find(reg, "volum periculos").Any(i => i.Id == "test.wipe") && !CommandBarSearch.Find(reg, "sterge tot de test").Any(i => i.Id == "test.wipe") &&
                  !CommandBarSearch.Find(reg, "doar din interfata").Any() && !CommandBarSearch.Allowed(reg.Get("test.wipe")) && CommandBarSearch.Allowed(reg.Get("device.eject-e")));
            Check("CB13", "Parametri: textul absoarbe cuvintele în plus; enum + număr în ordine; prea multe valori sau valori invalide → nimic legat",
                  CommandBarSearch.Find(reg, "notita de test salut lume").FirstOrDefault() is CommandItem note && note.Id == "test.note" && note.Args["text"] == "salut lume" &&
                  CommandBarSearch.Find(reg, "partea de test dreapta 3").FirstOrDefault() is CommandItem side && side.Args["parte"] == "dreapta" && side.Args["nr"] == "3" &&
                  !CommandBarSearch.Find(reg, "partea de test dreapta 3 4").Any(i => i.Id == "test.side" && i.Args.Count > 0) &&
                  !CommandBarSearch.Find(reg, "partea de test sus 3").Any(i => i.Id == "test.side" && i.Args.Count > 0) &&
                  !CommandBarSearch.TryBind(reg.Get("audio.mute"), new[] { "1" }, out _) && !CommandBarSearch.TryBind(reg.Get("test.note"), Array.Empty<string>(), out _));
            bool longOk = true;
            try { longOk = CommandBarSearch.Find(reg, new string('a', 5000) + " 30").Count <= CommandBarSearch.MaxResults && CommandBarSearch.Find(null, "volum").Count == 0 && CommandBarSearch.Find(reg, null) != null; }
            catch { longOk = false; }
            Check("CB14", "Text foarte lung, registru lipsă sau text null: fără excepție; cel mult 7 rezultate", longOk && CommandBarSearch.Find(reg, "e").Count <= CommandBarSearch.MaxResults);

            // invoked as the bar does it: through the registry, as CommandBar, the value never in the log
            logs.Clear();
            var r77 = reg.InvokeAsync(CommandBarSearch.Find(reg, "volum 77")[0].Id, CommandBarSearch.Find(reg, "volum 77")[0].Args, ActionInvoker.CommandBar).GetAwaiter().GetResult();
            Check("CB15", "Pornită prin registru ca CommandBar: volumul devine 77; în log doar id-ul și rezultatul (fără valoare)",
                  r77.Success && host.Volume == 77 && logs.Count == 1 && logs[0].Contains("audio.volume-set") && logs[0].Contains("CommandBar") && !logs[0].Contains("77"), string.Join(" / ", logs));
            Check("CB16", "Text gol: acțiunile folosite recent", CommandBarSearch.Find(reg, "").FirstOrDefault()?.Id == "audio.volume-set" && CommandBarSearch.Find(reg, "   ").Count >= 1);

            // ---- Enter, confirmation, arrows
            {
                var s = new CommandBarSession();
                s.SetResults("volum 30", CommandBarSearch.Find(reg, "volum 30"));
                var d = s.Enter();
                bool safeRuns = d.Outcome == EnterOutcome.Invoke && !d.Confirmed && d.Item.Id == "audio.volume-set" && s.Message == null;
                s.SetResults("volum", CommandBarSearch.Find(reg, "volum"));
                while (s.SelectedItem != null && s.SelectedItem.Id != "audio.volume-set") s.Move(1);
                var need = s.Enter();
                Check("CB17", "Enter pe o acțiune sigură o pornește direct (fără confirmare); fără valoare → mesaj „Scrie și „Volum” …”",
                      safeRuns && need.Outcome == EnterOutcome.NeedsParameters && s.Message != null && s.Message.Contains("Volum"), s.Message);

                var ej = CommandBarSearch.Find(reg, "scoate sandisk");
                s.SetResults("scoate sandisk", ej);
                bool ejFirst = s.SelectedItem?.Id == "device.eject-e";
                var c1 = s.Enter();
                bool asked = c1.Outcome == EnterOutcome.AskConfirm && s.Message == CommandBarSession.ConfirmMessage && s.PendingConfirm == "device.eject-e";
                var c2 = s.Enter();
                bool confirmed = c2.Outcome == EnterOutcome.Invoke && c2.Confirmed && s.PendingConfirm == null && s.Message == null;
                Check("CB18", "Confirm: primul Enter arată „Apasă Enter din nou pentru a confirma”, al doilea pornește cu confirmed: true",
                      ejFirst && asked && confirmed, string.Join(",", ej.Select(i => i.Id)));

                s.SetResults("scoate sandisk", ej);
                s.Enter();
                s.SetResults("scoate sandis", CommandBarSearch.Find(reg, "scoate sandis"));      // the text was edited
                var e1 = s.Enter();
                s.SetResults("scoate sandisk", ej);
                s.Enter();
                s.Move(1); s.Move(-1);                                                             // the selection moved (and came back)
                var e2 = s.Enter();
                s.SetResults("scoate sandisk", ej);
                s.Enter();
                bool sameKeeps = s.Select(0) && s.PendingConfirm == "device.eject-e";
                var e3 = s.Enter();
                Check("CB19", "O modificare a textului sau o mișcare a selecției anulează confirmarea; un click pe același rând o păstrează",
                      e1.Outcome == EnterOutcome.AskConfirm && !e1.Confirmed && e2.Outcome == EnterOutcome.AskConfirm && !e2.Confirmed && sameKeeps && e3.Outcome == EnterOutcome.Invoke && e3.Confirmed);

                var noYes = reg.InvokeAsync("device.eject-e", null, ActionInvoker.CommandBar).GetAwaiter().GetResult();
                var yes = reg.InvokeAsync(e3.Item.Id, e3.Item.Args, ActionInvoker.CommandBar, default, e3.Confirmed).GetAwaiter().GetResult();
                Check("CB20", "Registrul refuză Confirm fără confirmare; cu confirmarea din bară, scoaterea merge", !noYes.Success && yes.Success && host.Calls.Contains("eject E:\\"));

                var m = new CommandBarSession();
                bool emptyOk = !m.Move(1) && m.Selected == -1 && m.Enter().Outcome == EnterOutcome.Nothing && m.Message == null;
                m.SetResults("xyzqq", Array.Empty<CommandItem>());
                bool none = m.Message == CommandBarSession.NothingFound && m.Enter().Outcome == EnterOutcome.Nothing;
                m.SetResults("e", CommandBarSearch.Find(reg, "e"));
                int n = m.Results.Count;
                m.Move(-1);
                bool wrapUp = m.Selected == n - 1;
                m.Move(1);
                bool wrapDown = m.Selected == 0;
                Check("CB21", "Săgețile: circular sus/jos; fără rezultate nu se întâmplă nimic; „Nicio acțiune găsită”",
                      emptyOk && none && n > 1 && wrapUp && wrapDown && !m.Select(n) && !m.Select(-1));
                Check("CB22", "Mesajul pentru valoarea lipsă e în română, fără valoarea scrisă",
                      CommandBarSession.MissingMessage(ActionParameter.Percent("valoare", "Volum")) == "Scrie și „Volum” după comandă (de exemplu „… 30”)" &&
                      CommandBarSession.MissingMessage(ActionParameter.Enum("p", "Partea", "stanga", "dreapta")).Contains("stanga / dreapta"));
            }

            // ---- sizes
            Check("CB23", "Mărimea barei urmează rândurile: 52 px goală, +38 px pe rând (+8), +26 px pentru mesaj; cel mult 7 rânduri; raza 18",
                  CommandBarLayout.Height(0, false) == 52 && CommandBarLayout.Height(3, false) == 52 + 3 * 38 + 8 && CommandBarLayout.Height(3, true) == 52 + 3 * 38 + 8 + 26 &&
                  CommandBarLayout.Height(50, false) == CommandBarLayout.Height(7, false) && CommandBarLayout.Height(-1, true) == 78 && CommandBarLayout.Radius >= 16 && CommandBarLayout.Radius <= 18 &&
                  CommandBarLayout.Height(7, true) * 1.75 < 420 * 1.75);

            // ---- settings.<nume>
            {
                var opts = SettingsActions.All;
                var builtIn = new ActionRegistry();
                BuiltInActions.Register(builtIn, new FakeBuiltInHost());
                Check("CB24", "Setări ca acțiuni: id-uri „settings.<nume>” unice și valide, fără să le refolosească pe cele ale setărilor Windows",
                      opts.Count >= 20 && opts.Select(o => o.Id).Distinct().Count() == opts.Count && opts.All(o => o.Id.StartsWith("settings.", StringComparison.Ordinal) && ActionRegistry.IsValidId(o.Id)) &&
                      opts.All(o => builtIn.Get(o.Id) == null) && opts.All(o => o.Title.StartsWith("Setări: ", StringComparison.Ordinal) && o.Aliases.Count > 0 && o.Section.Length > 0));
                var acts = SettingsActions.Create(settingsHost);
                Check("CB25", "Fiecare e sigură, pe firul interfeței, în „Setări WinNotch”, cu iconiță, fără comutator",
                      acts.All(a => a.Safety == ActionSafety.Safe && a.RequiresUiThread && a.Category == SettingsActions.Category && a.Icon.Length > 0 && a.FeatureId == null &&
                                    (a.AllowedInvokers & ActionInvoker.CommandBar) != 0 && (a.AllowedInvokers & ActionInvoker.LocalApi) == 0));

                var all = new ActionRegistry();
                bool together = true;
                try
                {
                    BuiltInActions.Register(all, new FakeBuiltInHost());
                    ContextActions.Register(all, () => null, new FakeShowHost());
                    ActivityActions.Register(all, new FakeActivityHost());
                    SettingsActions.Register(all, new FakeSettingsHost());
                }
                catch (Exception ex) { together = false; Console.WriteLine("      " + ex.Message); }
                Check("CB26", "Toate acțiunile aplicației se înregistrează împreună fără id-uri dublate (ca în App.RegisterActions)", together && all.All.Count >= 45 && SettingsActions.All.All(o => all.Get(o.Id) != null), "count=" + all.All.Count);

                settingsHost.Targets.Clear();
                var pos = reg.InvokeAsync("settings.position", null, ActionInvoker.CommandBar).GetAwaiter().GetResult();
                var eye = reg.InvokeAsync("settings.eye-break", null, ActionInvoker.CommandBar).GetAwaiter().GetResult();
                Check("CB27", "„settings.position” deschide Setări la PosBox, „settings.eye-break” la EyeBox", pos.Success && eye.Success && settingsHost.Targets.SequenceEqual(new[] { "PosBox", "EyeBox" }));
                var sp = CommandBarSearch.Find(reg, "setari pozitie");
                Check("CB28", "Căutare: „setari pozitie” → „Setări: poziția notch-ului” primul (testul de fum se bazează pe el); „setari ochi” → pauza pentru ochi",
                      sp.FirstOrDefault()?.Id == "settings.position" && CommandBarSearch.Find(reg, "setari ochi").FirstOrDefault()?.Id == "settings.eye-break",
                      string.Join(",", sp.Select(i => i.Id)));

                string xaml = Src("SettingsWindow.xaml");
                var names = Regex.Matches(xaml, "<(\\w+)\\b[^>]*?x:Name=\"(\\w+)\"", RegexOptions.Singleline).Cast<Match>()
                                 .Select(mm => (Type: mm.Groups[1].Value, Name: mm.Groups[2].Value)).ToList();
                var missingTargets = opts.Where(o => !names.Any(x => x.Name == o.Target)).Select(o => o.Id).ToList();
                // inputs that belong to another option's row: the RAM threshold (ram-alert), latitude and longitude (weather)
                var covered = new HashSet<string> { "RamPctBox", "LatBox", "LonBox" };
                var inputs = names.Where(x => x.Type is "CheckBox" or "ComboBox" or "Slider" or "TextBox").Select(x => x.Name).ToList();
                var uncovered = inputs.Where(nm => !covered.Contains(nm) && !opts.Any(o => o.Target == nm)).ToList();
                Check("CB29", "Fiecare opțiune din SettingsWindow.xaml are acțiunea ei; fiecare țintă există (listele se deschid la secțiune)",
                      missingTargets.Count == 0 && uncovered.Count == 0 && inputs.Count >= 18 && opts.Count(o => o.SectionOnly) == 5,
                      "fără țintă: " + string.Join(",", missingTargets) + " · fără acțiune: " + string.Join(",", uncovered));
                Check("CB30", "Setarea scurtăturii: CmdKeyBox cu Win+Alt+Space („space”) și Win+Alt+K („k”); implicit „space” în AppSettings",
                      Regex.IsMatch(Norm(xaml), "x:Name=\"CmdKeyBox\".*Tag=\"space\" Content=\"Win\\+Alt\\+Space\".*Tag=\"k\" Content=\"Win\\+Alt\\+K\"") &&
                      Norm(Src("AppSettings.cs")).Contains("public string CommandBarKey { get; set; } = \"space\";") &&
                      Norm(Src("SettingsWindow.xaml.cs")).Contains("_s.CommandBarKey = TagOf(CmdKeyBox) ?? Features.CommandBar.CommandBarHotkeys.SpaceSetting;"));
            }

            // ---- smoke-test hooks
            Check("CB31", "Testul de fum: „open-command-bar” e o comandă; starea primește „;cmd=1” cu bara deschisă și se citește înapoi",
                  SmokeMode.Parse(" OPEN-COMMAND-BAR ")?.Kind == SmokeCommandKind.OpenCommandBar && SmokeMode.Parse("open-command-bar now") == null &&
                  SmokeMode.Status("Expanded", 560, 138, cmd: 1) == "mode=Expanded;pill=560x138;cmd=1" &&
                  SmokeMode.TryParseStatus("mode=Expanded;pill=560x138;cmd=1", out var sm, out _, out _, out var sx) && sm == "Expanded" && sx["cmd"] == 1 &&
                  SmokeMode.TryParseStatus("mode=Idle;pill=180x32", out _, out _, out _, out var sx0) && sx0["cmd"] == 0 &&
                  SmokeMode.CommandBoxAutomationId == "WinNotchCommandBox" && SmokeMode.CommandResultAutomationPrefix.Length > 0);

            CommandBarSourcePins();
        }

        /// <summary>The WPF side (no compiler here): small hooks in the big files, the launcher's focus path, the registry, nothing typed in the log.</summary>
        static void CommandBarSourcePins()
        {
            const string Part = "Features/CommandBar/NotchWindow.CommandBar.cs";
            string notch = Norm(NoComments(Src(LegacyAlerts.Notch))), part = Src(Part), partN = Norm(NoComments(part));
            string applyMode = Norm(NoComments(MethodBody(Src(LegacyAlerts.Notch), "private void ApplyMode()")));
            Check("CB32", "Legăturile din NotchWindow.xaml.cs sunt câte un rând: WndProc, primul rând din ApplyMode, finalul ApplySettings, Cleanup, SecondTick (panoul nu se reîmprospătează sub bară)",
                  Count(notch, "wParam.ToInt32() == CommandBarHotkeyId) { OnCommandBarShortcut(); handled = true; }") == 1 &&
                  applyMode.StartsWith("{ if (CommandBarApplyMode()) return; ", StringComparison.Ordinal) &&
                  Count(notch, "CommandBarSettingsChanged();") == 1 && Count(notch, "StopCommandBar();") == 1 &&
                  Count(notch, "if (_mode == Mode.Expanded && !CommandBarOpen)") == 1 &&
                  Count(notch, "CommandBar") == 6 && Count(notch, "CommandBarHotkeyId") == 1);
            string open = Norm(NoComments(MethodBody(part, "private void OpenCommandBar()")));
            string close = Norm(NoComments(MethodBody(part, "private void CloseCommandBar()")));
            Check("CB33", "Focus: aceeași cale ca lansatorul (EnableTyping la deschidere, Collapse → StopTyping → LastForeground la închidere); ForceForeground doar dacă Windows refuză Activate; niciun SetForegroundWindow propriu",
                  open.Contains("RememberForegroundForCommandBar();") && open.Contains("EnableTyping(_cmdBox);") && open.Contains("_mode = Mode.Expanded;") &&
                  open.Contains("ActionRegistry.Current?.Refresh();") && close.Contains("if (_mode == Mode.Expanded) Collapse();") &&
                  !part.Contains("SetForegroundWindow") && Count(partN, "ForceForeground") == 1 && open.Contains("EnableTyping(_cmdBox); if (!IsActive) Native.ForceForeground(_hwnd);") && !part.Contains("SetNoActivate") &&
                  partN.Contains("Deactivated += OnCommandBarDeactivated;") && partN.Contains("Deactivated -= OnCommandBarDeactivated;"));
            string shortcut = Norm(NoComments(MethodBody(part, "private void OnCommandBarShortcut()")));
            Check("CB34", "Scurtătura trece prin regula pură (ecran complet citit ca motorul de context); comanda de test intră pe aceeași cale",
                  shortcut.Contains("CommandBarRules.IsFullscreen(_cmdForeground.Read(), context)") && shortcut.Contains("CommandBarRules.OnShortcut(_cmdOn, _cmdOpen, full, _hidden, _toolBusy || Editing)") &&
                  Norm(Src("Features/Smoke/NotchWindow.Smoke.cs")).Contains("case SmokeCommandKind.OpenCommandBar: App.Log(\"Test de fum: Command Bar prin comandă (aceeași cale ca scurtătura).\"); OnCommandBarShortcut(); break;"));
            Check("CB35", "Acțiunile pornesc doar prin ActionRegistry.Current.InvokeAsync ca CommandBar, cu confirmarea din sesiune; niciun ExecuteAsync direct",
                  Regex.Matches(partN, @"(?<!Dispatcher)\.InvokeAsync\(").Count == 1 && partN.Contains("reg.InvokeAsync(id, args, ActionInvoker.CommandBar, CancellationToken.None, confirmed)") &&
                  !partN.Contains("ExecuteAsync(") && partN.Contains("_ = RunCommandAsync(item.Id, item.Args, d.Confirmed);") && !partN.Contains("async void"));
            var logLines = part.Split('\n').Where(l => l.Contains("App.Log(", StringComparison.Ordinal)).ToList();
            Check("CB36", "În log nu ajunge textul scris, parametrii sau titlurile (doar stări fixe)",
                  logLines.Count > 0 && logLines.All(l => !Regex.IsMatch(l, @"\.Text\b|args|Args|Title|item\.|_cmdSession")), string.Join(" | ", logLines.Select(Norm)));
            Check("CB37", "Scurtătura: RegisterHotKey o singură dată (prin CommandBarHotkey), eliberată la oprire; abonarea la comutator are dezabonare; UI prin Dispatcher",
                  Count(partN, "Native.RegisterHotKey(") == 1 && Count(partN, "Native.UnregisterHotKey(") == 1 &&
                  partN.Contains("FeatureFlags.Current.Changed += _cmdFlagHandler;") && partN.Contains("FeatureFlags.Current.Changed -= _cmdFlagHandler;") &&
                  partN.Contains("Dispatcher.InvokeAsync(ApplyCommandBarSwitch)") && Norm(NoComments(MethodBody(part, "private void StopCommandBar()"))).Contains("_cmdHotkey?.Apply(false, CommandBarKey.Space);"));
            Check("CB38", "Fără culori scrise în cod: doar pensulele temei (SetResourceReference), raza din CommandBarLayout; alertele prin Alert(…)",
                  !Regex.IsMatch(part, @"Color\.From|#[0-9A-Fa-f]{6}|new SolidColorBrush") && Count(partN, "SetResourceReference(") >= 6 &&
                  partN.Contains("CommandBarLayout.Radius * k") && Regex.Matches(partN, @"\bAlert\(CommandBarRules\.\w+AlertId,").Count == 2 && !partN.Contains("ShowLive("));
            Check("CB39", "App: acțiunile „settings.*” înregistrate o dată; scurtătura pornită după acțiuni; Setări la o opțiune prin fereastra WinNotch",
                  Count(Norm(Src("App.xaml.cs")), "Features.CommandBar.SettingsActions.Register(registry, new Features.CommandBar.AppSettingsHost(this));") == 1 &&
                  Norm(Src("App.xaml.cs")).Contains("RegisterActions(); _notch.StartCommandBar();") && Norm(Src("App.xaml.cs")).Contains("OpenEditor(\"settings\"); _editor?.RevealSetting(target);") &&
                  Norm(Src("EditorWindow.cs")).Contains("internal void RevealSetting(string target) => _settings?.Reveal(target);"));
        }
    }
}
