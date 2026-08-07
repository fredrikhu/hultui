using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VesperCommander;

/// <summary>One file operation for the elevated worker to perform.</summary>
public sealed class ElevatedItem
{
    public string Source { get; set; } = "";
    public string Target { get; set; } = "";     // full destination path (or the new name's full path)
    public bool IsDir { get; set; }
}

/// <summary>A batch handed to an elevated copy of this executable. <see cref="Verb"/> is
/// one of copy · move · delete · mkdir · rename.</summary>
public sealed class ElevatedJob
{
    public string Verb { get; set; } = "";
    public bool Overwrite { get; set; }
    public bool Recycle { get; set; }
    public List<ElevatedItem> Items { get; set; } = [];
}

/// <summary>Per-item outcome from the worker (index-aligned with the job's items).</summary>
public sealed class ElevatedResult
{
    public List<string?> Errors { get; set; } = [];   // null = that item succeeded
    public int Succeeded { get; set; }
    public int Failed { get; set; }
}

[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(ElevatedJob))]
[JsonSerializable(typeof(ElevatedResult))]
internal partial class ElevationJsonContext : JsonSerializerContext;

/// <summary>
/// Runs a batch of file operations with administrator/root rights.
///
/// <para><b>Why a second process.</b> A process's privilege level is fixed at creation —
/// Windows has no API to elevate in place. The only supported route is launching a new
/// process through <c>ShellExecute</c> with the <c>runas</c> verb, which raises the UAC
/// consent dialog. That verb requires <c>UseShellExecute</c>, which in turn forbids stdio
/// redirection, so the job cannot travel over a pipe: we hand it over as a JSON file and
/// read a JSON result file back. Vesper re-launches <i>its own executable</i> with
/// <c>--elevated-worker &lt;jobfile&gt;</c>, so there is no separate helper binary to ship,
/// sign, or keep in sync.</para>
///
/// <para><b>Trust.</b> The elevated child reads a file the unelevated parent wrote, so that
/// file is a privilege boundary: anything able to rewrite it between hand-off and read would
/// be choosing what runs as administrator. The job lives in a randomly-named file inside a
/// per-user directory whose ACL we reset to just this user (plus SYSTEM and Administrators),
/// with inheritance broken, so no other account can reach it. This does not defend against
/// code already running as the same user — such code can wait for any elevation prompt the
/// user is about to approve. That is inherent to on-demand elevation, not specific to this
/// design; the mitigation is prompting per batch (never holding a standing elevated helper
/// that a same-user process could drive at will).</para>
///
/// <para>On Linux the same worker runs under <c>pkexec</c>, which applies the desktop's
/// polkit policy. macOS has no equivalent CLI prompt, so elevation reports unsupported.</para>
/// </summary>
public static class Elevation
{
    public const string WorkerFlag = "--elevated-worker";

    /// <summary>True when this process already has administrator/root rights — then a
    /// failure is a genuine permission problem, not something elevation would fix.</summary>
    public static bool IsElevated
    {
        get
        {
            try
            {
                if (OperatingSystem.IsWindows()) return IsWindowsAdmin();
                return Environment.GetEnvironmentVariable("USER") == "root" || GetEuidSafe() == 0;
            }
            catch { return false; }
        }
    }

    /// <summary>True when we can plausibly raise a prompt: Windows (UAC) always, Linux when
    /// <c>pkexec</c> is on PATH. Already-elevated processes report false — there is nothing
    /// to escalate to.</summary>
    public static bool IsSupported
    {
        get
        {
            if (IsElevated) return false;
            if (OperatingSystem.IsWindows()) return true;
            if (OperatingSystem.IsLinux()) return FindOnPath("pkexec") != null;
            return false;
        }
    }

    /// <summary>Human-readable name for the privilege, used in prompts.</summary>
    public static string AdminNoun => OperatingSystem.IsWindows() ? "administrator" : "root";

    /// <summary>Hand <paramref name="job"/> to an elevated copy of this executable and wait
    /// for it. Returns the per-item outcome, or null when the user declined the prompt or the
    /// worker could not be launched (<paramref name="error"/> then explains, and is null for a
    /// plain decline). Blocking — call from a background thread.</summary>
    public static ElevatedResult? Run(ElevatedJob job, out string? error)
    {
        error = null;
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) { error = "can't locate the Vesper executable"; return null; }

        string dir = JobDirectory();
        string jobPath = Path.Combine(dir, Guid.NewGuid().ToString("N") + ".job.json");
        string resultPath = jobPath + ".result";

        try
        {
            File.WriteAllText(jobPath, JsonSerializer.Serialize(job, ElevationJsonContext.Default.ElevatedJob));

            var psi = OperatingSystem.IsWindows()
                ? new ProcessStartInfo(exe) { UseShellExecute = true, Verb = "runas", CreateNoWindow = true }
                : new ProcessStartInfo("pkexec") { UseShellExecute = false };
            if (!OperatingSystem.IsWindows()) psi.ArgumentList.Add(exe);
            psi.ArgumentList.Add(WorkerFlag);
            psi.ArgumentList.Add(jobPath);

            using var proc = Process.Start(psi);
            if (proc == null) { error = "couldn't start the elevated helper"; return null; }
            proc.WaitForExit();

            // pkexec exits 126/127 when the user dismisses the prompt or authentication fails.
            if (!OperatingSystem.IsWindows() && proc.ExitCode is 126 or 127) return null;

            if (!File.Exists(resultPath))
            {
                error = $"the elevated helper exited without a result (code {proc.ExitCode})";
                return null;
            }
            return JsonSerializer.Deserialize(File.ReadAllText(resultPath), ElevationJsonContext.Default.ElevatedResult);
        }
        catch (System.ComponentModel.Win32Exception w) when (w.NativeErrorCode == 1223)
        {
            return null;   // ERROR_CANCELLED — the user dismissed the UAC prompt
        }
        catch (Exception ex) { error = ex.Message; return null; }
        finally
        {
            TryDelete(jobPath);
            TryDelete(resultPath);
        }
    }

    /// <summary>The elevated child's entry point (<c>--elevated-worker &lt;jobfile&gt;</c>):
    /// perform the batch, write the result file beside the job, exit. Never opens a window.
    /// Returns the process exit code.</summary>
    public static int RunWorker(string jobPath)
    {
        var result = new ElevatedResult();
        try
        {
            var job = JsonSerializer.Deserialize(File.ReadAllText(jobPath), ElevationJsonContext.Default.ElevatedJob);
            if (job == null) return 2;

            foreach (var item in job.Items)
            {
                var err = Perform(job, item);
                result.Errors.Add(err);
                if (err == null) result.Succeeded++; else result.Failed++;
            }
            File.WriteAllText(jobPath + ".result",
                JsonSerializer.Serialize(result, ElevationJsonContext.Default.ElevatedResult));
            return result.Failed == 0 ? 0 : 1;
        }
        catch { return 2; }
    }

    private static string? Perform(ElevatedJob job, ElevatedItem item)
    {
        var entry = new FileEntry(Path.GetFileName(item.Target), item.Source, item.IsDir, 0, DateTime.Now);
        switch (job.Verb)
        {
            case "copy":
            {
                var destDir = Path.GetDirectoryName(item.Target) ?? "";
                return FileOps.Copy(entry with { Name = Path.GetFileName(item.Target) }, destDir, job.Overwrite);
            }
            case "move":
            {
                var destDir = Path.GetDirectoryName(item.Target) ?? "";
                return FileOps.Move(entry with { Name = Path.GetFileName(item.Target) }, destDir, job.Overwrite);
            }
            case "delete":
                return job.Recycle
                    ? FileOps.Recycle(item.Source, item.IsDir)
                    : FileOps.DeletePermanent(item.Source, item.IsDir);
            case "rename":
                return FileOps.Rename(entry, Path.GetFileName(item.Target));
            case "mkdir":
                try { Directory.CreateDirectory(item.Target); return null; }
                catch (Exception ex) { return ex.Message; }
            default:
                return $"unknown operation \"{job.Verb}\"";
        }
    }

    /// <summary>A per-user staging directory for job files, locked down to this account so
    /// no other user can influence what the elevated worker reads.</summary>
    private static string JobDirectory()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VesperCommander", "elevated");
        Directory.CreateDirectory(dir);
        if (OperatingSystem.IsWindows()) TryRestrictToCurrentUser(dir);
        return dir;
    }

    [SupportedOSPlatform("windows")]
    private static void TryRestrictToCurrentUser(string dir)
    {
        try
        {
            var info = new DirectoryInfo(dir);
            var sec = info.GetAccessControl();
            sec.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            foreach (System.Security.AccessControl.FileSystemAccessRule rule in
                     sec.GetAccessRules(true, false, typeof(System.Security.Principal.SecurityIdentifier)))
                sec.RemoveAccessRuleAll(rule);

            void Allow(System.Security.Principal.IdentityReference id) => sec.AddAccessRule(
                new System.Security.AccessControl.FileSystemAccessRule(id,
                    System.Security.AccessControl.FileSystemRights.FullControl,
                    System.Security.AccessControl.InheritanceFlags.ObjectInherit
                        | System.Security.AccessControl.InheritanceFlags.ContainerInherit,
                    System.Security.AccessControl.PropagationFlags.None,
                    System.Security.AccessControl.AccessControlType.Allow));

            using var me = System.Security.Principal.WindowsIdentity.GetCurrent();
            if (me.User != null) Allow(me.User);
            // The elevated worker runs as the same user but in the Administrators context;
            // SYSTEM keeps the directory serviceable (backup, cleanup).
            Allow(new System.Security.Principal.SecurityIdentifier(
                System.Security.Principal.WellKnownSidType.BuiltinAdministratorsSid, null));
            Allow(new System.Security.Principal.SecurityIdentifier(
                System.Security.Principal.WellKnownSidType.LocalSystemSid, null));
            info.SetAccessControl(sec);
        }
        catch { /* hardening is best-effort; the directory is already user-profile scoped */ }
    }

    [SupportedOSPlatform("windows")]
    private static bool IsWindowsAdmin()
    {
        using var id = System.Security.Principal.WindowsIdentity.GetCurrent();
        return new System.Security.Principal.WindowsPrincipal(id)
            .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }

    private static uint GetEuidSafe()
    {
        try { return OperatingSystem.IsWindows() ? 1u : geteuid(); }
        catch { return 1u; }
    }

    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "geteuid")]
    private static extern uint geteuid();

    private static string? FindOnPath(string exe)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try { var full = Path.Combine(dir, exe); if (File.Exists(full)) return full; }
            catch { /* unreadable PATH entry */ }
        }
        return null;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best-effort cleanup */ }
    }
}
