using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using HultUI.Headless;
using Xunit;

namespace VesperCommander.Tests;

/// <summary>
/// Browsing a bare <c>\\server</c> — the one UNC shape no filesystem call can answer for.
/// <c>Directory.Exists(@"\\server")</c> is false and enumerating it throws, because a server is
/// not a directory; the share below it is the first thing that is. So it is listed through the
/// network provider instead, and the rest of the app has to agree that it is a place.
///
/// <para>The provider is faked (<see cref="NetworkShares.EnumerateOverride"/>) and so is the
/// reachability dial, so these test Vesper's handling of every outcome rather than whether the
/// machine running them happens to have a file server.</para>
/// </summary>
public class ServerBrowseTests : IDisposable
{
    public ServerBrowseTests()
    {
        PathProbe.ResetCaches();
        // Reachable, without dialling: ListShares asks before it enumerates.
        PathProbe.ConnectOverride = (_, _, _) => Task.CompletedTask;
    }

    public void Dispose()
    {
        NetworkShares.EnumerateOverride = null;
        FileSystem.EnumerateOverride = null;
        PathProbe.ConnectOverride = null;
        PathProbe.ResetCaches();
    }

    // ── Which paths name a server ────────────────────────────────────────

    [Theory]
    [InlineData(@"\\server", "server")]
    [InlineData(@"\\server\", "server")]
    [InlineData("//server", "server")]
    [InlineData(@"\\?\UNC\server", "server")]
    [InlineData(@"\\server\share", null)]        // a share is a directory; read it normally
    [InlineData(@"\\server\share\sub", null)]
    [InlineData(@"C:\Windows", null)]
    [InlineData("", null)]
    public void ServerRootOf_NamesOnlyTheServerItself(string path, string? expected)
        => Assert.Equal(expected, PathProbe.ServerRootOf(path));

    /// <summary>A share root's parent is its server. Directory.GetParent answers null there —
    /// it stops at the path root — which is what left a share with no way out but the go-to
    /// prompt.</summary>
    [Theory]
    [InlineData(@"\\server\share", @"\\server")]
    [InlineData(@"\\server\share\", @"\\server")]
    [InlineData(@"\\server\share\sub", null)]    // a real parent directory; not our business
    [InlineData(@"\\server", null)]              // the top of the tree
    [InlineData(@"C:\Windows", null)]
    public void UncParentOf_ClimbsFromAShareToItsServer(string path, string? expected)
        => Assert.Equal(expected, PathProbe.UncParentOf(path));

    /// <summary>What the above is standing in for. (A trailing separator is a different matter —
    /// <c>GetParent(@"\\server\share\")</c> does answer, with the share itself, which is why the
    /// listing trims before it asks.)</summary>
    [Fact]
    public void DirectoryGetParent_HasNoAnswerForAShareRoot()
        => Assert.Null(Directory.GetParent(@"\\server\share"));

    [Fact]
    public async Task AReachableServerBrowsesLikeAFolder()
    {
        var kind = await PathProbe.KindAsync(@"\\server", TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.Equal(PathKind.Directory, kind);   // though Directory.Exists says otherwise
        Assert.False(Directory.Exists(@"\\server"));
    }

    // ── The listing ──────────────────────────────────────────────────────

    [Fact]
    public void SharesListAsFoldersUnderTheServer()
    {
        Shares(ShareListStatus.Ok, "docs", "media", "backup");

        var listing = FileSystem.List(@"\\server");

        Assert.Equal(PaneState.Ok, listing.State);
        Assert.Equal(["backup", "docs", "media"], listing.Entries.Select(e => e.Name));
        Assert.All(listing.Entries, e => Assert.True(e.IsDir));
        Assert.Equal(@"\\server\docs", listing.Entries.Single(e => e.Name == "docs").FullPath);
    }

    /// <summary>No ".." — a server is the top of its tree, and an entry claiming otherwise would
    /// navigate to something that is not a path.</summary>
    [Fact]
    public void AServerHasNoParentEntry()
    {
        Shares(ShareListStatus.Ok, "docs");

        Assert.DoesNotContain(FileSystem.List(@"\\server").Entries, e => e.IsParent);
    }

    /// <summary>A share whose folder is below it still gets its way back up, which is the other
    /// half of the same navigation.</summary>
    [Fact]
    public void AShareRootGetsAParentEntryPointingAtTheServer()
    {
        // The share itself is read as a directory; an empty sequence keeps it off the network.
        FileSystem.EnumerateOverride = _ => [];

        var listing = FileSystem.List(@"\\server\docs");

        var parent = Assert.Single(listing.Entries, e => e.IsParent);
        Assert.Equal(@"\\server", parent.FullPath);
    }

    [Fact]
    public void AdminSharesFollowTheHiddenFilesToggle()
    {
        Shares(ShareListStatus.Ok, "docs", "C$", "ADMIN$");

        Assert.Equal(["docs"], FileSystem.List(@"\\server").Entries.Select(e => e.Name));
        Assert.Equal(["ADMIN$", "C$", "docs"],
            FileSystem.List(@"\\server", showHidden: true).Entries.Select(e => e.Name));
    }

    [Theory]
    [InlineData(ShareListStatus.Empty, PaneState.Empty)]
    [InlineData(ShareListStatus.Denied, PaneState.Denied)]
    [InlineData(ShareListStatus.Offline, PaneState.Offline)]
    [InlineData(ShareListStatus.Unsupported, PaneState.Offline)]
    public void EveryProviderOutcomeReachesThePaneAsItsOwnState(ShareListStatus status, PaneState expected)
    {
        Shares(status);

        Assert.Equal(expected, FileSystem.List(@"\\server").State);
    }

    /// <summary>A server that never answers costs the probe's budget, not the operating system's
    /// patience. The enumeration is a network call with no timeout of ours, so it is not the
    /// thing allowed to decide how long this takes.</summary>
    [Fact]
    public void AnUnreachableServerIsOfflineWithoutEnumerating()
    {
        PathProbe.ConnectOverride = (_, _, _) => Task.FromException(new OperationCanceledException());
        bool enumerated = false;
        NetworkShares.EnumerateOverride = _ => { enumerated = true; return new(ShareListStatus.Ok, ["docs"]); };

        Assert.Equal(PaneState.Offline, FileSystem.List(@"\\server").State);
        Assert.False(enumerated);
    }

    [Fact]
    public void ACancelledListingStopsBeforeTheProvider()
    {
        bool enumerated = false;
        NetworkShares.EnumerateOverride = _ => { enumerated = true; return new(ShareListStatus.Ok, ["docs"]); };
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Same(DirListing.Loading, FileSystem.List(@"\\server", ct: cts.Token));
        Assert.False(enumerated);
    }

    // ── Navigation ───────────────────────────────────────────────────────

    /// <summary>Backspace out of a share reaches its server, with the cursor on the share just
    /// left — what every other ascent does. Before this it stopped dead: "\\server\share" is a
    /// path root, so the "already at the top" test claimed it was.</summary>
    [Fact]
    public void BackspaceOutOfAShareReachesTheServer()
    {
        Shares(ShareListStatus.Ok, "docs", "media");
        FileSystem.EnumerateOverride = _ => [];
        using var fx = new Fixture();

        fx.Invoke("NavigateTo", 'L', @"\\server\docs", null, false);
        fx.Invoke("GoUp", 'L');

        Assert.Equal(@"\\server", fx.Dir('L'));
        Assert.Equal("docs", fx.PendingFocusChild('L'));
    }

    // ── The trail ────────────────────────────────────────────────────────

    /// <summary>The leading crumb is the <i>server</i>, with the share as a step of its own.
    /// <c>Path.GetPathRoot</c> answers "\\server\share" because that is the volume, and the trail
    /// used to take it at its word — so the share was welded into the root and could not be
    /// clicked, even from a folder inside it.</summary>
    [Fact]
    public void TheTrailRootsAtTheServerWithTheShareAsItsOwnCrumb()
    {
        using var fx = new Fixture();
        fx.Go(@"\\server\share\src\core");

        Assert.Equal([@"\\server", "share", "src", "core"], fx.Crumbs());
    }

    [Fact]
    public void AShareRootShowsServerThenShare()
    {
        using var fx = new Fixture();
        fx.Go(@"\\server\share");

        Assert.Equal([@"\\server", "share"], fx.Crumbs());
    }

    [Fact]
    public void AServerIsTheWholeTrail()
    {
        using var fx = new Fixture();
        fx.Go(@"\\server");

        Assert.Equal([@"\\server"], fx.Crumbs());
    }

    /// <summary>A local path is untouched: its leading crumb is still the volume, because for a
    /// drive letter the volume and the place are the same thing.</summary>
    [Fact]
    public void ALocalTrailStillRootsAtItsDrive()
    {
        using var fx = new Fixture();
        fx.Go(@"C:\Windows\System32");

        Assert.Equal([@"C:", "Windows", "System32"], fx.Crumbs());
    }

    /// <summary>Which share you are on is not a folder among folders. A trail too long to fit
    /// drops middle folders behind an ellipsis, and the share has to survive that — otherwise
    /// every deep path reads "\\server / … / core" and the share is gone.</summary>
    [Fact]
    public void TheShareSurvivesTruncationOfADeepTrail()
    {
        using var fx = new Fixture(width: 900);
        fx.Go(@"\\server\share\a-rather-long-folder-name\another-long-one\and-a-third\and-fourth\leaf");

        var crumbs = fx.Crumbs();
        Assert.Contains("…", crumbs);                 // it did truncate…
        Assert.Equal(@"\\server", crumbs[0]);         // …and kept both pinned crumbs
        Assert.Equal("share", crumbs[1]);
        Assert.Equal("leaf", crumbs[^1]);
    }

    /// <summary>Clicking the server crumb goes there. It cannot open the drive picker the way a
    /// drive letter does — that list is built from mounted volumes and never holds a server.</summary>
    [Fact]
    public void ClickingTheServerCrumbNavigatesRatherThanOpeningThePicker()
    {
        Shares(ShareListStatus.Ok, "share");
        using var fx = new Fixture();
        fx.Go(@"\\server\share\src");

        fx.ClickCrumb(@"\\server");

        Assert.Equal(@"\\server", fx.Dir('L'));
        Assert.Null(fx.DrivePicker);
    }

    private static void Shares(ShareListStatus status, params string[] names)
        => NetworkShares.EnumerateOverride = _ => new(status, names);

    /// <summary>A hosted CommanderApp on a temp folder, settled.</summary>
    private sealed class Fixture : IDisposable
    {
        private readonly string _root;
        private readonly HeadlessSession _session;
        private readonly CommanderApp _app;

        public Fixture(int width = 1240)
        {
            _root = Path.Combine(Path.GetTempPath(), "vesper-srv-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            _session = HeadlessSession.Create(width, 820);
            _app = new CommanderApp(_session.Application, _session.Window.Window, _root);
            _session.Application.SetRoot(_app.Mount());
            Invoke("NavigateTo", 'L', _root, null, false);
            Invoke("NavigateTo", 'R', _root, null, false);
            _session.PumpUntil(() => _app.ListingsReady);
        }

        public string Dir(char side) => (string)Method("Dir").Invoke(_app, [side])!;

        public char? DrivePicker => (char?)typeof(CommanderApp)
            .GetField("_drivePicker", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(_app);

        /// <summary>Put the left pane somewhere without reading it — the trail is drawn from the
        /// path, and an empty listing keeps a made-up server off the network.</summary>
        public void Go(string dir)
        {
            FileSystem.EnumerateOverride = _ => [];
            Invoke("NavigateTo", 'L', dir, null, false);
            _session.PumpFrame();
        }

        /// <summary>The trail's crumbs, left to right, as rendered — read off the accessibility
        /// tree, so the assertion is about what a reader is actually told.</summary>
        public string[] Crumbs()
        {
            var strip = _session.Window.FindByAutomationId("crumb-strip-L");
            Assert.NotNull(strip);
            return [.. strip!.Node.Children
                .Where(c => c.Role == HultUI.Core.Accessibility.AccessibilityRole.Button)
                .Select(c => c.Name ?? "")];
        }

        public void ClickCrumb(string label)
        {
            var crumb = _session.Window.Find(n => n.Name == label
                && n.Role == HultUI.Core.Accessibility.AccessibilityRole.Button);
            Assert.NotNull(crumb);
            crumb!.Click();
            _session.PumpFrame();
        }

        public string? PendingFocusChild(char side)
            => ((string?[])typeof(CommanderApp)
                .GetField("_pendingFocusChild", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(_app)!)[side == 'L' ? 0 : 1];

        public void Invoke(string method, params object?[] args) => Method(method).Invoke(_app, args);

        private static MethodInfo Method(string name)
            => typeof(CommanderApp).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!;

        public void Dispose()
        {
            (_app as IDisposable)?.Dispose();
            _session.Dispose();
            try { Directory.Delete(_root, true); } catch { /* best-effort temp cleanup */ }
        }
    }
}
