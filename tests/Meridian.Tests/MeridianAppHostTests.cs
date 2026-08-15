using HultUI.Core.Accessibility;
using HultUI.Core.Events;
using HultUI.Headless;
using Xunit;

namespace Meridian.Tests;

/// <summary>
/// The whole app, mounted headless. Assertions go through the accessibility
/// tree — content that cannot be found here cannot be found by assistive
/// technology either (the JunctionAppHostTests discipline).
/// </summary>
public class MeridianAppHostTests
{
    private static void Hosted(Action<HeadlessSession, MeridianApp> body, string? pickerResult = null)
    {
        using var session = HeadlessSession.Create(1440, 900);
        if (pickerResult is not null)
            session.Window.FileDialogs.EnqueueResult(pickerResult);
        MeridianApp? app = null;
        try
        {
            app = new MeridianApp(session.Application, session.Window.Window);
            session.Application.SetRoot(app.Mount());
            session.PumpFrame();
            body(session, app);
        }
        finally
        {
            app?.Dispose();
        }
    }

    private static bool Contains(HeadlessSession session, string text) =>
        Walk(session.Window.AccessibilityTree).Any(n =>
            n.Name?.Contains(text, StringComparison.Ordinal) == true
            || n.Value?.Contains(text, StringComparison.Ordinal) == true);

    private static IEnumerable<AccessibilityNode> Walk(AccessibilityNode node)
    {
        yield return node;
        foreach (var child in node.Children)
            foreach (var descendant in Walk(child))
                yield return descendant;
    }

    [Theory]
    [InlineData(KeyCode.Digit1, MeridianView.Requests)]
    [InlineData(KeyCode.Digit2, MeridianView.Runner)]
    [InlineData(KeyCode.Digit3, MeridianView.Discovery)]
    [InlineData(KeyCode.Digit4, MeridianView.History)]
    [InlineData(KeyCode.Digit5, MeridianView.Environments)]
    public void EveryViewBuildsWithoutThrowing(KeyCode key, MeridianView expected)
    {
        // A view that throws mid-build kills the frame; each is driven through
        // the real key handler and then given frames to settle.
        Hosted((session, app) =>
        {
            session.Window.PressKey(key);
            session.PumpFrames(3);

            Assert.Equal(expected, app.View);
        });
    }

    [Fact]
    public void EveryViewIsReachableByAName()
    {
        Hosted((session, app) =>
        {
            var buttons = Walk(session.Window.AccessibilityTree)
                .Where(n => n.Role == AccessibilityRole.Button)
                .Select(n => n.Name ?? "")
                .ToList();

            foreach (var name in new[] { "Requests", "Runner", "Discovery", "History", "Environments" })
                Assert.Contains(buttons, b => b.StartsWith(name, StringComparison.Ordinal));
        });
    }

    [Fact]
    public void TheFrontDoorShowsAllFourEntryCards()
    {
        Hosted((session, app) =>
        {
            foreach (var card in new[] { "Open a folder", "New collection", "Import OpenAPI", "Discover an API" })
                Assert.True(Contains(session, card), $"\"{card}\" is missing from the front door");
        });
    }

    [Fact]
    public void CardsForUnbuiltFeaturesSaySo()
    {
        // The phantom-shortcut rule: a control that does nothing must not read as
        // live. Unbuilt cards carry "(not available yet)" in their accessible name.
        Hosted((session, app) =>
        {
            // New collection is the last unbuilt card (P11).
            Assert.True(Contains(session, "New collection (not available yet)"));
            // Everything else is live and must NOT be marked unavailable —
            // Import OpenAPI became real in P8.
            foreach (var live in new[] { "Open a folder", "Discover an API", "Import OpenAPI" })
                Assert.False(Contains(session, $"{live} (not available yet)"),
                    $"\"{live}\" is built — it must not read as unavailable");
        });
    }

    [Fact]
    public void TheDiscoverCardGoesToTheDiscoveryView()
    {
        Hosted((session, app) =>
        {
            var card = session.Window.FindByAutomationId("door-discover-an-api");
            Assert.NotNull(card);
            card!.Click();
            session.PumpFrames(2);

            Assert.Equal(MeridianView.Discovery, app.View);
        });
    }

    [Fact]
    public void OpeningAFolderLandsInTheCollectionShell()
    {
        var dir = Directory.CreateTempSubdirectory("meridian-open-").FullName;
        try
        {
            Hosted((session, app) =>
            {
                var card = session.Window.FindByAutomationId("door-open-a-folder");
                Assert.NotNull(card);
                card!.Click();
                // The picker resolves on a worker; wait for the shell to arrive.
                session.PumpUntil(() => Contains(session, "no requests yet"), TimeSpan.FromSeconds(10));

                Assert.Equal(MeridianView.Requests, app.View);
                Assert.NotNull(app.Workspace);
                // The workspace path is pinned in the tree footer — the honesty line.
                Assert.True(Contains(session, app.Workspace!.Name), "the collection's name never appeared");
            }, pickerResult: dir);
        }
        finally
        {
            Directory.Delete(dir);
        }
    }

    [Fact]
    public void ADeclinedPickerLeavesTheFrontDoorUp()
    {
        Hosted((session, app) =>
        {
            var card = session.Window.FindByAutomationId("door-open-a-folder");
            card!.Click();
            session.PumpFrames(5);

            Assert.Null(app.Workspace);
            Assert.True(Contains(session, "Open a folder"), "the front door disappeared");
        }, pickerResult: null);
    }

    [Fact]
    public void CtrlOOpensThePickerToo()
    {
        var dir = Directory.CreateTempSubdirectory("meridian-key-").FullName;
        try
        {
            Hosted((session, app) =>
            {
                session.Window.PressKey(KeyCode.O, KeyModifiers.Ctrl);
                session.PumpUntil(() => app.Workspace != null, TimeSpan.FromSeconds(10));

                Assert.Equal(Path.GetFullPath(dir), app.Workspace!.Root);
            }, pickerResult: dir);
        }
        finally
        {
            Directory.Delete(dir);
        }
    }
}
