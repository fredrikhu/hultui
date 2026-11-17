using System.Runtime.InteropServices;
using SkiaSharp;
using SkiaSharp.HarfBuzz;
using HbFace = HarfBuzzSharp.Face;
using HbFont = HarfBuzzSharp.Font;

namespace HultUI.Rendering.Skia;

/// <summary>
/// Glyph outlines that HarfBuzz reads from the font file, for
/// <see cref="SkiaRenderContext.TextAsOutlines"/>: the same outlines on every OS.
///
/// <para>Skia's own outlines (<see cref="SKFont.GetGlyphPath"/>) come from the platform's font
/// backend, DirectWrite on Windows and FreeType on Linux, and the two disagree on some glyphs.
/// For the pinned DejaVu faces at 11, 13, 14 and 24 px, 44 of 1,260 outlines differ in structure
/// (DirectWrite keeps a cubic where FreeType has a quadratic and a line), and 16 differ by up to
/// 0.02 px. Skia m119 rasterized both versions to the same pixels; m156 does not. A golden image
/// of italic and accented text came out 21 pixels apart on the two OSes (2026-11-17). HarfBuzz
/// reads the glyf and CFF tables itself, with the same code on every OS. Its outlines of those
/// 1,260 glyphs are byte-identical on Windows and Linux, and have the points Skia's have.</para>
///
/// <para>HarfBuzzSharp does not bind HarfBuzz's draw API, so its eight functions are called
/// through function pointers. They are looked up in the library HarfBuzzSharp itself loaded.
/// Unlike SkiaSharp's C API, HarfBuzz's is stable: a function, once released, is never removed
/// or changed. So nothing is pinned to a version; <c>GlyphOutlinesTests</c> checks that the
/// exports are there (docs/DECISIONS.md, Rendering).</para>
///
/// <para>Not for variable fonts: the HarfBuzz font is the file's default instance, whatever
/// variation the typeface was made at. The pinned faces are not variable.</para>
/// </summary>
internal sealed unsafe class GlyphOutlines : IDisposable
{
    /// <summary>The library HarfBuzzSharp's own bindings import.</summary>
    internal const string Library = "libHarfBuzzSharp";

    internal const string CreateFuncsExport = "hb_draw_funcs_create";
    internal const string SetMoveToExport = "hb_draw_funcs_set_move_to_func";
    internal const string SetLineToExport = "hb_draw_funcs_set_line_to_func";
    internal const string SetQuadraticToExport = "hb_draw_funcs_set_quadratic_to_func";
    internal const string SetCubicToExport = "hb_draw_funcs_set_cubic_to_func";
    internal const string SetClosePathExport = "hb_draw_funcs_set_close_path_func";
    internal const string MakeImmutableExport = "hb_draw_funcs_make_immutable";
    internal const string DrawGlyphExport = "hb_font_draw_glyph";

    internal static readonly string[] Exports =
    [
        CreateFuncsExport, SetMoveToExport, SetLineToExport, SetQuadraticToExport, SetCubicToExport,
        SetClosePathExport, MakeImmutableExport, DrawGlyphExport,
    ];

    // void hb_font_draw_glyph(hb_font_t*, hb_codepoint_t glyph, hb_draw_funcs_t*, void* draw_data)
    private static readonly delegate* unmanaged<nint, uint, nint, nint, void> s_drawGlyph;

    // The callbacks below, set once and made immutable: one hb_draw_funcs_t shared by every
    // instance for the life of the process, as HarfBuzz's own static funcs are.
    private static readonly nint s_funcs;

    /// <summary>Why the exports could not be used, or null when all of them were found.</summary>
    internal static readonly string? Missing;

    static GlyphOutlines()
    {
        if (!NativeLibrary.TryLoad(Library, typeof(HbFont).Assembly, null, out nint library))
        {
            Missing = $"{Library} could not be loaded for HarfBuzzSharp's assembly";
            return;
        }
        if (NativeTextBlob.Absent(library, Exports) is { Count: > 0 } absent)
        {
            Missing = $"{Library} does not export {string.Join(", ", absent)}";
            return;
        }

        // hb_draw_funcs_t* hb_draw_funcs_create(void)
        var create = (delegate* unmanaged<nint>)NativeLibrary.GetExport(library, CreateFuncsExport);
        // void hb_draw_funcs_set_*_func(hb_draw_funcs_t*, hb_draw_*_func_t, void* user_data,
        //                               hb_destroy_func_t destroy)
        var setMoveTo = (delegate* unmanaged<nint, nint, nint, nint, void>)NativeLibrary.GetExport(library, SetMoveToExport);
        var setLineTo = (delegate* unmanaged<nint, nint, nint, nint, void>)NativeLibrary.GetExport(library, SetLineToExport);
        var setQuadraticTo = (delegate* unmanaged<nint, nint, nint, nint, void>)NativeLibrary.GetExport(library, SetQuadraticToExport);
        var setCubicTo = (delegate* unmanaged<nint, nint, nint, nint, void>)NativeLibrary.GetExport(library, SetCubicToExport);
        var setClosePath = (delegate* unmanaged<nint, nint, nint, nint, void>)NativeLibrary.GetExport(library, SetClosePathExport);
        // void hb_draw_funcs_make_immutable(hb_draw_funcs_t*)
        var makeImmutable = (delegate* unmanaged<nint, void>)NativeLibrary.GetExport(library, MakeImmutableExport);

        nint funcs = create();
        setMoveTo(funcs, (nint)(delegate* unmanaged<nint, nint, nint, float, float, nint, void>)&MoveTo, 0, 0);
        setLineTo(funcs, (nint)(delegate* unmanaged<nint, nint, nint, float, float, nint, void>)&LineTo, 0, 0);
        setQuadraticTo(funcs, (nint)(delegate* unmanaged<nint, nint, nint, float, float, float, float, nint, void>)&QuadraticTo, 0, 0);
        setCubicTo(funcs, (nint)(delegate* unmanaged<nint, nint, nint, float, float, float, float, float, float, nint, void>)&CubicTo, 0, 0);
        setClosePath(funcs, (nint)(delegate* unmanaged<nint, nint, nint, nint, void>)&ClosePath, 0, 0);
        makeImmutable(funcs);
        s_funcs = funcs;
        s_drawGlyph = (delegate* unmanaged<nint, uint, nint, nint, void>)NativeLibrary.GetExport(library, DrawGlyphExport);
    }

    private readonly Dictionary<SKTypeface, (HbFont Font, int UnitsPerEm)> _fonts = new();
    private readonly Sink _sink = new();
    private bool _disposed;

    /// <exception cref="EntryPointNotFoundException">HarfBuzzSharp's native library lacks the
    /// draw API, naming what is missing.</exception>
    public GlyphOutlines()
    {
        if (Missing is { } why)
            throw new EntryPointNotFoundException(
                $"HultUI.Rendering.Skia reads glyph outlines for TextAsOutlines through HarfBuzz's draw " +
                $"API ({string.Join(", ", Exports)}), but {why}.");
    }

    /// <summary>How many HarfBuzz fonts are held: one per typeface outlined so far.</summary>
    internal int FontCount => _fonts.Count;

    /// <summary>
    /// Adds the outline of <paramref name="glyph"/> of <paramref name="typeface"/> at
    /// <paramref name="size"/> to <paramref name="builder"/>, with its origin at
    /// (<paramref name="x"/>, <paramref name="y"/>), y down as Skia draws. A glyph with no outline
    /// (a space, a bitmap glyph) adds nothing.
    /// </summary>
    public void Add(SKPathBuilder builder, SKTypeface typeface, uint glyph, float size, float x, float y)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var (font, unitsPerEm) = FontOf(typeface);
        _sink.Builder = builder;
        _sink.Scale = size / unitsPerEm;
        _sink.X = x;
        _sink.Y = y;
        var handle = GCHandle.Alloc(_sink);
        try
        {
            s_drawGlyph(font.Handle, glyph, s_funcs, GCHandle.ToIntPtr(handle));
        }
        finally
        {
            handle.Free();
            _sink.Builder = null;
        }
        GC.KeepAlive(font);
    }

    // At a scale of one unit per font unit, HarfBuzz hands over the file's own coordinates (and
    // midpoints between them), exactly; managed float math makes pixels of them, the same on
    // every OS.
    private (HbFont Font, int UnitsPerEm) FontOf(SKTypeface typeface)
    {
        if (_fonts.TryGetValue(typeface, out var known))
            return known;
        using var blob = typeface.OpenStream(out int index).ToHarfBuzzBlob();
        using var face = new HbFace(blob, index);
        int unitsPerEm = face.UnitsPerEm;
        var font = new HbFont(face);
        font.SetScale(unitsPerEm, unitsPerEm);
        font.SetFunctionsOpenType();
        return _fonts[typeface] = (font, unitsPerEm);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var (font, _) in _fonts.Values)
            font.Dispose();
        _fonts.Clear();
    }

    // Where one glyph's outline goes, and how its font units become pixels.
    //
    // A contour is passed on only once it leaves its first point. Fonts keep single-point
    // contours as hinting anchors (DejaVu's u has one above its x-height), which HarfBuzz hands
    // over as a move, a line to the same point and a close. DirectWrite and FreeType drop them.
    // Filled, one draws nothing, but it widens the path's bounds, and a stroke with round caps
    // (a halo) would draw it as a dot.
    private sealed class Sink
    {
        public SKPathBuilder? Builder;
        public float Scale, X, Y;

        // The contour's first point, in font units, and whether the contour has been passed on.
        private float _startX, _startY;
        private bool _started;

        public void MoveTo(float x, float y)
        {
            (_startX, _startY) = (x, y);
            _started = false;
        }

        public bool AtStart(float x, float y) => x == _startX && y == _startY;

        // Whether a segment ending at (x, y) is to be passed on: not while the contour has not
        // left its first point and this segment, controls included, does not either. Passes the
        // contour's move on with the first segment that does.
        public bool Goes(float x, float y, bool controlsAtStart)
        {
            if (_started) return true;
            if (controlsAtStart && AtStart(x, y)) return false;
            Builder!.MoveTo(PxX(_startX), PxY(_startY));
            _started = true;
            return true;
        }

        public void Close()
        {
            if (_started) Builder!.Close();
            _started = false;
        }

        public float PxX(float units) => X + units * Scale;
        public float PxY(float units) => Y - units * Scale;
    }

    private static Sink SinkOf(nint drawData) => (Sink)GCHandle.FromIntPtr(drawData).Target!;

    // void (*hb_draw_move_to_func_t)(hb_draw_funcs_t*, void* draw_data, hb_draw_state_t*,
    //                                float to_x, float to_y, void* user_data)
    [UnmanagedCallersOnly]
    private static void MoveTo(nint funcs, nint drawData, nint state, float x, float y, nint user) =>
        SinkOf(drawData).MoveTo(x, y);

    // hb_draw_line_to_func_t: as move_to.
    [UnmanagedCallersOnly]
    private static void LineTo(nint funcs, nint drawData, nint state, float x, float y, nint user)
    {
        var s = SinkOf(drawData);
        if (s.Goes(x, y, controlsAtStart: true))
            s.Builder!.LineTo(s.PxX(x), s.PxY(y));
    }

    // hb_draw_quadratic_to_func_t: (..., float control_x, float control_y, float to_x, float to_y, ...)
    [UnmanagedCallersOnly]
    private static void QuadraticTo(nint funcs, nint drawData, nint state,
        float cx, float cy, float x, float y, nint user)
    {
        var s = SinkOf(drawData);
        if (s.Goes(x, y, controlsAtStart: s.AtStart(cx, cy)))
            s.Builder!.QuadTo(s.PxX(cx), s.PxY(cy), s.PxX(x), s.PxY(y));
    }

    // hb_draw_cubic_to_func_t: (..., float control1_x, control1_y, control2_x, control2_y, to_x, to_y, ...)
    [UnmanagedCallersOnly]
    private static void CubicTo(nint funcs, nint drawData, nint state,
        float c1x, float c1y, float c2x, float c2y, float x, float y, nint user)
    {
        var s = SinkOf(drawData);
        if (s.Goes(x, y, controlsAtStart: s.AtStart(c1x, c1y) && s.AtStart(c2x, c2y)))
            s.Builder!.CubicTo(s.PxX(c1x), s.PxY(c1y), s.PxX(c2x), s.PxY(c2y), s.PxX(x), s.PxY(y));
    }

    // void (*hb_draw_close_path_func_t)(hb_draw_funcs_t*, void* draw_data, hb_draw_state_t*, void* user_data)
    [UnmanagedCallersOnly]
    private static void ClosePath(nint funcs, nint drawData, nint state, nint user) =>
        SinkOf(drawData).Close();
}
