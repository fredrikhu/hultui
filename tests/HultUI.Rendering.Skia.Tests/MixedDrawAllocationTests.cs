using System.Numerics;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using HultUI.Testing;
using Xunit;
using Xunit.Abstractions;

namespace HultUI.Rendering.Skia.Tests;

/// <summary>
/// The draws that are not plain fills and text, each on its own, drawn again every frame:
/// gradients, a picture in a rounded clip, polygons, polylines, an opacity layer, a transform and
/// a dashed border. The Graphite spike's mixed scene (its report, GRAPHITE-SPIKE.md, is on the
/// unmerged spike branch) draws these 48 times a frame and allocated 1,092 B a frame on the
/// production Ganesh renderer (SkiaVulkanRenderSurface), where cards of rounded rects, borders,
/// shadows and text allocate nothing.
///
/// <para>Measured one call at a time (2026-11-18), every one was 0 B a frame but the dashed and
/// dotted borders: 136 B a border, a new <c>SKPathEffect</c> and its interval array per draw. The
/// scene's eight dashed borders were its 1,088 B. The dashes are kept now, like blurs and gradient
/// shaders (<see cref="PaintEffectCacheTests"/>). The spike's probe on the Vulkan surface then
/// read 14 B a frame over 600 frames, 1.4 over 6,000: a constant 8.4 KB while the scene's own
/// caches fill (its pictures and gradients change every 30 and 60 frames), none per frame.</para>
///
/// <para>Bound and measure as in <see cref="TextDrawAllocationTests"/>: under one byte a frame,
/// over the least of a few rounds (<see cref="Allocations"/>).</para>
/// </summary>
public class MixedDrawAllocationTests(ITestOutputHelper output)
{
    private const int Frames = 100;
    private const int Cards = 8;

    private static readonly Gradient[] Gradients =
    [
        Gradient.Linear(30f, new Color(255, 94, 98), new Color(255, 195, 113)),
        Gradient.Linear(120f, new GradientStop(0f, new Color(67, 206, 162)),
            new GradientStop(0.5f, new Color(24, 90, 157)), new GradientStop(1f, new Color(12, 30, 60))),
        Gradient.Vertical(new Color(142, 45, 226), new Color(74, 0, 224)),
        Gradient.Radial(new Color(255, 255, 255), new Color(0, 0, 0)),
    ];

    private static readonly CornerRadius Radius = new(12f);
    private static readonly Color Card = new(255, 255, 255);
    private static readonly Color Ink = new(30, 34, 40);

    private static readonly ResolvedInheritedStyle Label = new(
        FontFamily: "sans-serif", FontSize: 13f, FontWeight: FontWeight.Medium,
        TextColor: Ink, TextDirection: TextDirection.Ltr, Cursor: CursorType.Default);

    private static readonly string[] LayerLabels = [.. Enumerable.Range(0, Cards).Select(i => $"Layer {i}")];

    private static Rect Box(int i) => new(16f + (i % 4) * 200f, 16f + (i / 4) * 150f, 184f, 134f);

    private static SkiaImageSource Picture(int seed)
    {
        const int size = 64;
        var pixels = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int o = (y * size + x) * 4;
                pixels[o] = (byte)(x * (seed + 1));
                pixels[o + 1] = (byte)(y * 3 + seed * 40);
                pixels[o + 2] = (byte)((x ^ y) + seed * 17);
                pixels[o + 3] = 255;
            }
        return SkiaImageSource.FromPixels(pixels, size, size, size * 4, PixelFormat.Rgba8888);
    }

    /// <summary>Bytes this thread allocated per frame of <paramref name="draw"/>, once warm: the
    /// least of a few rounds. The frame number goes to the draw, so what moves per frame (an
    /// angle, a polyline's points) moves while it is measured.</summary>
    private double AllocatedPerFrame(string what, Action<IRenderContext, int> draw)
    {
        using var surface = new SkiaRasterRenderSurface();
        surface.Resize(820, 320, 1f);
        var ctx = surface.RenderContext;
        int step = 0;

        void Frame()
        {
            ctx.BeginFrame(820, 320, 1f, Color.White);
            draw(ctx, step++);
            ctx.EndFrame();
        }

        Frame();
        Frame();
        double perFrame = Allocations.PerCall(Frames, Frame);
        output.WriteLine($"{what}: {perFrame:0.##} B per frame");
        return perFrame;
    }

    [Fact]
    public void Gradients_AllocateNothingPerFrame()
    {
        double perFrame = AllocatedPerFrame("gradients", (ctx, step) =>
        {
            for (int i = 0; i < Cards; i++)
                ctx.DrawGradientRect(Box(i), i % 2 == 0 ? Radius : default, Gradients[(i + step / 7) % Gradients.Length]);
        });

        Assert.True(perFrame < 1, $"{Cards} gradient rects allocated {perFrame:0.#} B per frame");
    }

    [Fact]
    public void PicturesInRoundedClips_AllocateNothingPerFrame()
    {
        var pictures = new SkiaImageSource[3];
        for (int i = 0; i < pictures.Length; i++) pictures[i] = Picture(i);
        try
        {
            double perFrame = AllocatedPerFrame("pictures in rounded clips", (ctx, step) =>
            {
                for (int i = 0; i < Cards; i++)
                {
                    ctx.PushRoundedClip(Box(i), Radius);
                    ctx.DrawImage(pictures[(i + step / 5) % pictures.Length], Box(i), (ImageFit)(i % 5));
                    ctx.Pop();
                }
            });

            Assert.True(perFrame < 1, $"{Cards} clipped pictures allocated {perFrame:0.#} B per frame");
        }
        finally
        {
            foreach (var picture in pictures) picture.Dispose();
        }
    }

    [Fact]
    public void Polygons_AllocateNothingPerFrame()
    {
        double perFrame = AllocatedPerFrame("polygons", (ctx, step) =>
        {
            Span<Point> star = stackalloc Point[10];
            for (int i = 0; i < Cards; i++)
            {
                var box = Box(i);
                for (int p = 0; p < star.Length; p++)
                {
                    double a = Math.PI * 2 * p / star.Length + step * 0.01;
                    float r = p % 2 == 0 ? 50f : 22f;
                    star[p] = new Point(box.X + 92f + r * (float)Math.Cos(a), box.Y + 67f + r * (float)Math.Sin(a));
                }
                ctx.FillPolygon(star, Ink);
            }
        });

        Assert.True(perFrame < 1, $"{Cards} ten-point polygons allocated {perFrame:0.#} B per frame");
    }

    [Fact]
    public void Polylines_AllocateNothingPerFrame()
    {
        double perFrame = AllocatedPerFrame("polylines", (ctx, step) =>
        {
            Span<Point> line = stackalloc Point[48];
            for (int i = 0; i < Cards; i++)
            {
                var box = Box(i);
                for (int p = 0; p < line.Length; p++)
                    line[p] = new Point(box.X + 8f + p * 3.5f, box.Y + 67f + 40f * MathF.Sin((p + step) * 0.25f));
                ctx.DrawPolyline(line, new Color(16, 185, 129), 2f);
            }
        });

        Assert.True(perFrame < 1, $"{Cards} 48-point polylines allocated {perFrame:0.#} B per frame");
    }

    [Fact]
    public void OpacityLayers_AllocateNothingPerFrame()
    {
        double perFrame = AllocatedPerFrame("opacity layers", (ctx, _) =>
        {
            for (int i = 0; i < Cards; i++)
            {
                var box = Box(i);
                if (i % 2 == 0) ctx.PushOpacity(0.6f, box);
                else ctx.PushOpacity(0.4f);
                ctx.DrawRoundedRect(box, Radius, new Color(253, 230, 138), new Color(217, 119, 6), new Thickness(2f));
                ctx.DrawText(LayerLabels[i], new Rect(box.X + 12f, box.Y + 12f, 160f, 20f), Label, TextOverflow.Ellipsis, 1);
                ctx.Pop();
            }
        });

        Assert.True(perFrame < 1, $"{Cards} opacity layers allocated {perFrame:0.#} B per frame");
    }

    [Fact]
    public void Transforms_AllocateNothingPerFrame()
    {
        double perFrame = AllocatedPerFrame("transforms", (ctx, step) =>
        {
            for (int i = 0; i < Cards; i++)
            {
                var box = Box(i);
                ctx.PushTransform(Matrix3x2.CreateRotation(step % 360 * MathF.PI / 180f,
                    new Vector2(box.X + 92f, box.Y + 67f)));
                ctx.DrawRoundedRect(new Rect(box.X + 42f, box.Y + 27f, 100f, 80f), new CornerRadius(6f), Card, Ink,
                    new Thickness(1f));
                ctx.Pop();
            }
        });

        Assert.True(perFrame < 1, $"{Cards} transforms allocated {perFrame:0.#} B per frame");
    }

    [Theory]
    [InlineData(BorderStyle.Dashed)]
    [InlineData(BorderStyle.Dotted)]
    [InlineData(BorderStyle.Solid)]
    public void Borders_AllocateNothingPerFrame(BorderStyle style)
    {
        double perFrame = AllocatedPerFrame($"{style} borders", (ctx, _) =>
        {
            for (int i = 0; i < Cards; i++)
                ctx.DrawBorder(Box(i), i % 2 == 0 ? Radius : default, Ink, new Thickness(1f + i % 3), style);
        });

        Assert.True(perFrame < 1, $"{Cards} {style} borders allocated {perFrame:0.#} B per frame");
    }
}
