using XTerm.Options;

namespace XTerm.Tests.Graphics;

/// <summary>
/// Kitty's delete matrix, and the image numbers several of its targets address.
///
/// <para>The case of the target letter is the whole difference between "stop showing this" and
/// "forget it entirely": lower case removes the appearances, upper case additionally releases the
/// stored image so its id stops resolving. Each pair is checked both ways, because a delete that
/// quietly frees the pixels breaks a client that meant to place the picture again.</para>
///
/// <para>Two keys change meaning on a delete. <c>x</c> and <c>y</c> are screen cells rather than a
/// crop origin, and they are one-based where the buffer is zero-based -- an off-by-one here deletes
/// the wrong row and looks like a rendering fault.</para>
/// </summary>
public class KittyDeleteTests
{
    private const string Esc = "";
    private const string St = Esc + "\\";

    private const int CellPixelWidth = 2;
    private const int CellPixelHeight = 3;

    private static Terminal Fresh()
        => new(new TerminalOptions
        {
            Cols = 30,
            Rows = 12,
            CellWidthPixels = CellPixelWidth,
            CellHeightPixels = CellPixelHeight
        });

    private static string Apc(string control, string payload = "")
        => payload.Length == 0 ? $"{Esc}_G{control}{St}" : $"{Esc}_G{control};{payload}{St}";

    /// <summary>A solid 4x6 picture, which covers two cells by two at the metrics above.</summary>
    private static string Pixels(int width = 4, int height = 6)
        => Convert.ToBase64String(new byte[width * height * 4]);

    private static bool HasImage(Terminal terminal, int col, int screenRow)
        => ImageAssertions.IsImageAt(terminal, col, screenRow);

    /// <summary>How many cells anywhere on screen still show a picture.</summary>
    private static int TileCount(Terminal terminal)
    {
        int count = 0;
        for (int row = 0; row < terminal.Rows; row++)
        {
            foreach (var placement in ImageAssertions.PlacementsOn(terminal, row))
            {
                var end = Math.Min(placement.EndColumn, terminal.Cols);
                count += Math.Max(0, end - placement.Column);
            }
        }
        return count;
    }

    private static List<string> Replies(Terminal terminal)
    {
        var replies = new List<string>();
        terminal.DataReceived += (_, e) => replies.Add(e.Data);
        return replies;
    }

    /// <summary>Transmits under an id and shows it at the cursor's current position.</summary>
    private static void Show(Terminal terminal, uint id, int col, int row, int placementId = 0)
    {
        terminal.Write($"{Esc}[{row + 1};{col + 1}H");
        var p = placementId != 0 ? $",p={placementId}" : "";
        terminal.Write(Apc($"a=T,i={id},f=32,s=4,v=6,q=2{p}", Pixels()));
    }

    // ---- by identity ------------------------------------------------------------------------------

    [Fact]
    public void Deleting_by_id_removes_every_appearance_of_that_image()
    {
        var terminal = Fresh();
        terminal.Write(Apc("a=t,i=7,f=32,s=4,v=6,q=2", Pixels()));
        terminal.Write($"{Esc}[1;1H" + Apc("a=p,i=7,q=2"));
        terminal.Write($"{Esc}[5;10H" + Apc("a=p,i=7,q=2"));
        Assert.Equal(8, TileCount(terminal));

        terminal.Write(Apc("a=d,d=i,i=7,q=2"));

        Assert.Equal(0, TileCount(terminal));
    }

    /// <summary>Lower case leaves the picture placeable; upper case does not.</summary>
    [Fact]
    public void Lower_case_delete_keeps_the_image_placeable()
    {
        var terminal = Fresh();
        Show(terminal, 7, 0, 0);

        terminal.Write(Apc("a=d,d=i,i=7,q=2"));
        Assert.Equal(0, TileCount(terminal));

        terminal.Write($"{Esc}[1;1H" + Apc("a=p,i=7,q=2"));

        Assert.Equal(4, TileCount(terminal));
    }

    [Fact]
    public void Upper_case_delete_frees_the_image()
    {
        var terminal = Fresh();
        Show(terminal, 7, 0, 0);
        var replies = Replies(terminal);

        terminal.Write(Apc("a=d,d=I,i=7,q=2"));
        terminal.Write($"{Esc}[1;1H" + Apc("a=p,i=7"));

        Assert.Equal(0, TileCount(terminal));
        Assert.Contains(replies, r => r.Contains("ENOENT"));
    }

    /// <summary>
    /// A placement id names one appearance. The others stay, which is the entire reason the protocol
    /// has placement ids at all.
    /// </summary>
    [Fact]
    public void A_placement_id_deletes_only_that_appearance()
    {
        var terminal = Fresh();
        terminal.Write(Apc("a=t,i=7,f=32,s=4,v=6,q=2", Pixels()));
        terminal.Write($"{Esc}[1;1H" + Apc("a=p,i=7,p=1,q=2"));
        terminal.Write($"{Esc}[5;10H" + Apc("a=p,i=7,p=2,q=2"));

        terminal.Write(Apc("a=d,d=i,i=7,p=1,q=2"));

        Assert.False(HasImage(terminal, 0, 0), "the named placement should be gone");
        Assert.True(HasImage(terminal, 9, 4), "the other placement should have stayed");
    }

    /// <summary>
    /// Even upper case must not free the pixels while another placement still shows them -- doing so
    /// would blank a picture the client never named.
    /// </summary>
    [Fact]
    public void Deleting_one_placement_never_frees_the_shared_image()
    {
        var terminal = Fresh();
        terminal.Write(Apc("a=t,i=7,f=32,s=4,v=6,q=2", Pixels()));
        terminal.Write($"{Esc}[1;1H" + Apc("a=p,i=7,p=1,q=2"));
        terminal.Write($"{Esc}[5;10H" + Apc("a=p,i=7,p=2,q=2"));

        terminal.Write(Apc("a=d,d=I,i=7,p=1,q=2"));

        Assert.True(HasImage(terminal, 9, 4));
        var replies = Replies(terminal);
        terminal.Write($"{Esc}[9;1H" + Apc("a=p,i=7"));
        Assert.DoesNotContain(replies, r => r.Contains("ENOENT"));
    }

    // ---- by image number --------------------------------------------------------------------------

    /// <summary>
    /// A client that does not want to manage an id space sends I=. The terminal picks an id and must
    /// report both halves, so the client can match the reply and then use the image.
    /// </summary>
    [Fact]
    public void An_image_number_is_answered_with_the_assigned_id()
    {
        var terminal = Fresh();
        var replies = Replies(terminal);

        terminal.Write(Apc("a=t,I=5,f=32,s=4,v=6", Pixels()));

        var reply = Assert.Single(replies);
        Assert.Contains("I=5", reply);
        Assert.Matches(@"i=\d+", reply);
        Assert.Contains("OK", reply);
    }

    [Fact]
    public void An_image_can_be_placed_by_its_number()
    {
        var terminal = Fresh();
        terminal.Write(Apc("a=t,I=5,f=32,s=4,v=6,q=2", Pixels()));

        terminal.Write($"{Esc}[1;1H" + Apc("a=p,I=5,q=2"));

        Assert.Equal(4, TileCount(terminal));
    }

    /// <summary>Sending the same number again makes a new image, and the number follows the newest.</summary>
    [Fact]
    public void A_repeated_number_refers_to_the_newest_image()
    {
        var terminal = Fresh();
        terminal.Write(Apc("a=t,I=5,f=32,s=4,v=6,q=2", Pixels(4, 6)));
        terminal.Write(Apc("a=t,I=5,f=32,s=8,v=12,q=2", Pixels(8, 12)));

        terminal.Write($"{Esc}[1;1H" + Apc("a=p,I=5,q=2"));

        Assert.True(ImageAssertions.IsImageAt(terminal, 0, 0));
        Assert.Equal(8, ImageAssertions.ImageAt(terminal, 0, 0)!.PixelWidth);
    }

    [Fact]
    public void Deleting_by_number_removes_the_image()
    {
        var terminal = Fresh();
        terminal.Write(Apc("a=t,I=5,f=32,s=4,v=6,q=2", Pixels()));
        terminal.Write($"{Esc}[1;1H" + Apc("a=p,I=5,q=2"));

        terminal.Write(Apc("a=d,d=n,I=5,q=2"));

        Assert.Equal(0, TileCount(terminal));
    }

    // ---- by position ------------------------------------------------------------------------------

    /// <summary>
    /// A placement found through one of its cells goes in its entirety. Removing only the cells in
    /// the named row would leave a picture with a hole through it.
    /// </summary>
    [Fact]
    public void Deleting_by_row_removes_the_whole_placement_not_just_that_row()
    {
        var terminal = Fresh();
        Show(terminal, 7, 0, 0);           // occupies rows 0 and 1
        Assert.Equal(4, TileCount(terminal));

        terminal.Write(Apc("a=d,d=y,y=2,q=2"));   // one-based: screen row 1

        Assert.Equal(0, TileCount(terminal));
    }

    /// <summary>
    /// Deliberately one column wide each, and adjacent. A two-column picture would swallow an
    /// off-by-one -- column 0 and column 1 both being inside it, the test would pass either way.
    /// </summary>
    [Fact]
    public void Deleting_by_column_leaves_placements_in_other_columns()
    {
        var terminal = Fresh();
        terminal.Write($"{Esc}[1;1H" + Apc("a=T,i=7,f=32,s=2,v=6,q=2", Pixels(2, 6)));   // column 0
        terminal.Write($"{Esc}[1;2H" + Apc("a=T,i=8,f=32,s=2,v=6,q=2", Pixels(2, 6)));   // column 1

        terminal.Write(Apc("a=d,d=x,x=1,q=2"));   // one-based: column 0

        Assert.False(HasImage(terminal, 0, 0), "the picture in column 0 should be gone");
        Assert.True(HasImage(terminal, 1, 0), "the picture in column 1 should have stayed");
    }

    [Fact]
    public void Deleting_at_a_cell_removes_the_placement_covering_it()
    {
        var terminal = Fresh();
        Show(terminal, 7, 0, 0);
        Show(terminal, 8, 10, 0);

        // The second tile of the first placement, one-based.
        terminal.Write(Apc("a=d,d=p,x=2,y=1,q=2"));

        Assert.False(HasImage(terminal, 0, 0));
        Assert.True(HasImage(terminal, 10, 0));
    }

    [Fact]
    public void Deleting_at_a_cell_with_no_picture_removes_nothing()
    {
        var terminal = Fresh();
        Show(terminal, 7, 0, 0);

        terminal.Write(Apc("a=d,d=p,x=20,y=9,q=2"));

        Assert.Equal(4, TileCount(terminal));
    }

    [Fact]
    public void Deleting_at_the_cursor_removes_the_placement_under_it()
    {
        var terminal = Fresh();
        Show(terminal, 7, 0, 0);
        Show(terminal, 8, 10, 0);

        terminal.Write($"{Esc}[1;11H");     // onto the second picture
        terminal.Write(Apc("a=d,d=c,q=2"));

        Assert.True(HasImage(terminal, 0, 0));
        Assert.False(HasImage(terminal, 10, 0));
    }

    // ---- by z-index -------------------------------------------------------------------------------

    [Fact]
    public void Deleting_by_z_index_selects_only_matching_placements()
    {
        var terminal = Fresh();
        terminal.Write(Apc("a=t,i=7,f=32,s=4,v=6,q=2", Pixels()));
        terminal.Write($"{Esc}[1;1H" + Apc("a=p,i=7,z=5,q=2"));
        terminal.Write($"{Esc}[5;10H" + Apc("a=p,i=7,z=9,q=2"));

        terminal.Write(Apc("a=d,d=z,z=5,q=2"));

        Assert.False(HasImage(terminal, 0, 0), "the z=5 placement should be gone");
        Assert.True(HasImage(terminal, 9, 4), "the z=9 placement should have stayed");
    }

    /// <summary>d=q is d=p narrowed by z-index: the cell must match and so must the depth.</summary>
    [Fact]
    public void Deleting_at_a_cell_with_a_z_index_requires_both_to_match()
    {
        var terminal = Fresh();
        Show(terminal, 7, 0, 0);           // no z given, so z=0

        terminal.Write(Apc("a=d,d=q,x=1,y=1,z=3,q=2"));
        Assert.Equal(4, TileCount(terminal));

        terminal.Write(Apc("a=d,d=q,x=1,y=1,z=0,q=2"));
        Assert.Equal(0, TileCount(terminal));
    }

    // ---- everything, and the edges ----------------------------------------------------------------

    [Fact]
    public void Deleting_all_clears_the_screen_of_pictures()
    {
        var terminal = Fresh();
        Show(terminal, 7, 0, 0);
        Show(terminal, 8, 10, 0);

        terminal.Write(Apc("a=d,d=a,q=2"));

        Assert.Equal(0, TileCount(terminal));
    }

    /// <summary>
    /// Nothing here stores animation frames, so there are none to remove. That is a request already
    /// satisfied rather than one this terminal cannot honour, so it is not an error.
    /// </summary>
    [Fact]
    public void Deleting_animation_frames_succeeds_having_nothing_to_do()
    {
        var terminal = Fresh();
        Show(terminal, 7, 0, 0);
        var replies = Replies(terminal);

        terminal.Write(Apc("a=d,d=f,i=7"));

        Assert.Equal(4, TileCount(terminal));
        Assert.DoesNotContain(replies, r => r.Contains("ENOTSUP"));
    }

    [Fact]
    public void An_unknown_delete_target_is_refused_rather_than_ignored()
    {
        var terminal = Fresh();
        Show(terminal, 7, 0, 0);
        var replies = Replies(terminal);

        terminal.Write(Apc("a=d,d=w,i=7"));

        Assert.Equal(4, TileCount(terminal));
        Assert.Contains(replies, r => r.Contains("ENOTSUP"));
    }

    /// <summary>
    /// The scrollback is deliberately not searched. A picture scrolled out of view is not "at row 1"
    /// however many rows above it happen to be.
    /// </summary>
    [Fact]
    public void A_positional_delete_ignores_the_scrollback()
    {
        var terminal = Fresh();
        Show(terminal, 7, 0, 0);

        // Push it off the top.
        for (int i = 0; i < terminal.Rows + 2; i++)
            terminal.Write("\r\n");

        terminal.Write(Apc("a=d,d=y,y=1,q=2"));

        var survives = false;
        for (int i = 0; i < terminal.Buffer.YBase; i++)
        {
            var line = terminal.Buffer.Lines[i];
            if (line is not null && line.TryGetPlacementAt(0, out _))
                survives = true;
        }

        Assert.True(survives, "a picture in the scrollback was deleted by a screen-row delete");
    }
}
