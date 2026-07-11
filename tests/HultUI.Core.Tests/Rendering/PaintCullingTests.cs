using System.Collections.Generic;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using Xunit;

namespace HultUI.Core.Tests.Rendering;

/// <summary>
/// Verifies the renderer culls off-screen content inside a scroll view: rows
/// scrolled out of the viewport are not drawn, while the ones actually visible
/// still are. Uses a recording render context that captures every DrawText.
/// </summary>
public class PaintCullingTests
{
    private const int RowCount = 200;
    private const float RowHeight = 14f; // StubMeasureContext line height
    private const float ViewportHeight = 100f;

    private static (ScrollViewElement scroll, RecordingRenderContext ctx) BuildList()
    {
        var rows = new Element[RowCount];
        for (int i = 0; i < RowCount; i++)
            // FlexShrink(0) so the column keeps each row's natural height and
            // overflows the viewport (scrolls) instead of squishing them to fit.
            rows[i] = Text.New($"row{i}").FlexShrink(0);

        var column = Flex.Column(gap: 0).FlexShrink(0);
        column.ReplaceChildren(rows);

        var scroll = ScrollView.New().Width(Dimension.Px(300)).Height(Dimension.Px((int)ViewportHeight));
        scroll.ReplaceChildren(column);

        StyleResolver.Resolve(scroll, Theme.Light);
        new LayoutEngine(new StubMeasureContext()).PerformLayout(scroll, 300, ViewportHeight);
        // Establish the scrollable content size so ScrollOffsetY isn't clamped to
        // zero before the first render (the renderer normally does this per frame).
        scroll.UpdateContentSize();

        return (scroll, new RecordingRenderContext());
    }

    [Fact]
    public void UnscrolledList_DrawsOnlyVisibleRows()
    {
        var (scroll, ctx) = BuildList();

        ElementRenderer.RenderTree(scroll, ctx);

        Assert.Contains("row0", ctx.DrawnTexts);
        Assert.Contains("row5", ctx.DrawnTexts);
        // A row far below the ~7-row viewport must be culled.
        Assert.DoesNotContain("row100", ctx.DrawnTexts);
        // Nowhere near all 200 rows were drawn.
        Assert.True(ctx.DrawnTexts.Count < 20,
            $"expected only the visible rows, but drew {ctx.DrawnTexts.Count}");
    }

    [Fact]
    public void ScrolledList_DrawsTheNewlyVisibleRowsAndCullsTheRest()
    {
        var (scroll, ctx) = BuildList();

        // Scroll so row 100 sits at the top of the viewport.
        scroll.ScrollOffsetY = 100 * RowHeight;
        ElementRenderer.RenderTree(scroll, ctx);

        Assert.Contains("row100", ctx.DrawnTexts);
        Assert.DoesNotContain("row0", ctx.DrawnTexts);
        Assert.DoesNotContain("row199", ctx.DrawnTexts);
    }

    private sealed class RecordingRenderContext : IRenderContext
    {
        public List<string> DrawnTexts { get; } = [];

        public void DrawText(string text, Rect bounds, ResolvedInheritedStyle style, TextOverflow overflow, int? maxLines)
            => DrawnTexts.Add(text);

        public void DrawRect(Rect rect, Color fill) { }
        public void DrawRoundedRect(Rect rect, CornerRadius radius, Color fill, Color stroke, Thickness strokeWidth) { }
        public void DrawImage(IImageSource source, Rect destRect, ImageFit fit) { }
        public void DrawLine(Point from, Point to, Color color, float strokeWidth) { }
        public Size MeasureText(string text, ResolvedInheritedStyle style) => new(text.Length * 7f, RowHeight);
        public IReadOnlyList<TextLineBox> LayoutTextLines(string text, ResolvedInheritedStyle style, float maxWidth, int? maxLines) =>
            string.IsNullOrEmpty(text) ? [] : [new TextLineBox(0, text.Length, 0, RowHeight, text.Length * 7f)];
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
