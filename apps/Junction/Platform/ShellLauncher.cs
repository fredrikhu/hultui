using System.Diagnostics;

namespace Junction.Platform;

/// <summary>
/// Hands a path to the desktop: the OS default handler for "open this", and the file
/// manager for "show me where this is".
///
/// <para>Deliberately not <see cref="Junction.Git.ExternalDiffLauncher"/>. That one runs a
/// <i>configured</i> program and needs the PATH/PATHEXT walk to find a <c>code.cmd</c>; here
/// there is no program to find — Windows is asked to apply the file association, and the two
/// Unix desktops each have one well-known opener on PATH. Mixing the two would mean resolving
/// a command that was never named.</para>
/// </summary>
public static class ShellLauncher
{
    /// <summary>Opens a file (or folder) with whatever the desktop associates with it.</summary>
    public static bool Open(string path, out string error)
    {
        error = "";
        try
        {
            if (OperatingSystem.IsWindows())
            {
                // UseShellExecute is the association lookup — without it Windows can only
                // start an executable, so a .md or .png would simply fail to launch.
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
                return true;
            }
            Process.Start(OperatingSystem.IsMacOS() ? "open" : "xdg-open", [path])?.Dispose();
            return true;
        }
        catch (Exception ex) when (IsLaunchFailure(ex))
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Shows a path in the desktop's file manager, selected where the platform can do that.
    ///
    /// <para>Windows and macOS both take the file itself and highlight it in its folder. Linux
    /// has no portable equivalent — <c>xdg-open</c> on a file would <i>launch</i> it, which is
    /// the opposite of what was asked — so there the containing folder is opened instead.</para>
    /// </summary>
    public static bool Reveal(string path, out string error)
    {
        error = "";
        try
        {
            if (OperatingSystem.IsWindows())
            {
                // Not ArgumentList: explorer parses its own command line and wants
                // `/select,"<path>"` as a single comma-joined token, which the quoting
                // rules ArgumentList applies would break apart.
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"")
                { UseShellExecute = true })?.Dispose();
                return true;
            }
            if (OperatingSystem.IsMacOS())
            {
                Process.Start("open", ["-R", path])?.Dispose();
                return true;
            }
            var folder = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(folder))
            {
                error = "No containing folder.";
                return false;
            }
            Process.Start("xdg-open", [folder])?.Dispose();
            return true;
        }
        catch (Exception ex) when (IsLaunchFailure(ex))
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>The desktop's own name for where <see cref="Reveal"/> goes, so the menu entry
    /// says the thing the reader will actually see open.</summary>
    public static string RevealLabel =>
        OperatingSystem.IsWindows() ? "Reveal in Explorer"
        : OperatingSystem.IsMacOS() ? "Reveal in Finder"
        : "Open containing folder";

    // A missing association, a missing opener, and a refused launch all arrive as one of
    // these; anything else is a bug here rather than a fact about the machine.
    private static bool IsLaunchFailure(Exception ex) =>
        ex is System.ComponentModel.Win32Exception or InvalidOperationException
            or FileNotFoundException or PlatformNotSupportedException;
}
