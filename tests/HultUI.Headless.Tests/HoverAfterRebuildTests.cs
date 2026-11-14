using System.Runtime.CompilerServices;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using SkiaSharp;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// A build that removes the element under a still pointer. Hover used to stay on the removed
/// element until the pointer next moved or left the window, and through its Parent and Children
/// links that element kept the whole old view alive (Meridian, a key switching views: up to 28 %
/// of the managed heap). After the frame, hover is hit-tested again where the pointer rests, as
/// browsers do: the removed chain gets one leave, the element now under the pointer one enter and
/// its hover look. Not while a press holds the pointer, nor when the pointer is outside the window.
/// </summary>
public class HoverAfterRebuildTests
{
    /// <summary>Enter and leave counts by name.</summary>
    private sealed class Log
    {
        public readonly Dictionary<string, int> Enters = new(), Leaves = new();
        public int Enter(string name) => Enters.GetValueOrDefault(name);
        public int Leave(string name) => Leaves.GetValueOrDefault(name);

        public T Watch<T>(T element, string name) where T : Element
        {
            element.OnPointerEnter(_ => Enters[name] = Enter(name) + 1);
            element.OnPointerLeave(_ => Leaves[name] = Leave(name) + 1);
            return element;
        }
    }

    /// <summary>A 200 × 100 view with a 100 × 50 leaf in its top left corner, both counted.</summary>
    private static BoxElement View(Log log, string name) =>
        log.Watch(Box.New().Width(200f).Height(100f), name)
            .Children(log.Watch(Box.New().Width(100f).Height(50f), name + ".leaf"));

    /// <summary>A counted host holding view <paramref name="first"/>, made here so no test method
    /// holds the view; the host takes its size from the view.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (HeadlessSession Session, BoxElement Host) Mount(Log log, string first)
    {
        var session = HeadlessSession.Create(400, 300);
        var host = log.Watch(Box.New(), "host").Children(View(log, first));
        session.Window.SetRoot(Flex.Column().AlignItems(AlignItems.FlexStart).Children(host));
        session.PumpFrame();
        return (session, host);
    }

    /// <summary>Replaces the host's view and hands back only a weak reference to the old one.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference Replace(BoxElement host, Element next)
    {
        var old = host.Children[0];
        host.ReplaceChildren(next);
        return new WeakReference(old);
    }

    private static Element Leaf(BoxElement host) => host.Children[0].Children[0];

    [Fact]
    public void AViewRemovedUnderAStillPointer_IsReleased()
    {
        var log = new Log();
        var (session, host) = Mount(log, "a");
        using var _ = session;
        session.Window.MoveMouse(50, 20);

        var old = Replace(host, View(log, "b"));
        session.PumpFrame();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.False(old.IsAlive, "hover kept the removed view alive");
    }

    [Fact]
    public void TheRemovedChainLeavesOnce_AndTheNewViewEntersOnce()
    {
        var log = new Log();
        var (session, host) = Mount(log, "a");
        using var _ = session;
        session.Window.MoveMouse(50, 20);
        Assert.Equal((1, 1, 1), (log.Enter("host"), log.Enter("a"), log.Enter("a.leaf")));

        host.ReplaceChildren(View(log, "b"));
        session.PumpFrame();
        session.PumpFrame();

        Assert.Equal((1, 1), (log.Leave("a"), log.Leave("a.leaf")));
        Assert.Equal((1, 1), (log.Enter("b"), log.Enter("b.leaf")));
        // The host never lost the pointer: no second enter, and no leave.
        Assert.Equal((1, 0), (log.Enter("host"), log.Leave("host")));
        Assert.True(host.IsHovered);
        Assert.True(Leaf(host).IsHovered);

        host.ReplaceChildren(View(log, "c"));
        session.PumpFrame();

        Assert.Equal((1, 1), (log.Leave("b"), log.Leave("b.leaf")));
        Assert.Equal((1, 1), (log.Enter("c"), log.Enter("c.leaf")));
        Assert.Equal((1, 0), (log.Enter("host"), log.Leave("host")));
    }

    [Fact]
    public void WhatTheOldChainIsNoLongerOver_LeavesToo()
    {
        var log = new Log();
        var (session, host) = Mount(log, "a");
        using var _ = session;
        session.Window.MoveMouse(150, 80);   // over view a, outside its leaf
        Assert.Equal((1, 1, 0), (log.Enter("host"), log.Enter("a"), log.Enter("a.leaf")));

        // A smaller view: the host shrinks to it, out from under the pointer.
        host.ReplaceChildren(log.Watch(Box.New().Width(100f).Height(50f), "small"));
        session.PumpFrame();

        Assert.Equal((1, 1), (log.Leave("a"), log.Leave("host")));
        Assert.Equal(0, log.Enter("small"));
        Assert.False(host.IsHovered);
    }

    [Fact]
    public void ABuildThatKeepsTheHoveredElement_FiresNothing()
    {
        var log = new Log();
        var (session, host) = Mount(log, "a");
        using var _ = session;
        session.Window.MoveMouse(50, 20);
        var hovered = Leaf(host);

        // Children change beside it, and the frame settles hover: still the same element.
        ((BoxElement)host.Children[0]).AddChild(log.Watch(Box.New().Width(20f).Height(20f), "beside"));
        session.PumpFrame();
        session.PumpFrame();

        Assert.Equal((1, 1, 1, 0), (log.Enter("host"), log.Enter("a"), log.Enter("a.leaf"), log.Enter("beside")));
        Assert.Empty(log.Leaves);
        Assert.True(hovered.IsHovered);
    }

    [Fact]
    public void TheElementUnderAStillPointer_ShowsItsHoverLook()
    {
        var red = new Color(220, 30, 30);
        var log = new Log();
        var (session, host) = Mount(log, "a");
        using var _ = session;
        session.Window.MoveMouse(150, 80);
        var next = Box.New().Width(200f).Height(100f).Background(new Color(30, 30, 220))
            .OnHover(b => b.Background(red));

        host.ReplaceChildren(next);
        session.PumpFrame();   // draws the new view; its end hit-tests hover again
        session.PumpFrame();   // draws the hover look

        Assert.True(next.IsHovered);
        Assert.Equal(red, next.BackgroundColor);
        using var image = session.Window.CapturePixels();
        using var bitmap = SKBitmap.FromImage(image);
        var pixel = bitmap.GetPixel(150, 80);
        Assert.Equal((red.R, red.G, red.B), (pixel.Red, pixel.Green, pixel.Blue));
    }

    /// <summary>The settle draws a frame only for a hover something draws. It drew one every time:
    /// Junction's commit file walk, with the pointer resting on the diff, drew each key twice, the
    /// second frame the same pixels (plan 17 round 12).</summary>
    [Fact]
    public void ABuildUnderAStillPointer_DrawsOnce_WhenNothingDrawsTheHover()
    {
        using var session = HeadlessSession.Create(400, 300);
        var host = Box.New().Children(Box.New().Width(200f).Height(100f));
        session.Window.SetRoot(Flex.Column().AlignItems(AlignItems.FlexStart).Children(host));
        session.PumpFrame();
        session.Window.MoveMouse(50, 20);
        session.PumpFrames(2);

        var next = Box.New().Width(200f).Height(100f);
        host.ReplaceChildren(next);

        Assert.True(session.PumpFrame());    // the build's; its end hit-tests hover again
        Assert.True(next.IsHovered);
        Assert.False(session.PumpFrame());   // and nothing draws that hover
    }

    /// <summary>An app's enter handler may change what is drawn without marking it, so it gets the
    /// frame an input that ran one would.</summary>
    [Fact]
    public void AnEnterHandler_GetsAFrameAfterTheSettle()
    {
        using var session = HeadlessSession.Create(400, 300);
        var host = Box.New().Children(Box.New().Width(200f).Height(100f));
        session.Window.SetRoot(Flex.Column().AlignItems(AlignItems.FlexStart).Children(host));
        session.PumpFrame();
        session.Window.MoveMouse(50, 20);
        session.PumpFrames(2);

        int entered = 0;
        var next = Box.New().Width(200f).Height(100f).OnPointerEnter(_ => entered++);
        host.ReplaceChildren(next);

        Assert.True(session.PumpFrame());
        Assert.Equal(1, entered);
        Assert.True(session.PumpFrame());
        Assert.False(session.PumpFrame());
    }

    /// <summary>A list draws its tint under the row the pointer is on from the row's hover, which
    /// marks nothing: the row the settle finds under the pointer gets the frame that tints it.</summary>
    [Fact]
    public void AListsTintUnderTheRow_IsDrawnAfterTheSettle()
    {
        var tint = new Color(30, 150, 90);
        using var session = HeadlessSession.Create(400, 300);
        var list = VirtualListElement<string>.New().ItemHeight(24f)
            .Items(Enumerable.Range(0, 20).Select(i => $"a{i}").ToList())
            .RenderWith((item, _) => Flex.Row().Children(HultUI.Core.Builders.Text.New(item).FontSize(12f)))
            .HoverHighlight(tint)
            .FlexGrow(1);
        session.Window.SetRoot(Flex.Column().Width(400f).Height(300f).Children(list));
        session.PumpFrames(2);
        session.Window.MoveMouse(200, 36);   // the second row
        session.PumpFrames(2);
        var hovered = list.Children.First(row => row.IsHovered);

        list.Items(Enumerable.Range(0, 20).Select(i => $"b{i}").ToList());   // every row made again

        Assert.True(session.PumpFrame());   // the new rows; the end of the frame hovers the new row
        Assert.False(hovered.IsHovered);
        Assert.Contains(list.Children, row => row.IsHovered && !ReferenceEquals(row, hovered));
        Assert.True(session.PumpFrame());   // the tint under it
        Assert.False(session.PumpFrame());
        using var image = session.Window.CapturePixels();
        using var bitmap = SKBitmap.FromImage(image);
        var pixel = bitmap.GetPixel(380, 36);
        Assert.Equal((tint.R, tint.G, tint.B), (pixel.Red, pixel.Green, pixel.Blue));
    }

    /// <summary>A build that removes a list with the row the pointer rests on: the list's tint under
    /// the row went with it, and the frame that showed the build drew without it, so the removed
    /// row's leave asks for no frame. It was counted as a drawn hover change and drew that frame's
    /// pixels again: Meridian, a key switching tabs with the pointer resting on the map list, two
    /// frames a switch (plan 17 round 14).</summary>
    [Fact]
    public void AListRemovedUnderAStillPointer_DrawsOnce()
    {
        using var session = HeadlessSession.Create(400, 300);
        var host = Box.New().Width(400f).Height(300f).Children(TintedList("a", new Color(30, 150, 90)));
        session.Window.SetRoot(Flex.Column().Children(host));
        session.PumpFrames(2);
        session.Window.MoveMouse(200, 36);   // the second row
        session.PumpFrames(2);
        Assert.Contains(host.Children[0].Children, row => row.IsHovered);

        var next = Box.New().Width(400f).Height(300f);
        host.ReplaceChildren(next);

        Assert.True(session.PumpFrame());    // the build's; its end hit-tests hover again
        Assert.True(next.IsHovered);
        Assert.False(session.PumpFrame());   // the tint went with the list
    }

    /// <summary>A list replaced by another under a still pointer: the row now under it enters, and
    /// the tint the new list draws under it gets its frame.</summary>
    [Fact]
    public void AListReplacedUnderAStillPointer_TintsTheNewRow()
    {
        var tint = new Color(30, 150, 90);
        using var session = HeadlessSession.Create(400, 300);
        var host = Box.New().Width(400f).Height(300f).Children(TintedList("a", tint));
        session.Window.SetRoot(Flex.Column().Children(host));
        session.PumpFrames(2);
        session.Window.MoveMouse(200, 36);
        session.PumpFrames(2);

        var next = TintedList("b", tint);
        host.ReplaceChildren(next);

        Assert.True(session.PumpFrame());    // the new list; the end of the frame hovers its row
        Assert.Contains(next.Children, row => row.IsHovered);
        Assert.True(session.PumpFrame());    // the tint under it
        Assert.False(session.PumpFrame());
        using var image = session.Window.CapturePixels();
        using var bitmap = SKBitmap.FromImage(image);
        var pixel = bitmap.GetPixel(380, 36);
        Assert.Equal((tint.R, tint.G, tint.B), (pixel.Red, pixel.Green, pixel.Blue));
    }

    /// <summary>Twenty rows of 24 px that draw <paramref name="tint"/> under the hovered one.</summary>
    private static VirtualListElement<string> TintedList(string name, Color tint) =>
        VirtualListElement<string>.New().ItemHeight(24f)
            .Items(Enumerable.Range(0, 20).Select(i => $"{name}{i}").ToList())
            .RenderWith((item, _) => Flex.Row().Children(HultUI.Core.Builders.Text.New(item).FontSize(12f)))
            .HoverHighlight(tint)
            .FlexGrow(1);

    [Fact]
    public void ACapturedPointer_KeepsHoverUntilTheRelease()
    {
        var log = new Log();
        var (session, host) = Mount(log, "a");
        using var _ = session;
        Leaf(host).OnPointerDown(e => e.CapturePointer());
        session.Window.MouseDown(50, 20);

        host.ReplaceChildren(View(log, "b"));
        session.PumpFrame();
        session.PumpFrame();
        Assert.Equal((0, 0), (log.Leave("a.leaf"), log.Enter("b.leaf")));   // hover stood still

        session.Window.MouseUp(50, 20);
        session.PumpFrame();
        session.PumpFrame();

        Assert.Equal((1, 1), (log.Leave("a"), log.Leave("a.leaf")));
        Assert.Equal((1, 1), (log.Enter("b"), log.Enter("b.leaf")));
        Assert.Equal((1, 0), (log.Enter("host"), log.Leave("host")));
    }

    [Fact]
    public void APressHeld_KeepsHoverUntilTheRelease()
    {
        var log = new Log();
        var (session, host) = Mount(log, "a");
        using var _ = session;
        session.Window.MouseDown(50, 20);

        host.ReplaceChildren(View(log, "b"));
        session.PumpFrame();
        session.PumpFrame();
        Assert.Equal((0, 0), (log.Leave("a.leaf"), log.Enter("b.leaf")));

        session.Window.MouseUp(50, 20);
        session.PumpFrame();   // the release asks for this frame; its end settles hover
        session.PumpFrame();

        Assert.Equal((1, 1), (log.Leave("a"), log.Leave("a.leaf")));
        Assert.Equal((1, 1), (log.Enter("b"), log.Enter("b.leaf")));
        Assert.Equal((1, 0), (log.Enter("host"), log.Leave("host")));
    }

    [Fact]
    public void APointerNeverInTheWindow_HitTestsNothing()
    {
        var log = new Log();
        var (session, host) = Mount(log, "a");
        using var _ = session;

        host.ReplaceChildren(View(log, "b"));
        session.PumpFrame();
        session.PumpFrame();

        Assert.Empty(log.Enters);
        Assert.Empty(log.Leaves);
    }

    [Fact]
    public void APointerThatLeftTheWindow_HitTestsNothing()
    {
        var log = new Log();
        var (session, host) = Mount(log, "a");
        using var _ = session;
        session.Window.MoveMouse(50, 20);
        session.Window.MouseLeaveWindow();
        Assert.Equal((1, 1, 1), (log.Leave("host"), log.Leave("a"), log.Leave("a.leaf")));

        host.ReplaceChildren(View(log, "b"));
        session.PumpFrame();
        session.PumpFrame();

        Assert.Equal((0, 0), (log.Enter("b"), log.Enter("b.leaf")));
        Assert.Equal(1, log.Enter("host"));
    }

    [Fact]
    public void AnOverlayClosedUnderAStillPointer_HandsHoverToWhatIsUnderIt()
    {
        var log = new Log();
        var (session, host) = Mount(log, "a");
        using var _ = session;
        var window = session.Window.Window;
        var menu = log.Watch(Box.New().Width(120f).Height(60f), "menu");
        var handle = window.ShowOverlay(menu, 10, 10);
        session.PumpFrame();
        session.Window.MoveMouse(50, 20);   // over the menu, which covers the leaf
        Assert.Equal((1, 0), (log.Enter("menu"), log.Enter("a.leaf")));

        window.CloseOverlay(handle);
        session.PumpFrame();

        Assert.Equal(1, log.Leave("menu"));
        Assert.Equal((1, 1, 1), (log.Enter("host"), log.Enter("a"), log.Enter("a.leaf")));
        Assert.False(menu.IsHovered);
        Assert.True(Leaf(host).IsHovered);
    }

    [Fact]
    public void ATooltipOfARemovedElementCloses_AndTheNewOneWaitsItsDelay()
    {
        var log = new Log();
        var (session, host) = Mount(log, "a");
        using var _ = session;
        Leaf(host).Tooltip("old");
        session.Window.MoveMouse(50, 20);
        session.PumpFrame(TimeSpan.FromSeconds(1));
        Assert.Equal(new[] { "old" }, TooltipTexts(session));

        var next = View(log, "b");
        next.Children[0].Tooltip("new");
        host.ReplaceChildren(next);
        session.PumpFrame();

        Assert.Empty(TooltipTexts(session));       // closed with its element
        session.PumpFrame(TimeSpan.FromMilliseconds(200));
        Assert.Empty(TooltipTexts(session));       // the new one waits the usual delay
        session.PumpFrame(TimeSpan.FromSeconds(1));
        Assert.Equal(new[] { "new" }, TooltipTexts(session));
    }

    [Fact]
    public void ATooltipStillInItsDelay_NeverOpensForARemovedElement()
    {
        var log = new Log();
        var (session, host) = Mount(log, "a");
        using var _ = session;
        Leaf(host).Tooltip("old");
        session.Window.MoveMouse(50, 20);
        session.PumpFrame(TimeSpan.FromMilliseconds(100));

        host.ReplaceChildren(View(log, "b"));
        session.PumpFrame();
        session.PumpFrame(TimeSpan.FromSeconds(1));

        Assert.Empty(TooltipTexts(session));
    }

    private static List<string> TooltipTexts(HeadlessSession session) =>
        session.Window.OverlayContents
            .SelectMany(o => o.Children)
            .OfType<TextElement>()
            .Select(t => t.Content)
            .ToList();
}
