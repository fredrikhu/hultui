using System.Numerics;
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
        // A sticky element is drawn moved by its shift, subtree and all; culled where it is drawn.
        if (element.PositionRead.Type == PositionType.Sticky && element.StickyShift is { } stick
            && (stick.X != 0 || stick.Y != 0))
        {
            ctx.PushTranslate(stick.X, stick.Y);
            RenderInPlace(element, ctx, tracer, offsetX + stick.X, offsetY + stick.Y, cull);
            ctx.Pop();
            return;
        }
        RenderInPlace(element, ctx, tracer, offsetX, offsetY, cull);
    }

    private static void RenderInPlace(Element element, IRenderContext ctx, FrameTracer? tracer,
        float offsetX, float offsetY, Rect? cull)
    {
        if (cull is Rect clip &&
            !element.Layout.AbsoluteBorderBox.Translate(offsetX, offsetY).IntersectsWith(clip))
            return; // fully outside the visible clip region — skip the subtree

        using var _trace = tracer is null
            ? default
            : tracer.Begin("Render", element.GetType().Name, element.Key ?? element.Name);

        // A transform rotates/scales the element and its subtree about its centre.
        // Once transformed, the screen-axis-aligned cull rect no longer bounds the
        // content, so culling is disabled for the subtree.
        bool hasTransform = element.Transform is { } tf && !tf.IsIdentity;
        if (hasTransform)
        {
            ctx.PushTransform(TransformMatrix(element));
            cull = null;
        }

        // Opacity blends the subtree as one group, drawn offscreen and composited once. The
        // group is sized to what the subtree paints — unbounded, it is the size of the clip,
        // the whole window or viewport for every faded element. It is pushed inside the
        // transform, so its bounds are in the element's own coordinates and the surface maps
        // them through the transform. A lone shape needs no group at all: it fades itself.
        float opacity = element.RenderOpacity;
        bool hasOpacity = false;
        if (opacity < 1f)
        {
            if (element.Children.Count == 0 && element.RenderWithOpacity(ctx, opacity))
            {
                if (hasTransform)
                    ctx.Pop();
                return;
            }
            if (SubtreePaintBounds(element, ctx, offsetX, offsetY, cull) is Rect bounds)
                ctx.PushOpacity(opacity, bounds);
            else
                ctx.PushOpacity(opacity);
            hasOpacity = true;
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

            if (hasOpacity)
                ctx.Pop();
            if (hasTransform)
                ctx.Pop();
            return;
        }

        // Clip to content box if overflow is hidden
        bool needsClip = element.BoxRead.OverflowX == Overflow.Hidden || element.BoxRead.OverflowY == Overflow.Hidden;
        Rect? childrenCull = cull;
        if (needsClip)
        {
            var clipRect = element.Layout.AbsoluteContentBox;
            // A rounded box clips round, the way `overflow: hidden` does with a
            // border-radius: otherwise an image inside a circular avatar is still a
            // square, and the corners of a rounded card square off wherever a child
            // paints over them.
            var radius = element is BoxElement rounded ? rounded.CornerRadius : CornerRadius.Zero;
            if (radius.IsZero) ctx.PushClip(clipRect);
            else ctx.PushRoundedClip(clipRect, radius);
            var clipScreen = clipRect.Translate(offsetX, offsetY);
            childrenCull = cull is Rect outer ? outer.Intersect(clipScreen) : clipScreen;
        }

        // Render children in z-order
        var children = ZOrderHelper.GetChildrenInZOrder(element);
        for (int i = 0; i < children.Count; i++)
            RenderElement(children[i], ctx, tracer, offsetX, offsetY, childrenCull);

        if (needsClip)
            ctx.Pop();

        if (hasOpacity)
            ctx.Pop();

        if (hasTransform)
            ctx.Pop();
    }

    private static Matrix3x2 TransformMatrix(Element element)
    {
        var b = element.Layout.AbsoluteBorderBox;
        return element.Transform!.Value.ToMatrix(b.X + b.Width * 0.5f, b.Y + b.Height * 0.5f);
    }

    // What `element` and the part of its subtree RenderInPlace would draw paint inside, in the
    // element's own coordinates (inside its transform); null when an element in it cannot say
    // (Element.PaintBounds). The walk follows the render's: the same cull, the same offsets.
    private static Rect? SubtreePaintBounds(Element element, IRenderContext ctx,
        float offsetX, float offsetY, Rect? cull)
    {
        if (element.PaintBounds(ctx) is not Rect bounds)
            return null;

        // A clipping element cuts its children to its content box, whatever they paint.
        if (element is ScrollViewElement
            || element.BoxRead.OverflowX == Overflow.Hidden || element.BoxRead.OverflowY == Overflow.Hidden)
            return bounds.Union(element.Layout.AbsoluteContentBox);

        var children = element.Children;
        for (int i = 0; i < children.Count; i++)
        {
            var child = children[i];
            float childOffsetX = offsetX, childOffsetY = offsetY;
            Point shift = default;
            if (child.PositionRead.Type == PositionType.Sticky)
            {
                shift = child.StickyShift;
                childOffsetX += shift.X;
                childOffsetY += shift.Y;
            }
            if (cull is Rect clip &&
                !child.Layout.AbsoluteBorderBox.Translate(childOffsetX, childOffsetY).IntersectsWith(clip))
                continue; // culled: RenderInPlace skips it, subtree and all

            bool transformed = child.Transform is { } tf && !tf.IsIdentity;
            if (SubtreePaintBounds(child, ctx, childOffsetX, childOffsetY, transformed ? null : cull) is not Rect sub)
                return null;
            if (transformed)
                sub = Transformed(sub, TransformMatrix(child));
            bounds = bounds.Union(sub.Translate(shift.X, shift.Y));
        }
        return bounds;
    }

    /// <summary>The axis-aligned box around <paramref name="r"/> mapped through <paramref name="m"/>.</summary>
    private static Rect Transformed(Rect r, Matrix3x2 m)
    {
        var a = Vector2.Transform(new Vector2(r.X, r.Y), m);
        var b = Vector2.Transform(new Vector2(r.Right, r.Y), m);
        var c = Vector2.Transform(new Vector2(r.X, r.Bottom), m);
        var d = Vector2.Transform(new Vector2(r.Right, r.Bottom), m);
        float minX = MathF.Min(MathF.Min(a.X, b.X), MathF.Min(c.X, d.X));
        float minY = MathF.Min(MathF.Min(a.Y, b.Y), MathF.Min(c.Y, d.Y));
        float maxX = MathF.Max(MathF.Max(a.X, b.X), MathF.Max(c.X, d.X));
        float maxY = MathF.Max(MathF.Max(a.Y, b.Y), MathF.Max(c.Y, d.Y));
        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }
}
