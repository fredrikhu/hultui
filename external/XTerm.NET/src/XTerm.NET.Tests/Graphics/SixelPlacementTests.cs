using XTerm.Buffer;
using XTerm.Graphics;
using XTerm.Options;

namespace XTerm.Tests.Graphics;

/// <summary>
/// Where a decoded image lands in the buffer, and where it leaves the cursor.
///
/// <para>Every test here uses 2x3 pixel cells so that a small, hand-written payload still covers
/// several of them. The payload <c>!4~-!4~</c> draws two bands of four full columns -- 4 by 12
/// pixels -- which is two columns by four rows of cells.</para>
/// </summary>
public class SixelPlacementTests
{
    private const string Esc = "\u001b";
    private const string St = Esc + "\\";

    /// <summary>Four pixels wide, twelve tall: two cells across, four down.</summary>
    private const string TwoByFourCells = "#0;2;100;0;0!4~-!4~";

    private static Terminal Fresh(int rows = 10, Action<TerminalOptions>? configure = null)
    {
        var options = new TerminalOptions
        {
            Cols = 20,
            Rows = rows,
            CellWidthPixels = 2,
            CellHeightPixels = 3
        };
        configure?.Invoke(options);
        return new Terminal(options);
    }

    private static void WriteSixel(Terminal terminal, string body = TwoByFourCells)
        => terminal.Write($"{Esc}P0;1;0q{body}{St}");

    private static BufferCell Cell(Terminal terminal, int col, int screenRow)
        => terminal.Buffer.Lines[terminal.Buffer.YBase + screenRow]![col];

    [Fact]
    public void An_image_covers_one_cell_per_tile()
    {
        var terminal = Fresh();
        WriteSixel(terminal);

        var image = ImageAssertions.ImageAt(terminal, 0, 0);
        Assert.NotNull(image);
        Assert.Equal(2, image!.Cols);
        Assert.Equal(4, image.Rows);

        // One run per line, each covering the picture's full width and carrying its own slice of
        // the source — which is what the per-cell tile grid said, expressed once per row instead of
        // once per cell.
        for (int row = 0; row < 4; row++)
        {
            var placement = ImageAssertions.PlacementAt(terminal, 0, row);
            Assert.NotNull(placement);
            Assert.True(ReferenceEquals(ImageAssertions.ImageAt(terminal, 0, row), image),
                $"row {row} should show part of the image");

            Assert.Equal(0, placement!.Value.Column);
            Assert.Equal(2, placement.Value.Cols);
            Assert.Equal(0, placement.Value.SrcX);
            Assert.Equal(row * terminal.Options.CellHeightPixels, placement.Value.SrcY);
        }
    }

    [Fact]
    public void Cells_beyond_the_image_are_left_alone()
    {
        var terminal = Fresh();
        WriteSixel(terminal);

        Assert.Null(ImageAssertions.ImageAt(terminal, 2, 0));
        Assert.Null(ImageAssertions.ImageAt(terminal, 0, 4));
    }

    [Fact]
    public void An_image_starts_at_the_cursor()
    {
        var terminal = Fresh();
        terminal.Write($"{Esc}[3;6H"); // row 3, column 6, one-based
        WriteSixel(terminal);

        Assert.True(ReferenceEquals(ImageAssertions.ImageAt(terminal, 5, 2), ImageAssertions.ImageAt(terminal, 6, 2)));
        Assert.NotNull(ImageAssertions.ImageAt(terminal, 5, 2));
        Assert.Null(ImageAssertions.ImageAt(terminal, 4, 2));
    }

    [Fact]
    public void The_cursor_ends_below_the_image_at_the_left_margin()
    {
        var terminal = Fresh();
        terminal.Write($"{Esc}[1;6H");
        WriteSixel(terminal);

        Assert.Equal(0, terminal.Buffer.X);
        Assert.Equal(4, terminal.Buffer.Y);
    }

    [Fact]
    public void Text_after_an_image_continues_below_it()
    {
        var terminal = Fresh();
        WriteSixel(terminal);
        terminal.Write("after");

        Assert.Equal("after", terminal.GetLine(terminal.Buffer.YBase + 4));
    }

    /// <summary>Mode 8452 leaves the cursor beside the image instead of beneath it.</summary>
    [Fact]
    public void Mode_8452_leaves_the_cursor_to_the_right_of_the_image()
    {
        var terminal = Fresh();
        terminal.Write($"{Esc}[?8452h");
        WriteSixel(terminal);

        Assert.Equal(2, terminal.Buffer.X);
        Assert.Equal(3, terminal.Buffer.Y);
    }

    /// <summary>
    /// DECSDM set is the older display behaviour: pinned to the top-left, clipped rather than
    /// scrolled, cursor untouched. Its sense reads backwards, which is why it is worth pinning down.
    /// </summary>
    [Fact]
    public void Decsdm_pins_the_image_to_the_top_left_and_leaves_the_cursor_alone()
    {
        var terminal = Fresh();
        terminal.Write($"{Esc}[?80h");
        terminal.Write($"{Esc}[3;6H");
        WriteSixel(terminal);

        Assert.NotNull(ImageAssertions.ImageAt(terminal, 0, 0));
        Assert.Null(ImageAssertions.ImageAt(terminal, 5, 2));
        Assert.Equal(5, terminal.Buffer.X);
        Assert.Equal(2, terminal.Buffer.Y);
    }

    [Fact]
    public void Decsdm_reset_restores_the_scrolling_behaviour()
    {
        var terminal = Fresh();
        terminal.Write($"{Esc}[?80h");
        terminal.Write($"{Esc}[?80l");
        terminal.Write($"{Esc}[3;6H");
        WriteSixel(terminal);

        Assert.NotNull(ImageAssertions.ImageAt(terminal, 5, 2));
    }

    /// <summary>
    /// An image that runs off the bottom pushes the screen up, exactly as a run of text would.
    /// </summary>
    [Fact]
    public void An_image_that_runs_past_the_bottom_scrolls_the_screen()
    {
        var terminal = Fresh(rows: 5);
        terminal.Write($"{Esc}[4;1H"); // last-but-one row
        WriteSixel(terminal);

        // Four image rows plus the cursor's own row need five: the screen scrolled until they fit.
        for (int row = 0; row < 4; row++)
        {
            var placement = ImageAssertions.PlacementAt(terminal, 0, row);
            Assert.NotNull(placement);
            Assert.Equal(row * terminal.Options.CellHeightPixels, placement!.Value.SrcY);
        }

        Assert.Equal(0, terminal.Buffer.X);
        Assert.Equal(4, terminal.Buffer.Y);
    }

    [Fact]
    public void An_image_taller_than_the_screen_keeps_its_last_rows()
    {
        var terminal = Fresh(rows: 3);
        WriteSixel(terminal);

        // Three rows of screen, four of image, and the cursor still needs a row of its own below
        // it -- so the picture scrolled up until its last two rows and the cursor fit.
        Assert.Equal(2 * terminal.Options.CellHeightPixels, ImageAssertions.PlacementAt(terminal, 0, 0)!.Value.SrcY);
        Assert.Equal(3 * terminal.Options.CellHeightPixels, ImageAssertions.PlacementAt(terminal, 0, 1)!.Value.SrcY);
        Assert.Equal(2, terminal.Buffer.Y);
    }

    [Fact]
    public void Decsdm_clips_a_tall_image_instead_of_scrolling()
    {
        var terminal = Fresh(rows: 3);
        terminal.Write($"{Esc}[?80h");
        WriteSixel(terminal);

        // Pinned at the top, so the first rows are the ones that survive.
        Assert.Equal(0 * terminal.Options.CellHeightPixels, ImageAssertions.PlacementAt(terminal, 0, 0)!.Value.SrcY);
        Assert.Equal(2 * terminal.Options.CellHeightPixels, ImageAssertions.PlacementAt(terminal, 0, 2)!.Value.SrcY);
    }

    [Fact]
    public void An_image_is_clipped_at_the_right_edge()
    {
        var terminal = Fresh(configure: o => o.Cols = 6);
        terminal.Write($"{Esc}[1;6H"); // one column from the right edge
        WriteSixel(terminal);

        Assert.NotNull(ImageAssertions.ImageAt(terminal, 5, 0));
        Assert.Equal(0 * terminal.Options.CellWidthPixels, ImageAssertions.PlacementAt(terminal, 5, 0)!.Value.SrcX);
    }

    /// <summary>
    /// Image cells carry a space, so selecting a picture and copying it produces blanks rather
    /// than something unreadable.
    /// </summary>
    [Fact]
    public void An_image_cell_reads_as_a_space()
    {
        var terminal = Fresh();
        WriteSixel(terminal);

        var cell = Cell(terminal, 0, 0);
        Assert.Equal(" ", cell.Content);
        Assert.Equal(1, cell.Width);
        Assert.Equal(0x20, cell.CodePoint);
        Assert.Equal("", terminal.GetLine(terminal.Buffer.YBase));
    }

    /// <summary>
    /// A row that gained an image has to repaint. The render cache a host hangs off the line is
    /// dropped by the same write path that puts the tiles there.
    /// </summary>
    [Fact]
    public void Placing_an_image_drops_the_lines_render_cache()
    {
        var terminal = Fresh();
        var line = terminal.Buffer.Lines[terminal.Buffer.YBase]!;
        line.Cache = "a host's cached row";

        WriteSixel(terminal);

        Assert.Null(line.Cache);
    }

    [Fact]
    public void Two_images_do_not_share_tiles()
    {
        var terminal = Fresh();
        WriteSixel(terminal);
        WriteSixel(terminal);

        var first = ImageAssertions.ImageAt(terminal, 0, 0);
        var second = ImageAssertions.ImageAt(terminal, 0, 4);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.False(ReferenceEquals(first, second));
        Assert.NotEqual(first!.Id, second!.Id);
    }
}
