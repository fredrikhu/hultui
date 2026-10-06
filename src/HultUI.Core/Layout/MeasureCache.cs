using System.Runtime.CompilerServices;
using HultUI.Core.Primitives;

namespace HultUI.Core.Layout;

/// <summary>
/// Small per-element memo of measure results, keyed on the full measurement input
/// (available constraint + forced dimensions). Flex measurement visits each element
/// under a handful of alternating inputs per pass — Phase 1's auto measure,
/// RemeasureFlexedCrossSizes' forced main size, Phase 6's arrange — and the previous
/// single-entry cache thrashed between them, so misses compounded with tree depth
/// (10–22 misses per element per frame on a 1000-row view). Four slots cover the
/// distinct inputs seen in practice; eviction is a ring overwrite. Invalidation is
/// unchanged from the single-entry cache: MarkLayoutDirty resets the element and
/// its ancestors.
/// <para><b>An entry holds for a range of offered heights</b>, not only the one it was
/// measured at: <see cref="Entry.FromH"/> is the least height for which the answer is
/// the same (−∞ for any height; one good at that height alone is marked so), worked out by the layout engine
/// as it measures. The measure pass offers most elements an unbounded height and the layout
/// pass their line's, and nearly every answer is the same for both — a resize of a
/// 2,000-row list measured 14,000 containers twice over for nothing.</para>
/// <para><b>And for a range of offered widths</b>, <see cref="Entry.LoW"/> to
/// <see cref="Entry.HiW"/>: every width for a box whose own width is set, and for one sized from
/// its content the widths its content comes out the same at — text on one line at any width it
/// fits in. A resize offers every box a new width each step, and a row's item is laid out at
/// the width it came out at, not the one it was measured at.</para>
/// </summary>
internal struct MeasureCache
{
    // Four slots: measured on a 1000-row view, rows settle at 2 distinct inputs
    // and containers at ~4; an 8-slot variant changed nothing but the footprint.
    private const int Capacity = 4;

    private Entries _entries;
    // Bytes, so the min-content width fits in the room the two ints took.
    private byte _count;   // 0 = empty; reset is the whole invalidation
    private byte _next;    // ring write position
    private bool _hasMinContentWidth;
    private byte _exactH;  // bit i: entry i holds at its FromH only (see Entry.FromH)
    private float _minContentWidth;

    /// <summary>The element's min-content width (LayoutEngine.MinContentWidth), if worked out
    /// since the last invalidation. No constraint goes into it, so it holds until the content
    /// does not: a shrinking row asked every label for it on every pass, and a styled label
    /// shapes its words to answer — a quarter of a 1,000-label resize.</summary>
    public readonly bool TryGetMinContentWidth(out float width)
    {
        width = _minContentWidth;
        return _hasMinContentWidth;
    }

    public void StoreMinContentWidth(float width)
    {
        _minContentWidth = width;
        _hasMinContentWidth = true;
    }

    /// <param name="validFromH">The least height the matched entry holds from: −∞ for any, NaN for
    /// the one it was measured at alone.</param>
    /// <param name="validW">The matched entry's widths, <see cref="Entry.LoW"/> to <see cref="Entry.HiW"/>.</param>
    public bool TryGet(float constraintW, float constraintH, float? forcedW, float? forcedH,
        out Size result, out float validFromH, out WidthRange validW)
    {
        float fw = forcedW ?? NotForced, fh = forcedH ?? NotForced;
        for (int i = 0; i < _count; i++)
        {
            ref var e = ref _entries[i];
            bool exactH = (_exactH & (1 << i)) != 0;
            if (constraintW >= e.LoW && constraintW <= e.HiW
                && (exactH ? constraintH == e.FromH : constraintH >= e.FromH)
                && e.ForcedW == fw && e.ForcedH == fh)
            {
                result = e.Result;
                validFromH = exactH ? float.NaN : e.FromH;
                validW = new WidthRange(e.LoW, e.HiW);
                return true;
            }
        }
        result = default;
        validFromH = float.NaN;
        validW = WidthRange.At(constraintW);
        return false;
    }

    /// <param name="validFromH">The least height <paramref name="result"/> holds from: −∞ for
    /// any, NaN for <paramref name="constraintH"/> alone.</param>
    /// <param name="validW">The widths <paramref name="result"/> holds for, which take in
    /// <paramref name="constraintW"/>.</param>
    public void Store(float constraintW, float constraintH, float? forcedW, float? forcedH,
        Size result, float validFromH, WidthRange validW)
    {
        ref var e = ref _entries[_next];
        e.LoW = validW.Lo;
        e.HiW = validW.Hi;
        e.ForcedW = forcedW ?? NotForced;
        e.ForcedH = forcedH ?? NotForced;
        e.Result = result;
        int bit = 1 << _next;
        if (float.IsNaN(validFromH))
        {
            e.FromH = constraintH;
            _exactH = (byte)(_exactH | bit);
        }
        else
        {
            // Taking in the height measured at, whatever rounding did to the floor.
            e.FromH = float.MinNative(validFromH, constraintH);
            _exactH = (byte)(_exactH & ~bit);
        }
        _next = (byte)((_next + 1) % Capacity);
        if (_count < Capacity) _count++;
    }

    public void Invalidate()
    {
        _count = 0;
        _next = 0;
        _hasMinContentWidth = false;
    }

    [InlineArray(Capacity)]
    private struct Entries
    {
        private Entry _e0;
    }

    /// <summary>A forced size of none. No forced size is −∞, and a plain float is half a
    /// <c>float?</c>: four entries sit inline in every element, so every byte here is paid per
    /// element — the height floor beside two <c>float?</c> fields grew each element by 16
    /// bytes. Packed, an entry is 28 bytes, with a width range and a height floor whose
    /// exactness is a bit in <see cref="_exactH"/>, where the height measured at used to be.</summary>
    private const float NotForced = float.NegativeInfinity;

    private struct Entry
    {
        /// <summary>The offered widths <see cref="Result"/> holds for, both ends in: −∞ to +∞ for
        /// any, the one it was measured at for that alone.</summary>
        public float LoW, HiW;
        /// <summary>The least offered height at which <see cref="Result"/> holds, for any above
        /// it too (−∞ for any height) — or, with the entry's bit in <see cref="_exactH"/>, the
        /// height it was measured at, which it holds at alone.</summary>
        public float FromH;
        public float ForcedW;   // NotForced for none
        public float ForcedH;   // NotForced for none
        public Size Result;
    }
}

/// <summary>The offered widths a measurement holds for, both ends included (see
/// <see cref="MeasureCache"/>).</summary>
internal readonly record struct WidthRange(float Lo, float Hi)
{
    /// <summary>Whatever width is offered.</summary>
    public static readonly WidthRange Any = new(float.NegativeInfinity, float.PositiveInfinity);

    /// <summary>That width and no other.</summary>
    public static WidthRange At(float width) => new(width, width);

    /// <summary>The widths both hold for.</summary>
    public static WidthRange Both(WidthRange a, WidthRange b) =>
        new(float.MaxNative(a.Lo, b.Lo), float.MinNative(a.Hi, b.Hi));

    public bool Contains(float width) => width >= Lo && width <= Hi;

    /// <summary>This range, widened to take in <paramref name="width"/> — the width a measurement
    /// was taken at, which it holds for whatever rounding did to the ends.</summary>
    public WidthRange With(float width) => new(float.MinNative(Lo, width), float.MaxNative(Hi, width));
}
