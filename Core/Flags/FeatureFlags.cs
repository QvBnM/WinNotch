using System;
using System.Collections.Generic;
using System.Linq;

namespace WinNotch.Core.Flags
{
    /// <summary>
    /// On/off state of the features in <see cref="FeatureCatalog"/>. Stored in settings.json ("Features"); only values that
    /// differ from the default are kept, so a feature turned on by default in a later version reaches everyone who never
    /// touched it. Safe mode (--safe-mode) treats Experimental and Beta features as off without changing what is saved.
    /// <para><see cref="Changed"/> fires once per change of the effective state, on the thread that made the change:
    /// handlers that touch the UI go through the Dispatcher. Handlers read <see cref="IsEnabled"/> instead of assuming
    /// the direction (two changes from different threads can arrive in either order). A handler that throws is logged
    /// and skipped.</para>
    /// </summary>
    public sealed class FeatureFlags
    {
        public const string SafeModeArg = "--safe-mode";

        /// <summary>Errors within <see cref="ErrorWindow"/> after which a feature is switched off by itself.</summary>
        public const int MaxErrors = 3;
        public static readonly TimeSpan ErrorWindow = TimeSpan.FromMinutes(10);

        /// <summary>The app's instance (set at startup); null in the helper modes.</summary>
        public static FeatureFlags Current { get; set; }

        private readonly IDictionary<string, bool> _store;
        private readonly IReadOnlyList<FeatureInfo> _catalog;
        private readonly Action _save;
        private readonly Action<string> _log;
        private readonly Func<DateTime> _now;
        private readonly object _lock = new object();
        private readonly Dictionary<string, List<DateTime>> _errors = new Dictionary<string, List<DateTime>>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _disabledWhy = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <param name="store">The "Features" dictionary from the settings (changed in place).</param>
        /// <param name="save">Persists the settings after an automatic <see cref="Disable"/> (Set is saved by its caller).</param>
        public FeatureFlags(IDictionary<string, bool> store, IReadOnlyList<FeatureInfo> catalog = null, bool safeMode = false,
                            Action save = null, Action<string> log = null, Func<DateTime> now = null)
        {
            _store = store ?? AppSettings.NewFeatures();
            _catalog = catalog ?? FeatureCatalog.All;
            SafeMode = safeMode;
            _save = save;
            _log = log;
            _now = now ?? (() => DateTime.UtcNow);
        }

        /// <summary>Started with --safe-mode: Experimental and Beta features are off for this run.</summary>
        public bool SafeMode { get; }

        /// <summary>A feature's effective state changed (feature id).</summary>
        public event Action<string> Changed;

        public IReadOnlyList<FeatureInfo> Catalog => _catalog;

        public FeatureInfo Find(string id) => _catalog.FirstOrDefault(f => string.Equals(f.Id, id, StringComparison.Ordinal));

        /// <summary>What the user chose (or the default), ignoring safe mode: what Settings shows.</summary>
        public bool IsSaved(string id)
        {
            var f = Find(id);
            if (f == null) return false;
            lock (_lock) return _store.TryGetValue(id, out bool v) ? v : f.DefaultOn;
        }

        /// <summary>Whether the feature should run now. Unknown ids are off.</summary>
        public bool IsEnabled(string id)
        {
            var f = Find(id);
            if (f == null) return false;
            if (SafeMode && f.Stage != FeatureStage.Stable) return false;
            return IsSaved(id);
        }

        /// <summary>Why the feature was switched off by itself in this run, or null.</summary>
        public string DisabledReason(string id)
        {
            lock (_lock) return _disabledWhy.TryGetValue(id ?? "", out var r) ? r : null;
        }

        /// <summary>Turns a feature on or off. <see cref="Changed"/> fires only if the effective state changes. Not saved here.</summary>
        public void Set(string id, bool on)
        {
            if (Find(id) == null) { _log?.Invoke("Funcție necunoscută ignorată: " + id); return; }
            bool turnedOn = Apply(id, on, out bool changed) && on;
            // switched on again by the user: a new chance, the automatic switch-off is forgotten
            if (turnedOn) lock (_lock) { _disabledWhy.Remove(id); _errors.Remove(id); }
            if (changed) Raise(id);
        }

        /// <summary>
        /// "Salvează" in Settings: applies only the switches the user changed on the page (<paramref name="shown"/> = what
        /// the page showed, <paramref name="chosen"/> = what it shows now), so a feature switched off automatically while
        /// the page was open stays off.
        /// </summary>
        public void ApplyChoices(IReadOnlyDictionary<string, bool> shown, IReadOnlyDictionary<string, bool> chosen)
        {
            foreach (var (id, on) in chosen)
                if (!shown.TryGetValue(id, out bool was) || was != on) Set(id, on);
        }

        /// <summary>Longest reason kept from <see cref="Disable"/> (log and Settings).</summary>
        public const int MaxReason = 120;

        /// <summary>
        /// Switches a feature off by itself (e.g. after repeated errors), writes the reason to the log and saves. The
        /// reason is a fixed text written by the feature, never an error message (it goes to the log).
        /// </summary>
        public void Disable(string id, string reason)
        {
            if (Find(id) == null) return;
            reason = (reason ?? "").Replace('\r', ' ').Replace('\n', ' ');
            if (reason.Length > MaxReason) reason = reason.Substring(0, MaxReason) + "…";
            Apply(id, false, out bool changed);
            lock (_lock) _disabledWhy[id] = reason;
            _log?.Invoke("Funcția „" + id + "” a fost oprită automat: " + reason);
            try { _save?.Invoke(); } catch (Exception ex) { _log?.Invoke("Salvarea după oprirea automată: " + ex.GetType().Name); }
            if (changed) Raise(id);
        }

        /// <summary>
        /// Calls each <see cref="Changed"/> handler on its own: one that throws can't stop the others, the caller (e.g. Save
        /// in Settings) or, on a background thread, the whole app. Only the exception type is logged.
        /// </summary>
        private void Raise(string id)
        {
            var handlers = Changed;
            if (handlers == null) return;
            foreach (Action<string> h in handlers.GetInvocationList())
            {
                try { h(id); }
                catch (Exception ex) { _log?.Invoke("Eroare la schimbarea funcției „" + id + "”: " + ex.GetType().Name); }
            }
        }

        /// <summary>
        /// A feature caught an error. Counted in the health summary; after <see cref="MaxErrors"/> within
        /// <see cref="ErrorWindow"/> the feature is switched off. Only the exception type is logged (messages can hold
        /// file names, titles or addresses).
        /// </summary>
        public void ReportError(string id, Exception ex)
        {
            if (Find(id) == null) return;
            Diagnostics.HealthLog.CountError(id);
            string type = ex?.GetType().Name ?? "Eroare";
            bool off;
            lock (_lock)
            {
                var now = _now();
                if (!_errors.TryGetValue(id, out var list)) _errors[id] = list = new List<DateTime>();
                list.Add(now);
                list.RemoveAll(t => now - t > ErrorWindow);
                off = list.Count >= MaxErrors;
                if (off) list.Clear();
            }
            _log?.Invoke("Eroare în funcția „" + id + "”: " + type);
            if (off && IsSaved(id)) Disable(id, MaxErrors + " erori în " + (int)ErrorWindow.TotalMinutes + " minute (ultima: " + type + ")");
        }

        /// <summary>Stores the choice. Returns whether the saved choice changed; <paramref name="changed"/> = the effective state.</summary>
        private bool Apply(string id, bool on, out bool changed)
        {
            lock (_lock)
            {
                bool before = IsEnabled(id), savedBefore = IsSaved(id);
                if (on == Find(id).DefaultOn) _store.Remove(id); else _store[id] = on;
                changed = before != IsEnabled(id);
                return savedBefore != on;
            }
        }
    }
}
