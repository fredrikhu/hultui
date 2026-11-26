using HultUI.Core.Events;
using HultUI.Core.Primitives;

namespace HultUI.Core.Elements;

/// <summary>
/// A surface a finger can scroll (docs/design/GESTURES.md §3). <see cref="ScrollViewElement"/>
/// (and the lists built on it, a <see cref="Builders.HorizontalPan"/> shelf included),
/// <see cref="TextAreaElement"/> and the rich text editor implement it; implement it on any other
/// surface that scrolls content it owns. A finger that lands on it, or inside it, and moves past
/// the touch slop along an axis it can still move in scrolls it 1:1, and a flick throws it.
/// </summary>
public interface IPanScrollable
{
    /// <summary>Whether the content can move by a positive (+1) or negative (-1) delta in this
    /// axis: false at that end of the content, or along an axis it does not scroll.</summary>
    bool CanPan(PanAxis axis, int direction);

    /// <summary>Scrolls by a delta (positive is further down and right through the content) and
    /// returns how much it actually moved. This is the user's scroll.</summary>
    Point PanBy(float dx, float dy);

    /// <summary>The offset, so a gesture can carry its position across a rebuild that replaced
    /// the surface under it.</summary>
    Point PanOffset { get; set; }
}
