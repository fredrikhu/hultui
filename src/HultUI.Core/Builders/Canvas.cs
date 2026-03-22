using HultUI.Core.Elements;
using HultUI.Core.Rendering;

namespace HultUI.Core.Builders;

public static class Canvas
{
    public static CanvasElement New(Action<ICanvasDrawingContext> draw) => CanvasElement.New(draw);
    public static CanvasElement New() => CanvasElement.New(null);
}
