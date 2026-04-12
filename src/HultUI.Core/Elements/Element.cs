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

    // ──── Tree structure ────
    public Element? Parent { get; internal set; }
    private readonly List<Element> _children = [];
    public IReadOnlyList<Element> Children => _children;

    // ──── Layout input ────
    public FlexStyle Flex { get; } = new();
    public BoxStyle Box { get; } = new();
    public PositionStyle Position { get; } = new();

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

    // ──── Drag & Drop ────
    public bool Draggable { get; set; }

    // ──── Window Chrome ────
    public bool IsWindowDragRegion { get; set; }

    // ──── Overlay ────
    public IOverlayHost? OverlayHost { get; internal set; }

    // ──── Tooltip ────
    public string? TooltipText { get; set; }

    // ──── Context Menu ────
    public Func<IReadOnlyList<ContextMenuItem>>? ContextMenuItems { get; set; }

    // ──── Focus ────
    public bool IsFocusable { get; set; }
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
    public string? AccessibleDescription { get; set; }

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

        if (!wasParented)
            child.OnMounted();
    }

    public void InsertChild(int index, Element child)
    {
        var wasParented = child.Parent is not null;

        if (child.Parent is not null)
            DetachSilently(child);

        child.Parent = this;
        _children.Insert(index, child);
        MarkLayoutDirty();

        if (!wasParented)
            child.OnMounted();
    }

    public void RemoveChild(Element child)
    {
        if (_children.Remove(child))
        {
            child.OnUnmounted();
            child.Parent = null;
            MarkLayoutDirty();
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
                old.OnUnmounted();
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
                child.OnMounted();
        }
        MarkLayoutDirty();
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
        child.Parent = null;
    }

    // ──── Patching (used by TreeDiffer) ────
    internal virtual void PatchFrom(Element source)
    {
        Key = source.Key;
        Name = source.Name;
        Flex.CopyFrom(source.Flex);
        Box.CopyFrom(source.Box);
        Position.CopyFrom(source.Position);
        Inherited.CopyFrom(source.Inherited);
        IsFocusable = source.IsFocusable;
        Draggable = source.Draggable;
        IsWindowDragRegion = source.IsWindowDragRegion;
        TooltipText = source.TooltipText;
        ContextMenuItems = source.ContextMenuItems;
        _opacity = source._opacity;
        Role = source.Role;
        AccessibleName = source.AccessibleName;
        AccessibleDescription = source.AccessibleDescription;
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
