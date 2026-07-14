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
    private readonly List<Element> _children = [];
    public IReadOnlyList<Element> Children => _children;

    // ──── Layout input ────
    public FlexStyle Flex { get; } = new();
    public BoxStyle Box { get; } = new();
    public PositionStyle Position { get; } = new();
    public GridStyle Grid { get; } = new();
    public GridItemStyle GridItem { get; } = new();

    // ──── Layout output ────
    public LayoutResult Layout { get; internal set; }

    // ──── Style / Inheritance ────
    public InheritedStyle Inherited { get; } = new();
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
    public Transform2D? Transform { get; set; }

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

    public void On<TEvent>(EventPhase phase, Action<TEvent> handler) where TEvent : ElementEvent
    {
        _handlers ??= [];
        var type = typeof(TEvent);
        if (!_handlers.TryGetValue(type, out var list))
        {
            list = [];
            _handlers[type] = list;
        }
        list.Add(new EventHandlerEntry(phase, evt => handler((TEvent)evt)));
    }

    internal bool HasEventHandler<TEvent>() where TEvent : ElementEvent =>
        _handlers is not null && _handlers.ContainsKey(typeof(TEvent));

    internal bool HasAnyEventHandlers => _handlers is not null && _handlers.Count > 0;

    internal void InvokeHandlers(ElementEvent evt, EventPhase phase)
    {
        if (_handlers is null) return;
        if (!_handlers.TryGetValue(evt.GetType(), out var list)) return;

        foreach (var entry in list)
        {
            if (entry.Phase == phase || phase == EventPhase.Target)
            {
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
        _children.Add(child);
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
        _children.Insert(index, child);
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
        if (_children.Remove(child))
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

        // Add new children.
        foreach (var child in children)
        {
            if (child.Parent is not null)
                DetachSilently(child);

            child.Parent = this;
            _children.Add(child);

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
        child.Parent?._children.Remove(child);
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
        Flex.CopyFrom(source.Flex);
        Box.CopyFrom(source.Box);
        Position.CopyFrom(source.Position);
        Grid.CopyFrom(source.Grid);
        GridItem.CopyFrom(source.GridItem);
        Inherited.CopyFrom(source.Inherited);
        IsFocusable = source.IsFocusable;
        TabIndex = source.TabIndex;
        Draggable = source.Draggable;
        IsWindowDragRegion = source.IsWindowDragRegion;
        IsWindowMaximizeButton = source.IsWindowMaximizeButton;
        TooltipText = source.TooltipText;
        ContextMenuItems = source.ContextMenuItems;
        _opacity = source._opacity;
        Transform = source.Transform;
        // Interaction overrides are pure functions of the element, so the config
        // carries across reconciliation. Copy it here (order-independent); the base
        // snapshot is refreshed and the active states re-applied by the reconciler
        // in RefreshInteractionAfterPatch, once all visual props have been copied.
        if (source._interaction is { } sourceInteraction)
            Interaction.CopyConfigFrom(sourceInteraction);
        Role = source.Role;
        AccessibleName = source.AccessibleName;
        AccessibleDescription = source.AccessibleDescription;
        AutomationId = source.AutomationId;
        AccessibleExpanded = source.AccessibleExpanded;
        // Event handlers are NOT copied. Handlers from the first Build()
        // capture references to the existing (live) elements. Copying handlers
        // from a new Build() would bring closures pointing to detached elements.
        // For cross-element references, use Ref<T> — refs ARE updated here.
        if (source._refs != null)
        {
            foreach (var r in source._refs)
                r.SetTarget(this);
        }

        MarkLayoutDirty();
        MarkPaintDirty();
        MarkStyleDirty();
    }

    // ──── Rendering ────
    public virtual void Render(IRenderContext ctx) { }

    public virtual bool HitTest(Point point) =>
        Layout.AbsoluteBorderBox.Contains(point);

    // ──── Measurement (overridden by leaf elements) ────
    internal virtual Size MeasureContent(float availableWidth, float availableHeight, IMeasureContext ctx) =>
        Size.Zero;
}

internal readonly record struct EventHandlerEntry(EventPhase Phase, Action<ElementEvent> Handler);
