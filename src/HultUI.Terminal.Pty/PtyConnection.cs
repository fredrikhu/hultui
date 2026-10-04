using System.Text;
using Porta.Pty;

namespace HultUI.Terminal.Pty;

/// <summary>
/// A local program under a pseudoterminal: ConPTY on Windows (Microsoft's out-of-band
/// <c>conpty.dll</c> and <c>OpenConsole.exe</c>, falling back to the one Windows has), forkpty on
/// Linux and macOS. Give it to a <see cref="TerminalController"/>, which owns it from then on.
/// </summary>
/// <remarks>
/// <para><b>Ending.</b> <see cref="ITerminalConnection.Exited"/> carries the program's exit code.
/// ConPTY keeps the output open after its program exits, until the pseudoconsole is closed, so on
/// Windows the pseudoconsole is closed when the program exits. The console host then paints the
/// last of what the program wrote and ends the output, the controller reads it all and sees the
/// end, and only then is the rest released. A program that leaves a background process attached
/// loses it then, as closing a terminal tab does.</para>
/// <para><b>Terminate</b> kills the program and everything it started that shares its console
/// (a job object on Windows). The exit code is then -1.</para>
/// </remarks>
public sealed class PtyConnection : ITerminalConnection
{
    /// <summary>How long after the program exits the rest is released even though the output
    /// has not been read to its end — a safety net: closing the pseudoconsole ends it.</summary>
    internal static readonly TimeSpan MostAfterExit = TimeSpan.FromSeconds(10);

    private readonly IPtyConnection _pty;
    private readonly OutputStream _output;
    private readonly ConnectionExit _exit = new();
    private int _closed;

    private PtyConnection(IPtyConnection pty, string program)
    {
        _pty = pty;
        Program = program;
        ProcessId = pty.Pid;
        _output = new OutputStream(pty.ReaderStream);
        _pty.ProcessExited += OnProcessExited;
        // A quick program can be gone before the line above: its exit event has been and gone.
        if (_pty.WaitForExit(0)) ProgramEnded(_pty.ExitCode);
    }

    /// <summary>
    /// Starts <paramref name="launch"/> under a pseudoterminal of
    /// <paramref name="columns"/> × <paramref name="rows"/>; the element resizes it to its grid.
    /// </summary>
    /// <exception cref="FileNotFoundException">The program is not a file and is not on
    /// <c>PATH</c>.</exception>
    /// <exception cref="InvalidOperationException">The pseudoterminal or the program could not
    /// start.</exception>
    public static PtyConnection Start(PtyLaunch launch, int columns = 80, int rows = 24)
    {
        // Off the calling thread's context: SpawnAsync awaits, and the UI thread has a
        // synchronization context that would wait for itself.
        return Task.Run(() => StartAsync(launch, columns, rows)).GetAwaiter().GetResult();
    }

    /// <inheritdoc cref="Start"/>
    public static async Task<PtyConnection> StartAsync(PtyLaunch launch, int columns = 80, int rows = 24,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(launch);
        var program = PtyLaunch.FindProgram(launch.Program)
            ?? throw new FileNotFoundException($"'{launch.Program}' is not a file and is not on PATH.", launch.Program);

        var environment = new Dictionary<string, string>
        {
            ["TERM"] = "xterm-256color",
            ["COLORTERM"] = "truecolor",
        };
        foreach (var (name, value) in launch.Environment)
            environment[name] = value ?? ""; // Porta.Pty removes a variable given an empty value

        bool windows = OperatingSystem.IsWindows();
        var pty = await PtyProvider.SpawnAsync(new PtyOptions
        {
            App = program,
            CommandLine = windows ? [.. launch.Arguments.Select(WindowsCommandLine.Quote)] : [.. launch.Arguments],
            VerbatimCommandLine = windows,
            Cols = Math.Max(1, columns),
            Rows = Math.Max(1, rows),
            Cwd = launch.WorkingDirectory ?? System.Environment.CurrentDirectory,
            Name = "xterm-256color",
            Environment = environment,
            // Overlapped pipes, and with them Porta.Pty closes the pseudoconsole itself the moment
            // the program exits, leaving our end of the output open: the console host paints its
            // final frame into it and ends it, which is how Microsoft's ClosePseudoConsole asks to
            // be used. Without it the output stays open until we close everything at once.
            UseAsyncIo = windows,
        }, cancellationToken).ConfigureAwait(false);
        return new PtyConnection(pty, program);
    }

    /// <summary>The program's full path, as found.</summary>
    public string Program { get; }

    /// <summary>The program's process id.</summary>
    public int ProcessId { get; }

    public Stream Output => _output;
    public Stream Input => _pty.WriterStream;

    /// <summary>On Windows, ConPTY: it reflows its own copy of the screen on a resize, and asks
    /// for win32-input-mode keys.</summary>
    public TerminalConnectionTraits Traits => OperatingSystem.IsWindows()
        ? TerminalConnectionTraits.ReflowsItself | TerminalConnectionTraits.WantsWin32Input
        : TerminalConnectionTraits.None;

    public event Action<int?>? Exited
    {
        add => _exit.Add(value);
        remove => _exit.Remove(value);
    }

    public void Resize(int columns, int rows)
    {
        if (Volatile.Read(ref _closed) != 0) return;
        try { _pty.Resize(Math.Max(1, columns), Math.Max(1, rows)); }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException) { }
    }

    public void Terminate()
    {
        try { _pty.Kill(); }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException
            or System.ComponentModel.Win32Exception) { }
    }

    private void OnProcessExited(object? sender, PtyExitedEventArgs e) => ProgramEnded(e.ExitCode);

    private void ProgramEnded(int exitCode)
    {
        if (!_exit.Raise(exitCode)) return;
        if (OperatingSystem.IsWindows())
            _ = Task.Run(CloseWhenRead);
    }

    /// <summary>
    /// Releases the rest once the output has been read to its end. Porta.Pty has closed the
    /// pseudoconsole at the exit (<c>UseAsyncIo</c>), so the end comes; closing our end of the
    /// pipe any sooner throws away what is still in it, because Porta.Pty's <c>Dispose</c> closes
    /// the pipe along with everything else.
    /// <para>This was timed: everything closed once nothing had been read for 100 ms after the
    /// exit. The console host paints on its own schedule and a reader starts on its own, and on a
    /// loaded machine — a cold test process among thirty — a quick program's whole output could
    /// still be in the pipe then: <c>cmd /c echo</c> came out as an empty screen.</para>
    /// </summary>
    private async Task CloseWhenRead()
    {
        await Task.WhenAny(_output.Ended, Task.Delay(MostAfterExit)).ConfigureAwait(false);
        Close();
    }

    private void Close()
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0) return;
        try { _pty.Dispose(); }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException) { }
    }

    public void Dispose()
    {
        _pty.ProcessExited -= OnProcessExited;
        Close();
    }

    /// <summary>The pseudoterminal's output, noting when a read has reached its end.</summary>
    private sealed class OutputStream(Stream inner) : Stream
    {
        private readonly TaskCompletionSource _ended = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Completes when a read has returned the end of the output.</summary>
        public Task Ended => _ended.Task;

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            int n;
            try { n = inner.Read(buffer); }
            catch (IOException)
            {
                // A broken pipe is an end too, however the stream reports it.
                _ended.TrySetResult();
                throw;
            }
            if (n == 0 && buffer.Length > 0) _ended.TrySetResult();
            return n;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }
    }
}

/// <summary>
/// One argument as the C runtime and <c>CommandLineToArgvW</c> split a Windows command line:
/// bare when it can be, otherwise quoted, with backslashes doubled only where they come before
/// a quote. Measured in the spike against <c>cmd /c exit 3</c> and argv round trips.
/// </summary>
internal static class WindowsCommandLine
{
    public static string Quote(string argument)
    {
        if (argument.Length > 0 && argument.IndexOfAny([' ', '\t', '\n', '\v', '"']) < 0) return argument;
        var sb = new StringBuilder(argument.Length + 2).Append('"');
        int slashes = 0;
        foreach (var ch in argument)
        {
            if (ch == '\\') { slashes++; continue; }
            sb.Append('\\', ch == '"' ? slashes * 2 + 1 : slashes).Append(ch);
            slashes = 0;
        }
        return sb.Append('\\', slashes * 2).Append('"').ToString();
    }
}
