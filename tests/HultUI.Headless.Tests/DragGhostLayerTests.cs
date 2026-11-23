using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using SkiaSharp;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// The in-window drag ghost fades through a group the size of what it paints, not one the
/// window's size. Unbounded, every frame of a drag drew the window through a layer, and on GL
/// Skia kept that texture for the window's life: Junction's first repository drag held 18 MB
/// more video memory at 2160×1350 (plan 17, round 18).
/// </summary>
public sealed class DragGhostLayerTests
{
    private static readonly Color Panel = Color.FromHex("#CBD5E1");
    private static readonly Color Red = Color.FromHex("#EF4444");
    private static readonly Color Green = Color.FromHex("#22C55E");
    private static readonly Color Amber = Color.FromHex("#F59E0B");

    // The group's origin moves from the window's corner to the ghost's, which shades antialiased
    // edges on another grid: a few pixels by a few levels (OpacityLayerTests.MovedOrigin; the user
    // accepted it for the ghost, plan 17 question 73). Ghost cut off is ink against background.
    private const int MovedOrigin = 16;

    private static T At<T>(T el, float left, float top) where T : Element
    {
        el.Position.Type = PositionType.Absolute;
        el.Position.Left = Dimension.Px(left);
        el.Position.Top = Dimension.Px(top);
        return el;
    }

    /// <summary>Paints nothing and cannot say so: a group holding it is the window's size.</summary>
    private sealed class UnboundedElement : Element
    {
        public override Rect? PaintBounds(IRenderContext ctx) => null;
    }

    /// <summary>A row that paints past its own box three ways: a child wider than it, one pulled
    /// out past its top-left corner and one placed outside it; or the same turned by 20°.</summary>
    private static BoxElement Source(string kind, bool unbounded)
    {
        var source = Box.New().Width(80).Height(30).Background(Panel).CornerRadius(6f).Draggable();
        if (kind == "rotated")
            source.Rotate(20f).Children(Box.New().Width(30).Height(12).Background(Red));
        else
            source.Children(
                Box.New().Width(130).Height(10).FlexShrink(0).Background(Red),
                Box.New().Width(20).Height(20).Margin(new Thickness(-12, 0, 0, -12)).Background(Green).CornerRadius(10f),
                At(Box.New().Width(26).Height(26).Background(Amber), 66, 20));
        // Draws nothing; only its unknown bounds make the ghost's group the window's size.
        if (unbounded) source.AddChild(At(new UnboundedElement().Width(1).Height(1), 0, 0));
        return source;
    }

    [Fact]
    public void TheGhostFadesInAGroupOfWhatTheSourcePaints()
    {
        using var session = HeadlessSession.Create(300, 200, options: new HeadlessOptions { RecordDrawCalls = true, PinnedText = true });
        var source = At(Box.New().Width(80).Height(30).Background(Panel).Draggable()
            .Children(Box.New().Width(150).Height(24).FlexShrink(0).Background(Red)), 20, 20);
        session.Window.SetRoot(Box.New().Width(300).Height(200).Children(source));
        session.PumpFrame();

        session.Window.MouseDown(60, 35);
        session.Window.MoveMouse(90, 80);
        session.Window.MoveMouse(120, 120);
        session.Window.RenderLog.Clear();
        session.PumpFrame();

        // In the source's own layout coordinates (the ghost is translated to the pointer): its box
        // and the child that overflows it, 150×30, against the window's 300×200.
        var log = session.Window.RenderLog.ToString();
        Assert.Contains("PushOpacity 0.7 bounds=(20,20 150x30)", log);
        session.Window.MouseUp(120, 120);
    }

    [Fact]
    public void ACustomDragVisualFadesInAGroupOfItsOwnSize()
    {
        using var session = HeadlessSession.Create(300, 200, options: new HeadlessOptions { RecordDrawCalls = true, PinnedText = true });
        var source = At(Box.New().Width(80).Height(30).Background(Panel).Draggable(), 20, 20);
        source.OnDragStart(e => e.DragVisual = Box.New().Width(40).Height(16).Background(Green));
        session.Window.SetRoot(Box.New().Width(300).Height(200).Children(source));
        session.PumpFrame();

        session.Window.MouseDown(60, 35);
        session.Window.MoveMouse(90, 80);
        session.Window.MoveMouse(120, 120);
        session.Window.RenderLog.Clear();
        session.PumpFrame();

        Assert.Contains("PushOpacity 0.8 bounds=(0,0 40x16)", session.Window.RenderLog.ToString());
        session.Window.MouseUp(120, 120);
    }

    /// <summary>
    /// The ghost of a row that paints past its box (or is turned), dragged to every edge and corner
    /// of the window so part of it is off the window, at 1× and 1.5×: every frame within the moved
    /// origin's few levels of the same ghost drawn through a group the window's size, so nothing
    /// is cut off; frames before the drag are identical, and the ghost is really there.
    /// </summary>
    [Theory]
    [InlineData("overflow", 1f)]
    [InlineData("overflow", 1.5f)]
    [InlineData("rotated", 1.5f)]
    public void TheGhostDrawsWholeAtTheWindowsEdges(string kind, float scale)
    {
        const int W = 400, H = 240;
        Point[] stops =
        [
            new(200, 120), new(6, 120), new(394, 120), new(200, 5), new(200, 235),
            new(4, 4), new(396, 4), new(4, 236), new(396.5f, 236.5f),
        ];

        List<SKBitmap> Frames(bool unbounded, out string log)
        {
            var frames = new List<SKBitmap>();
            using var session = HeadlessSession.Create(W, H, scale,
                options: new HeadlessOptions { RecordDrawCalls = true, PinnedText = true });
            var source = At(Source(kind, unbounded), 150, 100);
            session.Window.SetRoot(Box.New().Width(W).Height(H).Background(Color.White).Children(source));
            session.PumpFrame();
            frames.Add(Capture(session));

            session.Window.MouseDown(190, 115);
            session.Window.MoveMouse(193.3f, 118.7f);
            session.Window.RenderLog.Clear();
            foreach (var stop in stops)
            {
                session.Window.MoveMouse(stop.X, stop.Y);
                session.PumpFrame();
                frames.Add(Capture(session));
            }
            log = session.Window.RenderLog.ToString();
            session.Window.MouseUp(stops[^1].X, stops[^1].Y);
            return frames;
        }

        var bounded = Frames(unbounded: false, out var boundedLog);
        var window = Frames(unbounded: true, out var windowLog);
        try
        {
            static List<string> Ghosts(string log) =>
                log.Split('\n').Where(l => l.Contains("PushOpacity 0.7", StringComparison.Ordinal)).ToList();
            var boundedGhosts = Ghosts(boundedLog);
            var windowGhosts = Ghosts(windowLog);
            Assert.True(boundedGhosts.Count >= stops.Length, $"{boundedGhosts.Count} ghosts drawn");
            Assert.All(boundedGhosts, l => Assert.Contains("bounds=", l));
            Assert.Equal(boundedGhosts.Count, windowGhosts.Count);
            Assert.All(windowGhosts, l => Assert.DoesNotContain("bounds=", l));

            Assert.True(Diff(bounded[0], window[0]).Max == 0, "the frame before the drag differs");
            for (int i = 1; i < bounded.Count; i++)
            {
                var (max, far) = Diff(bounded[i], window[i]);
                Assert.True(far == 0, $"at {stops[i - 1]}: {far} pixels differ by more than {MovedOrigin} levels (at most {max}): the ghost was cut off");
                // The ghost is there: the frame is not the one before the drag.
                Assert.True(Diff(bounded[i], bounded[0]).Far > 100, $"at {stops[i - 1]} no ghost was drawn");
            }
        }
        finally
        {
            foreach (var f in bounded) f.Dispose();
            foreach (var f in window) f.Dispose();
        }
    }

    private static SKBitmap Capture(HeadlessSession session)
    {
        using var image = session.Window.CapturePixels();
        return SKBitmap.FromImage(image);
    }

    private static (int Max, int Far) Diff(SKBitmap a, SKBitmap b)
    {
        int max = 0, far = 0;
        for (int y = 0; y < a.Height; y++)
            for (int x = 0; x < a.Width; x++)
            {
                SKColor p = a.GetPixel(x, y), q = b.GetPixel(x, y);
                int d = Math.Max(Math.Abs(p.Red - q.Red), Math.Max(Math.Abs(p.Green - q.Green), Math.Abs(p.Blue - q.Blue)));
                max = Math.Max(max, d);
                if (d > MovedOrigin) far++;
            }
        return (max, far);
    }
}
