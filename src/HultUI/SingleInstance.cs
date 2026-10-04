using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace HultUI;

/// <summary>
/// One running copy of an app per person: the first launch claims the app, and a later one hands
/// its command line to that copy and exits — so opening ten files from a file manager gives one
/// window with ten things opened in it, not ten windows.
/// </summary>
/// <remarks>
/// <code>
/// using var instance = SingleInstance.Claim("Reel");
/// if (!instance.IsFirst &amp;&amp; instance.Forward(args)) return;   // handed over; this launch is done
///
/// using var app = new Application("Reel", 1280, 760);
/// ...
/// instance.Listen(launch =>                                  // on the UI thread
/// {
///     if (launch.Args.Count > 0) player.Open(launch.FullPath(launch.Args[0]));
///     window.Raise();
/// });
/// app.Run();
/// </code>
/// <para><b>Nothing a launch asked for is lost.</b> The first copy listens from the moment it
/// claims the app, before it has a window, and keeps what arrives until <see cref="Listen"/> is
/// called: a file manager starts one process per selected file all at once, and every one of
/// them but the first lands while the first is still starting. <see cref="Forward"/> waits for
/// the running copy to say it has the message, and returns false when it cannot — a copy that is
/// hung or shutting down — so the caller can open its own window rather than drop the
/// request.</para>
/// <para><b>Paths mean what they meant to the launch.</b> The command line is sent with the
/// working directory it was given in; <see cref="SecondLaunch.FullPath"/> resolves against that,
/// not against wherever the running copy was started.</para>
/// <para><b>Raising the window is allowed.</b> Windows does not let a background process take
/// the foreground, and the running copy is one. The launch that forwards — which the person just
/// started, so has that right — passes it on before it sends, and <see cref="Window.Raise"/> in
/// the handler works. Wayland gives an app no way to raise itself for another process.</para>
/// <para><b>Per person, and per session on Windows.</b> The claim is a locked file in the user's
/// own temporary folder (the runtime folder on Linux) and the channel a named pipe only the same
/// user can open, from either end. Two people on one machine each get their own copy; so do two
/// Windows sessions of one person, since a window on the other session's desktop is no use.</para>
/// </remarks>
public sealed partial class SingleInstance : IDisposable
{
    /// <summary>How long <see cref="Forward"/> waits for the running copy to take a launch.</summary>
    public static readonly TimeSpan ForwardTimeout = TimeSpan.FromSeconds(3);

    private const byte Version = 1;
    private const byte Ack = 0x06;
    private const int MaxMessage = 1 << 20;
    private const int MaxArgs = 4096;
    private static readonly TimeSpan ReceiveTimeout = TimeSpan.FromSeconds(5);

    private readonly string _pipeName;
    private readonly FileStream? _lock;
    private readonly CancellationTokenSource _stop = new();
    private readonly object _gate = new();
    private readonly List<SecondLaunch> _early = [];
    private Action<SecondLaunch>? _onLaunch;
    private bool _disposed;

    /// <summary>True when this launch claimed the app: it runs, and later launches come to it.
    /// False when another copy is already running — <see cref="Forward"/> to it.</summary>
    public bool IsFirst { get; }

    private SingleInstance(string pipeName, FileStream? lockFile, bool isFirst)
    {
        _pipeName = pipeName;
        _lock = lockFile;
        IsFirst = isFirst;
    }

    /// <summary>
    /// Claims <paramref name="appId"/> for this launch, or finds the copy that already has it.
    /// The id names the app, not a build of it: two apps that share one would hand each other
    /// their launches.
    /// </summary>
    public static SingleInstance Claim(string appId) => Claim(appId, listen: true);

    /// <summary>Without <paramref name="listen"/>, a claim with no one answering: a copy that is
    /// hung, as far as a later launch can tell. For tests.</summary>
    internal static SingleInstance Claim(string appId, bool listen)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appId);
        var key = Key(appId);
        var (lockFile, first) = TakeLock(Path.Combine(LockFolder(), key + ".lock"));
        var instance = new SingleInstance(key, lockFile, first);
        if (first && listen) instance.StartServer();
        return instance;
    }

    /// <summary>The pipe later launches connect to. For tests.</summary>
    internal string PipeName => _pipeName;

    /// <summary>
    /// Hands this launch's command line, with its working directory, to the running copy. True
    /// once that copy has it; false when it did not answer within <see cref="ForwardTimeout"/>,
    /// and the launch should carry on as an app of its own.
    /// </summary>
    public bool Forward(IReadOnlyList<string> args) => ForwardWithin(args, ForwardTimeout);

    internal bool ForwardWithin(IReadOnlyList<string> args, TimeSpan timeout)
    {
        if (IsFirst) throw new InvalidOperationException("This launch claimed the app; there is no other copy to forward to.");
        ObjectDisposedException.ThrowIf(_disposed, this);
        var message = Encode(args, Environment.CurrentDirectory);
        try
        {
            // Off any synchronization context: this blocks, and is called before the app has one,
            // but a continuation posted to a UI thread that is waiting here would never run.
            return Task.Run(() => SendAsync(message, timeout)).GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or OperationCanceledException
                                       or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Starts handing later launches to <paramref name="onLaunch"/>, on the UI thread — the ones
    /// that arrived before this call first, in the order they came. Call it once the
    /// <see cref="Application"/> exists, which is where the handler is run.
    /// </summary>
    public void Listen(Action<SecondLaunch> onLaunch)
    {
        ArgumentNullException.ThrowIfNull(onLaunch);
        if (!IsFirst) throw new InvalidOperationException("Only the launch that claimed the app receives the others.");
        lock (_gate)
        {
            _onLaunch = onLaunch;
            foreach (var launch in _early)
                Application.InvokeOnMainThread(() => onLaunch(launch));
            _early.Clear();
        }
    }

    /// <summary>Stops listening and gives up the claim, so the next launch is the first.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stop.Cancel();
        _lock?.Dispose();
    }

    // ──── Claim ────

    /// <summary>The name of the lock file and the pipe: the app, the person, and on Windows the
    /// session. A hash of the account rather than its name, which can hold anything.</summary>
    private static string Key(string appId)
    {
        var app = new StringBuilder(appId.Length);
        foreach (char c in appId)
            app.Append(char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_');
        var who = SHA256.HashData(Encoding.UTF8.GetBytes(Environment.UserDomainName + "\\" + Environment.UserName));
        var key = $"HultUI.{app}.{Convert.ToHexStringLower(who, 0, 6)}";
        // Not on Linux, where a session is a terminal's: two launches from two terminals are
        // one person at one desktop.
        if (OperatingSystem.IsWindows())
        {
            using var self = System.Diagnostics.Process.GetCurrentProcess();
            key += $".s{self.SessionId}";
        }
        return key;
    }

    /// <summary>The user's temporary folder on Windows. On Linux the runtime folder, which is the
    /// user's alone and gone at log-out; /tmp, shared, when there is none.</summary>
    private static string LockFolder()
    {
        if (!OperatingSystem.IsWindows()
            && Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") is { Length: > 0 } runtime
            && Path.IsPathFullyQualified(runtime) && Directory.Exists(runtime))
            return runtime;
        return Path.GetTempPath();
    }

    /// <summary>
    /// The claim is the file held open with no sharing — a share-mode conflict on Windows, an
    /// advisory <c>flock</c> on Linux. The OS lets go when the process ends however it ends, so a
    /// crashed copy never leaves the app claimed.
    /// </summary>
    private static (FileStream? Lock, bool First) TakeLock(string path)
    {
        try
        {
            // Deleted with the last handle on Windows. Not on Linux, where unlinking a locked file
            // lets a third launch lock a new one beside the second's.
            var options = OperatingSystem.IsWindows() ? FileOptions.DeleteOnClose : FileOptions.None;
            return (new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, options), true);
        }
        catch (IOException ex) when (ex is not (FileNotFoundException or DirectoryNotFoundException or PathTooLongException))
        {
            return (null, false);   // held: another copy is running
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Unknowable — a folder that cannot be written, a file another account made. Run
            // rather than hand the launch to a copy that may not exist.
            Console.Error.WriteLine($"[HultUI] Single-instance claim at {path} failed ({ex.Message}); running unclaimed.");
            return (null, true);
        }
    }

    // ──── Running copy ────

    private void StartServer()
    {
        // A copy that crashed on Linux leaves its socket file behind. Holding the claim means no
        // live copy owns it.
        if (!OperatingSystem.IsWindows())
        {
            try { File.Delete(Path.Combine(Path.GetTempPath(), "CoreFxPipe_" + _pipeName)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        // The first listener is made here, so the pipe exists before Claim returns.
        var first = TryCreateServer();
        _ = Task.Run(() => ServeAsync(first, _stop.Token));
    }

    private NamedPipeServerStream? TryCreateServer()
    {
        try
        {
            return new NamedPipeServerStream(_pipeName, PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Accepts launches until disposed. Each connection is read on its own task while the next
    /// listener waits, so a burst of launches is taken in parallel. A listener that cannot be
    /// made is retried with a growing pause and then given up — never in a tight loop, which is
    /// what the apps' own copies did when the name was taken.
    /// </summary>
    private async Task ServeAsync(NamedPipeServerStream? next, CancellationToken ct)
    {
        int failures = 0;
        while (!ct.IsCancellationRequested)
        {
            var server = next ?? TryCreateServer();
            next = null;
            if (server is null)
            {
                if (++failures > 5)
                {
                    Console.Error.WriteLine($"[HultUI] Single-instance pipe {_pipeName} could not be opened; later launches will open their own windows.");
                    return;
                }
                try { await Task.Delay(100 << failures, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
                continue;
            }

            try
            {
                await server.WaitForConnectionAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { await server.DisposeAsync().ConfigureAwait(false); return; }
            catch (IOException) { await server.DisposeAsync().ConfigureAwait(false); continue; }

            failures = 0;
            // The next listener before this connection is handed off. On Linux every listener
            // with one name shares a single socket, closed with the last of them: a connection
            // read and released before its successor existed closed it, and reset the launches
            // queued on it (a burst lost some on Linux and none on Windows).
            next = TryCreateServer();
            _ = ReceiveAsync(server, ct);
        }
    }

    private async Task ReceiveAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        await using (pipe.ConfigureAwait(false))
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(ReceiveTimeout);

                var header = new byte[4];
                await pipe.ReadExactlyAsync(header, timeout.Token).ConfigureAwait(false);
                int length = BitConverter.ToInt32(header);
                if (length is <= 0 or > MaxMessage) return;
                var body = new byte[length];
                await pipe.ReadExactlyAsync(body, timeout.Token).ConfigureAwait(false);

                // Taken before it is acknowledged: the launch exits as soon as it has the answer.
                Deliver(Decode(body));
                await pipe.WriteAsync(new[] { Ack }, timeout.Token).ConfigureAwait(false);
                await pipe.FlushAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or EndOfStreamException
                                           or InvalidDataException or ObjectDisposedException)
            {
                // A launch that gave up, or something that was not one. Keep listening.
            }
        }
    }

    private void Deliver(SecondLaunch launch)
    {
        lock (_gate)
        {
            if (_onLaunch is { } onLaunch) Application.InvokeOnMainThread(() => onLaunch(launch));
            else _early.Add(launch);
        }
    }

    // ──── Launch that forwards ────

    private async Task<bool> SendAsync(byte[] message, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        await using var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        // Retries until the listener exists: the running copy may have claimed the app a moment
        // ago and not yet opened its pipe.
        await pipe.ConnectAsync(cts.Token).ConfigureAwait(false);
        if (OperatingSystem.IsWindows()) LetServerTakeForeground(pipe);

        await pipe.WriteAsync(message, cts.Token).ConfigureAwait(false);
        await pipe.FlushAsync(cts.Token).ConfigureAwait(false);
        var answer = new byte[1];
        int read = await pipe.ReadAsync(answer, cts.Token).ConfigureAwait(false);
        return read == 1 && answer[0] == Ack;
    }

    /// <summary>The foreground right this launch has, handed to the process that will raise its
    /// window. Without it Windows only flashes the running copy's task bar button.</summary>
    [SupportedOSPlatform("windows")]
    private static void LetServerTakeForeground(NamedPipeClientStream pipe)
    {
        if (GetNamedPipeServerProcessId(pipe.SafePipeHandle.DangerousGetHandle(), out uint pid))
            AllowSetForegroundWindow(pid);
    }

    // ──── Wire format: [int32 length][version, working directory, count, args...] ────

    internal static byte[] Encode(IReadOnlyList<string> args, string workingDirectory)
    {
        using var body = new MemoryStream();
        using (var w = new BinaryWriter(body, Encoding.UTF8, leaveOpen: true))
        {
            w.Write(Version);
            w.Write(workingDirectory);
            w.Write(args.Count);
            foreach (var arg in args) w.Write(arg);
        }
        var message = new byte[4 + body.Length];
        BitConverter.TryWriteBytes(message, (int)body.Length);
        body.GetBuffer().AsSpan(0, (int)body.Length).CopyTo(message.AsSpan(4));
        return message;
    }

    internal static SecondLaunch Decode(byte[] body)
    {
        using var r = new BinaryReader(new MemoryStream(body), Encoding.UTF8);
        if (r.ReadByte() != Version) throw new InvalidDataException("Unknown single-instance message version.");
        var workingDirectory = r.ReadString();
        int count = r.ReadInt32();
        if (count is < 0 or > MaxArgs) throw new InvalidDataException("Implausible argument count.");
        var args = new string[count];
        for (int i = 0; i < count; i++) args[i] = r.ReadString();
        return new SecondLaunch(args, workingDirectory);
    }

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetNamedPipeServerProcessId(nint pipe, out uint serverProcessId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllowSetForegroundWindow(uint processId);
}

/// <summary>A later launch of the app, handed to the copy already running.</summary>
/// <param name="Args">Its command line, as it was given.</param>
/// <param name="WorkingDirectory">The directory it was started in.</param>
public sealed record SecondLaunch(IReadOnlyList<string> Args, string WorkingDirectory)
{
    /// <summary>
    /// <paramref name="path"/> resolved the way the launch would have resolved it: a relative
    /// path against its working directory, not the running copy's. Comes back unchanged when it
    /// is not a path that can be resolved.
    /// </summary>
    public string FullPath(string path)
    {
        try { return Path.GetFullPath(path, WorkingDirectory); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path;
        }
    }
}
