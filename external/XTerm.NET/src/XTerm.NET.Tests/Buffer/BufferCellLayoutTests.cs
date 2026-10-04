using System.Runtime.CompilerServices;
using XTerm.Buffer;
using Xunit;

namespace XTerm.Tests.Buffer;

/// <summary>
/// The two facts the buffer's performance rests on, asserted rather than assumed.
///
/// <para>Neither is a measurement, so neither is affected by how busy the machine is — they hold or
/// they do not. That makes them the right things to guard in CI, where a throughput number cannot be
/// trusted to a few per cent but a struct layout can be trusted absolutely.</para>
/// </summary>
public class BufferCellLayoutTests
{
    /// <summary>
    /// No managed reference anywhere in the cell.
    /// </summary>
    /// <remarks>
    /// This is the load-bearing one. GC write barriers are all-or-nothing: a single reference in the
    /// struct makes the collector trace the entire scrollback and makes the runtime emit a barrier
    /// for every cell written or filled. Measured on a 240-column line, a fill cost 239 ns with a
    /// reference and 70 ns without — the fill being what every scroll does.
    ///
    /// A <c>string</c>, an object, or an array field added to <see cref="BufferCell"/> would undo
    /// that at a stroke, and nothing else in the suite would notice.
    /// </remarks>
    [Fact]
    public void The_cell_holds_no_managed_references()
    {
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<BufferCell>(),
            "BufferCell gained a managed reference. Every cell of the scrollback is now traced by "
          + "the GC and every write to one emits a barrier; a 240-column fill goes from about 70 ns "
          + "to about 239 ns. Store an int and intern the rest, as CodePoint and ClusterId do.");
    }

    /// <summary>
    /// And it stays small: cell size is what every scroll copies, so it is paid per cell per line.
    /// Going from 24 to 32 bytes cost scroll-heavy output 22% when it was measured.
    /// </summary>
    /// <remarks>
    /// A deliberate widening is a fine thing to do — but it should be a decision, with the number
    /// re-measured, rather than something that arrives as a side effect of adding a field. Update
    /// this test in the same commit that widens the struct.
    /// </remarks>
    [Fact]
    public void The_cell_is_twenty_four_bytes()
    {
        Assert.Equal(24, Unsafe.SizeOf<BufferCell>());
    }
}
