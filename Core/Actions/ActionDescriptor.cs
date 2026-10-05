using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace WinNotch.Core.Actions
{
    /// <summary>How careful the caller must be. Only Safe actions may be offered to the local API.</summary>
    public enum ActionSafety { Safe, Confirm, Dangerous }

    /// <summary>Who may start an action.</summary>
    [Flags]
    public enum ActionInvoker
    {
        None = 0,
        UI = 1,
        CommandBar = 2,
        QuickAction = 4,
        Workflow = 8,
        LocalApi = 16,
        /// <summary>Everyone inside the app; never the local API unless asked for (and only for Safe actions).</summary>
        Default = UI | CommandBar | QuickAction | Workflow,
    }

    public enum ParamKind { Int, Percent, Enum, Text }

    /// <summary>A typed parameter. Values arrive as text (Command Bar, local API) and are checked before the action runs.</summary>
    public sealed class ActionParameter
    {
        public string Name { get; init; }
        /// <summary>Shown to the user (Romanian).</summary>
        public string Title { get; init; }
        public ParamKind Kind { get; init; }
        public int Min { get; init; } = int.MinValue;
        public int Max { get; init; } = int.MaxValue;
        public IReadOnlyList<string> Values { get; init; } = Array.Empty<string>();
        public int MaxLength { get; init; } = 200;
        public bool Required { get; init; } = true;

        public static ActionParameter Int(string name, string title, int min, int max) => new ActionParameter { Name = name, Title = title, Kind = ParamKind.Int, Min = min, Max = max };
        public static ActionParameter Percent(string name, string title) => new ActionParameter { Name = name, Title = title, Kind = ParamKind.Percent, Min = 0, Max = 100 };
        public static ActionParameter Enum(string name, string title, params string[] values) => new ActionParameter { Name = name, Title = title, Kind = ParamKind.Enum, Values = values };
        public static ActionParameter Text(string name, string title, int maxLength) => new ActionParameter { Name = name, Title = title, Kind = ParamKind.Text, MaxLength = maxLength };

        /// <summary>Checks and converts a value; the error is a short Romanian sentence (without the value itself).</summary>
        public bool TryConvert(string raw, out object value, out string error)
        {
            value = null; error = null;
            string text = raw?.Trim() ?? "";
            switch (Kind)
            {
                case ParamKind.Int:
                case ParamKind.Percent:
                    if (Kind == ParamKind.Percent) text = text.TrimEnd('%').Trim();
                    if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)) { error = "„" + Title + "” trebuie să fie un număr."; return false; }
                    if (n < Min || n > Max) { error = "„" + Title + "” trebuie să fie între " + Min + " și " + Max + "."; return false; }
                    value = n; return true;
                case ParamKind.Enum:
                    var match = Values.FirstOrDefault(v => string.Equals(v, text, StringComparison.OrdinalIgnoreCase));
                    if (match == null) { error = "„" + Title + "” nu are o valoare cunoscută."; return false; }
                    value = match; return true;
                default:
                    if (raw == null) { error = "Lipsește „" + Title + "”."; return false; }
                    if (raw.Length > MaxLength) { error = "„" + Title + "” e prea lung (maximum " + MaxLength + " de caractere)."; return false; }
                    value = raw; return true;
            }
        }
    }

    /// <summary>Checked parameter values, as the action reads them.</summary>
    public sealed class ActionArgs
    {
        public static readonly ActionArgs Empty = new ActionArgs(new Dictionary<string, object>());
        private readonly IReadOnlyDictionary<string, object> _values;

        public ActionArgs(IReadOnlyDictionary<string, object> values) { _values = values; }

        public bool Has(string name) => _values.ContainsKey(name);
        public int GetInt(string name, int fallback = 0) => _values.TryGetValue(name, out var v) && v is int i ? i : fallback;
        public string GetText(string name, string fallback = null) => _values.TryGetValue(name, out var v) && v is string s ? s : fallback;
    }

    /// <summary>Lets an action be undone later (Undo Center, P52). The token holds what's needed, never a secret.</summary>
    public sealed class UndoToken
    {
        public UndoToken(string actionId, string state) { ActionId = actionId; State = state; At = DateTime.UtcNow; }
        public string ActionId { get; }
        /// <summary>The state before the action (e.g. "35" for a volume).</summary>
        public string State { get; }
        public DateTime At { get; }
    }

    public sealed class ActionResult
    {
        public bool Success { get; private init; }
        /// <summary>Short Romanian sentence for the user.</summary>
        public string Message { get; private init; } = "";
        public UndoToken Undo { get; private init; }

        public static ActionResult Ok(string message = "", UndoToken undo = null) => new ActionResult { Success = true, Message = message ?? "", Undo = undo };
        public static ActionResult Failed(string message) => new ActionResult { Success = false, Message = message ?? "" };
        public static Task<ActionResult> OkTask(string message = "", UndoToken undo = null) => Task.FromResult(Ok(message, undo));
    }

    /// <summary>
    /// Something WinNotch can do, with a stable id ("zonă.verb", e.g. "audio.mute-mic"). Built with delegates for the
    /// common case, or subclassed. Registered once in <see cref="ActionRegistry"/>, which checks who may call it, its
    /// availability, its feature flag and its parameters before running it.
    /// </summary>
    public class ActionDescriptor
    {
        private readonly Func<ActionArgs, CancellationToken, Task<ActionResult>> _execute;
        private readonly Func<bool> _available;

        public ActionDescriptor(string id, string title, Func<ActionArgs, CancellationToken, Task<ActionResult>> execute, Func<bool> available = null)
        {
            Id = id; Title = title; _execute = execute; _available = available;
        }

        protected ActionDescriptor(string id, string title) { Id = id; Title = title; }

        public string Id { get; }
        /// <summary>Romanian, as the user sees it.</summary>
        public string Title { get; }
        /// <summary>Other words that find it, Romanian and English ("mute", "mut", "taie microfonul").</summary>
        public IReadOnlyList<string> Aliases { get; init; } = Array.Empty<string>();
        public string Category { get; init; } = "";
        /// <summary>A glyph from the icon font already used by the app (Segoe Fluent Icons / MDL2).</summary>
        public string Icon { get; init; } = "";
        public IReadOnlyList<ActionParameter> Parameters { get; init; } = Array.Empty<ActionParameter>();
        public ActionSafety Safety { get; init; } = ActionSafety.Safe;
        public ActionInvoker AllowedInvokers { get; init; } = ActionInvoker.Default;
        /// <summary>The feature flag that must be on (null = always on).</summary>
        public string FeatureId { get; init; }
        /// <summary>Must run on the UI thread (it touches windows, the clipboard, COM shell objects).</summary>
        public bool RequiresUiThread { get; init; }
        /// <summary>Null = the registry's default (10 s).</summary>
        public TimeSpan? Timeout { get; init; }
        /// <summary>Said when <see cref="IsAvailable"/> is false (e.g. "Nu e niciun stick USB conectat.").</summary>
        public string UnavailableMessage { get; init; }

        public virtual bool IsAvailable() => _available?.Invoke() ?? true;

        public virtual Task<ActionResult> ExecuteAsync(ActionArgs args, CancellationToken ct) =>
            _execute != null ? _execute(args, ct) : ActionResult.OkTask();
    }

    /// <summary>Actions that come and go (one per workspace, per USB drive…). Read again after <see cref="Changed"/> or a registry refresh.</summary>
    public interface IActionProvider
    {
        IEnumerable<ActionDescriptor> GetActions();
        /// <summary>The list changed: the registry drops its cached copy.</summary>
        event Action Changed;
    }
}
