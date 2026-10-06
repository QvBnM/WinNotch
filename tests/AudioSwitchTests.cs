using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using WinNotch.Core.Actions;
using WinNotch.Core.Flags;
using WinNotch.Features.AudioSwitch;
using WinNotch.Features.Smoke;

namespace WinNotch
{
    public static partial class T
    {
        /// <summary>Windows' playback devices, faked: the list, the default, every SetDefault asked; can be made to throw.</summary>
        sealed class FakeEndpoints : IAudioEndpointSwitcher
        {
            private readonly object _lock = new object();
            public List<AudioEndpointInfo> Devices = new List<AudioEndpointInfo>();
            public string Default;
            public Exception SetError, ListError;
            public readonly List<string> Set = new List<string>();
            public int ListCalls;
            public IReadOnlyList<AudioEndpointInfo> List()
            {
                lock (_lock) { ListCalls++; if (ListError != null) throw ListError; return Devices.ToList(); }
            }
            public string GetDefault() { lock (_lock) return Default; }
            public void SetDefault(string endpointId)
            {
                lock (_lock) { Set.Add(endpointId); if (SetError != null) throw SetError; Default = endpointId; }
            }
            public FakeEndpoints Add(string id, string name) { lock (_lock) Devices.Add(new AudioEndpointInfo(id, name)); return this; }
        }

        sealed class FakeEndpointEvents : IAudioEndpointEvents
        {
            public Action Callback;
            public int Starts, Stops;
            public void Start(Action changed) { Starts++; Callback = changed; }
            public void Stop() { Stops++; Callback = null; }
        }

        /// <summary>A debounce the test fires by hand: no clock, no sleeping.</summary>
        sealed class ManualDebounce : IAudioDebounce
        {
            public readonly Action Tick;
            public int Arms; public bool Disposed;
            public ManualDebounce(Action tick) { Tick = tick; }
            public void Arm() { if (!Disposed) Arms++; }
            public void Fire() { if (Arms > 0 && !Disposed) { Arms = 0; Tick(); } }
            public void Dispose() => Disposed = true;
        }

        sealed class AudioRig
        {
            public readonly FakeEndpoints Sw = new FakeEndpoints();
            public readonly FakeEndpointEvents Events = new FakeEndpointEvents();
            public readonly List<string> Log = new List<string>();
            public readonly FeatureFlags Flags;
            public readonly AudioSwitchService Service;
            public readonly ActionRegistry Registry;
            public readonly AudioOutputActions Provider;
            public readonly List<(string, string)> Failures = new List<(string, string)>();
            public int ChangedCount, ProviderChanged;
            public readonly List<ManualDebounce> Debounces = new List<ManualDebounce>();
            public readonly Queue<Action> Deferred = new Queue<Action>();
            public bool Defer;

            public AudioRig(bool on = true)
            {
                Flags = new FeatureFlags(new Dictionary<string, bool>(), FeatureCatalog.All, log: l => { lock (Log) Log.Add(l); });
                Service = new AudioSwitchService(Sw, Events, () => Flags, l => { lock (Log) Log.Add(l); }, a => { if (Defer) Deferred.Enqueue(a); else a(); },
                                                 tick => { var d = new ManualDebounce(tick); Debounces.Add(d); return d; });
                Service.Changed += () => Interlocked.Increment(ref ChangedCount);
                Service.Failed += (t, h) => { lock (Failures) Failures.Add((t, h)); };
                Registry = new ActionRegistry(Flags, null, l => { lock (Log) Log.Add(l); });
                Provider = AudioSwitchActions.Register(Registry, Service, () => Flags);
                Provider.Changed += () => Interlocked.Increment(ref ProviderChanged);
                if (on) Flags.Set(AudioOutputRules.FeatureId, true);
            }

            public List<ActionDescriptor> Actions() => Registry.All.Where(a => a.Id.StartsWith(AudioOutputRules.ActionPrefix, StringComparison.Ordinal)).ToList();
            public ActionResult Invoke(string id) => Registry.InvokeAsync(id, null, ActionInvoker.UI).GetAwaiter().GetResult();
            public string AllLog() { lock (Log) return string.Join("\n", Log); }
        }

        const string Ep1 = "{0.0.0.00000000}.{11111111-aaaa-bbbb-cccc-000000000001}", Ep2 = "{0.0.0.00000000}.{22222222-aaaa-bbbb-cccc-000000000002}",
                     Ep3 = "{0.0.0.00000000}.{33333333-aaaa-bbbb-cccc-000000000003}";

        /// <summary>P30 Căști/boxe: the list, the ids, the change, the automatic switch-off, the provider, the log, the wiring.</summary>
        static void AudioSwitchTests()
        {
            // ---- the list
            var none = AudioOutputRules.Build(new AudioEndpointInfo[0], null);
            var rig0 = new AudioRig();
            rig0.Service.Start();
            Check("AS1", "Zero dispozitive: lista goală, nicio acțiune „audio.output-*”, nimic marcat; textul „Nicio ieșire audio”",
                  none.Count == 0 && rig0.Service.Outputs.Count == 0 && rig0.Actions().Count == 0 && AudioOutputRules.Build(null, Ep1).Count == 0 &&
                  AudioOutputRules.EmptyText == "Nicio ieșire audio" && rig0.AllLog().Contains("Ieșire audio: 0 ieșiri active."));

            var one = AudioOutputRules.Build(new[] { new AudioEndpointInfo(Ep1, " Căști (Realtek) ") }, Ep1);
            Check("AS2", "Un dispozitiv: un rând, numele curățat, marcat ca implicit, id de acțiune „audio.output-<10 hex>”",
                  one.Count == 1 && one[0].Name == "Căști (Realtek)" && one[0].IsDefault && one[0].EndpointId == Ep1 &&
                  Regex.IsMatch(one[0].ActionId, "^audio\\.output-[0-9a-f]{10}$") && ActionRegistry.IsValidId(one[0].ActionId));

            var many = AudioOutputRules.Build(new[] { new AudioEndpointInfo(Ep2, "Speakers"), new AudioEndpointInfo(Ep1, "Căști"), new AudioEndpointInfo(Ep3, "Boxe HDMI") }, Ep2.ToUpperInvariant());
            Check("AS3", "Mai multe dispozitive: ordonate după nume, exact unul implicit (id-ul comparat fără majuscule)",
                  string.Join(",", many.Select(o => o.Name)) == "Boxe HDMI,Căști,Speakers" && many.Count(o => o.IsDefault) == 1 && many.Single(o => o.IsDefault).EndpointId == Ep2 &&
                  AudioOutputRules.Build(many.Select(o => new AudioEndpointInfo(o.EndpointId, o.Name)), "{altul}").All(o => !o.IsDefault));

            var dup = AudioOutputRules.Build(new[]
            {
                new AudioEndpointInfo(Ep1, "Speakers"), new AudioEndpointInfo(Ep2, "Speakers"), new AudioEndpointInfo(Ep3, "Speakers"),
                new AudioEndpointInfo(Ep1.ToLowerInvariant(), "Altceva"), new AudioEndpointInfo("  ", "Gol"), null, new AudioEndpointInfo("{x}", null),
                new AudioEndpointInfo("{y}", new string('a', 200)),
            }, null);
            var spk = dup.Where(o => o.Name.StartsWith("Speakers", StringComparison.Ordinal)).Select(o => o.Name).ToList();
            Check("AS4", "Nume duplicate → „Speakers”, „Speakers (2)”, „Speakers (3)”, mereu în aceeași ordine; id-uri goale sau repetate ignorate; fără nume → „Ieșire audio fără nume”; numele lungi tăiate",
                  dup.Count == 5 && spk.SequenceEqual(new[] { "Speakers", "Speakers (2)", "Speakers (3)" }) && dup.Select(o => o.Key).Distinct().Count() == 5 &&
                  dup.Any(o => o.Name == AudioOutputRules.Unnamed) && dup.All(o => o.Name.Length <= AudioOutputRules.MaxName) &&
                  string.Join("|", AudioOutputRules.Build(dup.Select(o => new AudioEndpointInfo(o.EndpointId, o.Name.Split(' ')[0])).Reverse(), null).Where(o => o.Name.StartsWith("Speakers")).Select(o => o.Key)) ==
                  string.Join("|", dup.Where(o => o.Name.StartsWith("Speakers")).Select(o => o.Key)));

            string k1 = AudioOutputRules.ShortId(Ep1);
            Check("AS5", "Id stabil și valid: 10 cifre hex din SHA-256 (aceleași la fiecare pornire, fără majuscule), fără acolade sau puncte; dispozitive diferite → id-uri diferite; formatul registrului acceptă id-ul",
                  k1.Length == 10 && Regex.IsMatch(k1, "^[0-9a-f]{10}$") && k1 == AudioOutputRules.ShortId(Ep1.ToLowerInvariant()) && k1 == AudioOutputRules.ShortId(Ep1) &&
                  k1 != AudioOutputRules.ShortId(Ep2) && !(AudioOutputRules.ActionPrefix + k1).Contains("{") && Count(AudioOutputRules.ActionPrefix + k1, ".") == 1 &&
                  ActionRegistry.IsValidId(AudioOutputRules.ActionPrefix + k1) && ActionRegistry.IsValidId(AudioOutputRules.ActionPrefix + k1 + "-2") &&
                  !ActionRegistry.IsValidId("audio.output." + k1) /* the plan's form has two dots: refused, hence the dash */);

            // ---- the service and the actions
            var rig = new AudioRig();
            rig.Sw.Add(Ep1, "Căști").Add(Ep2, "Boxe");
            rig.Sw.Default = Ep2;
            rig.Service.Start();
            var acts = rig.Actions();
            var headphones = rig.Service.Outputs.Single(o => o.EndpointId == Ep1);
            var a1 = acts.FirstOrDefault(a => a.Id == headphones.ActionId);
            Check("AS6", "Acțiunile: una pe ieșire, „Ieșire audio: <nume>” (implicita cu „(implicită)”), Safe, comutatorul „audio-switch”, aliasuri RO+EN, categoria Sunet, iconiță; nu pentru API-ul local",
                  acts.Count == 2 && a1 != null && a1.Title == "Ieșire audio: Căști" && acts.Any(a => a.Title == "Ieșire audio: Boxe (implicită)") &&
                  a1.Safety == ActionSafety.Safe && a1.FeatureId == "audio-switch" && a1.Category == "Sunet" && a1.Icon.Length > 0 && !a1.RequiresUiThread &&
                  new[] { "căști", "boxe", "headphones", "speakers", "ieșire audio", "Căști" }.All(x => a1.Aliases.Contains(x)) && (a1.AllowedInvokers & ActionInvoker.LocalApi) == 0 &&
                  rig.Registry.Search("căști", ActionInvoker.CommandBar).Any(a => a.Id == a1.Id) && rig.Registry.Search("headphones", ActionInvoker.CommandBar).Count >= 2);

            int changedBefore = rig.ChangedCount;
            var ok = rig.Invoke(headphones.ActionId);
            Check("AS7", "SetDefault reușit (prin registru): Windows primește id-ul endpoint-ului, lista se recitește, căștile devin implicite, mesaj prietenos",
                  ok.Success && ok.Message == "Ieșire audio: Căști" && rig.Sw.Set.SequenceEqual(new[] { Ep1 }) && rig.Service.Outputs.Single(o => o.IsDefault).EndpointId == Ep1 &&
                  rig.ChangedCount > changedBefore && rig.AllLog().Contains("Acțiune " + headphones.ActionId + " (UI): reușită"));

            var again = rig.Invoke(headphones.ActionId);
            Check("AS8", "Ieșirea care pare deja implicită (citită doar pentru Multimedia): SetDefault e chemat oricum (idempotent, toate cele trei roluri), reușit (R1)",
                  again.Success && again.Message == "Ieșire audio: Căști" && rig.Sw.Set.SequenceEqual(new[] { Ep1, Ep1 }));

            // a device unplugged between the list and the click: not an IPolicyConfig failure
            var speakers = rig.Service.Outputs.Single(o => o.EndpointId == Ep2);
            rig.Sw.Devices.RemoveAll(d => d.Id == Ep2);
            var gone = rig.Invoke(speakers.ActionId);
            Check("AS9", "Dispozitiv scos între listă și click: „nu mai e conectat”, fără SetDefault, fără oprire automată; lista se recitește și acțiunea dispare",
                  !gone.Success && gone.Message == AudioOutputRules.GoneMessage && rig.Sw.Set.Count == 2 && rig.Flags.IsEnabled("audio-switch") &&
                  rig.Flags.DisabledReason("audio-switch") == null && rig.Service.Outputs.Count == 1 && rig.Actions().Count == 1);

            // ---- the first error turns the switch off, once, with a fixed reason
            var bad = new AudioRig();
            bad.Sw.Add(Ep1, "Căști zq-marcaj-nume").Add(Ep2, "Boxe zq-marcaj-nume");
            bad.Sw.Default = Ep2;
            bad.Sw.SetError = new InvalidCastException("mesaj-secret-al-excepției");
            bad.Service.Start();
            var b1 = bad.Service.Outputs.Single(o => o.EndpointId == Ep1);
            var r1 = bad.Invoke(b1.ActionId);
            var r2 = bad.Invoke(b1.ActionId);
            string badLog = bad.AllLog();
            Check("AS10", "SetDefault aruncă → Disable(„audio-switch”) o singură dată, cu motivul fix (≤ 120 de caractere, fără mesajul excepției); mesajul în pastilă o dată; al doilea click e refuzat (comutator oprit), fără alt apel",
                  !r1.Success && r1.Message.StartsWith(AudioOutputRules.FailureTitle, StringComparison.Ordinal) && !bad.Flags.IsEnabled("audio-switch") &&
                  bad.Flags.DisabledReason("audio-switch") == AudioOutputRules.DisableReason && AudioOutputRules.DisableReason.Length <= FeatureFlags.MaxReason &&
                  Count(badLog, "a fost oprită automat") == 1 && bad.Failures.Count == 1 && bad.Failures[0] == (AudioOutputRules.FailureTitle, AudioOutputRules.FailureHint) &&
                  !r2.Success && bad.Sw.Set.Count == 1 && !badLog.Contains("mesaj-secret") && !r1.Message.Contains("mesaj-secret") &&
                  badLog.Contains("Ieșire audio: schimbarea ieșirii implicite a eșuat (InvalidCastException).") && bad.Service.HasFailed);

            // the notch stops the service when the switch goes off (here by hand, as its handler would)
            bad.Service.Stop();
            Check("AS11", "Comutatorul oprit → zero acțiuni (și în căutare), lista golită, abonarea la dispozitive scoasă",
                  bad.Actions().Count == 0 && bad.Registry.Search("căști", ActionInvoker.CommandBar).Count == 0 && bad.Service.Outputs.Count == 0 &&
                  bad.Events.Stops == 1 && bad.Events.Callback == null && !bad.Service.Running);

            bad.Sw.SetError = null;
            bad.Flags.Set("audio-switch", true);
            bad.Service.Start();
            var r3 = bad.Invoke(bad.Service.Outputs.Single(o => o.EndpointId == Ep1).ActionId);
            Check("AS12", "Pornit din nou de utilizator: o nouă șansă (motivul uitat, SetDefault încercat iar și reușit)",
                  r3.Success && bad.Sw.Set.Count == 2 && bad.Flags.DisabledReason("audio-switch") == null && !bad.Service.HasFailed);

            Check("AS13", "În log nimic din numele dispozitivelor: doar contoare, texte fixe, id-uri de acțiune și tipuri de excepții",
                  !badLog.Contains("zq-marcaj-nume") && !bad.AllLog().Contains("zq-marcaj-nume") && !rig.AllLog().Contains("Căști") && !rig.AllLog().Contains("Boxe") &&
                  bad.AllLog().Contains("Ieșire audio: 2 ieșiri active.") && AudioOutputRules.CountLogLine(1) == "Ieșire audio: 1 ieșire activă.");

            var off = new AudioRig(on: false);
            off.Sw.Add(Ep1, "Căști");
            int offReads = off.Sw.ListCalls;
            Check("AS14", "Comutatorul oprit de la început: nicio acțiune, niciun apel la Windows, nicio abonare",
                  off.Actions().Count == 0 && off.Provider.GetActions().Count() == 0 && off.Sw.ListCalls == offReads && off.Events.Starts == 0 && off.Service.Outputs.Count == 0 &&
                  FeatureCatalog.Find("audio-switch") is { Stage: FeatureStage.Experimental, DefaultOn: false } && FeatureCatalog.AudioSwitch == AudioOutputRules.FeatureId);

            // ---- refresh: provider Changed, registry re-reads
            var rr = new AudioRig();
            rr.Service.Start();
            int pc0 = rr.ProviderChanged;
            rr.Sw.Add(Ep1, "Căști");
            rr.Service.RefreshNow();
            int pc1 = rr.ProviderChanged;
            rr.Service.RefreshNow();                       // nothing changed: no event
            int pc2 = rr.ProviderChanged;
            rr.Sw.Default = Ep1;
            rr.Service.RefreshNow();                       // only the default changed: an event (the check moves)
            Check("AS15", "Refresh: provider-ul anunță registrul doar când lista se schimbă (un dispozitiv nou, altă implicită); registrul recitește acțiunile",
                  pc1 == pc0 + 1 && pc2 == pc1 && rr.ProviderChanged == pc2 + 1 && rr.Actions().Count == 1 && rr.Actions()[0].Title.EndsWith("(implicită)"));

            // ---- debounce of the device notifications (no polling); fired by hand (R1: no clock in the test)
            var db = new AudioRig();
            db.Service.Start();
            int reads0 = db.Sw.ListCalls;
            var cb = db.Events.Callback;
            var deb = db.Debounces.Single();
            for (int i = 0; i < 6; i++) cb();               // a burst from Windows (a Bluetooth headset connecting)
            int readsArmed = db.Sw.ListCalls;
            deb.Fire();
            int readsAfter = db.Sw.ListCalls;
            deb.Fire();                                     // nothing armed since: no read
            int readsIdle = db.Sw.ListCalls;
            db.Service.Stop();
            cb();                                           // a late notification after the stop
            deb.Fire();
            Check("AS16", "Notificările de dispozitive: debounce (≥ 300 ms în aplicație) — o rafală doar re-armează, o singură recitire la tick, niciuna fără notificare; după oprire debounce-ul e eliberat și nimic nu se mai citește; abonare și dezabonare o dată",
                  AudioSwitchService.DebounceMs >= 300 && readsArmed == reads0 && readsAfter == reads0 + 1 && readsIdle == readsAfter && db.Sw.ListCalls == readsAfter &&
                  deb.Disposed && db.Events.Starts == 1 && db.Events.Stops == 1 && Norm(NoComments(Src("Features/AudioSwitch/AudioSwitch.cs"))).Contains("tick => new TimerDebounce(tick, DebounceMs)"),
                  reads0 + " / " + readsArmed + " / " + readsAfter + " / " + db.Sw.ListCalls);

            var le = new AudioRig();
            le.Sw.ListError = new System.IO.IOException("x");
            le.Service.Start();
            Check("AS17", "Lista nu poate fi citită: eroare raportată funcției (ReportError, doar tipul), lista rămâne goală, nimic nu cade",
                  le.Service.Outputs.Count == 0 && le.AllLog().Contains("Eroare în funcția „audio-switch”: IOException") && le.Flags.IsEnabled("audio-switch"));

            // R1: unplugged between the check and SetDefault (SetDefault throws because the device is gone) → not a failure
            var race = new AudioRig();
            race.Sw.Add(Ep1, "Căști").Add(Ep2, "Boxe");
            var raceSw = new RemovingEndpoints(race.Sw, Ep1);
            var race2 = new AudioSwitchService(raceSw, null, () => race.Flags, l => { lock (race.Log) race.Log.Add(l); }, a => a());
            race2.Start();
            var rr2 = race2.Select(race2.Outputs.Single(o => o.EndpointId == Ep1).Key);
            Check("AS22", "Dispozitiv scos între verificare și SetDefault (SetDefault aruncă, iar lista recitită nu-l mai are): „nu mai e conectat”, fără oprire automată, fără mesaj de eroare (R1)",
                  !rr2.Success && rr2.Message == AudioOutputRules.GoneMessage && race.Flags.IsEnabled("audio-switch") && race.Flags.DisabledReason("audio-switch") == null &&
                  !race2.HasFailed && race2.Outputs.All(o => o.EndpointId != Ep1) && raceSw.SetCalls == 1);

            // R1: the switch turned off on the UI thread unsubscribes in the background; a quick restart keeps its subscription
            var bg = new AudioRig();
            bg.Service.Start();
            bg.Defer = true;
            bg.Service.Stop(wait: false);
            bool clearedNow = bg.Service.Outputs.Count == 0 && !bg.Service.Running && bg.Events.Stops == 0;
            bg.Defer = false;
            bg.Service.Start();                             // subscription kept (same callback), owned by the new start
            while (bg.Deferred.Count > 0) bg.Deferred.Dequeue()();     // the old stop's unsubscription runs late
            bool keptOnRestart = bg.Events.Stops == 0 && bg.Events.Starts == 1 && bg.Service.Running;
            bg.Defer = true;
            bg.Service.Stop(wait: false);
            bg.Defer = false;
            bg.Service.Stop();                              // at exit (Cleanup): the pending unsubscription is done now
            bool exitDone = bg.Events.Stops == 1;
            while (bg.Deferred.Count > 0) bg.Deferred.Dequeue()();
            Check("AS23", "Oprirea din UI nu așteaptă COM-ul: lista golită imediat, dezabonarea în fundal; o repornire rapidă își păstrează abonarea; la ieșire (Cleanup) dezabonarea rămasă se face pe loc, o singură dată (R1)",
                  clearedNow && keptOnRestart && exitDone && bg.Events.Stops == 1 &&
                  Norm(NoComments(Src("Features/AudioSwitch/NotchWindow.AudioSwitch.cs"))).Contains("_asService.Stop(wait: false);") &&
                  Norm(NoComments(MethodBody(Src("Features/AudioSwitch/NotchWindow.AudioSwitch.cs"), "private void StopAudioSwitch()"))).Contains("_asService.Stop();"));

            string ws = Norm(NoComments(Src("Features/Context/WindowsSources.cs")));
            Check("AS24", "Evenimentele de captură (microfoane, „{0.0.1.…}”) nu recitesc lista ieșirilor, doar cu includeDeviceEvents; motorul de context neschimbat; numele ilizibil → numele generic (R1)",
                  ws.Contains("public void OnDeviceAdded(string pwstrDeviceId) { if (_devices && !IsCapture(pwstrDeviceId)) _changed(); }") &&
                  ws.Contains("public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId) { if (flow == DataFlow.Render) _changed(); }") &&
                  ws.Contains("deviceId.StartsWith(\"{0.0.1.\", StringComparison.Ordinal)") && ws.Contains("_watcher = new EndpointWatcher(Raise);") &&
                  Norm(NoComments(Src("Features/AudioSwitch/PolicyConfigSwitcher.cs"))).Contains("try { name = d.FriendlyName; } catch (Exception) { name = null; }") &&
                  AudioOutputRules.Build(new[] { new AudioEndpointInfo(Ep1, null) }, null)[0].Name == AudioOutputRules.Unnamed);

            // ---- the Windows side, the notch's side, the wiring (source checks, like the earlier features)
            string pc = Src("Features/AudioSwitch/PolicyConfigSwitcher.cs"), pcN = Norm(NoComments(pc)), pure0 = Src("Features/AudioSwitch/AudioSwitch.cs");
            Check("AS18", "IPolicyConfig izolat în PolicyConfigSwitcher: CLSID 870af99c…, IID f8679f50…, [ComImport], SetDefaultEndpoint pentru eConsole, eMultimedia, eCommunications; obiectul COM eliberat (FinalReleaseComObject); fir MTA; listă Render + Active cu MMDevice eliberat; fără rutare per aplicație",
                  pc.Contains("870af99c-171d-4f9e-af0d-e63df40c2bc9") && pc.Contains("f8679f50-850a-41cf-9c72-430f290290c8") && pc.Contains("[ComImport") &&
                  pcN.Contains("new[] { ERoleConsole, ERoleMultimedia, ERoleCommunications }") && pcN.Contains("ERoleConsole = 0, ERoleMultimedia = 1, ERoleCommunications = 2") &&
                  pcN.Contains("Marshal.FinalReleaseComObject(client)") && pcN.Contains("ApartmentState.MTA") && pcN.Contains("EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)") &&
                  pcN.Contains("using var d = all[i];") && pcN.Contains("UnregisterEndpointNotificationCallback(_watcher)") && pcN.Contains("includeDeviceEvents: true") &&
                  !pcN.Contains("IAudioPolicyConfigFactory") && !pcN.Contains("IAudioSessionControl") && !pc.Contains("Process.Start") && !pc.Contains("ProcessStartInfo") &&
                  !Regex.IsMatch(Src("tests/WinNotch.Tests.csproj"), "Compile Include=\"[^\"]*(PolicyConfigSwitcher|NotchWindow\\.AudioSwitch)") && Src("tests/WinNotch.Tests.csproj").Contains("<Compile Include=\"..\\Features\\AudioSwitch\\AudioSwitch.cs\" />") && !pure0.Contains("using NAudio") && !Norm(NoComments(pure0)).Contains("ComImport") && !Norm(NoComments(pure0)).Contains("Marshal."));

            const string Part = "Features/AudioSwitch/NotchWindow.AudioSwitch.cs";
            string part = Src(Part), partN = Norm(NoComments(part));
            var pure = Norm(NoComments(Src("Features/AudioSwitch/AudioSwitch.cs")));
            var logCalls = new[] { Part, "Features/AudioSwitch/PolicyConfigSwitcher.cs" }.SelectMany(f => Src(f).Split('\n').Where(l => l.Contains("App.Log(", StringComparison.Ordinal))).ToList();
            Check("AS19", "Notch-ul: clickurile doar prin ActionRegistry.Current.InvokeAsync; protocolul comutatorului (IsEnabled citit în handler, Dispatcher, dezabonare); fără culori scrise în cod, colțuri 16, pensule din temă; mesajul de eroare prin Alert (Activity Manager dacă e pornit); fără polling; log fără nume",
                  partN.Contains("var r = await reg.InvokeAsync(actionId, null, ActionInvoker.UI);") && !partN.Contains("ExecuteAsync(") && !partN.Contains("SetDefault(") &&
                  partN.Contains("if (id == AudioOutputRules.FeatureId) Dispatcher.InvokeAsync(ApplyAudioSwitch);") && partN.Contains("bool on = AudioSwitchEnabled();") &&
                  partN.Contains("FeatureFlags.Current.Changed -= _asFlagHandler;") && partN.Contains("_asService.Changed -= _asChanged;") && partN.Contains("_asService.Failed -= _asFailed;") &&
                  !Regex.IsMatch(part, @"Color\.From|#[0-9A-Fa-f]{6}|new SolidColorBrush|Brushes\.|Ui\.B\(") && partN.Contains("CornerRadius = new CornerRadius(16)") && Count(partN, "SetResourceReference(") >= 8 &&
                  partN.Contains("Alert(AudioSwitchFailureAlertId, LiveRow(LiveIcon(Ui.GWarn, CWarn), title, hint, null), 560, 58, 6000, true);") && !partN.Contains("ShowLive(") &&
                  !partN.Contains("Thread.Sleep") && !partN.Contains("Task.Delay") && Count(partN, "new DispatcherTimer") == 1 && !pure.Contains("Thread.Sleep") && !pure.Contains("Task.Delay") &&
                  logCalls.Count >= 4 && logCalls.All(l => !Regex.IsMatch(l, @"\b(FriendlyName|name|ex\.Message)\b|\.Message\b|(?<!GetType\(\)\.)\bName\b")) &&
                  Regex.Matches(pure, @"Disable\(").Count == 1 && pure.Contains("Disable(AudioOutputRules.FeatureId, AudioOutputRules.DisableReason)"),
                  string.Join(" | ", logCalls.Select(l => l.Trim())));

            string notch0 = Src("NotchWindow.xaml.cs"), notch = Norm(NoComments(notch0)), pages = Norm(NoComments(Src("NotchWindow.Pages.cs"))), app = Norm(NoComments(Src("App.xaml.cs")));
            string home = Norm(NoComments(Src("Panes/HomePane.cs")));
            Check("AS20", "Legăturile sunt câte un rând: Collapse, ShowPane, Cleanup (NotchWindow.xaml.cs), UpdateHeader (Pages), slotul de lângă volum (HomePane), înregistrarea și pornirea (App); EndpointWatcher partajat cu motorul de context",
                  Count(notch, "AudioSwitch") == 3 && Norm(NoComments(MethodBody(notch0, "private void Collapse()"))).Contains("StopTyping(); AudioSwitchOnClose(); _pane?.Hidden();") &&
                  Norm(NoComments(MethodBody(notch0, "internal void ShowPane(Pane p)"))).Contains("ExpLayer.Height = PanelH(); AudioSwitchOnPaneChanged();") &&
                  Norm(NoComments(MethodBody(notch0, "public void Cleanup()"))).Contains("StopShelf(); StopAudioSwitch();") &&
                  Count(pages, "AudioSwitch") == 1 && Norm(NoComments(MethodBody(Src("NotchWindow.Pages.cs"), "private void UpdateHeader()"))).Contains("AudioSwitchHeaderChanged(); ShelfHeaderChanged();") &&
                  home.Contains("ctl.Put(OutputSlot, 7);") && Count(home, "OutputSlot") == 2 &&
                  app.Contains("_notch.StartShelf(); _notch.StartAudioSwitch();") && app.Contains("Features.AudioSwitch.AudioSwitchActions.Register(registry, _notch.AudioOutputs, () => Core.Flags.FeatureFlags.Current);") &&
                  Norm(NoComments(Src("Features/Context/WindowsSources.cs"))).Contains("_watcher = new EndpointWatcher(Raise);"));

            // ---- the smoke test
            string sp = SmokeSrc("SmokeAudio.cs");
            string body = MethodBody(sp, "private static void AudioOutputs()") ?? "";
            Check("AS21", "Testul de fum P30 rulează o singură dată (cu activity-manager oprit) și o spune; deschide lista fără dispozitive („Nicio ieșire audio” sau rânduri), butonul de lângă volum o închide și o redeschide, comutatorul nu se oprește singur, nicio alegere; repune starea",
                  sp.Contains("if (!_activityOn) Run(step = \"Căști/boxe") && sp.Contains("SKIP  Căști/boxe") && Src("tests/WinNotch.Smoke/WinNotch.Smoke.csproj").Contains("<Compile Include=\"SmokeAudio.cs\" />") &&
                  Count(body, "Command(\"toggle feature \" + AudioFeature);") == 3 && body.Contains("Command(\"smoke-audio-outputs\");") && body.Contains("\"Nicio ieșire audio\"") &&
                  Count(body, "toggle.AsButton().Invoke();") == 2 && body.Contains("LogCount(AudioAutoOff) > 0") && !body.Contains("SmokeMode.AudioOutputItemPrefix + ") &&
                  SmokeMode.Parse("smoke-audio-outputs")?.Kind == SmokeCommandKind.AudioOutputs && SmokeMode.Parse(" SMOKE-AUDIO-OUTPUTS ")?.Kind == SmokeCommandKind.AudioOutputs &&
                  SmokeMode.Parse("smoke-audio-outputs x") == null &&
                  Norm(NoComments(MethodBody(part, "private void SmokeAudioOutputs()"))).StartsWith("{ if (!SmokeMode.On) return;", StringComparison.Ordinal) &&
                  body.Contains("WaitFor(ListShown, p => p,") && body.Contains("if (!diag.Contains(\"deschisă: da\", StringComparison.Ordinal)) Fail(") &&
                  !body.Contains("AudioOutputsPanelAutomationId"));
            // the CI failure of run 40: the id was on a Border, which has no automation peer → the list was never found
            Check("AS25", "Lista e vizibilă în UI Automation: id-ul pe titlu (TextBlock, are peer) și cadrul e un Border cu peer propriu; linia de diagnostic a comenzii de fum are doar cuvinte fixe și contoare",
                  partN.Contains("AutomationProperties.SetAutomationId(title, SmokeMode.AudioOutputsTitleAutomationId);") && partN.Contains("_asPanel = new AutomationBorder {") &&
                  partN.Contains("new System.Windows.Automation.Peers.FrameworkElementAutomationPeer(this)") &&
                  SmokeMode.AudioOutputsLogLine(true, true, false, true, 0) == "Test de fum: lista ieșirilor audio — deschisă: da, pagina Acasă: da, editare: nu, butonul lângă volum: da, ieșiri: 0." &&
                  SmokeMode.AudioOutputsLogLine(false, false, true, false, 5000).EndsWith("ieșiri: 999.", StringComparison.Ordinal) &&
                  !SmokeMode.IsFatalLogLine(SmokeMode.AudioOutputsLogLine(false, true, false, true, 2)));
        }

        /// <summary>A switcher whose device disappears the moment SetDefault is called (and SetDefault then fails).</summary>
        sealed class RemovingEndpoints : IAudioEndpointSwitcher
        {
            private readonly FakeEndpoints _inner; private readonly string _id;
            public int SetCalls;
            public RemovingEndpoints(FakeEndpoints inner, string id) { _inner = inner; _id = id; }
            public IReadOnlyList<AudioEndpointInfo> List() => _inner.List();
            public string GetDefault() => _inner.GetDefault();
            public void SetDefault(string endpointId)
            {
                SetCalls++;
                _inner.Devices.RemoveAll(d => d.Id == _id);
                throw new System.Runtime.InteropServices.COMException("gone");
            }
        }
    }
}
