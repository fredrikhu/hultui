using HultUI.Core.Primitives;
using HultUI.Core.Rendering;

namespace HultUI.Core.Elements;

public class CanvasElement : BoxElement
{
    /// <summary>Draws the canvas. It runs only in a frame that draws, only while the canvas's box
    /// meets the visible area of a clipping ancestor (a box of no size meets nothing), and not while
    /// it or an ancestor is at opacity 0, so it is no after-layout hook: use <c>.OnLayoutChanged</c>
    /// for work that follows the canvas's size.</summary>
    public Action<ICanvasDrawingContext>? Draw { get; set; }

    public static CanvasElement New(Action<ICanvasDrawingContext>? draw = null, [OriginFile] string? file = null, [OriginLine] int line = 0) =>
        new CanvasElement { Draw = draw }.WithOrigin(file, line);

    /// <summary>Unbounded: the callback draws wherever it likes, unclipped.</summary>
    public override Rect? PaintBounds(IRenderContext ctx) => null;

    /// <summary>The callback draws (<see cref="Draw"/> is no hook). Not a subclass: its
    /// <see cref="Render"/> may do more.</summary>
    internal override bool RenderOnlyPaints => GetType() == typeof(CanvasElement);

    public override void Render(IRenderContext ctx)
    {
        // Render background/border from BoxElement
        base.Render(ctx);

        if (Draw is null) return;

        var drawCtx = ctx.CreateCanvasContext(Layout.AbsoluteContentBox);
        try
        {
            Draw(drawCtx);
        }
        finally
        {
            // A clip or transform the callback left open would otherwise apply to
            // everything painted after this element.
            drawCtx.PopAll();
        }
    }

    // What it copies below is paint: the callback runs when the canvas is drawn, and it
    // measures as the box it is.
    private protected override bool PatchMarksOwnLayout => GetType() == typeof(CanvasElement);

    internal override void PatchFrom(Element source)
    {
        base.PatchFrom(source);
        if (source is CanvasElement canvas)
        {
            Draw = canvas.Draw;
        }
    }
}
