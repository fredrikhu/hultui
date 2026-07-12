using HultUI.Core.DevTools;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;

namespace HultUI.Core.Rendering;

// Children are rendered in ZIndex order (ascending): low ZIndex = back, high ZIndex = front.
// When no child has a non-zero ZIndex, the original insertion order is used (zero allocation).

public static class ElementRenderer
{
    public static void RenderTree(Element root, IRenderContext ctx, FrameTracer? tracer = null)
    {
        // No clip region above the root, so nothing is culled at the top level
        // (top-level content may legitimately draw outside element bounds, e.g.
        // shadows). Culling switches on only once we descend into a clip.
        RenderElement(root, ctx, tracer, offsetX: 0, offsetY: 0, cull: null);
    }

    // <paramref name="offsetX"/>/<paramref name="offsetY"/> are the accumulated
    // render translation (from scroll views) so an element's on-screen rect is
    // its AbsoluteBorderBox shifted by this offset. <paramref name="cull"/>, when
    // set, is the visible region in screen space: an element whose on-screen rect
    // falls entirely outside it is skipped along with its whole subtree. It is
    // only ever set inside a clip (scroll view / overflow-hidden), where anything
    // outside is clipped away regardless — so for normal-flow content (a box
    // bounds its children) skipping is safe.
    private static void RenderElement(Element element, IRenderContext ctx, FrameTracer? tracer,
        float offsetX, float offsetY, Rect? cull)
    {
        if (cull is Rect clip &&
            !element.Layout.AbsoluteBorderBox.Translate(offsetX, offsetY).IntersectsWith(clip))
            return; // fully outside the visible clip region — skip the subtree

        using var _trace = tracer is null
            ? default
            : tracer.Begin("Render", element.GetType().Name, element.Key ?? element.Name);


        bool hasOpacity = element.Opacity < 1f;
        if (hasOpacity)
            ctx.PushOpacity(element.Opacity);

        // A transform rotates/scales the element and its subtree about its centre.
        // Once transformed, the screen-axis-aligned cull rect no longer bounds the
        // content, so culling is disabled for the subtree.
        bool hasTransform = element.Transform is { } tf && !tf.IsIdentity;
        if (hasTransform)
        {
            var b = element.Layout.AbsoluteBorderBox;
            ctx.PushTransform(element.Transform!.Value.ToMatrix(b.X + b.Width * 0.5f, b.Y + b.Height * 0.5f));
            cull = null;
        }

        // Render self
        element.Render(ctx);

        // ScrollViewElement: clip + translate children, then draw scrollbars on top
        if (element is ScrollViewElement scrollView)
        {
            scrollView.UpdateContentSize();

            var clipRect = element.Layout.AbsoluteContentBox;
            ctx.PushClip(clipRect);
            ctx.PushTranslate(-scrollView.ScrollOffsetX, -scrollView.ScrollOffsetY);

            // The viewport in screen space (the clip, shifted by the transform in
            // effect when it was pushed). Children are drawn under an extra scroll
            // translate, so they carry the reduced offset below.
            var viewport = clipRect.Translate(offsetX, offsetY);
            Rect childCull = cull is Rect outer ? outer.Intersect(viewport) : viewport;
            float childOffsetX = offsetX - scrollView.ScrollOffsetX;
            float childOffsetY = offsetY - scrollView.ScrollOffsetY;

            var scrollChildren = ZOrderHelper.GetChildrenInZOrder(element);
            for (int i = 0; i < scrollChildren.Count; i++)
                RenderElement(scrollChildren[i], ctx, tracer, childOffsetX, childOffsetY, childCull);

            ctx.Pop(); // translate
            ctx.Pop(); // clip

            scrollView.RenderScrollbars(ctx);

            if (hasTransform)
                ctx.Pop();
            if (hasOpacity)
                ctx.Pop();
            return;
        }

        // Clip to content box if overflow is hidden
        bool needsClip = element.Box.OverflowX == Overflow.Hidden || element.Box.OverflowY == Overflow.Hidden;
        Rect? childrenCull = cull;
        if (needsClip)
        {
            var clipRect = element.Layout.AbsoluteContentBox;
            ctx.PushClip(clipRect);
            var clipScreen = clipRect.Translate(offsetX, offsetY);
            childrenCull = cull is Rect outer ? outer.Intersect(clipScreen) : clipScreen;
        }

        // Render children in z-order
        var children = ZOrderHelper.GetChildrenInZOrder(element);
        for (int i = 0; i < children.Count; i++)
            RenderElement(children[i], ctx, tracer, offsetX, offsetY, childrenCull);

        if (needsClip)
            ctx.Pop();

        if (hasTransform)
            ctx.Pop();

        if (hasOpacity)
            ctx.Pop();
    }
}
