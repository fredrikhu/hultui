using System.Reflection;
using System.Runtime.InteropServices;
using SkiaSharp;
using Xunit;

namespace HultUI.Rendering.Skia.Tests;

/// <summary>
/// Paths drawn and clipped through <see cref="NativePath"/>: a pooled <see cref="SKPathBuilder"/>
/// whose path goes to the canvas through four functions of libSkiaSharp's C API, so SkiaSharp 4
/// makes no wrapper per shape. These hold the exports and their signatures to the pinned
/// SkiaSharp, and the pixels to the ones SkiaSharp's own <see cref="SKPath"/> calls give. The
/// allocation side is held by <c>PolygonTests</c>, <c>TextDrawAllocationTests</c> and the
/// headless <c>FrameAllocationTests</c>.
/// </summary>
public class NativePathTests
{
    [Fact]
    public void TheFourExports_AreInTheLibrarySkiaSharpLoaded()
    {
        Assert.Null(NativePath.Missing);
        var library = NativeTextBlob.LoadSkia();
        Assert.NotNull(library);
        Assert.Empty(NativeTextBlob.Absent(library.Value, NativePath.Exports));
    }

    public static TheoryData<string, Type, Type[]> Signatures() => new()
    {
        // sk_path_t* sk_pathbuilder_detach_path(sk_pathbuilder_t*)
        { NativePath.DetachExport, typeof(IntPtr), [typeof(IntPtr)] },
        // void sk_canvas_draw_path(sk_canvas_t*, const sk_path_t*, const sk_paint_t*)
        { NativePath.DrawExport, typeof(void), [typeof(IntPtr), typeof(IntPtr), typeof(IntPtr)] },
        // void sk_canvas_clip_path_with_operation(sk_canvas_t*, const sk_path_t*, sk_clipop_t, bool)
        { NativePath.ClipExport, typeof(void), [typeof(IntPtr), typeof(IntPtr), typeof(SKClipOperation), typeof(bool)] },
        // void sk_path_delete(sk_path_t*)
        { NativePath.DeleteExport, typeof(void), [typeof(IntPtr)] },
    };

    /// <summary>SkiaSharp's own bindings are generated from the C headers, so a changed C
    /// signature changes them, and NativePath's function pointers must change with it.</summary>
    [Theory]
    [MemberData(nameof(Signatures))]
    public void SkiaSharpsOwnBinding_HasTheSignatureCalledHere(string export, Type returns, Type[] parameters)
    {
        var api = typeof(SKCanvas).Assembly.GetType("SkiaSharp.SkiaApi", throwOnError: true)!;
        var method = api.GetMethod(export, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(method);
        Assert.Equal(returns, method.ReturnType);
        Assert.Equal(parameters, method.GetParameters().Select(p => p.ParameterType));
        // SkiaSharp binds a function with a bool through [LibraryImport], the rest through
        // [DllImport]; either way the library is SkiaSharp's own and the convention the
        // platform's default, which `delegate* unmanaged<...>` uses.
        if (method.GetCustomAttribute<DllImportAttribute>() is { } dllImport)
        {
            Assert.Equal(NativeTextBlob.Library, dllImport.Value);
            Assert.Equal(CallingConvention.Winapi, dllImport.CallingConvention);
        }
        else
        {
            var libraryImport = method.GetCustomAttribute<LibraryImportAttribute>();
            Assert.NotNull(libraryImport);
            Assert.Equal(NativeTextBlob.Library, libraryImport.LibraryName);
            Assert.Null(method.GetCustomAttribute<UnmanagedCallConvAttribute>());
        }
        // The clip's bool is C's one-byte bool, which NativePath passes as a byte.
        foreach (var parameter in method.GetParameters().Where(p => p.ParameterType == typeof(bool)))
            Assert.Equal(UnmanagedType.I1, parameter.GetCustomAttribute<MarshalAsAttribute>()?.Value);
        // sk_clipop_t is a C enum, which NativePath passes as an int.
        Assert.Equal(typeof(int), Enum.GetUnderlyingType(typeof(SKClipOperation)));
    }

    private static readonly SKPoint[] Star =
    [
        new(32.3f, 3.1f), new(40.7f, 24.9f), new(61.2f, 25.4f), new(44.6f, 38.8f), new(50.9f, 60.2f),
        new(32.3f, 47.5f), new(13.7f, 60.2f), new(20.1f, 38.8f), new(3.4f, 25.4f), new(23.9f, 24.9f),
    ];

    private static void Trace(SKPathBuilder builder, bool close)
    {
        builder.MoveTo(Star[0]);
        for (int i = 1; i < Star.Length; i++) builder.LineTo(Star[i]);
        if (close) builder.Close();
    }

    private static SKPath Wrapped(bool close)
    {
        using var builder = new SKPathBuilder();
        Trace(builder, close);
        return builder.Detach();
    }

    private static byte[] Pixels(Action<SKCanvas> draw)
    {
        using var surface = SKSurface.Create(new SKImageInfo(64, 64, SKColorType.Rgba8888, SKAlphaType.Premul))!;
        surface.Canvas.Clear(SKColors.White);
        draw(surface.Canvas);
        using var image = surface.Snapshot();
        using var pixmap = image.PeekPixels();
        return pixmap.GetPixelSpan().ToArray();
    }

    [Theory]
    [InlineData(false, true)]    // a filled polygon
    [InlineData(true, false)]    // a stroked polyline with round joins
    public void ADraw_IsTheDrawSkiaSharpsPathMakes(bool stroke, bool close)
    {
        using var paint = new SKPaint
        {
            Color = new SKColor(20, 90, 200, 220), IsAntialias = true, IsStroke = stroke,
            StrokeWidth = 3.5f, StrokeJoin = SKStrokeJoin.Round, StrokeCap = SKStrokeCap.Butt,
        };
        using var builder = new SKPathBuilder();

        var native = Pixels(canvas =>
        {
            Trace(builder, close);
            NativePath.Draw(canvas, builder, paint);
            // The draw emptied the builder: drawing it again adds nothing.
            NativePath.Draw(canvas, builder, paint);
        });
        var wrapped = Pixels(canvas =>
        {
            using var path = Wrapped(close);
            canvas.DrawPath(path, paint);
        });

        Assert.Contains(native, b => b != 255);
        Assert.Equal(wrapped, native);
    }

    [Fact]
    public void AClip_IsTheClipSkiaSharpsPathMakes_AndOutlivesThePath()
    {
        using var fill = new SKPaint { Color = new SKColor(200, 40, 40), IsAntialias = true };
        using var builder = new SKPathBuilder();

        var native = Pixels(canvas =>
        {
            Trace(builder, close: true);
            canvas.Save();
            NativePath.Clip(canvas, builder, SKClipOperation.Intersect, antialias: true);
            // The native path is gone by now; the clip kept what it needed.
            canvas.DrawRect(new SKRect(0, 0, 64, 64), fill);
            canvas.Restore();
        });
        var wrapped = Pixels(canvas =>
        {
            using var path = Wrapped(close: true);
            canvas.Save();
            canvas.ClipPath(path, SKClipOperation.Intersect, antialias: true);
            canvas.DrawRect(new SKRect(0, 0, 64, 64), fill);
            canvas.Restore();
        });

        Assert.Contains(native, b => b != 255);
        Assert.Equal(wrapped, native);
    }
}
