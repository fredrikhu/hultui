using System.Reflection;
using Aperture;
using Aperture.Core;
using Aperture.Core.Requests;
using Aperture.Core.Testing;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Platform;
using HultUI.Headless;
using Xunit;

namespace Aperture.Tests;

/// <summary>
/// The title bar and the rail are parts, kept while the app rebuilds around them
/// (ApertureApp.Parts.cs), each built only when the value of what it shows moved. A cursor key, a
/// resize, a picture landing, a load that lands build neither. The risk is a value they show
/// changing while they are kept: a rail still lit for the view you left, a badge with an old
/// count, a door to a request server that has gone, the last session's user.
///
/// <para>The oracle is a full build: each check compares the parts on screen with what a forced
/// rebuild of both makes, after each value they show changes the way the app changes it.</para>
/// </summary>
[Collection("ApertureProcessWide")]
public class PartTests
{
    [Fact]
    public void ACursorKey_BuildsTheViewAndNoPart()
    {
        Host((session, app, _) =>
        {
            OpenGrid(session);
            var parts = PartHosts(session);
            int builds = app.Builds, partBuilds = app.PartBuilds;

            foreach (var key in new[] { KeyCode.Right, KeyCode.Right, KeyCode.Down, KeyCode.Left, KeyCode.Up, KeyCode.End, KeyCode.Home })
            {
                session.Window.PressKey(key);
                session.PumpFrames(2);
            }

            // A key whose card is on screen moves the ring and builds nothing (GridCursorTests); End
            // reaches a row not made yet, and builds the view.
            Assert.True(app.Builds - builds >= 1, $"{app.Builds - builds} builds");
            Assert.Equal(0, app.PartBuilds - partBuilds);
            Assert.Equal(parts, PartHosts(session));
            AssertPartsAreAFullBuild(session, app, "the walk");
        });
    }

    /// <summary>A key builds the window, and the kept hosts go back with nothing marked: with a
    /// reader on, no tree is built for them.</summary>
    [Fact]
    public void AResizeAcrossAColumnCount_BuildsTheViewAndNoPart()
    {
        Host((session, app, _) =>
        {
            OpenGrid(session);
            int builds = app.Builds, partBuilds = app.PartBuilds;

            session.Window.SetClientSize(1100, 900);
            session.PumpFrames(2);

            Assert.True(app.Builds > builds);
            Assert.Equal(0, app.PartBuilds - partBuilds);
            AssertPartsAreAFullBuild(session, app, "narrower");
        });
    }

    [Fact]
    public void APictureLanding_BuildsNoPart()
    {
        var artwork = new GateArtwork();
        Host((session, app, _) =>
        {
            OpenGrid(session);
            int builds = app.Builds, partBuilds = app.PartBuilds;

            // The pictures that land together share a build (ApertureApp.PostArtworkLanded), so
            // this waits for all of them rather than for a build each.
            artwork.Gate.SetResult();
            session.PumpUntil(() => app.Builds > builds && app.Posters.PendingCount == 0, TimeSpan.FromSeconds(5));
            for (int i = 0; i < 20; i++) { session.PumpFrame(); Thread.Sleep(2); }

            Assert.Equal(0, app.PartBuilds - partBuilds);
        }, artwork: artwork);
    }

    /// <summary>A part builds when what it shows moved, whichever path built the app, and only
    /// then: a film's page opened builds the rail (its light went out) and not the title bar, and
    /// the loads that land on the page build neither; F5's libraries, a list of their own, build
    /// the rail alone. Until plan 17 round 18 every one of those builds built both parts, 49 KB a
    /// build: a film's page 4 part builds, a library's first open 8.</summary>
    [Fact]
    public void APartBuildsWhenWhatItShowsMoved_AndOnlyThen()
    {
        Host((session, app, _) =>
        {
            OpenGrid(session);
            session.Window.PressKey(KeyCode.Right);
            session.PumpFrames(2);
            var was = Built(app);
            int builds = app.Builds;
            session.Window.PressKey(KeyCode.Enter);
            session.PumpUntil(() => session.Window.FindByAutomationId("detail-play") is not null, TimeSpan.FromSeconds(5));
            session.PumpFrames(3);
            Assert.True(app.Builds - builds >= 2, $"{app.Builds - builds} builds: the page's loads landed in its first");
            Assert.Equal((0, 1), Since(app, was));
            Assert.Equal("", Lit(session));

            was = Built(app);
            typeof(ApertureApp).GetMethod("Rerender", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(app, null);
            session.PumpFrames(3);
            Assert.Equal((0, 0), Since(app, was));

            was = Built(app);
            session.Window.FindByAutomationId("rail-series")!.Click();
            session.PumpFrames(3);
            Assert.Equal((0, 1), Since(app, was));

            app.Refresh();   // the page and the libraries again, through the loaders' signals alone
            was = Built(app);
            builds = app.Builds;
            session.PumpUntil(() => app.Builds > builds, TimeSpan.FromSeconds(5));
            session.PumpFrames(3);
            // The page's loads land in one pass, and the pass's posted work is one batch: one build
            // for all of them (two, five part builds, until plan 17 round 15).
            Assert.Equal(builds + 1, app.Builds);
            Assert.Equal((0, 1), Since(app, was));
            AssertPartsAreAFullBuild(session, app, "after F5");
        });
    }

    /// <summary>Every way into a page lights its door, and the rail is what a full build of it
    /// makes after each: Back, Forward, Escape, the mouse's side buttons, a number key, 0 and /,
    /// a rail button. The rail lit for the view it left is the risk.</summary>
    [Fact]
    public void TheRailFollowsEveryWayIntoAPage()
    {
        Host((session, app, _) =>
        {
            OpenGrid(session);
            void Step(string how, Action act, string lit)
            {
                act();
                session.PumpFrames(3);
                Assert.True(lit == Lit(session), $"{how}: lit {Lit(session)}, not {lit}");
                AssertPartsAreAFullBuild(session, app, how);
            }
            void Side(MouseButton button)
            {
                session.Window.MouseDown(700, 400, button);
                session.Window.MouseUp(700, 400, button);
            }
            Step("a film's page", () =>
            {
                session.Window.PressKey(KeyCode.Right);
                session.PumpFrames(2);
                session.Window.PressKey(KeyCode.Enter);
            }, "");
            Step("Back", () => session.Window.PressKey(KeyCode.Left, KeyModifiers.Alt), "rail-films");
            Step("Forward", () => session.Window.PressKey(KeyCode.Right, KeyModifiers.Alt), "");
            Step("Escape", () => session.Window.PressKey(KeyCode.Escape), "rail-films");
            Step("the mouse's back button", () => Side(MouseButton.Back), "rail-home");
            Step("the mouse's forward button", () => Side(MouseButton.Forward), "rail-films");
            Step("a number key", () => session.Window.PressKey(KeyCode.Digit3), "rail-series");
            Step("0", () => session.Window.PressKey(KeyCode.Digit0), "rail-favourites");
            Step("/", () => session.Window.PressKey(KeyCode.Slash), "rail-search");
            Step("Escape from the search field", () => session.Window.PressKey(KeyCode.Escape), "rail-favourites");
            Step("a rail button", () => session.Window.FindByAutomationId("rail-films")!.Click(), "rail-films");
            Step("the title bar's user", () => session.Window.FindByAutomationId("titlebar-user")!.Click(), "rail-settings");
            Step("1", () => session.Window.PressKey(KeyCode.Digit1), "rail-home");
        });
    }

    /// <summary>Signing out and in as someone else on another server: the title bar names the new
    /// server and user, the rail the new libraries, lit at Home. Kept parts would show the last
    /// session's.</summary>
    [Fact]
    public void SigningInAsSomeoneElse_ReachesBothParts()
    {
        Host((session, app, _) =>
        {
            OpenGrid(session);
            Assert.Equal("Signed in as tester", RailName(session, "titlebar-user"));

            app.SignOut(keepAccount: true);
            session.PumpFrames(3);
            AssertPartsAreAFullBuild(session, app, "signed out");

            var other = new FakeMediaLibrary(
                    new ServerInfo("http://other.test:8096", "Other server", "12.1.0", "other-server-id"),
                    new SignedInUser(Guid.Parse("22222222-2222-2222-2222-222222222222"), "second", "other-token", null))
                .AddLibrary("docs", "Documentaries");
            typeof(ApertureApp).GetMethod("Adopt", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(app, [other, false]);
            session.PumpUntil(() => session.Window.FindByAutomationId("rail-documentaries") is not null, TimeSpan.FromSeconds(5));
            session.PumpFrames(3);

            Assert.Equal("Signed in as second", RailName(session, "titlebar-user"));
            Assert.Contains(Descendants(PartHosts(session)[0]).OfType<TextElement>(), t => t.Content == "Other server");
            Assert.Null(session.Window.FindByAutomationId("rail-films"));
            Assert.Equal("rail-home", Lit(session));
            AssertPartsAreAFullBuild(session, app, "signed in as someone else");
        });
    }

    /// <summary>A library added or renamed on the server reaches the rail with F5.</summary>
    [Fact]
    public void ALibraryAddedOrRenamed_ReachesTheRail()
    {
        Host((session, app, _) =>
        {
            OpenGrid(session);
            var library = (FakeMediaLibrary)app.Library!;
            library.AddLibrary("docs", "Documentaries");
            library.RenameLibrary("shows", "Anime");
            app.Refresh();
            session.PumpUntil(() => session.Window.FindByAutomationId("rail-documentaries") is not null, TimeSpan.FromSeconds(5));
            session.PumpFrames(3);

            Assert.Equal("Anime (3)", RailName(session, "rail-anime"));
            Assert.Equal("Documentaries (4)", RailName(session, "rail-documentaries"));
            Assert.Equal("rail-films", Lit(session));
            AssertPartsAreAFullBuild(session, app, "a library added and one renamed");
        });
    }

    /// <summary>The request server's doors follow the server and its user: an admin's three, gone
    /// with the server, a reporter's own "Your reports" door with the next one.</summary>
    [Fact]
    public void TheRequestDoors_FollowTheServerAndItsUser()
    {
        Host((session, app, _) =>
        {
            OpenGrid(session);
            var adopt = typeof(ApertureApp).GetMethod("AdoptRequestServer", BindingFlags.NonPublic | BindingFlags.Instance)!;
            adopt.Invoke(app, [new FakeRequestServer(null, new RequestUser(1, "tester", RequestPermission.Admin))]);
            session.PumpFrames(3);
            Assert.NotNull(session.Window.FindByAutomationId("rail-issues"));
            AssertPartsAreAFullBuild(session, app, "an admin's server");

            app.DisconnectRequestServer();
            session.PumpFrames(3);
            Assert.Null(session.Window.FindByAutomationId("rail-discover"));
            AssertPartsAreAFullBuild(session, app, "no server");

            adopt.Invoke(app, [new FakeRequestServer(null,
                new RequestUser(1, "tester", RequestPermission.Request | RequestPermission.CreateIssues))]);
            session.PumpFrames(3);
            Assert.NotNull(session.Window.FindByAutomationId("rail-your-reports"));
            Assert.Null(session.Window.FindByAutomationId("rail-issues"));
            AssertPartsAreAFullBuild(session, app, "a reporter's server");
        });
    }

    /// <summary>A view-only path and a full one in one event: one build, and the parts have it.</summary>
    [Fact]
    public void AViewOnlyRebuildAndAFullOneInOneBatch_BuildTheParts()
    {
        Host((session, app, _) =>
        {
            OpenGrid(session);
            int partBuilds = app.PartBuilds;

            HultUI.Core.Reactivity.ReactiveRuntime.Batch(() =>
            {
                typeof(ApertureApp).GetMethod("RerenderView", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(app, null);
                app.Navigate(new Route(ApertureView.Favourites));
            });
            session.PumpFrames(3);

            Assert.Equal("rail-favourites", Lit(session));
            Assert.True(app.PartBuilds > partBuilds);
        });
    }

    /// <summary>The same when the full one is the MarkDirty that runs at once, inside the batch:
    /// that build takes the view-only path's word, and only the hidden MarkDirty says otherwise.</summary>
    [Fact]
    public void AViewOnlyRebuildAndAMarkDirtyInOneBatch_BuildTheParts()
    {
        Host((session, app, _) =>
        {
            OpenGrid(session);
            int partBuilds = app.PartBuilds;

            HultUI.Core.Reactivity.ReactiveRuntime.Batch(() =>
            {
                typeof(ApertureApp).GetMethod("RerenderView", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(app, null);
                typeof(ApertureApp).GetMethod("MarkDirty", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly,
                    Type.EmptyTypes)!.Invoke(app, null);
            });
            session.PumpFrames(3);

            Assert.True(app.PartBuilds - partBuilds >= 2, $"{app.PartBuilds - partBuilds} part builds");
        });
    }

    /// <summary>A toast that comes builds the parts too, as the change it reports may be theirs; one
    /// that goes builds the view alone, as the toast is the window's (plan 17 round 10: Home 148.5 →
    /// 90.1 KB, flash to gone).</summary>
    [Fact]
    public void AToastGoing_BuildsTheViewAndNoPart()
    {
        Host((session, app, _) =>
        {
            OpenGrid(session);
            bool Shown() => Descendants(Root(session)).Any(e => e.Key == "toast");
            int partBuilds = app.PartBuilds;
            app.Flash("Marked as watched");
            session.PumpFrames(2);
            Assert.True(Shown());
            Assert.True(app.PartBuilds - partBuilds >= 2, $"{app.PartBuilds - partBuilds} part builds for a toast that came");

            int builds = app.Builds;
            partBuilds = app.PartBuilds;
            session.PumpFrame(TimeSpan.FromSeconds(2.7));     // the hold ran out; fading
            session.PumpFrame(TimeSpan.FromMilliseconds(300)); // gone
            session.PumpFrames(2);
            Assert.False(Shown());
            Assert.True(app.Builds > builds, "the toast went without a build");
            Assert.Equal(0, app.PartBuilds - partBuilds);
            AssertPartsAreAFullBuild(session, app, "after the toast");
        });
    }

    /// <summary>Every place the rail lights, in and out of each.</summary>
    [Fact]
    public void TheRailFollowsTheView()
    {
        Host((session, app, _) =>
        {
            OpenGrid(session);
            AssertPartsAreAFullBuild(session, app, "the library");
            Assert.Equal("rail-films", Lit(session));

            void Go(Route route, string lit)
            {
                app.Navigate(route);
                session.PumpFrames(3);
                Assert.Equal(lit, Lit(session));
                AssertPartsAreAFullBuild(session, app, route.View.ToString());
            }
            Go(new Route(ApertureView.Favourites), "rail-favourites");
            Go(new Route(ApertureView.Search), "rail-search");
            Go(new Route(ApertureView.Settings), "rail-settings");
            Go(new Route(ApertureView.Discover), "rail-discover");
            Go(new Route(ApertureView.Requests), "rail-requests");
            Go(new Route(ApertureView.Issues), "rail-issues");
            Go(new Route(ApertureView.Detail, "m001", "Film number 001"), "");

            Assert.True(app.GoBack());
            session.PumpFrames(3);
            Assert.Equal("rail-issues", Lit(session));
            AssertPartsAreAFullBuild(session, app, "back");

            session.Window.PressKey(KeyCode.Digit1);
            session.PumpFrames(3);
            Assert.Equal("rail-home", Lit(session));
            AssertPartsAreAFullBuild(session, app, "home");
        }, requests: true);
    }

    /// <summary>The libraries land after the first build, and the rail hears them by itself.</summary>
    [Fact]
    public void TheLibrariesLand_OnTheRail()
    {
        Host((session, app, _) =>
        {
            session.PumpUntil(() => session.Window.FindByAutomationId("rail-films") is not null, TimeSpan.FromSeconds(5));
            AssertPartsAreAFullBuild(session, app, "landed");
        });
    }

    /// <summary>The waiting count lands on the rail, and moves when a request is decided.</summary>
    [Fact]
    public void TheWaitingCount_ShowsOnTheRail()
    {
        Host((session, app, server) =>
        {
            session.PumpUntil(() => RailName(session, "rail-requests")?.Contains("waiting") == true, TimeSpan.FromSeconds(5));
            AssertPartsAreAFullBuild(session, app, "one waiting");
        }, requests: true, setup: s => s!.AddPendingRequest("movie:601"));
    }

    /// <summary>Disconnecting the request server takes its doors off the rail, and the view it left.</summary>
    [Fact]
    public void LosingTheRequestServer_TakesItsDoorsOffTheRail()
    {
        Host((session, app, _) =>
        {
            app.Navigate(new Route(ApertureView.Discover));
            session.PumpFrames(3);
            Assert.NotNull(session.Window.FindByAutomationId("rail-discover"));

            app.DisconnectRequestServer();
            session.PumpFrames(3);

            Assert.Null(session.Window.FindByAutomationId("rail-discover"));
            Assert.Equal("rail-home", Lit(session));
            AssertPartsAreAFullBuild(session, app, "disconnected");
        }, requests: true);
    }

    /// <summary>Signing out: the title bar loses the server and the user, the rail goes.</summary>
    [Fact]
    public void SigningOut_ClearsTheTitleBar_AndTheRailGoes()
    {
        Host((session, app, _) =>
        {
            OpenGrid(session);
            Assert.NotNull(session.Window.FindByAutomationId("titlebar-user"));

            app.SignOut(keepAccount: true);
            session.PumpFrames(3);

            Assert.Equal(ApertureStage.Connect, app.Stage);
            Assert.Null(session.Window.FindByAutomationId("titlebar-user"));
            Assert.Null(session.Window.FindByAutomationId("rail-home"));
            AssertPartsAreAFullBuild(session, app, "signed out");
        });
    }

    /// <summary>Icons are cut for the display scale: a move to another screen builds both.</summary>
    [Fact]
    public void ADisplayScaleChange_BuildsTheParts()
    {
        Host((session, app, _) =>
        {
            OpenGrid(session);
            try
            {
                int partBuilds = app.PartBuilds;
                session.Window.SetScale(1.5f);
                session.PumpFrames(3);
                Assert.True(app.PartBuilds - partBuilds >= 2, $"{app.PartBuilds - partBuilds} part builds");
            }
            finally
            {
                session.Window.SetScale(1f);
                session.PumpFrames(2);
            }
        });
    }

    /// <summary>Full screen takes the title bar off and leaving puts it back as it was, the same
    /// part, up to date with what moved while it was out.</summary>
    [Fact]
    public void FullScreen_TakesTheTitleBarOut_AndBack()
    {
        Host((session, app, _) =>
        {
            OpenGrid(session);
            session.Window.Window.SetFullscreen(true);
            session.Window.SetClientSize(1920, 1080);
            session.PumpFrames(3);
            Assert.Null(session.Window.FindByAutomationId("titlebar-user"));

            session.Window.Window.SetFullscreen(false);
            session.Window.SetClientSize(1440, 900);
            session.PumpFrames(3);
            Assert.NotNull(session.Window.FindByAutomationId("titlebar-user"));
            AssertPartsAreAFullBuild(session, app, "back from full screen");
        });
    }

    // ── The oracle ───────────────────────────────────────────────────────

    private static void AssertPartsAreAFullBuild(HeadlessSession session, ApertureApp app, string when)
    {
        var hosts = PartHosts(session);
        var kept = hosts.Select(WindowFitTests.Sig).ToList();
        int parts = app.PartBuilds;
        typeof(ApertureApp).GetMethod("MarkDirty", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly,
            Type.EmptyTypes)!.Invoke(app, null);
        session.PumpFrames(2);
        Assert.True(app.PartBuilds - parts >= hosts.Count, $"the forced rebuild built {app.PartBuilds - parts} of {hosts.Count} parts");
        var full = PartHosts(session).Select(WindowFitTests.Sig).ToList();
        Assert.Equal(full.Count, kept.Count);
        for (int i = 0; i < kept.Count; i++)
        {
            if (kept[i] == full[i]) continue;
            var xl = kept[i].Split('\n');
            var yl = full[i].Split('\n');
            for (int l = 0; l < Math.Max(xl.Length, yl.Length); l++)
                if (l >= xl.Length || l >= yl.Length || xl[l] != yl[l])
                    throw new Xunit.Sdk.XunitException($"a kept part is not what a full build makes ({when}), line {l}:\n"
                        + $"  kept: {(l < xl.Length ? xl[l] : "<none>")}\n  full: {(l < yl.Length ? yl[l] : "<none>")}");
        }
    }

    /// <summary>The hosts of the parts on screen, in tree order.</summary>
    private static List<ComponentHost> PartHosts(HeadlessSession session)
    {
        var found = new List<ComponentHost>();
        void Walk(Element e)
        {
            if (e is ComponentHost { Component: Part } h) { found.Add(h); return; }
            foreach (var c in e.Children) Walk(c);
        }
        Walk(Root(session));
        return found;
    }

    private static string? RailName(HeadlessSession session, string id) => session.Window.FindByAutomationId(id)?.Element.AccessibleName;

    /// <summary>Builds of the title bar and of the rail so far.</summary>
    private static (int Title, int Rail) Built(ApertureApp app)
    {
        static int Of(ApertureApp app, string field) =>
            (typeof(ApertureApp).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(app) as Part)?.Builds ?? 0;
        return (Of(app, "_titleBarPart"), Of(app, "_railPart"));
    }

    private static (int Title, int Rail) Since(ApertureApp app, (int Title, int Rail) was)
    {
        var now = Built(app);
        return (now.Title - was.Title, now.Rail - was.Rail);
    }

    /// <summary>The rail button lit for where the app is: the only one with an accent background.</summary>
    private static string Lit(HeadlessSession session)
    {
        var rail = PartHosts(session).Last();
        return Descendants(rail).OfType<BoxElement>()
            .FirstOrDefault(b => b.AutomationId?.StartsWith("rail-", StringComparison.Ordinal) == true
                && b.BorderColor != HultUI.Core.Primitives.Color.Transparent)?.AutomationId ?? "";
    }

    // ── Hosting ──────────────────────────────────────────────────────────

    private sealed class NoArtwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
    }

    private sealed class GateArtwork : HttpMessageHandler
    {
        public readonly TaskCompletionSource Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            await Gate.Task.ConfigureAwait(false);
            return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
        }
    }

    private static void OpenGrid(HeadlessSession session)
    {
        session.PumpUntil(() => session.Window.FindByAutomationId("rail-films") is not null, TimeSpan.FromSeconds(10));
        session.Window.FindByAutomationId("rail-films")!.Click();
        session.PumpUntil(() => session.Window.FindByAutomationId("card-m000") is not null, TimeSpan.FromSeconds(10));
        session.PumpFrames(3);
    }

    private static void Host(Action<HeadlessSession, ApertureApp, FakeRequestServer?> body, bool requests = false,
        Action<FakeRequestServer?>? setup = null, HttpMessageHandler? artwork = null)
    {
        var library = new FakeMediaLibrary().AddLibrary("movies", "Films", LibraryKind.Movies)
            .AddLibrary("shows", "Series", LibraryKind.TvShows);
        for (int i = 0; i < 40; i++)
            library.AddMovie($"m{i:000}", $"Film number {i:000}", "movies", 1950 + i % 70, 80 + i % 60);
        FakeRequestServer? server = null;
        if (requests)
        {
            server = new FakeRequestServer(null, new RequestUser(1, "tester", RequestPermission.Admin));
            for (int i = 0; i < 6; i++)
                server.AddMovie(600 + i, $"Discovered film {i}", 1990 + i, shelves: [DiscoverShelf.Trending]);
        }
        setup?.Invoke(server);

        using var session = HeadlessSession.Create(1440, 900);
        var settingsPath = Path.Combine(Path.GetTempPath(), "aperture-tests", Guid.NewGuid().ToString("N"), "settings.json");
        ApertureApp? app = null;
        try
        {
            var settings = new ApertureSettings(new InMemoryCredentialStore(), settingsPath);
            app = new ApertureApp(session.Application, session.Window.Window, settings, library,
                posterHttp: new HttpClient(artwork ?? new NoArtwork()), requests: server);
            session.Application.SetRoot(app.Mount());
            session.PumpFrames(3);
            body(session, app, server);
        }
        finally
        {
            app?.Dispose();
            try { Directory.Delete(Path.GetDirectoryName(settingsPath)!, true); } catch (IOException) { }
        }
    }

    private static IEnumerable<Element> Descendants(Element element)
    {
        yield return element;
        foreach (var child in element.Children)
            foreach (var d in Descendants(child))
                yield return d;
    }

    private static Element Root(HeadlessSession session) =>
        (Element)typeof(HultUI.Window).GetField("_root", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(session.Window.Window)!;
}
