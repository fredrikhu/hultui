using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Testing;
using SkiaSharp;
using Xunit;

namespace HultUI.Rendering.Skia.Tests;

/// <summary>
/// Shadows, gradients and dashed borders keep their native objects between frames. Each blur was
/// a new <c>SKMaskFilter</c> and each gradient a new <c>SKShader</c> plus two arrays, made and
/// disposed per draw: 104 B a shadow and 168 B a gradient on every frame (plan 17, round 2), the
/// last steady-frame bytes in the apps — a popover's shadow, the scrims over Aperture's artwork.
/// A dashed or dotted border was a new <c>SKPathEffect</c> and its interval array, 136 B a border
/// (the Graphite spike's mixed scene; <see cref="MixedDrawAllocationTests"/>). The kept objects
/// must still draw exactly what each draw asks for, and must not pile up.
/// </summary>
public class PaintEffectCacheTests
{
    private const int Frames = 100;
    private const int Width = 300, Height = 200;

    private static readonly Color Indigo = Color.FromHex("#6366F1");
    private static readonly Color Violet = Color.FromHex("#8B5CF6");

    private static SkiaRasterRenderSurface Surface(float scale = 1f)
    {
        var surface = new SkiaRasterRenderSurface();
        surface.Resize((int)MathF.Ceiling(Width * scale), (int)MathF.Ceiling(Height * scale), scale);
        return surface;
    }

    private static void Frame(SkiaRasterRenderSurface surface, Action<IRenderContext> draw) => Frame(surface, 1f, draw);

    private static void Frame(SkiaRasterRenderSurface surface, float scale, Action<IRenderContext> draw)
    {
        var ctx = surface.RenderContext;
        ctx.BeginFrame(Width, Height, scale, Color.White);
        draw(ctx);
        ctx.EndFrame();
    }

    /// <summary>Bytes this thread allocated per frame of <paramref name="draw"/> after two warm-up
    /// frames, the least of a few rounds (<see cref="Allocations"/>). The frame's number goes to
    /// the draw, so it can hand over a new object each time: up to <see cref="DrawnFrames"/>.</summary>
    private static double AllocatedPerFrame(SkiaRasterRenderSurface surface, Action<IRenderContext, int> draw)
    {
        int frame = 0;
        Action<IRenderContext> drawFrame = ctx => draw(ctx, frame++);
        void One() => Frame(surface, drawFrame);
        One();
        One();
        return Allocations.PerCall(Frames, One);
    }

    /// <summary>The most frames <see cref="AllocatedPerFrame"/> draws: two to warm up, and every
    /// round it may measure.</summary>
    private const int DrawnFrames = 2 + Frames * Allocations.MaxRounds;

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
        var linear = new Gradient[DrawnFrames];
        var vertical = new Gradient[DrawnFrames];
        var radial = new Gradient[DrawnFrames];
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

    /// <summary>A box of one size at a new place every frame, as Junction History's "Details"
    /// affordance follows the focused row: one shader, kept wherever the box goes, and nothing
    /// allocated. Kept by place, each new place made a shader (about 350 B and a native one).</summary>
    [Fact]
    public void AMovingGradient_KeepsOneShader_AndAllocatesNothingPerFrame()
    {
        var gradient = Gradient.Horizontal(new Color(99, 102, 241, 0), Indigo);
        using var surface = Surface();
        double perFrame = AllocatedPerFrame(surface, (ctx, frame) =>
            ctx.DrawGradientRect(new Rect(20 + frame % 100, 10 + frame / 100 % 6 * 26, 24, 26), default, gradient));

        Assert.True(perFrame < 1, $"a moving gradient allocated {perFrame:0.#} B per frame");
        Assert.Equal(1, Context(surface).CachedGradientShaderCount);
    }

    /// <summary>A gradient drawn with the shader a box of its size made somewhere else, the canvas
    /// moved by the difference, draws what a shader made in its own place draws: the same edges, and
    /// colours at most one level apart (the two compose their float matrices in a different order).
    /// Asked for at fractional places; at 1.5 the snapped edges are whole device pixels.</summary>
    [Theory]
    [InlineData(1f)]
    [InlineData(1.5f)]
    public void AMovedGradient_DrawsWhatOneMadeInPlaceDraws(float scale)
    {
        var first = new Rect(10.3f, 12.2f, 150, 90);
        var moved = new Rect(98.2f, 80.3f, 150, 90);
        foreach (var gradient in new[]
                 {
                     Gradient.Vertical(new Color(10, 10, 10, 25), new Color(10, 10, 10, 247)),
                     Gradient.Linear(135f, Indigo, Violet),
                     Gradient.Radial(new GradientStop(0f, Color.White), new GradientStop(0.4f, Indigo), new GradientStop(1f, Violet)),
                 })
        {
            using var fresh = Surface(scale);
            Frame(fresh, scale, ctx => ctx.DrawGradientRect(moved, new CornerRadius(8), gradient));
            var expected = Pixels(fresh);

            using var kept = Surface(scale);
            Frame(kept, scale, ctx => ctx.DrawGradientRect(first, new CornerRadius(8), gradient));
            Frame(kept, scale, ctx => ctx.DrawGradientRect(moved, new CornerRadius(8), gradient));
            var actual = Pixels(kept);

            Assert.Equal(1, Context(kept).CachedGradientShaderCount);
            Assert.Equal(expected.Length, actual.Length);
            int worst = 0, off = 0;
            for (int i = 0; i < expected.Length; i++)
            {
                int d = Math.Abs(expected[i] - actual[i]);
                worst = Math.Max(worst, d);
                if (d > 0) off++;
            }
            Assert.True(worst <= 1, $"{gradient.Kind} {gradient.AngleDegrees}° at {scale}: {off} channels off, by up to {worst}");
        }
    }

    /// <summary>A gradient that stays where its shader was made draws exactly what a shader made over
    /// its box draws (the renderer's own construction, written out here): no move, so not one pixel
    /// rounds differently. A shader made at (0, 0) and moved to every box rounded 4,254 pixels of
    /// Aperture's Detail hero one level away at 1.5.</summary>
    [Theory]
    [InlineData(1f)]
    [InlineData(1.5f)]
    public void AGradientThatDoesNotMove_DrawsExactlyAShaderMadeInPlace(float scale)
    {
        // On the grid at both scales (whole device pixels at 1.5 too), so snapping leaves it as it is.
        var box = new Rect(64, 40, 220, 150);
        var skRect = new SKRect(box.X, box.Y, box.X + box.Width, box.Y + box.Height);
        foreach (var gradient in new[]
                 {
                     Gradient.Vertical(new Color(10, 10, 10, 25), new Color(10, 10, 10, 247)),
                     Gradient.Horizontal(new Color(10, 10, 10, 245), new Color(10, 10, 10, 0)),
                     Gradient.Linear(135f, Indigo, Violet),
                     Gradient.Radial(new GradientStop(0f, Color.White), new GradientStop(0.4f, Indigo), new GradientStop(1f, Violet)),
                 })
        {
            var colors = gradient.Stops.Select(s => new SKColor(s.Color.R, s.Color.G, s.Color.B, s.Color.A)).ToArray();
            var positions = gradient.Stops.Select(s => s.Offset).ToArray();
            SKShader shader;
            if (gradient.Kind == GradientKind.Linear)
            {
                float rad = gradient.AngleDegrees * (MathF.PI / 180f);
                float dx = MathF.Cos(rad), dy = MathF.Sin(rad);
                float half = MathF.Abs(dx) * (skRect.Width * 0.5f) + MathF.Abs(dy) * (skRect.Height * 0.5f);
                shader = SKShader.CreateLinearGradient(new SKPoint(skRect.MidX - dx * half, skRect.MidY - dy * half),
                    new SKPoint(skRect.MidX + dx * half, skRect.MidY + dy * half), colors, positions, SKShaderTileMode.Clamp);
            }
            else
            {
                shader = SKShader.CreateRadialGradient(new SKPoint(skRect.MidX, skRect.MidY),
                    MathF.Sqrt(skRect.Width * skRect.Width + skRect.Height * skRect.Height) * 0.5f,
                    colors, positions, SKShaderTileMode.Clamp);
            }

            using var reference = Surface(scale);
            using (shader)
            using (var paint = new SKPaint { IsAntialias = true, Shader = shader })
                Frame(reference, scale, ctx => Context(reference).Canvas.DrawRect(skRect, paint));
            var expected = Pixels(reference);

            using var drawn = Surface(scale);
            Frame(drawn, scale, ctx => ctx.DrawGradientRect(box, default, gradient));
            Frame(drawn, scale, ctx => ctx.DrawGradientRect(box, default, gradient));

            Assert.Equal(expected, Pixels(drawn));
        }
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

    /// <summary>A dashed or dotted border drawn after other dashes were kept looks exactly like the
    /// same border drawn on a fresh surface. Each case first draws one that differs from it in one
    /// thing only, at the same place: the style, or the width the intervals are made from.</summary>
    [Theory]
    [InlineData(BorderStyle.Dashed, BorderStyle.Dotted, 2f, 2f)]
    [InlineData(BorderStyle.Dotted, BorderStyle.Dashed, 2f, 2f)]
    [InlineData(BorderStyle.Dashed, BorderStyle.Dashed, 1f, 3f)]
    [InlineData(BorderStyle.Dotted, BorderStyle.Dotted, 3f, 1f)]
    [InlineData(BorderStyle.Dashed, BorderStyle.Solid, 2f, 2f)]
    public void AKeptDashIsNeverReusedForADifferentOne(BorderStyle style, BorderStyle beforeStyle, float width,
        float beforeWidth)
    {
        var box = new Rect(40, 30, 200, 120);
        void Draw(IRenderContext ctx, BorderStyle s, float w) =>
            ctx.DrawBorder(box, new CornerRadius(10), Indigo, new Thickness(w), s);

        using var fresh = Surface();
        Frame(fresh, ctx => Draw(ctx, style, width));
        var expected = Pixels(fresh);

        using var kept = Surface();
        Frame(kept, ctx => Draw(ctx, beforeStyle, beforeWidth));
        Frame(kept, ctx => Draw(ctx, style, width));

        Assert.Equal(expected, Pixels(kept));
    }

    /// <summary>A gradient box whose size changes every frame, a blur that animates, or a dashed
    /// border whose width animates, makes a new object every frame; the maps stay within their
    /// bounds and are emptied with the context.</summary>
    [Fact]
    public void TheKeptObjectsStayBounded_AndGoWithTheContext()
    {
        var surface = Surface();
        var ctx = Context(surface);
        var gradient = Gradient.Horizontal(Indigo, Violet);
        int most = 0, mostBlurs = 0, mostDashes = 0;
        for (int i = 0; i < SkiaRenderContext.GradientShaderCacheMaxSize * 2 + 10; i++)
        {
            Frame(surface, c =>
            {
                c.DrawGradientRect(new Rect(i % 50, i / 50, 100 + i % 150, 40 + i / 150), default, gradient);
                c.DrawBoxShadow(new Rect(10, 10, 50, 50), default, new BoxShadow(0, 0, 1 + i * 0.5f, Color.Black));
                c.DrawBorder(new Rect(10, 10, 280, 180), default, Color.Black, new Thickness(1 + i % 90),
                    i % 2 == 0 ? BorderStyle.Dashed : BorderStyle.Dotted);
            });
            most = Math.Max(most, ctx.CachedGradientShaderCount);
            mostBlurs = Math.Max(mostBlurs, ctx.CachedBlurFilterCount);
            mostDashes = Math.Max(mostDashes, ctx.CachedDashEffectCount);
        }

        Assert.Equal(SkiaRenderContext.GradientShaderCacheMaxSize, most);
        Assert.Equal(SkiaRenderContext.BlurFilterCacheMaxSize, mostBlurs);
        Assert.Equal(SkiaRenderContext.DashEffectCacheMaxSize, mostDashes);
        Assert.InRange(ctx.CachedGradientShaderCount, 1, SkiaRenderContext.GradientShaderCacheMaxSize);
        Assert.InRange(ctx.CachedBlurFilterCount, 1, SkiaRenderContext.BlurFilterCacheMaxSize);
        Assert.InRange(ctx.CachedDashEffectCount, 1, SkiaRenderContext.DashEffectCacheMaxSize);

        surface.Dispose();
        Assert.Equal(0, ctx.CachedGradientShaderCount);
        Assert.Equal(0, ctx.CachedBlurFilterCount);
        Assert.Equal(0, ctx.CachedDashEffectCount);
    }
}
