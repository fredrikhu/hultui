using HultUI.Core.Accessibility;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;

namespace HultUI.Core.Elements;

public abstract class Element
{
    // ──── Identity ────
    public ElementId Id { get; } = ElementId.New();
    public string? Key { get; set; }
    public string? Name { get; set; }

    /// <summary>
    /// Stable, non-localized identifier exposed to UI Automation as AutomationId.
    /// Unlike the accessible name, it's meant for test/automation selectors
    /// (e.g. FlaUI's <c>ByAutomationId</c>), so keep it constant across runs.
    /// </summary>
    public string? AutomationId { get; set; }

    // ──── Tree structure ────
    public Element? Parent { get; internal set; }
    // Allocated on first child: roughly half of a real tree is leaves (text,
    // icons), and an empty List per leaf adds up at 10k+ elements.
    private List<Element>? _children;
    public IReadOnlyList<Element> Children => (IReadOnlyList<Element>?)_children ?? [];

    // ──── Layout input ────
    // Style objects are allocated on first write. Most elements never touch
    // Grid/GridItem/Position (and leaves often never touch Flex/Box), so eager
    // allocation dominated element-construction cost and GC churn in large
    // trees. Public getters keep the fluent API unchanged; hot read paths
    // (layout, render, hit test, style resolution) go through the *Read
    // accessors below, which fall back to a shared immutable default instead
    // of allocating. The defaults must never be mutated — reads only.
    private FlexStyle? _flex;
    private BoxStyle? _box;
    private PositionStyle? _position;
    private GridStyle? _grid;
    private GridItemStyle? _gridItem;

    public FlexStyle Flex => _flex ??= new();
    public BoxStyle Box => _box ??= new();
    public PositionStyle Position => _position ??= new();
    public GridStyle Grid => _grid ??= new();
    public GridItemStyle GridItem => _gridItem ??= new();

    internal FlexStyle FlexRead => _flex ?? FlexStyle.Default;
    internal BoxStyle BoxRead => _box ?? BoxStyle.Default;
    internal PositionStyle PositionRead => _position ?? PositionStyle.Default;
    internal GridStyle GridRead => _grid ?? GridStyle.Default;
    internal GridItemStyle GridItemRead => _gridItem ?? GridItemStyle.Default;

    // ──── Layout output ────
    public LayoutResult Layout { get; internal set; }

    // ──── Style / Inheritance ────
    private InheritedStyle? _inherited;
    public InheritedStyle Inherited => _inherited ??= new();
    internal InheritedStyle InheritedRead => _inherited ?? InheritedStyle.Default;
    public ResolvedInheritedStyle ResolvedInherited { get; internal set; }

    /// <summary>
    /// Called during style resolution to apply theme-dependent visual properties.
    /// Override in themed elements (Button, TextInput, etc.) to read from the theme.
    /// </summary>
    protected internal virtual void ApplyTheme(Theme theme) { }

    // ──── Visual ────
    private float _opacity = 1f;
    public float Opacity
    {
        get => _opacity;
        set
        {
            var clamped = Math.Clamp(value, 0f, 1f);
            if (_opacity == clamped) return;
            _opacity = clamped;
            MarkPaintDirty();
        }
    }

    /// <summary>
    /// A 2D transform (rotate/scale/translate) applied to this element and its
    /// subtree at render and hit-test time. Null = identity (no cost).
    /// </summary>
    private Transform2D? _transform;
    public Transform2D? Transform
    {
        get => _transform;
        set { if (Nullable.Equals(_transform, value)) return; _transform = value; MarkPaintDirty(); }
    }

    // ──── Interaction state (hover / press / focus) ────
    private InteractionStyles? _interaction;
    private InteractionStyles Interaction => _interaction ??= new InteractionStyles();

    /// <summary>True while the pointer is over this element or a descendant.</summary>
    public bool IsHovered => _interaction?.Hovered ?? false;
    /// <summary>True while the pointer is pressed on this element or a descendant.</summary>
    public bool IsPressed => _interaction?.Pressed ?? false;
    /// <summary>True while this element holds keyboard focus.</summary>
    public bool IsFocused => _interaction?.Focused ?? false;

    internal void AddHoverStyle(Action<Element> style) => Interaction.OnHover += style;
    internal void AddPressedStyle(Action<Element> style) => Interaction.OnPressed += style;
    internal void AddFocusStyle(Action<Element> style) => Interaction.OnFocus += style;
    internal void AddTransition(Animation.TransitionProperty property, float seconds, Func<float, float> easing) =>
        Interaction.AddTransition(property, seconds, easing);

    // Called by the event pipeline (PointerState / FocusManager) when interaction
    // state changes; no-ops when the element has no interaction styling registered.
    internal void SetInteractionHovered(bool value)
    {
        if (_interaction is { } s && s.Hovered != value) { s.Hovered = value; s.Recompute(this, animate: true); }
    }
    internal void SetInteractionPressed(bool value)
    {
        if (_interaction is { } s && s.Pressed != value) { s.Pressed = value; s.Recompute(this, animate: true); }
    }
    internal void SetInteractionFocused(bool value)
    {
        if (_interaction is { } s && s.Focused != value) { s.Focused = value; s.Recompute(this, animate: true); }
    }

    // Called by the reconciler after a full patch: the visual props now hold the new
    // base, so re-snapshot it and re-apply whatever states are currently active
    // (without animating — a rebuild shouldn't trigger transitions).
    internal void RefreshInteractionAfterPatch()
    {
        if (_interaction is { } s) { s.InvalidateBase(); s.Recompute(this, animate: false); }
    }

    /// <summary>
    /// Called by the reconciler after this element has been patched and its children
    /// reconciled. Self-managing elements (e.g. Tabs) override this to re-assert
    /// their internally-owned subtree, which the child reconciliation may have
    /// altered. Default is a no-op.
    /// </summary>
    internal virtual void OnReconciled() { }

    // ──── Drag & Drop ────
    public bool Draggable { get; set; }

    // ──── Window Chrome ────
    public bool IsWindowDragRegion { get; set; }

    /// <summary>Marks the element as the window's maximize/restore button. On
    /// Windows this lets the custom frame report <c>HTMAXBUTTON</c> for the
    /// element's region so Windows 11 shows its Snap Layouts flyout on hover.</summary>
    public bool IsWindowMaximizeButton { get; set; }

    // ──── Overlay ────
    public IOverlayHost? OverlayHost { get; internal set; }

    // ──── Tooltip ────
    public string? TooltipText { get; set; }

    // ──── Context Menu ────
    public Func<IReadOnlyList<ContextMenuItem>>? ContextMenuItems { get; set; }

    // ──── Focus ────
    public bool IsFocusable { get; set; }

    /// <summary>
    /// Controls Tab traversal order, following HTML <c>tabindex</c> semantics:
    ///   <list type="bullet">
    ///     <item><c>&gt; 0</c>: visited first, ascending (ties broken by document order).</item>
    ///     <item><c>== 0</c> (default): visited after all positives, in document order.</item>
    ///     <item><c>&lt; 0</c>: focusable programmatically via <see cref="Focus"/> but skipped by Tab.</item>
    ///   </list>
    /// Has no effect unless <see cref="IsFocusable"/> is true.
    /// </summary>
    public int TabIndex { get; set; }

    internal FocusManager? FocusManager { get; set; }

    /// <summary>
    /// Programmatically focuses this element. The element must be focusable
    /// and attached to a tree that has a <see cref="Events.FocusManager"/>.
    /// </summary>
    public void Focus()
    {
        if (!IsFocusable) return;

        // FocusManager may not have been propagated yet (e.g. element was
        // just added to the tree and style resolution hasn't run). Walk up
        // the parent chain to find it.
        var fm = FocusManager;
        if (fm is null)
        {
            for (var p = Parent; p is not null; p = p.Parent)
            {
                if (p.FocusManager is not null) { fm = p.FocusManager; break; }
            }
        }

        fm?.SetFocus(this);
    }

    // ──── Accessibility ────
    public AccessibilityRole Role { get; set; } = AccessibilityRole.None;
    public string? AccessibleName { get; set; }

    /// <summary>
    /// Expanded state for an expandable node (e.g. a tree item), exposed as the
    /// UIA ExpandCollapse pattern: <c>null</c> = not expandable, <c>true</c> =
    /// expanded, <c>false</c> = collapsed. Expand/Collapse from assistive tech
    /// reuse the element's click handler, so wire the node to toggle on click.
    /// </summary>
    public bool? AccessibleExpanded { get; set; }
    public string? AccessibleDescription { get; set; }

    /// <summary>
    /// Set by <see cref="Window"/> on the root when a platform accessibility
    /// bridge is active. Descendants discover it by walking up <see cref="Parent"/>
    /// in <see cref="NotifyAccessibilityPropertyChanged"/>.
    /// </summary>
    internal IElementPropertyObserver? AccessibilityObserver { get; set; }

    /// <summary>
    /// Announces that an accessibility-visible property on this element changed
    /// (e.g. a checkbox was toggled, a text input's value was replaced). Walks
    /// up the parent chain to deliver to the root-scoped observer, if any.
    /// Use constants from <see cref="AccessibilityProperties"/> for the name.
    /// </summary>
    protected internal void NotifyAccessibilityPropertyChanged(string propertyName)
    {
        for (Element? current = this; current is not null; current = current.Parent)
        {
            if (current.AccessibilityObserver is { } observer)
            {
                observer.OnElementPropertyChanged(this, propertyName);
                return;
            }
        }
    }

    // ──── Refs ────
    // Refs registered via WithRef(). During PatchFrom the source element's
    // refs are repointed to the live (target) element so that handler
    // closures which captured the Ref always reach the in-tree element.
    internal List<IRef>? _refs;

    /// <summary>
    /// Register a <see cref="Ref{T}"/> on this element. The ref's
    /// <c>Current</c> is set to this element immediately, and will be
    /// updated to the live element when PatchFrom runs during reconciliation.
    /// </summary>
    public Element WithRef<T>(Ref<T> r) where T : Element
    {
        _refs ??= [];
        _refs.Add(r);
        r.Current = (T)this;
        return this;
    }

    // ──── Events ────
    private Dictionary<Type, List<EventHandlerEntry>>? _handlers;

    public void On<TEvent>(Action<TEvent> handler) where TEvent : ElementEvent
    {
        On(EventPhase.Bubble, handler);
    }

    public void On<TEvent>(EventPhase phase, Action<TEvent> handler) where TEvent : ElementEvent =>
        AddHandler(phase, handler, owned: false);

    /// <summary>
    /// Register an element-owned handler: part of this element's own machinery
    /// (constructor wiring, handlers on internally-built children). Owned handlers
    /// survive reconciliation; app handlers registered via <see cref="On{TEvent}(Action{TEvent})"/>
    /// are replaced by the incoming build's handlers in <see cref="PatchFrom"/>.
    /// </summary>
    internal void OnOwn<TEvent>(Action<TEvent> handler) where TEvent : ElementEvent =>
        AddHandler(EventPhase.Bubble, handler, owned: true);

    private void AddHandler<TEvent>(EventPhase phase, Action<TEvent> handler, bool owned) where TEvent : ElementEvent
    {
        _handlers ??= [];
        var type = typeof(TEvent);
        if (!_handlers.TryGetValue(type, out var list))
        {
            list = [];
            _handlers[type] = list;
        }
        list.Add(new EventHandlerEntry(phase, evt => handler((TEvent)evt), owned));
    }

    internal bool HasEventHandler<TEvent>() where TEvent : ElementEvent =>
        _handlers is not null && _handlers.ContainsKey(typeof(TEvent));

    internal bool HasAnyEventHandlers => _handlers is not null && _handlers.Count > 0;

    /// <summary>
    /// Names of the event types this element has handlers for, for DevTools
    /// introspection ("why doesn't my click fire" is usually "nothing is listening").
    /// </summary>
    internal IEnumerable<string> HandlerEventNames =>
        _handlers is null ? [] : _handlers.Keys.Select(static t => t.Name);

    internal void InvokeHandlers(ElementEvent evt, EventPhase phase)
    {
        if (_handlers is null) return;
        if (!_handlers.TryGetValue(evt.GetType(), out var list)) return;

        // Snapshot: a handler can MarkDirty → synchronous rebuild → PatchFrom, which
        // swaps this element's app handlers mid-dispatch (CopyAppHandlersFrom). The
        // event runs the handlers that were attached when dispatch started.
        foreach (var entry in list.ToArray())
        {
            if (entry.Phase == phase || phase == EventPhase.Target)
            {
                EventDispatcher.HandlerInvocationCount++;
                entry.Handler(evt);
                if (evt.StopPropagation) break;
            }
        }
    }

    // ──── Dirty tracking ────
    internal DirtyFlags DirtyFlags { get; set; } = DirtyFlags.All;

    // ──── Layout cache (used by LayoutEngine for subtree skipping) ────
    internal float CacheConstraintW;
    internal float CacheConstraintH;
    internal float? CacheForcedW;
    internal float? CacheForcedH;
    internal Size CacheResultSize;

    // ──── Measure cache (Phase 1 sizing without writing Layout) ────
    // Each ancestor's flex Phase 1 calls MeasureSize on this element. Without
    // this cache, that's O(depth) measure passes per frame — see LayoutEngine
    // for the full rationale. CacheMeasureValid distinguishes "never cached"
    // from "cached size happens to be (0,0)".
    internal bool CacheMeasureValid;
    internal float CacheMeasureConstraintW;
    internal float CacheMeasureConstraintH;
    internal float? CacheMeasureForcedW;
    internal float? CacheMeasureForcedH;
    internal Size CacheMeasureResult;

    /// <summary>
    /// Marks this element (and ancestors up to root) as needing layout on the next frame.
    /// Call this after directly mutating <see cref="Flex"/>, <see cref="Box"/>, or
    /// <see cref="Position"/> style properties at runtime — those property setters do not
    /// auto-invalidate, so the layout pass would otherwise see no reason to re-run.
    /// Most code does not need this: builder fluent calls happen before the first layout
    /// pass, and component rebuilds invalidate via PatchFrom. This is the escape hatch
    /// for imperative drag/animation/resize code that mutates styles directly.
    /// </summary>
    public void MarkLayoutDirty()
    {
        var current = (Element?)this;
        while (current is not null)
        {
            if ((current.DirtyFlags & DirtyFlags.Layout) != 0)
                break;
            current.DirtyFlags |= DirtyFlags.Layout;
            // Invalidate the cached MeasureSize result for this element and
            // every ancestor: the changed subtree may produce a different
            // outer size at the next Phase 1 cascade. The layout cache
            // (CacheConstraintW/H/...) is gated by DirtyFlags.Layout, so it's
            // already invalidated implicitly.
            current.CacheMeasureValid = false;
            current = current.Parent;
        }
    }

    /// <summary>
    /// Marks this element (and ancestors up to root) as needing repaint on the next frame
    /// without re-running layout. Use after direct mutations that only affect appearance
    /// (background color, opacity, etc.) so the change is visible without a full layout pass.
    /// </summary>
    public void MarkPaintDirty()
    {
        var current = (Element?)this;
        while (current is not null)
        {
            if ((current.DirtyFlags & DirtyFlags.Paint) != 0)
                break;
            current.DirtyFlags |= DirtyFlags.Paint;
            current = current.Parent;
        }
    }

    /// <summary>
    /// Marks this element (and ancestors up to root) as having had a structural
    /// change to its children. Distinct from <see cref="MarkLayoutDirty"/>: only
    /// child add/remove/replace sets this, so the accessibility tree can rebuild
    /// on real structure changes without rebuilding on every resize or animation.
    /// </summary>
    internal void MarkChildrenDirty()
    {
        var current = (Element?)this;
        while (current is not null)
        {
            if ((current.DirtyFlags & DirtyFlags.Children) != 0)
                break;
            current.DirtyFlags |= DirtyFlags.Children;
            current = current.Parent;
        }
    }

    internal void MarkStyleDirty()
    {
        // Propagate down (inherited style may change for descendants)
        MarkStyleDirtyDown();
        // Propagate up so root knows style work is needed
        var current = Parent;
        while (current is not null)
        {
            if ((current.DirtyFlags & DirtyFlags.Style) != 0)
                break;
            current.DirtyFlags |= DirtyFlags.Style;
            current = current.Parent;
        }
    }

    private void MarkStyleDirtyDown()
    {
        DirtyFlags |= DirtyFlags.Style;
        if (_children is null) return;
        foreach (var child in _children)
            child.MarkStyleDirtyDown();
    }

    // ──── Lifecycle ────
    protected virtual void OnMounted() { }
    protected virtual void OnUnmounted() { }

    // ──── Tree mutation ────
    //
    // Lifecycle contract:
    //   OnMounted  fires when an element enters a tree for the first time.
    //   OnUnmounted fires when an element permanently leaves a tree.
    //
    // When an element is *moved* (it already has a parent and is added to a
    // different one), neither lifecycle event fires — the element was never
    // truly removed.  This prevents ComponentHost from tearing down its
    // reactive Effect every time a parent component rebuilds and passes the
    // same child through a helper like SplitPane.Build().

    public void AddChild(Element child)
    {
        var wasParented = child.Parent is not null;

        if (child.Parent is not null)
            DetachSilently(child);

        child.Parent = this;
        (_children ??= []).Add(child);
        MarkLayoutDirty();
        MarkChildrenDirty();

        if (!wasParented)
        {
            child.OnMounted();
            // A freshly mounted subtree has no resolved inherited style yet, so
            // it would measure with a zero font size (zero-height text) until
            // some later restyle. Flag it so the next frame's style pass resolves
            // it before layout. Structural ops otherwise mark only layout/children.
            child.MarkStyleDirty();
        }
    }

    public void InsertChild(int index, Element child)
    {
        var wasParented = child.Parent is not null;

        if (child.Parent is not null)
            DetachSilently(child);

        child.Parent = this;
        (_children ??= []).Insert(index, child);
        MarkLayoutDirty();
        MarkChildrenDirty();

        if (!wasParented)
        {
            child.OnMounted();
            child.MarkStyleDirty(); // see AddChild: resolve the new subtree's style
        }
    }

    public void RemoveChild(Element child)
    {
        if (_children?.Remove(child) == true)
        {
            ClearFocusIfRemoved(child);
            child.OnUnmounted();
            child.Parent = null;
            MarkLayoutDirty();
            MarkChildrenDirty();
        }
    }

    public void ReplaceChildren(params Element[] children)
    {
        // Snapshot which incoming children are already mounted somewhere so
        // we can distinguish moves (silent) from first mounts (fire OnMounted).
        var alreadyMounted = new HashSet<Element>();
        foreach (var child in children)
        {
            if (child.Parent is not null)
                alreadyMounted.Add(child);
        }

        // Remove old children. Only fire OnUnmounted for elements that are
        // genuinely leaving — not those that reappear in the new list.
        var newSet = new HashSet<Element>(children);
        if (_children is not null)
        {
            foreach (var old in _children)
            {
                if (!newSet.Contains(old))
                {
                    ClearFocusIfRemoved(old);
                    old.OnUnmounted();
                }
                old.Parent = null;
            }
            _children.Clear();
        }

        // Add new children.
        var list = _children ??= new List<Element>(children.Length);
        foreach (var child in children)
        {
            if (child.Parent is not null)
                DetachSilently(child);

            child.Parent = this;
            list.Add(child);

            if (!alreadyMounted.Contains(child))
            {
                child.OnMounted();
                child.MarkStyleDirty(); // see AddChild: resolve the new subtree's style
            }
        }
        MarkLayoutDirty();
        MarkChildrenDirty();
    }

    /// <summary>
    /// Removes <paramref name="child"/> from its current parent without
    /// firing lifecycle events.  Used for element moves — the child is not
    /// leaving the tree, just changing parents.
    /// </summary>
    private static void DetachSilently(Element child)
    {
        child.Parent?._children?.Remove(child);
        child.Parent?.MarkLayoutDirty();
        child.Parent?.MarkChildrenDirty();
        child.Parent = null;
    }

    private void ClearFocusIfRemoved(Element removed)
    {
        var fm = FocusManager ?? removed.FocusManager;
        if (fm?.FocusedElement is { } focused && ContainsElement(removed, focused))
            fm.ClearFocus();
    }

    private static bool ContainsElement(Element root, Element candidate)
    {
        for (Element? current = candidate; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, root))
                return true;
        }
        return false;
    }

    // ──── Patching (used by TreeDiffer) ────
    internal virtual void PatchFrom(Element source)
    {
        Key = source.Key;
        Name = source.Name;
        // Copy styles without forcing allocation on either side: when the source
        // never touched a style and the target did, reset the target to defaults
        // (copying from the shared immutable default); when neither side has one,
        // there is nothing to do.
        if (source._flex is not null) Flex.CopyFrom(source._flex);
        else _flex?.CopyFrom(FlexStyle.Default);
        if (source._box is not null) Box.CopyFrom(source._box);
        else _box?.CopyFrom(BoxStyle.Default);
        if (source._position is not null) Position.CopyFrom(source._position);
        else _position?.CopyFrom(PositionStyle.Default);
        if (source._grid is not null) Grid.CopyFrom(source._grid);
        else _grid?.CopyFrom(GridStyle.Default);
        if (source._gridItem is not null) GridItem.CopyFrom(source._gridItem);
        else _gridItem?.CopyFrom(GridItemStyle.Default);
        if (source._inherited is not null) Inherited.CopyFrom(source._inherited);
        else _inherited?.CopyFrom(InheritedStyle.Default);
        IsFocusable = source.IsFocusable;
        TabIndex = source.TabIndex;
        Draggable = source.Draggable;
        IsWindowDragRegion = source.IsWindowDragRegion;
        IsWindowMaximizeButton = source.IsWindowMaximizeButton;
        TooltipText = source.TooltipText;
        ContextMenuItems = source.ContextMenuItems;
        _opacity = source._opacity;
        Transform = source.Transform;
        // Interaction config follows the latest build, like event handlers: overrides
        // are pure functions of the element, so the incoming build's config carries
        // across reconciliation — and an incoming element WITHOUT interaction clears
        // any stale config (a conditionally-attached hover style must disappear when
        // its condition stops holding). The base snapshot is refreshed and the active
        // states re-applied in RefreshInteractionAfterPatch, once all visual props
        // have been copied.
        if (source._interaction is { } sourceInteraction)
            Interaction.CopyConfigFrom(sourceInteraction);
        else
            _interaction?.ClearConfig();
        Role = source.Role;
        AccessibleName = source.AccessibleName;
        AccessibleDescription = source.AccessibleDescription;
        AutomationId = source.AutomationId;
        AccessibleExpanded = source.AccessibleExpanded;
        // App event handlers ARE copied: the incoming build is the source of truth,
        // so a retained element always runs the latest build's handlers (matching how
        // callback props like ButtonElement.Clicked already behave). Element-OWNED
        // handlers (constructor wiring registered via OnOwn) are kept instead — the
        // incoming element's owned closures capture the incoming (detached) instance.
        // Caveat for app code: a handler that captures a *sibling element* from its own
        // build now points at a detached element after the next patch — reach across
        // elements via Ref<T> (repointed below), never via captured element locals.
        CopyAppHandlersFrom(source);
        if (source._refs != null)
        {
            foreach (var r in source._refs)
                r.SetTarget(this);
        }

        MarkLayoutDirty();
        MarkPaintDirty();
        MarkStyleDirty();
    }

    /// <summary>
    /// Drop this element's app handlers and adopt the incoming build's, preserving
    /// element-owned (constructor-wired) handlers and their registration order:
    /// owned handlers always precede app handlers, both at creation and after a patch.
    /// </summary>
    private void CopyAppHandlersFrom(Element source)
    {
        if (_handlers is not null)
        {
            List<Type>? empty = null;
            foreach (var (type, list) in _handlers)
            {
                list.RemoveAll(static e => !e.Owned);
                if (list.Count == 0)
                    (empty ??= []).Add(type);
            }
            if (empty is not null)
                foreach (var type in empty)
                    _handlers.Remove(type);
        }

        if (source._handlers is null)
            return;
        foreach (var (type, list) in source._handlers)
        {
            foreach (var entry in list)
            {
                if (entry.Owned)
                    continue;
                _handlers ??= [];
                if (!_handlers.TryGetValue(type, out var target))
                    _handlers[type] = target = [];
                target.Add(entry);
            }
        }
    }

    // ──── Rendering ────
    public virtual void Render(IRenderContext ctx) { }

    public virtual bool HitTest(Point point) =>
        Layout.AbsoluteBorderBox.Contains(point);

    // ──── Measurement (overridden by leaf elements) ────
    internal virtual Size MeasureContent(float availableWidth, float availableHeight, IMeasureContext ctx) =>
        Size.Zero;
}

internal readonly record struct EventHandlerEntry(EventPhase Phase, Action<ElementEvent> Handler, bool Owned = false);
