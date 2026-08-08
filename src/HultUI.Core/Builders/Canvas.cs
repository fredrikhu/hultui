using HultUI.Core.Elements;
using HultUI.Core.Rendering;

namespace HultUI.Core.Builders;

public static class Canvas
{
    public static CanvasElement New(Action<ICanvasDrawingContext> draw, [OriginFile] string? file = null, [OriginLine] int line = 0) =>
        CanvasElement.New(draw, file, line);
    public static CanvasElement New([OriginFile] string? file = null, [OriginLine] int line = 0) => CanvasElement.New(null, file, line);
}
