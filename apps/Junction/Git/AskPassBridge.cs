using System.Diagnostics;

namespace Junction.Git;

/// <summary>
/// Manages a temporary askpass helper script so that SSH passphrase prompts
/// (and git credential prompts) are routed through the GUI instead of the
/// terminal.
///
/// <b>Platform:</b> this relies on a FIFO and a POSIX <c>/bin/sh</c> askpass
/// script, so it is only active on Linux and macOS. On Windows git uses its
/// native credential helpers instead — Git Credential Manager for HTTPS and
/// ssh-agent / Windows OpenSSH for SSH — which present their own GUI prompts,
/// so the bridge stays inert (all members become no-ops) rather than trying to
/// run <c>mkfifo</c>, which does not exist there.
///
/// <b>Security:</b> the passphrase never touches the filesystem. The response
/// channel is a FIFO (named pipe) — data is kernel-buffered and never written
/// to disk. The prompt file contains only the non-sensitive prompt text
/// (e.g. "Enter passphrase for key '…':"). All temp files live in a private
/// directory with 0700 permissions, and are cleaned up on dispose.
///
/// Lifecycle: create one bridge per async git command that might need
/// credentials (push/pull/fetch). Dispose after the command completes.
/// </summary>
public sealed class AskPassBridge : IDisposable
{
    private readonly bool _enabled;
    private readonly string? _dir;
    private readonly string? _scriptPath;
    private readonly string? _promptPath;
    private readonly string? _responseFifo;

    public AskPassBridge()
    {
        // The FIFO + shell-script mechanism only exists on Unix. On Windows the
        // bridge is inert and git falls back to its native credential helpers.
        _enabled = OperatingSystem.IsLinux() || OperatingSystem.IsMacOS();
        if (!_enabled)
            return;

        var id = Guid.NewGuid().ToString("N")[..12];

        // Private temp directory — 0700 so only the current user can access it.
        _dir = Path.Combine(Path.GetTempPath(), $"hultui-askpass-{id}");
        Directory.CreateDirectory(_dir);
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            File.SetUnixFileMode(_dir,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        _promptPath = Path.Combine(_dir, "prompt");
        _responseFifo = Path.Combine(_dir, "response");
        _scriptPath = Path.Combine(_dir, "askpass.sh");

        // Create the FIFO for the response channel. mkfifo creates it with
        // the specified mode — 0600 means only the owner can read/write.
        // The script's `cat` blocks until the app writes, so no polling loop
        // is needed in the script and the passphrase never hits disk.
        using var mkfifo = Process.Start(new ProcessStartInfo
        {
            FileName = "mkfifo",
            ArgumentList = { "-m", "0600", _responseFifo },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        });
        mkfifo?.WaitForExit();

        // The askpass script:
        //   1. Writes the prompt text to a regular file (not sensitive).
        //   2. Reads the passphrase from the FIFO (blocking — cat waits
        //      until the app writes). Data is kernel-buffered, never on disk.
        File.WriteAllText(_scriptPath, $"""
            #!/bin/sh
            printf '%s' "$1" > '{Escape(_promptPath)}'
            cat '{Escape(_responseFifo)}'
            """);

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            File.SetUnixFileMode(_scriptPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    /// <summary>
    /// Configures a <see cref="ProcessStartInfo"/> to use this bridge's
    /// askpass script instead of the terminal for credential prompts.
    /// </summary>
    public void ConfigureProcess(ProcessStartInfo psi)
    {
        if (!_enabled)
            return;

        psi.Environment["SSH_ASKPASS"] = _scriptPath;
        psi.Environment["SSH_ASKPASS_REQUIRE"] = "force";
        psi.Environment["GIT_ASKPASS"] = _scriptPath;
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        psi.RedirectStandardInput = true;
    }

    /// <summary>
    /// Polls for askpass prompts while the process runs. Each time the
    /// askpass script writes a prompt file, <paramref name="handler"/> is
    /// invoked with the prompt text. If it returns a non-null string, that
    /// is written to the FIFO (unblocking the script); if null the process
    /// is killed (cancel).
    /// </summary>
    public async Task MonitorAsync(Process process, Func<string, Task<string?>> handler)
    {
        if (!_enabled)
            return;

        while (!process.HasExited)
        {
            if (File.Exists(_promptPath))
            {
                string prompt;
                try { prompt = (await File.ReadAllTextAsync(_promptPath)).Trim(); }
                catch { await Task.Delay(50); continue; }

                // Delete the prompt file before awaiting the handler so we
                // can detect a fresh prompt if SSH retries (wrong passphrase).
                try { File.Delete(_promptPath); } catch { }

                var response = await handler(prompt);
                if (response != null)
                {
                    // Write to the FIFO — this unblocks the script's `cat`.
                    // The data stays in kernel buffers, never on disk.
                    await File.WriteAllTextAsync(_responseFifo!, response);
                }
                else
                {
                    // User cancelled — unblock the script with empty data, then kill.
                    await File.WriteAllTextAsync(_responseFifo!, "");
                    try { process.Kill(); } catch { }
                    return;
                }
            }
            await Task.Delay(100);
        }
    }

    public void Dispose()
    {
        if (!_enabled)
            return;

        try { Directory.Delete(_dir!, recursive: true); } catch { }
    }

    private static string Escape(string path) => path.Replace("'", "'\\''");
}
