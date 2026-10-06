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
/// measured at: <see cref="Entry.ValidFromH"/> is the least height for which the answer is
/// the same (−∞ for any height, NaN for that height alone), worked out by the layout engine
/// as it measures. The measure pass offers most elements an unbounded height and the layout
/// pass their line's, and nearly every answer is the same for both — a resize of a
/// 2,000-row list measured 14,000 containers twice over for nothing.</para>
/// </summary>
internal struct MeasureCache
{
    // Four slots: measured on a 1000-row view, rows settle at 2 distinct inputs
    // and containers at ~4; an 8-slot variant changed nothing but the footprint.
    private const int Capacity = 4;

    private Entries _entries;
    private int _count;   // 0 = empty; reset is the whole invalidation
    private int _next;    // ring write position

    /// <param name="validFromH">The matched entry's <see cref="Entry.ValidFromH"/>.</param>
    public bool TryGet(float constraintW, float constraintH, float? forcedW, float? forcedH,
        out Size result, out float validFromH)
    {
        float fw = forcedW ?? NotForced, fh = forcedH ?? NotForced;
        for (int i = 0; i < _count; i++)
        {
            ref var e = ref _entries[i];
            // `>=` against NaN is false: an entry valid at its own height only matches it exactly.
            if (e.ConstraintW == constraintW
                && (e.ConstraintH == constraintH || constraintH >= e.ValidFromH)
                && e.ForcedW == fw && e.ForcedH == fh)
            {
                result = e.Result;
                validFromH = e.ValidFromH;
                return true;
            }
        }
        result = default;
        validFromH = float.NaN;
        return false;
    }

    public void Store(float constraintW, float constraintH, float? forcedW, float? forcedH,
        Size result, float validFromH)
    {
        ref var e = ref _entries[_next];
        e.ConstraintW = constraintW;
        e.ConstraintH = constraintH;
        e.ForcedW = forcedW ?? NotForced;
        e.ForcedH = forcedH ?? NotForced;
        e.Result = result;
        e.ValidFromH = validFromH;
        _next = (_next + 1) % Capacity;
        if (_count < Capacity) _count++;
    }

    public void Invalidate()
    {
        _count = 0;
        _next = 0;
    }

    [InlineArray(Capacity)]
    private struct Entries
    {
        private Entry _e0;
    }

    /// <summary>A forced size of none. No forced size is −∞, and a plain float is half a
    /// <c>float?</c> — which matters more than it looks: four entries sit inline in every
    /// element, and when ValidFromH was added beside two <c>float?</c> fields the element grew by
    /// 16 bytes and the renderer's walk over a 1,000-row list (one cull test per row) took twice
    /// as long. Packed, an entry is 28 bytes, 4 fewer than before ValidFromH.</summary>
    private const float NotForced = float.NegativeInfinity;

    private struct Entry
    {
        public float ConstraintW;
        public float ConstraintH;
        public float ForcedW;   // NotForced for none
        public float ForcedH;   // NotForced for none
        public Size Result;
        /// <summary>The least offered height at which <see cref="Result"/> still holds: −∞ for
        /// any, NaN for <see cref="ConstraintH"/> alone.</summary>
        public float ValidFromH;
    }
}
