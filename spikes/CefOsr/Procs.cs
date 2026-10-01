using System.Text.RegularExpressions;

namespace CefOsr;

/// <summary>What /proc says about this process's CEF children.</summary>
internal static class Procs
{
    public sealed record Child(int Pid, int PPid, string Type, string Exe, string CmdLine);

    public static List<Child> Children()
    {
        int me = Environment.ProcessId;
        var all = new Dictionary<int, (int ppid, string cmd, string exe)>();
        foreach (var d in Directory.GetDirectories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(d), out var pid)) continue;
            try
            {
                var stat = File.ReadAllText(Path.Combine(d, "stat"));
                var ppid = int.Parse(stat[(stat.LastIndexOf(')') + 2)..].Split(' ')[1]);
                var cmd = File.ReadAllText(Path.Combine(d, "cmdline")).Replace('\0', ' ').Trim();
                string exe;
                try { exe = new FileInfo(Path.Combine(d, "exe")).LinkTarget ?? "?"; }
                catch (Exception) { exe = "?"; }
                all[pid] = (ppid, cmd, exe);
            }
            catch (Exception) { }
        }
        bool Descends(int pid)
        {
            for (int i = 0; i < 16 && all.TryGetValue(pid, out var p); i++)
            {
                if (p.ppid == me) return true;
                pid = p.ppid;
            }
            return false;
        }
        return all.Where(kv => Descends(kv.Key))
            .Select(kv => new Child(kv.Key, kv.Value.ppid,
                Regex.Match(kv.Value.cmd, @"--type=(\S+)").Groups[1].Value is var t && t != "" ? t : "(no type)",
                kv.Value.exe, kv.Value.cmd))
            .OrderBy(c => c.Pid).ToList();
    }

    static string Read(string path) { try { return File.ReadAllText(path); } catch (Exception) { return ""; } }
    static string Link(string path) { try { return new FileInfo(path).LinkTarget ?? ""; } catch (Exception) { return ""; } }

    /// <summary>One line per child: namespaces against ours, seccomp, and what it has mapped.</summary>
    public static IEnumerable<string> Report()
    {
        var myUser = Link("/proc/self/ns/user");
        var myPid = Link("/proc/self/ns/pid");
        var myNet = Link("/proc/self/ns/net");
        foreach (var c in Children())
        {
            var dir = $"/proc/{c.Pid}";
            var status = Read(dir + "/status");
            var seccomp = Regex.Match(status, @"Seccomp:\s+(\d)").Groups[1].Value;
            var nnp = Regex.Match(status, @"NoNewPrivs:\s+(\d)").Groups[1].Value;
            var ns = new List<string>();
            var user = Link(dir + "/ns/user");
            if (user == "") ns.Add("ns unreadable");
            else
            {
                if (user != myUser) ns.Add("own userns");
                if (Link(dir + "/ns/pid") != myPid) ns.Add("own pidns");
                if (Link(dir + "/ns/net") != myNet) ns.Add("own netns");
            }
            var maps = Read(dir + "/maps");
            var libs = new[] { "iris_dri", "libgallium", "libvk_swiftshader", "swiftshader", "libEGL_mesa",
                               "libvulkan_intel", "libwayland-client", "libX11.so", "libxcb.so", "libGLESv2.so" }
                .Where(maps.Contains).ToList();
            var ozone = Regex.Match(c.CmdLine, @"--ozone-platform=(\S+)").Groups[1].Value;
            yield return $"pid {c.Pid} (parent {c.PPid}) {c.Type}: exe {Path.GetFileName(c.Exe)}, " +
                         $"seccomp {seccomp}, no_new_privs {nnp}, {(ns.Count == 0 ? "our namespaces" : string.Join(" + ", ns))}" +
                         (ozone != "" ? $", --ozone-platform={ozone}" : "") +
                         (libs.Count > 0 ? $", maps {string.Join(" ", libs)}" : "");
        }
    }

    /// <summary>Unix sockets this process has open, by the path they are bound or connected to.</summary>
    public static IEnumerable<string> DisplaySockets(int pid)
    {
        var inodes = new HashSet<string>();
        try
        {
            foreach (var fd in Directory.GetFiles($"/proc/{pid}/fd"))
            {
                var t = Link(fd);
                var m = Regex.Match(t, @"socket:\[(\d+)\]");
                if (m.Success) inodes.Add(m.Groups[1].Value);
            }
        }
        catch (Exception) { }
        // A client's end of a connection has no path; the server's end does. Match the peer
        // through ss when it is there; otherwise report what /proc/net/unix names.
        var unix = Read("/proc/net/unix").Split('\n').Skip(1);
        foreach (var line in unix)
        {
            var p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (p.Length >= 8 && inodes.Contains(p[6])) yield return p[7];
        }
    }
}
