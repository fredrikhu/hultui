using HultUI.Core.Elements;

namespace HultUI.Core.DevTools;

/// <summary>One row of the flattened element tree shown in the inspector.</summary>
public readonly record struct InspectorNode(Element Element, int Depth, bool HasChildren, bool IsExpanded);

/// <summary>
/// The inspector's model: what is selected, what the picker is hovering, which
/// subtrees are expanded, and how the keyboard moves between elements. Deliberately
/// free of any window or rendering dependency — the panel is one presentation of
/// this state, the highlight drawn over the app is another, and both are testable
/// headlessly by driving this class directly.
/// </summary>
public sealed class ElementInspector
{
    private readonly HashSet<ElementId> _expanded = [];
    private IReadOnlyList<Element> _roots = [];
    private Element? _selected;
    private Element? _hovered;
    private bool _isPicking;
    private string _filter = "";

    /// <summary>Raised whenever anything the panel or highlight renders from changes.</summary>
    public event Action? Changed;

    /// <summary>
    /// The trees available to inspect: a window's root plus any open overlay roots
    /// (context menus, tooltips, dialogs), which are separate trees and would
    /// otherwise be unreachable. Refreshed by the host each time it syncs.
    /// </summary>
    public IReadOnlyList<Element> Roots => _roots;

    /// <summary>The element whose properties are shown. Null when nothing is selected.</summary>
    public Element? Selected => _selected;

    /// <summary>
    /// The element under the pointer while picking. It drives the highlight but is
    /// not committed — moving away drops it, clicking promotes it to the selection.
    /// </summary>
    public Element? Hovered => _hovered;

    /// <summary>The element the highlight should outline: the live pick preview, else the selection.</summary>
    public Element? Highlighted => _hovered ?? _selected;

    /// <summary>
    /// True while the picker is armed: the inspected window routes pointer input to
    /// the inspector instead of the app, so clicking a button selects it rather than
    /// pressing it.
    /// </summary>
    public bool IsPicking => _isPicking;

    /// <summary>
    /// True when the selection has been detached from every root — its subtree was
    /// removed by a rebuild. The selection is kept (its last-known properties still
    /// read fine) but the panel can say so instead of implying it is live.
    /// </summary>
    public bool IsSelectionStale => _selected is not null && RootOf(_selected) is { } root && !_roots.Contains(root);

    /// <summary>
    /// Case-insensitive filter over type labels and descriptors. When set, the tree
    /// shows matching nodes and the ancestors needed to reach them, regardless of
    /// expansion state.
    /// </summary>
    public string Filter
    {
        get => _filter;
        set
        {
            var next = value ?? "";
            if (_filter == next) return;
            _filter = next;
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// Replaces the set of inspectable trees. Expansion state is dropped when the
    /// roots change identity (a new window's tree), because element ids from the
    /// old tree would otherwise linger forever.
    /// </summary>
    public void SetRoots(IReadOnlyList<Element> roots)
    {
        roots ??= [];
        if (_roots.Count == roots.Count)
        {
            bool same = true;
            for (int i = 0; i < roots.Count; i++)
            {
                if (!ReferenceEquals(_roots[i], roots[i])) { same = false; break; }
            }
            if (same) return;
        }

        bool primaryChanged = _roots.Count == 0 || roots.Count == 0
                              || !ReferenceEquals(_roots[0], roots[0]);
        _roots = roots;
        if (primaryChanged) _expanded.Clear();
        Changed?.Invoke();
    }

    /// <summary>Arms or disarms the picker.</summary>
    public void SetPicking(bool picking)
    {
        if (_isPicking == picking) return;
        _isPicking = picking;
        // Dropping the preview on disarm matters: otherwise the highlight keeps
        // outlining whatever the pointer happened to be over when picking ended.
        if (!picking) _hovered = null;
        Changed?.Invoke();
    }

    /// <summary>Updates the pick preview. No-op unless the picker is armed.</summary>
    public void SetHovered(Element? element)
    {
        if (!_isPicking || ReferenceEquals(_hovered, element)) return;
        _hovered = element;
        Changed?.Invoke();
    }

    /// <summary>
    /// Commits <paramref name="element"/> as the selection, expanding its ancestors
    /// so it is visible in the tree, and disarms the picker — one click, one pick,
    /// matching what every browser inspector does.
    /// </summary>
    public void Pick(Element? element)
    {
        _isPicking = false;
        _hovered = null;
        Select(element);
    }

    /// <summary>Selects <paramref name="element"/> and expands the path to it.</summary>
    public void Select(Element? element)
    {
        bool changed = !ReferenceEquals(_selected, element);
        _selected = element;
        if (element is not null)
        {
            for (var ancestor = element.Parent; ancestor is not null; ancestor = ancestor.Parent)
                changed |= _expanded.Add(ancestor.Id);
        }
        if (changed) Changed?.Invoke();
    }

    /// <summary>Clears the selection, the preview, and the picker.</summary>
    public void Clear()
    {
        if (_selected is null && _hovered is null && !_isPicking) return;
        _selected = null;
        _hovered = null;
        _isPicking = false;
        Changed?.Invoke();
    }

    public bool IsExpanded(Element element) => _expanded.Contains(element.Id);

    public void SetExpanded(Element element, bool expanded)
    {
        bool changed = expanded ? _expanded.Add(element.Id) : _expanded.Remove(element.Id);
        if (changed) Changed?.Invoke();
    }

    public void ToggleExpanded(Element element) => SetExpanded(element, !IsExpanded(element));

    /// <summary>Expands every node from the roots down, bounded by <paramref name="maxDepth"/>.</summary>
    public void ExpandAll(int maxDepth = 6)
    {
        foreach (var root in _roots)
            ExpandRecursive(root, maxDepth);
        Changed?.Invoke();

        void ExpandRecursive(Element element, int remaining)
        {
            if (remaining <= 0 || element.Children.Count == 0) return;
            _expanded.Add(element.Id);
            foreach (var child in element.Children)
                ExpandRecursive(child, remaining - 1);
        }
    }

    public void CollapseAll()
    {
        if (_expanded.Count == 0) return;
        _expanded.Clear();
        Changed?.Invoke();
    }

    /// <summary>
    /// The visible tree as a flat list, ready for a virtualized list. Children of a
    /// collapsed node are omitted; under a filter, only matches and their ancestors
    /// appear (a filtered tree ignores expansion state, or the match you searched
    /// for would stay hidden inside a collapsed parent).
    /// </summary>
    public IReadOnlyList<InspectorNode> Flatten()
    {
        var result = new List<InspectorNode>();
        if (_roots.Count == 0) return result;

        if (_filter.Length == 0)
        {
            foreach (var root in _roots)
                Walk(root, 0);
            return result;
        }

        var visible = new HashSet<ElementId>();
        foreach (var root in _roots)
            MarkMatches(root, visible);
        foreach (var root in _roots)
            WalkFiltered(root, 0, visible);
        return result;

        void Walk(Element element, int depth)
        {
            bool hasChildren = element.Children.Count > 0;
            bool expanded = hasChildren && _expanded.Contains(element.Id);
            result.Add(new InspectorNode(element, depth, hasChildren, expanded));
            if (!expanded) return;
            foreach (var child in element.Children)
                Walk(child, depth + 1);
        }

        void WalkFiltered(Element element, int depth, HashSet<ElementId> visible)
        {
            if (!visible.Contains(element.Id)) return;
            bool hasChildren = element.Children.Count > 0;
            result.Add(new InspectorNode(element, depth, hasChildren, hasChildren));
            foreach (var child in element.Children)
                WalkFiltered(child, depth + 1, visible);
        }
    }

    /// <summary>Marks <paramref name="element"/> visible when it or a descendant matches the filter.</summary>
    private bool MarkMatches(Element element, HashSet<ElementId> visible)
    {
        bool matched = Matches(element);
        foreach (var child in element.Children)
            matched |= MarkMatches(child, visible);
        if (matched) visible.Add(element.Id);
        return matched;
    }

    private bool Matches(Element element) =>
        ElementProperties.TypeLabel(element).Contains(_filter, StringComparison.OrdinalIgnoreCase)
        || ElementProperties.Descriptor(element).Contains(_filter, StringComparison.OrdinalIgnoreCase);

    /// <summary>Index of the selection in <paramref name="nodes"/>, or -1 when it isn't visible.</summary>
    public static int IndexOf(IReadOnlyList<InspectorNode> nodes, Element? element)
    {
        if (element is null) return -1;
        for (int i = 0; i < nodes.Count; i++)
        {
            if (ReferenceEquals(nodes[i].Element, element)) return i;
        }
        return -1;
    }

    /// <summary>The selection's ancestors, outermost first, followed by the selection itself.</summary>
    public IReadOnlyList<Element> SelectionPath()
    {
        if (_selected is null) return [];
        var path = new List<Element>();
        for (Element? current = _selected; current is not null; current = current.Parent)
            path.Add(current);
        path.Reverse();
        return path;
    }

    // ──── Keyboard navigation ────
    //
    // Left/Right collapse and expand (falling back to moving out of / into the
    // subtree when there is nothing to fold), Up/Down move through the flattened
    // visible list. That is the browser-inspector arrangement, and it means Down
    // walks the whole tree in document order rather than only the siblings.

    public void SelectParent()
    {
        if (_selected?.Parent is { } parent) Select(parent);
    }

    public void SelectFirstChild()
    {
        if (_selected is { } element && element.Children.Count > 0)
        {
            SetExpanded(element, true);
            Select(element.Children[0]);
        }
    }

    /// <summary>Moves the selection <paramref name="delta"/> rows through the visible tree.</summary>
    public void MoveSelection(int delta)
    {
        var nodes = Flatten();
        if (nodes.Count == 0) return;

        int index = IndexOf(nodes, _selected);
        if (index < 0)
        {
            Select(nodes[0].Element);
            return;
        }

        int next = Math.Clamp(index + delta, 0, nodes.Count - 1);
        if (next != index) Select(nodes[next].Element);
    }

    /// <summary>Right arrow: expand a collapsed node, else step into its first child.</summary>
    public void ExpandOrEnter()
    {
        if (_selected is not { } element) return;
        if (element.Children.Count == 0) return;
        if (!IsExpanded(element)) SetExpanded(element, true);
        else Select(element.Children[0]);
    }

    /// <summary>Left arrow: collapse an expanded node, else step out to its parent.</summary>
    public void CollapseOrExit()
    {
        if (_selected is not { } element) return;
        if (element.Children.Count > 0 && IsExpanded(element)) SetExpanded(element, false);
        else if (element.Parent is { } parent) Select(parent);
    }

    /// <summary>The topmost ancestor of <paramref name="element"/> (its tree's root).</summary>
    public static Element RootOf(Element element)
    {
        var current = element;
        while (current.Parent is { } parent) current = parent;
        return current;
    }
}
