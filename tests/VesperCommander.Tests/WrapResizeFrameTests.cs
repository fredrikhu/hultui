using System.Reflection;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Reactivity;
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
/// resize now leads the width by what the window gained or lost (LeadWrappedBodyWidth), and the
/// check stays to correct a lead that was wrong — the wrong-lead test makes one on purpose.
/// Plan 17 round 15 took the build out: a wrapped row reads neither the window nor the width,
/// so the resize measures the heights itself and hands them to the list (FitWrappedRowsInPlace).
/// The build made every row on screen again and patched the whole window, ~290 KB a 4 px step
/// of the reader over a 1,000-line file, and 64 KB a height-only step that changed nothing.</para>
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
    /// check read it. Here the check is told the window was 60 px wider then than it was, so
    /// the lead comes out 60 px short, as it would wherever the body did not take all of the
    /// window's change: the frame draws rows measured at the wrong width, and the check after it
    /// builds and draws a second frame at the width on screen, keeping the rows.
    /// </summary>
    [Fact]
    public void AWrongLead_IsCorrectedByTheCheckAfterTheFrame()
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
        Assert.Equal(2, FramesDrawn(t.Session));
        Assert.Equal(1, Builds(t) - builds);
        AssertKept(before, RowsOnScreen(t, "_viewerScrollRef"), "after the corrected lead");
        AssertMeasuredAtTheLaidOutWidth(t, "ViewerCodeWidth", "_viewerHeightsWidth", "_viewerTops", 1200);

        // And the next size change leads from the width the check read again.
        builds = Builds(t);
        t.Session.Window.SetClientSize(1180, 820);
        Assert.Equal(1, FramesDrawn(t.Session));
        Assert.Equal(0, Builds(t) - builds);
        AssertMeasuredAtTheLaidOutWidth(t, "ViewerCodeWidth", "_viewerHeightsWidth", "_viewerTops", 1180);
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

    /// <summary>A size change first, so the check after the frame has read the body's width: a
    /// resize leads only from a width it saw (the open's own build guesses from the window).</summary>
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
