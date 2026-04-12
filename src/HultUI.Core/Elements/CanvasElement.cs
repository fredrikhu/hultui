using HultUI.Core.Primitives;
using HultUI.Core.Rendering;

namespace HultUI.Core.Elements;

public class CanvasElement : BoxElement
{
    public Action<ICanvasDrawingContext>? Draw { get; set; }

    public static CanvasElement New(Action<ICanvasDrawingContext>? draw = null) => new() { Draw = draw };

    public override void Render(IRenderContext ctx)
    {
        // Render background/border from BoxElement
        base.Render(ctx);

        if (Draw is null) return;

        var drawCtx = ctx.CreateCanvasContext(Layout.AbsoluteContentBox);
        Draw(drawCtx);
    }

    internal override void PatchFrom(Element source)
    {
        base.PatchFrom(source);
        if (source is CanvasElement canvas)
        {
            Draw = canvas.Draw;
        }
    }
}
