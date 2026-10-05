using System;
using System.Collections.Generic;
using System.Linq;
using WinNotch.Core.Actions;
using WinNotch.Core.Context;
using WinNotch.Core.Flags;
using WinNotch.Features.Context;

namespace WinNotch
{
    public static partial class T
    {
        // ------------------------------------------------------------------ fakes for the context engine

        /// <summary>Hand-driven clock: nothing runs until <see cref="Advance"/>.</summary>
        sealed class FakeClock : IContextScheduler
        {
            sealed class Item : IDisposable { public DateTime At; public Action Work; public TimeSpan? Period; public bool Off; public void Dispose() => Off = true; }
            private readonly List<Item> _items = new List<Item>();
            public DateTime UtcNow { get; private set; } = new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);

            public IDisposable Schedule(TimeSpan due, Action work) { var i = new Item { At = UtcNow + due, Work = work }; _items.Add(i); return i; }
            public IDisposable Every(TimeSpan period, Action work) { var i = new Item { At = UtcNow + period, Work = work, Period = period }; _items.Add(i); return i; }

            public int Active => _items.Count(i => !i.Off);

            public void Advance(TimeSpan d)
            {
                var end = UtcNow + d;
                while (true)
                {
                    var next = _items.Where(i => !i.Off && i.At <= end).OrderBy(i => i.At).FirstOrDefault();
                    if (next == null) break;
                    if (next.At > UtcNow) UtcNow = next.At;
                    if (next.Period is TimeSpan p) next.At += p; else next.Off = true;
                    next.Work();
                }
                _items.RemoveAll(i => i.Off);
                UtcNow = end;
            }

            public void Ms(int ms) => Advance(TimeSpan.FromMilliseconds(ms));
        }

        class FakeSrc<TV> : IContextSource<TV>
        {
            private Action _changed;
            public FakeSrc(string name, TV value, bool polled = false) { Name = name; Value = value; Polled = polled; }
            public string Name { get; }
            public bool Polled { get; set; }
            public TV Value;
            public Exception ThrowOnRead, ThrowOnStart;
            public int Starts, Stops, Reads, Subscribers;
            public event Action Changed { add { _changed += value; Subscribers++; } remove { _changed -= value; Subscribers--; } }
            public void Start() { if (ThrowOnStart != null) throw ThrowOnStart; Starts++; }
            public void Stop() => Stops++;
            public TV Read() { Reads++; if (ThrowOnRead != null) throw ThrowOnRead; return Value; }
            /// <summary>New value and "read me again" (as a Windows event would).</summary>
            public void Set(TV v) { Value = v; _changed?.Invoke(); }
            public void Poke() => _changed?.Invoke();
        }

        sealed class FakeFg : FakeSrc<ForegroundInfo>, IForegroundSource { public FakeFg() : base("foreground", ForegroundInfo.None, true) { } }
        sealed class FakeMedia : FakeSrc<MediaState>, IMediaContextSource { public FakeMedia() : base("media", MediaState.None) { } }
        sealed class FakePrivacy : FakeSrc<CaptureState>, IPrivacyContextSource { public FakePrivacy() : base("privacy", CaptureState.None, true) { } }
        sealed class FakeAudio : FakeSrc<AudioOutputKind>, IAudioContextSource { public FakeAudio() : base("audio", AudioOutputKind.Speakers) { } }
        sealed class FakeNet : FakeSrc<NetworkState>, INetworkContextSource { public FakeNet() : base("network", NetworkState.FromConnectionType("Wi-Fi")) { } }
        sealed class FakePower : FakeSrc<PowerState>, IPowerContextSource { public FakePower() : base("power", new PowerState { HasBattery = true, OnBattery = false, Percent = 80 }) { } }
        sealed class FakeDisplay : FakeSrc<int>, IDisplayContextSource { public FakeDisplay() : base("display", 1) { } }
        sealed class FakeUsb : FakeSrc<bool>, IUsbDriveContextSource { public FakeUsb() : base("usb", false) { } }
        sealed class FakeIdle : FakeSrc<TimeSpan>, IIdleContextSource { public FakeIdle() : base("idle", TimeSpan.Zero, true) { } }

        sealed class Rig
        {
            public readonly FakeClock Clock = new FakeClock();
            public readonly FakeFg Fg = new FakeFg();
            public readonly FakeMedia Media = new FakeMedia();
            public readonly FakePrivacy Privacy = new FakePrivacy();
            public readonly FakeAudio Audio = new FakeAudio();
            public readonly FakeNet Net = new FakeNet();
            public readonly FakePower Power = new FakePower();
            public readonly FakeDisplay Display = new FakeDisplay();
            public readonly FakeUsb Usb = new FakeUsb();
            public readonly FakeIdle Idle = new FakeIdle();
            public readonly List<string> Log = new List<string>();
            public readonly List<ContextChangedEventArgs> Events = new List<ContextChangedEventArgs>();
            public readonly FeatureFlags Flags;
            public readonly ContextEngine Engine;

            public Rig(bool on = true, bool safeMode = false, IReadOnlyList<FeatureInfo> catalog = null)
            {
                var store = AppSettings.NewFeatures();
                if (!on) store[ContextEngine.FeatureId] = false;
                Flags = new FeatureFlags(store, catalog ?? FeatureCatalog.All, safeMode, log: Log.Add);
                Engine = new ContextEngine(new ContextSources
                {
                    Foreground = Fg, Media = Media, Privacy = Privacy, Audio = Audio, Network = Net, Power = Power, Display = Display, UsbDrive = Usb, Idle = Idle,
                }, Flags, Clock, log: Log.Add);
                Engine.Changed += (s, e) => Events.Add(e);
            }

            public int Subscribers => Fg.Subscribers + Media.Subscribers + Privacy.Subscribers + Audio.Subscribers + Net.Subscribers + Power.Subscribers + Display.Subscribers + Usb.Subscribers + Idle.Subscribers;
            public int Starts => Fg.Starts + Media.Starts + Privacy.Starts + Audio.Starts + Net.Starts + Power.Starts + Display.Starts + Usb.Starts + Idle.Starts;
            public int Reads => Fg.Reads + Media.Reads + Privacy.Reads + Audio.Reads + Net.Reads + Power.Reads + Display.Reads + Usb.Reads + Idle.Reads;
            public int Stops => Fg.Stops + Media.Stops + Privacy.Stops + Audio.Stops + Net.Stops + Power.Stops + Display.Stops + Usb.Stops + Idle.Stops;

            /// <summary>Starts the engine and lets the first snapshot through (one event), then forgets the events.</summary>
            public Rig Started()
            {
                Engine.Start();
                Clock.Ms(400);
                Events.Clear();
                return this;
            }
        }

        static ForegroundInfo Fg(string process, string title = "", bool covers = false, bool exclusive = false) =>
            new ForegroundInfo { Process = process, Title = title, CoversMonitor = covers, ExclusiveFullscreen = exclusive };

        sealed class FakeShowHost : IContextShowHost
        {
            public string Title, Detail; public int Calls;
            public void ShowContext(string title, string detail) { Title = title; Detail = detail; Calls++; }
        }

        /// <summary>Context engine (P12): categories, rules, debounce, exact fields, errors, feature switch, privacy of the log, context.show.</summary>
        static void ContextTests()
        {
            // ---- categories
            var cats = AppCategories.Default;
            Check("CX1", "Categorii: Dev, Browser, Meeting (nume noi și vechi), Media, Office, Creator, Game; cale, majuscule, .exe",
                  cats.Categorize("code.exe") == AppCategory.Dev && cats.Categorize(@"C:\Program Files\Microsoft VS Code\Code.exe") == AppCategory.Dev &&
                  cats.Categorize("chrome.exe") == AppCategory.Browser && cats.Categorize("ms-teams.exe") == AppCategory.Meeting &&
                  cats.Categorize("Teams.exe") == AppCategory.Meeting && cats.Categorize("Teams") == AppCategory.Meeting && cats.Categorize("Zoom.exe") == AppCategory.Meeting &&
                  cats.Categorize("Spotify.exe") == AppCategory.Media && cats.Categorize("WINWORD.EXE") == AppCategory.Office &&
                  cats.Categorize("Photoshop.exe") == AppCategory.Creator && cats.Categorize("gimp-2.10.exe") == AppCategory.Creator &&
                  cats.Categorize("cs2.exe") == AppCategory.Game && cats.Categorize("steam.exe") == AppCategory.Game);
            Check("CX2", "Nume de proces necunoscut, gol sau null → Other (fără excepție)",
                  cats.Categorize("aplicatie-necunoscuta-xyz.exe") == AppCategory.Other && cats.Categorize("") == AppCategory.Other &&
                  cats.Categorize(null) == AppCategory.Other && cats.Categorize("   ") == AppCategory.Other && cats.Categorize(".exe") == AppCategory.Other);
            var extended = cats.With(new AppCategoryEntry("aplicatia-mea.exe", AppCategory.Dev), new AppCategoryEntry("notepad", AppCategory.Dev));
            Check("CX3", "Tabelul se extinde ușor: un rând nou e găsit, un rând nou câștigă la același nume, implicitul rămâne neschimbat",
                  extended.Categorize("Aplicatia-Mea.exe") == AppCategory.Dev && extended.Categorize("notepad.exe") == AppCategory.Dev &&
                  cats.Categorize("notepad.exe") == AppCategory.Office && cats.Categorize("aplicatia-mea") == AppCategory.Other);
            var ids = AppCategories.DefaultEntries.Where(e => !e.Prefix).Select(e => e.Name).ToList();
            Check("CX4", "Nume afișate: ms-teams → Teams, cpthost → Zoom, necunoscut → cu majusculă; niciun rând dublu în tabel",
                  cats.DisplayName("ms-teams.exe") == "Teams" && cats.DisplayName("CptHost.exe") == "Zoom" && cats.DisplayName("foo.exe") == "Foo" &&
                  ids.Distinct().Count() == ids.Count, string.Join(",", ids.GroupBy(x => x).Where(g => g.Count() > 1).Select(g => g.Key)));

            // ---- rules
            var playing = new MediaState { Playing = true, App = "YouTube" };
            Check("CX5", "Ecran complet: joc (cunoscut sau D3D exclusiv), video (player sau browser care redă), altceva, nimic",
                  ContextRules.Fullscreen(Fg("cs2", covers: true), AppCategory.Game, MediaState.None) == FullscreenKind.Game &&
                  ContextRules.Fullscreen(Fg("necunoscut", exclusive: true), AppCategory.Other, MediaState.None) == FullscreenKind.Game &&
                  ContextRules.Fullscreen(Fg("vlc", covers: true), AppCategory.Media, MediaState.None) == FullscreenKind.Video &&
                  ContextRules.Fullscreen(Fg("chrome", covers: true), AppCategory.Browser, playing) == FullscreenKind.Video &&
                  ContextRules.Fullscreen(Fg("chrome", covers: true), AppCategory.Browser, MediaState.None) == FullscreenKind.Other &&
                  ContextRules.Fullscreen(Fg("powerpnt", covers: true), AppCategory.Office, playing) == FullscreenKind.Other &&
                  ContextRules.Fullscreen(Fg("cs2"), AppCategory.Game, MediaState.None) == FullscreenKind.None &&
                  ContextRules.Fullscreen(null, AppCategory.Other, null) == FullscreenKind.None);
            var micTeams = new CaptureState { MicrophoneInUse = true, MicrophoneApps = new[] { "Teams" } };
            var camZoom = new CaptureState { CameraInUse = true, CameraApps = new[] { "Zoom" } };
            var micDiscord = new CaptureState { MicrophoneInUse = true, MicrophoneApps = new[] { "Discord" } };
            Check("CX6", "Întâlnire după proces: Teams/Zoom folosesc microfonul sau camera (orice aplicație e în față)",
                  ContextRules.Meeting(Fg("code.exe", "Proiect"), micTeams, cats) == "Teams" && ContextRules.Meeting(ForegroundInfo.None, camZoom, cats) == "Zoom" &&
                  ContextRules.Meeting(Fg("explorer"), new CaptureState { MicrophoneInUse = false, MicrophoneApps = new[] { "Teams" } }, cats) == null);
            Check("CX7", "Întâlnire după titlu: fereastra de ședință Teams/Zoom, Meet/Teams pe web; Teams deschis fără ședință sau Discord pe microfon nu e întâlnire",
                  ContextRules.Meeting(Fg("ms-teams.exe", "Ședință săptămânală | Microsoft Teams"), CaptureState.None, cats) == "Teams" &&
                  ContextRules.Meeting(Fg("Zoom.exe", "Zoom Meeting"), CaptureState.None, cats) == "Zoom" &&
                  ContextRules.Meeting(Fg("chrome.exe", "Meet - abc-defg-hij"), CaptureState.None, cats) == "Meet" &&
                  ContextRules.Meeting(Fg("msedge.exe", "Meet – xyz-wxyz-abc - Google Chrome"), CaptureState.None, cats) == "Meet" &&
                  ContextRules.Meeting(Fg("msedge.exe", "Meeting with Ana | Microsoft Teams"), CaptureState.None, cats) == "Teams" &&
                  ContextRules.Meeting(Fg("ms-teams.exe", "Chat | Microsoft Teams"), CaptureState.None, cats) == null &&
                  ContextRules.Meeting(Fg("zoom.exe", "Zoom Workplace"), CaptureState.None, cats) == null &&
                  ContextRules.Meeting(Fg("chrome.exe", "Meeting notes - Google Docs"), micDiscord, cats) == null &&
                  ContextRules.Meeting(null, null, null) == null);
            Check("CX8", "Ieșire audio: căști/headset, boxe/HDMI, Bluetooth după dispozitiv sau nume, necunoscut",
                  ContextRules.AudioOutput(3, null, "Headphones") == AudioOutputKind.Headphones && ContextRules.AudioOutput(5, "{1}.USB\\VID_1", "Headset") == AudioOutputKind.Headphones &&
                  ContextRules.AudioOutput(1, "{1}.HDAUDIO\\FUNC_01", "Speakers (Realtek)") == AudioOutputKind.Speakers && ContextRules.AudioOutput(9, null, "LG HDR") == AudioOutputKind.Speakers &&
                  ContextRules.AudioOutput(3, "{1}.BTHENUM\\{0000110b}", "WH-1000XM4") == AudioOutputKind.Bluetooth && ContextRules.AudioOutput(null, null, "Căști (Realtek Audio)") == AudioOutputKind.Headphones &&
                  ContextRules.AudioOutput(null, null, "Hands-Free AG Audio") == AudioOutputKind.Bluetooth && ContextRules.AudioOutput(null, null, null) == AudioOutputKind.Unknown);
            Check("CX9", "Rețea din NetService: Wi-Fi, Ethernet, Offline, necunoscut",
                  NetworkState.FromConnectionType("Wi-Fi") is { Online: true, Kind: NetworkKind.WiFi } && NetworkState.FromConnectionType("Ethernet") is { Online: true, Kind: NetworkKind.Ethernet } &&
                  NetworkState.FromConnectionType("Offline") is { Online: false, Kind: NetworkKind.Offline } && NetworkState.FromConnectionType("") is { Kind: NetworkKind.Unknown } &&
                  NetworkState.FromConnectionType(null) is { Kind: NetworkKind.Unknown });

            // ---- snapshot
            var s1 = new ContextSnapshot { ForegroundProcess = "code", MicrophoneApps = new List<string> { "Teams" } };
            var s2 = s1 with { MicrophoneApps = new[] { "Teams" } };
            var s3 = s1 with { OnBattery = true };
            Check("CX10", "Snapshot imutabil: „with” face o copie; liste egale ca valori nu sunt o schimbare; null devine listă goală",
                  ContextSnapshot.Diff(s1, s2) == ContextField.None && ContextSnapshot.Diff(s1, s3) == ContextField.Power && !s1.OnBattery &&
                  new ContextSnapshot { CameraApps = null }.CameraApps.Count == 0 && ContextSnapshot.Diff(null, ContextSnapshot.Empty) == ContextField.None);

            // ---- engine: first snapshot
            var r = new Rig();
            r.Fg.Value = Fg("Code.exe", "main.cs - WinNotch");
            r.Engine.Start();
            bool quietBefore = r.Events.Count == 0;
            r.Clock.Ms(299);
            bool stillQuiet = r.Events.Count == 0;
            r.Clock.Ms(2);
            var first = r.Events.FirstOrDefault();
            Check("CX11", "Pornire: un singur Changed după 300 ms, de la Empty la contextul complet",
                  quietBefore && stillQuiet && r.Events.Count == 1 && first.Old == ContextSnapshot.Empty && first.New == r.Engine.Snapshot &&
                  r.Engine.Snapshot.ForegroundProcess == "code" && r.Engine.Snapshot.ForegroundCategory == AppCategory.Dev && r.Engine.Snapshot.Network == NetworkKind.WiFi &&
                  r.Engine.Snapshot.AudioOutput == AudioOutputKind.Speakers && r.Engine.Snapshot.HasBattery && !r.Engine.Snapshot.OnBattery &&
                  r.Engine.Snapshot.BatteryPercent == 80 && r.Engine.Snapshot.MonitorCount == 1 && first.Has(ContextField.Foreground) && first.Has(ContextField.Power) && r.Engine.Running,
                  "events=" + r.Events.Count);

            // ---- debounce: quick Alt+Tab through 10 windows → one event, with the last one
            r.Events.Clear();
            string[] apps = { "chrome.exe", "code.exe", "spotify.exe", "winword.exe", "ms-teams.exe", "explorer.exe", "excel.exe", "figma.exe", "cs2.exe", "chrome.exe" };
            foreach (var a in apps) { r.Fg.Set(Fg(a, "fereastra " + a)); r.Clock.Ms(50); }
            bool noneYet = r.Events.Count == 0;
            r.Clock.Ms(300);
            Check("CX12", "Debounce: 10 schimbări rapide (la 50 ms) → un singur eveniment, cu ultima aplicație",
                  noneYet && r.Events.Count == 1 && r.Events[0].New.ForegroundProcess == "chrome" && r.Events[0].Fields == ContextField.Foreground,
                  "events=" + r.Events.Count);
            r.Events.Clear();
            for (int i = 0; i < 15; i++) { r.Fg.Set(Fg("app" + i + ".exe")); r.Clock.Ms(200); }
            r.Clock.Ms(400);
            Check("CX13", "Schimbări care nu se opresc (la 200 ms, 3 s): publicate cel târziu la 1 s, nu pierdute și nu câte una pe schimbare",
                  r.Events.Count >= 3 && r.Events.Count <= 5 && r.Engine.Snapshot.ForegroundProcess == "app14", "events=" + r.Events.Count);

            // ---- exact fields
            r.Events.Clear();
            r.Power.Set(new PowerState { HasBattery = true, OnBattery = true, Percent = 79 });
            r.Clock.Ms(400);
            var ePower = r.Events.SingleOrDefault();
            r.Events.Clear();
            r.Fg.Value = Fg("vlc.exe", "film.mkv", covers: true);
            r.Privacy.Value = micDiscord;
            r.Fg.Poke(); r.Privacy.Poke();
            r.Clock.Ms(400);
            var eTwo = r.Events.SingleOrDefault();
            Check("CX14", "Changed raportează exact câmpurile schimbate (doar Power; apoi Foreground + Fullscreen + Microphone), cu Old și New corecte",
                  ePower != null && ePower.Fields == ContextField.Power && !ePower.Old.OnBattery && ePower.New.OnBattery && ePower.New.BatteryPercent == 79 &&
                  eTwo != null && eTwo.Fields == (ContextField.Foreground | ContextField.Fullscreen | ContextField.Microphone) &&
                  eTwo.New.Fullscreen == FullscreenKind.Video && eTwo.Old.ForegroundProcess == "app14",
                  ePower?.Fields + " / " + eTwo?.Fields);
            r.Events.Clear();
            r.Fg.Poke(); r.Audio.Set(AudioOutputKind.Speakers);
            r.Clock.Advance(TimeSpan.FromSeconds(10));
            Check("CX15", "Sursă care anunță fără să se fi schimbat ceva (și citirile periodice) → niciun eveniment", r.Events.Count == 0, "events=" + r.Events.Count);

            // ---- meeting through the engine
            r.Events.Clear();
            r.Privacy.Set(micTeams);
            r.Clock.Ms(400);
            var eMeet = r.Events.SingleOrDefault();
            Check("CX16", "Întâlnire detectată după proces: Teams pe microfon → MeetingActive „Teams”, câmpuri Microphone + Meeting",
                  eMeet != null && eMeet.New.MeetingActive && eMeet.New.MeetingApp == "Teams" && eMeet.Fields == (ContextField.Microphone | ContextField.Meeting),
                  eMeet?.Fields.ToString());
            r.Events.Clear();
            r.Privacy.Set(CaptureState.None);
            r.Clock.Ms(400);
            Check("CX17", "Microfonul eliberat → întâlnirea se încheie", r.Events.Count == 1 && !r.Engine.Snapshot.MeetingActive && r.Engine.Snapshot.MeetingApp == "");

            // ---- polled source (idle) and the 2-minute threshold
            r.Events.Clear();
            r.Idle.Value = TimeSpan.FromSeconds(119);
            r.Clock.Advance(TimeSpan.FromSeconds(4));
            bool notIdle = !r.Engine.Snapshot.Idle && r.Events.Count == 0;
            r.Idle.Value = TimeSpan.FromMinutes(2);
            r.Clock.Advance(ContextEngine.PollInterval + TimeSpan.FromMilliseconds(400));
            Check("CX18", "Inactivitate: citită periodic (fără eveniment), activă de la 2 minute; intervalul de citire ≥ 2 s",
                  notIdle && r.Engine.Snapshot.Idle && r.Events.Count == 1 && r.Events[0].Fields == ContextField.Idle && ContextEngine.PollInterval >= TimeSpan.FromSeconds(2));

            // ---- a source that throws
            var rt = new Rig().Started();
            rt.Idle.ThrowOnRead = new InvalidOperationException("TITLU-SECRET din mesaj");
            rt.Clock.Advance(TimeSpan.FromMinutes(3));                       // polled every 3 s: many failures
            rt.Fg.Set(Fg("code.exe"));
            rt.Clock.Ms(400);
            bool counted = Core.Diagnostics.HealthLog.TakeErrors().Any(e => e.Id == ContextEngine.FeatureId && e.Count == 1);
            Check("CX19", "Sursă care aruncă (mereu): motorul merge mai departe, celelalte surse publică, funcția nu se oprește singură",
                  rt.Engine.Running && rt.Events.Count == 1 && rt.Engine.Snapshot.ForegroundProcess == "code" && rt.Flags.IsEnabled(ContextEngine.FeatureId) &&
                  rt.Flags.DisabledReason(ContextEngine.FeatureId) == null);
            Check("CX20", "Eroarea ajunge în ReportError o singură dată pe sursă; în log doar tipul, fără mesaj; încercări rare (30 s, apoi tot mai rar)",
                  rt.Log.Count(l => l.Contains("Eroare în funcția „context-engine”")) == 1 && rt.Log.Any(l => l.Contains("idle") && l.Contains("InvalidOperationException")) &&
                  !rt.Log.Any(l => l.Contains("TITLU-SECRET")) && rt.Idle.Reads <= 6 && counted, "reads=" + rt.Idle.Reads);
            rt.Idle.ThrowOnRead = null;
            rt.Idle.Value = TimeSpan.FromMinutes(5);
            rt.Clock.Advance(TimeSpan.FromMinutes(11));
            Check("CX21", "Sursa își revine: după pauză e citită din nou și valoarea ei apare", rt.Engine.Snapshot.Idle && rt.Log.Any(l => l.Contains("merge din nou")));
            var rs = new Rig();
            rs.Audio.ThrowOnStart = new Exception("x");
            rs.Fg.Value = Fg("chrome.exe");
            bool noThrow = true; try { rs.Started(); } catch { noThrow = false; }
            rs.Fg.Set(Fg("code.exe")); rs.Clock.Ms(400);
            Check("CX22", "Sursă care aruncă la pornire: motorul pornește cu celelalte", noThrow && rs.Engine.Running && rs.Engine.Snapshot.ForegroundProcess == "code" &&
                  rs.Engine.Snapshot.AudioOutput == AudioOutputKind.Unknown && rs.Events.Count == 1);
            var rb = new Rig().Started();
            int got = 0;
            rb.Engine.Changed += (s, e) => throw new Exception("abonat stricat");
            rb.Engine.Changed += (s, e) => got++;
            bool noThrow2 = true; try { rb.Usb.Set(true); rb.Clock.Ms(400); } catch { noThrow2 = false; }
            Check("CX23", "Un abonat care aruncă nu oprește motorul și nici ceilalți abonați", noThrow2 && got == 1 && rb.Engine.Snapshot.UsbDriveConnected && rb.Engine.Running);

            // ---- feature switch
            var ro = new Rig(on: false);
            ro.Engine.Start();
            ro.Fg.Set(Fg("chrome.exe")); ro.Usb.Set(true);
            ro.Clock.Advance(TimeSpan.FromMinutes(1));
            Check("CX24", "Funcția oprită = zero evenimente: sursele nu pornesc, nu e niciun abonament, nimic citit, snapshot gol",
                  ro.Events.Count == 0 && ro.Starts == 0 && ro.Subscribers == 0 && ro.Reads == 0 && ro.Engine.Snapshot == ContextSnapshot.Empty && !ro.Engine.Running);
            ro.Flags.Set(ContextEngine.FeatureId, true);
            ro.Clock.Ms(400);
            bool startedNow = ro.Engine.Running && ro.Starts == 9 && ro.Subscribers == 9 && ro.Events.Count == 1 && ro.Engine.Snapshot.UsbDriveConnected;
            ro.Events.Clear();
            ro.Fg.Set(Fg("code.exe"));                                       // a change pending in the debounce…
            ro.Clock.Ms(100);
            ro.Flags.Set(ContextEngine.FeatureId, false);                     // …when the switch goes off
            ro.Clock.Advance(TimeSpan.FromMinutes(1));
            ro.Fg.Set(Fg("spotify.exe")); ro.Clock.Advance(TimeSpan.FromSeconds(5));
            Check("CX25", "Pornită din Setări: sursele pornesc; oprită: toate se opresc și se dezabonează, nimic în așteptare nu mai e publicat",
                  startedNow && ro.Events.Count == 0 && ro.Stops == 9 && ro.Subscribers == 0 && ro.Engine.Snapshot == ContextSnapshot.Empty && !ro.Engine.Running && ro.Clock.Active == 0,
                  "events=" + ro.Events.Count + " stops=" + ro.Stops + " subs=" + ro.Subscribers + " timers=" + ro.Clock.Active);
            var rsafe = new Rig(safeMode: true);
            rsafe.Engine.Start(); rsafe.Clock.Ms(400);
            Check("CX26", "Catalog: „context-engine” e Beta, pornit implicit, același id ca motorul; în mod sigur e oprit",
                  FeatureCatalog.Find(ContextEngine.FeatureId) is { Stage: FeatureStage.Beta, DefaultOn: true } && FeatureCatalog.ContextEngine == ContextEngine.FeatureId &&
                  !rsafe.Engine.Running && rsafe.Starts == 0 && rsafe.Events.Count == 0);
            var rd = new Rig().Started();
            rd.Engine.Dispose();
            rd.Flags.Set(ContextEngine.FeatureId, false); rd.Flags.Set(ContextEngine.FeatureId, true);
            rd.Fg.Set(Fg("chrome.exe")); rd.Clock.Advance(TimeSpan.FromSeconds(10));
            Check("CX27", "Dispose: sursele oprite și dezabonate, nu mai urmărește comutatorul", rd.Stops == 9 && rd.Subscribers == 0 && rd.Events.Count == 0 && !rd.Engine.Running);
            var r3 = new Rig(catalog: new[] { new FeatureInfo(ContextEngine.FeatureId, "Motor", "d", FeatureStage.Beta, true) });
            r3.Started();
            r3.Display.ThrowOnRead = new Exception(); r3.Usb.ThrowOnRead = new Exception(); r3.Net.ThrowOnRead = new Exception();
            r3.Display.Poke(); r3.Usb.Poke(); r3.Net.Poke();
            bool noThrow3 = true; try { r3.Clock.Ms(400); r3.Clock.Advance(TimeSpan.FromSeconds(10)); } catch { noThrow3 = false; }
            Check("CX28", "Trei surse diferite stricate: funcția se oprește singură (ReportError), curat, fără excepții și fără evenimente",
                  noThrow3 && !r3.Flags.IsEnabled(ContextEngine.FeatureId) && !r3.Engine.Running && r3.Subscribers == 0 && r3.Events.Count == 0);

            // ---- nothing personal in the log
            var rl = new Rig().Started();
            rl.Fg.Set(Fg("Zoom.exe", "Zoom Meeting — TITLU-SECRET Ana Popescu", covers: true));
            rl.Media.Set(new MediaState { Playing = true, App = "Spotify" });
            rl.Clock.Ms(400);
            rl.Net.Set(NetworkState.FromConnectionType("Offline")); rl.Clock.Ms(400);
            var snapLog = rl.Engine.Snapshot.ToLogString();
            Check("CX29", "Log fără titluri: apar doar categoria și procesul (zoom, Meeting), niciodată titlul ferestrei",
                  rl.Log.Any(l => l.StartsWith("Context: aplicație zoom (Meeting)")) && !rl.Log.Any(l => l.Contains("TITLU-SECRET") || l.Contains("Popescu")) &&
                  !snapLog.Contains("TITLU-SECRET") && rl.Engine.Snapshot.ForegroundTitle.Contains("TITLU-SECRET") && rl.Engine.Snapshot.MeetingApp == "Zoom",
                  string.Join(" | ", rl.Log));

            // ---- context.show
            var host = new FakeShowHost();
            var reg = new ActionRegistry(rl.Flags);
            ContextActions.Register(reg, () => rl.Engine, host);
            var show = reg.Get(ContextActions.ShowId);
            var res = reg.InvokeAsync(ContextActions.ShowId, null, ActionInvoker.CommandBar).GetAwaiter().GetResult();
            Check("CX30", "Acțiunea „context.show”: id valid, titlu și alias, ține de comutator, pe firul UI; arată categoria și procesul, fără titlul ferestrei",
                  show != null && ActionRegistry.IsValidId(show.Id) && show.Title.Length > 0 && show.Aliases.Count > 0 && show.Icon.Length > 0 && show.FeatureId == ContextEngine.FeatureId &&
                  show.RequiresUiThread && show.Safety == ActionSafety.Safe && (show.AllowedInvokers & ActionInvoker.LocalApi) == 0 &&
                  res.Success && host.Calls == 1 && host.Title == "Context: Întâlnire · zoom" && host.Detail.Contains("întâlnire Zoom") && host.Detail.Contains("offline") &&
                  !host.Title.Contains("TITLU") && !host.Detail.Contains("TITLU") && !res.Message.Contains("TITLU") &&
                  reg.Search("context", ActionInvoker.CommandBar).FirstOrDefault()?.Id == ContextActions.ShowId, host.Title + " / " + host.Detail);
            rl.Flags.Set(ContextEngine.FeatureId, false);
            var off = reg.InvokeAsync(ContextActions.ShowId, null, ActionInvoker.CommandBar).GetAwaiter().GetResult();
            var reg2 = new ActionRegistry();
            ContextActions.Register(reg2, () => null, host);
            var none = reg2.InvokeAsync(ContextActions.ShowId, null, ActionInvoker.CommandBar).GetAwaiter().GetResult();
            Check("CX31", "„context.show” cu funcția oprită sau fără motor → Failed cu mesaj, nu arată nimic",
                  !off.Success && !none.Success && none.Message.Contains("oprit") && host.Calls == 1);
            var (dt, dd) = ContextActions.Describe(new ContextSnapshot
            {
                ForegroundProcess = "code", ForegroundTitle = "secret.txt", ForegroundCategory = AppCategory.Dev, Fullscreen = FullscreenKind.Game,
                MicrophoneInUse = true, MicrophoneApps = new[] { "Teams" }, MeetingActive = true, MeetingApp = "Teams", AudioOutput = AudioOutputKind.Headphones,
                Network = NetworkKind.Ethernet, Online = true, OnBattery = true, HasBattery = true, BatteryPercent = 42, MonitorCount = 2, UsbDriveConnected = true, Idle = true,
            });
            Check("CX32", "Rezumatul în română: categorie, proces, ecran complet, întâlnire, microfon, căști, rețea, baterie, monitoare, USB, inactiv",
                  dt == "Context: Programare · code" && dd.Contains("ecran complet (joc)") && dd.Contains("întâlnire Teams") && dd.Contains("microfon: Teams") &&
                  dd.Contains("căști") && dd.Contains("Ethernet") && dd.Contains("baterie 42%") && dd.Contains("2 monitoare") && dd.Contains("stick USB") && dd.Contains("inactiv") &&
                  !dd.Contains("secret") && ContextActions.Describe(null).Title == "Context: nicio aplicație în față", dd);
        }
    }
}
