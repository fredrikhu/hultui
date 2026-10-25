using HultUI;
using HultUI.Core.Rendering;
using HultUI.Media;
using HultUI.Media.Mpv;
using HultUI.Rendering.Skia;
using SkiaSharp;

namespace Reel;

/// <summary>
/// The still under the pointer on the scrubber.
///
/// <para>Aperture asks the server, which rendered a sprite sheet when the film was added.
/// Nothing has done that for a file on disk, so Reel makes its own: a second, silent mpv
/// seeks through the file in the background and the frames are kept until another file is
/// opened. The scrubber shows whatever has arrived, so the stills fill in over the first
/// minute or two rather than all at once.</para>
///
/// <para>The policy — how many, how far apart, which one to show for a position — is
/// <see cref="ScrubPreviewCache"/>'s in the media layer, and could serve any player. What
/// is here is the two things it cannot know: where frames come from, and how to turn a PNG
/// into something this renderer can draw.</para>
/// </summary>
public sealed partial class ReelApp
{
    private ScrubPreviewCache? _previews;

    /// <summary>How wide a still is drawn. The media layer's default; named here because
    /// the decode width is chosen to match it.</summary>
    private const float PreviewWidth = 176f;

    /// <summary>Where stills come from: a second, silent mpv, or null when there is no libmpv.
    /// A test hands in its own.</summary>
    internal Func<IScrubFrameSource?> ScrubFrameSource { get; set; } =
        () => MpvFrameSource.IsAvailable ? new MpvFrameSource() : null;

    /// <summary>
    /// Starts making stills for the open file, if a decoder is available at all. Called once
    /// the duration is known — which is what says how far apart they should be.
    /// </summary>
    private void StartPreviews(string path, TimeSpan duration)
    {
        if (_videoPlayer is not { } video) return;

        if (_previews is null)
        {
            if (ScrubFrameSource() is not { } source) return;
            // A still landing changes nothing but the card over the scrubber, and only while the
            // pointer is on it: the player asks again for the point it is hovered at. This rebuilt
            // the whole app instead, 120 times a file, which never rebuilt the player that shows
            // the card, so a pointer resting on the scrubber saw a new still only when it next
            // moved. The worker has no business touching the window, so it is posted.
            Action refresh = () => _videoPlayer?.RefreshPreview();
            _previews = new ScrubPreviewCache(source, DecodeStill,
                onFrame: () => Application.InvokeOnMainThread(refresh));
        }

        // Every file has a player of its own (OpenStream), so each is handed the stills; only
        // the first file's used to be, and the next one in the folder had no preview at all.
        video.PreviewWidth = PreviewWidth;
        video.PreviewFrame = position => _previews?.Frame(position);
        _previews.Start(path, duration);
    }

    /// <summary>
    /// A PNG from mpv to something drawable, scaled on the way in. mpv hands back the video
    /// at its own size, and keeping 120 frames of 4K would be gigabytes for a picture drawn
    /// 176 points wide — so the resize happens once, here, rather than on every paint.
    /// </summary>
    private static IImageSource? DecodeStill(byte[] bytes)
    {
        using var decoded = SKBitmap.Decode(bytes);
        if (decoded is null) return null;

        // Twice the drawn width, so it stays sharp on a 2x display and on the larger card a
        // future layout might use.
        const int target = (int)(PreviewWidth * 2);
        if (decoded.Width <= target) return new SkiaImageSource(decoded.Copy(SKColorType.Bgra8888));

        int height = Math.Max(1, (int)Math.Round(decoded.Height * (target / (double)decoded.Width)));
        var scaled = decoded.Resize(new SKImageInfo(target, height, SKColorType.Bgra8888),
            new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
        return scaled is null ? null : new SkiaImageSource(scaled);
    }

    private void StopPreviews()
    {
        _previews?.Stop();
    }

    private void DisposePreviews()
    {
        _previews?.Dispose();
        _previews = null;
    }
}
