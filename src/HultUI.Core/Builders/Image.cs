using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Rendering;

namespace HultUI.Core.Builders;

public static class Image
{
    public static ImageElement New(IImageSource? source = null, [OriginFile] string? file = null, [OriginLine] int line = 0) => ImageElement.New(source, file, line);
}
