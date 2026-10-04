using System.Text;

namespace HultUI.Terminal;

/// <summary>
/// A connection made in the background, so the terminal shows at once. It says what it is
/// connecting to, and the program's output follows when the connection is made; if it fails, why
/// is written into the terminal and the session ends.
/// </summary>
/// <remarks>
/// <para><b>Why.</b> A <see cref="TerminalController"/> needs its connection when it is made,
/// and making some takes a while and asks the user things on the way: an SSH host's key, a
/// password. Made on the UI thread, the questions would wait for the thread that is waiting for
/// them. Made here, <paramref name="connect"/> runs on a pool thread, and its prompts can show
/// their dialogs on the UI thread while the terminal waits.</para>
/// <para><b>Meanwhile.</b> What is typed is kept and sent once connected, and the last size the
/// element asked for is applied then. <see cref="Terminate"/> cancels the connecting.</para>
/// <para><b>Traits</b> are the connection's once it is made, and none before.</para>
/// </remarks>
/// <example>
/// <code>
/// var shell = new TerminalController(new DeferredConnection(
///     async ct => await SshConnection.ConnectAsync(options, ct),
///     connecting: "Connecting to build.example.com…"));
/// </code>
/// </example>
public sealed class DeferredConnection : ITerminalConnection
{
    /// <summary>The most that is kept of what is typed before the connection is made.</summary>
    private const int MostPendingInput = 64 * 1024;

    private readonly object _gate = new();
    private readonly TerminalPipe _notices = new();
    private readonly CancellationTokenSource _cancel = new();
    private readonly ConnectionExit _exit = new();
    private readonly MemoryStream _pendingInput = new();
    private ITerminalConnection? _inner;
    private (int Columns, int Rows)? _size;
    private bool _ended;
    private bool _sentPending; // under _pendingInput's lock

    /// <param name="connect">Makes the connection; on a pool thread, cancelled by
    /// <see cref="Terminate"/>.</param>
    /// <param name="connecting">Written first, while it connects; null writes nothing.</param>
    /// <param name="describeFailure">What a failure writes; the exception's message by default.
    /// Nothing is written when it was cancelled.</param>
    public DeferredConnection(Func<CancellationToken, Task<ITerminalConnection>> connect, string? connecting = null,
        Func<Exception, string>? describeFailure = null)
    {
        ArgumentNullException.ThrowIfNull(connect);
        Output = new OutputStream(this);
        Input = new InputStream(this);
        if (connecting is not null) Notice(connecting);
        _ = Run(connect, describeFailure ?? (ex => ex.Message));
    }

    /// <summary>The connection, once made; null before, and if it failed.</summary>
    public ITerminalConnection? Connection
    {
        get { lock (_gate) return _inner; }
    }

    /// <summary>Why connecting failed; null while connecting, once connected, or if cancelled.</summary>
    public Exception? Failure { get; private set; }

    public Stream Output { get; }
    public Stream Input { get; }
    public TerminalConnectionTraits Traits => Connection?.Traits ?? TerminalConnectionTraits.None;

    public event Action<int?>? Exited
    {
        add => _exit.Add(value);
        remove => _exit.Remove(value);
    }

    private async Task Run(Func<CancellationToken, Task<ITerminalConnection>> connect, Func<Exception, string> describe)
    {
        ITerminalConnection inner;
        try
        {
            inner = await Task.Run(() => connect(_cancel.Token), _cancel.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            if (!_cancel.IsCancellationRequested)
            {
                Failure = ex;
                Notice(describe(ex));
            }
            End();
            return;
        }

        lock (_gate)
        {
            if (_ended)
            {
                // Terminated while connecting, and the connection came anyway.
                inner.Dispose();
                return;
            }
            _inner = inner;
            if (_size is { } size) inner.Resize(size.Columns, size.Rows);
        }
        inner.Exited += code => _exit.Raise(code);
        SendPendingInput(inner);
        _notices.Complete(); // the reader moves on to the connection's own output
    }

    /// <summary>Sends what was typed while connecting. Under the input's own lock, so nothing
    /// typed now can overtake it.</summary>
    private void SendPendingInput(ITerminalConnection inner)
    {
        lock (_pendingInput)
        {
            try
            {
                if (_pendingInput.Length > 0)
                {
                    inner.Input.Write(_pendingInput.GetBuffer(), 0, (int)_pendingInput.Length);
                    inner.Input.Flush();
                }
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException) { }
            _pendingInput.SetLength(0);
            _sentPending = true;
        }
    }

    private void Notice(string text) =>
        _notices.Write(Encoding.UTF8.GetBytes(text.ReplaceLineEndings("\r\n") + "\r\n"));

    /// <summary>Not connected, and not going to be: the output ends and so does the session.</summary>
    private void End()
    {
        lock (_gate) _ended = true;
        _notices.Complete();
        _exit.Raise(null);
    }

    public void Resize(int columns, int rows)
    {
        ITerminalConnection? inner;
        lock (_gate)
        {
            _size = (columns, rows);
            inner = _inner;
        }
        inner?.Resize(columns, rows);
    }

    public void Terminate()
    {
        _cancel.Cancel();
        ITerminalConnection? inner;
        lock (_gate)
        {
            inner = _inner;
            _ended = true;
        }
        if (inner is not null) inner.Terminate();
        else End();
    }

    public void Dispose()
    {
        Terminate();
        Connection?.Dispose();
    }

    /// <summary>The notices, then the connection's output: the reader switches when the notices
    /// end, so nothing copies between the two and the connection's flow control reaches the
    /// reader.</summary>
    private sealed class OutputStream(DeferredConnection owner) : Stream
    {
        private Stream? _program;

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            if (_program is null)
            {
                int n = owner._notices.Read(buffer);
                if (n > 0) return n;
                // The notices ended: connected, so the program's output follows; or not.
                if (owner.Connection is not { } inner) return 0;
                _program = inner.Output;
            }
            return _program.Read(buffer);
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

    /// <summary>Kept while connecting, then the connection's input.</summary>
    private sealed class InputStream(DeferredConnection owner) : Stream
    {
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            lock (owner._pendingInput)
            {
                if (!owner._sentPending)
                {
                    if (owner._pendingInput.Length + buffer.Length <= MostPendingInput) owner._pendingInput.Write(buffer);
                    return;
                }
            }
            owner.Connection?.Input.Write(buffer);
        }

        public override void Flush()
        {
            bool sent;
            lock (owner._pendingInput) sent = owner._sentPending;
            if (sent) owner.Connection?.Input.Flush();
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
