using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Xunit;

namespace VesperCommander.Tests;

/// <summary>
/// <see cref="FileSystem.List"/> honouring the token that says nobody is waiting for it any
/// more — the same between-entries check <c>FindEngine.Walk</c> and <c>SyncEngine</c> have
/// always had, which the pane's own listing never got.
///
/// <para>The shape of the answer matters as much as the fact of it: a cancelled walk
/// <b>returns a sentinel and does not throw</b>. <c>AsyncLoader</c> treats a throwing producer
/// as a failure and retries it three times with a widening backoff, so throwing here would make
/// every navigation away from a slow folder relaunch the read it had just abandoned.</para>
/// </summary>
public class FileSystemCancelTests : IDisposable
{
    private readonly string _dir;

    public FileSystemCancelTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "vesper-cancel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        for (int i = 0; i < 12; i++) File.WriteAllText(Path.Combine(_dir, $"f{i}.txt"), "x");
        Directory.CreateDirectory(Path.Combine(_dir, "sub"));
    }

    public void Dispose()
    {
        FileSystem.EnumerateOverride = null;
        try { Directory.Delete(_dir, true); } catch { /* best-effort temp cleanup */ }
    }

    /// <summary>The check that matters for a long walk: cancelled after it has begun, it stops
    /// where it is rather than finishing a folder nobody is waiting for. The sequence is the
    /// test's own, because "partway through" is not a moment a test can otherwise name — with a
    /// token cancelled up front, the check before the walk answers first and this one is never
    /// the reason for the result.</summary>
    [Fact]
    public void AWalkCancelledPartwayThroughStopsThere()
    {
        using var cts = new CancellationTokenSource();
        int yielded = 0;
        FileSystem.EnumerateOverride = _ => Entries(cts, () => yielded++);

        var listing = FileSystem.List(_dir, ct: cts.Token);

        Assert.Same(DirListing.Loading, listing);
        Assert.Equal(2, yielded);   // one before the cancel, one after — then it gave up
    }

    /// <summary>Twelve real entries, cancelling the walk once the second has been handed over.
    /// </summary>
    private IEnumerable<FileSystemInfo> Entries(CancellationTokenSource cts, Action counted)
    {
        int n = 0;
        foreach (var info in new DirectoryInfo(_dir).EnumerateFileSystemInfos())
        {
            counted();
            yield return info;
            if (++n == 1) cts.Cancel();
        }
    }

    /// <summary>The control: without a token it lists the folder as it always did. Everything
    /// below is only meaningful against this.</summary>
    [Fact]
    public void WithoutCancellationTheFolderListsAsBefore()
    {
        var listing = FileSystem.List(_dir);

        Assert.Equal(PaneState.Ok, listing.State);
        Assert.Equal(14, listing.Entries.Count);   // "..", 12 files, sub
    }

    [Fact]
    public void ACancelledTokenReturnsTheSentinelInsteadOfThrowing()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var listing = FileSystem.List(_dir, ct: cts.Token);   // must not throw

        Assert.Same(DirListing.Loading, listing);
    }

    /// <summary>Nothing is read, either — the walk stops before the first entry rather than
    /// collecting a partial folder and presenting it as the answer.</summary>
    [Fact]
    public void ACancelledWalkCollectsNothing()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var listing = FileSystem.List(_dir, ct: cts.Token);

        Assert.Empty(listing.Entries);
    }

    /// <summary>An <i>empty</i> folder pins the check that happens before the walk starts. With
    /// entries to iterate, the between-entries check answers on the first one and the earlier
    /// check is never the reason for the result — so a control that removed it passed. Here the
    /// loop body never runs, and without the entry check this comes back
    /// <see cref="PaneState.Empty"/>: a real answer about a folder nobody is waiting for.</summary>
    [Fact]
    public void AnEmptyFolderIsStillCancelledBeforeTheWalk()
    {
        var empty = Path.Combine(_dir, "empty");
        Directory.CreateDirectory(empty);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var listing = FileSystem.List(empty, ct: cts.Token);

        Assert.Same(DirListing.Loading, listing);
        Assert.NotEqual(PaneState.Empty, listing.State);
    }

    /// <summary>The sentinel is the loading placeholder, whose state is Ok with no rows — which
    /// is only safe because the loader's generation check drops a superseded result before it
    /// can be shown. If that ever changes, a cancelled listing would render as an empty folder.
    /// </summary>
    [Fact]
    public void TheSentinelIsTheLoadingPlaceholder()
    {
        Assert.Equal(PaneState.Ok, DirListing.Loading.State);
        Assert.Empty(DirListing.Loading.Entries);
    }
}
