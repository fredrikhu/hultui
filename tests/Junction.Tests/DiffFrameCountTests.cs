using System.Reflection;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Reactivity;
using HultUI.Headless;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// How many frames a step through a commit's diff draws. Plan 17 round 12 found two that drew
/// the window twice: each key of the Commit view's file walk with the pointer resting on the diff
/// (the window hit-tested hover again after the frame that replaced the rows, and asked for a
/// frame for a hover nothing draws: the same pixels again), and each size change of a diff with
/// a sideways scrollbar (the pan read its width after the frame, so that frame drew the old thumb
/// and a second one the new). Frames are counted over sixteen pumps of 16 ms each, so whatever
/// runs after a frame and asks for another is inside the count.
/// </summary>
[Collection("JunctionDiff")]
public class DiffFrameCountTests
{
    [Fact]
    public void AFileSwitchInTheCommitWalk_DrawsOneFrame_WithThePointerOnTheDiff()
    {
        Hosted((session, app) =>
        {
            var body = List(app).Layout.AbsoluteBorderBox;
            session.Window.MoveMouse(body.X + body.Width / 2, body.Y + 60);
            DiffScrollTests.Quiet(session, app);

            for (int key = 0; key < 4; key++)
            {
                var before = List(app).Children.Skip(1).FirstOrDefault();
                session.Window.PressKey(KeyCode.Down);
                Assert.Equal(1, FramesDrawn(session));
                // The walk moved on: the list shows another file's rows, and the row under the
                // pointer has the hover the frame that replaced them took away.
                Assert.NotSame(before, List(app).Children.Skip(1).FirstOrDefault());
                Assert.Contains(List(app).Children, row => row.IsHovered);
            }
        });
    }

    /// <summary>Nothing in the diff draws hover, so the pointer crossing its lines draws nothing.
    /// Every line it reached drew the window again, the same pixels: a sweep down the diff in a
    /// real window drew 42 frames for 52 moves (plan 17 round 13).</summary>
    [Fact]
    public void APointerSweepDownTheDiff_DrawsNoFrame()
    {
        Hosted((session, app) =>
        {
            var body = List(app).Layout.AbsoluteBorderBox;
            float x = body.X + body.Width / 2;
            session.Window.MoveMouse(x, body.Y + 20);
            DiffScrollTests.Quiet(session, app);

            int frames = 0, moves = 0;
            for (float y = body.Y + 28; y < body.Bottom - 20; y += 8, moves++)
            {
                session.Window.MoveMouse(x, y);
                frames += FramesDrawn(session);
            }
            Assert.True(moves > 50, $"the diff is {body.Height} px tall: too short to sweep");
            Assert.Equal(0, frames);
        });
    }

    [Fact]
    public void ASizeChangeWithADiffShown_DrawsOneFrame_WithTheThumbWhereItSettles()
    {
        Hosted((session, app) =>
        {
            var pan = Pan(app);
            Assert.True(pan.CanPan, "nothing to pan: the fixture is not wide");
            // Some of these cross the window's breakpoints and build (1,403, 1,181 and 1,440 px):
            // a build reads the width of the frame before, so those are counted too.
            foreach (int width in new[] { 1403, 1366, 1329, 1292, 1255, 1218, 1181, 1144, 1440 })
            {
                session.Window.SetClientSize(width, 900);
                Assert.Equal(1, FramesDrawn(session));

                // What a build makes of the width the frame laid out is what the frame drew.
                var thumb = Thumb(pan);
                float viewport = pan.ViewportWidth;
                Rebuild(app);
                session.PumpFrames(3);
                Assert.Equal(viewport, pan.ViewportWidth);
                Assert.Equal(thumb, Thumb(pan));
            }
        });
    }

    // ── harness ──────────────────────────────────────────────────────────

    /// <summary>Frames drawn over sixteen pumps of 16 ms.</summary>
    private static int FramesDrawn(HeadlessSession session)
    {
        int frames = 0;
        for (int i = 0; i < 16; i++)
            if (session.PumpFrame(TimeSpan.FromMilliseconds(16))) frames++;
        return frames;
    }

    /// <summary>The Commit view on a commit that changes six files in many places, every line far
    /// too long for the pane. Not a language the app colours: a tokenize landing would be a frame
    /// of its own, and a legitimate one.</summary>
    private static void Hosted(Action<HeadlessSession, JunctionApp> body)
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        static string Line(int file, int i, string tag) =>
            new string(' ', 20) + "a line that runs on far past the right hand edge of the pane " + new string('x', 160)
            + $" {file}.{i} {tag}";
        static string Content(int file, Func<int, string> tag) =>
            string.Join("\n", Enumerable.Range(0, 300).Select(i => Line(file, i, tag(i)))) + "\n";
        for (int f = 0; f < 6; f++) repo.Write($"notes/file{f}.notes", Content(f, _ => "one"));
        repo.Git("add", "-A");
        repo.Git("commit", "-m", "notes");
        for (int f = 0; f < 6; f++) repo.Write($"notes/file{f}.notes", Content(f, i => i % 50 < 4 ? "two" : "one"));
        repo.Git("add", "-A");
        repo.Git("commit", "-m", "change the notes");

        using var session = HeadlessSession.Create(1440, 900);
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
            session.Window.PressKey(KeyCode.Digit2);   // History selects the newest commit
            session.PumpUntil(() => Get<string?>(app, "_selectedCommitHash") != null, TimeSpan.FromSeconds(30));
            session.Window.PressKey(KeyCode.Digit3);   // Commit, on its first file
            session.PumpUntil(() => ListOrNull(app) is { } l && l.Children.Count > 1, TimeSpan.FromSeconds(30));
            DiffScrollTests.Quiet(session, app);

            body(session, app);
        }
        finally { app?.Dispose(); }
    }

    private static T Get<T>(JunctionApp app, string field) =>
        (T)typeof(JunctionApp).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(app)!;

    private static ScrollViewElement? ListOrNull(JunctionApp app) => Get<Ref<Element>>(app, "_diffBodyRef").Current as ScrollViewElement;

    private static ScrollViewElement List(JunctionApp app) =>
        ListOrNull(app) ?? throw new Xunit.Sdk.XunitException("the diff list is not on screen");

    private static HorizontalPan Pan(JunctionApp app) => Get<HorizontalPan>(app, "_diffPan");

    private static HultUI.Core.Primitives.Rect Thumb(HorizontalPan pan) =>
        (pan.ScrollbarElement is { Parent: not null } track ? track : throw new Xunit.Sdk.XunitException("no scrollbar"))
            .Children[1].Layout.AbsoluteBorderBox;

    private static void Rebuild(JunctionApp app)
    {
        for (var t = typeof(JunctionApp); t != null; t = t.BaseType)
            if (t.GetMethod("MarkDirty", BindingFlags.NonPublic | BindingFlags.Instance, Type.EmptyTypes) is { } m)
            { m.Invoke(app, null); return; }
        throw new Xunit.Sdk.XunitException("no MarkDirty to call");
    }
}
