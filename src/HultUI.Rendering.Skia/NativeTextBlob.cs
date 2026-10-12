using System.Runtime.InteropServices;
using SkiaSharp;

namespace HultUI.Rendering.Skia;

/// <summary>
/// Text blobs held as native handles, through four functions of libSkiaSharp's C API called
/// directly instead of through <see cref="SKTextBlob"/>.
///
/// <para>Every new <see cref="SKTextBlob"/> is a managed wrapper SkiaSharp also registers in its
/// handle table: 80 B per blob, and a scrolled list makes a blob for each row it shows for the
/// first time (plan 17, round 3: <c>scroll/jump 5,000-row list</c> 6.3 KB a step, most of it
/// those). The native calls are the ones the wrapper makes: <c>sk_textblob_builder_make</c> is
/// <see cref="SKTextBlobBuilder.Build"/>, <c>sk_canvas_draw_text_blob</c> is
/// <see cref="SKCanvas.DrawText(SKTextBlob, float, float, SKPaint)"/>,
/// <c>sk_textblob_get_bounds</c> is <see cref="SKTextBlob.Bounds"/> and <c>sk_textblob_unref</c>
/// is <see cref="SKTextBlob"/>'s Dispose. So the pixels and the lifetimes are what they were:
/// a blob starts with one reference, its owner's; a draw takes what it needs (a raster canvas
/// draws the glyphs before it returns, a recording canvas refs the blob, and Skia's GPU text
/// keeps glyphs of its own, told by the blob's destructor when it goes), so the owner may unref
/// it straight after a draw, as disposing the wrapper did.</para>
///
/// <para>This ties the renderer to SkiaSharp's C API at the pinned version (3.119.2), as
/// SkiaSharp's own bindings are tied to it. The exports are looked up in the library SkiaSharp
/// itself loaded, when the first render context is made, and a missing one stops it there with
/// an error that names it; <c>NativeTextBlobTests</c> fails when the exports or SkiaSharp's own
/// signatures for them change (docs/DECISIONS.md, Rendering).</para>
/// </summary>
internal static unsafe class NativeTextBlob
{
    /// <summary>The library SkiaSharp's own bindings import.</summary>
    internal const string Library = "libSkiaSharp";

    internal const string MakeExport = "sk_textblob_builder_make";
    internal const string DrawExport = "sk_canvas_draw_text_blob";
    internal const string UnrefExport = "sk_textblob_unref";
    internal const string BoundsExport = "sk_textblob_get_bounds";

    internal static readonly string[] Exports = [MakeExport, DrawExport, UnrefExport, BoundsExport];

    // sk_textblob_t* sk_textblob_builder_make(sk_textblob_builder_t*)
    private static readonly delegate* unmanaged<nint, nint> s_make;
    // void sk_canvas_draw_text_blob(sk_canvas_t*, sk_textblob_t*, float x, float y, const sk_paint_t*)
    private static readonly delegate* unmanaged<nint, nint, float, float, nint, void> s_draw;
    // void sk_textblob_unref(const sk_textblob_t*)
    private static readonly delegate* unmanaged<nint, void> s_unref;
    // void sk_textblob_get_bounds(const sk_textblob_t*, sk_rect_t*)
    private static readonly delegate* unmanaged<nint, SKRect*, void> s_bounds;

    /// <summary>Why the exports could not be used, or null when all of them were found.</summary>
    internal static readonly string? Missing;

    static NativeTextBlob()
    {
        if (LoadSkia() is not { } library)
        {
            Missing = $"{Library} could not be loaded for SkiaSharp's assembly";
            return;
        }
        if (Absent(library, Exports) is { Count: > 0 } absent)
        {
            Missing = $"{Library} does not export {string.Join(", ", absent)}";
            return;
        }
        s_make = (delegate* unmanaged<nint, nint>)NativeLibrary.GetExport(library, MakeExport);
        s_draw = (delegate* unmanaged<nint, nint, float, float, nint, void>)NativeLibrary.GetExport(library, DrawExport);
        s_unref = (delegate* unmanaged<nint, void>)NativeLibrary.GetExport(library, UnrefExport);
        s_bounds = (delegate* unmanaged<nint, SKRect*, void>)NativeLibrary.GetExport(library, BoundsExport);
    }

    /// <summary>The library SkiaSharp's assembly resolves its imports to (its resolver, if an
    /// app set one, and its search paths): the module SkiaSharp already loaded, never a second
    /// copy. Null when it cannot be loaded.</summary>
    internal static nint? LoadSkia() =>
        NativeLibrary.TryLoad(Library, typeof(SKCanvas).Assembly, null, out nint library) ? library : null;

    /// <summary>Those of <paramref name="names"/> that <paramref name="library"/> does not export.</summary>
    internal static List<string> Absent(nint library, IEnumerable<string> names)
    {
        var absent = new List<string>();
        foreach (var name in names)
            if (!NativeLibrary.TryGetExport(library, name, out _))
                absent.Add(name);
        return absent;
    }

    /// <summary>Throws, naming what is missing, unless every export was found. The render
    /// context calls it when it is made, so a SkiaSharp without these functions fails there and
    /// not at the first text it draws.</summary>
    internal static void EnsureAvailable()
    {
        if (Missing is { } why) throw Unavailable(why);
    }

    internal static EntryPointNotFoundException Unavailable(string why) => new(
        $"HultUI.Rendering.Skia draws text through four functions of SkiaSharp's native C API " +
        $"({string.Join(", ", Exports)}), built against SkiaSharp 3.119.2, but {why}. " +
        $"The SkiaSharp package and its native assets must be the version HultUI.Rendering.Skia references.");

    /// <summary>The blob of the runs <paramref name="builder"/> holds, with one reference for the
    /// caller; the builder is left empty. 0 when it held none.</summary>
    internal static nint Make(SKTextBlobBuilder builder)
    {
        nint blob = s_make(builder.Handle);
        GC.KeepAlive(builder);
        return blob;
    }

    internal static void Draw(SKCanvas canvas, nint blob, float x, float y, SKPaint paint)
    {
        s_draw(canvas.Handle, blob, x, y, paint.Handle);
        GC.KeepAlive(canvas);
        GC.KeepAlive(paint);
    }

    internal static SKRect Bounds(nint blob)
    {
        SKRect bounds;
        s_bounds(blob, &bounds);
        return bounds;
    }

    /// <summary>Gives up the caller's reference: the blob is freed once nothing else holds one.</summary>
    internal static void Unref(nint blob) => s_unref(blob);
}
