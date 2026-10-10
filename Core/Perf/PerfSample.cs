using System;
using System.Collections.Generic;
using System.Linq;

namespace WinNotch.Core.Perf
{
    /// <summary>
    /// What one process used over the last pass. <see cref="Name"/> is the process name, lowercase, without ".exe"
    /// (like <c>ContextSnapshot.ForegroundProcess</c>): never a window title, never a path.
    /// </summary>
    public readonly struct ProcUsage
    {
        public ProcUsage(string name, double cpuPercent, double workingSetMb, double privateMb, double gpuPercent, double vramMb, int processes)
        {
            Name = name ?? "";
            CpuPercent = cpuPercent; WorkingSetMb = workingSetMb; PrivateMb = privateMb;
            GpuPercent = gpuPercent; VramMb = vramMb; Processes = Math.Max(1, processes);
        }

        /// <summary>Process name, lowercase, without the extension ("chrome", "cs2").</summary>
        public string Name { get; }
        /// <summary>Share of the whole machine's CPU, 0..100 (not 0..100 per core).</summary>
        public double CpuPercent { get; }
        public double WorkingSetMb { get; }
        /// <summary>Private bytes: the memory only this process holds — the number that grows when something leaks.</summary>
        public double PrivateMb { get; }
        /// <summary>Share of the GPU, 0..100, summed over its engines.</summary>
        public double GpuPercent { get; }
        public double VramMb { get; }
        /// <summary>How many processes were added up under this name (Chrome has dozens).</summary>
        public int Processes { get; }
    }

    /// <summary>
    /// One moment of the machine, as one immutable value. Built by the sampler; everything that draws or decides reads
    /// it and never measures on its own. Nothing here is personal: process names, numbers, no titles and no paths.
    /// </summary>
    public sealed record PerfSample
    {
        public static readonly PerfSample Empty = new PerfSample();

        public DateTime TimeUtc { get; init; }

        /// <summary>Whole-machine CPU, 0..100.</summary>
        public double CpuPercent { get; init; }
        public double RamUsedGb { get; init; }
        public double RamTotalGb { get; init; }
        /// <summary>
        /// Committed memory: what every process has asked Windows to back, RAM plus pagefile. This, not "free RAM", is
        /// the number that says whether the machine is actually out of memory.
        /// </summary>
        public double CommitUsedGb { get; init; }
        public double CommitLimitGb { get; init; }

        /// <summary>Busiest GPU engine, 0..100; -1 when the GPU counters are not available.</summary>
        public double GpuPercent { get; init; } = -1;
        /// <summary>The 3D engine on its own, 0..100; -1 when unknown. In a game this is the one that matters.</summary>
        public double Gpu3dPercent { get; init; } = -1;
        /// <summary>Dedicated video memory in use, MB; -1 when unknown.</summary>
        public double VramUsedMb { get; init; } = -1;

        /// <summary>The processes that used the most, biggest first. Empty on the samples between two expensive passes.</summary>
        public IReadOnlyList<ProcUsage> Top { get => _top; init => _top = value ?? Array.Empty<ProcUsage>(); }
        private readonly IReadOnlyList<ProcUsage> _top = Array.Empty<ProcUsage>();

        public bool HasGpu => GpuPercent >= 0;
        public bool HasProcesses => _top.Count > 0;
        public double RamPercent => RamTotalGb > 0 ? Math.Clamp(RamUsedGb / RamTotalGb * 100, 0, 100) : 0;
        public double CommitPercent => CommitLimitGb > 0 ? Math.Clamp(CommitUsedGb / CommitLimitGb * 100, 0, 100) : 0;

        /// <summary>
        /// For the log: round numbers only, and never the process list. One line, so a sample can be traced without
        /// writing anything about what the user is doing.
        /// </summary>
        public string ToLogString() =>
            "cpu " + Math.Round(CpuPercent) + "%" +
            ", ram " + RamUsedGb.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "/" +
            RamTotalGb.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " GB" +
            ", commit " + Math.Round(CommitPercent) + "%" +
            (HasGpu ? ", gpu " + Math.Round(GpuPercent) + "%" : ", gpu -") +
            (VramUsedMb >= 0 ? ", vram " + Math.Round(VramUsedMb) + " MB" : "");
    }
}
