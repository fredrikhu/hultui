using HultUI.Text;

namespace HultUI.Rendering.Skia;

/// <summary>
/// Which glyphs of a run can put ink inside an x-range: the part of the run a colour stretch of a
/// spanned line has to draw (<see cref="SkiaRenderContext"/>'s sub-blobs, plan 17, round 3).
/// A glyph at <c>X</c> inks no further than <c>[X + left, X + right]</c>, its face's bounding
/// box with a margin; anything outside the stretch's clip is clipped away, so leaving it out of
/// the draw changes no pixel.
/// </summary>
internal static class GlyphRange
{
    /// <summary>True when no glyph sits left of the one before it — what the binary search in
    /// <see cref="Reaching"/> needs. Right-to-left runs are laid out left to right too; a mark
    /// pulled back over its base, or a reordered cluster, can make X go backwards.</summary>
    internal static bool XOrdered(ReadOnlySpan<GlyphPosition> glyphs)
    {
        for (int i = 1; i < glyphs.Length; i++)
            if (glyphs[i].X < glyphs[i - 1].X)
                return false;
        return true;
    }

    /// <summary>
    /// <c>[Start, End)</c>: from the first glyph whose ink <c>[X + left, X + right]</c> can reach
    /// <c>[x0, x1)</c> to the last that can, run-relative; empty (<c>Start == End</c>) when none
    /// can. Glyphs in X order are found by two binary searches, and every glyph between the two
    /// reaches; otherwise one pass finds the first and the last, and the glyphs between them are
    /// drawn whether they reach or not.
    /// </summary>
    internal static (int Start, int End) Reaching(ReadOnlySpan<GlyphPosition> glyphs, bool xOrdered,
        float left, float right, float x0, float x1)
    {
        int n = glyphs.Length;
        if (xOrdered)
        {
            // The first glyph whose ink ends after x0...
            int lo = 0, hi = n;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (glyphs[mid].X + right > x0) hi = mid;
                else lo = mid + 1;
            }
            int start = lo;
            // ...and the first after it whose ink starts at or past x1.
            hi = n;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (glyphs[mid].X + left >= x1) hi = mid;
                else lo = mid + 1;
            }
            return (start, lo);
        }

        int first = -1, last = -1;
        for (int i = 0; i < n; i++)
        {
            float x = glyphs[i].X;
            if (x + right > x0 && x + left < x1)
            {
                if (first < 0) first = i;
                last = i;
            }
        }
        return first < 0 ? (0, 0) : (first, last + 1);
    }
}
