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

    // ──── Scaling to fit (opt-in) ────

    private bool _scaleDownToFit;
    private float? _naturalWidth, _maxImageHeight;

    /// <summary>
    /// When true the image lays out at its natural size scaled down — never up, and keeping
    /// its aspect ratio — so it fits the width it is offered and <see cref="MaxImageHeight"/>.
    /// CSS's <c>max-width: 100%; height: auto</c>: an image in flowing content (a document, a
    /// comment) that should not overflow a narrow column or stretch in a wide one. Give it
    /// <c>AlignSelf(FlexStart)</c> in a column, or the column stretches its box.
    /// See <see cref="ScaledSize"/> for the arithmetic, public so a caller that sizes rows
    /// before they are built can reach the same answer.
    /// </summary>
    public bool ScaleDownToFit
    {
        get => _scaleDownToFit;
        set { if (_scaleDownToFit == value) return; _scaleDownToFit = value; MarkLayoutDirty(); }
    }

    /// <summary>The width the image asks for before scaling, in layout units — what
    /// <c>width="600"</c> says on an HTML image. Null means its intrinsic width. Only read
    /// under <see cref="ScaleDownToFit"/>.</summary>
    public float? NaturalWidth
    {
        get => _naturalWidth;
        set { if (_naturalWidth == value) return; _naturalWidth = value; MarkLayoutDirty(); }
    }

    /// <summary>The tallest the image lays out under <see cref="ScaleDownToFit"/>; a taller
    /// one scales down further, narrowing with it. Null is no limit.</summary>
    public float? MaxImageHeight
    {
        get => _maxImageHeight;
        set { if (_maxImageHeight == value) return; _maxImageHeight = value; MarkLayoutDirty(); }
    }

    /// <summary>
    /// The size <see cref="ScaleDownToFit"/> lays an image of <paramref name="intrinsic"/> size
    /// out at: <paramref name="naturalWidth"/> (or the intrinsic width) with the height that
    /// keeps the aspect ratio, then scaled down uniformly until it fits
    /// <paramref name="availableWidth"/> and <paramref name="maxHeight"/>. Whole pixels, so a
    /// row sized from this and the element drawn from it agree exactly.
    /// </summary>
    public static Size ScaledSize(Size intrinsic, float? naturalWidth, float availableWidth, float? maxHeight)
    {
        if (intrinsic.Width <= 0 || intrinsic.Height <= 0) return Size.Zero;
        float w = naturalWidth is > 0 ? naturalWidth.Value : intrinsic.Width;
        float h = w * intrinsic.Height / intrinsic.Width;
        float scale = 1f;
        if (float.IsFinite(availableWidth) && availableWidth >= 0 && w > availableWidth) scale = availableWidth / w;
        if (maxHeight is > 0 && h * scale > maxHeight.Value) scale = maxHeight.Value / h;
        return new Size(MathF.Floor(w * scale), MathF.Floor(h * scale));
    }

    public static ImageElement New(IImageSource? source = null, [OriginFile] string? file = null, [OriginLine] int line = 0) =>
        new ImageElement { Source = source }.WithOrigin(file, line);

    /// <summary>
    /// The picture's shape — the crop's, under <see cref="SourceRect"/> — so an image given only a
    /// width is as tall as the picture is at that width, and one given only a height as wide.
    /// None until it loads, and none under <see cref="ScaleDownToFit"/>, which sizes itself.
    /// </summary>
    internal override float NaturalAspectRatio
    {
        get
        {
            if (_scaleDownToFit || Source is not { IsLoaded: true } source) return 0f;
            var size = _sourceRect is { } crop ? new Size(crop.Width, crop.Height) : source.IntrinsicSize;
            return size.Width > 0 && size.Height > 0 ? size.Width / size.Height : 0f;
        }
    }

    internal override Size MeasureContent(float availableWidth, float availableHeight, IMeasureContext ctx)
    {
        if (Source is null || !Source.IsLoaded)
            return Size.Zero;

        // A sheet measured whole would size the element to twenty frames.
        if (_sourceRect is { } crop)
            return _scaleDownToFit
                ? ScaledSize(new Size(crop.Width, crop.Height), _naturalWidth, availableWidth, _maxImageHeight)
                : new Size(crop.Width, crop.Height);

        return _scaleDownToFit
            ? ScaledSize(Source.IntrinsicSize, _naturalWidth, availableWidth, _maxImageHeight)
            : Source.IntrinsicSize;
    }

    /// <summary>
    /// One rectangle of the source, in the image's own pixels, drawn over the whole
    /// element — a frame cut from a sprite sheet. Null (the default) draws the image
    /// itself under <see cref="Fit"/>. When set it is also the intrinsic size, so a sheet
    /// of 20 frames measures as one frame rather than as the sheet.
    /// </summary>
    public Rect? SourceRect
    {
        get => _sourceRect;
        set { if (_sourceRect != value) { _sourceRect = value; MarkLayoutDirty(); } }
    }
    private Rect? _sourceRect;

    public override void Render(IRenderContext ctx)
    {
        if (Source is null || !Source.IsLoaded) return;

        if (_sourceRect is { } crop) ctx.DrawImage(Source, Layout.AbsoluteContentBox, crop);
        else ctx.DrawImage(Source, Layout.AbsoluteContentBox, Fit);
    }

    internal override void PatchFrom(Element source)
    {
        base.PatchFrom(source);
        if (source is ImageElement img)
        {
            Source = img.Source;
            Fit = img.Fit;
            SourceRect = img.SourceRect;
            ScaleDownToFit = img.ScaleDownToFit;
            NaturalWidth = img.NaturalWidth;
            MaxImageHeight = img.MaxImageHeight;
        }
    }
}
