using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;

namespace HultUI.Core.Elements;

public class ImageElement : Element
{
    // Self-marking: swapping the source at runtime (an async load completing)
    // changes the intrinsic size, so it invalidates layout as well as paint.
    private IImageSource? _source;
    public IImageSource? Source
    {
        get => _source;
        set
        {
            if (ReferenceEquals(_source, value)) return;
            _source = value;
            MarkLayoutDirty();
            MarkPaintDirty();
        }
    }

    private ImageFit _fit = ImageFit.Contain;
    public ImageFit Fit
    {
        get => _fit;
        set { if (_fit == value) return; _fit = value; MarkPaintDirty(); }
    }

    public static ImageElement New(IImageSource? source = null, [OriginFile] string? file = null, [OriginLine] int line = 0) =>
        new ImageElement { Source = source }.WithOrigin(file, line);

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
