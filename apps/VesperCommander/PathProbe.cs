using System.Net.Sockets;

namespace VesperCommander;

/// <summary>What a path turned out to be. <see cref="Unreachable"/> and <see cref="TimedOut"/>
/// are network answers: the host never replied, so whether anything is actually there is
/// still unknown.</summary>
public enum PathKind { Directory, File, Missing, Unreachable, TimedOut }

/// <summary>
/// Asking the filesystem about a path without betting the UI thread on the answer.
///
/// <para><b>Why this exists.</b> <c>Directory.Exists</c> on an unreachable UNC host is a
/// synchronous network call with no timeout of ours: measured at 1.2 s for a hostname that
/// fails DNS and <b>21 s</b> for an IP that silently drops the SYN. Called from a key press
/// — which is what Ctrl+G did — that is 21 s of frozen window.</para>
///
/// <para><b>The trick.</b> A filesystem call cannot be cancelled; a socket connect can. So a
/// remote path is first asked the one question we are allowed to give up on — "does anything
/// answer on SMB?" — under a budget we control. The same unroutable address that costs
/// <c>Directory.Exists</c> 21 s is refused here in 2.0 s.</para>
///
/// <para><b>Only a timeout short-circuits.</b> A fast <see cref="SocketException"/> — refused,
/// unreachable, no such host — falls through and lets the real filesystem call answer. We are
/// buying our way out of the long wait, not second-guessing the OS: a live server reached some
/// way this probe does not model must never be declared offline by it.</para>
///
/// <para><b>What a timeout does not do.</b> A timed-out stat <i>abandons</i> its thread; it
/// cannot kill it. The worker stays parked in the syscall until the OS gives up and then throws
/// its answer away. This bounds the caller's wait, not the syscall — which is exactly why
/// <see cref="ReachableAsync"/> runs first and why results are cached instead of re-dialled.</para>
/// </summary>
public static class PathProbe
{
    /// <summary>Microsoft-DS. Modern Windows shares answer here; the probe is a reachability
    /// test, not a protocol negotiation, so the older NetBIOS port is not worth a second dial
    /// — a host that answers only on 139 fails this check fast and falls through.</summary>
    private const int SmbPort = 445;

    /// <summary>How long the reachability dial gets. Long enough to cross a slow VPN, short
    /// enough that a dead host is not what the user remembers about the app.</summary>
    public static readonly TimeSpan ReachBudget = TimeSpan.FromSeconds(2);

    // ── Remote classification ────────────────────────────────────────────

    /// <summary>True when reaching <paramref name="path"/> means going over the network — a
    /// UNC path, or a drive letter mapped to a share. Everything else answers in microseconds
    /// and needs none of the machinery here.</summary>
    public static bool IsRemote(string? path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        var p = path.Replace('/', '\\');

        // "\\?\" and "\\.\" are the extended-length and device prefixes, not shares. Only the
        // "\\?\UNC\server\share" form under them is remote; "\\?\C:\x" is the local disk.
        if (p.StartsWith(@"\\?\", StringComparison.Ordinal) || p.StartsWith(@"\\.\", StringComparison.Ordinal))
            return p.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase);

        if (p.StartsWith(@"\\", StringComparison.Ordinal)) return true;
        return p.Length >= 2 && p[1] == ':' && char.IsAsciiLetter(p[0]) && IsNetworkDrive(p[0]);
    }

    /// <summary>The server name in a UNC path, or null when it is not one. Handles the bare
    /// <c>\\server</c> and the extended <c>\\?\UNC\server\share</c> forms as well as the
    /// ordinary <c>\\server\share\…</c>.</summary>
    internal static string? HostOf(string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        var p = path.Replace('/', '\\');
        if (p.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) p = @"\\" + p[8..];
        else if (!p.StartsWith(@"\\", StringComparison.Ordinal)
                 || p.StartsWith(@"\\?\", StringComparison.Ordinal)
                 || p.StartsWith(@"\\.\", StringComparison.Ordinal)) return null;

        var rest = p[2..];
        int slash = rest.IndexOf('\\');
        var host = slash < 0 ? rest : rest[..slash];
        return host.Length == 0 ? null : host;
    }

    /// <summary>The server name when <paramref name="path"/> is a bare <c>\\server</c> and
    /// nothing under it, else null. This is the one UNC shape the filesystem API cannot answer
    /// for — a server is not a directory, the share is — so it is listed through
    /// <see cref="NetworkShares"/> instead of read.</summary>
    public static string? ServerRootOf(string? path)
    {
        if (HostOf(path) is not { } host) return null;
        var p = path!.Replace('/', '\\');
        if (p.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) p = @"\\" + p[8..];
        // Nothing but the host: "\\server", or "\\server\" with the separator and no share.
        return p.TrimEnd('\\').Length == host.Length + 2 ? host : null;
    }

    /// <summary>The parent of a UNC path that <see cref="Directory.GetParent"/> has no answer
    /// for: a share root's parent is its server. Null for anything else, including a server
    /// root, which is the top of the tree.</summary>
    public static string? UncParentOf(string? path)
    {
        if (string.IsNullOrEmpty(path) || ServerRootOf(path) != null) return null;
        if (HostOf(path) is not { } host) return null;
        var p = path.Replace('/', '\\').TrimEnd('\\');
        // Only a share root climbs to the server; deeper paths have a real parent directory.
        int afterHost = 2 + host.Length;
        return p.Length > afterHost && p.IndexOf('\\', afterHost + 1) < 0 ? @"\\" + host : null;
    }

    // Drive letter → "is a mapped network drive". Rebuilt on a TTL so a drive mapped while
    // the app is running is picked up, and cached because IsRemote is asked per navigation.
    private static readonly object DriveLock = new();
    private static Dictionary<char, bool>? _driveKinds;
    private static long _driveKindsExpire;
    private const long DriveTtlMs = 10_000;

    private static bool IsNetworkDrive(char letter)
    {
        lock (DriveLock)
        {
            long now = Environment.TickCount64;
            if (_driveKinds is null || now >= _driveKindsExpire)
            {
                var map = new Dictionary<char, bool>();
                try
                {
                    foreach (var d in DriveInfo.GetDrives())
                    {
                        var root = d.RootDirectory.FullName;
                        if (root.Length >= 2 && root[1] == ':' && char.IsAsciiLetter(root[0]))
                            map[char.ToUpperInvariant(root[0])] = SafeDriveType(d) == DriveType.Network;
                    }
                }
                catch { /* volume list unavailable — treat everything as local */ }
                _driveKinds = map;
                _driveKindsExpire = now + DriveTtlMs;
            }
            return _driveKinds.TryGetValue(char.ToUpperInvariant(letter), out bool network) && network;
        }
    }

    private static DriveType SafeDriveType(DriveInfo d)
    {
        try { return d.DriveType; }
        catch { return DriveType.Unknown; }
    }

    // ── Reachability ─────────────────────────────────────────────────────

    // Per-host answers, briefly. One navigation asks more than once (the goto probe, then
    // whatever follows it), and a share that just answered has not moved since.
    private static readonly object HostLock = new();
    private static readonly Dictionary<string, (bool Reachable, long Expires)> HostCache =
        new(StringComparer.OrdinalIgnoreCase);
    private const long ReachableTtlMs = 5_000;
    private const long UnreachableTtlMs = 3_000;

    /// <summary>Whether the host behind <paramref name="path"/> answers on SMB inside
    /// <paramref name="budget"/>. A path with no host to ask about is reachable by definition.
    /// False means only one thing: the dial ran out of time. Every other outcome — including
    /// an outright refusal — answers true and leaves the verdict to the filesystem.</summary>
    public static async Task<bool> ReachableAsync(string path, TimeSpan budget, CancellationToken ct)
    {
        if (HostOf(path) is not { } host) return true;

        lock (HostLock)
        {
            if (HostCache.TryGetValue(host, out var hit) && Environment.TickCount64 < hit.Expires)
                return hit.Reachable;
        }

        bool reachable = await DialAsync(host, budget, ct).ConfigureAwait(false);

        lock (HostLock)
        {
            HostCache[host] = (reachable,
                Environment.TickCount64 + (reachable ? ReachableTtlMs : UnreachableTtlMs));
        }
        return reachable;
    }

    /// <summary>Stands in for the socket connect so the refusal-vs-timeout rule can be tested
    /// without a network — the decision it guards is the whole point of this class, and a test
    /// that had to find a genuinely unroutable address would only be as reliable as the CI
    /// runner's routing table. Null in every shipped path.</summary>
    internal static Func<string, int, CancellationToken, Task>? ConnectOverride;

    private static async Task<bool> DialAsync(string host, TimeSpan budget, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(budget);
        try
        {
            if (ConnectOverride is { } connect)
                await connect(host, SmbPort, deadline.Token).ConfigureAwait(false);
            else
            {
                using var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
                await socket.ConnectAsync(host, SmbPort, deadline.Token).ConfigureAwait(false);
            }
            return true;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return false;     // the budget ran out — the one case that short-circuits
        }
        catch (SocketException)
        {
            return true;      // answered fast, just not the way we hoped — let the FS decide
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return true;      // anything unmodelled is not evidence of an unreachable host
        }
    }

    // ── Kind ─────────────────────────────────────────────────────────────

    /// <summary>What <paramref name="path"/> is, without blocking the caller past
    /// <paramref name="budget"/>. A remote path is dialled first, so an unreachable host costs
    /// <see cref="ReachBudget"/> rather than the operating system's own patience.</summary>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> fired.</exception>
    public static async Task<PathKind> KindAsync(string path, TimeSpan budget, CancellationToken ct)
    {
        if (IsRemote(path) && !await ReachableAsync(path, ReachBudget, ct).ConfigureAwait(false))
            return PathKind.Unreachable;

        // A reachable server browses like a folder even though no filesystem call agrees:
        // Directory.Exists(@"\\server") is false because a server is not a directory. Asking
        // the disk here would report Missing and refuse a path that is perfectly good.
        if (ServerRootOf(path) != null) return PathKind.Directory;

        // Not cancellable, and deliberately not handed `ct`: a token there would only stop it
        // from starting. Once running it owns its thread until the OS answers.
        var stat = Task.Run(() => LocalKind(path), CancellationToken.None);
        if (await Task.WhenAny(stat, Task.Delay(budget, ct)).ConfigureAwait(false) == stat)
            return await stat.ConfigureAwait(false);

        ct.ThrowIfCancellationRequested();
        return PathKind.TimedOut;
    }

    /// <summary>The same question asked straight out, for a path already known to be local.
    /// Microseconds on a real disk; never call it on a remote path from the UI thread.</summary>
    public static PathKind LocalKind(string path)
    {
        try
        {
            return Directory.Exists(path) ? PathKind.Directory
                 : File.Exists(path) ? PathKind.File
                 : PathKind.Missing;
        }
        catch { return PathKind.Missing; }
    }

    /// <summary>Drop both caches — for tests, which must not inherit a verdict from the
    /// test that ran before them.</summary>
    internal static void ResetCaches()
    {
        lock (HostLock) HostCache.Clear();
        lock (DriveLock) { _driveKinds = null; _driveKindsExpire = 0; }
    }
}
