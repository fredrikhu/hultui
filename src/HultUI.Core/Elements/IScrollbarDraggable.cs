using HultUI.Core.Primitives;

namespace HultUI.Core.Elements;

/// <summary>
/// An element whose scrollbar thumb can be grabbed and dragged with the pointer.
/// The window routes pointer-down/move/up to the implementer: a down that lands on
/// the scrollbar is consumed (starting a thumb drag or a track jump), and while
/// <see cref="IsDraggingScrollbar"/> is set the window forwards moves and the release.
/// Implemented by <see cref="ScrollViewElement"/> and <see cref="TextAreaElement"/>.
/// </summary>
public interface IScrollbarDraggable
{
    /// <summary>True while a thumb drag is in progress (set by TryStartScrollbarDrag).</summary>
    bool IsDraggingScrollbar { get; }

    /// <summary>Handle a pointer-down: returns true if it hit the scrollbar (consumed).
    /// Starts a thumb drag when the thumb was hit, or jumps on a track click.</summary>
    bool TryStartScrollbarDrag(Point position);

    /// <summary>Update scroll from the pointer while a thumb drag is active.</summary>
    void UpdateScrollbarDrag(Point position);

    /// <summary>End a thumb drag.</summary>
    void EndScrollbarDrag();
}
