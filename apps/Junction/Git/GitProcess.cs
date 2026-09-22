using System.Diagnostics;
using System.Text;

namespace Junction.Git;

/// <summary>
/// Runs git CLI commands and captures output.
/// </summary>
public static class GitProcess
{
    // git emits UTF-8; without this .NET decodes stdout/stderr with the console's
    // ANSI code page, mangling any non-ASCII (accented names, box-drawing chars,
    // CJK) in diffs, logs, and status. No BOM so stdin patches stay byte-clean.
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    // Somewhere that is nobody's repository. Only the OS-level pin matters here: -C decides
    // where git looks for the repository, so this never affects what a command sees.
    private static readonly string NeutralDirectory = Path.GetTempPath();

    /// <summary>
    /// The common part of launching git against a repository.
    ///
    /// <para>The repository is passed as <c>-C &lt;path&gt;</c> rather than as the process's
    /// working directory, because Windows pins a running process's current directory: while
    /// any git call was in flight, the folder it ran in could not be deleted, moved or
    /// renamed. Junction's loaders outlive the views that start them, so a repository stayed
    /// pinned after the user had moved on from it — and the test fixtures, which are ordinary
    /// repositories in the temp folder, could not be cleaned up on the way out.</para>
    ///
    /// <para><c>-C</c> is defined as changing to that directory before running, so commands
    /// behave identically, relative path arguments included — they still resolve against the
    /// repository. One difference is deliberate: a path that does not exist used to throw from
    /// <see cref="Process.Start(ProcessStartInfo)"/> and now comes back as an ordinary failed
    /// <see cref="GitResult"/>, which is what every caller already handles.</para>
    /// </summary>
    private static ProcessStartInfo StartInfo(string workingDirectory, string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = NeutralDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        if (!string.IsNullOrEmpty(workingDirectory))
        {
            psi.ArgumentList.Add("-C");
            psi.ArgumentList.Add(workingDirectory);
        }
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        return psi;
    }

    /// <summary>How long one async git invocation may run before it is killed. Git has no
    /// network timeout of its own, so a command against a remote that has gone away — a
    /// dropped VPN, a laptop that changed networks mid-fetch — waits forever. Forever was
    /// not confined to that one command either: callers hold their "a remote command is
    /// running" flag until this returns, so a single hung fetch refused every later fetch,
    /// pull and push for the rest of the session. Generous enough that a first fetch of a
    /// large repository over a slow link still finishes inside it.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(10);

    public static Task<GitResult> RunAsync(string workingDirectory, params string[] args)
        => RunAsync(workingDirectory, null, args);

    /// <summary>
    /// Runs a git command asynchronously. When <paramref name="askPassHandler"/>
    /// is non-null, SSH/git credential prompts are routed through it instead of
    /// the terminal. The handler receives the prompt text and should return the
    /// passphrase, or null to cancel.
    /// </summary>
    public static Task<GitResult> RunAsync(
        string workingDirectory,
        Func<string, Task<string?>>? askPassHandler,
        params string[] args)
        => RunAsync(workingDirectory, askPassHandler, DefaultTimeout, CancellationToken.None, args);

    /// <summary>
    /// As <see cref="RunAsync(string, Func{string, Task{string?}}?, string[])"/>, with an
    /// explicit budget and cancellation.
    ///
    /// <para>The body runs on the thread pool rather than on the caller's thread. An async
    /// method runs synchronously until its first yielding await, and the first one here is a
    /// read of the child's output — so <see cref="Process.Start(ProcessStartInfo)"/> used to
    /// run on whichever thread called in, which from a toolbar handler is the UI thread.
    /// Starting a process is not free under the load this app makes for itself: the
    /// background sweep keeps git running across every open repository, each fetch fans out
    /// into more of them, and the virus scanner meets every new git.exe. Measured between
    /// 30 ms and 620 ms — a frame the window could not paint and a stretch where hover went
    /// dead, all of it *before* the spinner that was meant to say "working" appeared.</para>
    /// </summary>
    public static Task<GitResult> RunAsync(
        string workingDirectory,
        Func<string, Task<string?>>? askPassHandler,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        string[] args)
        => Task.Run(
            () => RunCoreAsync(workingDirectory, askPassHandler, timeout, cancellationToken, args),
            CancellationToken.None);

    private static async Task<GitResult> RunCoreAsync(
        string workingDirectory,
        Func<string, Task<string?>>? askPassHandler,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        string[] args)
    {
        var psi = StartInfo(workingDirectory, args);
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.StandardOutputEncoding = Utf8NoBom;
        psi.StandardErrorEncoding = Utf8NoBom;

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

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (timeout > TimeSpan.Zero)
            cts.CancelAfter(timeout);

        try
        {
            // Exit first, then the pipes. Waiting on the readers first — as this used to —
            // makes the budget unenforceable: a git that hangs never closes stdout, so the
            // read never completes and there is nothing left to time out. Both readers are
            // already in flight, so the pipes keep draining and a chatty command still
            // cannot fill a buffer and deadlock.
            await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The whole tree: fetch and push spawn ssh, and killing git alone leaves the
            // child holding the pipes open, so the reads below would never return.
            try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
            try { await process.WaitForExitAsync().ConfigureAwait(false); } catch { /* ditto */ }

            var partial = (await Drain(stderrTask).ConfigureAwait(false)).Trim();
            var why = cancellationToken.IsCancellationRequested
                ? "was cancelled"
                : $"timed out after {timeout.TotalMinutes:N0} min";
            return new GitResult(-1, "",
                $"git {string.Join(' ', args)} {why} and was stopped."
                + (partial.Length > 0 ? $"\n{partial}" : ""));
        }

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);

        return new GitResult(process.ExitCode, stdout, stderr);
    }

    /// <summary>Whatever a reader managed to collect, for the message a killed command
    /// leaves behind. Bounded, because this runs on the failure path and must not become a
    /// second place to hang: the pipe should close with the process, but the reason we are
    /// here at all is that something about this command did not behave.</summary>
    private static async Task<string> Drain(Task<string> reader)
    {
        try { return await reader.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
        catch { return ""; }
    }

    public static GitResult Run(string workingDirectory, params string[] args)
        => RunWithEnv(workingDirectory, null, args);

    /// <summary>
    /// Same as <see cref="Run"/> but lets callers inject environment variables
    /// like <c>GIT_SEQUENCE_EDITOR</c> used to drive interactive rebase.
    /// </summary>
    public static GitResult RunWithEnv(string workingDirectory, Dictionary<string, string>? env, params string[] args)
    {
        var psi = StartInfo(workingDirectory, args);
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.StandardOutputEncoding = Utf8NoBom;
        psi.StandardErrorEncoding = Utf8NoBom;

        if (env != null)
            foreach (var kv in env)
                psi.Environment[kv.Key] = kv.Value;

        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return new GitResult(process.ExitCode, stdout, stderr);
    }

    /// <summary>
    /// Runs git and returns stdout as raw bytes. Needed for blob content (images and
    /// other binaries) — <see cref="Run"/> decodes stdout as UTF-8, which corrupts
    /// any non-text payload.
    /// </summary>
    public static (bool Success, byte[] Stdout) RunBytes(string workingDirectory, params string[] args)
    {
        // No StandardOutputEncoding: we read BaseStream directly, undecoded.
        var psi = StartInfo(workingDirectory, args);
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;

        using var process = Process.Start(psi)!;
        using var buffer = new MemoryStream();
        process.StandardOutput.BaseStream.CopyTo(buffer);
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode == 0, buffer.ToArray());
    }

    public static GitResult RunWithStdin(string workingDirectory, string stdin, params string[] args)
    {
        var psi = StartInfo(workingDirectory, args);
        psi.RedirectStandardInput = true;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.StandardOutputEncoding = Utf8NoBom;
        psi.StandardErrorEncoding = Utf8NoBom;
        psi.StandardInputEncoding = Utf8NoBom;

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

/// <summary>
/// A read query git refused. Thrown by the queries that feed the views (the log, the
/// working-tree changes, a commit's files and diff, a tree listing) so that a refused
/// query is a failed load, which keeps the previous answer on screen, rather than an
/// empty one, which used to land as data — "no commits", "no changes" — and take the
/// selection with it. Git refuses these in ordinary use: <c>git diff</c> dies with
/// "cannot hash" on a file whose writer still holds it open, which is exactly when the
/// working-tree watcher asks.
/// </summary>
public sealed class GitCommandException : Exception
{
    public GitCommandException(IReadOnlyList<string> args, GitResult result)
        : base($"git {string.Join(' ', args)} failed ({result.ExitCode}): {result.Stderr.Trim()}")
    {
        Args = args;
        Result = result;
    }

    public IReadOnlyList<string> Args { get; }
    public GitResult Result { get; }
}
