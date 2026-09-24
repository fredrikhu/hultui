using HultUI.Core.Primitives;
using HultUI.Core.Rendering;

namespace HultUI.Media;

/// <summary>
/// A still to show for a position on the scrubber, supplied by the app through
/// <see cref="VideoPlayer.PreviewFrame"/>.
///
/// <para><paramref name="Source"/> is optional and is what makes a sprite sheet work: a
/// server's scrubber thumbnails arrive as a grid of frames in one image, and the frame to
/// show is a rectangle of it, in the image's own pixels. Null means the image is the
/// frame. <paramref name="Time"/> is the position the frame actually stands for, which is
/// rounded to whatever interval the thumbnails were made at — the label reads that rather
/// than the pointer's own position, or it would claim a precision the picture has not got.</para>
/// </summary>
public readonly record struct VideoPreviewFrame(IImageSource Image, Rect? Source = null, TimeSpan? Time = null);
