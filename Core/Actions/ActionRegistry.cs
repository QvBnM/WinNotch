using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using WinNotch.Core.Flags;

namespace WinNotch.Core.Actions
{
    /// <summary>
    /// Every action WinNotch can do, in one place: registered once at startup (plus providers for actions that come and
    /// go), searchable without diacritics and run with the same checks whoever calls them (UI, Command Bar, Quick
    /// Actions, Workflows, the local API). <see cref="InvokeAsync"/> never throws: a failure is an <see cref="ActionResult"/>.
    /// The log gets only the id, the caller and the outcome, never parameter values.
    /// </summary>
    public sealed class ActionRegistry
    {
        /// <summary>The app's registry (set once at startup); null in the helper modes.</summary>
        public static ActionRegistry Current { get; set; }

        public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);
        public const int MaxRecent = 50;

        /// <summary>"zonă.verb": lowercase words with dashes, one dot (e.g. "audio.mute-mic", "workspace.open-lucru").</summary>
        private static readonly Regex IdFormat = new Regex("^[a-z0-9]+(-[a-z0-9]+)*\\.[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.CultureInvariant);

        private readonly Dictionary<string, ActionDescriptor> _static = new Dictionary<string, ActionDescriptor>(StringComparer.Ordinal);
        private readonly List<IActionProvider> _providers = new List<IActionProvider>();
        private readonly List<string> _recent = new List<string>();        // most recent first, in memory only
        private readonly object _lock = new object();
        private readonly FeatureFlags _flags;
        private readonly IUiDispatcher _ui;
        private readonly Action<string> _log;
        private List<ActionDescriptor> _dynamic;                           // cached provider actions; null = read again

        public ActionRegistry(FeatureFlags flags = null, IUiDispatcher ui = null, Action<string> log = null)
        {
            _flags = flags;
            _ui = ui ?? new InlineUiDispatcher();
            _log = log;
        }

        /// <summary>After every invocation: id, caller, success (for the Undo Center and the journal).</summary>
        public event Action<string, ActionInvoker, bool> ActionInvoked;

        public static bool IsValidId(string id) => id != null && id.Length <= 80 && IdFormat.IsMatch(id);

        /// <summary>Adds a fixed action. Throws on a bad or duplicate id, or a non-Safe action offered to the local API.</summary>
        public void Register(ActionDescriptor a)
        {
            Check(a);
            lock (_lock)
            {
                if (_static.ContainsKey(a.Id) || (_dynamic?.Any(d => d.Id == a.Id) ?? false))
                    throw new InvalidOperationException("Acțiune înregistrată de două ori: " + a.Id);
                _static[a.Id] = a;
            }
        }

        public void RegisterProvider(IActionProvider p)
        {
            if (p == null) throw new ArgumentNullException(nameof(p));
            lock (_lock) { _providers.Add(p); _dynamic = null; }
            p.Changed += Refresh;
        }

        /// <summary>Drops the cached provider actions (they are read again on the next use).</summary>
        public void Refresh()
        {
            lock (_lock) _dynamic = null;
        }

        private static void Check(ActionDescriptor a)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (!IsValidId(a.Id)) throw new ArgumentException("Id invalid (format „zonă.verb”): " + a.Id);
            if (string.IsNullOrWhiteSpace(a.Title)) throw new ArgumentException("Acțiune fără titlu: " + a.Id);
            if ((a.AllowedInvokers & ActionInvoker.LocalApi) != 0 && a.Safety != ActionSafety.Safe)
                throw new ArgumentException("Doar acțiunile sigure pot fi pornite din API-ul local: " + a.Id);
            var names = a.Parameters.Select(p => p?.Name).ToList();
            if (names.Any(string.IsNullOrEmpty) || names.Distinct().Count() != names.Count)
                throw new ArgumentException("Parametri fără nume sau dubli: " + a.Id);
        }

        /// <summary>Provider actions, read once and kept until a refresh. Bad or duplicate ones are skipped (and logged).</summary>
        private List<ActionDescriptor> Dynamic()
        {
            lock (_lock)
            {
                if (_dynamic != null) return _dynamic;
                var list = new List<ActionDescriptor>();
                var seen = new HashSet<string>(_static.Keys, StringComparer.Ordinal);
                foreach (var p in _providers)
                {
                    IEnumerable<ActionDescriptor> items;
                    try { items = p.GetActions()?.ToList() ?? new List<ActionDescriptor>(); }
                    catch (Exception ex) { _log?.Invoke("Acțiuni: o listă dinamică nu a putut fi citită: " + ex.GetType().Name); continue; }
                    foreach (var a in items)
                    {
                        try { Check(a); }
                        catch (Exception) { _log?.Invoke("Acțiuni: acțiune dinamică ignorată (invalidă)."); continue; }
                        if (!seen.Add(a.Id)) { _log?.Invoke("Acțiuni: acțiune dinamică ignorată (id dublu): " + a.Id); continue; }
                        list.Add(a);
                    }
                }
                _dynamic = list;
                return list;
            }
        }

        public ActionDescriptor Get(string id)
        {
            if (id == null) return null;
            lock (_lock) if (_static.TryGetValue(id, out var a)) return a;
            return Dynamic().FirstOrDefault(d => d.Id == id);
        }

        public IReadOnlyList<ActionDescriptor> All
        {
            get
            {
                List<ActionDescriptor> fixedOnes;
                lock (_lock) fixedOnes = _static.Values.ToList();
                return fixedOnes.Concat(Dynamic()).ToList();
            }
        }

        /// <summary>The ids used most recently, newest first (memory only).</summary>
        public IReadOnlyList<string> Recent { get { lock (_lock) return _recent.ToList(); } }

        // ------------------------------------------------------------------ search

        /// <summary>"Mută" → "muta", "Șterge" → "sterge": lowercase, without diacritics.</summary>
        public static string Fold(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var d = s.Normalize(NormalizationForm.FormD);
            var b = new StringBuilder(d.Length);
            foreach (char c in d)
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) b.Append(char.ToLowerInvariant(c));
            return b.ToString().Normalize(NormalizationForm.FormC).Trim();
        }

        private const int Exact = 400, WordStart = 300, Substring = 200, Fuzzy = 100;

        /// <summary>How well a (folded) query matches a (folded) text: exact > start of a word > inside > letters in order.</summary>
        public static int Score(string text, string q)
        {
            if (q.Length == 0 || text.Length == 0) return 0;
            if (text == q) return Exact;
            int at = text.IndexOf(q, StringComparison.Ordinal);
            if (at == 0) return WordStart;
            if (at > 0)
            {
                // inside: does one of the occurrences start a word?
                for (int i = at; i > 0; i = text.IndexOf(q, i + 1, StringComparison.Ordinal))
                    if (!char.IsLetterOrDigit(text[i - 1])) return WordStart;
                return Substring;
            }
            // letters in order ("mtmc" → "mută microfonul"); not for one or two letters (too loose)
            if (q.Length < 3) return 0;
            int k = 0;
            foreach (char c in text) if (k < q.Length && c == q[k]) k++;
            return k == q.Length ? Fuzzy : 0;
        }

        private static int Best(ActionDescriptor a, string q)
        {
            int best = Score(Fold(a.Title), q);
            foreach (var al in a.Aliases) best = Math.Max(best, Score(Fold(al), q));
            best = Math.Max(best, Math.Min(Substring, Score(Fold(a.Category), q)));    // the category alone never beats a title
            return best;
        }

        /// <summary>
        /// Actions the caller may start, best match first; ties go to the ones used most recently. Empty text = the
        /// recent ones. Unavailable actions and those whose feature is off are left out.
        /// </summary>
        public IReadOnlyList<ActionDescriptor> Search(string text, ActionInvoker invoker, int max = 10)
        {
            if (max <= 0) return Array.Empty<ActionDescriptor>();
            string q = Fold(text);
            var recent = Recent.ToList();
            int Rank(ActionDescriptor a) { int i = recent.IndexOf(a.Id); return i < 0 ? int.MaxValue : i; }
            var usable = All.Where(a => (a.AllowedInvokers & invoker) == invoker && invoker != ActionInvoker.None && FeatureOn(a) && Available(a));
            if (q.Length == 0)
                return usable.Where(a => recent.Contains(a.Id)).OrderBy(Rank).Take(max).ToList();
            return usable.Select(a => (a, s: Best(a, q))).Where(x => x.s > 0)
                         .OrderByDescending(x => x.s).ThenBy(x => Rank(x.a)).ThenBy(x => x.a.Title, StringComparer.CurrentCulture)
                         .Take(max).Select(x => x.a).ToList();
        }

        private bool FeatureOn(ActionDescriptor a)
        {
            if (string.IsNullOrEmpty(a.FeatureId)) return true;
            var f = _flags ?? FeatureFlags.Current;
            return f != null && f.IsEnabled(a.FeatureId);
        }

        private static bool Available(ActionDescriptor a)
        {
            try { return a.IsAvailable(); } catch { return false; }
        }

        // ------------------------------------------------------------------ invoke

        /// <summary>
        /// Runs an action after checking: it exists, the caller may start it, it is available, its feature is on and the
        /// parameters are valid. Runs on the UI thread if it needs to, with a timeout (10 s by default). Never throws.
        /// </summary>
        public async Task<ActionResult> InvokeAsync(string id, IReadOnlyDictionary<string, string> args, ActionInvoker invoker, CancellationToken ct = default)
        {
            ActionResult result;
            ActionDescriptor a = null;
            try
            {
                a = Get(id);
                result = await Run(a, args, invoker, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                result = ActionResult.Failed("Acțiunea nu a reușit.");
                Report(a, ex);
            }
            result ??= ActionResult.Failed("Acțiunea nu a reușit.");
            if (result.Success && a != null)
                lock (_lock) { _recent.Remove(a.Id); _recent.Insert(0, a.Id); if (_recent.Count > MaxRecent) _recent.RemoveRange(MaxRecent, _recent.Count - MaxRecent); }
            string shownId = a?.Id ?? (IsValidId(id) ? id : "(necunoscută)");      // an unknown id from outside isn't echoed raw
            _log?.Invoke("Acțiune " + shownId + " (" + invoker + "): " + (result.Success ? "reușită" : "eșuată"));
            try { ActionInvoked?.Invoke(shownId, invoker, result.Success); }
            catch (Exception ex) { _log?.Invoke("Acțiuni: un abonat a dat eroare: " + ex.GetType().Name); }
            return result;
        }

        private async Task<ActionResult> Run(ActionDescriptor a, IReadOnlyDictionary<string, string> raw, ActionInvoker invoker, CancellationToken ct)
        {
            if (a == null) return ActionResult.Failed("Acțiunea nu există.");
            if (invoker == ActionInvoker.None || (a.AllowedInvokers & invoker) != invoker) return ActionResult.Failed("Acțiunea nu poate fi pornită de aici.");
            if (!FeatureOn(a)) return ActionResult.Failed("Funcția e oprită (Setări › Funcții noi).");
            if (!Available(a)) return ActionResult.Failed(a.UnavailableMessage ?? "Acțiunea nu e disponibilă acum.");

            // parameters: every one known, required ones present, each value valid
            var values = new Dictionary<string, object>(StringComparer.Ordinal);
            raw ??= new Dictionary<string, string>();
            foreach (var k in raw.Keys)
                if (!a.Parameters.Any(p => p.Name == k)) return ActionResult.Failed("Parametru necunoscut.");
            foreach (var p in a.Parameters)
            {
                if (!raw.TryGetValue(p.Name, out var text))
                {
                    if (p.Required) return ActionResult.Failed("Lipsește „" + p.Title + "”.");
                    continue;
                }
                if (!p.TryConvert(text, out var v, out var err)) return ActionResult.Failed(err);
                values[p.Name] = v;
            }
            var args = new ActionArgs(values);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(a.Timeout ?? DefaultTimeout);
            Task<ActionResult> work;
            try
            {
                work = a.RequiresUiThread ? _ui.InvokeAsync(() => a.ExecuteAsync(args, cts.Token)) : a.ExecuteAsync(args, cts.Token);
            }
            catch (Exception ex) { Report(a, ex); return ActionResult.Failed("Acțiunea nu a reușit."); }
            if (work == null) return ActionResult.Failed("Acțiunea nu a reușit.");

            var stop = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (cts.Token.Register(() => stop.TrySetResult(true)))
            {
                var first = await Task.WhenAny(work, stop.Task).ConfigureAwait(false);
                if (first != work)
                {
                    // the action keeps its token (now cancelled); a late failure is only observed, never thrown
                    _ = work.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
                    return ActionResult.Failed(ct.IsCancellationRequested ? "Anulat." : "Acțiunea a durat prea mult.");
                }
            }
            try { return await work.ConfigureAwait(false) ?? ActionResult.Failed("Acțiunea nu a reușit."); }
            catch (OperationCanceledException) { return ActionResult.Failed(ct.IsCancellationRequested ? "Anulat." : "Acțiunea a durat prea mult."); }
            catch (Exception ex) { Report(a, ex); return ActionResult.Failed("Acțiunea nu a reușit."); }
        }

        private void Report(ActionDescriptor a, Exception ex)
        {
            _log?.Invoke("Acțiune " + (a?.Id ?? "(necunoscută)") + ": eroare " + ex.GetType().Name);
            if (a != null && !string.IsNullOrEmpty(a.FeatureId)) (_flags ?? FeatureFlags.Current)?.ReportError(a.FeatureId, ex);
        }
    }
}
