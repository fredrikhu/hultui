using HultUI.Core.Accessibility;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;

namespace HultUI.Media;

/// <summary>
/// Paints a <see cref="MediaPlayer"/>'s frames. An <see cref="ImageElement"/> whose
/// source is the player's frame ring, which repaints itself when a frame arrives and
/// measures to the video's aspect ratio inside whatever space it is given.
/// </summary>
/// <remarks>
/// <para>Listening starts only while the element hangs off a hosted root — at its first
/// paint, or on mounting into a tree a window already shows — and stops when it is
/// unmounted or a frame finds it no longer hosted. <see cref="Element.OnMounted"/> alone
/// is not enough: the freshly built twin of every rebuild gets a parent, and so
/// <c>OnMounted</c>, from its builder, then is patched into the live element and thrown
/// away without an unmount. A player playing under a rebuilding overlay collected one
/// dead listener per rebuild that way. Nor does every exit unmount: <c>SetRoot</c>
/// unmounts nothing.</para>
/// <para>The accessible name defaults to the source's display name.</para>
/// </remarks>
public class VideoElement : ImageElement
{
    private MediaPlayer? _player;
    private bool _listening;
    private Size _lastFrameSize;

    public VideoElement()
    {
        Role = AccessibilityRole.Image;
        Fit = ImageFit.Contain;
        // MeasureContent scales the frame down to the height offered, so a measurement holds at
        // that height only.
        IgnoresAvailableHeight = false;
    }

    public MediaPlayer? Player
    {
        get => _player;
        set
        {
            if (ReferenceEquals(_player, value)) return;
            StopListening();
            _player = value;
            Source = value?.Frames;
            _lastFrameSize = Source?.IntrinsicSize ?? Size.Zero;
            AccessibleName ??= value?.Source?.DisplayName;
            StartListening();
        }
    }

    public static VideoElement New(MediaPlayer? player = null, [OriginFile] string? file = null, [OriginLine] int line = 0) =>
        new VideoElement { Player = player }.WithOrigin(file, line);

    internal bool IsListeningForFrames => _listening;

    protected override void OnMounted()
    {
        base.OnMounted();
        StartListening();
    }

    protected override void OnUnmounted()
    {
        base.OnUnmounted();
        StopListening();
    }

    // A twin's root has no host, so a rebuild's throwaway never subscribes. The hosted
    // walk runs only while not yet listening; Render calls this every frame.
    private void StartListening()
    {
        if (_listening || _player is null || !IsHosted) return;
        _player.FrameArrived += OnFrameArrived;
        _listening = true;
    }

    private void StopListening()
    {
        if (!_listening) return;
        _player!.FrameArrived -= OnFrameArrived;
        _listening = false;
        TellFrameRate(null);
    }

    private void OnFrameArrived()
    {
        // Left behind without an unmount: the root swapped by SetRoot.
        if (!IsHosted) { StopListening(); return; }
        var size = Source?.IntrinsicSize ?? Size.Zero;
        if (size != _lastFrameSize)
        {
            _lastFrameSize = size;
            MarkLayoutDirty();
        }
        MarkPaintDirty();
        TellFrameRate(CoversItsWindow() ? _player?.VideoFrameRate : null);
    }

    /// <summary>The window told of this video's frame rate, and the rate told.</summary>
    private Window? _toldWindow;
    private double? _toldRate;

    /// <summary>
    /// Hands the window the frame rate of the video it shows over most of itself, for a TV's display
    /// to match (docs/design/TV-VIDEO.md §5), and null when this video goes or stops covering it: a
    /// thumbnail or a trailer in a corner switches no display.
    /// </summary>
    private void TellFrameRate(double? rate)
    {
        if (rate == _toldRate) return;
        var window = rate is null ? _toldWindow : OverlayHost as Window;
        _toldRate = rate;
        _toldWindow = rate is null ? null : window;
        window?.SetContentFrameRate(this, rate);
    }

    /// <summary>Whether this video takes at least half of its window: the film, not a preview.</summary>
    private bool CoversItsWindow()
    {
        if (OverlayHost is not Window window) return false;
        var box = WindowBorderBox;
        float area = window.LayoutWidth * window.LayoutHeight;
        return area > 0 && box.Width * box.Height >= area / 2f;
    }

    /// <summary>
    /// The frame's size, scaled down — never up — to fit whichever of the available
    /// dimensions is bounded, keeping the aspect ratio. An image element reports its
    /// intrinsic size and lets the box crop or letterbox; a video in a column would then
    /// keep a 1080-pixel height while its width shrank to the column.
    /// </summary>
    internal override Size MeasureContent(float availableWidth, float availableHeight, IMeasureContext ctx)
    {
        if (Source is null || !Source.IsLoaded) return Size.Zero;
        var size = Source.IntrinsicSize;
        if (size.Width <= 0 || size.Height <= 0) return Size.Zero;

        float scale = 1f;
        if (!LayoutSpace.IsUnbounded(availableWidth) && availableWidth < size.Width)
            scale = availableWidth / size.Width;
        if (!LayoutSpace.IsUnbounded(availableHeight) && availableHeight < size.Height * scale)
            scale = availableHeight / size.Height;
        return new Size(size.Width * scale, size.Height * scale);
    }

    /// <summary>
    /// Unlike an image, a video is drawn even before its first frame: the renderer's
    /// visit is what tells the frame source which kind of surface it is on, and a
    /// decoder needs to know that before it can load anything. The first paint is also
    /// where an element that entered the tree inside a freshly built subtree, or before its
    /// root was shown, starts listening for frames.
    /// </summary>
    public override void Render(IRenderContext ctx)
    {
        StartListening();
        // Laid out by now: a frame that arrived before the first layout found no box to measure the
        // window by, and a paused film sends no more.
        if (_listening) TellFrameRate(CoversItsWindow() ? _player?.VideoFrameRate : null);
        if (Source is null) return;
        ctx.DrawImage(Source, Layout.AbsoluteContentBox, Fit);
    }

    // Its measure reads the source's size. Image's setters mark the layout when what they copy
    // changes it, Player's does through Source, and a frame of another size marks it as it
    // arrives (OnFrameArrived).
    private protected override bool PatchMarksOwnLayout => GetType() == typeof(VideoElement);

    internal override void PatchFrom(Element source)
    {
        base.PatchFrom(source);
        if (source is VideoElement video)
            Player = video.Player;
    }
}
