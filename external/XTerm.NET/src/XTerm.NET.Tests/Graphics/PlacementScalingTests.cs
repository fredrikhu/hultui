using System.Linq;
using XTerm.Graphics;
using XTerm.Options;

namespace XTerm.Tests.Graphics;

/// <summary>
/// The scaling context a renderer needs to draw a placement's strips: <see
/// cref="LinePlacement.PxPerCellX"/> and <see cref="LinePlacement.PxPerCellY"/>.
///
/// <para>The strips a placement becomes are sliced to whole source pixels, and a renderer that
/// converts them back to screen size has to know how many source pixels one cell of THIS placement
/// covers. Without that it can only assume the image's natural metric -- which drew every stretched
/// picture at its own size instead: striped when blown up, clipped when shrunk. Tom's #114.</para>
/// </summary>
public class PlacementScalingTests
{
    private const string Esc = "";
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

    private static string SolidRgba(int width, int height)
    {
        var bytes = new byte[width * height * 4];
        for (int i = 0; i < bytes.Length; i += 4)
            bytes[i + 3] = 255;
        return Convert.ToBase64String(bytes);
    }

    private static LinePlacement FirstPlacement(Terminal terminal, int screenRow)
        => terminal.Buffer.Lines[terminal.Buffer.YBase + screenRow]!.Placements.First();

    [Fact]
    public void A_stretched_placement_carries_its_boxs_pixels_per_cell()
    {
        // An 8x9 picture stretched into a 2x3 cell box: each cell covers 4x3 source pixels,
        // whatever the image's natural metric says.
        var terminal = Fresh();
        terminal.Write(Apc("a=t,i=1,f=32,s=8,v=9,q=2", SolidRgba(8, 9)));
        terminal.Write(Apc("a=p,i=1,c=2,r=3,q=2"));

        var strip = FirstPlacement(terminal, 0);

        Assert.Equal(8f / 2, strip.PxPerCellX);
        Assert.Equal(9f / 3, strip.PxPerCellY);
    }

    [Fact]
    public void A_natural_placement_carries_zero_meaning_the_images_own_metric()
    {
        // Zero rather than the cell metric itself, so a renderer can tell "natural" apart from
        // "stretched to exactly the natural size" -- the first keeps unstretched edges, and the
        // convention also keeps placements written before the field existed drawing correctly.
        var terminal = Fresh();
        terminal.Write(Apc("a=t,i=1,f=32,s=8,v=9,q=2", SolidRgba(8, 9)));
        terminal.Write(Apc("a=p,i=1,q=2"));

        var strip = FirstPlacement(terminal, 0);

        Assert.Equal(0f, strip.PxPerCellX);
        Assert.Equal(0f, strip.PxPerCellY);
    }

    [Fact]
    public void Slicing_a_run_keeps_the_scaling_context()
    {
        // Sixel runs are split when text prints into them; the surviving parts must keep drawing
        // at the same scale as the whole did.
        var placement = new LinePlacement(
            imageId: 7, column: 0, cols: 10,
            srcX: 0, srcY: 0, srcWidth: 40, srcHeight: 3,
            pxPerCellX: 4f, pxPerCellY: 3f);

        var right = placement.TruncatedAfter(3);

        Assert.Equal(4f, right.PxPerCellX);
        Assert.Equal(3f, right.PxPerCellY);
    }
}
