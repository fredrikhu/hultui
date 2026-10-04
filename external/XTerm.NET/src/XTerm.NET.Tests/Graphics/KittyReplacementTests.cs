using XTerm.Graphics;
using XTerm.Options;

namespace XTerm.Tests.Graphics;

/// <summary>
/// What replaces what in Kitty: a placement sent again under the same ids, an image transmitted
/// again under the same id, a deleted animation frame -- and the registry's byte count staying true
/// through all of it.
///
/// <para>The byte count matters more than it looks. It is the only thing bounding what a program
/// can make the terminal hold, and it is never read directly, so a drift shows only as the budget
/// no longer evicting. The tests here check it the same way: by what is evicted.</para>
/// </summary>
public class KittyReplacementTests
{
    private const string Esc = "\u001b";
    private const string St = Esc + "\\";

    /// <summary>A 4x4 picture is 64 bytes.</summary>
    private const int Picture = 4 * 4 * TerminalImage.BytesPerPixel;

    private static Terminal Fresh(long registryBudget = 0)
    {
        var options = new TerminalOptions
        {
            Cols = 30,
            Rows = 12,
            CellWidthPixels = 2,
            CellHeightPixels = 3
        };
        if (registryBudget > 0)
            options.MaxImageRegistryBytes = registryBudget;
        return new Terminal(options);
    }

    private static string Apc(string control, string payload = "")
        => payload.Length == 0 ? $"{Esc}_G{control}{St}" : $"{Esc}_G{control};{payload}{St}";

    private static string Rgba(int width, int height, byte r, byte g, byte b)
    {
        var bytes = new byte[width * height * 4];
        for (int i = 0; i < width * height; i++)
            (bytes[i * 4], bytes[i * 4 + 1], bytes[i * 4 + 2], bytes[i * 4 + 3]) = (r, g, b, 255);
        return Convert.ToBase64String(bytes);
    }

    private static void MoveTo(Terminal terminal, int col, int row) => terminal.Write($"{Esc}[{row + 1};{col + 1}H");

    /// <summary>Cells on screen showing a picture. A 4x6 picture at 2x3 cells takes four.</summary>
    private static int Tiles(Terminal terminal) => ImageAssertions.VisibleImageCells(terminal);

    private static List<string> Replies(Terminal terminal)
    {
        var replies = new List<string>();
        terminal.DataReceived += (_, e) => replies.Add(e.Data);
        return replies;
    }

    /// <summary>Whether an id still resolves: a put of it is answered OK, not ENOENT.</summary>
    private static bool IsHeld(Terminal terminal, uint id)
    {
        var replies = Replies(terminal);
        MoveTo(terminal, 20, 10);
        terminal.Write(Apc($"a=p,i={id},C=1"));
        return replies.Any(r => r.Contains("OK"));
    }

    // ---- placements -------------------------------------------------------------------------------

    [Fact]
    public void A_placement_sent_again_under_the_same_ids_moves_rather_than_copies()
    {
        var terminal = Fresh();
        terminal.Write(Apc("a=t,i=7,f=32,s=4,v=6,q=2", Rgba(4, 6, 255, 0, 0)));

        MoveTo(terminal, 0, 0);
        terminal.Write(Apc("a=p,i=7,p=1,C=1,q=2"));
        MoveTo(terminal, 10, 5);
        terminal.Write(Apc("a=p,i=7,p=1,C=1,q=2"));

        Assert.Equal(4, Tiles(terminal));
        Assert.False(ImageAssertions.IsImageAt(terminal, 0, 0));
        Assert.True(ImageAssertions.IsImageAt(terminal, 10, 5));
    }

    [Fact]
    public void Another_placement_id_or_none_adds_a_picture()
    {
        var terminal = Fresh();
        terminal.Write(Apc("a=t,i=7,f=32,s=4,v=6,q=2", Rgba(4, 6, 255, 0, 0)));

        MoveTo(terminal, 0, 0);
        terminal.Write(Apc("a=p,i=7,p=1,C=1,q=2"));
        MoveTo(terminal, 10, 0);
        terminal.Write(Apc("a=p,i=7,p=2,C=1,q=2"));
        MoveTo(terminal, 20, 0);
        terminal.Write(Apc("a=p,i=7,C=1,q=2"));
        MoveTo(terminal, 0, 5);
        terminal.Write(Apc("a=p,i=7,C=1,q=2"));

        Assert.Equal(16, Tiles(terminal));
    }

    [Fact]
    public void The_same_placement_id_on_another_image_is_another_placement()
    {
        var terminal = Fresh();
        terminal.Write(Apc("a=t,i=7,f=32,s=4,v=6,q=2", Rgba(4, 6, 255, 0, 0)));
        terminal.Write(Apc("a=t,i=8,f=32,s=4,v=6,q=2", Rgba(4, 6, 0, 255, 0)));

        MoveTo(terminal, 0, 0);
        terminal.Write(Apc("a=p,i=7,p=1,C=1,q=2"));
        MoveTo(terminal, 10, 0);
        terminal.Write(Apc("a=p,i=8,p=1,C=1,q=2"));

        Assert.Equal(8, Tiles(terminal));
    }

    // ---- images -----------------------------------------------------------------------------------

    [Fact]
    public void Transmitting_an_id_again_takes_the_old_pictures_off_the_screen()
    {
        var terminal = Fresh();
        MoveTo(terminal, 0, 0);
        terminal.Write(Apc("a=T,i=7,f=32,s=4,v=6,C=1,q=2", Rgba(4, 6, 255, 0, 0)));
        Assert.Equal(4, Tiles(terminal));

        // Only transmitted: the new data is not shown until it is placed.
        terminal.Write(Apc("a=t,i=7,f=32,s=4,v=6,q=2", Rgba(4, 6, 0, 255, 0)));

        Assert.Equal(0, Tiles(terminal));
        Assert.True(IsHeld(terminal, 7));
    }

    [Fact]
    public void Transmitting_a_number_again_keeps_the_older_image_on_screen()
    {
        var terminal = Fresh();
        MoveTo(terminal, 0, 0);
        terminal.Write(Apc("a=T,I=3,f=32,s=4,v=6,C=1,q=2", Rgba(4, 6, 255, 0, 0)));
        MoveTo(terminal, 10, 0);
        terminal.Write(Apc("a=T,I=3,f=32,s=4,v=6,C=1,q=2", Rgba(4, 6, 0, 255, 0)));

        Assert.Equal(8, Tiles(terminal));
    }

    // ---- animation frames -------------------------------------------------------------------------

    /// <summary>Image 1, 4x4: frame 1 red, 2 green, 3 blue; placed at the top left.</summary>
    private static (Terminal Terminal, TerminalImage Image) ThreeFrames(long registryBudget = 0)
    {
        var terminal = Fresh(registryBudget);
        terminal.Write(Apc("a=t,i=1,f=32,s=4,v=4,q=2", Rgba(4, 4, 255, 0, 0)));
        terminal.Write(Apc("a=f,i=1,f=32,s=4,v=4,q=2", Rgba(4, 4, 0, 255, 0)));
        terminal.Write(Apc("a=f,i=1,f=32,s=4,v=4,q=2", Rgba(4, 4, 0, 0, 255)));
        MoveTo(terminal, 0, 0);
        terminal.Write(Apc("a=p,i=1,C=1,q=2"));
        var image = ImageAssertions.ImageAt(terminal, 0, 0);
        Assert.NotNull(image);
        Assert.Equal(3, image!.Animation!.FrameCount);
        return (terminal, image);
    }

    /// <summary>A frame's colour as (R, G, B); the buffer is BGRA.</summary>
    private static (byte, byte, byte) Colour(TerminalImage image, int frame)
    {
        Assert.True(image.Animation!.TryGetFrame(frame, out var f));
        var span = f.Pixels.Span;
        return (span[2], span[1], span[0]);
    }

    [Fact]
    public void Deleting_a_frame_by_number_moves_the_later_ones_down()
    {
        var (terminal, image) = ThreeFrames();

        terminal.Write(Apc("a=d,d=f,i=1,r=2,q=2"));

        Assert.Equal(2, image.Animation!.FrameCount);
        Assert.Equal(((byte)255, (byte)0, (byte)0), Colour(image, 1));
        Assert.Equal(((byte)0, (byte)0, (byte)255), Colour(image, 2));
    }

    [Fact]
    public void Deleting_a_frame_without_a_number_deletes_the_first()
    {
        var (terminal, image) = ThreeFrames();
        var serial = image.FrameSerial;

        terminal.Write(Apc("a=d,d=f,i=1,q=2"));

        Assert.Equal(2, image.Animation!.FrameCount);
        Assert.Equal(((byte)0, (byte)255, (byte)0), Colour(image, 1));
        Assert.NotEqual(serial, image.FrameSerial); // what is shown changed
    }

    [Fact]
    public void Deleting_the_current_frame_shows_the_one_that_took_its_place()
    {
        var (terminal, image) = ThreeFrames();
        terminal.Write(Apc("a=a,i=1,c=3,q=2"));
        Assert.Equal(3, image.Animation!.CurrentFrame);

        terminal.Write(Apc("a=d,d=f,i=1,r=3,q=2")); // the last: the new last shows
        Assert.Equal(2, image.Animation.CurrentFrame);

        terminal.Write(Apc("a=a,i=1,c=1,q=2"));
        terminal.Write(Apc("a=d,d=f,i=1,r=1,q=2")); // the first: the next moves into its place
        Assert.Equal(1, image.Animation.CurrentFrame);
        Assert.Equal(((byte)0, (byte)255, (byte)0), Colour(image, 1));
    }

    [Fact]
    public void An_animation_down_to_one_frame_stops()
    {
        var (terminal, image) = ThreeFrames();
        terminal.Write(Apc("a=a,i=1,s=3,q=2"));
        Assert.True(terminal.HasRunningAnimations());

        terminal.Write(Apc("a=d,d=f,i=1,q=2"));
        Assert.True(terminal.HasRunningAnimations());
        terminal.Write(Apc("a=d,d=f,i=1,q=2"));

        Assert.Equal(1, image.Animation!.FrameCount);
        Assert.False(terminal.HasRunningAnimations());
    }

    [Fact]
    public void The_last_frame_is_the_picture_lower_case_keeps_it_upper_case_deletes_it()
    {
        var (terminal, image) = ThreeFrames();
        terminal.Write(Apc("a=d,d=f,i=1,r=99,q=2")); // past the end: the last
        terminal.Write(Apc("a=d,d=f,i=1,q=2"));
        Assert.Equal(1, image.Animation!.FrameCount);

        terminal.Write(Apc("a=d,d=f,i=1,q=2"));
        Assert.Equal(1, image.Animation.FrameCount);
        Assert.Equal(4, Tiles(terminal));

        terminal.Write(Apc("a=d,d=F,i=1,q=2"));
        Assert.Equal(0, Tiles(terminal));
        Assert.False(IsHeld(terminal, 1));
    }

    // ---- the registry's byte count ----------------------------------------------------------------

    /// <summary>
    /// An animated image deleted gives back what it was charged, no more.
    /// </summary>
    /// <remarks>
    /// It used to give back its size at the time, which counted the root frame twice: a picture
    /// more than it was charged. Three images then fit in two pictures' budget, and every animated
    /// image deleted bought room for one more.
    /// </remarks>
    [Fact]
    public void Deleting_an_animated_image_leaves_the_budget_as_it_found_it()
    {
        var terminal = Fresh(registryBudget: 3 * Picture);
        terminal.Write(Apc("a=t,i=1,f=32,s=4,v=4,q=2", Rgba(4, 4, 255, 0, 0)));
        terminal.Write(Apc("a=f,i=1,f=32,s=4,v=4,q=2", Rgba(4, 4, 0, 255, 0)));
        terminal.Write(Apc("a=d,d=I,i=1,q=2"));

        // Four pictures in a budget of three: the oldest has to go.
        for (uint id = 2; id <= 5; id++)
            terminal.Write(Apc($"a=t,i={id},f=32,s=4,v=4,q=2", Rgba(4, 4, 0, 0, 255)));

        Assert.False(IsHeld(terminal, 2));
        Assert.True(IsHeld(terminal, 3));
    }

    /// <summary>
    /// Editing the root copies it off the image, so the image grows after it was charged. Deleting
    /// it still gives back only what was charged.
    /// </summary>
    [Fact]
    public void Deleting_an_image_whose_root_was_edited_leaves_the_budget_as_it_found_it()
    {
        var terminal = Fresh(registryBudget: 3 * Picture);
        terminal.Write(Apc("a=t,i=1,f=32,s=4,v=4,q=2", Rgba(4, 4, 255, 0, 0)));
        terminal.Write(Apc("a=f,i=1,r=1,f=32,s=2,v=2,q=2", Rgba(2, 2, 0, 255, 0)));
        terminal.Write(Apc("a=d,d=I,i=1,q=2"));

        for (uint id = 2; id <= 5; id++)
            terminal.Write(Apc($"a=t,i={id},f=32,s=4,v=4,q=2", Rgba(4, 4, 0, 0, 255)));

        Assert.False(IsHeld(terminal, 2));
        Assert.True(IsHeld(terminal, 3));
    }

    [Fact]
    public void A_deleted_frame_gives_its_bytes_back()
    {
        var terminal = Fresh(registryBudget: 3 * Picture);
        terminal.Write(Apc("a=t,i=1,f=32,s=4,v=4,q=2", Rgba(4, 4, 255, 0, 0)));
        terminal.Write(Apc("a=f,i=1,f=32,s=4,v=4,q=2", Rgba(4, 4, 0, 255, 0)));
        terminal.Write(Apc("a=d,d=f,i=1,r=2,q=2"));

        // Image 1 is one picture again, so two more fit beside it.
        terminal.Write(Apc("a=t,i=2,f=32,s=4,v=4,q=2", Rgba(4, 4, 0, 0, 255)));
        terminal.Write(Apc("a=t,i=3,f=32,s=4,v=4,q=2", Rgba(4, 4, 0, 0, 255)));

        Assert.True(IsHeld(terminal, 1));
    }

    [Fact]
    public void An_animated_image_counts_each_frame_once()
    {
        var (_, image) = ThreeFrames();
        Assert.Equal(3 * Picture, image.ByteCount);
    }
}
