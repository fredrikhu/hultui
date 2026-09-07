using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;

namespace HultUI.Core.Builders;

/// <summary>
/// Moves content sideways within a box too narrow for it, for surfaces that cannot use a
/// horizontal <see cref="ScrollViewElement"/>.
///
/// <para>Two situations rule the ScrollView out. Inside a
/// <see cref="VirtualListElement{T}"/> its content width is pinned to the viewport, so its
/// own <c>ScrollOffsetX</c> can never leave zero. And nesting one inside a vertical
/// ScrollView costs that outer view its wheel, because <c>OnScroll</c> stops propagation
/// whether or not it could act on the event.</para>
///
/// <para>So the offset lives here instead, and is applied as a negative left margin on
/// content inside a cell that clips. The margin moves the content left and — because the
/// cross-axis stretch that sizes it subtracts the horizontal margin — widens it by the same
/// amount, so what arrives from the right is genuinely laid out rather than leaving a hole.
/// Two things fall out of that which a real scroll view would not give you: columns beside
/// the panned cell (line numbers, gutters, checkboxes) stay put, and several cells can share
/// one offset so they move in step.</para>
///
/// <para>The panned content is rebuilt at the current offset, so <see cref="Offset"/> is
/// app state: every mutator reports whether it changed anything, and the
/// <c>onChanged</c> callback given to the constructor is where the owner marks itself
/// dirty.</para>
///
/// <para>Hand it to the list it pans with <c>ScrollSidewaysWith</c>, and the middle-click
/// autoscroll moves it too: the gesture otherwise drives the list's own horizontal offset,
/// which under a virtualized list has nowhere to go.</para>
/// </summary>
public sealed class HorizontalPan : ISidewaysScroller
{
    private readonly Action _onChanged;
    private readonly Ref<BoxElement> _trackRef = new();
    private float _offset;
    private float _grab;

    /// <param name="onChanged">Runs whenever the offset actually moves — normally the
    /// owner's <c>MarkDirty</c>, since the content has to be rebuilt to show it.</param>
    public HorizontalPan(Action onChanged) => _onChanged = onChanged;

    /// <summary>Width of the widest content to be panned. The owner measures and assigns
    /// this; <see cref="MeasureWidest"/> covers the monospace case.</summary>
    public float ContentWidth { get; set; }

    /// <summary>Width of the box the content shows through — the panned cell, not the whole
    /// surface. The owner assigns it, because only the owner knows which of its columns are
    /// fixed and whether the space is split between several cells.</summary>
    public float ViewportWidth { get; set; }

    /// <summary>How far right the content can go before its end reaches the viewport's.</summary>
    public float MaxOffset => Math.Max(0, ContentWidth - ViewportWidth);

    /// <summary>Whether there is anything to pan — what to gate a scrollbar on.</summary>
    public bool CanPan => MaxOffset > 0;

    /// <summary>Current offset, always within <c>[0, MaxOffset]</c>.</summary>
    public float Offset => Math.Min(_offset, MaxOffset);

    /// <summary>Height the scrollbar occupies, for owners that need to budget the space.</summary>
    public const float ScrollbarHeight = 10f;

    private const float ThumbMinWidth = 30f;

    /// <summary>One press of a left/right key. About four monospace characters — enough to
    /// line something up, coarse enough to cross a long line without wearing the key out.</summary>
    public const float KeyStep = 28f;

    /// <summary>Moves by <paramref name="delta"/>, clamped. True when the offset changed.</summary>
    public bool PanBy(float delta) => PanTo(_offset + delta);

    /// <summary>Moves to <paramref name="offset"/>, clamped. Pass <see cref="float.MaxValue"/>
    /// for "as far right as it goes". True when the offset changed.</summary>
    public bool PanTo(float offset)
    {
        float next = Math.Clamp(offset, 0, MaxOffset);
        if (next == _offset) return false;
        _offset = next;
        _onChanged();
        return true;
    }

    /// <summary>Back to the left margin — call when the content changes underneath.</summary>
    public void Reset() => _offset = 0;

    // ISidewaysScroller: the autoscroll's sideways half, by way of the list this pans under.
    bool ISidewaysScroller.CanScrollSideways => CanPan;
    bool ISidewaysScroller.ScrollSidewaysBy(float dx) => PanBy(dx);

    /// <summary>
    /// Wraps <paramref name="content"/> in a cell that clips, shifted by the current offset.
    /// The cell grows to fill the space its parent gives it.
    /// </summary>
    public Element Cell(Element content)
    {
        if (Offset > 0) content.Margin(new Thickness(0, 0, 0, -Offset));
        return Box.New().FlexGrow(1).FlexShrink(1).OverflowX(Overflow.Hidden).Children(content);
    }

    /// <summary>
    /// A track and thumb reflecting the current position, draggable and clickable. Press the
    /// thumb to grab it; press the track and it jumps under the pointer.
    ///
    /// <para>Build it only when <see cref="CanPan"/>, and call <see cref="AttachDrag"/> once
    /// on an ancestor — a drag routinely ends with the pointer off the thumb, so the move and
    /// release have to be heard higher up.</para>
    /// </summary>
    public Element Scrollbar(Color track, Color thumb, Color border)
    {
        // Sized and positioned in percentages of the track, so the layout engine resolves
        // both against its real width. Working in pixels would mean knowing that width while
        // building, which is a frame too early — the bar would be wrong until something
        // rebuilt it.
        float visible = ContentWidth > 0 ? Math.Min(1f, ViewportWidth / ContentWidth) : 1f;
        float travelled = MaxOffset > 0 ? Offset / MaxOffset : 0;

        var bar = Flex.Row(gap: 0).Height(ScrollbarHeight).FlexShrink(0)
            .AlignItems(AlignItems.Center)
            .Background(track)
            .BorderColor(border).BorderWidth(new Thickness(1, 0, 0, 0))
            .WithRef(_trackRef);

        // The thumb takes its share of the track; two growing spacers split what is left in
        // the ratio of how far along it is. Flex-grow rather than a second percentage
        // because the thumb has a minimum width — once that bites, the leftover is no longer
        // the percentage arithmetic says it is, and only the layout knows the real figure.
        bar.AddChild(Box.New().FlexGrow(travelled).FlexBasis(Dimension.Px(0)));
        bar.AddChild(Box.New()
            .Height(ScrollbarHeight - 4)
            .Width(Dimension.Percent(visible * 100)).MinWidth(ThumbMinWidth).FlexShrink(0)
            .CornerRadius(3f).Background(thumb).Cursor(CursorType.Pointer));
        bar.AddChild(Box.New().FlexGrow(1 - travelled).FlexBasis(Dimension.Px(0)));

        bar.On<PointerDownEvent>(ev =>
        {
            var (tx, w, th) = Geometry();
            float local = ev.Position.X - tx;
            float thumbAt = MaxOffset > 0 ? (w - th) * (Offset / MaxOffset) : 0;
            _grab = local >= thumbAt && local <= thumbAt + th ? local - thumbAt : th / 2;
            IsDragging = true;
            DragTo(ev.Position.X);
            ev.StopPropagation = true;
        });
        return bar;
    }

    /// <summary>True while a scrollbar drag is in progress.</summary>
    public bool IsDragging { get; private set; }

    /// <summary>The scrollbar from the most recent <see cref="Scrollbar"/>, once
    /// reconciliation has pointed at the live one. Null when none has been built, and
    /// detached (<c>Parent</c> null) when the last build left it out.</summary>
    public Element? ScrollbarElement => _trackRef.Current;

    /// <summary>Wires the pointer move and release a scrollbar drag needs onto an ancestor —
    /// the root of the surface is the usual choice. Safe to call on every build.</summary>
    public void AttachDrag(Element root)
    {
        // Passive: these run on every pointer move over the whole surface and do
        // nothing outside a drag; when they do act, DragTo marks dirty through
        // onChanged. Registered as ordinary app handlers they would force a frame
        // per move over an idle window (the input safety net counts handlers that
        // ran), which is exactly the idle-rerender bug this wording exists to stop.
        root.OnPassive<PointerMoveEvent>(ev => { if (IsDragging) DragTo(ev.Position.X); });
        root.OnPassive<PointerUpEvent>(_ => IsDragging = false);
    }

    /// <summary>Maps a pointer x onto the offset, for a scrollbar drag.</summary>
    public void DragTo(float pointerX)
    {
        var (x, width, thumb) = Geometry();
        float travel = width - thumb;
        if (travel <= 0) return;
        PanTo(Math.Clamp(pointerX - x - _grab, 0, travel) / travel * MaxOffset);
    }

    /// <summary>
    /// Routes a wheel event: a trackpad's sideways swipe arrives as <c>DeltaX</c>, a mouse's
    /// as Shift plus <c>DeltaY</c>. True when it was used for panning.
    ///
    /// <para>Attach this to the scrolling element itself. Its own handler sees the event
    /// first and clamps it to nothing, having no horizontal range, then leaves it alone.</para>
    /// </summary>
    public bool HandleScroll(ScrollEvent e)
    {
        float dx = e.DeltaX != 0 ? e.DeltaX
            : e.Modifiers.HasFlag(KeyModifiers.Shift) ? e.DeltaY : 0;
        return dx != 0 && PanBy(-dx * KeyStep);
    }

    /// <summary>
    /// Handles ← and → by <see cref="KeyStep"/>, and Ctrl (or Cmd) with either to jump to a
    /// margin. True when the key was one of those, whether or not the view could move — the
    /// caller should stop propagation either way, or the key means two things at once.
    /// </summary>
    public bool HandleKey(KeyDownEvent e)
    {
        bool ctrl = e.Modifiers.HasFlag(KeyModifiers.Ctrl) || e.Modifiers.HasFlag(KeyModifiers.Super);
        switch (e.Key)
        {
            case KeyCode.Left when ctrl: PanTo(0); return true;
            case KeyCode.Right when ctrl: PanTo(float.MaxValue); return true;
            case KeyCode.Left: PanBy(-KeyStep); return true;
            case KeyCode.Right: PanBy(KeyStep); return true;
            default: return false;
        }
    }

    /// <summary>
    /// Width of the widest of <paramref name="lines"/> in a <b>monospace</b> font, for
    /// <see cref="ContentWidth"/>.
    ///
    /// <para>Monospace is what makes this cheap and is a real precondition: the line with the
    /// most characters is the widest one, so a single measurement settles it where measuring
    /// every line of a large file would not. In a proportional font the answer is an estimate,
    /// and the cost of being wrong is panning slightly too far or not quite far enough.</para>
    ///
    /// <para>Call from <c>Build</c>, where <see cref="TextMeasure"/> is available. Returns
    /// zero before a measure context exists, which reads as "nothing to pan".</para>
    /// </summary>
    public static float MeasureWidest(IEnumerable<string?> lines, float fontSize, string fontFamily)
    {
        var longest = "";
        foreach (var line in lines)
            if (line != null && line.Length > longest.Length) longest = line;
        return TextMeasure.MeasureWidth(longest, fontSize, fontFamily);
    }

    /// <summary>Track geometry, for pointer work only — a drag happens long after the frame
    /// it started in has been laid out, so the real boxes are available. Building the bar
    /// deliberately needs none of this.</summary>
    private (float x, float width, float thumb) Geometry()
    {
        if (_trackRef.Current is not { } track) return (0, 0, 0);
        var box = track.Layout.AbsoluteContentBox;
        // Children: [before spacer, thumb, after spacer].
        float thumb = track.Children.Count > 1 ? track.Children[1].Layout.AbsoluteBorderBox.Width : 0;
        return (box.X, box.Width, thumb);
    }
}
