using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Primitives;

namespace HultUI.Core.Builders;

/// <summary>
/// The draggable border between a fixed-size pane and whatever fills the rest of the row
/// (or column). Holds the pane's size as app state and reports it as it moves, the way
/// <see cref="HorizontalPan"/> holds its offset — a size pushed straight onto an element
/// would be overwritten by the next build. Build the pane at <see cref="Size"/>, put
/// <see cref="Handle()"/> beside it, and call <see cref="AttachDrag"/> once on an ancestor.
/// </summary>
/// <remarks>
/// The grab is remembered as the pointer's position at the press plus the size it
/// started from, rather than as "pointer minus the pane's edge". Those are the same
/// figure only while the pane's edge stays put, and it does not: a splitter dragged past
/// its own minimum stops moving while the pointer carries on, and a size derived from
/// the live edge would then jump the moment the pointer came back.
/// </remarks>
public sealed class PaneSplitter
{
    private readonly Action _onChanged;
    private readonly Action? _onCommitted;
    private readonly float _min;
    private readonly float _max;
    private float _size;
    private float _grabAt;
    private float _grabSize;
    private bool _dragMoved;
    // Made once, not per AttachDrag (owners call it on every build).
    private Func<bool>? _listening;
    // Where the pointer is along the axis, applied once per frame while dragging.
    private float _pointer;
    private PointerFollow? _follow;

    /// <param name="onChanged">Runs whenever the size moves — normally the owner's
    /// <c>MarkDirty</c>, since the pane is rebuilt at the new size. A drag runs it at most
    /// once per frame that may be drawn, at the pointer's latest position, however many
    /// moves came in between (a 1,000 Hz mouse sends about sixteen a frame), and the release
    /// applies the last position before the drag ends.</param>
    /// <param name="onCommitted">Runs once when a drag is let go or a key is released,
    /// rather than on every move: where the owner remembers the size.</param>
    /// <param name="min">Smallest the pane may be.</param>
    /// <param name="max">Largest the pane may be.</param>
    /// <param name="initial">Starting size, clamped.</param>
    /// <param name="axis">Side by side (the pane's width) or stacked (its height).</param>
    public PaneSplitter(Action onChanged, Action? onCommitted, float min, float max, float initial,
        SplitterAxis axis = SplitterAxis.Horizontal)
    {
        _onChanged = onChanged;
        _onCommitted = onCommitted;
        _min = min;
        _max = max;
        _size = Math.Clamp(initial, min, max);
        Axis = axis;
    }

    public SplitterAxis Axis { get; }

    /// <summary>Current size of the pane before the splitter (its width side by side, its
    /// height stacked), always within range.</summary>
    public float Size => Math.Clamp(_size, _min, _max);

    /// <summary>True while a drag is in progress — the handle paints itself lit.</summary>
    public bool IsDragging { get; private set; }

    /// <summary>Sets the size directly (restoring a remembered one). No callback: this is
    /// the owner telling the splitter, not the splitter telling the owner.</summary>
    public void Restore(float size) => _size = Math.Clamp(size, _min, _max);

    /// <summary>The handle, themed. Place it between the pane and its neighbour.</summary>
    public SplitterHandleElement Handle() => Wire(new SplitterHandleElement(Axis));

    /// <summary>The handle with explicit tints, for a palette the theme does not describe.</summary>
    public SplitterHandleElement Handle(Color hover, Color grabbed)
    {
        var handle = Handle();
        handle.HoverColor = hover;
        handle.GrabbedColor = grabbed;
        return handle;
    }

    private SplitterHandleElement Wire(SplitterHandleElement handle)
    {
        handle.IsGrabbed = IsDragging;
        handle.OnOwn<PointerDownEvent>(e =>
        {
            if (e.Button != 0) return;
            IsDragging = true;
            _grabAt = Axis == SplitterAxis.Horizontal ? e.Position.X : e.Position.Y;
            _grabSize = Size;
            handle.IsGrabbed = true;
            _onChanged();
        });
        // A key acts after the moves before it, in order.
        handle.OnKeyStep = delta =>
        {
            _follow?.Flush();
            if (Resize(Size + delta)) _onCommitted?.Invoke();
        };
        handle.OnKeyEnd = toEnd =>
        {
            _follow?.Flush();
            if (Resize(toEnd ? _max : _min)) _onCommitted?.Invoke();
        };
        return handle;
    }

    /// <summary>
    /// Hears the move and release on an ancestor, because a drag routinely ends with the
    /// pointer well off the handle — often outside the window's client area entirely.
    /// Passive so an idle window does not re-render on every pointer move over it, and heard
    /// only while dragging, so a move over the window makes no move event for it.
    /// </summary>
    public void AttachDrag(Element root)
    {
        root.OnPassive<PointerMoveEvent>(e =>
        {
            if (!IsDragging) return;
            // Recorded, and applied once per frame that may be drawn: onChanged rebuilds the
            // owner, and a 1,000 Hz mouse rebuilt it about sixteen times for every frame drawn.
            _pointer = Axis == SplitterAxis.Horizontal ? e.Position.X : e.Position.Y;
            (_follow ??= new PointerFollow(() => DragTo(_pointer))).Request(e.CurrentTarget);
        }, _listening ??= () => IsDragging);
        root.OnPassive<PointerUpEvent>(_ =>
        {
            if (!IsDragging) return;
            // The drag ends exactly where the pointer did, whether or not a frame came between.
            _follow?.Flush();
            IsDragging = false;
            _onChanged();
            // Remembered when the grab is let go rather than on every move, so one drag
            // writes the settings file once instead of sixty times a second — and not at
            // all for a press that only focused the handle.
            if (_dragMoved) _onCommitted?.Invoke();
            _dragMoved = false;
        });
    }

    /// <summary>Maps a pointer position along the axis onto the pane size. True when the
    /// size actually moved.</summary>
    public bool DragTo(float pointer)
    {
        bool moved = Resize(_grabSize + (pointer - _grabAt));
        _dragMoved |= moved;
        return moved;
    }

    private bool Resize(float next)
    {
        next = Math.Clamp(next, _min, _max);
        if (Math.Abs(next - _size) < 0.5f) return false;
        _size = next;
        _onChanged();
        return true;
    }
}
