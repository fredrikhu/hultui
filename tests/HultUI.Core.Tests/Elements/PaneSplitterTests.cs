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
