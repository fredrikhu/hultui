using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PerfSuite;

/// <summary>
/// Keeps the run on the CPU's fastest cores. On a hybrid CPU (this repo's i7-14700K: 8
/// performance cores, 12 efficiency cores) the scheduler moves a single busy thread between
/// the two kinds, and an efficiency core runs this workload ~1.8× slower — measured. A short
/// or filtered run then lands on whichever the scheduler chose and reads up to twice its
/// full-suite number. Pinning to the performance cores makes the two agree.
/// </summary>
static class CorePinning
{
    /// <summary>Pins the process to the fastest core class. Returns a line for the report, or null when there was nothing to do.</summary>
    public static string? PinToFastestCores()
    {
        try
        {
            var classes = OperatingSystem.IsWindows() ? WindowsEfficiencyClasses()
                : OperatingSystem.IsLinux() ? LinuxMaxFrequencies()
                : null;
            if (classes is null || classes.Count == 0) return null;

            // Within 10 % of the best, not equal to it: on Linux the performance cores' maximum
            // frequencies differ by a bin or two (the "favoured" cores boost higher).
            long best = classes.Values.Max();
            var fast = classes.Where(kv => kv.Value * 10 >= best * 9).Select(kv => kv.Key).ToList();
            if (fast.Count == classes.Count) return null;   // not hybrid: every core is the same

            long mask = 0;
            foreach (int cpu in fast)
                if (cpu < 64) mask |= 1L << cpu;
            if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
                Process.GetCurrentProcess().ProcessorAffinity = (nint)mask;
            return $"pinned to the {fast.Count} fastest of {classes.Count} logical processors (hybrid CPU; --no-pin to disable)";
        }
        catch (Exception e)
        {
            return $"could not pin to the fastest cores ({e.GetType().Name}); numbers may move between core types";
        }
    }

    // ── Windows: SYSTEM_CPU_SET_INFORMATION.EfficiencyClass, higher = faster ──

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemCpuSetInformation(nint information, uint bufferLength,
        out uint returnedLength, nint process, uint flags);

    private static Dictionary<int, long>? WindowsEfficiencyClasses()
    {
        GetSystemCpuSetInformation(0, 0, out uint length, 0, 0);
        if (length == 0) return null;
        nint buffer = Marshal.AllocHGlobal((int)length);
        try
        {
            if (!GetSystemCpuSetInformation(buffer, length, out length, 0, 0)) return null;
            var result = new Dictionary<int, long>();
            for (int offset = 0; offset < length;)
            {
                // struct { DWORD Size; DWORD Type; DWORD Id; WORD Group;
                //          BYTE LogicalProcessorIndex, CoreIndex, LastLevelCacheIndex,
                //               NumaNodeIndex, EfficiencyClass; … }
                int size = Marshal.ReadInt32(buffer, offset);
                int type = Marshal.ReadInt32(buffer, offset + 4);
                if (size <= 0) break;
                if (type == 0)   // CpuSetInformation
                {
                    short group = Marshal.ReadInt16(buffer, offset + 12);
                    byte logical = Marshal.ReadByte(buffer, offset + 14);
                    byte efficiency = Marshal.ReadByte(buffer, offset + 18);
                    if (group == 0) result[logical] = efficiency;
                }
                offset += size;
            }
            return result;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    // ── Linux: cpuinfo_max_freq per CPU, higher = faster ──

    private static Dictionary<int, long>? LinuxMaxFrequencies()
    {
        var result = new Dictionary<int, long>();
        foreach (var dir in Directory.EnumerateDirectories("/sys/devices/system/cpu", "cpu*"))
        {
            if (!int.TryParse(Path.GetFileName(dir)[3..], out int cpu)) continue;
            var file = Path.Combine(dir, "cpufreq", "cpuinfo_max_freq");
            if (File.Exists(file) && long.TryParse(File.ReadAllText(file).Trim(), out long khz))
                result[cpu] = khz;
        }
        return result;
    }
}
