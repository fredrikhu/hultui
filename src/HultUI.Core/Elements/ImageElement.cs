using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;

namespace HultUI.Core.Elements;

public class ImageElement : Element
{
    public IImageSource? Source { get; set; }
    public ImageFit Fit { get; set; } = ImageFit.Contain;

    public static ImageElement New(IImageSource? source = null) => new() { Source = source };

    internal override Size MeasureContent(float availableWidth, float availableHeight, IMeasureContext ctx)
    {
        if (Source is null || !Source.IsLoaded)
            return Size.Zero;

        return Source.IntrinsicSize;
    }

    public override void Render(IRenderContext ctx)
    {
        if (Source is null || !Source.IsLoaded) return;

        ctx.DrawImage(Source, Layout.AbsoluteContentBox, Fit);
    }

    internal override void PatchFrom(Element source)
    {
        base.PatchFrom(source);
        if (source is ImageElement img)
        {
            Source = img.Source;
            Fit = img.Fit;
        }
    }
}
