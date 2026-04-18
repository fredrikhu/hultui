using HultUI.Core.DevTools;
using HultUI.Core.Elements;
using HultUI.Core.Layout;

namespace HultUI.Core.Rendering;

// Children are rendered in ZIndex order (ascending): low ZIndex = back, high ZIndex = front.
// When no child has a non-zero ZIndex, the original insertion order is used (zero allocation).

public static class ElementRenderer
{
    public static void RenderTree(Element root, IRenderContext ctx, FrameTracer? tracer = null)
    {
        RenderElement(root, ctx, tracer);
    }

    private static void RenderElement(Element element, IRenderContext ctx, FrameTracer? tracer)
    {
        using var _trace = tracer is null
            ? default
            : tracer.Begin("Render", element.GetType().Name, element.Key ?? element.Name);


        bool hasOpacity = element.Opacity < 1f;
        if (hasOpacity)
            ctx.PushOpacity(element.Opacity);

        // Render self
        element.Render(ctx);

        // ScrollViewElement: clip + translate children, then draw scrollbars on top
        if (element is ScrollViewElement scrollView)
        {
            scrollView.UpdateContentSize();

            var clipRect = element.Layout.AbsoluteContentBox;
            ctx.PushClip(clipRect);
            ctx.PushTranslate(-scrollView.ScrollOffsetX, -scrollView.ScrollOffsetY);

            var scrollChildren = ZOrderHelper.GetChildrenInZOrder(element);
            for (int i = 0; i < scrollChildren.Count; i++)
                RenderElement(scrollChildren[i], ctx, tracer);

            ctx.Pop(); // translate
            ctx.Pop(); // clip

            scrollView.RenderScrollbars(ctx);

            if (hasOpacity)
                ctx.Pop();
            return;
        }

        // Clip to content box if overflow is hidden
        bool needsClip = element.Box.OverflowX == Overflow.Hidden || element.Box.OverflowY == Overflow.Hidden;
        if (needsClip)
        {
            var clipRect = element.Layout.AbsoluteContentBox;
            ctx.PushClip(clipRect);
        }

        // Render children in z-order
        var children = ZOrderHelper.GetChildrenInZOrder(element);
        for (int i = 0; i < children.Count; i++)
            RenderElement(children[i], ctx, tracer);

        if (needsClip)
            ctx.Pop();

        if (hasOpacity)
            ctx.Pop();
    }
}
