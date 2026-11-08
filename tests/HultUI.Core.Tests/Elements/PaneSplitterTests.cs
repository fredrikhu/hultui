using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using Xunit;

namespace HultUI.Core.Tests.Elements;

public class PaneSplitterTests
{
    [Fact]
    public void Initial_IsClamped_AndRestoreIsSilent()
    {
        int changed = 0;
        var s = new PaneSplitter(() => changed++, null, 100, 400, 900);
        Assert.Equal(400, s.Size);
        s.Restore(50);
        Assert.Equal(100, s.Size);
        Assert.Equal(0, changed);
    }

    [Fact]
    public void DragTo_MovesFromTheGrab_ClampsAndReports()
    {
        int changed = 0;
        var s = new PaneSplitter(() => changed++, null, 100, 400, 200);
        var handle = s.Handle();
        // A press at x=500 with the pane at 200: every later x maps relative to that.
        Press(s, handle, 500);
        Assert.True(s.IsDragging);

        Assert.True(s.DragTo(560));
        Assert.Equal(260, s.Size);
        Assert.False(s.DragTo(560.2f));           // under the half-pixel threshold
        Assert.True(s.DragTo(9000));
        Assert.Equal(400, s.Size);
        Assert.True(s.DragTo(-9000));
        Assert.Equal(100, s.Size);
        Assert.Equal(1 + 3, changed);             // the press and three real moves
    }

    /// <summary>A pane after its handle — a response pane right of an editor, a console below one —
    /// grows as the handle moves away from it, and End takes it to its smallest.</summary>
    [Fact]
    public void Trailing_GrowsAsTheHandleMovesAwayFromIt()
    {
        int committed = 0;
        var s = new PaneSplitter(() => { }, () => committed++, 100, 400, 200, trailing: true);
        var handle = s.Handle();
        Press(s, handle, 500);

        Assert.True(s.DragTo(440));                 // the handle 60 to the left: the pane 60 wider
        Assert.Equal(260, s.Size);
        Assert.True(s.DragTo(9000));
        Assert.Equal(100, s.Size);

        handle.OnKeyStep!(-10);                     // Left
        Assert.Equal(110, s.Size);
        handle.OnKeyStep!(10);                      // Right
        Assert.Equal(100, s.Size);
        handle.OnKeyEnd!(false);                    // Home: the handle to the start, the pane largest
        Assert.Equal(400, s.Size);
        handle.OnKeyEnd!(true);                     // End
        Assert.Equal(100, s.Size);
        Assert.Equal(4, committed);
    }

    /// <summary>A room that follows the window: the size reads clamped to it and a drag stops at
    /// it — no dead zone when dragging back — and the size asked for returns when it grows.</summary>
    [Fact]
    public void SetMax_ClampsTheSizeAndTheDrag_AndAWiderRoomGivesItBack()
    {
        var s = new PaneSplitter(() => { }, null, 100, 10_000, 600);
        s.SetMax(400);
        Assert.Equal(400, s.Size);
        s.SetMax(800);
        Assert.Equal(600, s.Size);                  // what it was given, back

        s.SetMax(400);
        var handle = s.Handle();
        Press(s, handle, 500);                      // the grab starts from what shows: 400
        s.DragTo(900);
        Assert.Equal(400, s.Size);
        s.DragTo(450);                              // back 450 from the far point: moves at once
        Assert.Equal(350, s.Size);

        s.SetMax(50);                               // never below the smallest
        Assert.Equal(100, s.Size);
    }

    [Fact]
    public void Handle_ReadsAsASeparator_WithTheResizeCursor()
    {
        var s = new PaneSplitter(() => { }, null, 0, 100, 50);
        var h = s.Handle();
        Assert.Equal(AccessibilityRole.Separator, AccessibilityTreeBuilder.ResolveRole(h));
        Assert.Equal(CursorType.ResizeEW, h.Inherited.Cursor);

        var v = new PaneSplitter(() => { }, null, 0, 100, 50, SplitterAxis.Vertical).Handle();
        Assert.Equal(CursorType.ResizeNS, v.Inherited.Cursor);
    }

    private static void Press(PaneSplitter s, SplitterHandleElement handle, float x)
    {
        var evt = new PointerDownEvent { Position = new Point(x, 10), Button = 0 };
        EventDispatcher.DispatchDirect(evt, handle);
    }
}
