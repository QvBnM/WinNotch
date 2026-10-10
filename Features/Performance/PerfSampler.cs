using System;
using System.Collections.Generic;
using System.Diagnostics;
using WinNotch.Core.Perf;
using WinNotch.Services;

namespace WinNotch.Features.Performance
{
    /// <summary>
    /// P60: one pass of measurement. Knows how to read the machine; knows nothing about when to do it (that is
    /// <see cref="PerfMonitor"/>) and nothing about how to draw it.
    /// <para>Everything here is cheap and unprivileged: <c>GetSystemTimes</c> for the CPU,
    /// <c>GlobalMemoryStatusEx</c> for memory and commit, Windows' GPU counters for the graphics card. The expensive
    /// half — walking every process — is asked for separately, because <see cref="PerfRules.ProcessSeconds"/> keeps it
    /// slow even while a game runs.</para>
    /// </summary>
    internal sealed class PerfSampler : IDisposable
    {
        private readonly GpuCounters _gpu = new GpuCounters();
        private long _idle, _kernel, _user;
        private Dictionary<int, RawProc> _lastProcs = new Dictionary<int, RawProc>();
        private DateTime _lastProcTime;

        /// <summary>The last reading of the GPU counters, kept so the samples between two passes still show a value.</summary>
        private GpuReading _lastGpu = new GpuReading();

        /// <summary>
        /// Reads the machine. <paramref name="withProcesses"/> adds the per-process pass; without it the sample has the
        /// totals only and <c>Top</c> stays empty.
        /// </summary>
        public PerfSample Sample(bool withProcesses)
        {
            var now = DateTime.UtcNow;
            double cpu = ReadCpu();
            var (ramUsed, ramTotal, commitUsed, commitLimit) = ReadMemory();

            var gpu = _gpu.Read();
            if (gpu.BusiestPercent >= 0 || gpu.ByPid.Count > 0) _lastGpu = gpu; else gpu = _lastGpu;

            IReadOnlyList<ProcUsage> top = Array.Empty<ProcUsage>();
            if (withProcesses)
            {
                var procs = ReadProcesses();
                double wallMs = _lastProcTime == default ? 0 : (now - _lastProcTime).TotalMilliseconds;
                var merged = ProcessRollup.Merge(_lastProcs, procs, wallMs, Environment.ProcessorCount, gpu.ByPid, gpu.VramByPid);
                var next = new Dictionary<int, RawProc>(procs.Count);
                foreach (var p in procs) next[p.Pid] = p;
                _lastProcs = next;
                _lastProcTime = now;
                top = merged;
            }

            return new PerfSample
            {
                TimeUtc = now,
                CpuPercent = cpu,
                RamUsedGb = ramUsed,
                RamTotalGb = ramTotal,
                CommitUsedGb = commitUsed,
                CommitLimitGb = commitLimit,
                GpuPercent = gpu.BusiestPercent,
                Gpu3dPercent = gpu.Percent3d,
                VramUsedMb = gpu.VramMb,
                Top = top,
            };
        }

        /// <summary>The counters have not answered yet (a fresh query needs one pass to prime).</summary>
        public bool GpuAvailable => _gpu.Available;

        private double ReadCpu()
        {
            try
            {
                if (!Native.GetSystemTimes(out long idle, out long kernel, out long user)) return 0;
                long di = idle - _idle, dk = kernel - _kernel, du = user - _user;
                long total = dk + du;
                double value = _kernel != 0 && total > 0 ? Math.Clamp((total - di) * 100.0 / total, 0, 100) : 0;
                _idle = idle; _kernel = kernel; _user = user;
                return value;
            }
            catch { return 0; }
        }

        private static (double RamUsed, double RamTotal, double CommitUsed, double CommitLimit) ReadMemory()
        {
            try
            {
                var m = new Native.MEMORYSTATUSEX { dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.MEMORYSTATUSEX)) };
                if (!Native.GlobalMemoryStatusEx(ref m)) return (0, 0, 0, 0);
                const double gb = 1073741824.0;
                // ullTotalPageFile is the commit limit (RAM + pagefile), not the size of the pagefile.
                return ((m.ullTotalPhys - m.ullAvailPhys) / gb, m.ullTotalPhys / gb,
                        (m.ullTotalPageFile - m.ullAvailPageFile) / gb, m.ullTotalPageFile / gb);
            }
            catch { return (0, 0, 0, 0); }
        }

        /// <summary>
        /// Every process we are allowed to ask about: its name, its total CPU time and its memory. Protected processes
        /// (and ones that end mid-pass) throw and are skipped — that is normal, not an error worth a log line.
        /// </summary>
        private static List<RawProc> ReadProcesses()
        {
            var list = new List<RawProc>(400);
            Process[] all;
            try { all = Process.GetProcesses(); } catch { return list; }
            foreach (var p in all)
            {
                try
                {
                    if (p.Id <= 4) continue;                    // Idle and System: no name, no meaning here
                    double cpuMs = p.TotalProcessorTime.TotalMilliseconds;
                    list.Add(new RawProc(p.Id, p.ProcessName, cpuMs, p.WorkingSet64 / 1048576.0, p.PrivateMemorySize64 / 1048576.0));
                }
                catch { }
                finally { try { p.Dispose(); } catch { } }
            }
            return list;
        }

        public void Dispose() => _gpu.Dispose();
    }
}
