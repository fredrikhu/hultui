namespace HultUI.Core.Styling;

/// <summary>
/// A cursor drawn from pixels, for when no system cursor fits: the pointer of a remote desktop, a
/// drawing tool's brush outline. Set it with <c>.Cursor(image)</c>, which makes the element's
/// <see cref="CursorType"/> <see cref="CursorType.Image"/>.
/// </summary>
/// <remarks>
/// <para><b>Pixels.</b> Rows of RGBA bytes, top row first, alpha not premultiplied: the layout
/// <c>Window.SetIcon</c> takes. The constructor copies them, so the caller may reuse its buffer.</para>
///
/// <para><b>Identity.</b> Compared by reference. A platform makes one native cursor per instance
/// and keeps a few, so make one instance per shape and set it again when the shape comes back —
/// a remote desktop announcing a cached pointer — rather than a new instance with the same
/// pixels.</para>
///
/// <para><b>Size.</b> Drawn at its own pixel size; a display scale does not resize it.</para>
/// </remarks>
public sealed class CursorImage
{
    private readonly byte[] _pixels;

    /// <param name="width">Width in pixels, 1 or more.</param>
    /// <param name="height">Height in pixels, 1 or more.</param>
    /// <param name="rgbaPixels">At least <c>width * height * 4</c> bytes; any beyond are ignored.</param>
    /// <param name="hotspotX">The column the pointer's position is at, from the left.</param>
    /// <param name="hotspotY">The row the pointer's position is at, from the top.</param>
    public CursorImage(int width, int height, ReadOnlySpan<byte> rgbaPixels, int hotspotX, int hotspotY)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        var length = checked(width * height * 4);
        if (rgbaPixels.Length < length)
            throw new ArgumentException(
                $"A {width}x{height} cursor needs {length} bytes of RGBA; got {rgbaPixels.Length}.", nameof(rgbaPixels));
        if ((uint)hotspotX >= (uint)width || (uint)hotspotY >= (uint)height)
            throw new ArgumentOutOfRangeException(nameof(hotspotX),
                $"The hotspot ({hotspotX}, {hotspotY}) is outside the {width}x{height} image.");

        Width = width;
        Height = height;
        HotspotX = hotspotX;
        HotspotY = hotspotY;
        _pixels = rgbaPixels[..length].ToArray();
    }

    public int Width { get; }
    public int Height { get; }
    public int HotspotX { get; }
    public int HotspotY { get; }

    /// <summary>The image: <see cref="Height"/> rows of <see cref="Width"/> RGBA pixels.</summary>
    public ReadOnlySpan<byte> Pixels => _pixels;
}
