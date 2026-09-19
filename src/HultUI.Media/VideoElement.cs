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
/// Listening starts when the element is mounted and stops when it leaves the tree, so
/// a throwaway element from a rebuild never keeps a player's frames marking a detached
/// tree dirty. The accessible name defaults to the source's display name.
/// </remarks>
public class VideoElement : ImageElement
{
    private MediaPlayer? _player;
    private bool _mounted;
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
        _mounted = true;
        StartListening();
    }

    protected override void OnUnmounted()
    {
        base.OnUnmounted();
        _mounted = false;
        StopListening();
    }

    private void StartListening()
    {
        if (_listening || !_mounted || _player is null) return;
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

    internal override void PatchFrom(Element source)
    {
        base.PatchFrom(source);
        if (source is VideoElement video)
            Player = video.Player;
    }
}
