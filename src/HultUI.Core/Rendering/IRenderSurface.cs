namespace HultUI.Core.Rendering;

public interface IRenderSurface : IDisposable
{
    IRenderContext RenderContext { get; }
    IMeasureContext MeasureContext { get; }

    void Resize(int widthPx, int heightPx, float scaleFactor);
    void Present();
}
