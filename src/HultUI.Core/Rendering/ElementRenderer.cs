using HultUI.Core.Elements;
using HultUI.Core.Layout;

namespace HultUI.Core.Rendering;

public static class ElementRenderer
{
    public static void RenderTree(Element root, IRenderContext ctx)
    {
        RenderElement(root, ctx);
    }

    private static void RenderElement(Element element, IRenderContext ctx)
    {
        // Render self
        element.Render(ctx);

        // Clip to content box if overflow is hidden
        bool needsClip = element.Box.OverflowX == Overflow.Hidden || element.Box.OverflowY == Overflow.Hidden;
        if (needsClip)
        {
            var clipRect = element.Layout.AbsoluteContentBox;
            ctx.PushClip(clipRect);
        }

        // Render children in order (absolute children on top handled by z-index later)
        foreach (var child in element.Children)
            RenderElement(child, ctx);

        if (needsClip)
            ctx.Pop();
    }
}
