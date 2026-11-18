using System.Reflection;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using HultUI.Headless;
using Xunit;

namespace VesperCommander.Tests;

/// <summary>
/// A size change with the wrapped viewer (Shift+F3) or diff (F3) open draws once, with every
/// row as tall as its text at the width that frame lays the body out at, and builds nothing:
/// the rows on screen are kept and take their new heights from the list.
///
/// <para>A wrapped row's height is measured at the body's width and handed to the virtualized
/// body. It used to be measured in a build, which ran before the frame's layout and read the
/// width the body had a frame ago: the frame drew every row at the old width's height, and the
/// check after it (SettleWrappedRowWidths) built and drew a second frame at the new one. The
/// resize now leads the width by what the window gained or lost (LeadWrappedBodyWidth), and a
/// check corrects a lead that was wrong — the wrong-lead test makes one on purpose. Plan 17
/// round 15 took the build out: a wrapped row reads neither the window nor the width, so the
/// resize measures the heights itself and hands them to the list (FitWrappedRowsInPlace). The
/// build made every row on screen again and patched the whole window, ~290 KB a 4 px step of
/// the reader over a 1,000-line file, and 64 KB a height-only step that changed nothing. Round
/// 16 moved the check into the body's layout hook (WrappedBodyLaidOut), before the frame is
/// drawn, which is also where a view's first open measures its rows.</para>
/// </summary>
public class WrapResizeFrameTests
{
    // Shrinks and grows, a few pixels and a few hundred, from the fixture's 1,240 px.
    private static readonly int[] Widths = [1236, 1232, 1228, 1180, 1260, 1300, 940, 1240];

    [Fact]
    public void ASizeChangeWithTheWrappedViewerOpen_DrawsOnce_KeepsItsRows_AndBuildsNothing()
    {
        using var t = new DiffViewTests.Fixture();
        t.WriteLeft("a.txt", LongLines(80));
        t.Open(wrap: true);
        OpenViewer(t, "a.txt");
        Settle(t);

        foreach (int width in Widths)
        {
            int builds = Builds(t);
            var before = RowsOnScreen(t, "_viewerScrollRef");
            t.Session.Window.SetClientSize(width, 820);
            Assert.Equal(1, FramesDrawn(t.Session));
            Assert.Equal(0, Builds(t) - builds);
            AssertKept(before, RowsOnScreen(t, "_viewerScrollRef"), $"at {width} px");
            AssertKeptRowsDrawAsMadeRows(t, "_viewerScrollRef", $"at {width} px");
            AssertMeasuredAtTheLaidOutWidth(t, "ViewerCodeWidth", "_viewerHeightsWidth", "_viewerTops", width);
        }
    }

    [Fact]
    public void ASizeChangeWithTheWrappedDiffOpen_DrawsOnce_KeepsItsRows_AndBuildsNothing()
    {
        using var t = new DiffViewTests.Fixture();
        t.WriteLeft("a.txt", LongLines(80));
        t.WriteRight("a.txt", LongLines(80).Replace("word1 ", "WORD1 "));
        t.Open(wrap: true);
        t.OpenDiffOn("a.txt");
        Assert.Equal("Ok", t.DiffState);
        Settle(t);

        foreach (int width in Widths)
        {
            int builds = Builds(t);
            var before = RowsOnScreen(t, "_diffScrollRef");
            t.Session.Window.SetClientSize(width, 820);
            Assert.Equal(1, FramesDrawn(t.Session));
            Assert.Equal(0, Builds(t) - builds);
            AssertKept(before, RowsOnScreen(t, "_diffScrollRef"), $"at {width} px");
            AssertKeptRowsDrawAsMadeRows(t, "_diffScrollRef", $"at {width} px");
            AssertMeasuredAtTheLaidOutWidth(t, "DiffCodeWidth", "_diffHeightsWidth", "_diffTops", width);
        }
    }

    /// <summary>A height alone leaves every wrapped row as tall as it was: nothing is measured and
    /// nothing builds, in the reader and in the diff.</summary>
    [Fact]
    public void AHeightChangeWithAWrappedViewOpen_MeasuresAndBuildsNothing()
    {
        using var t = new DiffViewTests.Fixture();
        t.WriteLeft("a.txt", LongLines(80));
        t.WriteRight("a.txt", LongLines(80).Replace("word1 ", "WORD1 "));
        t.Open(wrap: true);
        OpenViewer(t, "a.txt");
        Settle(t);
        HeightSteps(t, "_viewerTops");

        t.Press(KeyCode.Escape);
        t.Session.PumpFrames(3);
        t.OpenDiffOn("a.txt");
        Assert.Equal("Ok", t.DiffState);
        Settle(t);
        HeightSteps(t, "_diffTops");
    }

    /// <summary>
    /// The lead is the body's last laid-out width plus what the window gained or lost since the
    /// body's layout hook read it. Here the hook is told the window was 60 px wider then than it
    /// was, so the lead comes out 60 px short, as it would wherever the body did not take all of
    /// the window's change: the resize measures the rows at the wrong width, and the hook
    /// measures them again at the width laid out, in the same frame, before it is drawn. Until
    /// plan 17 round 16 that frame drew the rows at the wrong heights, and a check after it built
    /// and drew a second frame.
    /// </summary>
    [Fact]
    public void AWrongLead_IsCorrectedInTheFrameItLaysOut()
    {
        using var t = new DiffViewTests.Fixture();
        t.WriteLeft("a.txt", LongLines(80));
        t.Open(wrap: true);
        OpenViewer(t, "a.txt");
        Settle(t);

        var field = typeof(CommanderApp).GetField("_wrapLaidOutWindowWidth", BindingFlags.NonPublic | BindingFlags.Instance)!;
        float read = (float)field.GetValue(t.App)!;
        Assert.Equal(1236f, read);
        field.SetValue(t.App, read + 60f);

        int builds = Builds(t);
        var before = RowsOnScreen(t, "_viewerScrollRef");
        t.Session.Window.SetClientSize(1200, 820);
        Assert.Equal(1, FramesDrawn(t.Session));
        Assert.Equal(0, Builds(t) - builds);
        AssertKept(before, RowsOnScreen(t, "_viewerScrollRef"), "after the corrected lead");
        AssertMeasuredAtTheLaidOutWidth(t, "ViewerCodeWidth", "_viewerHeightsWidth", "_viewerTops", 1200);

        // And the next size change leads from the width the check read again.
        builds = Builds(t);
        t.Session.Window.SetClientSize(1180, 820);
        Assert.Equal(1, FramesDrawn(t.Session));
        Assert.Equal(0, Builds(t) - builds);
        AssertMeasuredAtTheLaidOutWidth(t, "ViewerCodeWidth", "_viewerHeightsWidth", "_viewerTops", 1180);
    }

    /// <summary>
    /// A wrapped view's first open builds for its text once and draws its rows at their heights
    /// from the first frame that shows them: every line is measured once, at the code width the
    /// body is laid out at, by the body's layout hook before that frame is drawn. A check after
    /// the frame used to build the whole window again, asked from the Loading view (whose code
    /// width counts no lines), and drew another frame (plan 17 round 16: 14 builds and 4 frames
    /// against a later open's 11 and 3). The reader, then the compare view.
    /// </summary>
    [Fact]
    public void AFirstWrappedOpen_BuildsOnceForItsText_AndMeasuresEachLineOnce_AtTheLaidOutWidth()
    {
        using var t = new DiffViewTests.Fixture();
        string text = LongLines(80);
        t.WriteLeft("a.txt", text);
        t.WriteRight("a.txt", text.Replace("word1 ", "WORD1 "));
        t.Open(wrap: true);
        t.PutCursorOn('L', "a.txt");

        FirstOpen(t, () => t.Session.Window.PressKey(KeyCode.F3, KeyModifiers.Shift), "_viewerState", "Loading",
            "_viewerScrollRef", "ViewerCodeWidth", "_viewerHeightsWidth", "_viewerTops", "the reader");
        t.Press(KeyCode.Escape);
        t.Session.PumpFrames(3);
        // The comparison held at "Comparing…" for a few frames, as a long one is: the check after
        // the frame built the whole window again from there.
        var release = t.HoldComparison();
        FirstOpen(t, () => t.Session.Window.PressKey(KeyCode.F3), "_diffState", "Comparing",
            "_diffScrollRef", "DiffCodeWidth", "_diffHeightsWidth", "_diffTops", "the compare view", release);
    }

    /// <summary>
    /// An open after the window was resized while the view was closed measures every line once, at
    /// the width the body is laid out at. The build read the box the list of the open before had
    /// left behind, measured every line at that width and drew them at it, and the check after the
    /// frame measured them all again (plan 17 round 16: the 602k-character archive 3,040 line
    /// measures against 1,520).
    /// </summary>
    [Fact]
    public void AnOpenAfterTheWindowWasResized_MeasuresEachLineOnce_AtTheNewWidth()
    {
        using var t = new DiffViewTests.Fixture();
        string text = LongLines(80);
        t.WriteLeft("a.txt", text);
        t.WriteRight("a.txt", text.Replace("word1 ", "WORD1 "));
        t.Open(wrap: true);
        OpenViewer(t, "a.txt");
        t.Press(KeyCode.Escape);
        t.Session.PumpFrames(3);
        t.Session.Window.SetClientSize(1180, 820);
        t.Session.PumpFrames(3);

        t.PutCursorOn('L', "a.txt");
        FirstOpen(t, () => t.Session.Window.PressKey(KeyCode.F3, KeyModifiers.Shift), "_viewerState", "Loading",
            "_viewerScrollRef", "ViewerCodeWidth", "_viewerHeightsWidth", "_viewerTops", "the reader after a resize");
    }

    /// <summary>One open from the key press until nothing draws: two frames, the Loading or
    /// Comparing view's and the text's, the second showing the rows; nothing built after either;
    /// one measure of each line, at the width laid out; and the rows on screen drawing as rows
    /// made at a measure from scratch do.</summary>
    private static void FirstOpen(DiffViewTests.Fixture t, System.Action press, string state, string loading,
        string scrollRef, string codeWidth, string heightsWidth, string tops, string what, System.Action? release = null)
    {
        int frames = 0, builtAfterAFrame = 0, measures;
        bool textDrawn = false;
        var trace = new System.Text.StringBuilder();
        using (var spy = new WrapSpy())
        {
            press();
            int builds = Builds(t);
            for (int quiet = 0, i = 0; i < 2000 && quiet < 16; i++)
            {
                // A held load lands once the waiting view has been drawn and some frames have gone by.
                if (i == 12) release?.Invoke();
                int b0 = Builds(t);
                bool drew = t.Session.PumpFrame(System.TimeSpan.FromMilliseconds(16));
                string now = t.Field<object>(state).ToString()!;
                if (drew || Builds(t) != b0) trace.Append($"[{i} drew={drew} builds={Builds(t) - builds} {now} measures={spy.Widths.Count}] ");
                // Built again while still loading, after the Loading view was drawn, or after the
                // text was: a check after a frame, not the text landing.
                if (Builds(t) != b0 && (textDrawn || (frames > 0 && now == loading))) builtAfterAFrame++;
                if (drew) frames++;
                if (drew && now == "Ok") textDrawn = true;
                quiet = drew || now == loading ? 0 : quiet + 1;
                System.Threading.Thread.Sleep(2);
            }
            Assert.True(frames == 2 && builtAfterAFrame == 0, $"{what}: {frames} frames, {builtAfterAFrame} builds after a frame: {trace}");
            Assert.Equal("Ok", t.Field<object>(state).ToString());
            float laidOut = (float)typeof(CommanderApp).GetMethod(codeWidth, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(t.App, null)!;
            Assert.Equal(laidOut, t.Field<float>(heightsWidth));
            Assert.True(spy.Widths.Count > 20, $"{what}: only {spy.Widths.Count} long lines measured: not a wrap test");
            Assert.True(spy.Widths.All(w => w == laidOut),
                $"{what}: lines measured at {string.Join(", ", spy.Widths.Distinct())}, the body laid out at {laidOut}");
            measures = spy.Widths.Count;
        }

        // What it drew is what rows made again at a measure from scratch draw, and the open
        // measured as much as that one measure does.
        var drawn = t.Field<float[]>(tops);
        var kept = Pixels(t.Session);
        typeof(CommanderApp).GetField(heightsWidth, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(t.App, -1f);
        typeof(CommanderApp).GetField(tops, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(t.App, System.Array.Empty<float>());
        using (var once = new WrapSpy())
        {
            Rebuild(t);
            t.Session.PumpFrames(3);
            Assert.True(measures == once.Widths.Count, $"{what}: the open measured {measures} long lines, one measure is {once.Widths.Count}");
        }
        Assert.True(drawn.AsSpan().SequenceEqual(t.Field<float[]>(tops)), $"{what}: a measure from scratch gives other heights");
        Assert.True(kept.AsSpan().SequenceEqual(Pixels(t.Session)), $"{what}: the rows drawn differ from rows made again");
    }

    /// <summary>Counts the measures the app makes through <see cref="TextMeasure"/> of the long
    /// lines (the wrapped rows' heights), by width. The layout engine measures through its own
    /// reference to the context, so it is not counted. The tests in this assembly run one at a
    /// time, so the context can be swapped for one.</summary>
    private sealed class WrapSpy : IMeasureContext, System.IDisposable
    {
        private readonly IMeasureContext _inner = TextMeasure.Context!;
        private readonly HashSet<(string, float)> _seen = [];
        public readonly List<float> Widths = [];
        public int Distinct => _seen.Count;

        public WrapSpy() => TextMeasure.Context = this;
        public void Dispose() => TextMeasure.Context = _inner;

        private void Note(string text, float maxWidth)
        {
            if (text.Length <= 100 || !(maxWidth > 0) || maxWidth > 1e30f) return;
            Widths.Add(maxWidth);
            _seen.Add((text, maxWidth));
        }

        public Size MeasureText(string text, ResolvedInheritedStyle style, float maxWidth, int? maxLines,
            TextOverflow overflow = TextOverflow.Clip)
        { Note(text, maxWidth); return _inner.MeasureText(text, style, maxWidth, maxLines, overflow); }

        public Size MeasureText(string text, ResolvedInheritedStyle style, float maxWidth, int? maxLines,
            TextOverflow overflow, IReadOnlyList<TextSpan>? spans)
        { Note(text, maxWidth); return _inner.MeasureText(text, style, maxWidth, maxLines, overflow, spans); }

        public Size MeasureText(string text, ResolvedInheritedStyle style, float maxWidth, int? maxLines,
            TextOverflow overflow, IReadOnlyList<TextSpan>? spans, out float sameFromWidth)
        { Note(text, maxWidth); return _inner.MeasureText(text, style, maxWidth, maxLines, overflow, spans, out sameFromWidth); }

        public float MinContentTextWidth(string text, ResolvedInheritedStyle style, IReadOnlyList<TextSpan>? spans = null) =>
            _inner.MinContentTextWidth(text, style, spans);

        public FontMetrics MeasureFont(ResolvedInheritedStyle style) => _inner.MeasureFont(style);

        public bool HasGlyph(ResolvedInheritedStyle style, int codePoint) => _inner.HasGlyph(style, codePoint);

        public float MeasureFirstBaseline(string text, ResolvedInheritedStyle style, float maxWidth, int? maxLines,
            TextOverflow overflow = TextOverflow.Clip, IReadOnlyList<TextSpan>? spans = null) =>
            _inner.MeasureFirstBaseline(text, style, maxWidth, maxLines, overflow, spans);
    }

    /// <summary>With a Brief grid behind the full-shell view the resize builds as it did. The
    /// build keeps the rows on screen too (a width alone is all that moved), and they get their
    /// heights at the width the frame lays out: the reader's and the diff's.</summary>
    [Fact]
    public void ASizeChangeWithAGridBehindAWrappedView_StillBuilds_AndKeepsTheRows()
    {
        using var t = new DiffViewTests.Fixture();
        t.WriteLeft("a.txt", LongLines(80));
        t.WriteRight("a.txt", LongLines(80).Replace("word1 ", "WORD1 "));
        t.Open(wrap: true);
        t.Press(KeyCode.F2, KeyModifiers.Ctrl);
        t.Session.PumpFrames(3);
        Assert.Equal(ViewMode.Brief, (ViewMode)t.Invoke("ViewOf", 'L')!);
        OpenViewer(t, "a.txt");
        Settle(t);
        BuildingSteps(t, "_viewerScrollRef", "ViewerCodeWidth", "_viewerHeightsWidth", "_viewerTops");

        t.Press(KeyCode.Escape);
        t.Session.PumpFrames(3);
        t.OpenDiffOn("a.txt");
        Assert.Equal("Ok", t.DiffState);
        Settle(t);
        BuildingSteps(t, "_diffScrollRef", "DiffCodeWidth", "_diffHeightsWidth", "_diffTops");
    }

    private static void BuildingSteps(DiffViewTests.Fixture t, string scrollRef, string codeWidth, string heightsWidth, string tops)
    {
        foreach (int width in new[] { 1232, 1180 })
        {
            int builds = Builds(t);
            var before = RowsOnScreen(t, scrollRef);
            t.Session.Window.SetClientSize(width, 820);
            Assert.Equal(1, FramesDrawn(t.Session));
            Assert.Equal(1, Builds(t) - builds);
            AssertKept(before, RowsOnScreen(t, scrollRef), $"at {width} px, built");
            AssertMeasuredAtTheLaidOutWidth(t, codeWidth, heightsWidth, tops, width);
        }
    }

    // ── harness ──────────────────────────────────────────────────────────

    /// <summary>A size change first, so the steps start from a body the layout hook has read at a
    /// width a resize gave it: a resize leads only from a width the hook saw.</summary>
    private static void Settle(DiffViewTests.Fixture t)
    {
        t.Session.Window.SetClientSize(1236, 820);
        FramesDrawn(t.Session);
    }

    private static void HeightSteps(DiffViewTests.Fixture t, string tops)
    {
        foreach (int height in new[] { 816, 812, 760, 820 })
        {
            int builds = Builds(t);
            var heights = t.Field<float[]>(tops);
            t.Session.Window.SetClientSize(1236, height);
            Assert.Equal(1, FramesDrawn(t.Session));
            Assert.Equal(0, Builds(t) - builds);
            Assert.Same(heights, t.Field<float[]>(tops));
        }
    }

    /// <summary>The rows on screen by what they show: the line numbers and the text of each, which
    /// no two rows share.</summary>
    private static Dictionary<string, Element> RowsOnScreen(DiffViewTests.Fixture t, string scrollRef)
    {
        var list = t.Field<Ref<ScrollViewElement>>(scrollRef).Current!;
        var rows = new Dictionary<string, Element>();
        foreach (var row in list.Children)
        {
            var texts = LayoutProbe.Descendants<TextElement>(row);
            if (texts.Count > 0) rows[string.Join("\u0001", texts.Select(x => x.Content))] = row;
        }
        return rows;
    }

    /// <summary>Every row on screen before and after a step is the row it was, not one made again.</summary>
    private static void AssertKept(Dictionary<string, Element> before, Dictionary<string, Element> after, string when)
    {
        int compared = 0, made = 0;
        foreach (var (key, row) in after)
        {
            if (!before.TryGetValue(key, out var was)) continue;
            compared++;
            if (!ReferenceEquals(was, row)) made++;
        }
        Assert.True(compared > 5, $"{when}: only {compared} rows were on screen before and after");
        Assert.True(made == 0, $"{when}: {made} of {compared} rows on screen were made again");
    }

    /// <summary>The kept rows draw what rows made at this width draw: the window drawn now, and
    /// again once the list has made every row on screen again.</summary>
    private static void AssertKeptRowsDrawAsMadeRows(DiffViewTests.Fixture t, string scrollRef, string when)
    {
        var kept = Pixels(t.Session);
        var list = t.Field<Ref<ScrollViewElement>>(scrollRef).Current!;
        list.GetType().GetMethod("InvalidateRows")!.Invoke(list, null);
        t.Session.PumpFrames(3);
        Assert.True(kept.AsSpan().SequenceEqual(Pixels(t.Session)), $"{when}: the kept rows draw other pixels than rows made again");
    }

    private static byte[] Pixels(HeadlessSession session)
    {
        using var image = session.Window.CapturePixels();
        using var pixmap = image.PeekPixels();
        return pixmap.GetPixelSpan().ToArray();
    }

    /// <summary>The rows were measured at the code column the frame laid out (the width read
    /// from the laid-out body, the lead long spent), and a build from scratch now measures
    /// them the same: the frame that was drawn is the one that settles.</summary>
    private static void AssertMeasuredAtTheLaidOutWidth(DiffViewTests.Fixture t, string codeWidth, string heightsWidth, string tops, int width)
    {
        float laidOut = (float)typeof(CommanderApp).GetMethod(codeWidth, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(t.App, null)!;
        Assert.Equal(laidOut, t.Field<float>(heightsWidth));
        var drawn = t.Field<float[]>(tops);

        // Forget the widths the heights were measured at and build: the heights come out the same.
        typeof(CommanderApp).GetField(heightsWidth, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(t.App, -1f);
        Rebuild(t);
        t.Session.PumpFrames(3);
        var fresh = t.Field<float[]>(tops);
        Assert.True(drawn.Length == fresh.Length && drawn.AsSpan().SequenceEqual(fresh),
            $"at {width} px the frame drew rows {drawn[^1]:0.#} px tall in all, a fresh measure makes {fresh[^1]:0.#} px");
    }

    private static void OpenViewer(DiffViewTests.Fixture t, string name)
    {
        t.PutCursorOn('L', name);
        t.Press(KeyCode.F3, KeyModifiers.Shift);
        t.Session.PumpUntil(() => t.Field<object>("_viewerState").ToString() != "Loading", System.TimeSpan.FromSeconds(30));
        t.Session.PumpFrames(3);
        Assert.Equal("Ok", t.Field<object>("_viewerState").ToString());
        Assert.True(t.Wrap, "not a wrap test");
    }

    /// <summary>Short words, so a line wraps at spaces, and long enough to take several lines
    /// at any width here.</summary>
    private static string LongLines(int count) =>
        string.Join("\n", System.Linq.Enumerable.Range(0, count).Select(k =>
            $"{k:000} " + string.Join(" ", System.Linq.Enumerable.Range(0, 40).Select(x => $"word{(k * 31 + x) % 97}")))) + "\n";

    private static int Builds(DiffViewTests.Fixture t) => t.Field<int>("_builds");

    private static int FramesDrawn(HeadlessSession session)
    {
        int frames = 0;
        for (int i = 0; i < 16; i++)
            if (session.PumpFrame(System.TimeSpan.FromMilliseconds(16))) frames++;
        return frames;
    }

    private static void Rebuild(DiffViewTests.Fixture t) => t.MarkDirty();
}
