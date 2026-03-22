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

        // ScrollViewElement: clip + translate children, then draw scrollbars on top
        if (element is ScrollViewElement scrollView)
        {
            scrollView.UpdateContentSize();

            var clipRect = element.Layout.AbsoluteContentBox;
            ctx.PushClip(clipRect);
            ctx.PushTranslate(-scrollView.ScrollOffsetX, -scrollView.ScrollOffsetY);

            foreach (var child in element.Children)
                RenderElement(child, ctx);

            ctx.Pop(); // translate
            ctx.Pop(); // clip

            scrollView.RenderScrollbars(ctx);
            return;
        }

        // Clip to content box if overflow is hidden
        bool needsClip = element.Box.OverflowX == Overflow.Hidden || element.Box.OverflowY == Overflow.Hidden;
        if (needsClip)
        {
            var clipRect = element.Layout.AbsoluteContentBox;
            ctx.PushClip(clipRect);
        }

        // Render children in order
        foreach (var child in element.Children)
            RenderElement(child, ctx);

        if (needsClip)
            ctx.Pop();
    }
}
