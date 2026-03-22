using HultUI.Core.Elements;
using HultUI.Core.Primitives;

namespace HultUI.Core.Events;

public static class HitTester
{
    /// <summary>
    /// Find the deepest element at the given point.
    /// Traverses depth-first in reverse child order (front-to-back).
    /// </summary>
    public static Element? HitTest(Element root, Point point)
    {
        return HitTestRecursive(root, point, clipRect: null);
    }

    private static Element? HitTestRecursive(Element element, Point point, Rect? clipRect)
    {
        // If we have a clip rect, check if the point is inside it
        if (clipRect.HasValue && !clipRect.Value.Contains(point))
            return null;

        // Check if the element itself contains the point
        if (!element.HitTest(point))
            return null;

        // Determine clip rect and point transform for children
        Rect? childClip = clipRect;
        Point childPoint = point;

        if (element is ScrollViewElement scrollView)
        {
            // ScrollView clips to content box and offsets hit test point.
            // Both point and clip rect must be transformed into content-space
            // so that child AbsoluteX/Y (which don't account for scroll) can be
            // compared correctly against the scroll-adjusted point.
            var contentBox = element.Layout.AbsoluteContentBox;
            var screenClip = clipRect.HasValue ? Intersect(clipRect.Value, contentBox) : contentBox;
            childClip = new Rect(
                screenClip.X + scrollView.ScrollOffsetX,
                screenClip.Y + scrollView.ScrollOffsetY,
                screenClip.Width,
                screenClip.Height);
            childPoint = new Point(
                point.X + scrollView.ScrollOffsetX,
                point.Y + scrollView.ScrollOffsetY);
        }
        else if (element.Box.OverflowX == Layout.Overflow.Hidden || element.Box.OverflowY == Layout.Overflow.Hidden)
        {
            var contentBox = element.Layout.AbsoluteContentBox;
            childClip = clipRect.HasValue ? Intersect(clipRect.Value, contentBox) : contentBox;
        }

        // Traverse children in reverse order (last child = front-most)
        for (int i = element.Children.Count - 1; i >= 0; i--)
        {
            var hit = HitTestRecursive(element.Children[i], childPoint, childClip);
            if (hit is not null)
                return hit;
        }

        // No child was hit, this element is the target
        return element;
    }

    private static Rect Intersect(Rect a, Rect b)
    {
        float x = Math.Max(a.X, b.X);
        float y = Math.Max(a.Y, b.Y);
        float right = Math.Min(a.X + a.Width, b.X + b.Width);
        float bottom = Math.Min(a.Y + a.Height, b.Y + b.Height);
        float w = Math.Max(0, right - x);
        float h = Math.Max(0, bottom - y);
        return new Rect(x, y, w, h);
    }
}
