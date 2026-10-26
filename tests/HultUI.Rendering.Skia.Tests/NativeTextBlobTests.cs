using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using HultUI.Text;
using SkiaSharp;
using Xunit;

namespace HultUI.Rendering.Skia.Tests;

/// <summary>
/// Text blobs as native handles (<see cref="NativeTextBlob"/>, plan 17, round 3): four functions
/// of libSkiaSharp's C API called directly, each blob made once and given up once. These hold
/// the exports and their signatures to the pinned SkiaSharp, the pixels and bounds to the
/// <see cref="SKTextBlob"/> wrapper's, and every blob's reference to exactly one release.
/// </summary>
public class NativeTextBlobTests
{
    private static ResolvedInheritedStyle Style(float size = 14f) => new(
        FontFamily: "sans-serif", FontSize: size, FontWeight: FontWeight.Regular,
        TextColor: new Color(30, 60, 90, 255), TextDirection: TextDirection.Ltr, Cursor: CursorType.Default,
        TextAlign: TextAlign.Start, Decoration: TextDecoration.None);

    private static SkiaRasterRenderSurface Surface(int width = 600, int height = 200)
    {
        var surface = new SkiaRasterRenderSurface();
        surface.Resize(width, height, 1f);
        return surface;
    }

    [Fact]
    public void TheFourExports_AreInTheLibrarySkiaSharpLoaded()
    {
        Assert.Null(NativeTextBlob.Missing);
        var library = NativeTextBlob.LoadSkia();
        Assert.NotNull(library);
        Assert.Empty(NativeTextBlob.Absent(library.Value, NativeTextBlob.Exports));

        // A function that is not there is reported by name, and the error says what to do.
        Assert.Equal(["sk_no_such_function"],
            NativeTextBlob.Absent(library.Value, [NativeTextBlob.MakeExport, "sk_no_such_function"]));
        var error = NativeTextBlob.Unavailable("libSkiaSharp does not export sk_textblob_unref");
        Assert.Contains("sk_textblob_unref", error.Message);
        Assert.Contains("3.119.2", error.Message);
    }

    public static TheoryData<string, Type, Type[]> Signatures() => new()
    {
        // sk_textblob_t* sk_textblob_builder_make(sk_textblob_builder_t*)
        { NativeTextBlob.MakeExport, typeof(IntPtr), [typeof(IntPtr)] },
        // void sk_canvas_draw_text_blob(sk_canvas_t*, sk_textblob_t*, float, float, const sk_paint_t*)
        { NativeTextBlob.DrawExport, typeof(void), [typeof(IntPtr), typeof(IntPtr), typeof(float), typeof(float), typeof(IntPtr)] },
        // void sk_textblob_unref(const sk_textblob_t*)
        { NativeTextBlob.UnrefExport, typeof(void), [typeof(IntPtr)] },
        // void sk_textblob_get_bounds(const sk_textblob_t*, sk_rect_t*)
        { NativeTextBlob.BoundsExport, typeof(void), [typeof(IntPtr), typeof(SKRect).MakePointerType()] },
    };

    /// <summary>SkiaSharp's own bindings are generated from the C headers, so a changed C
    /// signature changes them: NativeTextBlob's function pointers must match them exactly, and a
    /// SkiaSharp other than the pinned one has to be checked again.</summary>
    [Theory]
    [MemberData(nameof(Signatures))]
    public void SkiaSharpsOwnBinding_HasTheSignatureCalledHere(string export, Type returns, Type[] parameters)
    {
        var api = typeof(SKCanvas).Assembly.GetType("SkiaSharp.SkiaApi", throwOnError: true)!;
        var method = api.GetMethod(export, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(method);
        Assert.Equal(returns, method.ReturnType);
        Assert.Equal(parameters, method.GetParameters().Select(p => p.ParameterType));
        var import = method.GetCustomAttribute<DllImportAttribute>();
        Assert.NotNull(import);
        Assert.Equal(NativeTextBlob.Library, import.Value);
        // The platform's default convention, which `delegate* unmanaged<...>` uses.
        Assert.Equal(CallingConvention.Winapi, import.CallingConvention);
    }

    [Fact]
    public void SkiaSharp_IsThePinnedVersion()
    {
        string version = typeof(SKCanvas).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
        Assert.True(version.StartsWith("3.119.2", StringComparison.Ordinal),
            $"SkiaSharp {version}: NativeTextBlob calls its C API directly; check the four functions' signatures " +
            "against this version's headers, then pin it here and in docs/DECISIONS.md");
    }

    // Several runs in several faces: Latin, Hebrew, an emoji, CJK.
    private const string Mixed = "Draw blobs: Office affine שלום \U0001F600 東京 and more words to wrap the line";

    /// <summary>A native blob is the blob <see cref="SKTextBlobBuilder.Build"/> made: drawn at
    /// the same places in the same paint, the pixels are the wrapper's to the byte, and its
    /// bounds (which <see cref="IRenderContext.TextPaintBounds"/> reads) are the wrapper's.</summary>
    [Fact]
    public void ANativeBlob_DrawsAndBoundsAsSkiaSharpsWrapperDoes()
    {
        var style = Style(17f);
        using var native = Surface();
        var ctx = (SkiaRenderContext)native.RenderContext;
        var paragraph = (PipelineTextParagraph)ctx.LayoutParagraph(Mixed, style, 420);
        Assert.True(paragraph.TextLines.Count >= 2);
        Assert.True(paragraph.TextLines.Sum(l => l.Runs.Count) >= 4);
        ctx.BeginFrame(600, 200, 1f, Color.White);
        ctx.DrawParagraph(paragraph, new Point(10, 10));
        ctx.EndFrame();
        native.Present();

        using var wrapped = Surface();
        var reference = (SkiaRenderContext)wrapped.RenderContext;
        reference.BeginFrame(600, 200, 1f, Color.White);
        using (var paint = new SKPaint { Color = new SKColor(30, 60, 90, 255), IsAntialias = true })
        using (var font = new SKFont { Subpixel = true })
        using (var builder = new SKTextBlobBuilder())
        {
            for (int i = 0; i < paragraph.TextLines.Count; i++)
            {
                var line = paragraph.TextLines[i];
                float baseX = 10 + paragraph.LineLeft(i), baseline = 10 + line.OffsetY + line.Ascent;
                foreach (var run in line.Runs)
                {
                    if (run.Glyphs.Length == 0) continue;
                    font.Typeface = (SKTypeface)run.Font.NativeHandle;
                    font.Size = run.FontSize;
                    var buffer = builder.AllocateRawPositionedRun(font, run.Glyphs.Length);
                    for (int g = 0; g < run.Glyphs.Length; g++)
                    {
                        buffer.Glyphs[g] = (ushort)run.Glyphs[g].GlyphId;
                        buffer.Positions[g] = new SKPoint(run.Glyphs[g].X, run.Glyphs[g].Y);
                    }
                    using var blob = builder.Build()!;
                    reference.Canvas.DrawText(blob, baseX + run.OffsetX, baseline, paint);
                    Assert.Equal(blob.Bounds, NativeTextBlob.Bounds(ctx.BlobOf(run)));
                }
            }
        }
        reference.EndFrame();
        wrapped.Present();

        Assert.Equal(wrapped.ReadPixels(out _, out _, out _), native.ReadPixels(out _, out _, out _));
    }

    private static TextParagraph[] Rows(IRenderContext ctx, int count, string prefix = "Row") =>
        Enumerable.Range(0, count)
            .Select(i => ctx.LayoutParagraph($"{prefix} {i:D6} of the list", Style(), 400))
            .ToArray();

    /// <summary>Every blob is made with one reference and released once: when the cache fills
    /// and lets go of what the last two frames did not draw, and at Dispose for those still
    /// cached. None is released twice (Skia would free it under another holder) and none is kept
    /// (a leak of native memory).</summary>
    [Fact]
    public void EveryBlobMade_IsReleasedOnce_ThroughCacheChurnAndDispose()
    {
        var surface = Surface();
        var ctx = (SkiaRenderContext)surface.RenderContext;
        var rows = Rows(ctx, 3 * SkiaRenderContext.RunBlobCacheMaxSize + 100);

        // A hundred new rows a frame, as a list scrolled through.
        for (int i = 0; i < rows.Length; i++)
        {
            if (i % 100 == 0) ctx.BeginFrame(600, 200, 1f, Color.White);
            ctx.DrawParagraph(rows[i], new Point(10, 10));
            Assert.Equal(ctx.CachedBlobCount, ctx.BlobsMade - ctx.BlobsReleased);
            if (i % 100 == 99 || i == rows.Length - 1) ctx.EndFrame();
        }
        // The last rows again: drawn from the cache, nothing made.
        ctx.BeginFrame(600, 200, 1f, Color.White);
        long made = ctx.BlobsMade;
        for (int i = rows.Length - 50; i < rows.Length; i++) ctx.DrawParagraph(rows[i], new Point(10, 10));
        Assert.Equal(made, ctx.BlobsMade);
        // Bounds come from the same blobs: a row let go long ago is made again.
        ctx.TextPaintBounds("Row 000001 of the list", new Rect(0, 0, 400, 30), Style(), TextOverflow.Clip, null, null);
        ctx.EndFrame();

        Assert.Equal(rows.Length + 1, ctx.BlobsMade);
        Assert.True(ctx.BlobsReleased >= 3 * SkiaRenderContext.RunBlobCacheMaxSize - SkiaRenderContext.RunBlobCacheMaxSize,
            $"the cache emptied {ctx.BlobsReleased} blobs");

        surface.Dispose();
        Assert.Equal(0, ctx.CachedBlobCount);
        Assert.Equal(ctx.BlobsMade, ctx.BlobsReleased);

        surface.Dispose();   // twice: nothing released again
        Assert.Equal(ctx.BlobsMade, ctx.BlobsReleased);
    }
}

/// <summary>
/// Native memory over many unique blobs: alone in the process, so other tests' allocations do not
/// move the private bytes it reads.
/// </summary>
[CollectionDefinition(nameof(NativeMemory), DisableParallelization = true)]
public sealed class NativeMemory;

[Collection(nameof(NativeMemory))]
public class NativeTextBlobLeakTests
{
    private static ResolvedInheritedStyle Style() => new(
        FontFamily: "sans-serif", FontSize: 14f, FontWeight: FontWeight.Regular,
        TextColor: new Color(20, 20, 20, 255), TextDirection: TextDirection.Ltr, Cursor: CursorType.Default,
        TextAlign: TextAlign.Start, Decoration: TextDecoration.None);

    /// <summary>
    /// 100,000 rows never drawn before, each a blob of ~60 glyphs (~1 KB native), through one
    /// context whose cache empties at its bound: private bytes stay where the first 30,000 left
    /// them (the text pipeline's caches fill to their own bounds there). A blob never released
    /// would grow them by ~100 MB.
    /// </summary>
    [Fact]
    public void ManyUniqueBlobs_DoNotGrowNativeMemory()
    {
        using var surface = new SkiaRasterRenderSurface();
        surface.Resize(800, 60, 1f);
        var ctx = (SkiaRenderContext)surface.RenderContext;
        var style = Style();
        void Draw(int from, int count)
        {
            ctx.BeginFrame(800, 60, 1f, Color.White);
            for (int i = from; i < from + count; i++)
                ctx.DrawText($"Row {i:D8} with words enough for a long run of glyphs, {i % 9973} more of them",
                    new Rect(4, 4, 790, 30), style, TextOverflow.Clip, null);
            ctx.EndFrame();
        }
        static long Private()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            using var process = Process.GetCurrentProcess();
            return process.PrivateMemorySize64;
        }

        for (int k = 0; k < 30; k++) Draw(k * 1_000, 1_000);
        long before = Private();
        for (int k = 30; k < 130; k++) Draw(k * 1_000, 1_000);
        long after = Private();

        Assert.True(ctx.BlobsMade >= 130_000, $"{ctx.BlobsMade} blobs made");
        Assert.Equal(ctx.CachedBlobCount, ctx.BlobsMade - ctx.BlobsReleased);
        double grewMB = (after - before) / 1048576.0;
        Assert.True(grewMB < 32, $"private bytes grew {grewMB:F1} MB over 100,000 new blobs");
    }
}
