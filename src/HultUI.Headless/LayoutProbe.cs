using System.Reflection;
using HultUI.Core.Elements;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;

namespace HultUI.Headless;

/// <summary>
/// Reading laid-out geometry for tests, with the distinctions that are easy to get wrong
/// spelled out rather than re-derived at each call site.
///
/// <para>
/// Every helper here exists because an assertion written the obvious way passed against
/// deliberately broken code. They are not conveniences; each one names a specific way a
/// layout test can measure something other than what it claims to.
/// </para>
///
/// <list type="bullet">
/// <item><b>Painted vs Inner.</b> A content box is inset by padding <i>by definition</i>, so
/// comparing a child against its parent's content box holds no matter how much padding there
/// is. Claims about the area a parent paints, or about a margin swallowing a click, have to
/// use <see cref="Painted"/>.</item>
/// <item><b>Element box vs drawn text.</b> A label's box is clamped by its container, so it
/// can never report overflowing it. Only the laid-out line boxes can — see
/// <see cref="DrawnText"/>. A row shorter than its own text looks fine through the box.</item>
/// <item><b>Rows are not a fixed number of parents up.</b> Wrapping, panning and staging each
/// insert or remove a wrapper, so <c>label.Parent.Parent</c> means different things in
/// different modes. <see cref="RowIn"/> finds the row by its relationship to the list.</item>
/// <item><b>Press in pixels, not fractions.</b> A dead strip along a row edge is a pixel or
/// two deep; 10% of a 22px row is well inside the content and proves nothing. And a press
/// within half a pixel of an edge can round into the neighbour, which makes a test a coin
/// toss rather than a statement — see <see cref="InsetFromTop"/>.</item>
/// </list>
/// </summary>
public static class LayoutProbe
{
    /// <summary>The box an element paints: background, border, and everything inside them.
    /// This is the one to compare against for claims about coverage, tinting or hit area.</summary>
    public static Rect Painted(Element element) => element.Layout.AbsoluteBorderBox;

    /// <summary>The box an element's children live in — inset by its padding. Comparing a
    /// child against this cannot detect padding, because padding is what defines it.</summary>
    public static Rect Inner(Element element) => element.Layout.AbsoluteContentBox;

    /// <summary>
    /// The text actually laid out in a label: how tall it is, how wide its widest line is,
    /// and how many lines it broke into. Read from the line boxes rather than the element,
    /// because the element's box is clamped by its container and so cannot show an overflow.
    /// <para>
    /// Only populated for selectable or linked labels — a <see cref="TextElement"/> keeps its
    /// lines when it may have to draw a selection over them or map a click through them.
    /// Reports zeroes otherwise, so a test that needs this must make its label selectable or
    /// assert something else.
    /// </para>
    /// </summary>
    public static (float Height, float Width, int LineCount) DrawnText(TextElement label)
    {
        var lines = DrawnLines(label);
        if (lines.Count == 0) return (0f, 0f, 0);

        float bottom = 0f, widest = 0f;
        foreach (var line in lines)
        {
            bottom = Math.Max(bottom, line.OffsetY + line.Height);
            widest = Math.Max(widest, line.Width);
        }
        return (bottom, widest, lines.Count);
    }

    /// <summary>The line boxes behind <see cref="DrawnText"/>: each line's text range, top,
    /// height and width, as the label last drew them. Empty under the same conditions.</summary>
    public static IReadOnlyList<TextLineBox> DrawnLines(TextElement label) =>
        label.DrawnParagraph?.Lines ?? [];

    /// <summary>
    /// The row <paramref name="descendant"/> belongs to: the ancestor that is a direct child
    /// of <paramref name="container"/>. Walking a fixed number of parents instead is the
    /// mistake this exists to prevent — the chain between a label and its row changes with
    /// the view's mode, so the same expression means different things in each.
    /// </summary>
    public static Element? RowIn(Element descendant, Element container)
    {
        for (var e = descendant; e is not null; e = e.Parent)
            if (ReferenceEquals(e.Parent, container)) return e;
        return null;
    }

    /// <summary>
    /// A point <paramref name="pixels"/> below a box's top edge, horizontally centred.
    /// <para>
    /// Give it at least a whole pixel. Closer than that to an edge and hit testing can round
    /// into the neighbouring element, which turns the test into a coin toss under load rather
    /// than a claim about this element. A dead strip worth catching is deeper than that
    /// anyway — the ones found so far ran about two pixels.
    /// </para>
    /// </summary>
    public static Point InsetFromTop(Rect box, float pixels = 1.5f) =>
        new(box.X + box.Width / 2, box.Y + pixels);

    /// <summary>A point <paramref name="pixels"/> above a box's bottom edge, horizontally
    /// centred. See <see cref="InsetFromTop"/> for why it is not half a pixel.</summary>
    public static Point InsetFromBottom(Rect box, float pixels = 1.5f) =>
        new(box.X + box.Width / 2, box.Bottom - pixels);

    /// <summary>The middle of a box — the case that works even when the edges do not.</summary>
    public static Point Middle(Rect box) =>
        new(box.X + box.Width / 2, box.Y + box.Height / 2);

    /// <summary>
    /// Converts a point in an element's own laid-out space into the screen coordinates a
    /// pointer event needs. Rows inside a scrolled container are laid out unscrolled — the
    /// renderer applies the offset — so a press at a row's laid-out Y lands wherever that row
    /// would be if nothing had scrolled.
    /// </summary>
    public static Point OnScreen(Point layoutPoint, ScrollViewElement scroller) =>
        new(layoutPoint.X - scroller.ScrollOffsetX, layoutPoint.Y - scroller.ScrollOffsetY);

    /// <summary>Every descendant of <paramref name="root"/> matching <paramref name="match"/>,
    /// in tree order.</summary>
    public static List<T> Descendants<T>(Element? root, Func<T, bool>? match = null) where T : Element
    {
        var found = new List<T>();
        Walk(root);
        return found;

        void Walk(Element? e)
        {
            if (e is null) return;
            if (e is T t && (match is null || match(t))) found.Add(t);
            foreach (var child in e.Children) Walk(child);
        }
    }
}
