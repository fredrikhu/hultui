using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Primitives;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// Drag and drop inside an overlay: a dialog (a settings panel shown with
/// <c>ShowOverlay</c>) can have draggable chips and drop targets of its own. The source is
/// found under the press in the overlay, the hover and the drop are hit-tested in the overlays
/// first, and a modal overlay keeps the tree beneath it from taking the drop.
/// </summary>
public class OverlayDragDropTests
{
    private const float OverlayX = 120, OverlayY = 60;

    private static Point At(Element el, float dx = OverlayX, float dy = OverlayY)
    {
        var b = el.Layout.AbsoluteBorderBox;
        return new Point(dx + b.X + b.Width / 2f, dy + b.Y + b.Height / 2f);
    }

    [Fact]
    public void ADraggableInAnOverlay_DropsOnATargetInTheSameOverlay()
    {
        using var session = HeadlessSession.Create(width: 600, height: 400);
        session.Window.SetRoot(Box.New().Width(600).Height(400));
        string? dropped = null;
        bool ended = false, accepted = false;

        var source = Box.New().Width(80).Height(40).Draggable();
        source.OnDragStart(e => e.DragData.Set("payload", "chip"));
        source.OnDragEnd(e => { ended = true; accepted = e.WasAccepted; });
        var target = Box.New().Width(120).Height(80);
        target.OnDragOver(e => e.Accepted = e.DragData.Has("payload"));
        target.OnDrop(e => dropped = e.DragData.TryGet<string>("payload", out var v) ? v : null);

        var dialog = Flex.Column(gap: 40).Padding(10).Width(300).Children(source, target);
        session.Window.Window.ShowOverlay(dialog, OverlayX, OverlayY, dismissOnOutsideClick: false, modal: true);
        session.PumpFrame();

        session.Window.Drag(At(source), At(target), steps: 10);

        Assert.Equal("chip", dropped);
        Assert.True(ended);
        Assert.True(accepted);
    }

    [Fact]
    public void ADropOverAModalOverlay_NeverReachesTheTreeBeneathIt()
    {
        using var session = HeadlessSession.Create(width: 600, height: 400);
        bool mainDropped = false;
        var under = Box.New().Width(600).Height(400);
        under.OnDragOver(e => e.Accepted = true);
        under.OnDrop(_ => mainDropped = true);
        session.Window.SetRoot(under);

        var source = Box.New().Width(80).Height(40).Draggable();
        source.OnDragStart(e => e.DragData.Set("payload", "chip"));
        // A full-window backdrop, as a modal dialog has: it accepts nothing itself.
        var backdrop = Flex.Column(gap: 0).Width(600).Height(400).Padding(20).Children(source);
        session.Window.Window.ShowOverlay(backdrop, 0, 0, dismissOnOutsideClick: false, modal: true);
        session.PumpFrame();

        session.Window.Drag(At(source, 0, 0), new Point(400, 300), steps: 10);

        Assert.False(mainDropped);
    }

    [Fact]
    public void TheEnterAndLeaveOfATargetInAnOverlay_ArePaired()
    {
        using var session = HeadlessSession.Create(width: 600, height: 400);
        session.Window.SetRoot(Box.New().Width(600).Height(400));
        var log = new List<string>();
        var source = Box.New().Width(80).Height(40).Draggable();
        source.OnDragStart(e => e.DragData.Set("payload", 1));
        var target = Box.New().Width(120).Height(60);
        target.OnDragEnter(_ => log.Add("enter"));
        target.OnDragLeave(_ => log.Add("leave"));
        target.OnDragOver(e => e.Accepted = true);
        var spare = Box.New().Width(120).Height(60);
        var dialog = Flex.Column(gap: 20).Padding(10).Width(300).Children(source, target, spare);
        session.Window.Window.ShowOverlay(dialog, OverlayX, OverlayY, dismissOnOutsideClick: false, modal: true);
        session.PumpFrame();

        // Over the target and on past it, dropped on the spare box (which accepts nothing).
        session.Window.Drag(At(source), At(spare), steps: 12);

        Assert.Equal(["enter", "leave"], log);
    }
}
