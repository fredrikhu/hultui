using Aperture;
using Aperture.Core;
using Aperture.Core.Testing;
using HultUI.Core.Accessibility;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Platform;
using HultUI.Headless;
using Xunit;

namespace Aperture.Tests;

/// <summary>
/// The library grid is a virtual list whose rows of posters answer for themselves
/// (<c>RowsAreComposite</c>): each card is a button of its own, an element only once its row is
/// realized, which drawing does after the frame's accessibility phase. A reader got no card on
/// opening a library until something else built a tree. The tree asks the list for its rows now.
/// </summary>
[Collection("ApertureProcessWide")]
public class LibraryReaderTests
{
    private sealed class NoArtwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
    }

    private static void Host(Action<HeadlessSession, HeadlessAccessibilityBridge> body)
    {
        var library = new FakeMediaLibrary().AddLibrary("movies", "Films", LibraryKind.Movies);
        for (int i = 0; i < 60; i++)
            library.AddMovie($"m{i:000}", $"Film number {i:000}", "movies", 1950 + i % 70, 80 + i % 60);

        using var session = HeadlessSession.Create(1440, 900);
        var bridge = session.Window.InstallAccessibilityBridge();
        var settingsPath = Path.Combine(Path.GetTempPath(), "aperture-tests", Guid.NewGuid().ToString("N"), "settings.json");
        ApertureApp? app = null;
        try
        {
            var settings = new ApertureSettings(new InMemoryCredentialStore(), settingsPath);
            app = new ApertureApp(session.Application, session.Window.Window, settings, library,
                posterHttp: new HttpClient(new NoArtwork()));
            session.Application.SetRoot(app.Mount());
            session.PumpUntil(() => session.Window.FindByAutomationId("rail-films") is not null, TimeSpan.FromSeconds(10));
            session.Window.FindByAutomationId("rail-films")!.Click();
            session.PumpUntil(() => session.Window.FindByAutomationId("card-m000") is not null, TimeSpan.FromSeconds(10));
            body(session, bridge);
        }
        finally
        {
            app?.Dispose();
            try { Directory.Delete(Path.GetDirectoryName(settingsPath)!, true); } catch (IOException) { }
        }
    }

    /// <summary>The clock past whatever refresh is coming, as a reader at rest sees it.</summary>
    private static void Rest(HeadlessSession session)
    {
        for (int i = 0; i < 3; i++) session.PumpFrame(TimeSpan.FromMilliseconds(200));
    }

    private static bool Has(AccessibilityNode? node, string automationId)
    {
        if (node is null) return false;
        if (node.AutomationId == automationId) return true;
        foreach (var child in node.Children)
            if (Has(child, automationId)) return true;
        return false;
    }

    private static void AssertFresh(HeadlessSession session, HeadlessAccessibilityBridge bridge)
    {
        var diff = AccessibilityTreeDiffer.Diff(bridge.CurrentTree, session.Window.AccessibilityTree);
        Assert.True(diff.IsEmpty, $"stale: +{diff.Added.Count} -{diff.Removed.Count} "
            + string.Join(", ", diff.PropertyChanged.Select(c => $"{c.PropertyName} of {c.Node.Role} '{c.Node.Name}'")));
    }

    [Fact]
    public void TheCards_ReachAReader_OnOpeningTheLibrary() => Host((session, bridge) =>
    {
        Rest(session);

        Assert.True(Has(bridge.CurrentTree, "card-m000"), "no card in the tree");
        AssertFresh(session, bridge);
    });

    [Fact]
    public void AMoveOfTheCursor_ReachesAReader() => Host((session, bridge) =>
    {
        Rest(session);
        session.Window.PressKey(KeyCode.Right);
        session.PumpFrame();
        session.Window.PressKey(KeyCode.Right);
        session.PumpFrame();
        Rest(session);

        AssertFresh(session, bridge);
    });

    [Fact]
    public void AWheelTurn_LeavesTheTreeWithTheCardsOnScreen() => Host((session, bridge) =>
    {
        Rest(session);
        for (int i = 0; i < 12; i++)
        {
            session.Window.Wheel(700, 400, 0, -1);
            session.PumpFrame(TimeSpan.FromMilliseconds(16));
        }
        Rest(session);

        AssertFresh(session, bridge);
    });
}
