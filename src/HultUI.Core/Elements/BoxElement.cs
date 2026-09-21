using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;

namespace HultUI.Core.Elements;

public class BoxElement : Element
{
    // Visual properties are self-marking: assigning a new value marks the
    // element paint-dirty, so imperative mutations from any code path (event
    // handlers, await continuations, timers) become visible without an
    // explicit MarkPaintDirty. Equality checks keep no-op writes free — in
    // particular ApplyTheme and the reconciler re-assign these every pass.
    private Color _backgroundColor = Color.Transparent;
    public Color BackgroundColor
    {
        get => _backgroundColor;
        set { if (_backgroundColor == value) return; _backgroundColor = value; MarkPaintDirty(); }
    }

    private Color _borderColor = Color.Transparent;
    public Color BorderColor
    {
        get => _borderColor;
        set { if (_borderColor == value) return; _borderColor = value; MarkPaintDirty(); }
    }

    private BorderStyle _borderStyle = BorderStyle.Solid;
    public BorderStyle BorderStyle
    {
        get => _borderStyle;
        set { if (_borderStyle == value) return; _borderStyle = value; MarkPaintDirty(); }
    }

    /// <summary>A ring drawn outside the border box that takes up no room — see
    /// <see cref="Primitives.Outline"/>. Use it for a selected or active state on an
    /// auto-sized element, where adding a border would resize the element and shove its
    /// siblings along.</summary>
    private Outline _outline;
    public Outline Outline
    {
        get => _outline;
        set { if (_outline == value) return; _outline = value; MarkPaintDirty(); }
    }

    private CornerRadius _cornerRadius;
    public CornerRadius CornerRadius
    {
        get => _cornerRadius;
        set { if (_cornerRadius == value) return; _cornerRadius = value; MarkPaintDirty(); }
    }

    /// <summary>A gradient fill. When set it replaces <see cref="BackgroundColor"/>.</summary>
    private Gradient? _backgroundGradient;
    public Gradient? BackgroundGradient
    {
        get => _backgroundGradient;
        set { if (ReferenceEquals(_backgroundGradient, value)) return; _backgroundGradient = value; MarkPaintDirty(); }
    }

    /// <summary>
    /// Drop shadows drawn behind the box, back-to-front. Null = none. The
    /// getter exposes a <see cref="DirtyList{T}"/>, so in-place mutations
    /// (<c>Add</c>, <c>RemoveAt</c>, …) repaint automatically; the setter
    /// wraps whatever list is assigned (by reference, no copy).
    /// </summary>
    private DirtyList<BoxShadow>? _boxShadows;
    public IList<BoxShadow>? BoxShadows
    {
        get => _boxShadows;
        set
        {
            if (ReferenceEquals(_boxShadows, value)) return;
            _boxShadows = value is null ? null : new DirtyList<BoxShadow>(this, value);
            MarkPaintDirty();
        }
    }

    public static BoxElement New([OriginFile] string? file = null, [OriginLine] int line = 0) => new BoxElement().WithOrigin(file, line);

    internal override void PatchFrom(Element source)
    {
        base.PatchFrom(source);
        if (source is BoxElement box)
        {
            BackgroundColor = box.BackgroundColor;
            BorderColor = box.BorderColor;
            BorderStyle = box.BorderStyle;
            Outline = box.Outline;
            CornerRadius = box.CornerRadius;
            BackgroundGradient = box.BackgroundGradient;
            BoxShadows = box.BoxShadows;
        }
    }

    /// <summary>
    /// The radius this box is painted with. <see cref="CornerRadius"/> unless a subclass
    /// only knows it once laid out — a circular button is half its box, whatever the box
    /// turned out to be.
    /// </summary>
    protected virtual CornerRadius RenderCornerRadius(in Rect box) => CornerRadius;

    public override void Render(IRenderContext ctx)
    {
        var box = Layout.AbsoluteBorderBox;
        var radius = RenderCornerRadius(in box);

        // Shadows sit behind the box; draw them first so the background paints over
        // the portion beneath the box. Enumerate the field directly — DirtyList
        // exposes List<T>'s struct enumerator, the IList interface would box one
        // per frame.
        if (_boxShadows is { Count: > 0 } shadows)
        {
            foreach (var shadow in shadows)
                ctx.DrawBoxShadow(box, radius, shadow);
        }

        var w = BoxRead.BorderWidth;
        bool hasBorder = BorderColor.A > 0 && (w.Top > 0 || w.Right > 0 || w.Bottom > 0 || w.Left > 0);
        bool uniformWidth = w.Top == w.Right && w.Right == w.Bottom && w.Bottom == w.Left;
        // The simple case (uniform width, solid style) uses the combined draw call
        // and keeps its existing centered-stroke geometry, so nothing regresses.
        // Per-side widths or dashed/dotted styles route through DrawBorder instead.
        bool advancedBorder = hasBorder && (BorderStyle != BorderStyle.Solid || !uniformWidth);

        if (BackgroundGradient is { } gradient)
        {
            ctx.DrawGradientRect(box, radius, gradient);
            if (advancedBorder)
                ctx.DrawBorder(box, radius, BorderColor, w, BorderStyle);
            else if (hasBorder)
                ctx.DrawRoundedRect(box, radius, Color.Transparent, BorderColor, w);
        }
        else if (advancedBorder)
        {
            if (BackgroundColor.A > 0)
                ctx.DrawRoundedRect(box, radius, BackgroundColor, Color.Transparent, default);
            ctx.DrawBorder(box, radius, BorderColor, w, BorderStyle);
        }
        else if (BackgroundColor.A > 0 || hasBorder)
        {
            ctx.DrawRoundedRect(box, radius, BackgroundColor, hasBorder ? BorderColor : Color.Transparent, w);
        }

        // Last, and outside everything: the ring is painted over the element's own box the way
        // CSS paints an outline, and it is the only thing here drawn beyond the border box.
        if (_outline.IsVisible)
        {
            // A border is drawn inside the rect it is given, so the rect is inflated to the
            // ring's outer edge and the band falls inward from there. The radius grows by the
            // same amount, or the ring would square off at the corners while the box it
            // surrounds stays round.
            float outer = _outline.Offset + _outline.Width;
            var ring = box.Inflate(new Thickness(outer));
            ctx.DrawRoundedRect(ring, radius.Inflate(outer), Color.Transparent,
                _outline.Color, new Thickness(_outline.Width));
        }
    }
}
