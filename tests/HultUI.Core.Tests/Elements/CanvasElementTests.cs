using HultUI.Core.Elements;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using Xunit;

namespace HultUI.Core.Tests.Elements;

public class CanvasElementTests
{
    [Fact]
    public void New_CreatesWithDrawCallback()
    {
        bool called = false;
        var canvas = CanvasElement.New(_ => called = true);

        Assert.NotNull(canvas.Draw);
        Assert.False(called);
    }

    [Fact]
    public void New_WithoutCallback_DrawIsNull()
    {
        var canvas = CanvasElement.New();
        Assert.Null(canvas.Draw);
    }

    [Fact]
    public void PatchFrom_CopiesDrawCallback()
    {
        var canvas1 = CanvasElement.New(_ => { });
        Action<ICanvasDrawingContext> newDraw = _ => { };
        var canvas2 = CanvasElement.New(newDraw);

        canvas1.PatchFrom(canvas2);

        Assert.Equal(newDraw, canvas1.Draw);
    }

    [Fact]
    public void Render_CallsDrawCallback()
    {
        bool drawCalled = false;
        Rect? receivedBounds = null;

        var canvas = CanvasElement.New(ctx =>
        {
            drawCalled = true;
            receivedBounds = ctx.Bounds;
        });

        var mockCtx = new StubRenderContext();
        canvas.Render(mockCtx);

        Assert.True(drawCalled);
        Assert.NotNull(receivedBounds);
    }

    [Fact]
    public void Render_DoesNothing_WhenDrawIsNull()
    {
        var canvas = CanvasElement.New();
        var mockCtx = new StubRenderContext();

        // Should not throw
        canvas.Render(mockCtx);
    }

    private sealed class StubRenderContext : IRenderContext
    {
        public void DrawRect(Rect rect, Color fill) { }
        public void DrawRoundedRect(Rect rect, CornerRadius radius, Color fill, Color stroke, Thickness strokeWidth) { }
        public void DrawText(string text, Rect bounds, Core.Styling.ResolvedInheritedStyle style, Core.Layout.TextOverflow overflow, int? maxLines) { }
        public void DrawImage(IImageSource source, Rect destRect, Core.Layout.ImageFit fit) { }
        public void DrawLine(Point from, Point to, Color color, float strokeWidth) { }
        public void PushClip(Rect clipRect) { }
        public void PushRoundedClip(Rect clipRect, CornerRadius radius) { }
        public void PushTranslate(float x, float y) { }
        public void PushOpacity(float opacity) { }
        public void Pop() { }
        public ICanvasDrawingContext CreateCanvasContext(Rect bounds) => new StubCanvasDrawingContext(bounds);
        public void BeginFrame(float width, float height, float scaleFactor, Color? clearColor = null) { }
        public void EndFrame() { }
    }

    private sealed class StubCanvasDrawingContext(Rect bounds) : ICanvasDrawingContext
    {
        public Rect Bounds => bounds;
        public void DrawRect(Rect rect, Color fill) { }
        public void DrawLine(Point from, Point to, Color color, float strokeWidth) { }
        public void DrawCircle(Point center, float radius, Color fill) { }
        public void DrawCircleStroke(Point center, float radius, Color color, float strokeWidth) { }
        public void DrawRoundedRect(Rect rect, CornerRadius radius, Color fill) { }
        public void DrawRoundedRectStroke(Rect rect, CornerRadius radius, Color color, float strokeWidth) { }
        public void DrawText(string text, Point position, Color color, float fontSize) { }
    }
}
