using HultUI.Core.Accessibility;
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
    public void PatchFrom_CopiesTheTextSource()
    {
        var canvas1 = CanvasElement.New();
        var source = new FixedTextSource();
        var canvas2 = CanvasElement.New();
        canvas2.TextSource = source;

        canvas1.PatchFrom(canvas2);

        Assert.Same(source, canvas1.TextSource);
    }

    [Fact]
    public void DrawTextLine_ByDefault_DrawsTheTextInTheStylesColourAndSize()
    {
        var ctx = new StubCanvasDrawingContext(new Rect(0, 0, 100, 100));
        var style = TextMeasure.StyleFor(13f) with { TextColor = new Color(10, 20, 30) };

        ((ICanvasDrawingContext)ctx).DrawTextLine("hello", new Point(4, 8), style, [new TextSpan(0, 2, Color.White)]);
        float empty = ((ICanvasDrawingContext)ctx).DrawTextLine("", new Point(4, 8), style);

        var drawn = Assert.Single(ctx.Texts);
        Assert.Equal(("hello", new Point(4, 8), new Color(10, 20, 30), 13f), drawn);
        Assert.Equal(0f, empty);
    }

    private sealed class FixedTextSource : IAccessibleTextSource
    {
        public AccessibleText? AccessibleText => new("text");
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
        public Size MeasureText(string text, Core.Styling.ResolvedInheritedStyle style) => new(text.Length * style.FontSize * 0.6f, style.FontSize);
        public IReadOnlyList<TextLineBox> LayoutTextLines(string text, Core.Styling.ResolvedInheritedStyle style, float maxWidth, int? maxLines) =>
            string.IsNullOrEmpty(text) ? [] : [new TextLineBox(0, text.Length, 0, style.FontSize, text.Length * style.FontSize * 0.6f)];
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
        public void DrawBezier(Point from, Point to, Color color, float strokeWidth) { }
        public void DrawCircle(Point center, float radius, Color fill) { }
        public void DrawCircleStroke(Point center, float radius, Color color, float strokeWidth) { }
        public void DrawRoundedRect(Rect rect, CornerRadius radius, Color fill) { }
        public void DrawRoundedRectStroke(Rect rect, CornerRadius radius, Color color, float strokeWidth) { }
        public readonly List<(string Text, Point Position, Color Color, float FontSize)> Texts = [];
        public void DrawText(string text, Point position, Color color, float fontSize) =>
            Texts.Add((text, position, color, fontSize));
    }
}
