using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;

namespace HultUI.Core.Rendering;

public interface IRenderContext
{
    void DrawRect(Rect rect, Color fill);
    void DrawRoundedRect(Rect rect, CornerRadius radius, Color fill, Color stroke, Thickness strokeWidth);

    /// <summary>
    /// Draws a drop shadow behind a (possibly rounded) box. Default is a no-op so
    /// non-visual render contexts (test doubles) need not implement it.
    /// </summary>
    void DrawBoxShadow(Rect box, CornerRadius radius, BoxShadow shadow) { }

    /// <summary>
    /// Fills a (possibly rounded) rect with a linear or radial gradient. Default is
    /// a no-op so non-visual render contexts need not implement it.
    /// </summary>
    void DrawGradientRect(Rect rect, CornerRadius radius, Gradient gradient) { }

    /// <summary>
    /// Draws a border with per-side widths and/or a dashed/dotted style. The simple
    /// uniform-solid case is handled by <see cref="DrawRoundedRect"/>; this covers
    /// the rest. Default is a no-op so non-visual render contexts need not implement it.
    /// </summary>
    void DrawBorder(Rect box, CornerRadius radius, Color color, Thickness widths, BorderStyle style) { }
    void DrawText(string text, Rect bounds, ResolvedInheritedStyle style, TextOverflow overflow, int? maxLines);

    /// <summary>
    /// Draws text like <see cref="DrawText(string, Rect, ResolvedInheritedStyle, TextOverflow, int?)"/>,
    /// but colors the character ranges in
    /// <paramref name="spans"/> individually (uncovered ranges use the style's
    /// text color). Layout, wrapping, and truncation are identical to the plain
    /// overload. A null or empty <paramref name="spans"/> renders exactly like the
    /// plain overload. Default ignores the spans so non-visual render contexts need
    /// not implement it.
    /// </summary>
    void DrawText(string text, Rect bounds, ResolvedInheritedStyle style, TextOverflow overflow, int? maxLines,
        IReadOnlyList<TextSpan>? spans)
        => DrawText(text, bounds, style, overflow, maxLines);

    void DrawImage(IImageSource source, Rect destRect, ImageFit fit);

    /// <summary>
    /// Draws one rectangle of <paramref name="source"/> — in the image's own pixels —
    /// stretched to fill <paramref name="destRect"/>. What a sprite sheet needs: a video
    /// scrubber's preview thumbnails arrive as a grid of frames in one JPEG, and cutting
    /// one out of it here costs nothing, where cropping a bitmap per frame would allocate
    /// one image per hovered second.
    ///
    /// <para>The default falls back to drawing the whole image, for a context with no
    /// notion of pixels to cut from (a recorder, a measurer).</para>
    /// </summary>
    void DrawImage(IImageSource source, Rect destRect, Rect sourceRect)
        => DrawImage(source, destRect, ImageFit.Fill);
    void DrawLine(Point from, Point to, Color color, float strokeWidth);

    /// <summary>
    /// Fills the polygon through <paramref name="points"/>, closed back to the first, antialiased:
    /// a shape no rectangle makes, such as a terminal's Powerline triangles and half-circles.
    /// Default is a no-op so non-visual render contexts need not implement it.
    /// </summary>
    void FillPolygon(ReadOnlySpan<Point> points, Color fill) { }

    /// <summary>
    /// Strokes the open line through <paramref name="points"/>, antialiased, with round joins so a
    /// curve made of short segments has no notches, and flat ends that stop at the last point.
    /// Default is a no-op so non-visual render contexts need not implement it.
    /// </summary>
    void DrawPolyline(ReadOnlySpan<Point> points, Color color, float strokeWidth) { }

    /// <summary>
    /// Measures the width and height of a text string using the same font metrics as DrawText.
    /// Used by elements like TextInput that need precise cursor/selection positioning.
    /// </summary>
    Size MeasureText(string text, ResolvedInheritedStyle style);

    /// <summary>
    /// The widths of <paramref name="text"/>'s prefixes, measured together:
    /// <c>Width(n)</c> is <c>MeasureText(text[..n], style).Width</c>, exactly, for about the
    /// cost of measuring <paramref name="text"/> once. Something that measures many prefixes
    /// of one string — a search for where a line breaks — asks for this instead of measuring
    /// each. Null when the context cannot answer exactly, which is the default; the caller
    /// then measures each prefix.
    /// </summary>
    ITextPrefixWidths? MeasureTextPrefixes(string text, ResolvedInheritedStyle style) => null;

    /// <summary>
    /// Lays out <paramref name="text"/> into lines under the same wrapping rules
    /// <see cref="DrawText(string, Rect, ResolvedInheritedStyle, TextOverflow, int?)"/> uses,
    /// and returns each line's source character range
    /// and vertical placement. Selectable text uses this to map pointer positions
    /// and selection offsets across wrapped lines. Returns an empty list for empty
    /// text.
    /// </summary>
    IReadOnlyList<TextLineBox> LayoutTextLines(string text, ResolvedInheritedStyle style, float maxWidth, int? maxLines);

    /// <summary>
    /// The metrics of the font <paramref name="style"/> names, read from the font DrawText draws
    /// with (<see cref="IMeasureContext.MeasureFont"/>). The default estimates them from a
    /// measured <c>"0"</c>.
    /// </summary>
    FontMetrics MeasureFont(ResolvedInheritedStyle style)
        => FontMetrics.Estimate(style, MeasureText("0", style));

    /// <summary>
    /// Whether the face <paramref name="style"/> resolves to has its own glyph for
    /// <paramref name="codePoint"/> (<see cref="IMeasureContext.HasGlyph"/>). The default says yes.
    /// </summary>
    bool HasGlyph(ResolvedInheritedStyle style, int codePoint) => true;

    // ── Offset ↔ x mapping for one line of text (docs/plans/14-bidi.md, stage 2) ──
    //
    // Editable and selectable text needs three things from a laid-out line: where
    // a caret goes, what a click means, and what a selection covers. For text that
    // reads left to right all three are prefix widths, which is what the defaults
    // below compute. Once a line contains right-to-left script they stop being the
    // same question: a logical prefix is no longer a visual prefix, and a logical
    // range can be visually discontiguous. A render context that lays text out for
    // real overrides these; one that only measures keeps the prefix answers, which
    // stay exactly right for the text it can render.

    /// <summary>
    /// Visual x, relative to the start of the line, of the caret at logical
    /// <paramref name="offset"/> within <paramref name="lineText"/>.
    /// </summary>
    float TextCaretX(string lineText, ResolvedInheritedStyle style, int offset)
        => offset <= 0 || lineText.Length == 0
            ? 0f
            : MeasureText(lineText[..Math.Min(offset, lineText.Length)], style).Width;

    /// <summary>
    /// The logical offset in <paramref name="lineText"/> whose caret sits nearest
    /// visual x <paramref name="x"/> — what a click at that x selects.
    /// </summary>
    int TextOffsetAtX(string lineText, ResolvedInheritedStyle style, float x)
    {
        if (lineText.Length == 0) return 0;
        // Width is monotonic in prefix length, so the first boundary at or past x
        // is a binary search; the nearer of it and its predecessor wins, so
        // clicking the left half of a character puts the caret before it.
        int lo = 0, hi = lineText.Length;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (TextCaretX(lineText, style, mid) < x) lo = mid + 1;
            else hi = mid;
        }
        int before = Math.Max(0, lo - 1);
        return Math.Abs(TextCaretX(lineText, style, before) - x) <= Math.Abs(TextCaretX(lineText, style, lo) - x)
            ? before
            : lo;
    }

    /// <summary>
    /// The caret position one step to the visual left or right of
    /// <paramref name="offset"/>, for an arrow key on a line that mixes
    /// directions. Returns false — the default — when the line reads left to right
    /// throughout and the caller should keep its own stepping, which knows about
    /// surrogate pairs and clusters.
    /// </summary>
    bool TryMoveCaretVisually(string lineText, ResolvedInheritedStyle style, int offset, bool right, out int result)
    {
        result = offset;
        return false;
    }

    /// <summary>
    /// Visual x-ranges covering the logical range <c>[from, to)</c> of
    /// <paramref name="lineText"/> — one entry for text that reads left to right,
    /// more when the range crosses a direction boundary.
    /// </summary>
    IReadOnlyList<TextRangeBox> TextSelectionRanges(string lineText, ResolvedInheritedStyle style, int from, int to)
    {
        if (to <= from || lineText.Length == 0) return [];
        float x0 = TextCaretX(lineText, style, from);
        float x1 = TextCaretX(lineText, style, to);
        return x1 > x0 ? [new TextRangeBox(x0, x1)] : [];
    }

    void PushClip(Rect clipRect);
    void PushRoundedClip(Rect clipRect, CornerRadius radius);
    void PushTranslate(float x, float y);

    /// <summary>
    /// Draws everything up to the matching <see cref="Pop"/> into a group, then blends the
    /// group at <paramref name="opacity"/>. Without bounds the group is as large as the
    /// current clip — on a raster surface, a window-sized offscreen image per call.
    /// </summary>
    void PushOpacity(float opacity);

    /// <summary>
    /// <see cref="PushOpacity(float)"/> for content known to paint only inside
    /// <paramref name="bounds"/>, in the current coordinates: the group is allocated at that
    /// size. Anything drawn outside it is lost. The default ignores the bounds.
    /// </summary>
    void PushOpacity(float opacity, Rect bounds) => PushOpacity(opacity);

    /// <summary>
    /// The rect that <see cref="DrawText(string, Rect, ResolvedInheritedStyle, TextOverflow, int?, IReadOnlyList{TextSpan}?)"/>
    /// with the same arguments paints inside: every glyph's ink, decorations and span
    /// backgrounds. It can reach past <paramref name="bounds"/> — a word too long for its line,
    /// more lines than the box holds, an italic overhang. Null when the context cannot say,
    /// which is the default.
    /// </summary>
    Rect? TextPaintBounds(string text, Rect bounds, ResolvedInheritedStyle style, TextOverflow overflow, int? maxLines,
        IReadOnlyList<TextSpan>? spans) => null;

    /// <summary>
    /// Concatenates a 2D affine transform onto the current matrix (paired with
    /// <see cref="Pop"/>). Default is a no-op so non-visual contexts, whose Pop is
    /// also a no-op, stay balanced.
    /// </summary>
    void PushTransform(System.Numerics.Matrix3x2 matrix) { }

    void Pop();

    ICanvasDrawingContext CreateCanvasContext(Rect bounds);

    void BeginFrame(float width, float height, float scaleFactor, Color? clearColor = null);
    void EndFrame();
}

/// <summary>
/// One laid-out line of text: its character range in the source string
/// (<see cref="Start"/>, <see cref="Length"/>), its vertical offset from the text
/// origin (<see cref="OffsetY"/>), and its <see cref="Height"/> and drawn
/// <see cref="Width"/>. Line-relative x positions come from
/// <see cref="IRenderContext.TextCaretX"/> and its two companions, which know
/// about direction; measuring a prefix directly only agrees with them for text
/// that reads left to right.
/// </summary>
public readonly record struct TextLineBox(int Start, int Length, float OffsetY, float Height, float Width);

/// <summary>
/// One contiguous visual x-range on a line, relative to the line's start — the
/// shape a selection highlight or a span background is painted as.
/// </summary>
public readonly record struct TextRangeBox(float X0, float X1);

/// <summary>
/// The widths of one string's prefixes (<see cref="IRenderContext.MeasureTextPrefixes"/>).
/// </summary>
public interface ITextPrefixWidths
{
    /// <summary>The string's length: the longest prefix there is a width for.</summary>
    int Length { get; }

    /// <summary>
    /// The width of the string's first <paramref name="length"/> chars, as
    /// <see cref="IRenderContext.MeasureText(string, ResolvedInheritedStyle)"/> measures them.
    /// </summary>
    float Width(int length);
}

public interface IImageSource
{
    Size IntrinsicSize { get; }
    bool IsLoaded { get; }
    string? AlternateText { get; }
}
