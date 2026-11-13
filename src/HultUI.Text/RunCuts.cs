namespace HultUI.Text;

/// <summary>
/// Where a shaped left-to-right run can be cut without shaping its prefix again. HarfBuzz marks
/// the glyphs at whose cluster a cut is unsafe (UNSAFE_TO_BREAK); at the start of any other
/// cluster, the characters before it shaped as a string of their own come out as the glyphs
/// before it at the same advances, so their width is the pen there, summed as the shaper sums
/// it: the same float, to the bit. Only when that prefix guesses the run's script: the shaper
/// guesses it from the first character with a script of its own, so a prefix that stops before
/// that one (punctuation or digits alone) is shaped in another and is measured the long way.
/// </summary>
internal sealed class RunCuts(int[] starts, float[] pens, int firstStrong)
{
    /// <summary>The top bit of a glyph's start: a cut at its cluster is unsafe.</summary>
    public const int Unsafe = int.MinValue;

    /// <summary>Cuts that read no prefix, for a word the shaper gave none (a run shaped right to
    /// left): kept in their place, they keep the word from being shaped again for its cuts.</summary>
    public static readonly RunCuts None = new([], [], int.MaxValue);

    // Per glyph: where in the run's text its cluster starts, with Unsafe set where a cut there is unsafe.
    private readonly int[] _starts = starts;

    // Per glyph: the pen before it.
    private readonly float[] _pens = pens;

    // The first character with a script of its own, -1 when the run has none.
    private readonly int _firstStrong = firstStrong;

    /// <summary>The bytes the cuts keep, for the word cache's weight.</summary>
    public int Bytes => 72 + 8 * _starts.Length;

    /// <summary>The width and glyph count of the run's first <paramref name="length"/> characters,
    /// shaped alone, when a cut there is safe: false where it is not, and inside a cluster.</summary>
    public bool TryPrefix(int length, out float width, out int glyphs)
    {
        width = 0;
        glyphs = 0;
        if (length <= _firstStrong) return false;
        // The first glyph whose cluster starts at or after the cut: clusters only grow, left to right.
        var starts = _starts;
        int lo = 0, hi = starts.Length;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if ((starts[mid] & ~Unsafe) < length) lo = mid + 1;
            else hi = mid;
        }
        // Past the last cluster, inside a cluster, or unsafe (the top bit makes it negative).
        if (lo == starts.Length || starts[lo] != length) return false;
        width = _pens[lo];
        glyphs = lo;
        return true;
    }
}

/// <summary>
/// A shaper that can also say where its left-to-right runs can be cut (<see cref="RunCuts"/>):
/// the word cache keeps them with the long words a prefix table shapes, and the table reads such a
/// word cut short off them instead of shaping it again. In text without spaces a table's word is
/// the whole font run from the row start, and a TextArea's line search cuts it at every probe: a
/// resize of Junction's Files view on a Japanese README made 14,513 HarfBuzz calls, now 11,360.
/// </summary>
internal interface IShapesWithCuts
{
    /// <summary><see cref="ITextShaper.Shape"/> left to right, and where the run can be cut; null
    /// when the shaper cannot say.</summary>
    ShapedRun Shape(string text, IResolvedFont font, float fontSize, out RunCuts? cuts);
}
