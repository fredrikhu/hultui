using System.Reflection;
using HultUI.Core.Events;
using HultUI.Headless;
using Xunit;

namespace VesperCommander.Tests;

/// <summary>
/// A size change with the wrapped viewer (Shift+F3) or diff (F3) open builds once and draws
/// once, with every row as tall as its text at the width that frame lays the body out at.
///
/// <para>A wrapped row's height is measured in the build, at the body's width, and the build
/// runs before the frame's layout. It used to read the width the body had a frame ago: the
/// frame drew every row at the old width's height, and the check after it
/// (SettleWrappedRowWidths) built and drew a second frame at the new one. The resize now leads
/// the width by what the window gained or lost (LeadWrappedBodyWidth), and the check stays to
/// correct a lead that was wrong — the last test makes one wrong on purpose.</para>
/// </summary>
public class WrapResizeFrameTests
{
    // Shrinks and grows, a few pixels and a few hundred, from the fixture's 1,240 px.
    private static readonly int[] Widths = [1236, 1232, 1228, 1180, 1260, 1300, 940, 1240];

    [Fact]
    public void ASizeChangeWithTheWrappedViewerOpen_BuildsAndDrawsOnce_AtTheWidthItLaysOut()
    {
        using var t = new DiffViewTests.Fixture();
        t.WriteLeft("a.txt", LongLines(80));
        t.Open(wrap: true);
        OpenViewer(t, "a.txt");

        foreach (int width in Widths)
        {
            int builds = Builds(t);
            t.Session.Window.SetClientSize(width, 820);
            Assert.Equal(1, FramesDrawn(t.Session));
            Assert.Equal(1, Builds(t) - builds);
            AssertMeasuredAtTheLaidOutWidth(t, "ViewerCodeWidth", "_viewerHeightsWidth", "_viewerTops", width);
        }
    }

    [Fact]
    public void ASizeChangeWithTheWrappedDiffOpen_BuildsAndDrawsOnce_AtTheWidthItLaysOut()
    {
        using var t = new DiffViewTests.Fixture();
        t.WriteLeft("a.txt", LongLines(80));
        t.WriteRight("a.txt", LongLines(80).Replace("word1 ", "WORD1 "));
        t.Open(wrap: true);
        t.OpenDiffOn("a.txt");
        Assert.Equal("Ok", t.DiffState);

        foreach (int width in Widths)
        {
            int builds = Builds(t);
            t.Session.Window.SetClientSize(width, 820);
            Assert.Equal(1, FramesDrawn(t.Session));
            Assert.Equal(1, Builds(t) - builds);
            AssertMeasuredAtTheLaidOutWidth(t, "DiffCodeWidth", "_diffHeightsWidth", "_diffTops", width);
        }
    }

    /// <summary>
    /// The lead is the body's last laid-out width plus what the window gained or lost since the
    /// check read it. Here the check is told the window was 60 px wider then than it was, so
    /// the lead comes out 60 px short, as it would wherever the body did not take all of the
    /// window's change: the frame draws rows measured at the wrong width, and the check after it
    /// builds and draws a second frame at the width on screen, as every size change did before.
    /// </summary>
    [Fact]
    public void AWrongLead_IsCorrectedByTheCheckAfterTheFrame()
    {
        using var t = new DiffViewTests.Fixture();
        t.WriteLeft("a.txt", LongLines(80));
        t.Open(wrap: true);
        OpenViewer(t, "a.txt");
        // Settle the check's reading at this size first.
        t.Session.Window.SetClientSize(1236, 820);
        FramesDrawn(t.Session);

        var field = typeof(CommanderApp).GetField("_wrapLaidOutWindowWidth", BindingFlags.NonPublic | BindingFlags.Instance)!;
        float read = (float)field.GetValue(t.App)!;
        Assert.Equal(1236f, read);
        field.SetValue(t.App, read + 60f);

        int builds = Builds(t);
        t.Session.Window.SetClientSize(1200, 820);
        Assert.Equal(2, FramesDrawn(t.Session));
        Assert.Equal(2, Builds(t) - builds);
        AssertMeasuredAtTheLaidOutWidth(t, "ViewerCodeWidth", "_viewerHeightsWidth", "_viewerTops", 1200);

        // And the next size change leads from the width the check read again.
        builds = Builds(t);
        t.Session.Window.SetClientSize(1180, 820);
        Assert.Equal(1, FramesDrawn(t.Session));
        Assert.Equal(1, Builds(t) - builds);
        AssertMeasuredAtTheLaidOutWidth(t, "ViewerCodeWidth", "_viewerHeightsWidth", "_viewerTops", 1180);
    }

    // ── harness ──────────────────────────────────────────────────────────

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
