using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace VesperCommander.Tests;

/// <summary>
/// <see cref="PathProbe"/> — the primitive that keeps an unreachable host off the UI thread.
///
/// <para>The rule under test is the one that is easy to get backwards: <b>only a timeout means
/// unreachable.</b> A host that answers fast with a refusal is *not* short-circuited, because
/// this class exists to escape the long wait, not to second-guess the operating system about
/// what is out there. Getting that wrong would declare a live server offline for a reason the
/// user could never see.</para>
///
/// <para>The dial is faked (<see cref="PathProbe.ConnectOverride"/>) so the decision is tested
/// rather than the CI runner's routing table. One test does dial for real, and asserts only the
/// property that is actually promised: it comes back inside the budget.</para>
/// </summary>
public class PathProbeTests : IDisposable
{
    public PathProbeTests() => PathProbe.ResetCaches();

    public void Dispose()
    {
        PathProbe.ConnectOverride = null;
        PathProbe.ResetCaches();
    }

    // ── Shape parsing ────────────────────────────────────────────────────

    [Theory]
    [InlineData(@"\\server\share\folder\sub", "server")]
    [InlineData(@"\\server\share", "server")]
    [InlineData(@"\\server", "server")]
    [InlineData("//server/share/x", "server")]          // alt separators reach us from args
    [InlineData(@"\\?\UNC\server\share\x", "server")]   // extended-length UNC
    [InlineData(@"C:\local\path", null)]
    [InlineData(@"\\?\C:\local", null)]                 // extended-length *local*, not a host
    [InlineData(@"\\.\PhysicalDrive0", null)]           // device path, not a host
    [InlineData("relative/path", null)]
    [InlineData("", null)]
    public void HostOf_ReadsTheServerOutOfEveryUncShape(string path, string? expected)
        => Assert.Equal(expected, PathProbe.HostOf(path));

    [Theory]
    [InlineData(@"\\server\share\x", true)]
    [InlineData(@"\\?\UNC\server\share", true)]
    [InlineData(@"C:\Windows", false)]
    [InlineData(@"\\?\C:\Windows", false)]
    [InlineData(@"\\.\PhysicalDrive0", false)]
    [InlineData("sub/folder", false)]
    public void IsRemote_SeparatesSharesFromEverythingThatMerelyLooksLikeOne(string path, bool expected)
        => Assert.Equal(expected, PathProbe.IsRemote(path));

    // ── The refusal-vs-timeout rule ──────────────────────────────────────

    /// <summary>A dial that runs out of time is the one outcome that short-circuits.</summary>
    [Fact]
    public async Task ADialThatTimesOutReportsUnreachable()
    {
        PathProbe.ConnectOverride = (_, _, ct) => Task.Delay(Timeout.Infinite, ct);

        bool reachable = await PathProbe.ReachableAsync(
            @"\\quiet-host\share", TimeSpan.FromMilliseconds(80), CancellationToken.None);

        Assert.False(reachable);
    }

    /// <summary>A refusal is an answer, and a fast one. It must fall through to the filesystem
    /// rather than condemn the host — a share reached some way this probe does not model (an
    /// odd port, a protocol we never dial) is live, and the real call is what knows that.</summary>
    [Theory]
    [InlineData(SocketError.ConnectionRefused)]
    [InlineData(SocketError.HostNotFound)]
    [InlineData(SocketError.NetworkUnreachable)]
    public async Task AFastRefusalFallsThroughInsteadOfCondemningTheHost(SocketError error)
    {
        PathProbe.ConnectOverride = (_, _, _) => Task.FromException(new SocketException((int)error));

        bool reachable = await PathProbe.ReachableAsync(
            @"\\refusing-host\share", TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.True(reachable);
    }

    [Fact]
    public async Task AHostThatAnswersIsReachable()
    {
        PathProbe.ConnectOverride = (_, _, _) => Task.CompletedTask;

        Assert.True(await PathProbe.ReachableAsync(
            @"\\live-host\share", TimeSpan.FromSeconds(5), CancellationToken.None));
    }

    /// <summary>A local path has no host to ask about, so it is reachable without a dial —
    /// the guard that keeps every local navigation off the network entirely.</summary>
    [Fact]
    public async Task ALocalPathIsReachableWithoutDialling()
    {
        int dials = 0;
        PathProbe.ConnectOverride = (_, _, _) => { dials++; return Task.CompletedTask; };

        Assert.True(await PathProbe.ReachableAsync(@"C:\Windows", TimeSpan.FromSeconds(5), CancellationToken.None));
        Assert.Equal(0, dials);
    }

    /// <summary>The verdict is cached per host: one navigation asks more than once, and a share
    /// that just answered has not moved since.</summary>
    [Fact]
    public async Task TheVerdictIsCachedSoOneNavigationDialsOnce()
    {
        int dials = 0;
        PathProbe.ConnectOverride = (_, _, _) => { dials++; return Task.CompletedTask; };

        for (int i = 0; i < 4; i++)
            await PathProbe.ReachableAsync($@"\\cached-host\share{i}", TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.Equal(1, dials);
    }

    // ── Kind ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task AnUnreachableHostIsReportedWithoutStattingThePath()
    {
        PathProbe.ConnectOverride = (_, _, ct) => Task.Delay(Timeout.Infinite, ct);

        var kind = await PathProbe.KindAsync(
            @"\\quiet-host\share\folder", TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.Equal(PathKind.Unreachable, kind);
    }

    [Fact]
    public async Task AReachableHostFallsThroughToTheRealAnswer()
    {
        PathProbe.ConnectOverride = (_, _, _) => Task.CompletedTask;
        using var tmp = new TempTree();

        // Not a share, but it is what the filesystem says about a path whose host answered.
        Assert.Equal(PathKind.Missing, await PathProbe.KindAsync(
            Path.Combine(tmp.Root, "nope"), TimeSpan.FromSeconds(5), CancellationToken.None));
    }

    [Fact]
    public async Task CancellingTheCallerThrowsRatherThanReportingAVerdict()
    {
        PathProbe.ConnectOverride = (_, _, ct) => Task.Delay(Timeout.Infinite, ct);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            PathProbe.KindAsync(@"\\quiet-host\share", TimeSpan.FromSeconds(5), cts.Token));
    }

    [Fact]
    public void LocalKind_TellsDirectoryFromFileFromNothing()
    {
        using var tmp = new TempTree();
        Assert.Equal(PathKind.Directory, PathProbe.LocalKind(tmp.Root));
        Assert.Equal(PathKind.File, PathProbe.LocalKind(tmp.File));
        Assert.Equal(PathKind.Missing, PathProbe.LocalKind(Path.Combine(tmp.Root, "no-such-thing")));
    }

    // ── The promise, against a real socket ───────────────────────────────

    /// <summary>The one test that dials for real. It asserts the property the class actually
    /// promises — an answer inside the budget — and not *which* answer, because that depends on
    /// whether the runner's network drops the packet or refuses it, and both are correct. The
    /// measurement this replaces: <c>Directory.Exists</c> on the same address took 21 seconds.
    /// </summary>
    [Fact]
    public async Task ARealDialComesBackInsideItsBudget()
    {
        // TEST-NET-1 (RFC 5737): reserved for documentation, so nothing should ever answer.
        var sw = Stopwatch.StartNew();
        await PathProbe.ReachableAsync(@"\\192.0.2.1\share", TimeSpan.FromMilliseconds(400), CancellationToken.None);
        sw.Stop();

        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5),
            $"the dial took {sw.ElapsedMilliseconds} ms against a 400 ms budget");
    }

    private sealed class TempTree : IDisposable
    {
        public readonly string Root, File;

        public TempTree()
        {
            Root = Path.Combine(Path.GetTempPath(), "vesper-probe-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            File = Path.Combine(Root, "file.txt");
            System.IO.File.WriteAllText(File, "x");
        }

        public void Dispose()
        {
            try { Directory.Delete(Root, true); } catch { /* best-effort temp cleanup */ }
        }
    }
}
