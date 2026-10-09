using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using Xunit;

namespace HultUI.Rendering.Skia.Tests;

/// <summary>
/// Shadows and gradients keep their native objects between frames. Each blur was a new
/// <c>SKMaskFilter</c> and each gradient a new <c>SKShader</c> plus two arrays, made and disposed
/// per draw: 104 B a shadow and 168 B a gradient on every frame (plan 17, round 2), the last
/// steady-frame bytes in the apps — a popover's shadow, the scrims over Aperture's artwork. The
/// kept objects must still draw exactly what each draw asks for, and must not pile up.
/// </summary>
public class PaintEffectCacheTests
{
    private const int Frames = 100;
    private const int Width = 300, Height = 200;

    private static readonly Color Indigo = Color.FromHex("#6366F1");
    private static readonly Color Violet = Color.FromHex("#8B5CF6");

    private static SkiaRasterRenderSurface Surface()
    {
        var surface = new SkiaRasterRenderSurface();
        surface.Resize(Width, Height, 1f);
        return surface;
    }

    private static void Frame(SkiaRasterRenderSurface surface, Action<IRenderContext> draw)
    {
        var ctx = surface.RenderContext;
        ctx.BeginFrame(Width, Height, 1f, Color.White);
        draw(ctx);
        ctx.EndFrame();
    }

    /// <summary>Bytes this thread allocated per frame of <paramref name="draw"/> after two warm-up
    /// frames. The frame's number goes to the draw, so it can hand over a new object each time.</summary>
    private static double AllocatedPerFrame(SkiaRasterRenderSurface surface, Action<IRenderContext, int> draw)
    {
        int frame = 0;
        Action<IRenderContext> drawFrame = ctx => draw(ctx, frame++);
        void One() => Frame(surface, drawFrame);
        One();
        One();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < Frames; i++) One();
        return (GC.GetAllocatedBytesForCurrentThread() - before) / (double)Frames;
    }

    private static byte[] Pixels(SkiaRasterRenderSurface surface)
    {
        surface.Present();
        return surface.ReadPixels(out _, out _, out _);
    }

    private static SkiaRenderContext Context(SkiaRasterRenderSurface surface) => (SkiaRenderContext)surface.RenderContext;

    [Fact]
    public void Shadows_AllocateNothingPerFrame()
    {
        using var surface = Surface();
        double perFrame = AllocatedPerFrame(surface, (ctx, _) =>
        {
            ctx.DrawBoxShadow(new Rect(20, 20, 112, 72), new CornerRadius(10), new BoxShadow(0, 4, 14, new Color(15, 23, 42, 55)));
            ctx.DrawBoxShadow(new Rect(150, 20, 112, 72), default, new BoxShadow(0, 2, 6, 1, new Color(0, 0, 0, 80)));
            ctx.DrawBoxShadow(new Rect(20, 110, 112, 72), new CornerRadius(4), new BoxShadow(2, 2, 0, 3, new Color(0, 0, 0, 40)));
        });

        Assert.True(perFrame < 1, $"drawing three box shadows allocated {perFrame:0.#} B per frame");
    }

    /// <summary>A new but equal <see cref="Gradient"/> every frame, as an app that builds its scrim
    /// in Build hands over on every rebuild: the shader is found by what it draws.</summary>
    [Fact]
    public void Gradients_AllocateNothingPerFrame_EvenWhenRebuiltEachFrame()
    {
        var linear = new Gradient[Frames + 2];
        var vertical = new Gradient[Frames + 2];
        var radial = new Gradient[Frames + 2];
        for (int i = 0; i < linear.Length; i++)
        {
            linear[i] = Gradient.Linear(135f, Indigo, Violet);
            vertical[i] = Gradient.Vertical(new Color(10, 10, 10, 25), new Color(10, 10, 10, 247));
            radial[i] = Gradient.Radial(new GradientStop(0f, Color.White), new GradientStop(0.4f, Indigo), new GradientStop(1f, Violet));
        }

        using var surface = Surface();
        double perFrame = AllocatedPerFrame(surface, (ctx, frame) =>
        {
            ctx.DrawGradientRect(new Rect(20, 20, 112, 72), new CornerRadius(10), linear[frame]);
            ctx.DrawGradientRect(new Rect(150, 20, 112, 72), default, vertical[frame]);
            ctx.DrawGradientRect(new Rect(20.4f, 110.6f, 112, 72), new CornerRadius(6), radial[frame]);
        });

        Assert.True(perFrame < 1, $"drawing three gradients allocated {perFrame:0.#} B per frame");
    }

    public static TheoryData<string> GradientChanges => new() { "rect", "stops", "offsets", "angle", "kind", "stop count" };

    /// <summary>A gradient drawn after other gradients were kept looks exactly like the same
    /// gradient drawn on a fresh surface. Each case first draws one that differs from it in one
    /// thing only, at the same place, so a kept shader found by too little would be reused.</summary>
    [Theory]
    [MemberData(nameof(GradientChanges))]
    public void AKeptGradientIsNeverReusedForADifferentOne(string change)
    {
        var rect = new Rect(40, 30, 200, 120);
        var target = Gradient.Linear(change == "kind" ? 0f : 90f, Indigo, Violet);
        (Rect at, Gradient gradient) before = change switch
        {
            "rect" => (new Rect(40, 30, 120, 60), Gradient.Linear(90f, Indigo, Violet)),
            "stops" => (rect, Gradient.Linear(90f, Indigo, Color.Black)),
            "offsets" => (rect, Gradient.Linear(90f, new GradientStop(0.3f, Indigo), new GradientStop(1f, Violet))),
            "angle" => (rect, Gradient.Linear(0f, Indigo, Violet)),
            // Radial gradients carry angle 0: the same angle as this case's target.
            "kind" => (rect, Gradient.Radial(Indigo, Violet)),
            "stop count" => (rect, Gradient.Linear(90f, new GradientStop(0f, Indigo), new GradientStop(0.5f, Color.Black), new GradientStop(1f, Violet))),
            _ => throw new ArgumentOutOfRangeException(nameof(change)),
        };

        using var fresh = Surface();
        Frame(fresh, ctx => ctx.DrawGradientRect(rect, new CornerRadius(8), target));
        var expected = Pixels(fresh);

        using var kept = Surface();
        Frame(kept, ctx => ctx.DrawGradientRect(before.at, new CornerRadius(8), before.gradient));
        Frame(kept, ctx => ctx.DrawGradientRect(rect, new CornerRadius(8), target));

        Assert.Equal(expected, Pixels(kept));
    }

    /// <summary>A stop array the app changes after the first draw changes what the next draw shows.</summary>
    [Fact]
    public void AGradientWhoseStopsChange_DrawsTheNewStops()
    {
        var rect = new Rect(40, 30, 200, 120);
        var stops = new[] { new GradientStop(0f, Indigo), new GradientStop(1f, Violet) };
        var gradient = Gradient.Linear(45f, stops);

        using var fresh = Surface();
        Frame(fresh, ctx => ctx.DrawGradientRect(rect, default, Gradient.Linear(45f, Indigo, Color.Black)));
        var expected = Pixels(fresh);

        using var kept = Surface();
        Frame(kept, ctx => ctx.DrawGradientRect(rect, default, gradient));
        stops[1] = new GradientStop(1f, Color.Black);
        Frame(kept, ctx => ctx.DrawGradientRect(rect, default, gradient));

        Assert.Equal(expected, Pixels(kept));
    }

    [Fact]
    public void AKeptBlurIsNeverReusedForADifferentOne()
    {
        var box = new Rect(80, 50, 120, 80);
        var target = new BoxShadow(0, 4, 24, new Color(0, 0, 0, 160));

        using var fresh = Surface();
        Frame(fresh, ctx => ctx.DrawBoxShadow(box, new CornerRadius(8), target));
        var expected = Pixels(fresh);

        using var kept = Surface();
        Frame(kept, ctx => ctx.DrawBoxShadow(box, new CornerRadius(8), target with { Blur = 6 }));
        Frame(kept, ctx => ctx.DrawBoxShadow(box, new CornerRadius(8), target));

        Assert.Equal(expected, Pixels(kept));
    }

    /// <summary>A gradient box that moves every frame, or a blur that animates, makes a new object
    /// every frame; the maps stay within their bounds and are emptied with the context.</summary>
    [Fact]
    public void TheKeptObjectsStayBounded_AndGoWithTheContext()
    {
        var surface = Surface();
        var ctx = Context(surface);
        var gradient = Gradient.Horizontal(Indigo, Violet);
        int most = 0, mostBlurs = 0;
        for (int i = 0; i < SkiaRenderContext.GradientShaderCacheMaxSize * 2 + 10; i++)
        {
            Frame(surface, c =>
            {
                c.DrawGradientRect(new Rect(i % 50, i / 50, 100, 40), default, gradient);
                c.DrawBoxShadow(new Rect(10, 10, 50, 50), default, new BoxShadow(0, 0, 1 + i * 0.5f, Color.Black));
            });
            most = Math.Max(most, ctx.CachedGradientShaderCount);
            mostBlurs = Math.Max(mostBlurs, ctx.CachedBlurFilterCount);
        }

        Assert.Equal(SkiaRenderContext.GradientShaderCacheMaxSize, most);
        Assert.Equal(SkiaRenderContext.BlurFilterCacheMaxSize, mostBlurs);
        Assert.InRange(ctx.CachedGradientShaderCount, 1, SkiaRenderContext.GradientShaderCacheMaxSize);
        Assert.InRange(ctx.CachedBlurFilterCount, 1, SkiaRenderContext.BlurFilterCacheMaxSize);

        surface.Dispose();
        Assert.Equal(0, ctx.CachedGradientShaderCount);
        Assert.Equal(0, ctx.CachedBlurFilterCount);
    }
}
