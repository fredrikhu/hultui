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
        DirtyFlags |= DirtyFlags.Paint;
    }

    internal void MarkStyleDirty()
    {
        DirtyFlags |= DirtyFlags.Style;
        foreach (var child in _children)
            child.MarkStyleDirty();
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
        Role = source.Role;
        AccessibleName = source.AccessibleName;
        AccessibleDescription = source.AccessibleDescription;
        // Note: event handlers are NOT copied. Handlers from the first Build()
        // capture references to the existing (live) elements. Copying handlers
        // from a new Build() would bring closures pointing to detached elements.
        // For dynamic handler behavior, read signals inside handlers instead.
        MarkLayoutDirty();
        MarkPaintDirty();
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
