namespace HultUI.Terminal.Pty;

/// <summary>
/// What to run under a pseudoterminal: a program, its arguments, where, and with what added to
/// the environment. <see cref="PtyConnection.Start"/> runs it.
/// </summary>
/// <remarks>
/// <para><b>Arguments</b> reach the program exactly as given, on every platform. On Windows the
/// command line is built with the rules the C runtime and <c>CommandLineToArgvW</c> parse by,
/// so a program that splits its command line the usual way gets them back; <c>cmd.exe</c>
/// parses its own and is the exception, as it is everywhere. (Porta.Pty's own Windows quoting
/// wrapped every argument in quotes, which made <c>cmd /c exit 3</c> exit 1.)</para>
/// <para><b>The environment</b> is the application's own, with <c>TERM=xterm-256color</c> and
/// <c>COLORTERM=truecolor</c> added, then <see cref="Environment"/> on top.</para>
/// </remarks>
public sealed class PtyLaunch
{
    /// <param name="program">A path, or a name looked up on <c>PATH</c> (with <c>PATHEXT</c> on
    /// Windows, so <c>pwsh</c> finds <c>pwsh.exe</c>).</param>
    /// <param name="arguments">The arguments, each passed as it is.</param>
    public PtyLaunch(string program, params IEnumerable<string> arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(program);
        Program = program;
        Arguments = [.. arguments];
    }

    public string Program { get; }
    public IReadOnlyList<string> Arguments { get; }

    /// <summary>The program's working directory; null takes the application's current one.</summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>Variables to set for the program, over the application's own; a null value
    /// removes one.</summary>
    public IReadOnlyDictionary<string, string?> Environment { get; init; } = new Dictionary<string, string?>();

    /// <summary>
    /// The user's shell: on Windows PowerShell 7 (<c>pwsh</c>) when it is on <c>PATH</c>, then
    /// Windows PowerShell, then <c>cmd.exe</c>; elsewhere <c>$SHELL</c>, then <c>/bin/bash</c>, then
    /// <c>/bin/sh</c>. On macOS it is started as a login shell, as Terminal does, so the profile
    /// that sets <c>PATH</c> runs.
    /// </summary>
    public static PtyLaunch DefaultShell(string? workingDirectory = null)
    {
        if (OperatingSystem.IsWindows())
        {
            var system = System.Environment.SystemDirectory;
            var shell = FindProgram("pwsh")
                ?? Existing(Path.Combine(system, @"WindowsPowerShell\v1.0\powershell.exe"));
            if (shell is not null)
                return new PtyLaunch(shell, "-NoLogo") { WorkingDirectory = workingDirectory };
            var cmd = Existing(System.Environment.GetEnvironmentVariable("ComSpec")) ?? Path.Combine(system, "cmd.exe");
            return new PtyLaunch(cmd) { WorkingDirectory = workingDirectory };
        }

        var unixShell = Existing(System.Environment.GetEnvironmentVariable("SHELL"))
            ?? Existing("/bin/bash") ?? "/bin/sh";
        return OperatingSystem.IsMacOS()
            ? new PtyLaunch(unixShell, "-l") { WorkingDirectory = workingDirectory }
            : new PtyLaunch(unixShell) { WorkingDirectory = workingDirectory };
    }

    /// <summary>
    /// The full path <paramref name="program"/> names: itself when it is a path to a file, else
    /// the first match on <c>PATH</c> (trying each <c>PATHEXT</c> extension on Windows when it has
    /// none). Null when there is none. The current directory is not searched.
    /// </summary>
    public static string? FindProgram(string program)
    {
        if (string.IsNullOrWhiteSpace(program)) return null;
        if (program.Contains(Path.DirectorySeparatorChar) || program.Contains(Path.AltDirectorySeparatorChar))
            return Existing(Path.GetFullPath(program));

        var path = System.Environment.GetEnvironmentVariable("PATH") ?? "";
        string[] extensions = OperatingSystem.IsWindows() && !Path.HasExtension(program)
            ? (System.Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD")
                .ToLowerInvariant() // .EXE as listed; pwsh.exe as written on disk and shown
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [""];
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var extension in extensions)
            {
                string candidate;
                try { candidate = Path.Combine(dir.Trim('"'), program + extension); }
                catch (ArgumentException) { break; } // a malformed PATH entry
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }

    private static string? Existing(string? path) =>
        !string.IsNullOrEmpty(path) && File.Exists(path) ? path : null;
}
