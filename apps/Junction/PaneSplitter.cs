using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;

namespace Junction;

/// <summary>
/// The draggable border between a fixed-width pane and whatever fills the rest of the row.
/// Holds the pane's width as app state and rebuilds on every move, the way
/// <see cref="HorizontalPan"/> holds its offset — a width pushed straight onto an element
/// would be overwritten by the next build.
/// </summary>
/// <remarks>
/// The grab is remembered as the pointer's x at the press plus the width it started from,
/// rather than as "pointer x minus the pane's left edge". Those are the same figure only
/// while the pane's edge stays put, and it does not: a splitter dragged past its own minimum
/// stops moving while the pointer carries on, and a width derived from the live edge would
/// then jump the moment the pointer came back.
/// </remarks>
public sealed class PaneSplitter(Action onChanged, Action onCommitted, float min, float max, float initial)
{
    private readonly Action _onChanged = onChanged;
    private readonly Action _onCommitted = onCommitted;
    private float _width = Math.Clamp(initial, min, max);
    private float _grabX;
    private float _grabWidth;

    /// <summary>How wide the handle is to the pointer. Wider than the line it draws, because
    /// a 1px target is a game of skill rather than a control.</summary>
    private const float HandleWidth = 6f;

    /// <summary>Current width of the pane to the splitter's left, always within range.</summary>
    public float Width => Math.Clamp(_width, min, max);

    /// <summary>True while a drag is in progress — the handle paints itself lit.</summary>
    public bool IsDragging { get; private set; }

    /// <summary>Sets the width directly (restoring a remembered one). No callback: this is
    /// the owner telling the splitter, not the splitter telling the owner.</summary>
    public void Restore(float width) => _width = Math.Clamp(width, min, max);

    /// <summary>The handle itself. Place it between the pane and its neighbour.</summary>
    /// <remarks>
    /// It draws no line of its own. The pane to its left already borders that edge, and a
    /// second line three pixels along — centred in a 6px box, so it lands on a half-pixel and
    /// renders as a smear rather than a line — reads as a rendering fault next to the crisp
    /// one, not as a grip. What the handle contributes is the target and the feedback: a
    /// tint under the pointer that says it can be grabbed, and a stronger one while it is.
    /// </remarks>
    public Element Handle(Color hover, Color grabbed)
    {
        var handle = Box.New()
            .Width(HandleWidth).FlexShrink(0)
            .Cursor(CursorType.ResizeEW)
            .Tooltip("Drag to resize");
        if (IsDragging) handle.Background(grabbed);
        else handle.HoverBg(hover, Color.Transparent);
        handle.On<PointerDownEvent>(e =>
        {
            IsDragging = true;
            _grabX = e.Position.X;
            _grabWidth = Width;
            _onChanged();
        });
        return handle;
    }

    /// <summary>
    /// Hears the move and release on an ancestor, because a drag routinely ends with the
    /// pointer well off the handle — often outside the window's client area entirely.
    /// Passive so an idle window does not re-render on every pointer move over it.
    /// </summary>
    public void AttachDrag(Element root)
    {
        root.OnPassive<PointerMoveEvent>(e => { if (IsDragging) DragTo(e.Position.X); });
        root.OnPassive<PointerUpEvent>(_ =>
        {
            if (!IsDragging) return;
            IsDragging = false;
            _onChanged();
            // Remembered when the grab is let go rather than on every move, so one drag
            // writes the settings file once instead of sixty times a second.
            _onCommitted();
        });
    }

    /// <summary>Maps a pointer x onto the pane width. True when the width actually moved.</summary>
    public bool DragTo(float pointerX)
    {
        float next = Math.Clamp(_grabWidth + (pointerX - _grabX), min, max);
        if (Math.Abs(next - _width) < 0.5f) return false;
        _width = next;
        _onChanged();
        return true;
    }
}
