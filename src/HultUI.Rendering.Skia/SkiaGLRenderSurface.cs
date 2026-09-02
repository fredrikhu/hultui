using HultUI.Core.Rendering;
using SkiaSharp;

namespace HultUI.Rendering.Skia;

public sealed class SkiaGLRenderSurface : IRenderSurface
{
    private readonly GRGlInterface _glInterface;
    private readonly GRContext _grContext;
    private readonly SkiaRenderContext _renderContext = new();
    private readonly SkiaTextServices _text;

    private GRBackendRenderTarget? _renderTarget;
    private SKSurface? _surface;
    private int _width;
    private int _height;
    private float _scaleFactor = 1f;

    public IRenderContext RenderContext => _renderContext;
    public IMeasureContext MeasureContext => _text.MeasureContext;

    /// <param name="glGetProcAddress">Resolver for GL entry points, from the windowing layer.</param>
    /// <param name="text">Text pipeline to adopt instead of building one. Nothing in
    /// it needs GL, so a caller that started the driver loading on another thread can
    /// have built (and measured with) it already — see <see cref="SkiaTextServices"/>.
    /// Null builds a fresh one, which is what every caller did before.</param>
    public SkiaGLRenderSurface(Func<string, IntPtr> glGetProcAddress, SkiaTextServices? text = null)
    {
        _glInterface = GRGlInterface.CreateOpenGl(name => glGetProcAddress(name))
            ?? throw new InvalidOperationException("Failed to create SkiaSharp GL interface");
        _grContext = GRContext.CreateGl(_glInterface)
            ?? throw new InvalidOperationException("Failed to create SkiaSharp GR context");

        _text = text ?? new SkiaTextServices();
        _renderContext.SetPipeline(_text.Pipeline);
    }

    public void Resize(int widthPx, int heightPx, float scaleFactor)
    {
        if (widthPx == _width && heightPx == _height && Math.Abs(scaleFactor - _scaleFactor) < 0.001f)
            return;

        _width = widthPx;
        _height = heightPx;
        _scaleFactor = scaleFactor;

        _surface?.Dispose();
        _renderTarget?.Dispose();

        // Target the default framebuffer (FBO 0)
        var fbInfo = new GRGlFramebufferInfo(0, 0x8058); // GL_RGBA8
        _renderTarget = new GRBackendRenderTarget(widthPx, heightPx, 0, 8, fbInfo);
        _surface = SKSurface.Create(_grContext, _renderTarget, GRSurfaceOrigin.BottomLeft, SKColorType.Rgba8888)
            ?? throw new InvalidOperationException($"Failed to create SkiaSharp surface ({widthPx}x{heightPx})");

        // Tell Skia its cached view state (viewport, scissor) is stale — it
        // will re-issue glViewport on the next draw using its own loader,
        // sized from the GRBackendRenderTarget.
        _grContext.ResetContext(GRGlBackendState.View);

        _renderContext.SetCanvas(_surface.Canvas);
    }

    public void Present()
    {
        _surface?.Canvas.Flush();
        _grContext.Flush();
    }

    public void Dispose()
    {
        _renderContext.Dispose();
        _surface?.Dispose();
        _renderTarget?.Dispose();
        _grContext.Dispose();
        _glInterface.Dispose();
    }
}
