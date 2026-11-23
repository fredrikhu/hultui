using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Primitives;
using HultUI.Testing;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// A list's window moving by a row: the row that leaves is let go, the one that comes is made,
/// and the ~40 between stay as they are (plan 17 round 18). Each move made a new array of the
/// children and, past 64 old × new, a set of them to ask which old ones stay: about 1.7 KB of a
/// 19.4 KB wheel notch in Vesper's reader, every notch of Junction's diffs and History. The
/// children that stay are told by a mark now, and the array is the list's own while the count of
/// rows holds.
/// </summary>
public class VirtualListWindowMoveTests
{
    private const float Row = 20f;
    private const int Count = 2_000;

    private sealed class Line : BoxElement
    {
        public int Mounts, Unmounts;
        protected override void OnMounted() => Mounts++;
        protected override void OnUnmounted() => Unmounts++;
    }

    /// <summary>A list of <see cref="Count"/> rows, each made once and handed back whenever its item
    /// comes into the window, so a move makes nothing of the app's.</summary>
    private static (HeadlessSession Session, VirtualListElement<int> List, Line?[] Lines) Mount()
    {
        var session = HeadlessSession.Create(400, 820);
        var lines = new Line?[Count];
        var list = VirtualListElement<int>.New()
            .ItemHeight(Row)
            .Items(Enumerable.Range(0, Count).ToArray())
            .RenderWith(i => lines[i] ??= new Line { BackgroundColor = (i % 2 == 0 ? Color.White : Color.Black) })
            .FlexGrow(1);
        session.Window.SetRoot(Flex.Column().FlexGrow(1).Children(list));
        session.PumpFrames(2);
        return (session, list, lines);
    }

    [Fact]
    public void AMoveByOneRow_KeepsEveryRowThatStays()
    {
        var (session, list, lines) = Mount();
        using var _ = session;
        list.ScrollOffsetY = 10 * Row;
        session.PumpFrames(2);
        var before = list.Children.OfType<Line>().ToList();
        Assert.True(before.Count > 40, $"only {before.Count} rows realized");
        int first = Array.IndexOf(lines, before[0]);

        list.ScrollOffsetY = 11 * Row;
        session.PumpFrames(2);

        var after = list.Children.OfType<Line>().ToList();
        Assert.Same(lines[first + 1], after[0]);
        // Every row but the first stays: the same element, in the same order, never let go.
        for (int i = 1; i < before.Count; i++)
        {
            Assert.Same(before[i], after[i - 1]);
            Assert.Equal(0, before[i].Unmounts);
            Assert.Equal(1, before[i].Mounts);
            Assert.Same(list, before[i].Parent);
        }
        Assert.Equal(1, before[0].Unmounts);
        Assert.Null(before[0].Parent);
        Assert.Equal(1, after[^1].Mounts);
    }

    [Fact]
    public void AMoveByOneRow_AllocatesNoSetAndNoArray()
    {
        var (session, list, _) = Mount();
        using var s = session;
        list.ScrollOffsetY = 10 * Row;
        session.PumpFrames(2);
        // Down a row and back: the window moves twice, the rows are the ones made before.
        void Round()
        {
            list.ScrollOffsetY = 11 * Row;
            session.PumpFrame();
            list.ScrollOffsetY = 10 * Row;
            session.PumpFrame();
        }
        Round();
        Round();

        long bytes = Allocations.Least(Round, goal: 0);

        // A set of the ~42 children and their array were 2,880 B over the two moves.
        Assert.True(bytes == 0, $"two moves of the window allocated {bytes:N0} B");
    }
}
