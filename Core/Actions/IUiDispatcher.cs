using System;
using System.Threading.Tasks;

namespace WinNotch.Core.Actions
{
    /// <summary>Runs work on the UI thread (WPF in the app, <see cref="InlineUiDispatcher"/> in the tests).</summary>
    public interface IUiDispatcher
    {
        Task<ActionResult> InvokeAsync(Func<Task<ActionResult>> work);
    }

    /// <summary>Runs the work right away, on the calling thread (tests, or code that is already on the UI thread).</summary>
    public sealed class InlineUiDispatcher : IUiDispatcher
    {
        /// <summary>How many times work was sent through here (the tests check it).</summary>
        public int Calls { get; private set; }

        public Task<ActionResult> InvokeAsync(Func<Task<ActionResult>> work)
        {
            Calls++;
            return work();
        }
    }
}
