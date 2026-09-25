namespace HultUI.Core.Rendering;

public interface IRenderSurface : IDisposable
{
    IRenderContext RenderContext { get; }
    IMeasureContext MeasureContext { get; }

    void Resize(int widthPx, int heightPx, float scaleFactor);

    /// <summary>
    /// Opens a frame, returning false when there is nothing to draw into and the frame
    /// should be skipped. Default true, because a GL frame has no beginning — Skia draws
    /// into framebuffer 0 whenever it likes. A Vulkan frame has to acquire one of several
    /// rotating swapchain images first, and that acquire can legitimately fail mid-resize
    /// or on a minimised window, which is not an error.
    /// </summary>
    bool BeginFrame() => true;

    void Present();
}
