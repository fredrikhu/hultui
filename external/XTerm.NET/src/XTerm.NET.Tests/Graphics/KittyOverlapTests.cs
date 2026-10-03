using XTerm.Options;

namespace XTerm.Tests.Graphics;

/// <summary>
/// Overlapping placements: two pictures over the same columns are two runs, and neither destroys
/// the other.
///
/// <para>Nothing here needs a mechanism. A picture is a run held by the line, so covering one has no
/// way to modify it — a translucent picture blends over what it covers because what it covers is
/// still on the line, and deleting the front one reveals the back one whole because the back one was
/// never touched. Where images lived in cells, the covering write destroyed them and both of those
/// had to be built.</para>
///
/// <para>What still has to be got right is which runs a delete takes, since a placement spans
/// several lines and is found through one cell of one of them.</para>
/// </summary>
public class KittyOverlapTests
{
    private const string Esc = "\u001b";
    private const string St = Esc + "\\";

    private static Terminal Fresh()
        => new(new TerminalOptions
        {
            Cols = 30,
            Rows = 12,
            CellWidthPixels = 2,
            CellHeightPixels = 3
        });

    private static string Apc(string control, string payload = "")
        => payload.Length == 0 ? $"{Esc}_G{control}{St}" : $"{Esc}_G{control};{payload}{St}";

    /// <summary>A 4x6 picture, which covers two cells by two at the metrics above.</summary>
    private static string Pixels() => Convert.ToBase64String(new byte[4 * 6 * 4]);

    private static Terminal WithTwoImages()
    {
        var terminal = Fresh();
        terminal.Write(Apc("a=t,i=1,f=32,s=4,v=6,q=2", Pixels()));
        terminal.Write(Apc("a=t,i=2,f=32,s=4,v=6,q=2", Pixels()));
        return terminal;
    }

    private static void PlaceAt(Terminal terminal, uint id, int col, int row, int z)
    {
        terminal.Write($"{Esc}[{row + 1};{col + 1}H");
        terminal.Write(Apc($"a=p,i={id},z={z},C=1,q=2"));
    }

    // ---- the stack --------------------------------------------------------------------------------

    /// <summary>Three pictures over one cell are all kept, ordered front to back.</summary>
    [Fact]
    public void A_cell_keeps_every_picture_covering_it()
    {
        var terminal = WithTwoImages();
        PlaceAt(terminal, 1, 0, 0, z: 1);
        PlaceAt(terminal, 2, 0, 0, z: 5);
        PlaceAt(terminal, 1, 0, 0, z: 3);

        Assert.Equal(new[] { 5, 3, 1 },
                     ImageAssertions.StackAt(terminal, 0, 0).Select(p => (int)p.ZIndex));
    }

    /// <summary>
    /// A picture arriving behind one already there is recorded, not dropped — the case a cell
    /// holding a single placement could not express at all.
    /// </summary>
    [Fact]
    public void A_picture_placed_behind_another_is_still_recorded()
    {
        var terminal = WithTwoImages();
        PlaceAt(terminal, 1, 0, 0, z: 5);

        PlaceAt(terminal, 2, 0, 0, z: 1);

        Assert.Equal(2, ImageAssertions.StackAt(terminal, 0, 0).Count);
    }

    /// <summary>Every row of one placement shares a serial, and no two placements share one.</summary>
    [Fact]
    public void One_placement_is_one_serial_across_all_its_rows()
    {
        var terminal = WithTwoImages();
        PlaceAt(terminal, 1, 0, 0, z: 0);

        var first = ImageAssertions.PlacementAt(terminal, 0, 0)!.Value.Serial;
        var second = ImageAssertions.PlacementAt(terminal, 0, 1)!.Value.Serial;
        Assert.Equal(first, second);

        PlaceAt(terminal, 2, 0, 0, z: 1);
        Assert.NotEqual(first, ImageAssertions.StackAt(terminal, 0, 0)[0].Serial);
    }

    // ---- the reveal -------------------------------------------------------------------------------

    /// <summary>Deleting the front picture brings back the one it was covering.</summary>
    [Fact]
    public void Deleting_the_front_picture_reveals_the_one_behind()
    {
        var terminal = WithTwoImages();
        PlaceAt(terminal, 1, 0, 0, z: 1);
        var behind = ImageAssertions.PlacementAt(terminal, 0, 0)!.Value.Serial;
        PlaceAt(terminal, 2, 0, 0, z: 5);

        terminal.Write(Apc("a=d,d=i,i=2,q=2"));

        var stack = ImageAssertions.StackAt(terminal, 0, 0);
        Assert.Single(stack);
        Assert.Equal(behind, stack[0].Serial);
    }

    /// <summary>
    /// The whole of it, not the part that was not covered. A picture two cells wide with something
    /// dropped over its second cell used to come back one cell wide.
    /// </summary>
    [Fact]
    public void The_revealed_picture_has_no_hole_in_it()
    {
        var terminal = WithTwoImages();
        PlaceAt(terminal, 1, 0, 0, z: 1);          // columns 0-1
        var behind = ImageAssertions.PlacementAt(terminal, 0, 0)!.Value.Serial;
        PlaceAt(terminal, 2, 1, 0, z: 5);          // columns 1-2, covering its second cell

        terminal.Write(Apc("a=d,d=i,i=2,q=2"));

        Assert.Equal(behind, ImageAssertions.PlacementAt(terminal, 0, 0)!.Value.Serial);
        Assert.Equal(behind, ImageAssertions.PlacementAt(terminal, 1, 0)!.Value.Serial);
    }

    /// <summary>
    /// A positional delete takes the WHOLE placement, every row of it — not only the row the named
    /// cell is on. That is what the serial is for: a run knows nothing about the rows above and
    /// below it.
    /// </summary>
    [Fact]
    public void A_positional_delete_takes_every_row_of_the_placement()
    {
        var terminal = WithTwoImages();
        PlaceAt(terminal, 1, 0, 0, z: 0);          // two rows tall
        Assert.True(ImageAssertions.IsImageAt(terminal, 0, 1));

        terminal.Write(Apc("a=d,d=p,x=1,y=1,q=2"));   // one-based: the cell at 0,0

        Assert.False(ImageAssertions.IsImageAt(terminal, 0, 0));
        Assert.False(ImageAssertions.IsImageAt(terminal, 0, 1));
    }

    /// <summary>
    /// And it reaches a covered picture. Selecting only what is on top would make "delete what is
    /// here" depend on what happened to be stacked over it.
    /// </summary>
    [Fact]
    public void A_positional_delete_reaches_a_covered_picture()
    {
        var terminal = WithTwoImages();
        PlaceAt(terminal, 1, 0, 0, z: 1);
        PlaceAt(terminal, 2, 0, 0, z: 5);

        terminal.Write(Apc("a=d,d=p,x=1,y=1,q=2"));

        Assert.Empty(ImageAssertions.StackAt(terminal, 0, 0));
    }

    /// <summary>Deleting by z takes only the placements at that depth.</summary>
    [Fact]
    public void Deleting_by_z_leaves_the_other_depths()
    {
        var terminal = WithTwoImages();
        PlaceAt(terminal, 1, 0, 0, z: 1);
        PlaceAt(terminal, 2, 0, 0, z: 5);

        terminal.Write(Apc("a=d,d=z,z=5,q=2"));

        Assert.Equal(new[] { 1 },
                     ImageAssertions.StackAt(terminal, 0, 0).Select(p => (int)p.ZIndex));
    }

    /// <summary>Erasing takes the whole stack; a picture showing through a cleared screen is a leak.</summary>
    [Fact]
    public void Erasing_clears_every_layer()
    {
        var terminal = WithTwoImages();
        PlaceAt(terminal, 1, 0, 0, z: -1);
        PlaceAt(terminal, 2, 0, 0, z: 5);

        terminal.Write($"{Esc}[2J");

        Assert.Empty(ImageAssertions.StackAt(terminal, 0, 0));
    }

    // ---- what a covered picture costs -------------------------------------------------------------

    /// <summary>
    /// A cell covered by one picture holds one run. The structure is there for the rare overlap and
    /// must not cost the common case anything.
    /// </summary>
    [Fact]
    public void One_picture_over_a_cell_is_one_run()
    {
        var terminal = WithTwoImages();
        PlaceAt(terminal, 1, 0, 0, z: 0);

        Assert.Single(ImageAssertions.PlacementsOn(terminal, 0));
    }

    /// <summary>
    /// Typing across a covered picture leaves both of them, because a Kitty run is an overlay. The
    /// Sixel case is the opposite and is checked in <c>SixelPlacementTests</c>.
    /// </summary>
    [Fact]
    public void Typing_across_a_stack_leaves_both_pictures()
    {
        var terminal = WithTwoImages();
        PlaceAt(terminal, 1, 0, 0, z: -1);
        PlaceAt(terminal, 2, 0, 0, z: 4);

        terminal.Write($"{Esc}[1;1HX");

        Assert.Equal(2, ImageAssertions.StackAt(terminal, 0, 0).Count);
        Assert.Equal("X", terminal.Buffer.Lines[terminal.Buffer.YBase]![0].Content);
    }
}
