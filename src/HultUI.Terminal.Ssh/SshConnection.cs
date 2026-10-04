using Renci.SshNet;
using Renci.SshNet.Common;

namespace HultUI.Terminal.Ssh;

/// <summary>The host rejected: its key was revoked, changed or unknown and not accepted.</summary>
public sealed class SshHostKeyRejectedException(SshHostKey hostKey, Exception? innerException = null)
    : Exception($"The host key of {hostKey.Host} was not trusted: {hostKey.Status}, {hostKey.Fingerprint}.", innerException)
{
    /// <summary>The key the host presented, and what the known hosts made of it.</summary>
    public SshHostKey HostKey { get; } = hostKey;
}

/// <summary>
/// A shell on another machine over SSH, through SSH.NET. Give it to a
/// <see cref="TerminalController"/>, which owns it from then on.
/// </summary>
/// <remarks>
/// <para><b>Trust.</b> The host's key is checked against <see cref="SshConnectOptions.KnownHosts"/>
/// before anything is sent: a known key connects, a revoked one never does, and an unknown or
/// changed one only on <see cref="SshConnectOptions.VerifyHostKey"/>'s decision. SSH.NET itself
/// trusts every key unless told otherwise; this always tells it.</para>
/// <para><b>The shell</b> gets a pseudoterminal of <see cref="SshConnectOptions.TerminalName"/>,
/// UTF-8 input (<c>IUTF8</c>) and DEL as erase, and follows the element's grid.</para>
/// <para><b>Ending.</b> <see cref="ITerminalConnection.Exited"/> carries the remote program's exit
/// code, or 128 plus the signal's number, as a shell would report it. It is null when the
/// connection was lost or ended here, and when the program ended within the last round trip of
/// connecting, before the code could be listened for.</para>
/// <para><b>Flow.</b> Output waiting unread past <see cref="SshConnectOptions.MaxBufferedBytes"/>
/// stops the connection reading from the network until the terminal catches up.</para>
/// <para><b>Windows' OpenSSH server</b> runs the shell under ConPTY, so such a connection has
/// ConPTY's traits: it reflows its own screen, and asks for win32-input-mode keys.</para>
/// </remarks>
public sealed class SshConnection : ITerminalConnection
{
    private readonly SshClient _client;
    private readonly ShellStream _shell;
    private readonly OutputStream _output;
    private readonly InputStream _input;
    private readonly SshExitStatus _status;
    private readonly ConnectionExit _exit = new();
    private int _closed;

    private SshConnection(SshClient client, ShellStream shell, SshExitStatus status, SshHostKey hostKey, long maxBuffered)
    {
        _client = client;
        _shell = shell;
        _status = status;
        HostKey = hostKey;
        ServerVersion = client.ConnectionInfo.ServerVersion ?? "";
        _output = new OutputStream(this, shell, maxBuffered);
        _input = new InputStream(shell);
        // The shell ended, or the connection is gone: end the session. On a pool thread, as these
        // come on SSH.NET's own, and disconnecting waits for its receiving thread. What the shell
        // stream holds is still read after it is closed. A lost connection must be handled here:
        // SSH.NET reports it but leaves the shell stream open, and its reader waits forever.
        _shell.Closed += (_, _) => { Ended(); Task.Run(Close); };
        _shell.ErrorOccurred += (_, _) => Task.Run(Close);
    }

    /// <summary>
    /// Connects, checks the host's key, signs in and starts a shell.
    /// </summary>
    /// <exception cref="SshHostKeyRejectedException">The host's key was not trusted.</exception>
    /// <exception cref="SshSignInException">No way of signing in was accepted; it says what each
    /// did, and what the server takes.</exception>
    /// <exception cref="SshConnectionException">The connection failed or was refused.</exception>
    /// <exception cref="SshOperationTimeoutException">The host did not answer in time.</exception>
    /// <exception cref="System.Net.Sockets.SocketException">The host could not be reached.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was
    /// cancelled.</exception>
    public static async Task<SshConnection> ConnectAsync(SshConnectOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Authentication.Count == 0)
            throw new ArgumentException("Give at least one way to sign in.", nameof(options));

        var log = new SignInLog();
        var methods = options.Authentication.Select(a => a.Create(options.User, options.Host, log.Add(a), cancellationToken)).ToArray();
        var info = new ConnectionInfo(options.Host, options.Port, options.User, methods) { Timeout = options.ConnectTimeout };
        if (options.Banner is { } banner) info.AuthenticationBanner += (_, e) => banner(e.BannerMessage);
        var client = new SshClient(info)
        {
            KeepAliveInterval = options.KeepAlive > TimeSpan.Zero ? options.KeepAlive : Timeout.InfiniteTimeSpan,
        };

        SshHostKey? hostKey = null;
        bool rejected = false;
        client.HostKeyReceived += (_, e) =>
        {
            e.CanTrust = false; // SSH.NET's default is true
            hostKey = Check(options, e.HostKey, cancellationToken, out bool trusted);
            rejected = !trusted;
            e.CanTrust = trusted;
        };

        try
        {
            // Off the caller's thread: the host key and the prompts wait on the UI thread, which
            // may well be the caller.
            await Task.Run(() => client.ConnectAsync(cancellationToken), cancellationToken).ConfigureAwait(false);
            var status = new SshExitStatus();
            var modes = new Dictionary<TerminalModes, uint>
            {
                [TerminalModes.IUTF8] = 1,
                [TerminalModes.VERASE] = 0x7F,
            };
            var shell = client.CreateShellStream(options.TerminalName,
                (uint)Math.Max(1, options.Columns), (uint)Math.Max(1, options.Rows), 0, 0, 32 * 1024, modes);
            status.TryWatch(shell);
            return new SshConnection(client, shell, status, hostKey!, options.MaxBufferedBytes);
        }
        catch (Exception ex)
        {
            client.Dispose();
            cancellationToken.ThrowIfCancellationRequested();
            // SSH.NET reports its own refusal as a plain connection failure ("Host key could not
            // be verified"), with no reason to tell it by; this side knows it said no.
            if (rejected && hostKey is { } key) throw new SshHostKeyRejectedException(key, ex);
            if (ex is SshAuthenticationException)
                throw new SshSignInException(options.User, options.Host, log.ServerAccepts ?? AcceptsIn(ex.Message), log.Lines(), ex);
            throw;
        }
    }

    /// <summary>The methods SSH.NET names when none it was given is one the server takes: "No
    /// suitable authentication method found to complete authentication (publickey,password)."</summary>
    private static string[] AcceptsIn(string message)
    {
        int open = message.LastIndexOf('('), close = message.LastIndexOf(')');
        return message.StartsWith("No suitable", StringComparison.Ordinal) && open >= 0 && close > open
            ? message[(open + 1)..close].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [];
    }

    /// <summary>What the known hosts and the verifier make of the key. Records it when the user
    /// trusts it.</summary>
    private static SshHostKey Check(SshConnectOptions options, byte[] key, CancellationToken cancellation, out bool trusted)
    {
        IReadOnlyList<string> onFile = [];
        var status = options.KnownHosts?.Check(options.Host, options.Port, key, out onFile) ?? HostKeyStatus.Unknown;
        var hostKey = new SshHostKey(options.Host, options.Port, key, status, onFile);
        trusted = status == HostKeyStatus.Known;
        if (status is HostKeyStatus.Known or HostKeyStatus.Revoked || options.VerifyHostKey is null) return hostKey;

        HostKeyDecision decision;
        try { decision = options.VerifyHostKey(hostKey, cancellation).GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { return hostKey; }
        if (decision == HostKeyDecision.Trust && options.KnownHosts is { } known)
        {
            if (status == HostKeyStatus.Changed) known.Replace(options.Host, options.Port, key);
            else known.Add(options.Host, options.Port, key);
        }
        trusted = decision is HostKeyDecision.Trust or HostKeyDecision.AcceptOnce;
        return hostKey;
    }

    /// <summary>The key the host presented, and what the known hosts made of it.</summary>
    public SshHostKey HostKey { get; }

    /// <summary>The server's identification, e.g. <c>SSH-2.0-OpenSSH_9.6p1 Ubuntu-3ubuntu13</c>.</summary>
    public string ServerVersion { get; }

    public Stream Output => _output;
    public Stream Input => _input;

    public TerminalConnectionTraits Traits => TraitsOf(ServerVersion);

    /// <summary>Windows' OpenSSH server names itself <c>OpenSSH_for_Windows</c>, and runs the shell
    /// under ConPTY.</summary>
    internal static TerminalConnectionTraits TraitsOf(string serverVersion) =>
        serverVersion.Contains("OpenSSH_for_Windows", StringComparison.OrdinalIgnoreCase)
            ? TerminalConnectionTraits.ReflowsItself | TerminalConnectionTraits.WantsWin32Input
            : TerminalConnectionTraits.None;

    public event Action<int?>? Exited
    {
        add => _exit.Add(value);
        remove => _exit.Remove(value);
    }

    /// <summary>Output waiting unread; for tests.</summary>
    internal long Buffered => _output.Pending;

    public void Resize(int columns, int rows)
    {
        if (Volatile.Read(ref _closed) != 0) return;
        try { _shell.ChangeWindowSize((uint)Math.Max(1, columns), (uint)Math.Max(1, rows), 0, 0); }
        catch (Exception ex) when (ex is SshException or ObjectDisposedException or InvalidOperationException) { }
    }

    /// <summary>Closes the shell and the connection; the remote side hangs its programs up.</summary>
    public void Terminate() => Close();

    private void Ended() => _exit.Raise(_status.Code);

    private void Close()
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0) return;
        // First let SSH.NET's receiving thread go, if the flow control holds it: disconnecting
        // waits for that thread.
        _output.Release();
        try { _shell.Dispose(); }
        catch (Exception ex) when (ex is SshException or ObjectDisposedException or InvalidOperationException) { }
        try { _client.Disconnect(); }
        catch (Exception ex) when (ex is SshException or ObjectDisposedException or InvalidOperationException
            or IOException or System.Net.Sockets.SocketException) { }
        Ended();
    }

    public void Dispose()
    {
        Close();
        _client.Dispose();
    }

    /// <summary>
    /// The shell's output, read by the controller's reader, with flow control: SSH.NET
    /// acknowledges data as it arrives, not as it is read, so past the cap this holds SSH.NET's
    /// receiving thread until the reader catches up. Nothing more is then read from the socket,
    /// and TCP stops the server.
    /// </summary>
    private sealed class OutputStream : Stream
    {
        private readonly SshConnection _owner;
        private readonly ShellStream _shell;
        private readonly long _cap;
        private readonly object _gate = new();
        private long _pending;
        private bool _released;

        public OutputStream(SshConnection owner, ShellStream shell, long cap)
        {
            _owner = owner;
            _shell = shell;
            _cap = cap;
            _shell.DataReceived += OnData;
        }

        public long Pending { get { lock (_gate) return _pending; } }

        // On SSH.NET's receiving thread, after the shell stream took the data in.
        private void OnData(object? sender, ShellDataEventArgs e)
        {
            lock (_gate)
            {
                _pending += e.Data.Length;
                while (_pending > _cap && !_released) Monitor.Wait(_gate);
            }
        }

        public void Release()
        {
            lock (_gate)
            {
                _released = true;
                Monitor.PulseAll(_gate);
            }
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            int n;
            try { n = _shell.Read(buffer); }
            catch (SshException ex) { throw new IOException(ex.Message, ex); }
            lock (_gate)
            {
                // Not clamped: the reader can take bytes before OnData has counted them, and the
                // count comes right when it does.
                _pending -= n;
                if (_pending <= _cap) Monitor.PulseAll(_gate);
            }
            if (n == 0) _owner.Ended();
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
    }

    /// <summary>What the shell reads. SSH.NET's own errors become IOException, which the
    /// controller's writer stops on; anything else would end its thread with an exception.</summary>
    private sealed class InputStream(ShellStream shell) : Stream
    {
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            try { shell.Write(buffer); }
            catch (SshException ex) { throw new IOException(ex.Message, ex); }
        }

        public override void Flush()
        {
            try { shell.Flush(); }
            catch (SshException ex) { throw new IOException(ex.Message, ex); }
        }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
