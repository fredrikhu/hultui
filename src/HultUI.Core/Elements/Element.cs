using HultUI.Core.Accessibility;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
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

    // ──── Focus ────
    public bool IsFocusable { get; set; }

    // ──── Accessibility ────
    public AccessibilityRole Role { get; set; } = AccessibilityRole.None;
    public string? AccessibleName { get; set; }
    public string? AccessibleDescription { get; set; }

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

    internal void MarkLayoutDirty()
    {
        var current = (Element?)this;
        while (current is not null)
        {
            if (current.DirtyFlags.HasFlag(DirtyFlags.Layout))
                break;
            current.DirtyFlags |= DirtyFlags.Layout;
            current = current.Parent;
        }
    }

    internal void MarkPaintDirty()
    {
        var current = (Element?)this;
        while (current is not null)
        {
            if (current.DirtyFlags.HasFlag(DirtyFlags.Paint))
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
            if (current.DirtyFlags.HasFlag(DirtyFlags.Style))
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
    public void AddChild(Element child)
    {
        if (child.Parent is not null)
            child.Parent.RemoveChild(child);

        child.Parent = this;
        _children.Add(child);
        MarkLayoutDirty();
        child.OnMounted();
    }

    public void InsertChild(int index, Element child)
    {
        if (child.Parent is not null)
            child.Parent.RemoveChild(child);

        child.Parent = this;
        _children.Insert(index, child);
        MarkLayoutDirty();
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
        foreach (var old in _children)
        {
            old.OnUnmounted();
            old.Parent = null;
        }
        _children.Clear();

        foreach (var child in children)
        {
            if (child.Parent is not null)
                child.Parent.RemoveChild(child);
            child.Parent = this;
            _children.Add(child);
            child.OnMounted();
        }
        MarkLayoutDirty();
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
        _opacity = source._opacity;
        Role = source.Role;
        AccessibleName = source.AccessibleName;
        AccessibleDescription = source.AccessibleDescription;
        // Note: event handlers are NOT copied. Handlers from the first Build()
        // capture references to the existing (live) elements. Copying handlers
        // from a new Build() would bring closures pointing to detached elements.
        // For dynamic handler behavior, read signals inside handlers instead.
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
