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
        if (Source is null) return;
        ctx.DrawImage(Source, Layout.AbsoluteContentBox, Fit);
    }

    internal override void PatchFrom(Element source)
    {
        base.PatchFrom(source);
        if (source is VideoElement video)
            Player = video.Player;
    }
}
