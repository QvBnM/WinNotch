using System;
using System.Globalization;

namespace WinNotch.Core.Perf
{
    /// <summary>
    /// P60: the name Windows gives one line of the GPU performance counters, taken apart. Pure string work, so the
    /// fiddly part can be tested without a GPU.
    /// <para>Windows publishes the GPU's work per process and per engine under names like
    /// <c>pid_12345_luid_0x00000000_0x0000D5BE_phys_0_eng_3_engtype_3D</c> (counter set "GPU Engine") and
    /// <c>pid_12345_luid_0x00000000_0x0000D5BE_phys_0</c> ("GPU Process Memory"). These counters need no driver
    /// library and no special rights — which is exactly why they are our first source for "who is using the GPU",
    /// instead of NVML in our own process (see the note in <c>docs/PROGRESS.md</c>, P51c: a dead handle there killed
    /// the whole app with an exception that cannot be caught).</para>
    /// </summary>
    public readonly struct GpuInstance
    {
        private GpuInstance(int pid, string engineType, bool valid)
        {
            Pid = pid; EngineType = engineType ?? ""; IsValid = valid;
        }

        public int Pid { get; }
        /// <summary>"3D", "VideoDecode", "Copy", "Compute_0"…; "" when the name carries no engine (process memory).</summary>
        public string EngineType { get; }
        public bool IsValid { get; }

        /// <summary>The engine that renders the game. The one worth showing on its own.</summary>
        public bool Is3D => string.Equals(EngineType, "3D", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Reads the process id and the engine out of one instance name. Returns an invalid value for anything that
        /// does not start with a process id — including the "_Total" line, which we must not add to the per-process
        /// numbers, and anything Windows may add to the format later.
        /// </summary>
        public static GpuInstance Parse(string instance)
        {
            if (string.IsNullOrEmpty(instance)) return default;
            if (!instance.StartsWith("pid_", StringComparison.OrdinalIgnoreCase)) return default;

            int i = 4, pid = 0, digits = 0;
            for (; i < instance.Length && instance[i] >= '0' && instance[i] <= '9'; i++, digits++)
            {
                if (digits > 9) return default;                     // a pid that long is not a pid
                pid = pid * 10 + (instance[i] - '0');
            }
            if (digits == 0) return default;
            if (i < instance.Length && instance[i] != '_') return default;

            const string tag = "_engtype_";
            int t = instance.IndexOf(tag, StringComparison.OrdinalIgnoreCase);
            string engine = t < 0 ? "" : instance.Substring(t + tag.Length);
            return new GpuInstance(pid, engine, true);
        }

        public override string ToString() =>
            IsValid ? Pid.ToString(CultureInfo.InvariantCulture) + (EngineType.Length > 0 ? "/" + EngineType : "") : "-";
    }
}
