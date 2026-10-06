using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WinNotch.Core.Actions;
using WinNotch.Core.Flags;

namespace WinNotch.Features.AudioSwitch
{
    /// <summary>One active playback endpoint as Windows lists it: its endpoint id ("{0.0.0.00000000}.{guid}") and friendly name.</summary>
    public sealed class AudioEndpointInfo
    {
        public AudioEndpointInfo(string id, string name) { Id = id; Name = name; }
        public string Id { get; }
        /// <summary>May be personal ("Căștile lui Mihai"): shown on screen, never written to the log.</summary>
        public string Name { get; }
    }

    /// <summary>
    /// The playback devices and the default output (P30, ADR 0012). In the app: <c>PolicyConfigSwitcher</c> (NAudio for the
    /// list, the undocumented IPolicyConfig for the change); in the tests: a fake. Every method may throw.
    /// </summary>
    public interface IAudioEndpointSwitcher
    {
        /// <summary>The active render endpoints (DataFlow.Render, DeviceState.Active); empty when there are none.</summary>
        IReadOnlyList<AudioEndpointInfo> List();
        /// <summary>The endpoint id of the default output, or null when there is none.</summary>
        string GetDefault();
        /// <summary>Makes the endpoint the default output (for every role, ADR 0012). Throws when Windows refuses.</summary>
        void SetDefault(string endpointId);
    }

    /// <summary>Windows' "a playback device came, went, changed state or became the default" notifications (IMMNotificationClient).</summary>
    public interface IAudioEndpointEvents
    {
        /// <summary>Subscribes; <paramref name="changed"/> comes on a COM thread and must return at once (no COM calls in it).</summary>
        void Start(Action changed);
        /// <summary>Unsubscribes (UnregisterEndpointNotificationCallback) and releases what Start made.</summary>
        void Stop();
    }

    /// <summary>One output as the list and the actions show it.</summary>
    public sealed class AudioOutput
    {
        public AudioOutput(string key, string endpointId, string name, bool isDefault) { Key = key; EndpointId = endpointId; Name = name; IsDefault = isDefault; }
        /// <summary>Short, stable id (10 hex digits of the endpoint id's SHA-256, "-2"… on a clash): the end of the action id.</summary>
        public string Key { get; }
        public string EndpointId { get; }
        /// <summary>The name shown: the friendly name, with " (2)", " (3)"… when two devices have the same one.</summary>
        public string Name { get; }
        public bool IsDefault { get; }
        public string ActionId => AudioOutputRules.ActionPrefix + Key;
    }

    /// <summary>The pure rules of P30: ids, the list, the texts. No NAudio, no COM.</summary>
    public static class AudioOutputRules
    {
        public const string FeatureId = "audio-switch";
        /// <summary>
        /// "audio.output-&lt;key&gt;": the registry's id format allows one dot ("zonă.verb"), so the device is joined with a dash
        /// (the plan's "audio.output.&lt;dispozitiv&gt;" would have two dots and be refused).
        /// </summary>
        public const string ActionPrefix = "audio.output-";
        public const int KeyLength = 10;
        public const int MaxName = 80;
        public const string Category = "Sunet";
        /// <summary>Speaker glyph (Segoe Fluent / MDL2), the font the notch already uses.</summary>
        public const string Glyph = "";

        public const string EmptyText = "Nicio ieșire audio";
        public const string Unnamed = "Ieșire audio fără nume";
        public const string GoneMessage = "Dispozitivul nu mai e conectat.";
        public const string ReadFailedMessage = "Lista ieșirilor audio nu a putut fi citită.";
        /// <summary>The fixed reason given to FeatureFlags.Disable (never the exception's message: it goes to the log and Settings).</summary>
        public const string DisableReason = "Windows a refuzat schimbarea ieșirii audio implicite (interfață nedocumentată).";
        public const string FailureTitle = "Ieșirea audio nu a putut fi schimbată";
        public const string FailureHint = "„Căști/boxe” s-a oprit; o poți porni din nou din Setări › Funcții noi.";

        public static readonly IReadOnlyList<string> Aliases = new[] { "căști", "boxe", "difuzoare", "ieșire audio", "schimbă ieșirea", "headphones", "speakers", "audio output", "output device" };

        /// <summary>10 lowercase hex digits of SHA-256(endpoint id in capitals): the same device always gets the same key.</summary>
        public static string ShortId(string endpointId)
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes((endpointId ?? "").ToUpperInvariant()));
            return Convert.ToHexString(hash, 0, KeyLength / 2).ToLowerInvariant();
        }

        public static string DisplayName(string name)
        {
            string n = (name ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
            if (n.Length == 0) return Unnamed;
            return n.Length > MaxName ? n.Substring(0, MaxName - 1) + "…" : n;
        }

        /// <summary>"Ieșire audio: Căști (implicită)" for the Command Bar.</summary>
        public static string ActionTitle(AudioOutput o) => "Ieșire audio: " + o.Name + (o.IsDefault ? " (implicită)" : "");

        /// <summary>
        /// The list as shown: one entry per endpoint id (blank and repeated ids dropped), sorted by name (then key, so the order
        /// is stable), the default marked, equal names numbered " (2)", " (3)" in that order.
        /// </summary>
        public static IReadOnlyList<AudioOutput> Build(IEnumerable<AudioEndpointInfo> endpoints, string defaultId)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var keys = new HashSet<string>(StringComparer.Ordinal);
            var rows = new List<(string Key, string Id, string Name)>();
            foreach (var e in endpoints ?? Enumerable.Empty<AudioEndpointInfo>())
            {
                if (e == null || string.IsNullOrWhiteSpace(e.Id) || !seen.Add(e.Id)) continue;
                string key = ShortId(e.Id);
                for (int i = 2; !keys.Add(key); i++) key = ShortId(e.Id) + "-" + i;
                rows.Add((key, e.Id, DisplayName(e.Name)));
            }
            var sorted = rows.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Key, StringComparer.Ordinal).ToList();
            var count = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var list = new List<AudioOutput>();
            foreach (var r in sorted)
            {
                count[r.Name] = count.TryGetValue(r.Name, out int n) ? n + 1 : 1;
                string shown = count[r.Name] == 1 ? r.Name : r.Name + " (" + count[r.Name] + ")";
                list.Add(new AudioOutput(r.Key, r.Id, shown, defaultId != null && string.Equals(r.Id, defaultId, StringComparison.OrdinalIgnoreCase)));
            }
            return list;
        }

        /// <summary>Same keys, names and default (in order): nothing to redraw.</summary>
        public static bool Same(IReadOnlyList<AudioOutput> a, IReadOnlyList<AudioOutput> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
                if (a[i].Key != b[i].Key || a[i].Name != b[i].Name || a[i].IsDefault != b[i].IsDefault) return false;
            return true;
        }

        /// <summary>The only log line about the list: a counter, never a device name.</summary>
        public static string CountLogLine(int n) => "Ieșire audio: " + n + (n == 1 ? " ieșire activă." : " ieșiri active.");
    }

    /// <summary>
    /// The list of outputs and the change of the default one (P30, ADR 0012), thread-safe. Started and stopped by the
    /// "audio-switch" switch (the notch does that, on its Dispatcher). Started: subscribes to the device notifications and
    /// reads the list off the UI thread; every notification only re-arms a one-shot timer (debounce
    /// <see cref="DebounceMs"/>), whose tick reads the list again: no polling. The first failure of
    /// <see cref="IAudioEndpointSwitcher.SetDefault"/> turns the switch off (FeatureFlags.Disable, a fixed reason) and raises
    /// <see cref="Failed"/> once. Only counters and exception types go to the log.
    /// </summary>
    public sealed class AudioSwitchService
    {
        public const int DebounceMs = 400;

        private readonly IAudioEndpointSwitcher _sw;
        private readonly IAudioEndpointEvents _events;
        private readonly Func<FeatureFlags> _flags;
        private readonly Action<string> _log;
        private readonly Action<Action> _background;
        private readonly object _lock = new object();
        private readonly object _eventsLock = new object();
        private IReadOnlyList<AudioOutput> _outputs = Array.Empty<AudioOutput>();
        private Timer _debounce;
        private bool _running, _failed, _eventsOn;
        private int _gen, _lastCount = -1;

        /// <param name="background">Runs the start (subscription and first read) off the caller's thread; the tests pass an inline one.</param>
        public AudioSwitchService(IAudioEndpointSwitcher switcher, IAudioEndpointEvents events, Func<FeatureFlags> flags, Action<string> log, Action<Action> background = null)
        {
            _sw = switcher ?? throw new ArgumentNullException(nameof(switcher));
            _events = events;
            _flags = flags ?? (() => FeatureFlags.Current);
            _log = log ?? (_ => { });
            _background = background ?? (a => Task.Run(a));
        }

        /// <summary>The list changed (any thread): redraw through the Dispatcher; the action provider forwards it to the registry.</summary>
        public event Action Changed;
        /// <summary>The first SetDefault failure since the start (any thread): title and hint for the pill, both fixed texts.</summary>
        public event Action<string, string> Failed;

        public bool Running { get { lock (_lock) return _running; } }
        /// <summary>SetDefault failed since the last start (the switch was turned off by it).</summary>
        public bool HasFailed { get { lock (_lock) return _failed; } }
        public IReadOnlyList<AudioOutput> Outputs { get { lock (_lock) return _outputs; } }
        public bool Has(string key) => Outputs.Any(o => o.Key == key);

        public void Start()
        {
            int gen;
            lock (_lock)
            {
                if (_running) return;
                _running = true;
                _failed = false;                                  // switched on again: a new chance
                gen = ++_gen;
                _debounce = new Timer(OnDebounce, gen, Timeout.Infinite, Timeout.Infinite);
            }
            _log("Ieșire audio: pornit.");
            _background(() =>
            {
                lock (_eventsLock)
                {
                    if (!IsCurrent(gen)) return;                  // stopped meanwhile
                    try { _events?.Start(DevicesChanged); _eventsOn = _events != null; }
                    catch (Exception ex) { _log("Ieșire audio: schimbările de dispozitive nu pot fi urmărite (" + ex.GetType().Name + ")."); }
                }
                Refresh(gen);
            });
        }

        public void Stop()
        {
            Timer t;
            lock (_lock)
            {
                if (!_running) return;
                _running = false;
                _gen++;
                t = _debounce;
                _debounce = null;
                _outputs = Array.Empty<AudioOutput>();
                _lastCount = -1;
            }
            try { t?.Dispose(); } catch { }
            lock (_eventsLock)
            {
                if (_eventsOn) { try { _events.Stop(); } catch (Exception ex) { _log("Ieșire audio: dezabonarea a eșuat (" + ex.GetType().Name + ")."); } }
                _eventsOn = false;
            }
            _log("Ieșire audio: oprit.");
            RaiseChanged();
        }

        /// <summary>From the COM notification: only re-arms the timer (never blocks, never calls COM, takes no lock).</summary>
        public void DevicesChanged()
        {
            var t = Volatile.Read(ref _debounce);
            try { t?.Change(DebounceMs, Timeout.Infinite); } catch (ObjectDisposedException) { }
        }

        private void OnDebounce(object state) => Refresh((int)state);

        private bool IsCurrent(int gen) { lock (_lock) return _running && gen == _gen; }

        /// <summary>Reads the list now, on the calling thread (never the UI thread: the actions run it through Task.Run).</summary>
        public void RefreshNow()
        {
            int gen;
            lock (_lock) { if (!_running) return; gen = _gen; }
            Refresh(gen);
        }

        private void Refresh(int gen)
        {
            if (!IsCurrent(gen)) return;
            IReadOnlyList<AudioOutput> built;
            try { built = AudioOutputRules.Build(_sw.List(), _sw.GetDefault()); }
            catch (Exception ex) { ReportError(ex); return; }
            bool changed;
            string countLine = null;
            lock (_lock)
            {
                if (!_running || gen != _gen) return;
                changed = !AudioOutputRules.Same(_outputs, built);
                _outputs = built;
                if (built.Count != _lastCount) { _lastCount = built.Count; countLine = AudioOutputRules.CountLogLine(built.Count); }
            }
            if (countLine != null) _log(countLine);
            if (changed) RaiseChanged();
        }

        /// <summary>
        /// Makes the output the default one. Checked first against a fresh list: a device unplugged meanwhile is "no longer
        /// connected", not an IPolicyConfig failure. A failure of SetDefault itself turns the switch off (the first time).
        /// </summary>
        public ActionResult Select(string key)
        {
            var target = Outputs.FirstOrDefault(o => o.Key == key);
            if (target == null || !Running) return ActionResult.Failed(AudioOutputRules.GoneMessage);
            try
            {
                var now = _sw.List() ?? Array.Empty<AudioEndpointInfo>();
                if (!now.Any(e => e != null && string.Equals(e.Id, target.EndpointId, StringComparison.OrdinalIgnoreCase)))
                {
                    RefreshNow();
                    return ActionResult.Failed(AudioOutputRules.GoneMessage);
                }
                if (string.Equals(_sw.GetDefault(), target.EndpointId, StringComparison.OrdinalIgnoreCase))
                    return ActionResult.Ok("„" + target.Name + "” e deja ieșirea audio.");
            }
            catch (Exception ex) { ReportError(ex); return ActionResult.Failed(AudioOutputRules.ReadFailedMessage); }

            try { _sw.SetDefault(target.EndpointId); }
            catch (Exception ex)
            {
                Fail(ex);
                return ActionResult.Failed(AudioOutputRules.FailureTitle + ". " + AudioOutputRules.FailureHint);
            }
            RefreshNow();
            return ActionResult.Ok("Ieșire audio: " + target.Name);
        }

        /// <summary>The first failure only: Disable with the fixed reason, then <see cref="Failed"/> (the notch shows it).</summary>
        private void Fail(Exception ex)
        {
            lock (_lock) { if (_failed) return; _failed = true; }
            _log("Ieșire audio: schimbarea ieșirii implicite a eșuat (" + ex.GetType().Name + ").");
            try { _flags()?.Disable(AudioOutputRules.FeatureId, AudioOutputRules.DisableReason); }
            catch (Exception e2) { _log("Ieșire audio: oprirea automată a eșuat (" + e2.GetType().Name + ")."); }
            try { Failed?.Invoke(AudioOutputRules.FailureTitle, AudioOutputRules.FailureHint); }
            catch (Exception e3) { _log("Ieșire audio: mesajul de eroare nu a putut fi arătat (" + e3.GetType().Name + ")."); }
        }

        private void ReportError(Exception ex)
        {
            try { _flags()?.ReportError(AudioOutputRules.FeatureId, ex); } catch { }
        }

        private void RaiseChanged()
        {
            var h = Changed;
            if (h == null) return;
            foreach (Action a in h.GetInvocationList())
            {
                try { a(); }
                catch (Exception ex) { _log("Ieșire audio: un abonat a dat eroare (" + ex.GetType().Name + ")."); }
            }
        }
    }

    /// <summary>
    /// "Ieșire audio: &lt;dispozitiv&gt;" for every active output (Safe; behind "audio-switch"; none with the switch off).
    /// The registry reads them again after <see cref="Changed"/> (the service's list changed or it stopped).
    /// </summary>
    public sealed class AudioOutputActions : IActionProvider
    {
        private readonly AudioSwitchService _s;
        private readonly Func<FeatureFlags> _flags;

        public AudioOutputActions(AudioSwitchService service, Func<FeatureFlags> flags = null)
        {
            _s = service ?? throw new ArgumentNullException(nameof(service));
            _flags = flags ?? (() => FeatureFlags.Current);
            _s.Changed += () => Changed?.Invoke();
        }

        public event Action Changed;

        public IEnumerable<ActionDescriptor> GetActions()
        {
            if (!(_flags()?.IsEnabled(AudioOutputRules.FeatureId) ?? false)) return Array.Empty<ActionDescriptor>();
            var list = new List<ActionDescriptor>();
            foreach (var o in _s.Outputs)
            {
                string key = o.Key;
                list.Add(new ActionDescriptor(o.ActionId, AudioOutputRules.ActionTitle(o),
                    (a, ct) => Task.Run(() => _s.Select(key), ct),                 // COM off the UI thread (thread pool, MTA)
                    () => _s.Has(key))
                {
                    Aliases = AudioOutputRules.Aliases.Concat(new[] { o.Name }).ToArray(),
                    Category = AudioOutputRules.Category, Icon = AudioOutputRules.Glyph, Safety = ActionSafety.Safe,
                    FeatureId = AudioOutputRules.FeatureId, UnavailableMessage = AudioOutputRules.GoneMessage,
                });
            }
            return list;
        }
    }

    public static class AudioSwitchActions
    {
        /// <summary>Registered once at startup, from App.RegisterActions.</summary>
        public static AudioOutputActions Register(ActionRegistry registry, AudioSwitchService service, Func<FeatureFlags> flags = null)
        {
            var p = new AudioOutputActions(service, flags);
            registry.RegisterProvider(p);
            return p;
        }
    }
}
