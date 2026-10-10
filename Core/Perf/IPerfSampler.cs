using System;

namespace WinNotch.Core.Perf
{
    /// <summary>
    /// One pass of measurement. The implementation that talks to Windows lives in <c>Features/Performance</c>; this
    /// interface is what <see cref="PerfMonitor"/> knows, so the monitor's timing, its viewer counting and the way it
    /// retires a sampler can all be tested without a machine under load.
    /// </summary>
    public interface IPerfSampler : IDisposable
    {
        /// <summary>
        /// Reads the machine. <paramref name="withProcesses"/> adds the expensive per-process pass; without it the
        /// sample carries the totals only.
        /// </summary>
        PerfSample Sample(bool withProcesses);
    }
}
