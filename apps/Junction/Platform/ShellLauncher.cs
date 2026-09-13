using System.Diagnostics;

namespace Junction.Platform;

/// <summary>
/// Hands a path to the desktop: the OS default handler for "open this", the file manager
/// for "show me where this is", and a terminal for "let me do the rest by hand".
///
/// <para>Deliberately not <see cref="Junction.Git.ExternalDiffLauncher"/>. That one runs the
/// program the <i>user</i> named in settings, and so has to resolve an arbitrary command
/// against PATH/PATHEXT to find a <c>code.cmd</c>. Nothing here is configurable: these are the
/// desktop's own verbs and, for the terminal, the small fixed list of programs each platform
/// ships. Letting the two share a resolver would mean resolving commands nobody named.</para>
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

    /// <summary>The desktop's own name for the file manager <see cref="Open"/> puts a
    /// folder in.</summary>
    public static string FileManagerLabel =>
        OperatingSystem.IsWindows() ? "Open in Explorer"
        : OperatingSystem.IsMacOS() ? "Open in Finder"
        : "Open in file manager";

    /// <summary>
    /// Opens a terminal with <paramref name="directory"/> as its working directory.
    ///
    /// <para>Unlike <see cref="Open"/> and <see cref="Reveal"/> there is no OS verb for
    /// this, so a program does have to be named. Windows Terminal is tried first — it is
    /// what a current Windows install actually opens, and its <c>wt.exe</c> is an
    /// execution alias on PATH — and the machine's configured interpreter is the fallback
    /// for a machine without it. The two Unix desktops each have one conventional opener.</para>
    /// </summary>
    public static bool OpenTerminal(string directory, out string error) =>
        OpenTerminal(directory, TerminalPreset.System, out error);

    /// <summary>
    /// Runs the reader's own command from Options › External tools with <c>$file</c>,
    /// <c>$line</c> and <c>$repo</c> filled in — each quoted, so a path with a space stays
    /// one argument. The first word is the program; the shell is not involved.
    /// </summary>
    public static bool RunCustomCommand(string command, string repo, string? file, int? line, out string error)
    {
        error = "";
        var expanded = command
            .Replace("$file", Quote(file ?? ""))
            .Replace("$line", (line ?? 0).ToString())
            .Replace("$repo", Quote(repo))
            .Trim();
        if (expanded.Length == 0) { error = "No custom command is set."; return false; }
        int space = expanded.IndexOf(' ');
        var program = space < 0 ? expanded : expanded[..space];
        var args = space < 0 ? "" : expanded[(space + 1)..];
        try
        {
            var psi = new ProcessStartInfo(program.Trim('"'))
            {
                Arguments = args,
                WorkingDirectory = repo,
                UseShellExecute = true,
            };
            Process.Start(psi)?.Dispose();
            return true;
        }
        catch (Exception ex) when (IsLaunchFailure(ex))
        {
            error = ex.Message;
            return false;
        }
    }

    private static string Quote(string s) => s.Contains(' ') ? "\"" + s + "\"" : s;

    /// <summary>As above, with Options › External tools' choice first: a named terminal is
    /// tried before the machine's own order, which stays the fallback.</summary>
    public static bool OpenTerminal(string directory, TerminalPreset preset, out string error)
    {
        error = "";
        foreach (var (program, args) in PreferredTerminal(directory, preset).Concat(TerminalCandidates(directory)))
        {
            try
            {
                var psi = new ProcessStartInfo(program)
                {
                    // The working directory is the whole point of the request, and it is
                    // also the fallback for the shells that take no directory argument.
                    WorkingDirectory = directory,
                    UseShellExecute = true,
                };
                foreach (var arg in args) psi.ArgumentList.Add(arg);
                Process.Start(psi)?.Dispose();
                return true;
            }
            catch (Exception ex) when (IsLaunchFailure(ex))
            {
                // Not installed on this machine — remember why and try the next.
                error = ex.Message;
            }
        }
        if (error.Length == 0) error = "No terminal found.";
        return false;
    }

    private static IEnumerable<(string Program, string[] Args)> PreferredTerminal(string directory, TerminalPreset preset)
    {
        if (!OperatingSystem.IsWindows()) yield break;
        switch (preset)
        {
            case TerminalPreset.WindowsTerminal:
                yield return ("wt.exe", ["-d", directory]);
                break;
            case TerminalPreset.PowerShell:
                yield return ("pwsh.exe", ["-NoExit"]);
                yield return ("powershell.exe", ["-NoExit"]);
                break;
            case TerminalPreset.CommandPrompt:
                var comspec = Environment.GetEnvironmentVariable("ComSpec");
                if (!string.IsNullOrEmpty(comspec)) yield return (comspec, []);
                break;
        }
    }

    private static IEnumerable<(string Program, string[] Args)> TerminalCandidates(string directory)
    {
        if (OperatingSystem.IsWindows())
        {
            yield return ("wt.exe", ["-d", directory]);
            // COMSPEC is a full path, so this needs no PATH lookup of its own.
            var comspec = Environment.GetEnvironmentVariable("ComSpec");
            if (!string.IsNullOrEmpty(comspec)) yield return (comspec, []);
            yield break;
        }
        if (OperatingSystem.IsMacOS())
        {
            yield return ("open", ["-a", "Terminal", directory]);
            yield break;
        }
        // Debian's alternatives symlink, then the two desktops' own terminals.
        yield return ("x-terminal-emulator", []);
        yield return ("gnome-terminal", []);
        yield return ("konsole", []);
    }

    // A missing association, a missing opener, and a refused launch all arrive as one of
    // these; anything else is a bug here rather than a fact about the machine.
    private static bool IsLaunchFailure(Exception ex) =>
        ex is System.ComponentModel.Win32Exception or InvalidOperationException
            or FileNotFoundException or PlatformNotSupportedException;
}
