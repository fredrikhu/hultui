namespace HultUI.Core.RichText;

/// <summary>
/// How list items are numbered and marked: one definition for the editor, which draws them, and
/// the writers, which spell them out.
/// </summary>
internal static class RichLists
{
    /// <summary>How many depths the counters follow: one more than the deepest indent, for a
    /// paragraph continuing an item at that depth.</summary>
    public const int Depths = RichBlock.MaxIndent + 2;

    /// <summary>
    /// The number of <paramref name="block"/>, for blocks read in order with one set of
    /// <paramref name="counters"/> (<see cref="Depths"/> long): an item continues the count of
    /// the items before it at its depth until a block at a shallower depth, or a different list,
    /// ends it. 0 for anything but a numbered item.
    /// </summary>
    public static int Next(RichBlock block, Span<int> counters)
    {
        int depth = Math.Clamp(block.Indent, 0, counters.Length - 1);
        if (block.IsListItem)
        {
            counters[(depth + 1)..].Clear();
            if (block.Kind == BlockKind.NumberedItem) return ++counters[depth];
            counters[depth] = 0;
            return 0;
        }
        // A paragraph continuing an item at depth - 1 keeps that item's count; anything else
        // at depth 0 ends every list.
        counters[(block.Kind == BlockKind.Paragraph ? depth : 0)..].Clear();
        return 0;
    }

    /// <summary>The bullet at <paramref name="depth"/>, as the editor draws it: a disc, a circle,
    /// a square, and round again.</summary>
    public static char Bullet(int depth) => (depth % 3) switch
    {
        0 => '•',
        1 => '◦',
        _ => '▪',
    };

    /// <summary>A task item's box, ticked or empty.</summary>
    public static char Box(bool done) => done ? '☑' : '☐';
}
