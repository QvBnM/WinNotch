using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using WinNotch.Core.Actions;
using WinNotch.Core.Context;

namespace WinNotch.Features.CommandBar
{
    /// <summary>The two shortcuts the Command Bar can use (Settings › Comportament). No other new shortcut exists.</summary>
    public enum CommandBarKey { Space, K }

    /// <summary>The shortcut's names, keys and the texts of the conflict alert (P14, ADR 0007). No WPF: tested.</summary>
    public static class CommandBarHotkeys
    {
        /// <summary>Values of AppSettings.CommandBarKey.</summary>
        public const string SpaceSetting = "space", KSetting = "k";
        /// <summary>Virtual-key codes for RegisterHotKey (with MOD_WIN | MOD_ALT | MOD_NOREPEAT).</summary>
        public const uint VkSpace = 0x20, VkK = 0x4B;

        /// <summary>"k" → K; anything else (missing, hand-edited, unknown) → the default, Space.</summary>
        public static CommandBarKey Parse(string setting) =>
            string.Equals(setting?.Trim(), KSetting, StringComparison.OrdinalIgnoreCase) ? CommandBarKey.K : CommandBarKey.Space;

        public static string ToSetting(CommandBarKey key) => key == CommandBarKey.K ? KSetting : SpaceSetting;

        public static uint VirtualKey(CommandBarKey key) => key == CommandBarKey.K ? VkK : VkSpace;

        public static string Label(CommandBarKey key) => key == CommandBarKey.K ? "Win+Alt+K" : "Win+Alt+Space";

        /// <summary>The one to propose when <paramref name="key"/> is taken.</summary>
        public static CommandBarKey Other(CommandBarKey key) => key == CommandBarKey.K ? CommandBarKey.Space : CommandBarKey.K;

        /// <summary>Title of the conflict alert.</summary>
        public static string ConflictTitle(CommandBarKey key) => Label(key) + " e folosită de altă aplicație";

        /// <summary>Second line of the conflict alert: the other shortcut and where to choose it.</summary>
        public static string ConflictHint(CommandBarKey key) => "Alege " + Label(Other(key)) + " în Setări › Comportament › Scurtătura Command Bar";
    }

    public enum HotkeyChange { None, Registered, Unregistered, Failed }

    /// <summary>What <see cref="CommandBarHotkey.Apply"/> did.</summary>
    public readonly struct HotkeyResult
    {
        public HotkeyResult(HotkeyChange change, CommandBarKey key, bool showAlert) { Change = change; Key = key; ShowAlert = showAlert; }
        public HotkeyChange Change { get; }
        public CommandBarKey Key { get; }
        /// <summary>Show the conflict alert now: the first failure of this key in this run (one alert, never a stream).</summary>
        public bool ShowAlert { get; }
    }

    /// <summary>
    /// Registers the Command Bar's shortcut only while the "command-bar" switch is on, re-registers it when the chosen key
    /// changes and decides when the conflict alert is shown (once per key and run). The Win32 calls are injected (tests).
    /// </summary>
    public sealed class CommandBarHotkey
    {
        private readonly Func<uint, bool> _register;
        private readonly Action _unregister;
        private readonly HashSet<CommandBarKey> _alerted = new HashSet<CommandBarKey>();

        public CommandBarHotkey(Func<uint, bool> register, Action unregister)
        {
            _register = register ?? throw new ArgumentNullException(nameof(register));
            _unregister = unregister ?? throw new ArgumentNullException(nameof(unregister));
        }

        /// <summary>The key registered now (null: none).</summary>
        public CommandBarKey? Active { get; private set; }
        /// <summary>The key that could not be registered (taken by another app), while the switch is on.</summary>
        public CommandBarKey? Conflict { get; private set; }

        public HotkeyResult Apply(bool featureOn, CommandBarKey wanted)
        {
            if (!featureOn)
            {
                Conflict = null;
                if (Active is CommandBarKey was) { Active = null; SafeUnregister(); return new HotkeyResult(HotkeyChange.Unregistered, was, false); }
                return new HotkeyResult(HotkeyChange.None, wanted, false);
            }
            if (Active == wanted) return new HotkeyResult(HotkeyChange.None, wanted, false);
            if (Active != null) { Active = null; SafeUnregister(); }
            bool ok;
            try { ok = _register(CommandBarHotkeys.VirtualKey(wanted)); }
            catch (Exception) { ok = false; }
            if (ok)
            {
                Active = wanted;
                Conflict = null;
                return new HotkeyResult(HotkeyChange.Registered, wanted, false);
            }
            bool again = Conflict == wanted;            // tried again (Settings saved): same state, nothing new to say
            Conflict = wanted;
            bool alert = _alerted.Add(wanted);
            return new HotkeyResult(again && !alert ? HotkeyChange.None : HotkeyChange.Failed, wanted, alert);
        }

        private void SafeUnregister()
        {
            try { _unregister(); } catch (Exception) { /* the window may already be gone */ }
        }
    }

    public enum ShortcutDecision { Open, Close, Ignore }

    /// <summary>When the shortcut opens the Command Bar. Pure: the notch passes what it sees.</summary>
    public static class CommandBarRules
    {
        public const string FeatureId = "command-bar";
        /// <summary>Alert ids (through the notch's Alert, so the Activity Manager handles them when it is on).</summary>
        public const string ConflictAlertId = "command-bar-hotkey", ResultAlertId = "command-bar-result";

        /// <summary>
        /// Switch off → nothing. Open → the shortcut closes it. A fullscreen app in front (game, video, presentation), the
        /// pill hidden for fullscreen, a screen tool running or the notch in edit mode → nothing: the Command Bar never
        /// takes the keyboard from a fullscreen app.
        /// </summary>
        public static ShortcutDecision OnShortcut(bool featureOn, bool isOpen, bool foregroundFullscreen, bool notchHidden, bool busy)
        {
            if (!featureOn) return ShortcutDecision.Ignore;
            if (isOpen) return ShortcutDecision.Close;
            if (foregroundFullscreen || notchHidden || busy) return ShortcutDecision.Ignore;
            return ShortcutDecision.Open;
        }

        /// <summary>
        /// The window in front covers its monitor or runs exclusive fullscreen (the context engine's foreground reading),
        /// or the context engine already says fullscreen.
        /// </summary>
        public static bool IsFullscreen(ForegroundInfo foreground, FullscreenKind context) =>
            (foreground != null && (foreground.CoversMonitor || foreground.ExclusiveFullscreen)) || context != FullscreenKind.None;
    }

    /// <summary>One result row: an action, with the parameters read from the text ("volum 30" → valoare = 30).</summary>
    public sealed class CommandItem
    {
        public CommandItem(ActionDescriptor action, IReadOnlyDictionary<string, string> args)
        {
            Action = action ?? throw new ArgumentNullException(nameof(action));
            Args = args ?? new Dictionary<string, string>(StringComparer.Ordinal);
        }

        public ActionDescriptor Action { get; }
        public IReadOnlyDictionary<string, string> Args { get; }
        public string Id => Action.Id;

        /// <summary>The first required parameter the text did not give, or null.</summary>
        public ActionParameter MissingParameter => Action.Parameters.FirstOrDefault(p => p.Required && !Args.ContainsKey(p.Name));

        public bool NeedsConfirm => Action.Safety == ActionSafety.Confirm;

        /// <summary>The values as shown on the row ("30%", "dreapta"); "" without parameters. Never logged.</summary>
        public string Detail
        {
            get
            {
                var parts = new List<string>();
                foreach (var p in Action.Parameters)
                {
                    if (!Args.TryGetValue(p.Name, out var v)) continue;
                    v = (v ?? "").Trim();
                    if (p.Kind == ParamKind.Percent) v = v.TrimEnd('%').Trim() + "%";
                    if (v.Length > 40) v = v.Substring(0, 40) + "…";
                    parts.Add(v);
                }
                return string.Join(" · ", parts);
            }
        }
    }

    /// <summary>
    /// The text typed in the Command Bar → result rows, over <see cref="ActionRegistry.Search"/>. Trailing words can be the
    /// action's parameters ("volum 30", "volum 30%"): the words before them find the action, the rest are checked with the
    /// parameter's own rules (<see cref="ActionParameter.TryConvert"/>). Dangerous actions never appear. Nothing is logged.
    /// </summary>
    public static class CommandBarSearch
    {
        public const int MaxResults = 7;
        /// <summary>Longer text is cut (the box also limits it).</summary>
        public const int MaxTextLength = 200;
        /// <summary>At most this many trailing words are tried as parameters.</summary>
        public const int MaxParameterWords = 8;
        private const int Candidates = 20;

        public static IReadOnlyList<CommandItem> Find(ActionRegistry registry, string text, int max = MaxResults)
        {
            var list = new List<CommandItem>();
            if (registry == null || max <= 0) return list;
            text = (text ?? "").Trim();
            if (text.Length > MaxTextLength) text = text.Substring(0, MaxTextLength);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var words = text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);

            // 1) "<action words> <values>": the longest action text first (fewest values)
            for (int split = words.Length - 1; split >= 1 && split >= words.Length - MaxParameterWords; split--)
            {
                string head = string.Join(" ", words, 0, split);
                var tail = words.Skip(split).ToArray();
                foreach (var a in registry.Search(head, ActionInvoker.CommandBar, Candidates))
                {
                    if (a.Parameters.Count == 0 || !Allowed(a) || seen.Contains(a.Id)) continue;
                    if (!TryBind(a, tail, out var args)) continue;
                    seen.Add(a.Id);
                    list.Add(new CommandItem(a, args));
                }
            }

            // 2) the whole text as the action's name (empty text: the recent ones)
            foreach (var a in registry.Search(text, ActionInvoker.CommandBar, Candidates))
            {
                if (!Allowed(a) || !seen.Add(a.Id)) continue;
                list.Add(new CommandItem(a, null));
            }
            return list.Take(max).ToList();
        }

        /// <summary>Never Dangerous (not even after a confirmation); only actions the Command Bar may start.</summary>
        public static bool Allowed(ActionDescriptor a) =>
            a != null && a.Safety != ActionSafety.Dangerous && (a.AllowedInvokers & ActionInvoker.CommandBar) == ActionInvoker.CommandBar;

        /// <summary>
        /// The trailing words as the action's parameters, in order; extra words go into a last Text parameter. True only if
        /// every required parameter gets a value and every value passes its check.
        /// </summary>
        public static bool TryBind(ActionDescriptor a, IReadOnlyList<string> values, out Dictionary<string, string> args)
        {
            var bound = new Dictionary<string, string>(StringComparer.Ordinal);
            args = bound;
            var ps = a?.Parameters ?? Array.Empty<ActionParameter>();
            if (ps.Count == 0 || values == null || values.Count == 0) return false;
            if (values.Count > ps.Count && ps[ps.Count - 1].Kind != ParamKind.Text) return false;
            int n = Math.Min(values.Count, ps.Count);
            for (int i = 0; i < n; i++)
            {
                string v = i == ps.Count - 1 && values.Count > ps.Count ? string.Join(" ", values.Skip(i)) : values[i];
                if (!ps[i].TryConvert(v, out _, out _)) return false;
                bound[ps[i].Name] = v;
            }
            return ps.Where(p => p.Required).All(p => bound.ContainsKey(p.Name));
        }
    }

    public enum EnterOutcome { Nothing, NeedsParameters, AskConfirm, Invoke }

    public readonly struct EnterDecision
    {
        public EnterDecision(EnterOutcome outcome, CommandItem item, bool confirmed) { Outcome = outcome; Item = item; Confirmed = confirmed; }
        public EnterOutcome Outcome { get; }
        public CommandItem Item { get; }
        /// <summary>Invoke with confirmed: true (the user pressed Enter twice on a Confirm action).</summary>
        public bool Confirmed { get; }
    }

    /// <summary>
    /// The open Command Bar's state: results, the selected row, the pending confirmation and the line under the results.
    /// A Confirm action needs Enter twice; editing the text or moving the selection cancels the pending confirmation.
    /// </summary>
    public sealed class CommandBarSession
    {
        public const string ConfirmMessage = "Apasă Enter din nou pentru a confirma";
        public const string NothingFound = "Nicio acțiune găsită";

        public string Text { get; private set; } = "";
        public IReadOnlyList<CommandItem> Results { get; private set; } = Array.Empty<CommandItem>();
        /// <summary>Index of the selected row; -1 without results.</summary>
        public int Selected { get; private set; } = -1;
        /// <summary>Id of the action waiting for the second Enter, or null.</summary>
        public string PendingConfirm { get; private set; }
        /// <summary>The line under the results (confirmation, missing value, nothing found), or null.</summary>
        public string Message { get; private set; }

        public CommandItem SelectedItem => Selected >= 0 && Selected < Results.Count ? Results[Selected] : null;

        /// <summary>The text changed: new results, the first one selected, any pending confirmation cancelled.</summary>
        public void SetResults(string text, IReadOnlyList<CommandItem> results)
        {
            Text = text ?? "";
            Results = results ?? Array.Empty<CommandItem>();
            Selected = Results.Count > 0 ? 0 : -1;
            PendingConfirm = null;
            Message = Results.Count == 0 && Text.Trim().Length > 0 ? NothingFound : null;
        }

        /// <summary>Arrow keys (wrapping around). Cancels a pending confirmation. False without results.</summary>
        public bool Move(int delta)
        {
            if (Results.Count == 0) return false;
            int n = Results.Count;
            Selected = (((Selected < 0 ? 0 : Selected) + delta) % n + n) % n;
            CancelPending();
            return true;
        }

        /// <summary>A row clicked: selected (another row cancels a pending confirmation).</summary>
        public bool Select(int index)
        {
            if (index < 0 || index >= Results.Count) return false;
            if (index != Selected) { Selected = index; CancelPending(); }
            return true;
        }

        private void CancelPending()
        {
            PendingConfirm = null;
            if (Message == ConfirmMessage || (Message != null && Message.StartsWith("Scrie și", StringComparison.Ordinal))) Message = null;
        }

        public EnterDecision Enter()
        {
            var item = SelectedItem;
            if (item == null || !CommandBarSearch.Allowed(item.Action)) return new EnterDecision(EnterOutcome.Nothing, null, false);
            var missing = item.MissingParameter;
            if (missing != null)
            {
                PendingConfirm = null;
                Message = MissingMessage(missing);
                return new EnterDecision(EnterOutcome.NeedsParameters, item, false);
            }
            if (item.NeedsConfirm)
            {
                if (PendingConfirm == item.Id)
                {
                    PendingConfirm = null;
                    Message = null;
                    return new EnterDecision(EnterOutcome.Invoke, item, true);
                }
                PendingConfirm = item.Id;
                Message = ConfirmMessage;
                return new EnterDecision(EnterOutcome.AskConfirm, item, false);
            }
            return new EnterDecision(EnterOutcome.Invoke, item, false);
        }

        /// <summary>"Scrie și „Volum” după comandă (de exemplu „… 30”)".</summary>
        public static string MissingMessage(ActionParameter p)
        {
            string example = p.Kind switch
            {
                ParamKind.Percent => " (de exemplu „… 30”)",
                ParamKind.Int => " (de exemplu „… " + (p.Min > 0 ? p.Min : 1).ToString(CultureInfo.InvariantCulture) + "”)",
                ParamKind.Enum when p.Values.Count > 0 => " (" + string.Join(" / ", p.Values.Take(3)) + ")",
                _ => "",
            };
            return "Scrie și „" + p.Title + "” după comandă" + example;
        }
    }

    /// <summary>Sizes of the open Command Bar, before the notch's scale (pure: the height follows the rows).</summary>
    public static class CommandBarLayout
    {
        public const double Width = 560, BoxHeight = 52, RowHeight = 38, ListPadding = 8, MessageHeight = 26, Radius = 18;

        public static double Height(int rows, bool message)
        {
            rows = Math.Clamp(rows, 0, CommandBarSearch.MaxResults);
            return BoxHeight + (rows > 0 ? rows * RowHeight + ListPadding : 0) + (message ? MessageHeight : 0);
        }
    }
}
