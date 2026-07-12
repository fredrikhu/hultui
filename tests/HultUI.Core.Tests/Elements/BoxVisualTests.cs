using System.Collections.Generic;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using Xunit;

namespace HultUI.Core.Tests.Elements;

/// <summary>
/// Covers the reconciliation wiring for the box visual effects (shadow, gradient):
/// the fluent builders populate the element, and PatchFrom carries them onto the
/// live element so a rebuilt tree keeps its shadows/gradient.
/// </summary>
public class BoxVisualTests
{
    [Fact]
    public void BoxShadow_Fluent_AppendsShadows()
    {
        var box = Box.New()
            .BoxShadow(0, 2, 8, new Color(0, 0, 0, 60))
            .BoxShadow(0, 8, 24, 2f, new Color(0, 0, 0, 40));

        Assert.NotNull(box.BoxShadows);
        Assert.Equal(2, box.BoxShadows!.Count);
        Assert.Equal(8f, box.BoxShadows[0].Blur);
        Assert.Equal(2f, box.BoxShadows[1].Spread);
    }

    [Fact]
    public void BackgroundGradient_Fluent_SetsGradient()
    {
        var box = Box.New().Background(Gradient.Vertical(Color.White, Color.Black));

        Assert.NotNull(box.BackgroundGradient);
        Assert.Equal(GradientKind.Linear, box.BackgroundGradient!.Kind);
        Assert.Equal(90f, box.BackgroundGradient.AngleDegrees);
        Assert.Equal(2, box.BackgroundGradient.Stops.Count);
    }

    [Fact]
    public void SolidBackground_Overload_LeavesGradientNull()
    {
        var box = Box.New().Background(Color.Red);
        Assert.Null(box.BackgroundGradient);
        Assert.Equal(Color.Red, box.BackgroundColor);
    }

    [Fact]
    public void PatchFrom_CopiesShadowsAndGradient()
    {
        var live = Box.New();
        var incoming = Box.New()
            .BoxShadow(0, 4, 12, new Color(0, 0, 0, 80))
            .Background(Gradient.Horizontal(Color.Red, Color.Blue));

        live.PatchFrom(incoming);

        Assert.Same(incoming.BoxShadows, live.BoxShadows);
        Assert.Same(incoming.BackgroundGradient, live.BackgroundGradient);
    }

    [Fact]
    public void Border_Fluent_SetsWidthsColorAndStyle()
    {
        var box = Box.New().Border(new Thickness(0, 0, 2, 0), Color.Red, BorderStyle.Dashed);

        Assert.Equal(new Thickness(0, 0, 2, 0), box.Box.BorderWidth);
        Assert.Equal(Color.Red, box.BorderColor);
        Assert.Equal(BorderStyle.Dashed, box.BorderStyle);
    }

    [Fact]
    public void PatchFrom_CopiesBorderStyle()
    {
        var live = Box.New();
        var incoming = Box.New().BorderStyle(BorderStyle.Dotted);
        live.PatchFrom(incoming);
        Assert.Equal(BorderStyle.Dotted, live.BorderStyle);
    }

    [Fact]
    public void Render_PerSideBorder_RoutesThroughDrawBorder()
    {
        // A bottom-only border has non-uniform widths, so it must use DrawBorder
        // (exact per-edge) rather than the averaging combined path.
        var box = LayoutBox(Box.New().Border(new Thickness(0, 0, 2, 0), Color.Black));
        var ctx = new RecordingRenderContext();

        box.Render(ctx);

        Assert.Equal(1, ctx.DrawBorderCalls);
    }

    [Fact]
    public void Render_UniformSolidBorder_UsesCombinedPath()
    {
        // Uniform solid border keeps the existing combined DrawRoundedRect call.
        var box = LayoutBox(Box.New().BorderWidth(1f).BorderColor(Color.Black).Background(Color.White));
        var ctx = new RecordingRenderContext();

        box.Render(ctx);

        Assert.Equal(0, ctx.DrawBorderCalls);
        Assert.True(ctx.DrawRoundedRectCalls >= 1);
    }

    private static BoxElement LayoutBox(BoxElement box)
    {
        box.Width(120).Height(48);
        StyleResolver.Resolve(box, Theme.Light);
        new LayoutEngine(new StubMeasureContext()).PerformLayout(box, 120, 48);
        return box;
    }

    private sealed class RecordingRenderContext : IRenderContext
    {
        public int DrawBorderCalls { get; private set; }
        public int DrawRoundedRectCalls { get; private set; }

        public void DrawBorder(Rect box, CornerRadius radius, Color color, Thickness widths, BorderStyle style)
            => DrawBorderCalls++;
        public void DrawRoundedRect(Rect rect, CornerRadius radius, Color fill, Color stroke, Thickness strokeWidth)
            => DrawRoundedRectCalls++;

        public void DrawRect(Rect rect, Color fill) { }
        public void DrawText(string text, Rect bounds, ResolvedInheritedStyle style, TextOverflow overflow, int? maxLines) { }
        public void DrawImage(IImageSource source, Rect destRect, ImageFit fit) { }
        public void DrawLine(Point from, Point to, Color color, float strokeWidth) { }
        public Size MeasureText(string text, ResolvedInheritedStyle style) => Size.Zero;
        public IReadOnlyList<TextLineBox> LayoutTextLines(string text, ResolvedInheritedStyle style, float maxWidth, int? maxLines) => [];
        public void PushClip(Rect clipRect) { }
        public void PushRoundedClip(Rect clipRect, CornerRadius radius) { }
        public void PushTranslate(float x, float y) { }
        public void PushOpacity(float opacity) { }
        public void Pop() { }
        public ICanvasDrawingContext CreateCanvasContext(Rect bounds) => throw new System.NotSupportedException();
        public void BeginFrame(float width, float height, float scaleFactor, Color? clearColor = null) { }
        public void EndFrame() { }
    }
}
