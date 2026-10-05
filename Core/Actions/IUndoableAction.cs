using System;
using System.Threading;
using System.Threading.Tasks;

namespace WinNotch.Core.Actions
{
    /// <summary>An action that can be undone (P52 will list and call these).</summary>
    public interface IUndoableAction
    {
        Task<ActionResult> UndoAsync(UndoToken token, CancellationToken ct);
    }

    /// <summary>An action built with delegates that can also be undone.</summary>
    public sealed class UndoableAction : ActionDescriptor, IUndoableAction
    {
        private readonly Func<UndoToken, CancellationToken, Task<ActionResult>> _undo;

        public UndoableAction(string id, string title, Func<ActionArgs, CancellationToken, Task<ActionResult>> execute,
                              Func<UndoToken, CancellationToken, Task<ActionResult>> undo, Func<bool> available = null)
            : base(id, title, execute, available)
        {
            _undo = undo;
        }

        public Task<ActionResult> UndoAsync(UndoToken token, CancellationToken ct)
        {
            if (token == null || token.ActionId != Id) return Task.FromResult(ActionResult.Failed("Nu am ce anula."));
            return _undo(token, ct);
        }
    }
}
