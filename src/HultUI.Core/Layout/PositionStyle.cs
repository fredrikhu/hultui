using HultUI.Core.Elements;
using HultUI.Core.Primitives;

namespace HultUI.Core.Layout;

public sealed class PositionStyle
{
    /// <summary>Shared all-defaults instance for elements that never set position
    /// properties. Read-only by contract — never mutate.</summary>
    internal static readonly PositionStyle Default = new();

    public PositionStyle() { }

    /// <summary>The style of <paramref name="owner"/>, whose parent is told when
    /// <see cref="Type"/> or <see cref="ZIndex"/> changes.</summary>
    internal PositionStyle(Element owner) => _owner = owner;

    private readonly Element? _owner;
    private PositionType _type = PositionType.Relative;
    private int _zIndex;

    /// <summary>How the element is placed. A change tells the parent, whose children are drawn
    /// in another order when one turns sticky or stops being sticky; the element's layout
    /// still needs <see cref="Element.MarkLayoutDirty"/>.</summary>
    public PositionType Type
    {
        get => _type;
        set
        {
            if (_type == value) return;
            _type = value;
            _owner?.Parent?.ChildZOrderChanged();
        }
    }

    public Dimension Top { get; set; } = Dimension.None;
    public Dimension Right { get; set; } = Dimension.None;
    public Dimension Bottom { get; set; } = Dimension.None;
    public Dimension Left { get; set; } = Dimension.None;

    /// <summary>Where the element is stacked among its siblings: higher is drawn later, over
    /// the others, and hit first. A change tells the parent, which keeps its children's order
    /// between frames and sorts them again on the next paint or hit test, so
    /// <see cref="Element.MarkPaintDirty"/> is all it needs.</summary>
    public int ZIndex
    {
        get => _zIndex;
        set
        {
            if (_zIndex == value) return;
            _zIndex = value;
            _owner?.Parent?.ChildZOrderChanged();
        }
    }

    /// <returns>Whether anything changed: a patch that changes nothing in a layout style
    /// leaves the element's layout standing (Element.PatchFrom).</returns>
    internal bool CopyFrom(PositionStyle source)
    {
        if (Type == source.Type
            && Top == source.Top
            && Right == source.Right
            && Bottom == source.Bottom
            && Left == source.Left
            && ZIndex == source.ZIndex)
            return false;

        Type = source.Type;
        Top = source.Top;
        Right = source.Right;
        Bottom = source.Bottom;
        Left = source.Left;
        ZIndex = source.ZIndex;
        return true;
    }
}
