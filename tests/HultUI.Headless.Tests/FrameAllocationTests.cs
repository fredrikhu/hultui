using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// What a frame allocates when little changes: an idle pass of the loop, and a frame that
/// repaints, restyles or lays out one element again, allocate nothing. Each frame used to leave
/// ~0.5 KB behind however little it drew: a delegate per pass of the loop, a LinkedList node per
/// DevTools peak tracker (eight per frame, HUD shown or not), and a boxed enumerator for every
/// container the dirty-flag walks, the style pass and every scroll view's extent visited
/// (plan 17, runtime-frame-floor).
///
/// <para>These drive <see cref="Application.RunFrame"/> directly, the call
/// <see cref="Application.Run"/> makes each pass, and count what this thread allocated.</para>
/// </summary>
public class FrameAllocationTests
{
    private static readonly TimeSpan Frame = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 60);

    /// <summary>A screen with what the per-frame walks visit: a scroll view, rows nested a few
    /// containers deep, text. Returns the root and a box at the bottom of one row.</summary>
    private static (Element Root, BoxElement Deep) Screen()
    {
        var rows = new Element[40];
        BoxElement deep = null!;
        for (int i = 0; i < rows.Length; i++)
        {
            var inner = Box.New().Width(Dimension.Px(20)).Height(Dimension.Px(12)).Background(Color.Blue);
            if (i == 7) deep = inner;
            rows[i] = Flex.Row(gap: 6).FlexShrink(0).Padding(4).Children(
                TextElement.New($"Row {i}"),
                Flex.Column().Children(Flex.Row().Children(inner)));
        }
        var root = Flex.Column().FlexGrow(1).Children(
            TextElement.New("Header"),
            ScrollView.New().FlexGrow(1).Children(Flex.Column().FlexShrink(0).Children(rows)));
        return (root, deep);
    }

    private static HeadlessSession Session(Element root)
    {
        var session = HeadlessSession.Create(500, 400);
        session.Window.SetRoot(root);
        for (int i = 0; i < 4; i++) session.Application.RunFrame(Frame);
        return session;
    }

    [Fact]
    public void AnIdleLoopPass_AllocatesNothing()
    {
        var (root, _) = Screen();
        using var session = Session(root);
        var app = session.Application;
        for (int i = 0; i < 10; i++) { _ = app.IsRunning; app.RunFrame(Frame); }

        bool running = true, rendered = false;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 200; i++)
        {
            running &= app.IsRunning;
            rendered |= app.RunFrame(Frame);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(running);
        Assert.False(rendered, "the screen was meant to be idle");
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void ASteadyRepaint_AllocatesNothing()
    {
        var (root, deep) = Screen();
        using var session = Session(root);
        var app = session.Application;

        double perFrame = PerFrame(app, () => deep.MarkPaintDirty());

        Assert.True(perFrame < 1, $"a repaint allocated {perFrame:0.#} B per frame");
    }

    [Fact]
    public void ARestyleOfOneElement_AllocatesNothing()
    {
        var (root, deep) = Screen();
        using var session = Session(root);
        var app = session.Application;

        double perFrame = PerFrame(app, () => deep.MarkStyleDirty());

        Assert.True(perFrame < 1, $"restyling one box allocated {perFrame:0.#} B per frame");
    }

    [Fact]
    public void ARelayoutOfOneElement_AllocatesNothing()
    {
        var (root, deep) = Screen();
        using var session = Session(root);
        var app = session.Application;

        double perFrame = PerFrame(app, () => deep.MarkLayoutDirty());

        Assert.True(perFrame < 1, $"laying one box out again allocated {perFrame:0.#} B per frame");
    }

    /// <summary>Bytes this thread allocated per rendered frame, <paramref name="change"/>
    /// applied before each.</summary>
    private static double PerFrame(Application app, Action change)
    {
        for (int i = 0; i < 10; i++) { change(); app.RunFrame(Frame); }

        const int frames = 100;
        int rendered = 0;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < frames; i++)
        {
            change();
            if (app.RunFrame(Frame)) rendered++;
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(frames, rendered);
        return allocated / (double)frames;
    }
}
