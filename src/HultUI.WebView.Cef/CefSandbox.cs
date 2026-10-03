namespace HultUI.WebView.Cef;

/// <summary>
/// Whether Chromium's sandbox can start in this process, asked before <c>cef_initialize</c>:
/// where it cannot, CEF ends the process ("No usable sandbox!") instead of returning, so the
/// engine says why and fails instead (step 5.7).
/// </summary>
/// <remarks>
/// <para>The sandbox is user namespaces: the browser process (this one) starts the zygote in a
/// new one, and the renderers make theirs inside it. It cannot when the kernel refuses this
/// process a user namespace:</para>
/// <list type="bullet">
/// <item>Ubuntu 23.10 and later (<c>kernel.apparmor_restrict_unprivileged_userns=1</c>): an
/// unprivileged process may make one only under an AppArmor profile that allows it. The
/// process's label says whether it has one: <c>unconfined</c> is none.</item>
/// <item>User namespaces off outright: <c>user.max_user_namespaces=0</c>, or Debian's old
/// <c>kernel.unprivileged_userns_clone=0</c>.</item>
/// </list>
/// <para>Root, and a setuid-root <c>chrome-sandbox</c> beside <c>libcef.so</c> (CEF's other
/// sandbox, which needs no namespace), are fine either way.</para>
/// </remarks>
internal static class CefSandbox
{
    /// <summary>What the kernel says, read once from <c>/proc</c>.</summary>
    internal readonly record struct Facts(
        string? RestrictUnprivilegedUserns,
        string? AppArmorLabel,
        string? MaxUserNamespaces,
        string? UnprivilegedUsernsClone,
        bool IsRoot,
        bool HasSetuidSandbox,
        string Executable);

    /// <summary>Why the sandbox cannot start here, or null when it can (or nothing says it cannot).</summary>
    public static string? Problem(string cefDirectory) => Judge(Read(cefDirectory));

    internal static string? Judge(Facts f)
    {
        if (f.IsRoot || f.HasSetuidSandbox) return null;
        if (f.MaxUserNamespaces?.Trim() == "0" || f.UnprivilegedUsernsClone?.Trim() == "0")
            return "Chromium's sandbox needs user namespaces, and this system has them turned off " +
                   "(user.max_user_namespaces or kernel.unprivileged_userns_clone is 0). " +
                   "Turn them on, or install a setuid-root chrome-sandbox beside libcef.so.";
        bool restricted = f.RestrictUnprivilegedUserns?.Trim() == "1";
        bool unconfined = f.AppArmorLabel is null || f.AppArmorLabel.Trim().TrimEnd('\0') == "unconfined";
        if (!restricted || !unconfined) return null;
        var name = Path.GetFileName(f.Executable);
        var dotnet = name is "dotnet" or "dotnet.exe"
            ? " This process is the dotnet host: an application with a bundled CEF ships as its own " +
              "executable, so that the profile names it and not every .NET program."
            : "";
        return $"Chromium's sandbox cannot start: this system (Ubuntu 23.10 or later) lets a program make " +
               $"user namespaces only under an AppArmor profile that allows it, and {f.Executable} has none. " +
               $"The application's package installs one (/etc/apparmor.d/, allowing \"userns\" for this " +
               $"executable); see HultUI.WebView.Cef.Natives' README.{dotnet}";
    }

    private static Facts Read(string cefDirectory) => new(
        ReadOrNull("/proc/sys/kernel/apparmor_restrict_unprivileged_userns"),
        ReadOrNull("/proc/self/attr/apparmor/current") ?? ReadOrNull("/proc/self/attr/current"),
        ReadOrNull("/proc/sys/user/max_user_namespaces"),
        ReadOrNull("/proc/sys/kernel/unprivileged_userns_clone"),
        Environment.UserName == "root" || geteuid() == 0,
        HasSetuidSandbox(Path.Combine(cefDirectory, "chrome-sandbox")),
        Environment.ProcessPath ?? "this application");

    private static string? ReadOrNull(string path)
    {
        try { return File.Exists(path) ? File.ReadAllText(path) : null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>Setuid: what CEF's setuid sandbox needs. Whether root owns it is CEF's to check
    /// (.NET reads no owner); a setuid file a user owns would only fail later, as before.</summary>
    private static bool HasSetuidSandbox(string path)
    {
        try { return File.Exists(path) && (File.GetUnixFileMode(path) & UnixFileMode.SetUser) != 0; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    [System.Runtime.InteropServices.DllImport("libc")]
    private static extern uint geteuid();
}
