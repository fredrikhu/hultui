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
/// </summary>
internal struct MeasureCache
{
    // Four slots: measured on a 1000-row view, rows settle at 2 distinct inputs
    // and containers at ~4; an 8-slot variant changed nothing but the footprint.
    private const int Capacity = 4;

    private Entries _entries;
    private int _count;   // 0 = empty; reset is the whole invalidation
    private int _next;    // ring write position

    public bool TryGet(float constraintW, float constraintH, float? forcedW, float? forcedH,
        out Size result)
    {
        for (int i = 0; i < _count; i++)
        {
            ref var e = ref _entries[i];
            if (e.ConstraintW == constraintW && e.ConstraintH == constraintH
                && e.ForcedW == forcedW && e.ForcedH == forcedH)
            {
                result = e.Result;
                return true;
            }
        }
        result = default;
        return false;
    }

    public void Store(float constraintW, float constraintH, float? forcedW, float? forcedH,
        Size result)
    {
        ref var e = ref _entries[_next];
        e.ConstraintW = constraintW;
        e.ConstraintH = constraintH;
        e.ForcedW = forcedW;
        e.ForcedH = forcedH;
        e.Result = result;
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

    private struct Entry
    {
        public float ConstraintW;
        public float ConstraintH;
        public float? ForcedW;
        public float? ForcedH;
        public Size Result;
    }
}
