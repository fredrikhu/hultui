using XTerm.Graphics;

namespace XTerm.Tests.Graphics;

/// <summary>
/// A placement is what a cell references: which part of a picture it shows, at what size. Kitty
/// transmits an image once and may then show it several times, cropped and scaled differently, so
/// the view of the pixels has to live apart from the pixels.
///
/// <para>The load-bearing detail is that the two scalings are different arithmetic rather than one
/// formula with a special case. Natural keeps a picture at its own size and lets the edge tiles fall
/// short; Stretched divides the source across the cell box it was told to fill. Folding them
/// together would quietly resample every Sixel image, which is what the first test here guards.</para>
/// </summary>
public class ImagePlacementTests
{
    private const int CellWidth = 14;
    private const int CellHeight = 15;

    private static TerminalImage Image(int width, int height)
        => new(new byte[width * height * TerminalImage.BytesPerPixel], width, height, CellWidth, CellHeight);

    // ---- the regression guard ---------------------------------------------------------------

    /// <summary>
    /// A natural placement must lay its tiles exactly where the image itself would. Sixel goes
    /// through a placement now, and a difference of a pixel or two per tile would be a resampled
    /// picture with nothing to report it.
    /// </summary>
    /// <remarks>
    /// The dimensions are the ones a real Sixel produced in testing: 1160x870 over a 14x15 cell,
    /// which needs 83 columns. 83 times 14 is 1162, so the source does NOT divide evenly into the
    /// cells it occupies — precisely the case where a proportional division would disagree.
    /// </remarks>
    [Fact]
    public void A_natural_placement_lays_tiles_exactly_where_the_image_does()
    {
        var image = Image(1160, 870);
        var placement = ImagePlacement.Natural(image);

        Assert.Equal(image.Cols, placement.Cols);
        Assert.Equal(image.Rows, placement.Rows);

        for (int row = 0; row < image.Rows; row++)
        {
            for (int col = 0; col < image.Cols; col++)
            {
                var onImage = image.TryGetTileSource(col, row, out var ix, out var iy, out var iw, out var ih);
                var onPlacement = placement.TryGetTileSource(col, row, out var px, out var py, out var pw, out var ph);

                Assert.Equal(onImage, onPlacement);
                Assert.True((ix, iy, iw, ih) == (px, py, pw, ph),
                    $"tile ({col},{row}) moved: image gave ({ix},{iy},{iw},{ih}), placement gave ({px},{py},{pw},{ph})");
            }
        }
    }

    /// <summary>
    /// And the two modes really do disagree, which is why both exist. Documented here so nobody
    /// simplifies one into the other.
    /// </summary>
    [Fact]
    public void The_two_scalings_disagree_on_an_image_that_does_not_divide_evenly()
    {
        var image = Image(1160, 870);
        var natural = ImagePlacement.Natural(image);
        var stretched = new ImagePlacement(image, 0, 0, 0, 1160, 870,
                                           natural.Cols, natural.Rows, ImageScaling.Stretched);

        natural.TryGetTileSource(0, 0, out _, out _, out var naturalWidth, out _);
        stretched.TryGetTileSource(0, 0, out _, out _, out var stretchedWidth, out _);

        Assert.Equal(14, naturalWidth);
        Assert.Equal(13, stretchedWidth);
    }

    // ---- natural ------------------------------------------------------------------------------

    [Fact]
    public void A_natural_edge_tile_reports_only_the_pixels_it_covers()
    {
        // Seven pixels wide over 14-pixel cells: one column holding half a cell.
        var placement = ImagePlacement.Natural(Image(7, 15));

        Assert.Equal(1, placement.Cols);
        Assert.True(placement.TryGetTileSource(0, 0, out var x, out var y, out var w, out var h));
        Assert.Equal((0, 0, 7, 15), (x, y, w, h));

        placement.GetTileCoverage(w, h, out var cellsWide, out var cellsHigh);
        Assert.Equal(0.5, cellsWide);
        Assert.Equal(1.0, cellsHigh);
    }

    // ---- stretched ----------------------------------------------------------------------------

    /// <summary>What `c=` and `r=` mean: fill the box asked for, whatever the source size.</summary>
    [Fact]
    public void A_stretched_placement_fills_the_cell_box_it_was_given()
    {
        // 40x40 into 4 columns by 2 rows, which is what chafa asks for.
        var image = Image(40, 40);
        var placement = new ImagePlacement(image, 0, 0, 0, 40, 40, 4, 2, ImageScaling.Stretched);

        Assert.True(placement.TryGetTileSource(0, 0, out var x, out var y, out var w, out var h));
        Assert.Equal((0, 0, 10, 20), (x, y, w, h));

        Assert.True(placement.TryGetTileSource(3, 1, out x, out y, out w, out h));
        Assert.Equal((30, 20, 10, 20), (x, y, w, h));
    }

    /// <summary>Every tile fills its cell, so the destination is never scaled down.</summary>
    [Fact]
    public void A_stretched_tile_always_covers_a_whole_cell()
    {
        var placement = new ImagePlacement(Image(41, 41), 0, 0, 0, 41, 41, 4, 2, ImageScaling.Stretched);

        placement.TryGetTileSource(3, 1, out _, out _, out var w, out var h);
        placement.GetTileCoverage(w, h, out var cellsWide, out var cellsHigh);

        Assert.Equal(1.0, cellsWide);
        Assert.Equal(1.0, cellsHigh);
    }

    /// <summary>
    /// Tiles must abut exactly. Rounding each tile's own width independently would leave a seam of
    /// dropped pixels, or overlap and draw a column twice.
    /// </summary>
    [Fact]
    public void Stretched_tiles_meet_without_a_seam_or_an_overlap()
    {
        // 41 does not divide by 4, so the rounding has somewhere to go wrong.
        var placement = new ImagePlacement(Image(41, 41), 0, 0, 0, 41, 41, 4, 3, ImageScaling.Stretched);

        int nextX = 0;
        for (int col = 0; col < placement.Cols; col++)
        {
            placement.TryGetTileSource(col, 0, out var x, out _, out var w, out _);
            Assert.True(x == nextX, $"column {col} starts at {x}, but the one before it ended at {nextX}");
            nextX = x + w;
        }
        Assert.Equal(41, nextX);

        int nextY = 0;
        for (int row = 0; row < placement.Rows; row++)
        {
            placement.TryGetTileSource(0, row, out _, out var y, out _, out var h);
            Assert.True(y == nextY, $"row {row} starts at {y}, but the one before it ended at {nextY}");
            nextY = y + h;
        }
        Assert.Equal(41, nextY);
    }

    // ---- cropping -----------------------------------------------------------------------------

    [Fact]
    public void A_crop_offsets_every_tile()
    {
        var placement = new ImagePlacement(Image(100, 100), 0, 20, 30, 40, 60, 2, 3, ImageScaling.Stretched);

        Assert.True(placement.TryGetTileSource(0, 0, out var x, out var y, out var w, out var h));
        Assert.Equal((20, 30, 20, 20), (x, y, w, h));

        Assert.True(placement.TryGetTileSource(1, 2, out x, out y, out w, out h));
        Assert.Equal((40, 70, 20, 20), (x, y, w, h));
    }

    /// <summary>
    /// The crop arrives from another process. A rectangle that runs off the edge is clamped to what
    /// exists rather than refused — a picture slightly smaller than asked for beats no picture.
    /// </summary>
    [Fact]
    public void A_crop_running_off_the_edge_is_clamped()
    {
        var placement = new ImagePlacement(Image(100, 100), 0, 80, 80, 500, 500, 2, 2, ImageScaling.Stretched);

        Assert.Equal(20, placement.SourceWidth);
        Assert.Equal(20, placement.SourceHeight);

        Assert.True(placement.TryGetTileSource(1, 1, out var x, out var y, out var w, out var h));
        Assert.Equal((90, 90, 10, 10), (x, y, w, h));
    }

    // ---- bounds -------------------------------------------------------------------------------

    [Fact]
    public void A_tile_outside_the_placement_is_refused()
    {
        var placement = new ImagePlacement(Image(40, 40), 0, 0, 0, 40, 40, 4, 2, ImageScaling.Stretched);

        Assert.False(placement.TryGetTileSource(4, 0, out _, out _, out _, out _));
        Assert.False(placement.TryGetTileSource(0, 2, out _, out _, out _, out _));
        Assert.False(placement.TryGetTileSource(-1, 0, out _, out _, out _, out _));
    }

    [Fact]
    public void A_placement_covering_no_cells_is_refused()
    {
        var image = Image(40, 40);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ImagePlacement(image, 0, 0, 0, 40, 40, 0, 2, ImageScaling.Stretched));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ImagePlacement(image, 0, 0, 0, 40, 0, 4, 2, ImageScaling.Stretched));
    }

    /// <summary>
    /// Two placements of one picture share its pixels, so a host keys its texture on the image and
    /// gets one upload for both.
    /// </summary>
    [Fact]
    public void Two_placements_of_one_image_share_its_pixels()
    {
        var image = Image(40, 40);
        var first = ImagePlacement.Natural(image);
        var second = new ImagePlacement(image, 7, 0, 0, 20, 20, 2, 1, ImageScaling.Stretched);

        Assert.Same(image, first.Image);
        Assert.Same(image, second.Image);
        Assert.NotSame(first, second);
        Assert.Equal(7, second.Id);
    }

    // ---- pixel offsets within the first cell ------------------------------------------------------

    /// <summary>
    /// A picture 28 pixels wide over a 14 pixel cell, shifted 4 pixels right. It still owns two
    /// cells -- the protocol is explicit that an offset "is not added to the number of
    /// rows/columns" -- so the last 4 pixels fall off the end and are clipped.
    /// </summary>
    private static ImagePlacement Shifted(int offsetX, int offsetY)
        => new(Image(28, 30), id: 0,
               sourceX: 0, sourceY: 0, sourceWidth: 28, sourceHeight: 30,
               cols: 2, rows: 2, ImageScaling.Natural, zIndex: 0,
               offsetX: offsetX, offsetY: offsetY);

    [Fact]
    public void An_offset_does_not_add_columns_or_rows()
    {
        var placement = Shifted(4, 5);

        Assert.Equal(2, placement.Cols);
        Assert.Equal(2, placement.Rows);
    }

    /// <summary>
    /// The leading tile starts partway into its cell and shows correspondingly fewer pixels. This is
    /// the case the older size-only call cannot express, which is why the layout call exists.
    /// </summary>
    [Fact]
    public void The_first_tile_starts_at_the_offset_and_is_shorter_for_it()
    {
        var placement = Shifted(4, 5);

        Assert.True(placement.TryGetTileLayout(0, 0, out var x, out var y, out var w, out var h,
                                               out var offX, out var offY, out var wide, out var high));

        Assert.Equal(0, x);
        Assert.Equal(0, y);
        Assert.Equal(10, w);                       // 14 - 4
        Assert.Equal(10, h);                       // 15 - 5
        Assert.Equal(4 / 14.0, offX, 6);
        Assert.Equal(5 / 15.0, offY, 6);
        Assert.Equal(10 / 14.0, wide, 6);
        Assert.Equal(10 / 15.0, high, 6);
    }

    /// <summary>
    /// The tile after it fills its cell from the left, continuing from where the first stopped --
    /// no gap and no repeated pixels at the join.
    /// </summary>
    [Fact]
    public void Later_tiles_continue_from_the_first_without_a_seam()
    {
        var placement = Shifted(4, 0);

        Assert.True(placement.TryGetTileLayout(0, 0, out _, out _, out var firstWidth, out _,
                                               out _, out _, out _, out _));
        Assert.True(placement.TryGetTileLayout(1, 0, out var x, out _, out var w, out _,
                                               out var offX, out _, out var wide, out _));

        Assert.Equal(firstWidth, x);               // starts exactly where the first ended
        Assert.Equal(0, offX);                     // and at the left edge of its own cell
        Assert.Equal(14, w);
        Assert.Equal(1.0, wide, 6);
    }

    /// <summary>What runs past the last cell of the box is clipped, not wrapped or shrunk to fit.</summary>
    [Fact]
    public void Pixels_pushed_past_the_last_cell_are_clipped()
    {
        var placement = Shifted(4, 0);

        var total = 0;
        for (int col = 0; col < placement.Cols; col++)
        {
            Assert.True(placement.TryGetTileLayout(col, 0, out _, out _, out var w, out _,
                                                   out _, out _, out _, out _));
            total += w;
        }

        // Two cells hold 28 pixels; four of them went to the offset, so four of the picture are lost.
        Assert.Equal(24, total);
    }

    /// <summary>
    /// The protocol requires an offset smaller than the cell. A larger one is clamped rather than
    /// refused -- it arrives from another process, and shifting the picture entirely out of its own
    /// first cell is not a meaningful request to honour.
    /// </summary>
    [Fact]
    public void An_offset_of_a_whole_cell_or_more_is_clamped()
    {
        Assert.Equal(CellWidth - 1, Shifted(CellWidth, 0).OffsetX);
        Assert.Equal(CellHeight - 1, Shifted(0, CellHeight * 3).OffsetY);
        Assert.Equal(0, Shifted(-5, 0).OffsetX);
    }

    /// <summary>
    /// With no offset the layout call must agree with the older source-only one, tile for tile, in
    /// both scalings. They are the same arithmetic and must not drift apart.
    /// </summary>
    [Fact]
    public void Without_an_offset_the_layout_matches_the_source_call()
    {
        var natural = ImagePlacement.Natural(Image(1160, 870));
        var stretched = new ImagePlacement(Image(97, 61), 0, 0, 0, 97, 61, 7, 3, ImageScaling.Stretched);

        foreach (var placement in new[] { natural, stretched })
        {
            for (int row = 0; row < placement.Rows; row++)
            {
                for (int col = 0; col < placement.Cols; col++)
                {
                    var bySource = placement.TryGetTileSource(col, row, out var sx, out var sy, out var sw, out var sh);
                    var byLayout = placement.TryGetTileLayout(col, row, out var lx, out var ly, out var lw, out var lh,
                                                              out var offX, out var offY, out _, out _);

                    Assert.Equal(bySource, byLayout);
                    Assert.True((sx, sy, sw, sh) == (lx, ly, lw, lh),
                        $"tile ({col},{row}) disagrees: source ({sx},{sy},{sw},{sh}) vs layout ({lx},{ly},{lw},{lh})");
                    Assert.Equal(0, offX);
                    Assert.Equal(0, offY);
                }
            }
        }
    }
}