using HultUI.Core.Primitives;
using HultUI.Core.Rendering;

namespace HultUI.Core.Elements;

public class BoxElement : Element
{
    public Color BackgroundColor { get; set; } = Color.Transparent;
    public Color BorderColor { get; set; } = Color.Transparent;
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

        if (BackgroundGradient is { } gradient)
        {
            ctx.DrawGradientRect(box, CornerRadius, gradient);
            // Border is drawn as a separate stroke over the gradient fill.
            if (BorderColor.A > 0)
                ctx.DrawRoundedRect(box, CornerRadius, Color.Transparent, BorderColor, Box.BorderWidth);
        }
        else if (BackgroundColor.A > 0 || BorderColor.A > 0)
        {
            ctx.DrawRoundedRect(box, CornerRadius, BackgroundColor, BorderColor, Box.BorderWidth);
        }
    }
}
