using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;

namespace HultUI.Core.Elements;

public class BoxElement : Element
{
    public Color BackgroundColor { get; set; } = Color.Transparent;
    public Color BorderColor { get; set; } = Color.Transparent;
    public BorderStyle BorderStyle { get; set; } = BorderStyle.Solid;
    public CornerRadius CornerRadius { get; set; }

    /// <summary>A gradient fill. When set it replaces <see cref="BackgroundColor"/>.</summary>
    public Gradient? BackgroundGradient { get; set; }

    /// <summary>Drop shadows drawn behind the box, back-to-front. Null = none.</summary>
    public List<BoxShadow>? BoxShadows { get; set; }

    public static BoxElement New() => new();

    internal override void PatchFrom(Element source)
    {
        base.PatchFrom(source);
        if (source is BoxElement box)
        {
            BackgroundColor = box.BackgroundColor;
            BorderColor = box.BorderColor;
            BorderStyle = box.BorderStyle;
            CornerRadius = box.CornerRadius;
            BackgroundGradient = box.BackgroundGradient;
            BoxShadows = box.BoxShadows;
        }
    }

    public override void Render(IRenderContext ctx)
    {
        var box = Layout.AbsoluteBorderBox;

        // Shadows sit behind the box; draw them first so the background paints over
        // the portion beneath the box.
        if (BoxShadows is { Count: > 0 } shadows)
        {
            foreach (var shadow in shadows)
                ctx.DrawBoxShadow(box, CornerRadius, shadow);
        }

        var w = Box.BorderWidth;
        bool hasBorder = BorderColor.A > 0 && (w.Top > 0 || w.Right > 0 || w.Bottom > 0 || w.Left > 0);
        bool uniformWidth = w.Top == w.Right && w.Right == w.Bottom && w.Bottom == w.Left;
        // The simple case (uniform width, solid style) uses the combined draw call
        // and keeps its existing centered-stroke geometry, so nothing regresses.
        // Per-side widths or dashed/dotted styles route through DrawBorder instead.
        bool advancedBorder = hasBorder && (BorderStyle != BorderStyle.Solid || !uniformWidth);

        if (BackgroundGradient is { } gradient)
        {
            ctx.DrawGradientRect(box, CornerRadius, gradient);
            if (advancedBorder)
                ctx.DrawBorder(box, CornerRadius, BorderColor, w, BorderStyle);
            else if (hasBorder)
                ctx.DrawRoundedRect(box, CornerRadius, Color.Transparent, BorderColor, w);
        }
        else if (advancedBorder)
        {
            if (BackgroundColor.A > 0)
                ctx.DrawRoundedRect(box, CornerRadius, BackgroundColor, Color.Transparent, default);
            ctx.DrawBorder(box, CornerRadius, BorderColor, w, BorderStyle);
        }
        else if (BackgroundColor.A > 0 || hasBorder)
        {
            ctx.DrawRoundedRect(box, CornerRadius, BackgroundColor, hasBorder ? BorderColor : Color.Transparent, w);
        }
    }
}
