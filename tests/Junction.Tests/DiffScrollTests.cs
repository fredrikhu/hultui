using System.Reflection;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Headless;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// Sideways scrolling in the diff, which the list it lives in cannot provide:
/// <c>VirtualListElement.UpdateContentSize</c> pins content width to the viewport, so its
/// own <c>ScrollOffsetX</c> is permanently clamped to zero. The offset is app state applied
/// as a negative margin inside a clipping cell instead — these tests are what say that the
/// arrangement survives virtualization and recycling.
/// </summary>
public class DiffScrollTests
{
    private const string Wide =
        "                    a line indented far enough that its content starts well past the right hand edge of any pane";

    [Fact]
    public void ALongLineCanBeScrolledIntoView()
    {
        Hosted((session, app) =>
        {
            Assert.True(MaxScrollX(app) > 0, "a 110-character line left nothing to scroll");
            Assert.Equal(0f, Offset(app), 1);

            Press(session, app, KeyCode.Right);
            Assert.Equal(28f, Offset(app), 1);

            Press(session, app, KeyCode.Right, KeyModifiers.Ctrl);
            Assert.Equal(MaxScrollX(app), Offset(app), 1);

            Press(session, app, KeyCode.Left, KeyModifiers.Ctrl);
            Assert.Equal(0f, Offset(app), 1);
        });
    }

    [Fact]
    public void TheListsOwnHorizontalOffsetStaysZero()
    {
        // The reason for the negative margin. If this ever stops being true the mechanism
        // could be simplified — and if it silently became false, the two would fight.
        Hosted((session, app) =>
        {
            Press(session, app, KeyCode.Right, KeyModifiers.Ctrl);

            var list = FindList(app);
            Assert.Equal(0f, list.ScrollOffsetX);
            Assert.True(MaxScrollX(app) > 0, "the view believes it has nowhere to scroll");
        });
    }

    [Fact]
    public void ScrollingSurvivesTheRowsBeingRecycled()
    {
        // Rows are realized on demand; scrolling down replaces the elements entirely. The
        // offset has to be re-applied by the template, not held on an element.
        Hosted((session, app) =>
        {
            Press(session, app, KeyCode.Right);
            float shifted = Offset(app);

            var list = FindList(app);
            list.ScrollOffsetY = 400;
            session.PumpFrames(3);
            list.ScrollOffsetY = 0;
            session.PumpFrames(3);

            Assert.Equal(shifted, Offset(app), 1);
        });
    }

    [Fact]
    public void TheLineNumbersDoNotMoveWithTheCode()
    {
        Hosted((session, app) =>
        {
            float numberAtRest = NumberX(app);

            Press(session, app, KeyCode.Right, KeyModifiers.Ctrl);

            Assert.Equal(numberAtRest, NumberX(app), 1);
            Assert.True(Offset(app) > 10, "the code did not move");
        });
    }

    [Fact]
    public void AShortDiffOffersNoScrollbar()
    {
        // Wide by default, so there is a track. Its presence is settled one build after the
        // body has a real width, so give that rebuild a frame.
        Hosted((session, app) =>
        {
            session.PumpFrames(4);
            Assert.True(MaxScrollX(app) > 0, "nothing to scroll — the fixture is not wide");
            Assert.NotNull(TrackOrNull(app));
        });

        Hosted((session, app) =>
        {
            session.PumpFrames(4);
            Assert.Equal(0f, MaxScrollX(app));
            Assert.Null(TrackOrNull(app));
        }, content: "short\n");
    }

    // ── harness ──────────────────────────────────────────────────────────
    private static void Hosted(Action<HeadlessSession, JunctionApp> body, string? content = null)
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        repo.Write("wide.txt", content ?? string.Join("\n", Enumerable.Range(0, 60).Select(i => Wide + " " + i)) + "\n");
        repo.Git("add", "-A");
        repo.Git("commit", "-m", "add a wide file");
        repo.Write("wide.txt", (content ?? string.Join("\n", Enumerable.Range(0, 60).Select(i => Wide + " " + i))) + "\nEDIT\n");

        using var session = HeadlessSession.Create(1400, 900);
        JunctionApp? app = null;
        try
        {
            app = new JunctionApp(session.Application, session.Window.Window, new JunctionSettingsData
            {
                RepositoryPaths = [repo.Path],
                ActiveRepoPath = repo.Path,
                Workspaces = [new WorkspaceData { Id = "w1", Name = "Tests", RepoPaths = [repo.Path] }],
                ActiveWorkspaceId = "w1",
            });
            session.Application.SetRoot(app.Mount());
            session.PumpFrames(3);

            session.Window.PressKey(KeyCode.Digit4);   // Changes
            session.PumpUntil(() => FindListOrNull(app!) is { } l && l.Children.Count > 1,
                TimeSpan.FromSeconds(30));
            session.PumpFrames(4);

            body(session, app);
        }
        finally { app?.Dispose(); }
    }

    private static void Press(HeadlessSession session, JunctionApp app, KeyCode key, KeyModifiers mods = KeyModifiers.None)
    {
        FindList(app).Focus();
        session.Window.PressKey(key, mods);
        session.PumpFrames(2);
    }

    // The list through the app's own Ref: Window.Root is internal, and the Ref is exactly
    // the handle the scrolling code itself uses.
    private static ScrollViewElement FindList(JunctionApp app) =>
        FindListOrNull(app) ?? throw new Xunit.Sdk.XunitException("the diff list is not on screen");

    private static ScrollViewElement? FindListOrNull(JunctionApp app) =>
        ((HultUI.Core.Reactivity.Ref<Element>)typeof(JunctionApp)
            .GetField("_diffBodyRef", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(app)!).Current as ScrollViewElement;

    /// <summary>How far the code has been shifted inside its cell — the mechanism itself,
    /// rather than the text's absolute position. The cell is a flex child among fixed-width
    /// siblings, so its own X can move by a fraction; what must hold exactly is that the
    /// text sits that far left of the cell it is clipped by.</summary>
    private static float Offset(JunctionApp app)
    {
        var cell = FirstCodeRow(app).Children.Last(c => c is BoxElement && c.Children.Count > 0);
        return cell.Layout.AbsoluteContentBox.X - cell.Children[0].Layout.AbsoluteBorderBox.X;
    }

    /// <summary>X of the line number. Not Children[0] — in the staging views that is the
    /// checkbox column, which is empty on a context row.</summary>
    private static float NumberX(JunctionApp app) =>
        FirstCodeRow(app).Children
            .First(c => c.Children.Count > 0 && c.Children[0] is TextElement)
            .Children[0].Layout.AbsoluteBorderBox.X;

    /// <summary>The first row that is a code line rather than a hunk header.</summary>
    private static Element FirstCodeRow(JunctionApp app) =>
        FindList(app).Children.First(c => c.Children.Count >= 4);

    private static float MaxScrollX(JunctionApp app) =>
        (float)typeof(JunctionApp).GetMethod("DiffMaxScrollX", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(app, null)!;

    private static Element? TrackOrNull(JunctionApp app) =>
        ((HultUI.Core.Reactivity.Ref<BoxElement>)typeof(JunctionApp)
            .GetField("_diffHTrackRef", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(app)!).Current is { Parent: not null } track ? track : null;

}
