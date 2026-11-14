using System.Reflection;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
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

    /// <summary>A wrapped diff measures its rows' heights in the build, at the list's width, and a
    /// build reads the width of the frame before. A size change built twice and drew twice, the
    /// chrome with it: the first frame drew the rows at the old width's heights and the check after
    /// it built at the new one (plan 17 round 14). The resize hands the build the width it leads
    /// to and builds only the view; the widths here cross no breakpoint.</summary>
    [Fact]
    public void AWrappedDiffsSizeChange_DrawsOneFrame_AndBuildsTheViewAlone()
    {
        Hosted((session, app) =>
        {
            WrapOn(session, app);
            session.Window.SetClientSize(1380, 900);   // under the compact chrome's 1,406: crossed here, not below
            DiffScrollTests.Quiet(session, app);
            foreach (int width in new[] { 1376, 1372, 1368, 1364, 1300, 1240, 1376 })
            {
                int builds = app.Builds, chrome = app.ChromeBuilds;
                session.Window.SetClientSize(width, 900);
                Assert.Equal(1, FramesDrawn(session));
                Assert.Equal((1, 0), (app.Builds - builds, app.ChromeBuilds - chrome));
                AssertMeasuredAtTheLaidOutWidth(app, $"at {width} px");
            }
        });
    }

    /// <summary>A height alone leaves every wrapped row as tall as it was: nothing builds.</summary>
    [Fact]
    public void AWrappedDiffsHeightChange_BuildsNothing()
    {
        Hosted((session, app) =>
        {
            WrapOn(session, app);
            foreach (int height in new[] { 896, 892, 860, 900 })
            {
                int builds = app.Builds, chrome = app.ChromeBuilds;
                session.Window.SetClientSize(1440, height);
                Assert.Equal(1, FramesDrawn(session));
                Assert.Equal((0, 0), (app.Builds - builds, app.ChromeBuilds - chrome));
                AssertMeasuredAtTheLaidOutWidth(app, $"at a height of {height} px");
            }
        });
    }

    /// <summary>A size change across a breakpoint still builds the chrome, which shows it, and the
    /// wrapped rows are measured at the width the frame lays out.</summary>
    [Fact]
    public void AWrappedDiffsSizeChangeAcrossABreakpoint_StillBuildsTheChrome()
    {
        Hosted((session, app) =>
        {
            WrapOn(session, app);
            foreach (int width in new[] { 1380, 1440, 1380, 1440 })   // across the compact chrome's 1,406
            {
                int chrome = app.ChromeBuilds;
                session.Window.SetClientSize(width, 900);
                Assert.Equal(1, FramesDrawn(session));
                Assert.True(app.ChromeBuilds - chrome >= 3, $"at {width} px: {app.ChromeBuilds - chrome} chrome part builds");
                AssertMeasuredAtTheLaidOutWidth(app, $"at {width} px");
            }
        });
    }

    /// <summary>A drag of the file list's splitter beside a wrapped diff builds the view for the
    /// rows' heights. That build read the width of the frame before, so each step built twice and
    /// drew twice, the second time the chrome too, the same pixels as the first (plan 17 round 14).
    /// The step hands the build the width the list gave up.</summary>
    [Fact]
    public void AFileListDragBesideAWrappedDiff_DrawsOneFrameAStep_AndBuildsTheViewAlone()
    {
        Hosted((session, app) =>
        {
            WrapOn(session, app);
            var files = Get<Ref<ScrollViewElement>>(app, "_fileScrollRef").Current!;
            float edge = files.Layout.AbsoluteBorderBox.Right;
            var handle = Descendants(RootOf(session)).OfType<SplitterHandleElement>()
                .Where(h => h.Layout.AbsoluteBorderBox.Height > 0)
                .MinBy(h => MathF.Abs(h.Layout.AbsoluteBorderBox.X - edge))!;
            var box = handle.ScreenBorderBox;
            float x = box.X + box.Width / 2, y = box.Y + box.Height / 2;
            session.Window.MoveMouse(x, y);
            session.Window.MouseDown(x, y);
            DiffScrollTests.Quiet(session, app);
            foreach (var step in new[] { -4f, -4f, -40f, 60f, 4f })
            {
                int builds = app.Builds, chrome = app.ChromeBuilds;
                float width = files.Layout.Width;
                x += step;
                session.Window.MoveMouse(x, y);
                Assert.Equal(1, FramesDrawn(session));
                Assert.Equal(width + step, Get<Ref<ScrollViewElement>>(app, "_fileScrollRef").Current!.Layout.Width, 0.5f);
                Assert.Equal((1, 0), (app.Builds - builds, app.ChromeBuilds - chrome));
                AssertMeasuredAtTheLaidOutWidth(app, $"a step of {step} px");
                files = Get<Ref<ScrollViewElement>>(app, "_fileScrollRef").Current!;
            }
            session.Window.MouseUp(x, y);
            DiffScrollTests.Quiet(session, app);
        });
    }

    /// <summary>The rows a size change or a file list drag makes again lay their wrapped lines out
    /// at the code width, never at zero. A zero flex basis had the layout measure each new label at
    /// zero width before it grew, a word to a line, the most expensive layout a line has; the
    /// text pipeline's layout cache kept those only while a build measured few enough rows, and a
    /// Changes diff of 400 wrapped rows laid them out again every step, 534 → 709 KB a drag step
    /// (plan 17 round 14). Counted at the layout engine's own measure context.</summary>
    [Fact]
    public void AWrappedDiffsNewRows_AreNeverLaidOutAtZeroWidth()
    {
        Hosted((session, app) =>
        {
            WrapOn(session, app);
            var spy = MeasureSpy.Install(session);
            foreach (int width in new[] { 1380, 1376, 1300 })
            {
                session.Window.SetClientSize(width, 900);
                FramesDrawn(session);
            }
            var files = Get<Ref<ScrollViewElement>>(app, "_fileScrollRef").Current!;
            float edge = files.Layout.AbsoluteBorderBox.Right;
            var handle = Descendants(RootOf(session)).OfType<SplitterHandleElement>()
                .Where(h => h.Layout.AbsoluteBorderBox.Height > 0)
                .MinBy(h => MathF.Abs(h.Layout.AbsoluteBorderBox.X - edge))!;
            var box = handle.ScreenBorderBox;
            float x = box.X + box.Width / 2, y = box.Y + box.Height / 2;
            session.Window.MoveMouse(x, y);
            session.Window.MouseDown(x, y);
            FramesDrawn(session);
            session.Window.MoveMouse(x - 8, y);
            FramesDrawn(session);
            session.Window.MouseUp(x - 8, y);
            FramesDrawn(session);

            Assert.True(spy.LongLines > 20, $"{spy.LongLines} long lines measured: the steps made no rows");
            Assert.True(spy.LongLinesAtZero == 0, $"{spy.LongLinesAtZero} of {spy.LongLines} long lines laid out at zero width");
        });
    }

    // ── harness ──────────────────────────────────────────────────────────

    /// <summary>Counts what the window's layout engine measures, by wrapping its measure context: lines
    /// longer than 100 characters, and those of them laid out at no width.</summary>
    private sealed class MeasureSpy(IMeasureContext inner) : IMeasureContext
    {
        public int LongLines, LongLinesAtZero;

        public static MeasureSpy Install(HeadlessSession session)
        {
            var engine = typeof(HultUI.Window).GetField("_layoutEngine", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(session.Window.Window)!;
            var field = engine.GetType().GetField("_measureContext", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var spy = new MeasureSpy((IMeasureContext)field.GetValue(engine)!);
            field.SetValue(engine, spy);
            return spy;
        }

        private void Note(string text, float maxWidth)
        {
            if (text.Length <= 100) return;
            LongLines++;
            if (maxWidth <= 0f) LongLinesAtZero++;
        }

        public Size MeasureText(string text, ResolvedInheritedStyle style, float maxWidth, int? maxLines,
            TextOverflow overflow = TextOverflow.Clip)
        {
            Note(text, maxWidth);
            return inner.MeasureText(text, style, maxWidth, maxLines, overflow);
        }

        public Size MeasureText(string text, ResolvedInheritedStyle style, float maxWidth, int? maxLines,
            TextOverflow overflow, IReadOnlyList<TextSpan>? spans)
        {
            Note(text, maxWidth);
            return inner.MeasureText(text, style, maxWidth, maxLines, overflow, spans);
        }

        public Size MeasureText(string text, ResolvedInheritedStyle style, float maxWidth, int? maxLines,
            TextOverflow overflow, IReadOnlyList<TextSpan>? spans, out float sameFromWidth)
        {
            Note(text, maxWidth);
            return inner.MeasureText(text, style, maxWidth, maxLines, overflow, spans, out sameFromWidth);
        }

        public float MinContentTextWidth(string text, ResolvedInheritedStyle style, IReadOnlyList<TextSpan>? spans = null) =>
            inner.MinContentTextWidth(text, style, spans);

        public FontMetrics MeasureFont(ResolvedInheritedStyle style) => inner.MeasureFont(style);

        public bool HasGlyph(ResolvedInheritedStyle style, int codePoint) => inner.HasGlyph(style, codePoint);

        public float MeasureFirstBaseline(string text, ResolvedInheritedStyle style, float maxWidth, int? maxLines,
            TextOverflow overflow = TextOverflow.Clip, IReadOnlyList<TextSpan>? spans = null) =>
            inner.MeasureFirstBaseline(text, style, maxWidth, maxLines, overflow, spans);
    }

    /// <summary>Wrap on, from the diff's own key, and settled.</summary>
    private static void WrapOn(HeadlessSession session, JunctionApp app)
    {
        List(app).Focus();
        session.Window.PressKey(KeyCode.W);
        DiffScrollTests.Quiet(session, app);
        Assert.True(Get<bool>(app, "_diffWrap"), "w did not wrap the diff");
        AssertMeasuredAtTheLaidOutWidth(app, "after wrapping");
    }

    /// <summary>The wrapped rows on screen were measured at the code width the list is laid out at.</summary>
    private static void AssertMeasuredAtTheLaidOutWidth(JunctionApp app, string when)
    {
        float laidOut = (float)typeof(JunctionApp).GetMethod("CodeWidthFor", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(app, [List(app).Layout.AbsoluteContentBox.Width])!;
        var shape = Get<(bool Wrap, bool Split, float Width, int Rows, int Review)>(app, "_diffHeightShape");
        Assert.True(shape.Wrap, $"{when}: the rows were not measured wrapped");
        Assert.True(MathF.Abs(laidOut - shape.Width) <= 0.5f, $"{when}: rows measured at {shape.Width:0.##}, laid out at {laidOut:0.##}");
    }

    private static Element RootOf(HeadlessSession session) =>
        (Element)typeof(HultUI.Window).GetField("_root", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(session.Window.Window)!;

    private static IEnumerable<Element> Descendants(Element element)
    {
        yield return element;
        foreach (var child in element.Children)
            foreach (var d in Descendants(child))
                yield return d;
    }

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
