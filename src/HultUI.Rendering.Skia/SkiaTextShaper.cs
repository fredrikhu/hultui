using HultUI.Text;
using SkiaSharp;
using SkiaSharp.HarfBuzz;
using HbBuffer = HarfBuzzSharp.Buffer;
using HbFont = HarfBuzzSharp.Font;

namespace HultUI.Rendering.Skia;

/// <summary>
/// Shapes text with HarfBuzz, keeping one HarfBuzz font per typeface it has met, and one
/// buffer it fills again on every call.
///
/// <para>Each of those fonts holds native memory and a stream over the font file that only
/// <see cref="Dispose"/> gives back: a finalizer never runs for it, because the stream is
/// reachable from a handle HarfBuzz holds until the font is destroyed. A shaper that is
/// dropped without being disposed leaks them.</para>
///
/// <para>It drives HarfBuzz itself rather than through SkiaSharp's <see cref="SKShaper"/>,
/// whose every call made a buffer, an <see cref="SKFont"/>, an <see cref="SKPaint"/> nothing
/// read, a UTF-8 copy of the text and five arrays of results: 936 B to shape "Office", against
/// 176 B now, the run and its glyph array. The positions are SKShaper's arithmetic, to the bit;
/// <c>ShaperEquivalenceTests</c> holds the two together over every script the system's fonts
/// cover. The cluster numbers HarfBuzz gives back count UTF-16 units now instead of UTF-8
/// bytes; nothing reads them.</para>
///
/// <para>The kept buffer and font size serve one call at a time. Text services are for one
/// thread, but <see cref="HultUI.Core.Rendering.TextMeasure"/>'s context is process-wide, so
/// a call that finds them in use shapes with objects of its own and keeps nothing, as calls
/// after <see cref="Dispose"/> do.</para>
/// </summary>
public sealed class SkiaTextShaper : ITextShaper, IDisposable
{
    // SKShaper's units: HarfBuzz positions at 512 per em, which size / 512 turns into pixels.
    private const int HarfBuzzScale = 512;

    // A buffer keeps the native memory of the longest text it held (~60 B a char), for as
    // long as the shaper lives. Longer texts shape in a buffer of their own.
    private const int KeptBufferMaxChars = 4096;

    private static readonly HarfBuzzSharp.Feature[] NoFeatures = [];

    private readonly Dictionary<SKTypeface, HbFont> _fonts = new();
    private HbBuffer? _buffer;
    private SKFont? _sizer;

    // The font size the scales below were taken for, as bits (NaN included), if any.
    private bool _haveScale;
    private int _scaleSizeBits;
    private float _scaleX, _scaleY;

    // 1 while a call uses the kept objects; 1 for good once disposed.
    private int _busy;
    private int _disposed;

    /// <summary>How many HarfBuzz fonts the shaper holds: one per typeface it has shaped.</summary>
    internal int CachedShaperCount => _fonts.Count;

    public ShapedRun Shape(string text, IResolvedFont font, float fontSize, bool rtl = false)
    {
        if (string.IsNullOrEmpty(text))
            return new ShapedRun(text, font, fontSize, 0, []);

        var typeface = (SKTypeface)font.NativeHandle;
        if (Interlocked.CompareExchange(ref _busy, 1, 0) == 0)
        {
            try
            {
                var hbFont = FontFor(typeface);
                var sizer = _sizer ??= new SKFont();
                int sizeBits = BitConverter.SingleToInt32Bits(fontSize);
                if (!_haveScale || sizeBits != _scaleSizeBits)
                {
                    Scales(sizer, fontSize, out _scaleX, out _scaleY);
                    _scaleSizeBits = sizeBits;
                    _haveScale = true;
                }

                if (text.Length > KeptBufferMaxChars)
                {
                    using var own = new HbBuffer();
                    return Shape(hbFont, own, _scaleX, _scaleY, text, font, fontSize, rtl);
                }
                var buffer = _buffer ??= new HbBuffer();
                buffer.ClearContents();
                return Shape(hbFont, buffer, _scaleX, _scaleY, text, font, fontSize, rtl);
            }
            finally
            {
                Volatile.Write(ref _busy, 0);
            }
        }

        // Disposed, or in use by another call. Still shapes, keeping nothing: a disposed shaper
        // can be reached afterwards — TextMeasure's ambient context outlives the application
        // that installed it — and shaping there must neither fail nor start leaking again.
        using var transientFont = CreateFont(typeface);
        using var transientBuffer = new HbBuffer();
        using var transientSizer = new SKFont();
        Scales(transientSizer, fontSize, out float scaleX, out float scaleY);
        return Shape(transientFont, transientBuffer, scaleX, scaleY, text, font, fontSize, rtl);
    }

    /// <summary>
    /// SKShaper's pixels per HarfBuzz unit: the size Skia keeps for <paramref name="fontSize"/>
    /// (it clamps), over the scale, times the font's horizontal scale.
    /// </summary>
    private static void Scales(SKFont sizer, float fontSize, out float scaleX, out float scaleY)
    {
        sizer.Size = fontSize;
        scaleY = sizer.Size / HarfBuzzScale;
        scaleX = scaleY * sizer.ScaleX;
    }

    private static ShapedRun Shape(HbFont hbFont, HbBuffer buffer, float scaleX, float scaleY,
        string text, IResolvedFont font, float fontSize, bool rtl)
    {
        buffer.AddUtf16(text);
        buffer.GuessSegmentProperties();
        // Direction set explicitly, not guessed: guessing is per-buffer, and a neutral-heavy
        // RTL run (punctuation, digits) guesses LTR and comes out backwards. The script is
        // still guessed — that part is per-content.
        if (rtl)
            buffer.Direction = HarfBuzzSharp.Direction.RightToLeft;
        hbFont.Shape(buffer, NoFeatures);

        var infos = buffer.GetGlyphInfoSpan();
        var positions = buffer.GetGlyphPositionSpan();
        int count = infos.Length;
        var glyphs = new GlyphPosition[count];

        // SKShaper's arithmetic, in its order: a glyph sits at the pen plus its offset (HarfBuzz's
        // y grows up, Skia's down), then the pen moves on by its advance. A glyph's advance is the
        // distance to the next glyph's position, and the last one's to the run's width.
        float penX = 0f, penY = 0f;
        uint glyphId = 0;
        float x = 0f, y = 0f;
        for (int i = 0; i < count; i++)
        {
            float nextX = penX + positions[i].XOffset * scaleX;
            float nextY = penY - positions[i].YOffset * scaleY;
            if (i > 0)
                glyphs[i - 1] = new GlyphPosition(glyphId, x, y, nextX - x);
            glyphId = infos[i].Codepoint;
            x = nextX;
            y = nextY;
            penX += positions[i].XAdvance * scaleX;
            penY += positions[i].YAdvance * scaleY;
        }
        if (count > 0)
            glyphs[count - 1] = new GlyphPosition(glyphId, x, y, penX - x);

        return new ShapedRun(text, font, fontSize, penX, glyphs);
    }

    private HbFont FontFor(SKTypeface typeface)
    {
        if (_fonts.TryGetValue(typeface, out var cached))
            return cached;

        var created = CreateFont(typeface);
        _fonts[typeface] = created;
        return created;
    }

    /// <summary>A HarfBuzz font over <paramref name="typeface"/>'s file, made as SKShaper makes it.</summary>
    private static HbFont CreateFont(SKTypeface typeface)
    {
        using var blob = typeface.OpenStream(out int index).ToHarfBuzzBlob();
        using var face = new HarfBuzzSharp.Face(blob, index);
        face.Index = index;
        face.UnitsPerEm = typeface.UnitsPerEm;
        var font = new HbFont(face);
        font.SetScale(HarfBuzzScale, HarfBuzzScale);
        font.SetFunctionsOpenType();
        return font;
    }

    /// <summary>
    /// Destroys the HarfBuzz fonts and the buffer the shaper holds. It can still shape
    /// afterwards, at the cost of building a HarfBuzz font for each call, and keeps nothing.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        // Wait out a call that is using the kept objects, then keep them taken for good.
        var wait = new SpinWait();
        while (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
            wait.SpinOnce();

        foreach (var font in _fonts.Values)
            font.Dispose();
        _fonts.Clear();
        _buffer?.Dispose();
        _buffer = null;
        _sizer?.Dispose();
        _sizer = null;
    }
}
