namespace HultUI.Terminal.Ssh;

/// <summary>Where to connect, how to sign in, and whom to trust. See <see cref="SshConnection.ConnectAsync"/>.</summary>
public sealed record SshConnectOptions
{
    /// <summary>The default <see cref="MaxBufferedBytes"/>: 4 MiB.</summary>
    public const long DefaultMaxBufferedBytes = 4L * 1024 * 1024;

    private readonly int _port = 22;
    private readonly long _maxBuffered = DefaultMaxBufferedBytes;

    /// <summary>The host's name or address, as the known hosts list it.</summary>
    public required string Host { get; init; }

    /// <summary>The port, 22 by default.</summary>
    public int Port
    {
        get => _port;
        init => _port = value is >= 1 and <= 65535 ? value
            : throw new ArgumentOutOfRangeException(nameof(Port), value, "A port is 1 to 65535.");
    }

    /// <summary>The user to sign in as.</summary>
    public required string User { get; init; }

    /// <summary>The ways to sign in, tried in this order among those the server accepts.</summary>
    public IReadOnlyList<SshAuthentication> Authentication { get; init; } = [];

    /// <summary>The host keys already trusted: the user's own <c>~/.ssh/known_hosts</c> by default,
    /// shared with <c>ssh</c>. Null trusts nothing ahead of time, so every key is
    /// <see cref="HostKeyStatus.Unknown"/> and <see cref="VerifyHostKey"/> decides each one.</summary>
    public KnownHostsFile? KnownHosts { get; init; } = new(KnownHostsFile.UserFilePath);

    /// <summary>
    /// Decides on a key the known hosts do not vouch for: <see cref="HostKeyStatus.Unknown"/> or
    /// <see cref="HostKeyStatus.Changed"/>. A known key connects without asking and a revoked one
    /// never does. Called on a background thread, which waits for the decision: show the dialog
    /// on the UI thread. Without it, an unknown or changed key does not connect.
    /// </summary>
    /// <remarks>A changed key is what a man in the middle looks like. Its dialog should make
    /// <see cref="HostKeyDecision.Reject"/> the easy choice and trusting it a deliberate one, with
    /// <see cref="SshHostKey.FingerprintsOnFile"/> beside the new fingerprint.</remarks>
    public Func<SshHostKey, CancellationToken, Task<HostKeyDecision>>? VerifyHostKey { get; init; }

    /// <summary>Receives the server's message before sign-in (OpenSSH's <c>Banner</c>), when it
    /// sends one, on a background thread.</summary>
    public Action<string>? Banner { get; init; }

    /// <summary>The terminal type the remote programs are told (<c>TERM</c>).</summary>
    public string TerminalName { get; init; } = "xterm-256color";

    /// <summary>The grid the shell starts with; the element resizes it to its own.</summary>
    public int Columns { get; init; } = 80;

    /// <inheritdoc cref="Columns"/>
    public int Rows { get; init; } = 24;

    /// <summary>How long connecting and each step of signing in may take, prompts aside.</summary>
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>How often to tell the server the connection is alive, which also finds a dead one.
    /// <see cref="TimeSpan.Zero"/> turns it off.</summary>
    public TimeSpan KeepAlive { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How much output may wait unread before the connection stops reading from the network, so
    /// the server stops sending until the terminal catches up. SSH.NET acknowledges data as it
    /// arrives, so without this a program writing faster than the terminal draws would fill
    /// memory.
    /// </summary>
    public long MaxBufferedBytes
    {
        get => _maxBuffered;
        init => _maxBuffered = value >= 64 * 1024 ? value
            : throw new ArgumentOutOfRangeException(nameof(MaxBufferedBytes), value, "At least 64 KiB.");
    }
}
