using XTerm.Graphics;
using XTerm.Options;

namespace XTerm.Tests.Graphics;

/// <summary>
/// Kitty animation: frames added to an image, composed from deltas, and stepped either by the
/// client or by the terminal.
///
/// <para>Nothing here keeps time. The emulator is driven by <c>Write</c> and owns no timer, so a
/// host calls <c>AdvanceAnimations</c> with however long its last frame took. That makes the timing
/// exactly testable -- no sleeping, no tolerance windows, no flake.</para>
///
/// <para>The pixel checks matter more than they look. A frame is built by composing a rectangle
/// onto a canvas, so an error in the offsets or the blend produces a picture that is the right size
/// and the wrong content, which no structural assertion would catch.</para>
/// </summary>
public class KittyAnimationTests
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

    /// <summary>A solid block of RGBA, as the protocol carries it.</summary>
    private static string Rgba(int width, int height, byte r, byte g, byte b, byte a = 255)
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

    /// <summary>Transmits a 4x4 red picture under id 1 and returns the terminal.</summary>
    private static Terminal WithImage()
    {
        var terminal = Fresh();
        terminal.Write(Apc("a=t,i=1,f=32,s=4,v=4,q=2", Rgba(4, 4, 255, 0, 0)));
        return terminal;
    }

    private static TerminalImage Image(Terminal terminal, uint id = 1)
    {
        // Placing it is the only way to reach the image from outside, and C=1 keeps the cursor put.
        terminal.Write(Apc($"a=p,i={id},C=1,q=2"));
        var image = ImageAssertions.ImageAt(terminal, 0, 0);
        Assert.NotNull(image);
        return image!;
    }

    /// <summary>Reads a pixel of a frame as (R, G, B, A); the buffer itself is BGRA.</summary>
    private static (byte R, byte G, byte B, byte A) Pixel(ReadOnlyMemory<byte> pixels, int width, int x, int y)
    {
        var span = pixels.Span;
        var at = (y * width + x) * TerminalImage.BytesPerPixel;
        return (span[at + 2], span[at + 1], span[at], span[at + 3]);
    }

    private static List<string> Replies(Terminal terminal)
    {
        var replies = new List<string>();
        terminal.DataReceived += (_, e) => replies.Add(e.Data);
        return replies;
    }

    // ---- adding frames ----------------------------------------------------------------------------

    [Fact]
    public void A_still_picture_has_no_animation_until_a_frame_is_added()
    {
        var terminal = WithImage();
        var image = Image(terminal);

        Assert.Null(image.Animation);

        terminal.Write(Apc("a=f,i=1,f=32,s=4,v=4,q=2", Rgba(4, 4, 0, 255, 0)));

        Assert.NotNull(image.Animation);
        Assert.Equal(2, image.Animation!.FrameCount);
    }

    /// <summary>The root frame is frame one, and it is the picture the image was made from.</summary>
    [Fact]
    public void The_root_frame_is_the_original_picture()
    {
        var terminal = WithImage();
        var image = Image(terminal);
        terminal.Write(Apc("a=f,i=1,f=32,s=4,v=4,q=2", Rgba(4, 4, 0, 255, 0)));

        Assert.True(image.Animation!.TryGetFrame(1, out var root));
        Assert.Equal((255, (byte)0, (byte)0, (byte)255), Pixel(root.Pixels, 4, 0, 0));
    }

    /// <summary>
    /// A frame need only carry the pixels that changed. The rest of the canvas comes from the frame
    /// the client names with c=, which is the whole point of the delta form.
    /// </summary>
    [Fact]
    public void A_partial_frame_composes_onto_a_named_base_frame()
    {
        var terminal = WithImage();
        var image = Image(terminal);

        // A single blue pixel at (1,1), over the red root.
        terminal.Write(Apc("a=f,i=1,c=1,x=1,y=1,f=32,s=1,v=1,q=2", Rgba(1, 1, 0, 0, 255)));

        Assert.True(image.Animation!.TryGetFrame(2, out var frame));
        Assert.Equal((0, (byte)0, (byte)255, (byte)255), Pixel(frame.Pixels, 4, 1, 1));
        Assert.Equal((255, (byte)0, (byte)0, (byte)255), Pixel(frame.Pixels, 4, 0, 0));
    }

    /// <summary>
    /// Without a base frame the canvas is a flat colour, black and fully transparent unless the
    /// client says otherwise. The colour arrives as RGBA and the buffer is BGRA, so a swap here is
    /// a picture that looks right until something is transparent.
    /// </summary>
    [Fact]
    public void An_unbased_frame_starts_from_the_background_colour()
    {
        var terminal = WithImage();
        var image = Image(terminal);

        // Y=4278190335 is 0xff0000ff: opaque red in RGBA.
        terminal.Write(Apc("a=f,i=1,Y=4278190335,x=1,y=1,f=32,s=1,v=1,q=2", Rgba(1, 1, 0, 0, 255)));

        Assert.True(image.Animation!.TryGetFrame(2, out var frame));
        Assert.Equal((255, (byte)0, (byte)0, (byte)255), Pixel(frame.Pixels, 4, 0, 0));
        Assert.Equal((0, (byte)0, (byte)255, (byte)255), Pixel(frame.Pixels, 4, 1, 1));
    }

    [Fact]
    public void An_unbased_frame_with_no_colour_is_transparent()
    {
        var terminal = WithImage();
        var image = Image(terminal);

        terminal.Write(Apc("a=f,i=1,x=1,y=1,f=32,s=1,v=1,q=2", Rgba(1, 1, 0, 0, 255)));

        Assert.True(image.Animation!.TryGetFrame(2, out var frame));
        Assert.Equal((0, (byte)0, (byte)0, (byte)0), Pixel(frame.Pixels, 4, 0, 0));
    }

    /// <summary>
    /// Editing a frame with r= composes onto that frame itself, so several rectangles can build one
    /// frame up piece by piece without a new frame each time.
    /// </summary>
    [Fact]
    public void Editing_a_frame_composes_onto_itself()
    {
        var terminal = WithImage();
        var image = Image(terminal);
        terminal.Write(Apc("a=f,i=1,c=1,f=32,s=4,v=4,q=2", Rgba(4, 4, 0, 255, 0)));

        terminal.Write(Apc("a=f,i=1,r=2,x=0,y=0,f=32,s=1,v=1,q=2", Rgba(1, 1, 0, 0, 255)));

        Assert.Equal(2, image.Animation!.FrameCount);
        Assert.True(image.Animation.TryGetFrame(2, out var frame));
        Assert.Equal((0, (byte)0, (byte)255, (byte)255), Pixel(frame.Pixels, 4, 0, 0));
        Assert.Equal((0, (byte)255, (byte)0, (byte)255), Pixel(frame.Pixels, 4, 3, 3));
    }

    /// <summary>
    /// Editing the root must not change the image's own pixels. A host may have uploaded them as a
    /// texture and been told they never change.
    /// </summary>
    [Fact]
    public void Editing_the_root_frame_leaves_the_images_own_pixels_alone()
    {
        var terminal = WithImage();
        var image = Image(terminal);
        terminal.Write(Apc("a=f,i=1,c=1,f=32,s=4,v=4,q=2", Rgba(4, 4, 0, 255, 0)));

        terminal.Write(Apc("a=f,i=1,r=1,x=0,y=0,X=1,f=32,s=1,v=1,q=2", Rgba(1, 1, 0, 0, 255)));

        Assert.Equal((255, (byte)0, (byte)0, (byte)255), Pixel(image.Pixels, 4, 0, 0));
        Assert.True(image.Animation!.TryGetFrame(1, out var root));
        Assert.Equal((0, (byte)0, (byte)255, (byte)255), Pixel(root.Pixels, 4, 0, 0));
    }

    /// <summary>X=1 overwrites outright; the default blends, so a translucent pixel lets the base through.</summary>
    [Fact]
    public void The_composition_mode_decides_whether_pixels_blend_or_replace()
    {
        var terminal = WithImage();
        var image = Image(terminal);

        // Half-transparent blue over the opaque red root.
        terminal.Write(Apc("a=f,i=1,c=1,x=0,y=0,f=32,s=1,v=1,q=2", Rgba(1, 1, 0, 0, 255, 128)));
        terminal.Write(Apc("a=f,i=1,c=1,x=0,y=0,X=1,f=32,s=1,v=1,q=2", Rgba(1, 1, 0, 0, 255, 128)));

        Assert.True(image.Animation!.TryGetFrame(2, out var blended));
        Assert.True(image.Animation.TryGetFrame(3, out var replaced));

        var mixed = Pixel(blended.Pixels, 4, 0, 0);
        Assert.True(mixed.R is > 100 and < 200, $"red should have survived the blend, got {mixed.R}");
        Assert.True(mixed.B is > 100 and < 200, $"blue should have come through, got {mixed.B}");

        Assert.Equal((0, (byte)0, (byte)255, (byte)128), Pixel(replaced.Pixels, 4, 0, 0));
    }

    [Fact]
    public void A_frame_for_an_unknown_image_is_refused()
    {
        var terminal = WithImage();
        var replies = Replies(terminal);

        terminal.Write(Apc("a=f,i=99,f=32,s=4,v=4", Rgba(4, 4, 0, 255, 0)));

        Assert.Contains(replies, r => r.Contains("ENOENT"));
    }

    // ---- client driven ----------------------------------------------------------------------------

    /// <summary>The simplest animation: the client says which frame to show, and nothing else moves.</summary>
    [Fact]
    public void A_client_can_make_a_frame_current()
    {
        var terminal = WithImage();
        var image = Image(terminal);
        terminal.Write(Apc("a=f,i=1,c=1,f=32,s=4,v=4,q=2", Rgba(4, 4, 0, 255, 0)));

        terminal.Write(Apc("a=a,i=1,c=2,q=2"));

        Assert.Equal(2, image.Animation!.CurrentFrame);
        Assert.Equal((0, (byte)255, (byte)0, (byte)255), Pixel(image.CurrentPixels, 4, 0, 0));
    }

    [Fact]
    public void A_frame_that_does_not_exist_cannot_be_made_current()
    {
        var terminal = WithImage();
        Image(terminal);
        terminal.Write(Apc("a=f,i=1,c=1,f=32,s=4,v=4,q=2", Rgba(4, 4, 0, 255, 0)));
        var replies = Replies(terminal);

        terminal.Write(Apc("a=a,i=1,c=9"));

        Assert.Contains(replies, r => r.Contains("ENOENT"));
    }

    /// <summary>
    /// The image's own pixels stay the root frame whatever is current, so a host that cached them
    /// is not lied to; what moves is <c>CurrentPixels</c>.
    /// </summary>
    [Fact]
    public void The_current_frame_moves_but_the_root_does_not()
    {
        var terminal = WithImage();
        var image = Image(terminal);
        terminal.Write(Apc("a=f,i=1,c=1,f=32,s=4,v=4,q=2", Rgba(4, 4, 0, 255, 0)));

        terminal.Write(Apc("a=a,i=1,c=2,q=2"));

        Assert.Equal((255, (byte)0, (byte)0, (byte)255), Pixel(image.Pixels, 4, 0, 0));
        Assert.Equal((0, (byte)255, (byte)0, (byte)255), Pixel(image.CurrentPixels, 4, 0, 0));
    }

    // ---- terminal driven --------------------------------------------------------------------------

    /// <summary>Builds a two frame animation with a known gap and starts it running.</summary>
    private static (Terminal Terminal, TerminalImage Image) Running(int gap = 100, string state = "3")
    {
        var terminal = WithImage();
        var image = Image(terminal);
        terminal.Write(Apc($"a=f,i=1,c=1,z={gap},f=32,s=4,v=4,q=2", Rgba(4, 4, 0, 255, 0)));
        terminal.Write(Apc($"a=a,i=1,r=1,z={gap},q=2"));
        terminal.Write(Apc($"a=a,i=1,s={state},q=2"));
        return (terminal, image);
    }

    [Fact]
    public void A_stopped_animation_does_not_move()
    {
        var (terminal, image) = Running(state: "1");

        Assert.False(terminal.AdvanceAnimations(TimeSpan.FromSeconds(10)));
        Assert.Equal(1, image.Animation!.CurrentFrame);
    }

    [Fact]
    public void Time_shorter_than_the_gap_does_not_advance_the_frame()
    {
        var (terminal, image) = Running(gap: 100);

        Assert.False(terminal.AdvanceAnimations(TimeSpan.FromMilliseconds(60)));
        Assert.Equal(1, image.Animation!.CurrentFrame);
    }

    [Fact]
    public void Time_past_the_gap_advances_the_frame()
    {
        var (terminal, image) = Running(gap: 100);

        Assert.True(terminal.AdvanceAnimations(TimeSpan.FromMilliseconds(120)));
        Assert.Equal(2, image.Animation!.CurrentFrame);
    }

    /// <summary>
    /// Several gaps inside one slice step several frames. A host that repaints late must not make
    /// the animation run slow -- the elapsed time is what decides, not the number of calls.
    /// </summary>
    [Fact]
    public void A_long_slice_steps_more_than_one_frame()
    {
        var (terminal, image) = Running(gap: 50);

        // Three gaps: 1 -> 2 -> 1 -> 2.
        terminal.AdvanceAnimations(TimeSpan.FromMilliseconds(160));

        Assert.Equal(2, image.Animation!.CurrentFrame);
    }

    /// <summary>Running loops back to the first frame rather than stopping at the last.</summary>
    [Fact]
    public void A_running_animation_loops()
    {
        var (terminal, image) = Running(gap: 50);

        terminal.AdvanceAnimations(TimeSpan.FromMilliseconds(60));
        Assert.Equal(2, image.Animation!.CurrentFrame);

        terminal.AdvanceAnimations(TimeSpan.FromMilliseconds(60));
        Assert.Equal(1, image.Animation.CurrentFrame);
    }

    /// <summary>
    /// A finite loop count plays that many minus one, then stops -- which is the protocol's
    /// arithmetic, not an off-by-one. v=2 means one loop.
    /// </summary>
    [Fact]
    public void A_finite_loop_count_stops_when_it_is_spent()
    {
        var terminal = WithImage();
        var image = Image(terminal);
        terminal.Write(Apc("a=f,i=1,c=1,z=50,f=32,s=4,v=4,q=2", Rgba(4, 4, 0, 255, 0)));
        terminal.Write(Apc("a=a,i=1,r=1,z=50,q=2"));
        terminal.Write(Apc("a=a,i=1,s=3,v=2,q=2"));

        // 1 -> 2, then round to 1 (one loop spent), then 2, and no further.
        terminal.AdvanceAnimations(TimeSpan.FromMilliseconds(60));
        terminal.AdvanceAnimations(TimeSpan.FromMilliseconds(60));
        terminal.AdvanceAnimations(TimeSpan.FromMilliseconds(60));
        terminal.AdvanceAnimations(TimeSpan.FromMilliseconds(500));

        Assert.Equal(AnimationState.Stopped, image.Animation!.State);
    }

    /// <summary>Unspecified loops means forever, not none.</summary>
    [Fact]
    public void An_unspecified_loop_count_runs_indefinitely()
    {
        var (terminal, image) = Running(gap: 50);

        for (int i = 0; i < 20; i++)
            terminal.AdvanceAnimations(TimeSpan.FromMilliseconds(60));

        Assert.NotEqual(AnimationState.Stopped, image.Animation!.State);
    }
    /// <summary>
    /// Loading mode waits at the last frame instead of looping, so an animation can start playing
    /// before all of it has arrived without repeating the part that has.
    /// </summary>
    [Fact]
    public void A_loading_animation_waits_at_the_last_frame()
    {
        var (terminal, image) = Running(gap: 50, state: "2");

        terminal.AdvanceAnimations(TimeSpan.FromMilliseconds(60));
        Assert.Equal(2, image.Animation!.CurrentFrame);

        terminal.AdvanceAnimations(TimeSpan.FromMilliseconds(500));
        Assert.Equal(2, image.Animation.CurrentFrame);
    }

    /// <summary>
    /// The serial changes with the visible pixels, which is how a host spots a stale texture without
    /// comparing buffers.
    /// </summary>
    [Fact]
    public void The_frame_serial_changes_when_the_picture_does()
    {
        var (terminal, image) = Running(gap: 50);
        var before = image.FrameSerial;

        terminal.AdvanceAnimations(TimeSpan.FromMilliseconds(60));

        Assert.NotEqual(before, image.FrameSerial);
    }

    [Fact]
    public void A_terminal_with_no_animation_reports_none_running()
    {
        var terminal = WithImage();
        Image(terminal);

        Assert.False(terminal.HasRunningAnimations());
        Assert.False(terminal.AdvanceAnimations(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void A_running_animation_is_reported_as_running()
    {
        var (terminal, _) = Running();

        Assert.True(terminal.HasRunningAnimations());
    }

    /// <summary>
    /// An animation on an image that was transmitted but never placed still runs. A client may set
    /// one going and only then decide where to show it.
    /// </summary>
    [Fact]
    public void An_unplaced_animation_still_runs()
    {
        var terminal = Fresh();
        terminal.Write(Apc("a=t,i=1,f=32,s=4,v=4,q=2", Rgba(4, 4, 255, 0, 0)));
        terminal.Write(Apc("a=f,i=1,c=1,z=50,f=32,s=4,v=4,q=2", Rgba(4, 4, 0, 255, 0)));
        terminal.Write(Apc("a=a,i=1,r=1,z=50,q=2"));
        terminal.Write(Apc("a=a,i=1,s=3,q=2"));

        Assert.True(terminal.HasRunningAnimations());
        Assert.True(terminal.AdvanceAnimations(TimeSpan.FromMilliseconds(60)));
    }

    /// <summary>
    /// A gapless frame is never shown. It exists to hold base data for the frames that compose
    /// against it -- a static background under a moving object, say.
    /// </summary>
    [Fact]
    public void A_gapless_frame_is_skipped_rather_than_displayed()
    {
        var terminal = WithImage();
        var image = Image(terminal);

        terminal.Write(Apc("a=f,i=1,c=1,z=-1,f=32,s=4,v=4,q=2", Rgba(4, 4, 0, 255, 0)));   // frame 2
        terminal.Write(Apc("a=f,i=1,c=1,z=50,f=32,s=4,v=4,q=2", Rgba(4, 4, 0, 0, 255)));   // frame 3
        terminal.Write(Apc("a=a,i=1,r=1,z=50,q=2"));
        terminal.Write(Apc("a=a,i=1,s=3,q=2"));

        // Exactly two gaps' worth. A gapless frame consumes no time at all, so 50 for frame one and
        // 50 for frame three comes right back round to frame one. Were it treated as an ordinary
        // frame -- even a one millisecond one -- the total would overrun and land elsewhere, which
        // is what makes this a test of the skip rather than of the arithmetic around it.
        terminal.AdvanceAnimations(TimeSpan.FromMilliseconds(100));

        Assert.Equal(1, image.Animation!.CurrentFrame);
    }

    // ---- composing frames -------------------------------------------------------------------------

    /// <summary>
    /// a=c copies a rectangle from one frame onto another with no pixels crossing the wire.
    /// </summary>
    [Fact]
    public void A_rectangle_can_be_composed_from_one_frame_onto_another()
    {
        var terminal = WithImage();
        var image = Image(terminal);
        terminal.Write(Apc("a=f,i=1,f=32,s=4,v=4,q=2", Rgba(4, 4, 0, 255, 0)));   // frame 2, green

        // A 1x1 block from the root's (0,0) onto frame 2 at (2,2).
        terminal.Write(Apc("a=c,i=1,r=1,c=2,w=1,h=1,X=0,Y=0,x=2,y=2,C=1,q=2"));

        Assert.True(image.Animation!.TryGetFrame(2, out var frame));
        Assert.Equal((255, (byte)0, (byte)0, (byte)255), Pixel(frame.Pixels, 4, 2, 2));
        Assert.Equal((0, (byte)255, (byte)0, (byte)255), Pixel(frame.Pixels, 4, 0, 0));
    }

    [Fact]
    public void Composing_from_a_frame_that_does_not_exist_is_refused()
    {
        var terminal = WithImage();
        Image(terminal);
        terminal.Write(Apc("a=f,i=1,f=32,s=4,v=4,q=2", Rgba(4, 4, 0, 255, 0)));
        var replies = Replies(terminal);

        terminal.Write(Apc("a=c,i=1,r=9,c=2,w=1,h=1"));

        Assert.Contains(replies, r => r.Contains("ENOENT"));
    }

    /// <summary>A rectangle running off the edge is EINVAL, which the protocol states outright.</summary>
    [Fact]
    public void Composing_out_of_bounds_is_refused()
    {
        var terminal = WithImage();
        Image(terminal);
        terminal.Write(Apc("a=f,i=1,f=32,s=4,v=4,q=2", Rgba(4, 4, 0, 255, 0)));
        var replies = Replies(terminal);

        terminal.Write(Apc("a=c,i=1,r=1,c=2,w=4,h=4,x=2,y=2"));

        Assert.Contains(replies, r => r.Contains("EINVAL"));
    }

    /// <summary>
    /// One frame onto itself with overlapping rectangles is refused: the answer would depend on the
    /// order the pixels were copied in, so there is no right one.
    /// </summary>
    [Fact]
    public void Composing_a_frame_onto_itself_with_overlap_is_refused()
    {
        var terminal = WithImage();
        Image(terminal);
        terminal.Write(Apc("a=f,i=1,f=32,s=4,v=4,q=2", Rgba(4, 4, 0, 255, 0)));
        var replies = Replies(terminal);

        terminal.Write(Apc("a=c,i=1,r=2,c=2,w=3,h=3,X=0,Y=0,x=1,y=1"));

        Assert.Contains(replies, r => r.Contains("EINVAL"));
    }

    /// <summary>The same frame is fine when the rectangles do not touch.</summary>
    [Fact]
    public void Composing_a_frame_onto_itself_without_overlap_is_allowed()
    {
        var terminal = WithImage();
        Image(terminal);
        terminal.Write(Apc("a=f,i=1,f=32,s=4,v=4,q=2", Rgba(4, 4, 0, 255, 0)));
        var replies = Replies(terminal);

        terminal.Write(Apc("a=c,i=1,r=2,c=2,w=2,h=2,X=0,Y=0,x=2,y=2,C=1"));

        Assert.DoesNotContain(replies, r => r.Contains("EINVAL"));
    }

    /// <summary>
    /// The frames are counted against the image budget as well as the root picture, and the two are
    /// summed as a long.
    /// </summary>
    /// <remarks>
    /// This used to clamp the animation to <c>int.MaxValue</c> and then add the root in int
    /// arithmetic, so the total wrapped negative at exactly the size the clamp existed to guard
    /// against -- and a negative byte count makes the eviction sweep believe an image is free. The
    /// overflow itself needs gigabytes to reach; what is checkable here is that the sum is a long
    /// and that it counts both halves.
    /// </remarks>
    [Fact]
    public void An_animation_counts_its_frames_against_the_budget()
    {
        var terminal = WithImage();
        var image = Image(terminal);

        var root = image.ByteCount;
        Assert.Equal(4 * 4 * TerminalImage.BytesPerPixel, root);
        Assert.Null(image.Animation);

        terminal.Write(Apc("a=f,i=1,z=100,f=32,s=4,v=4,q=2", Rgba(4, 4, 0, 255, 0)));

        // Both halves, added together rather than one of them clamped and then added.
        Assert.NotNull(image.Animation);
        Assert.Equal(root + image.Animation!.ByteCount, image.ByteCount);
        Assert.True(image.ByteCount > root, "the frames have to reach the budget somehow");
    }
}
