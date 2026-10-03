using System.Linq;
using XTerm.Buffer;
using XTerm.Graphics;
using XTerm.Options;

namespace XTerm.Tests.Graphics;

/// <summary>
/// The Kitty graphics protocol, driven the way a program drives it: escape sequences in, pictures
/// and replies out.
///
/// <para>Cell metrics are pinned at 2x3 pixels so a hand-sized payload still covers several cells
/// and the tile arithmetic is checkable by eye.</para>
/// </summary>
public class KittyGraphicsTests
{
    private const string Esc = "\u001b";
    private const string St = Esc + "\\";

    private const int CellPixelWidth = 2;
    private const int CellPixelHeight = 3;

    private static Terminal Fresh(Action<TerminalOptions>? configure = null)
    {
        var options = new TerminalOptions
        {
            Cols = 30,
            Rows = 12,
            CellWidthPixels = CellPixelWidth,
            CellHeightPixels = CellPixelHeight
        };
        configure?.Invoke(options);
        return new Terminal(options);
    }

    /// <summary>Wraps control data and payload as one Kitty escape sequence.</summary>
    private static string Apc(string control, string payload = "")
        => payload.Length == 0 ? $"{Esc}_G{control}{St}" : $"{Esc}_G{control};{payload}{St}";

    /// <summary>RGBA bytes for a solid picture, base64 as the protocol carries them.</summary>
    private static string SolidRgba(int width, int height, byte r, byte g, byte b, byte a = 255)
    {
        var bytes = new byte[width * height * 4];
        for (int i = 0; i < width * height; i++)
        {
            bytes[i * 4] = r;
            bytes[i * 4 + 1] = g;
            bytes[i * 4 + 2] = b;
            bytes[i * 4 + 3] = a;
        }
        return Convert.ToBase64String(bytes);
    }

    private static BufferCell Cell(Terminal terminal, int col, int screenRow)
        => terminal.Buffer.Lines[terminal.Buffer.YBase + screenRow]![col];

    private static (byte R, byte G, byte B, byte A) Pixel(TerminalImage image, int x, int y)
    {
        var span = image.Pixels.Span;
        var at = (y * image.PixelWidth + x) * TerminalImage.BytesPerPixel;
        return (span[at + 2], span[at + 1], span[at], span[at + 3]);
    }

    private static List<string> Replies(Terminal terminal)
    {
        var replies = new List<string>();
        terminal.DataReceived += (_, e) => replies.Add(e.Data);
        return replies;
    }

    // ---- transmit and display -------------------------------------------------------------------

    [Fact]
    public void An_rgba_image_is_decoded_and_placed_at_the_cursor()
    {
        var terminal = Fresh();

        // 4x6 pixels over 2x3 cells: two columns by two rows.
        terminal.Write(Apc("a=T,f=32,s=4,v=6,q=2", SolidRgba(4, 6, 200, 100, 50)));

        var placement = ImageAssertions.PlacementAt(terminal, 0, 0);
        Assert.NotNull(placement);

        var image = ImageAssertions.ImageAt(terminal, 0, 0)!;
        Assert.Equal(4, image.PixelWidth);
        Assert.Equal(6, image.PixelHeight);
        Assert.Equal(2, placement!.Value.Cols);
        Assert.Equal(2, ImageAssertions.RowsOf(terminal, placement.Value.Serial));
        Assert.Equal((200, 100, 50, (byte)255), Pixel(image, 0, 0));

        // Every covered position reads the pixels it should: two pixels across per cell, three down.
        for (int row = 0; row < 2; row++)
        {
            for (int col = 0; col < 2; col++)
            {
                Assert.Equal(placement.Value.Serial,
                             ImageAssertions.PlacementAt(terminal, col, row)!.Value.Serial);
                Assert.Equal((col * 2, row * 3), ImageAssertions.SourceAt(terminal, col, row));
            }
        }
    }

    [Fact]
    public void An_rgb_image_is_taken_as_opaque()
    {
        var terminal = Fresh();

        var rgb = Convert.ToBase64String(new byte[] { 10, 20, 30, 40, 50, 60 }); // two pixels
        terminal.Write(Apc("a=T,f=24,s=2,v=1,q=2", rgb));

        var image = ImageAssertions.ImageAt(terminal, 0, 0);
        Assert.NotNull(image);
        Assert.Equal(((byte)10, (byte)20, (byte)30, (byte)255), Pixel(image!, 0, 0));
        Assert.Equal(((byte)40, (byte)50, (byte)60, (byte)255), Pixel(image!, 1, 0));
    }

    [Fact]
    public void Transparency_is_kept()
    {
        var terminal = Fresh();
        terminal.Write(Apc("a=T,f=32,s=2,v=3,q=2", SolidRgba(2, 3, 9, 8, 7, a: 64)));

        Assert.Equal((byte)64, Pixel(ImageAssertions.ImageAt(terminal, 0, 0)!, 0, 0).A);
    }

    // ---- what chafa actually sends ---------------------------------------------------------------

    /// <summary>
    /// The exact shape <c>chafa -f kitty</c> emits: control data alone in the first sequence with no
    /// semicolon at all, payload in the middle ones, and an empty <c>m=0</c> to finish.
    /// </summary>
    [Fact]
    public void A_chunked_transmission_in_chafas_shape_is_assembled()
    {
        var terminal = Fresh();
        var payload = SolidRgba(4, 6, 11, 22, 33);

        var half = payload.Length / 2;
        terminal.Write(Apc("a=T,f=32,s=4,v=6,c=2,r=2,m=1,q=2"));      // control only, no payload
        terminal.Write(Apc("m=1", payload[..half]));
        terminal.Write(Apc("m=1", payload[half..]));
        terminal.Write(Apc("m=0"));                                    // empty terminator

        var image = ImageAssertions.ImageAt(terminal, 0, 0);
        Assert.NotNull(image);
        Assert.Equal((11, 22, 33, (byte)255), Pixel(image!, 0, 0));
    }

    /// <summary>
    /// Split at a point that is not a multiple of four, which is where decoding each chunk as it
    /// arrives would corrupt everything after the join.
    /// </summary>
    [Fact]
    public void A_chunk_boundary_off_a_base64_quantum_still_assembles()
    {
        var terminal = Fresh();
        var payload = SolidRgba(4, 6, 5, 6, 7);

        var awkward = 5; // deliberately not a multiple of 4
        terminal.Write(Apc("a=T,f=32,s=4,v=6,m=1,q=2", payload[..awkward]));
        terminal.Write(Apc("m=0", payload[awkward..]));

        var image = ImageAssertions.ImageAt(terminal, 0, 0);
        Assert.NotNull(image);
        Assert.Equal(((byte)5, (byte)6, (byte)7, (byte)255), Pixel(image!, 0, 0));
    }

    // ---- c and r ---------------------------------------------------------------------------------

    /// <summary>
    /// c and r name a box to fill, and chafa always sends them. A 4x6 picture asked into 4 columns
    /// by 1 row must occupy that, not the two-by-two its own size would give.
    /// </summary>
    [Fact]
    public void A_cell_box_stretches_the_picture_to_fit()
    {
        var terminal = Fresh();
        terminal.Write(Apc("a=T,f=32,s=4,v=6,c=4,r=1,q=2", SolidRgba(4, 6, 1, 2, 3)));

        var placement = ImageAssertions.PlacementAt(terminal, 0, 0);
        Assert.NotNull(placement);
        Assert.Equal(4, placement!.Value.Cols);
        Assert.Equal(1, ImageAssertions.RowsOf(terminal, placement.Value.Serial));

        Assert.False(ImageAssertions.IsImageAt(terminal, 0, 1));   // one row only
        Assert.True(ImageAssertions.IsImageAt(terminal, 3, 0));
    }

    /// <summary>Without them the picture keeps its own size, and the edge tiles are clipped.</summary>
    [Fact]
    public void Without_a_cell_box_the_picture_keeps_its_natural_size()
    {
        var terminal = Fresh();
        terminal.Write(Apc("a=T,f=32,s=4,v=6,q=2", SolidRgba(4, 6, 1, 2, 3)));

        var placement = ImageAssertions.PlacementAt(terminal, 0, 0)!.Value;
        Assert.Equal(2, placement.Cols);
        Assert.Equal(2, ImageAssertions.RowsOf(terminal, placement.Serial));
    }

    [Fact]
    public void A_crop_shows_only_the_part_asked_for()
    {
        var terminal = Fresh();
        terminal.Write(Apc("a=T,f=32,s=8,v=12,x=2,y=3,w=4,h=6,q=2", SolidRgba(8, 12, 1, 2, 3)));

        var placement = ImageAssertions.PlacementAt(terminal, 0, 0)!.Value;
        Assert.Equal(2, placement.SrcX);
        Assert.Equal(3, placement.SrcY);
        Assert.Equal(4, placement.SrcWidth);
        // The run is one row of the crop, so its height is that row's slice rather than the whole.
        Assert.Equal(6, ImageAssertions.PlacementsOn(terminal, 0)
                                       .Where(p => p.Serial == placement.Serial)
                                       .Sum(p => p.SrcHeight)
                        + ImageAssertions.PlacementsOn(terminal, 1)
                                       .Where(p => p.Serial == placement.Serial)
                                       .Sum(p => p.SrcHeight));
    }

    // ---- transmit once, place many ---------------------------------------------------------------

    [Fact]
    public void An_image_can_be_transmitted_then_placed_by_id()
    {
        var terminal = Fresh();

        terminal.Write(Apc("a=t,i=7,f=32,s=4,v=6,q=2", SolidRgba(4, 6, 90, 80, 70)));
        Assert.False(ImageAssertions.IsImageAt(terminal, 0, 0));   // a=t shows nothing

        terminal.Write(Apc("a=p,i=7,q=2"));

        var image = ImageAssertions.ImageAt(terminal, 0, 0);
        Assert.NotNull(image);
        Assert.Equal(((byte)90, (byte)80, (byte)70, (byte)255), Pixel(image!, 0, 0));
    }

    /// <summary>
    /// Two appearances of one picture share its pixels but are distinct placements, which is what
    /// keeps a renderer from running one strip across the join between them.
    /// </summary>
    [Fact]
    public void Two_placements_of_one_image_share_pixels_but_stay_distinct()
    {
        var terminal = Fresh();

        terminal.Write(Apc("a=t,i=3,f=32,s=4,v=6,q=2", SolidRgba(4, 6, 1, 2, 3)));
        terminal.Write($"{Esc}[1;1H");
        terminal.Write(Apc("a=p,i=3,C=1,q=2"));
        terminal.Write($"{Esc}[1;3H");
        terminal.Write(Apc("a=p,i=3,C=1,q=2"));

        var first = ImageAssertions.PlacementAt(terminal, 0, 0);
        var second = ImageAssertions.PlacementAt(terminal, 2, 0);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(first!.Value.ImageId, second!.Value.ImageId);
        Assert.NotEqual(first.Value.Serial, second.Value.Serial);
    }

    [Fact]
    public void Placing_an_unknown_id_reports_that_it_is_missing()
    {
        var terminal = Fresh();
        var replies = Replies(terminal);

        terminal.Write(Apc("a=p,i=99"));

        Assert.Contains(replies, r => r.Contains("ENOENT"));
        Assert.False(ImageAssertions.IsImageAt(terminal, 0, 0));
    }

    // ---- the cursor -------------------------------------------------------------------------------

    [Fact]
    public void The_cursor_lands_below_the_picture_by_default()
    {
        var terminal = Fresh();
        terminal.Write(Apc("a=T,f=32,s=4,v=6,q=2", SolidRgba(4, 6, 1, 2, 3)));

        Assert.Equal(0, terminal.Buffer.X);
        Assert.Equal(2, terminal.Buffer.Y);
    }

    [Fact]
    public void C_equals_one_leaves_the_cursor_alone()
    {
        var terminal = Fresh();
        terminal.Write($"{Esc}[3;5H");
        terminal.Write(Apc("a=T,f=32,s=4,v=6,C=1,q=2", SolidRgba(4, 6, 1, 2, 3)));

        Assert.Equal(4, terminal.Buffer.X);
        Assert.Equal(2, terminal.Buffer.Y);
        Assert.True(ImageAssertions.IsImageAt(terminal, 4, 2));
    }

    // ---- replies -----------------------------------------------------------------------------------

    /// <summary>
    /// A query is how a program finds out the terminal speaks this protocol. It must answer, and it
    /// must not draw anything -- programs probe with a real image and expect their output untouched.
    /// </summary>
    [Fact]
    public void A_query_replies_and_places_nothing()
    {
        var terminal = Fresh();
        var replies = Replies(terminal);

        terminal.Write(Apc("i=31,s=1,v=1,a=q,t=d,f=24", "AAAA"));

        Assert.Equal($"{Esc}_Gi=31;OK{St}", Assert.Single(replies));
        Assert.False(ImageAssertions.IsImageAt(terminal, 0, 0));
        Assert.Equal(0, terminal.Buffer.X);
        Assert.Equal(0, terminal.Buffer.Y);
    }

    [Theory]
    [InlineData(1, false, "q=1 suppresses success")]
    [InlineData(2, false, "q=2 suppresses everything")]
    [InlineData(0, true, "q=0 says so")]
    public void Quiet_is_honoured(int quiet, bool expectReply, string what)
    {
        var terminal = Fresh();
        var replies = Replies(terminal);

        terminal.Write(Apc($"i=31,s=1,v=1,a=q,t=d,f=24,q={quiet}", "AAAA"));

        Assert.True(replies.Count > 0 == expectReply, what);
    }

    [Fact]
    public void A_failure_is_still_reported_under_q_equals_one()
    {
        var terminal = Fresh();
        var replies = Replies(terminal);

        terminal.Write(Apc("a=p,i=99,q=1"));

        Assert.Contains(replies, r => r.Contains("ENOENT"));
    }

    /// <summary>
    /// Reading a file the client names would have the terminal open a path on its say-so, and this
    /// library runs inside hosts that may hold more privilege than the program they run.
    /// </summary>
    [Theory]
    [InlineData('f', "a file")]
    [InlineData('t', "a temporary file")]
    [InlineData('s', "shared memory")]
    public void Transmission_from_outside_the_escape_sequence_is_refused(char medium, string what)
    {
        var terminal = Fresh();
        var replies = Replies(terminal);

        terminal.Write(Apc($"a=T,i=5,f=32,s=4,v=6,t={medium}", "L3RtcC94"));

        Assert.True(replies.Any(r => r.Contains("ENOTSUP")), $"{what} should be refused");
        Assert.False(ImageAssertions.IsImageAt(terminal, 0, 0));
    }

    /// <summary>
    /// Animating an image that was never transmitted is ENOENT rather than ENOTSUP: the action is
    /// supported, the picture is what is missing. See <c>KittyAnimationTests</c> for the rest.
    /// </summary>
    [Fact]
    public void Animating_an_unknown_image_says_so()
    {
        var terminal = Fresh();
        var replies = Replies(terminal);

        terminal.Write(Apc("a=a,i=5"));

        Assert.Contains(replies, r => r.Contains("ENOENT"));
    }

    /// <summary>An action letter from no revision of the protocol is still refused outright.</summary>
    [Fact]
    public void An_unknown_action_is_refused_rather_than_ignored()
    {
        var terminal = Fresh();
        var replies = Replies(terminal);

        terminal.Write(Apc("a=w,i=5"));

        Assert.Contains(replies, r => r.Contains("ENOTSUP"));
    }

    // ---- deletion ------------------------------------------------------------------------------------

    [Fact]
    public void Delete_all_clears_the_screen_of_pictures()
    {
        var terminal = Fresh();
        terminal.Write(Apc("a=T,f=32,s=4,v=6,q=2", SolidRgba(4, 6, 1, 2, 3)));
        Assert.True(ImageAssertions.IsImageAt(terminal, 0, 0));

        terminal.Write(Apc("a=d,d=a,q=2"));

        Assert.False(ImageAssertions.IsImageAt(terminal, 0, 0));
    }

    [Fact]
    public void Delete_by_id_removes_only_that_picture()
    {
        var terminal = Fresh();

        terminal.Write(Apc("a=T,i=1,f=32,s=4,v=6,C=1,q=2", SolidRgba(4, 6, 1, 1, 1)));
        terminal.Write($"{Esc}[5;1H");
        terminal.Write(Apc("a=T,i=2,f=32,s=4,v=6,C=1,q=2", SolidRgba(4, 6, 2, 2, 2)));

        terminal.Write(Apc("a=d,d=i,i=1,q=2"));

        Assert.False(ImageAssertions.IsImageAt(terminal, 0, 0));
        Assert.True(ImageAssertions.IsImageAt(terminal, 0, 4));
    }

    /// <summary>
    /// A lower-case target frees the placement but keeps the pixels, so the picture can be shown
    /// again without retransmitting it.
    /// </summary>
    [Fact]
    public void Delete_keeps_the_image_unless_the_target_is_upper_case()
    {
        var terminal = Fresh();
        terminal.Write(Apc("a=t,i=4,f=32,s=4,v=6,q=2", SolidRgba(4, 6, 7, 7, 7)));

        terminal.Write(Apc("a=d,d=i,i=4,q=2"));
        terminal.Write(Apc("a=p,i=4,q=2"));
        Assert.True(ImageAssertions.IsImageAt(terminal, 0, 0));

        terminal.Write(Apc("a=d,d=I,i=4,q=2"));
        var replies = Replies(terminal);
        terminal.Write(Apc("a=p,i=4"));
        Assert.Contains(replies, r => r.Contains("ENOENT"));
    }

    // ---- malformed input ------------------------------------------------------------------------------

    /// <summary>
    /// The payload is untrusted output from another process. None of these may throw, and the
    /// parser has to come back afterwards.
    /// </summary>
    [Theory]
    [InlineData("a=T,f=32,s=4,v=6", "!!!not base64!!!", "bad base64")]
    [InlineData("a=T,f=32,s=999,v=999", "AAAA", "payload smaller than declared")]
    [InlineData("a=T,f=32", "AAAA", "no dimensions for a raw format")]
    [InlineData("a=T,f=100", "AAAA", "not a png")]
    [InlineData("a=T,f=77,s=1,v=1", "AAAA", "unknown format")]
    [InlineData("", "AAAA", "no control data at all")]
    [InlineData("a=T,f=32,s=1,v=1,zzz=9", "AAAAAA==", "an unknown key")]
    public void Malformed_commands_are_survived(string control, string payload, string what)
    {
        var terminal = Fresh();

        var exception = Record.Exception(() => terminal.Write(Apc(control, payload)));
        Assert.True(exception is null, $"{what} threw: {exception}");

        terminal.Write("OK");
        Assert.Contains("OK", terminal.GetLine(terminal.Buffer.YBase + terminal.Buffer.Y));
    }

    [Fact]
    public void An_image_larger_than_the_budget_is_refused()
    {
        var terminal = Fresh(o => o.MaxSixelPixels = 4);
        var replies = Replies(terminal);

        terminal.Write(Apc("a=T,i=2,f=32,s=100,v=100", SolidRgba(100, 100, 1, 2, 3)));

        Assert.Contains(replies, r => r.Contains("EFBIG"));
        Assert.False(ImageAssertions.IsImageAt(terminal, 0, 0));
    }

    [Fact]
    public void Kitty_can_be_switched_off()
    {
        var terminal = Fresh(o => o.KittyGraphicsEnabled = false);
        var replies = Replies(terminal);

        terminal.Write(Apc("a=T,f=32,s=4,v=6,q=0", SolidRgba(4, 6, 1, 2, 3)));

        Assert.False(ImageAssertions.IsImageAt(terminal, 0, 0));
        Assert.Empty(replies);
    }

    /// <summary>An abandoned transmission must not be appended to whatever comes next.</summary>
    [Fact]
    public void An_interrupted_transmission_is_dropped()
    {
        var terminal = Fresh();

        terminal.Write($"{Esc}_Ga=T,f=32,s=4,v=6,m=1,q=2;AAAA");   // no terminator
        terminal.Write($"{Esc}[1;1H");                              // something else entirely

        terminal.Write(Apc("a=T,f=32,s=4,v=6,q=2", SolidRgba(4, 6, 60, 60, 60)));

        var image = ImageAssertions.ImageAt(terminal, 0, 0);
        Assert.NotNull(image);
        Assert.Equal(((byte)60, (byte)60, (byte)60, (byte)255), Pixel(image!, 0, 0));
    }

    // ---- lifetime, the same as any other cell content --------------------------------------------------

    [Fact]
    public void A_kitty_picture_is_an_overlay_rather_than_content()
    {
        var terminal = Fresh();
        terminal.Write(Apc("a=T,f=32,s=4,v=6,q=2", SolidRgba(4, 6, 1, 2, 3)));

        // Printing does not remove it. A Kitty placement is an overlay the z-index orders against
        // the text, not content the way a Sixel is, so the character lands and the picture stays --
        // hidden while it is in front, and there again the moment it is deleted.
        terminal.Write($"{Esc}[1;1HX");
        Assert.Equal("X", Cell(terminal, 0, 0).Content);
        Assert.True(ImageAssertions.IsImageAt(terminal, 0, 0));
        Assert.True(ImageAssertions.IsImageAt(terminal, 1, 0));

        // Erasing does remove it, whichever protocol placed it: a cleared cell is blank, and a
        // picture showing through one would be a leak.
        terminal.Write($"{Esc}[2J");
        Assert.False(ImageAssertions.IsImageAt(terminal, 0, 0));
        Assert.False(ImageAssertions.IsImageAt(terminal, 1, 0));
    }
}
