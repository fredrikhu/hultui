using System.Runtime.InteropServices;
using SkiaSharp;

namespace HultUI.Rendering.Skia;

/// <summary>
/// A pooled <see cref="SKPathBuilder"/>'s path drawn or clipped through four functions of
/// libSkiaSharp's C API, so a shape the renderer draws every frame allocates nothing.
///
/// <para>SkiaSharp 4 builds paths with <see cref="SKPathBuilder"/>; <see cref="SKPath"/> sits on an
/// immutable native path, and its MoveTo/LineTo/Rewind are obsolete shims that make a new
/// <see cref="SKPathBuilder"/> (80 B, with a finalizer) whenever a path that was drawn is changed
/// again. A pooled path redrawn every frame paid that every time: a polygon, a polyline, a tick,
/// a rounded border's corners. The builder's own way out, <see cref="SKPathBuilder.Detach"/>,
/// makes a new <see cref="SKPath"/> wrapper instead. The native calls here are the ones those
/// wrappers make: <c>sk_pathbuilder_detach_path</c> is Detach (it also empties the builder),
/// <c>sk_canvas_draw_path</c> is <see cref="SKCanvas.DrawPath(SKPath, SKPaint)"/>,
/// <c>sk_canvas_clip_path_with_operation</c> is
/// <see cref="SKCanvas.ClipPath(SKPath, SKClipOperation, bool)"/> and <c>sk_path_delete</c> is
/// the wrapper's Dispose. A draw or a clip keeps what it needs of the path (a raster canvas
/// draws before it returns, a recording canvas and the clip stack hold the path's shared
/// geometry), so the path is deleted straight after, as disposing the wrapper did.</para>
///
/// <para>Like <see cref="NativeTextBlob"/>, this ties the renderer to SkiaSharp's C API at the
/// pinned version, and the exports are looked up in the library SkiaSharp itself loaded;
/// <c>NativePathTests</c> fails when they go (docs/DECISIONS.md, Rendering).</para>
/// </summary>
internal static unsafe class NativePath
{
    internal const string DetachExport = "sk_pathbuilder_detach_path";
    internal const string DrawExport = "sk_canvas_draw_path";
    internal const string ClipExport = "sk_canvas_clip_path_with_operation";
    internal const string DeleteExport = "sk_path_delete";

    internal static readonly string[] Exports = [DetachExport, DrawExport, ClipExport, DeleteExport];

    // sk_path_t* sk_pathbuilder_detach_path(sk_pathbuilder_t*)
    private static readonly delegate* unmanaged<nint, nint> s_detach;
    // void sk_canvas_draw_path(sk_canvas_t*, const sk_path_t*, const sk_paint_t*)
    private static readonly delegate* unmanaged<nint, nint, nint, void> s_draw;
    // void sk_canvas_clip_path_with_operation(sk_canvas_t*, const sk_path_t*, sk_clipop_t, bool doAA)
    private static readonly delegate* unmanaged<nint, nint, int, byte, void> s_clip;
    // void sk_path_delete(sk_path_t*)
    private static readonly delegate* unmanaged<nint, void> s_delete;

    /// <summary>Why the exports could not be used, or null when all of them were found.</summary>
    internal static readonly string? Missing;

    static NativePath()
    {
        if (NativeTextBlob.LoadSkia() is not { } library)
        {
            Missing = $"{NativeTextBlob.Library} could not be loaded for SkiaSharp's assembly";
            return;
        }
        if (NativeTextBlob.Absent(library, Exports) is { Count: > 0 } absent)
        {
            Missing = $"{NativeTextBlob.Library} does not export {string.Join(", ", absent)}";
            return;
        }
        s_detach = (delegate* unmanaged<nint, nint>)NativeLibrary.GetExport(library, DetachExport);
        s_draw = (delegate* unmanaged<nint, nint, nint, void>)NativeLibrary.GetExport(library, DrawExport);
        s_clip = (delegate* unmanaged<nint, nint, int, byte, void>)NativeLibrary.GetExport(library, ClipExport);
        s_delete = (delegate* unmanaged<nint, void>)NativeLibrary.GetExport(library, DeleteExport);
    }

    /// <summary>Throws, naming what is missing, unless every export was found. The render
    /// context calls it when it is made, so a SkiaSharp without these functions fails there and
    /// not at the first shape it draws.</summary>
    internal static void EnsureAvailable()
    {
        if (Missing is { } why)
            throw new EntryPointNotFoundException(
                $"HultUI.Rendering.Skia draws paths through four functions of SkiaSharp's native C API " +
                $"({string.Join(", ", Exports)}), built against SkiaSharp 4.156.1, but {why}. " +
                $"The SkiaSharp package and its native assets must be the version HultUI.Rendering.Skia references.");
    }

    /// <summary>Draws what <paramref name="builder"/> holds with <paramref name="paint"/>, and
    /// leaves the builder empty.</summary>
    internal static void Draw(SKCanvas canvas, SKPathBuilder builder, SKPaint paint)
    {
        nint path = s_detach(builder.Handle);
        try
        {
            s_draw(canvas.Handle, path, paint.Handle);
        }
        finally
        {
            s_delete(path);
        }
        GC.KeepAlive(builder);
        GC.KeepAlive(canvas);
        GC.KeepAlive(paint);
    }

    /// <summary>Clips <paramref name="canvas"/> to what <paramref name="builder"/> holds, and
    /// leaves the builder empty.</summary>
    internal static void Clip(SKCanvas canvas, SKPathBuilder builder, SKClipOperation operation, bool antialias)
    {
        nint path = s_detach(builder.Handle);
        try
        {
            s_clip(canvas.Handle, path, (int)operation, antialias ? (byte)1 : (byte)0);
        }
        finally
        {
            s_delete(path);
        }
        GC.KeepAlive(builder);
        GC.KeepAlive(canvas);
    }
}
