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
[Collection("JunctionDiff")]
public class DiffScrollTests
{
    private const string Wide =
        "                    a line indented far enough that its content starts well past the right hand edge of any pane "
        + "and then continues for a good while longer still, so that no plausible pane width could ever contain all of it";

    [Fact]
    public void ALongLineCanBeScrolledIntoView()
    {
        Hosted((session, app) =>
        {
            Assert.True(MaxScrollX(app) > 0,
                $"nothing to scroll: content={Pan(app).ContentWidth:0} viewport={Pan(app).ViewportWidth:0}");
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

    /// <summary>
    /// The pan's viewport is the code column as it was laid out, not as a build guessed it. The
    /// first build runs before any layout and guesses the window, about twice the column, and a
    /// window resize lays out again without a build: the sideways wheel stopped hundreds of pixels
    /// short of the end of the longest line (plan 17 round 10 step 13: 361 of 959 at the start)
    /// and the thumb kept the size it was built with. At the start and after each resize the wheel
    /// takes the end of the line to the column's edge (less the label's padding), and the thumb is
    /// the column's share of the line.
    /// </summary>
    [Fact]
    public void TheWheelReachesTheEndOfTheLongestLine_AtTheStartAndAfterResizes()
    {
        Hosted((session, app) =>
        {
            AssertTheEndIsReached(session, app, "at the start");
            foreach (int width in new[] { 1396, 1392, 1200, 1000, 1100, 1400 })
            {
                WheelSideways(session, app, 4000f);   // back to the left margin
                Quiet(session, app);   // the loads behind the other tabs build when they land
                int builds = app.Builds;
                session.Window.SetClientSize(width, 900);
                session.PumpFrames(3);
                // A few pixels cross no breakpoint, and the pan follows them with no build.
                if (width is 1396 or 1392) Assert.Equal(builds, app.Builds);
                AssertTheEndIsReached(session, app, $"at {width} px");
            }
        });
    }

    /// <summary>Frames until twenty in a row build nothing, for at most five seconds.</summary>
    internal static void Quiet(HeadlessSession session, JunctionApp app)
    {
        var until = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        for (int quiet = 0, last = app.Builds; quiet < 20 && DateTime.UtcNow < until;)
        {
            session.PumpFrame();
            Thread.Sleep(5);
            quiet = app.Builds == last ? quiet + 1 : 0;
            last = app.Builds;
        }
    }

    /// <summary>The thumb as it is on screen, then one turn of the wheel far to the right: it
    /// takes the end of the longest line to the column's edge, and the thumb to the track's.
    /// One turn, since each builds the rows at the offset it reached, and the build reads the
    /// width the frame before was laid out at: a second turn went on from where the first
    /// stopped short.
    ///
    /// <para>The app reckons the code column from the list's width less its fixed columns, and
    /// with the staging boxes that is 2.5 px narrower than the column the layout gives: the line
    /// goes that much further than the padding. So the end must be in view and at most
    /// <see cref="Slack"/> short of the edge, and the thumb within it.</para></summary>
    private static void AssertTheEndIsReached(HeadlessSession session, JunctionApp app, string when)
    {
        AssertTheThumb(app, when + ", at the left margin", atEnd: false);
        WheelSideways(session, app, -4000f, turns: 1);
        var cell = FirstCodeRow(app).Children.Last(c => c is BoxElement && c.Children.Count > 0);
        var label = cell.Children[0];
        var column = cell.Layout.AbsoluteContentBox;
        float textEnd = label.Layout.AbsoluteContentBox.X + Pan(app).ContentWidth;
        float shortOfEdge = column.X + column.Width - PadRight(app) - textEnd;
        Assert.True(shortOfEdge >= -0.5f && shortOfEdge <= Slack,
            $"{when}: the longest line ends at {textEnd:0.#} after the wheel, the column at {column.X + column.Width:0.#} "
            + $"(less {PadRight(app):0.#} of padding); offset {Pan(app).Offset:0.#} of {Pan(app).MaxOffset:0.#}, "
            + $"viewport {Pan(app).ViewportWidth:0.#} for a column of {column.Width:0.#}");
        AssertTheThumb(app, when + ", at the end", atEnd: true);
    }

    private const float Slack = 4f;

    /// <summary>The thumb is the column's share of the longest line (the column less the
    /// label's padding, against the line), at the start or the end of the track.</summary>
    private static void AssertTheThumb(JunctionApp app, string when, bool atEnd)
    {
        var cell = FirstCodeRow(app).Children.Last(c => c is BoxElement && c.Children.Count > 0);
        var track = TrackOrNull(app) ?? throw new Xunit.Sdk.XunitException($"{when}: no scrollbar");
        var bar = track.Layout.AbsoluteContentBox;
        var thumb = track.Children[1].Layout.AbsoluteBorderBox;
        float share = Math.Min(1f, (cell.Layout.AbsoluteContentBox.Width - PadRight(app)) / Pan(app).ContentWidth);
        Assert.True(Math.Abs(Math.Max(30f, bar.Width * share) - thumb.Width) <= Slack,
            $"{when}: the thumb is {thumb.Width:0.#} px of a {bar.Width:0.#} px track, for a column showing {share:P1} of the line "
            + $"(column {cell.Layout.AbsoluteContentBox.Width:0.##}, line {Pan(app).ContentWidth:0.##}; the pan's viewport {Pan(app).ViewportWidth:0.##})");
        float gap = atEnd ? bar.X + bar.Width - (thumb.X + thumb.Width) : thumb.X - bar.X;
        Assert.True(Math.Abs(gap) <= 1f, $"{when}: the thumb is {gap:0.#} px from that end of the track");
    }

    /// <summary>The code label's padding on its right: what the column keeps clear of text.</summary>
    private static float PadRight(JunctionApp app)
    {
        var label = FirstCodeRow(app).Children.Last(c => c is BoxElement && c.Children.Count > 0).Children[0];
        return label.Layout.AbsoluteBorderBox.X + label.Layout.AbsoluteBorderBox.Width
            - (label.Layout.AbsoluteContentBox.X + label.Layout.AbsoluteContentBox.Width);
    }

    private static void WheelSideways(HeadlessSession session, JunctionApp app, float dx, int turns = 4)
    {
        var list = FindList(app).Layout.AbsoluteBorderBox;
        for (int i = 0; i < turns; i++)
        {
            session.Window.Wheel(list.X + list.Width / 2, list.Y + 40, dx, 0);
            session.PumpFrames(2);
        }
    }

    // ── harness ──────────────────────────────────────────────────────────
    internal static void Hosted(Action<HeadlessSession, JunctionApp> body, string? content = null)
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

    private static HultUI.Core.Builders.HorizontalPan Pan(JunctionApp app) =>
        (HultUI.Core.Builders.HorizontalPan)typeof(JunctionApp)
            .GetField("_diffPan", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(app)!;

    private static float MaxScrollX(JunctionApp app) => Pan(app).MaxOffset;

    private static Element? TrackOrNull(JunctionApp app) =>
        Pan(app).ScrollbarElement is { Parent: not null } track ? track : null;

}

/// <summary>
/// Serializes the diff test classes. Each hosts a real app in its own headless session, and
/// some process-wide state is shared between sessions — the ambient text-measure context
/// above all, which the newest window installs. Run in parallel they measure and lay out
/// against each other's windows, which shows up as a hit test missing by a pixel.
/// </summary>
[CollectionDefinition("JunctionDiff", DisableParallelization = true)]
public class JunctionDiffCollection { }
