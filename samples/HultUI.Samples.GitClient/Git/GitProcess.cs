using System.Diagnostics;

namespace GitClient.Git;

/// <summary>
/// Runs git CLI commands and captures output.
/// </summary>
public static class GitProcess
{
    public static Task<GitResult> RunAsync(string workingDirectory, params string[] args)
        => RunAsync(workingDirectory, null, args);

    /// <summary>
    /// Runs a git command asynchronously. When <paramref name="askPassHandler"/>
    /// is non-null, SSH/git credential prompts are routed through it instead of
    /// the terminal. The handler receives the prompt text and should return the
    /// passphrase, or null to cancel.
    /// </summary>
    public static async Task<GitResult> RunAsync(
        string workingDirectory,
        Func<string, Task<string?>>? askPassHandler,
        params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var bridge = askPassHandler != null ? new AskPassBridge() : null;
        bridge?.ConfigureProcess(psi);

        using var process = Process.Start(psi)!;

        // Close stdin immediately so SSH can't read from the terminal.
        if (psi.RedirectStandardInput)
            process.StandardInput.Close();

        // Run output readers and askpass monitor concurrently.
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        if (bridge != null && askPassHandler != null)
            _ = bridge.MonitorAsync(process, askPassHandler);

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        await process.WaitForExitAsync().ConfigureAwait(false);

        return new GitResult(process.ExitCode, stdout, stderr);
    }

    public static GitResult Run(string workingDirectory, params string[] args)
        => RunWithEnv(workingDirectory, null, args);

    /// <summary>
    /// Same as <see cref="Run"/> but lets callers inject environment variables
    /// like <c>GIT_SEQUENCE_EDITOR</c> used to drive interactive rebase.
    /// </summary>
    public static GitResult RunWithEnv(string workingDirectory, Dictionary<string, string>? env, params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        if (env != null)
            foreach (var kv in env)
                psi.Environment[kv.Key] = kv.Value;

        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return new GitResult(process.ExitCode, stdout, stderr);
    }

    public static GitResult RunWithStdin(string workingDirectory, string stdin, params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi)!;
        process.StandardInput.Write(stdin);
        process.StandardInput.Close();
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return new GitResult(process.ExitCode, stdout, stderr);
    }
}

public record GitResult(int ExitCode, string Stdout, string Stderr)
{
    public bool Success => ExitCode == 0;
    public string[] Lines => Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);
}
