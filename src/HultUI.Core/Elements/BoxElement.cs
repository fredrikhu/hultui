using HultUI.Core.Primitives;
using HultUI.Core.Rendering;

namespace HultUI.Core.Elements;

public class BoxElement : Element
{
    public Color BackgroundColor { get; set; } = Color.Transparent;
    public Color BorderColor { get; set; } = Color.Transparent;
    public CornerRadius CornerRadius { get; set; }

    public static BoxElement New() => new();

    internal override void PatchFrom(Element source)
    {
        base.PatchFrom(source);
        if (source is BoxElement box)
        {
            BackgroundColor = box.BackgroundColor;
            BorderColor = box.BorderColor;
            CornerRadius = box.CornerRadius;
        }
    }

    public override void Render(IRenderContext ctx)
    {
        var box = Layout.AbsoluteBorderBox;

        if (BackgroundColor.A > 0 || BorderColor.A > 0)
        {
            ctx.DrawRoundedRect(box, CornerRadius, BackgroundColor, BorderColor, Box.BorderWidth);
        }
    }
}
