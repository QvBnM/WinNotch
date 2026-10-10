using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using WinNotch.Core.Perf;

namespace WinNotch.Features.Performance
{
    /// <summary>What one pass over the GPU counters found.</summary>
    internal sealed class GpuReading
    {
        /// <summary>
        /// The counters answered this pass. Separate from the values on purpose: an idle graphics card reports zero,
        /// and "zero" must not be confused with "could not read" in either direction.
        /// </summary>
        public bool Ok;
        /// <summary>Busiest engine type, 0..100; -1 when there was nothing to read.</summary>
        public double BusiestPercent = -1;
        /// <summary>The 3D engine alone, 0..100; -1 when unknown. In a game, this is the GPU load.</summary>
        public double Percent3d = -1;
        /// <summary>Dedicated video memory in use, MB; -1 when unknown.</summary>
        public double VramMb = -1;
        /// <summary>Per process id: its share of the GPU, 0..100.</summary>
        public Dictionary<int, double> ByPid = new Dictionary<int, double>();
        /// <summary>Per process id: its dedicated video memory, MB.</summary>
        public Dictionary<int, double> VramByPid = new Dictionary<int, double>();
    }

    /// <summary>
    /// P60: the GPU's load and video memory, per process, from Windows' own performance counters — no vendor library,
    /// no driver call, no elevation.
    /// <para>This is a deliberate choice, not a shortcut. Reading NVML inside WinNotch is what closed the app without
    /// a trace in October (P51c): a GPU handle died when a monitor was unplugged and the access violation that
    /// followed cannot be caught by any <c>catch</c> in .NET. These counters are read by Windows, handed to us as
    /// numbers, and the worst a bad pass can do is return nothing. Temperature, power and clocks are not here — those
    /// need the sensor library, and they come from the SYSTEM helper, in its own process, where a crash costs us
    /// nothing.</para>
    /// <para>The counter paths are added with <c>PdhAddEnglishCounter</c>, so the app reads the same names on a
    /// Romanian Windows as on an English one.</para>
    /// </summary>
    internal sealed class GpuCounters : IDisposable
    {
        private const string EnginePath = @"\GPU Engine(*)\Utilization Percentage";
        private const string MemoryPath = @"\GPU Process Memory(*)\Dedicated Usage";

        private const uint PDH_FMT_DOUBLE = 0x00000200;
        private const uint PDH_FMT_NOCAP100 = 0x00008000;
        private static readonly int PDH_MORE_DATA = unchecked((int)0x800007D2);

        [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
        private static extern int PdhOpenQueryW(string dataSource, IntPtr userData, out IntPtr query);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
        private static extern int PdhAddEnglishCounterW(IntPtr query, string fullPath, IntPtr userData, out IntPtr counter);
        [DllImport("pdh.dll")]
        private static extern int PdhCollectQueryData(IntPtr query);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
        private static extern int PdhGetFormattedCounterArrayW(IntPtr counter, uint format, ref uint bufferSize, out uint itemCount, IntPtr itemBuffer);
        [DllImport("pdh.dll")]
        private static extern int PdhCloseQuery(IntPtr query);

        /// <summary>
        /// One line of the answer. The native layout on x64 is a pointer, then the status, then (after four bytes of
        /// padding, which the double's alignment adds on its own) the value: 24 bytes, which is what the sequential
        /// layout below produces. The app only ever ships as win-x64.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct CounterItem
        {
            public IntPtr Name;
            public uint Status;
            public double Value;
        }

        /// <summary>Failed collections in a row before the query is thrown away and opened again.</summary>
        private const int MaxFailures = 4;

        private IntPtr _query, _engine, _memory;
        private bool _primed;
        private int _failures;
        private DateTime _nextTry = DateTime.MinValue;
        private byte[] _engineBuf = new byte[64 * 1024], _memoryBuf = new byte[16 * 1024];

        /// <summary>The counters answered at least once. False on a machine or a session where they are not published.</summary>
        public bool Available { get; private set; }

        /// <summary>
        /// Reads one pass. Returns a reading with -1 in the totals (and empty maps) whenever the counters are not
        /// there, which the UI shows as "—" rather than as a zero: not knowing and being idle are different things.
        /// </summary>
        public GpuReading Read()
        {
            var r = new GpuReading();
            try
            {
                if (!Open()) return r;
                int rc = PdhCollectQueryData(_query);
                if (rc != 0)
                {
                    // A query that has started failing for good (a driver swap, the counter service restarted) would
                    // otherwise answer nothing for ever: after a few in a row, close it and let the retry delay
                    // below open a fresh one.
                    if (++_failures >= MaxFailures) Close();
                    return r;
                }
                _failures = 0;
                if (!_primed) { _primed = true; return r; }     // a rate counter says nothing on its first pass
                r.Ok = true;
                Available = true;
                r.BusiestPercent = 0;
                r.Percent3d = 0;

                double total3d = 0;
                var byType = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                foreach (var (instance, value) in Items(_engine, ref _engineBuf))
                {
                    var gi = GpuInstance.Parse(instance);
                    if (!gi.IsValid || value <= 0) continue;
                    r.ByPid.TryGetValue(gi.Pid, out double had);
                    r.ByPid[gi.Pid] = Math.Min(100, had + value);
                    byType.TryGetValue(gi.EngineType, out double t);
                    byType[gi.EngineType] = t + value;
                    if (gi.Is3D) total3d += value;
                }
                double busiest = 0;
                foreach (var kv in byType) if (kv.Value > busiest) busiest = kv.Value;
                r.BusiestPercent = Math.Clamp(busiest, 0, 100);
                r.Percent3d = Math.Clamp(total3d, 0, 100);

                double vram = 0;
                bool anyVram = _memory != IntPtr.Zero;
                foreach (var (instance, value) in Items(_memory, ref _memoryBuf))
                {
                    var gi = GpuInstance.Parse(instance);
                    if (!gi.IsValid || value <= 0) continue;
                    double mb = value / 1048576.0;
                    r.VramByPid.TryGetValue(gi.Pid, out double had);
                    r.VramByPid[gi.Pid] = had + mb;
                    vram += mb;
                }
                if (anyVram) r.VramMb = vram;      // 0 is a real answer; -1 stays only when there is no such counter
            }
            catch (Exception ex)
            {
                Close();
                Core.Flags.FeatureFlags.Current?.ReportError(PerfRules.FeatureId, ex);
            }
            return r;
        }

        private bool Open()
        {
            if (_engine != IntPtr.Zero) return true;
            if (DateTime.UtcNow < _nextTry) return false;
            _nextTry = DateTime.UtcNow.AddSeconds(60);          // a machine without these counters is not asked every pass
            if (PdhOpenQueryW(null, IntPtr.Zero, out _query) != 0) { _query = IntPtr.Zero; return false; }
            if (PdhAddEnglishCounterW(_query, EnginePath, IntPtr.Zero, out _engine) != 0) { Close(); return false; }
            if (PdhAddEnglishCounterW(_query, MemoryPath, IntPtr.Zero, out _memory) != 0) _memory = IntPtr.Zero;
            _primed = false;
            return true;
        }

        /// <summary>The lines of one counter. Grows its buffer once and keeps it: this runs every couple of seconds.</summary>
        private IEnumerable<(string Instance, double Value)> Items(IntPtr counter, ref byte[] buffer)
        {
            var list = new List<(string, double)>();
            if (counter == IntPtr.Zero) return list;

            uint size = (uint)buffer.Length, count;
            var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                int rc = PdhGetFormattedCounterArrayW(counter, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100, ref size, out count, handle.AddrOfPinnedObject());
                if (rc == PDH_MORE_DATA)
                {
                    handle.Free();
                    handle = default;
                    if (size > 8 * 1024 * 1024) return list;     // something is wrong; do not chase it
                    buffer = new byte[Math.Max(size, (uint)buffer.Length * 2)];
                    size = (uint)buffer.Length;
                    handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
                    rc = PdhGetFormattedCounterArrayW(counter, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100, ref size, out count, handle.AddrOfPinnedObject());
                }
                if (rc != 0) return list;

                IntPtr at = handle.AddrOfPinnedObject();
                int stride = Marshal.SizeOf<CounterItem>();
                for (uint i = 0; i < count; i++)
                {
                    var item = Marshal.PtrToStructure<CounterItem>(at + (int)(i * stride));
                    if (item.Status != 0 || item.Name == IntPtr.Zero) continue;
                    string name = Marshal.PtrToStringUni(item.Name);
                    if (!string.IsNullOrEmpty(name)) list.Add((name, item.Value));
                }
            }
            finally { if (handle.IsAllocated) handle.Free(); }
            return list;
        }

        private void Close()
        {
            if (_query != IntPtr.Zero) { try { PdhCloseQuery(_query); } catch { } }
            _query = _engine = _memory = IntPtr.Zero;
            _primed = false;
            _failures = 0;
            Available = false;              // whatever it used to answer, it does not answer now
        }

        public void Dispose() => Close();
    }
}
