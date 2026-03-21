using HultUI.Core.Rendering;
using HultUI.Text;
using SkiaSharp;

namespace HultUI.Rendering.Skia;

public sealed class SkiaGLRenderSurface : IRenderSurface
{
    private readonly GRGlInterface _glInterface;
    private readonly GRContext _grContext;
    private readonly SkiaRenderContext _renderContext = new();
    private readonly SkiaMeasureContext _measureContext;
    private readonly TextPipeline _pipeline;

    private GRBackendRenderTarget? _renderTarget;
    private SKSurface? _surface;
    private int _width;
    private int _height;
    private float _scaleFactor = 1f;

    public IRenderContext RenderContext => _renderContext;
    public IMeasureContext MeasureContext => _measureContext;

    public SkiaGLRenderSurface(Func<string, IntPtr> glGetProcAddress)
    {
        _glInterface = GRGlInterface.CreateOpenGl(name => glGetProcAddress(name))
            ?? throw new InvalidOperationException("Failed to create SkiaSharp GL interface");
        _grContext = GRContext.CreateGl(_glInterface)
            ?? throw new InvalidOperationException("Failed to create SkiaSharp GR context");

        var fontCollection = new SkiaFontCollection();
        var textShaper = new SkiaTextShaper();
        _pipeline = new TextPipeline(fontCollection, textShaper);
        _measureContext = new SkiaMeasureContext(_pipeline);
        _renderContext.SetPipeline(_pipeline);
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

        _renderContext.SetCanvas(_surface.Canvas);
    }

    public void Present()
    {
        _surface?.Canvas.Flush();
        _grContext.Flush();
    }

    public void Dispose()
    {
        _surface?.Dispose();
        _renderTarget?.Dispose();
        _grContext.Dispose();
        _glInterface.Dispose();
    }
}
