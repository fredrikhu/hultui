using HultUI.Core.Elements;

namespace HultUI.Media;

/// <summary>Builder for <see cref="VideoElement"/>, in the style of <c>Image.New</c>.</summary>
public static class Video
{
    public static VideoElement New(MediaPlayer? player = null, [OriginFile] string? file = null, [OriginLine] int line = 0)
        => VideoElement.New(player, file, line);
}
