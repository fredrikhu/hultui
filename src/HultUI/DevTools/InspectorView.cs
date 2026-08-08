using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.DevTools;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Core.Styling;

// Inside the HultUI namespace, a bare `Text` binds to the sibling HultUI.Text
// namespace rather than the builder, and a `Text` alias loses to it by lookup order.
using TextEl = HultUI.Core.Builders.Text;

namespace HultUI.DevTools;

/// <summary>
/// The inspector panel, built from ordinary HultUI elements and hosted in its own
/// window. Everything it shows comes from <see cref="ElementInspector"/>; it holds no
/// state of its own beyond scroll position, so a rebuild is always safe.
/// </summary>
internal sealed class InspectorView : Component
{
    private const float RowHeight = 22f;
    private const float TreeWidth = 380f;

    private readonly ElementInspector _inspector;
    private readonly Func<Theme> _theme;
    private readonly Action _onCopy;

    private readonly Ref<VirtualListElement<InspectorNode>> _tree = new();
    private IReadOnlyList<InspectorNode> _nodes = [];
    private int _selectedIndex = -1;
    private int _revealedIndex = -1;

    public InspectorView(ElementInspector inspector, Func<Theme> theme, Action onCopy)
    {
        _inspector = inspector;
        _theme = theme;
        _onCopy = onCopy;
    }

    /// <summary>Rebuilds the panel. Called by the host whenever the inspected tree may have moved on.</summary>
    public void Refresh() => MarkDirty();

    public override Element Build()
    {
        var t = _theme();
        var palette = InspectorPalette.From(t);

        _nodes = _inspector.Flatten();
        _selectedIndex = ElementInspector.IndexOf(_nodes, _inspector.Selected);

        return Flex.Column()
            .Background(palette.Background)
            .TextColor(palette.Text)
            .FontFamily(t.FontFamily)
            .FontSize(12f)
            .Width(Dimension.Percent(100))
            .Height(Dimension.Percent(100))
            .Children(
                BuildToolbar(palette),
                BuildBreadcrumb(palette),
                Flex.Row().FlexGrow(1).Children(
                    BuildTree(palette),
                    BuildProperties(palette)),
                BuildStatusBar(palette));
    }

    protected internal override void OnAfterBuild()
    {
        // Scroll the selection into view when it changes — a selection made by the
        // picker or by an arrow key is usually somewhere off-screen. Only on change,
        // so the user can freely scroll away from it afterwards.
        if (_selectedIndex < 0 || _selectedIndex == _revealedIndex) return;
        _revealedIndex = _selectedIndex;
        _tree.Current?.ScrollRangeIntoView(_selectedIndex * RowHeight, RowHeight);
    }

    // ──── Toolbar ────

    private Element BuildToolbar(InspectorPalette p)
    {
        var filter = TextInput.New(_inspector.Filter)
            .PlaceholderText("Filter by type or name")
            .OnTextChanged(text => _inspector.Filter = text)
            .FlexGrow(1);

        return Flex.Row(gap: 6)
            .Padding(new Thickness(6, 8))
            .AlignItems(AlignItems.Center)
            .FlexShrink(0)
            .Background(p.Surface)
            .Border(new Thickness(0, 0, 1, 0), p.Border)
            .Children(
                ToolButton(p, "⊹ Pick", "Ctrl+Shift+C — click an element in the app to select it",
                    active: _inspector.IsPicking,
                    onClick: () => _inspector.SetPicking(!_inspector.IsPicking)),
                filter,
                ToolButton(p, "Expand", "Expand the tree (6 levels)", false, () => _inspector.ExpandAll()),
                ToolButton(p, "Collapse", "Collapse the whole tree", false, _inspector.CollapseAll),
                ToolButton(p, "Copy", "Copy the selected element's properties as text", false, _onCopy));
    }

    private static Element ToolButton(InspectorPalette p, string label, string tooltip, bool active, Action onClick)
    {
        var background = active ? p.Accent : p.Control;
        return Box.New()
            .Padding(new Thickness(4, 9))
            .CornerRadius(4)
            .Background(background)
            // A chip blended off the background alone reads as a smudge on some
            // themes; the outline is what makes it read as a control.
            .Border(new Thickness(1), active ? p.Accent : p.Border)
            .TextColor(active ? p.AccentText : p.Text)
            .Cursor(CursorType.Pointer)
            .Tooltip(tooltip)
            .AccessibleRole(Core.Accessibility.AccessibilityRole.Button)
            .AccessibleName(label)
            .OnHover(e => e.Background(active ? p.Accent : p.ControlHover))
            .OnClick(_ => onClick())
            .Children(TextEl.New(label).FontSize(11f));
    }

    // ──── Breadcrumb ────

    private Element BuildBreadcrumb(InspectorPalette p)
    {
        var path = _inspector.SelectionPath();
        if (path.Count == 0)
        {
            return Box.New().Padding(new Thickness(4, 8)).FlexShrink(0)
                .Background(p.Surface)
                .Border(new Thickness(0, 0, 1, 0), p.Border)
                .Children(TextEl.New("Nothing selected — press Pick, then click an element in the app.")
                    .FontSize(11f).TextColor(p.Muted));
        }

        // Deep trees make an unbounded crumb trail useless; keep the tail, where
        // the selection actually lives, and mark that the head was dropped.
        const int MaxCrumbs = 8;
        var crumbs = new List<Element>();
        int start = Math.Max(0, path.Count - MaxCrumbs);
        if (start > 0) crumbs.Add(TextEl.New("…").FontSize(11f).TextColor(p.Muted));

        for (int i = start; i < path.Count; i++)
        {
            var element = path[i];
            if (crumbs.Count > 0)
                crumbs.Add(TextEl.New("›").FontSize(11f).TextColor(p.Muted));

            bool isLast = i == path.Count - 1;
            crumbs.Add(Box.New()
                .Padding(new Thickness(1, 4))
                .CornerRadius(3)
                .Cursor(CursorType.Pointer)
                .OnHover(e => e.Background(p.ControlHover))
                .OnClick(_ => _inspector.Select(element))
                .Children(TextEl.New(ElementProperties.TypeLabel(element))
                    .FontSize(11f)
                    .TextColor(isLast ? p.Text : p.Muted)));
        }

        return Flex.Row(gap: 2)
            .Padding(new Thickness(4, 8))
            .AlignItems(AlignItems.Center)
            .FlexWrap(FlexWrap.Wrap)
            .FlexShrink(0)
            .Background(p.Surface)
            .Border(new Thickness(0, 0, 1, 0), p.Border)
            .Children([.. crumbs]);
    }

    // ──── Element tree ────

    private Element BuildTree(InspectorPalette p)
    {
        var list = VirtualListElement<InspectorNode>.New()
            .ItemHeight(RowHeight)
            .Items(_nodes)
            .RenderWith(node => TreeRow(p, node))
            .AccessibleItemLabel(node => ElementProperties.Summarize(node.Element))
            .SelectedIndex(_selectedIndex)
            .OnActivateItem(node => _inspector.Select(node.Element))
            .FlexGrow(1);
        list.WithRef(_tree);

        return Flex.Column()
            .Width(TreeWidth)
            .FlexShrink(0)
            .Border(new Thickness(0, 1, 0, 0), p.Border)
            .Children(list);
    }

    private Element TreeRow(InspectorPalette p, InspectorNode node)
    {
        var element = node.Element;
        bool isSelected = ReferenceEquals(element, _inspector.Selected);
        bool isHighlighted = ReferenceEquals(element, _inspector.Hovered);

        var twisty = node.HasChildren
            ? Box.New()
                .Width(14).AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center)
                .Cursor(CursorType.Pointer)
                .OnClick(e =>
                {
                    // Toggling must not also select: the twisty is a disclosure
                    // control, and the click would otherwise bubble to the row.
                    e.StopPropagation = true;
                    _inspector.ToggleExpanded(element);
                })
                .Children(TextEl.New(node.IsExpanded ? "▾" : "▸").FontSize(9f).TextColor(p.Muted))
            : Box.New().Width(14);

        var descriptor = ElementProperties.Descriptor(element);
        var size = $"{element.Layout.Width:0.#}×{element.Layout.Height:0.#}";

        var row = Flex.Row(gap: 4)
            .Height(RowHeight)
            .Padding(new Thickness(0, 6, 0, 6 + node.Depth * 12))
            .AlignItems(AlignItems.Center)
            .Cursor(CursorType.Pointer)
            .OnClick(_ => _inspector.Select(element))
            .Children(
                twisty,
                TextEl.New(ElementProperties.TypeLabel(element))
                    .FontSize(11f)
                    .TextColor(isSelected ? p.AccentText : p.Text)
                    .TextOverflow(TextOverflow.Ellipsis)
                    .MaxLines(1),
                TextEl.New(descriptor)
                    .FontSize(11f)
                    .TextColor(isSelected ? p.AccentText : p.Accent)
                    .TextOverflow(TextOverflow.Ellipsis)
                    .MaxLines(1)
                    .FlexShrink(1),
                Box.New().FlexGrow(1),
                TextEl.New(size)
                    .FontSize(10f)
                    .TextColor(isSelected ? p.AccentText : p.Muted)
                    .FlexShrink(0));

        if (isSelected) row.Background(p.Accent);
        else if (isHighlighted) row.Background(p.PickTint);
        else row.OnHover(e => e.Background(p.RowHover));

        return row;
    }

    // ──── Property pane ────

    private Element BuildProperties(InspectorPalette p)
    {
        var content = Flex.Column(gap: 0).Padding(8);

        if (_inspector.Selected is not { } selected)
        {
            content.Children(TextEl.New("No element selected.").FontSize(11f).TextColor(p.Muted));
        }
        else
        {
            var blocks = new List<Element>();
            if (_inspector.IsSelectionStale)
            {
                blocks.Add(Box.New()
                    .Padding(new Thickness(4, 8)).CornerRadius(4).Margin(new Thickness(0, 0, 8, 0))
                    .Background(p.WarningTint)
                    .Children(TextEl.New("This element was removed from the tree — values are its last known state.")
                        .FontSize(11f).TextColor(p.Warning)));
            }

            foreach (var group in ElementProperties.Describe(selected))
            {
                blocks.Add(TextEl.New(group.Name)
                    .FontSize(11f)
                    .FontWeight(FontWeight.SemiBold)
                    .TextColor(p.Accent)
                    .Margin(new Thickness(10, 0, 4, 0)));

                foreach (var row in group.Rows)
                {
                    blocks.Add(Flex.Row(gap: 8).AlignItems(AlignItems.FlexStart).Children(
                        TextEl.New(row.Name).FontSize(11f).TextColor(p.Muted).Width(112).FlexShrink(0),
                        TextEl.New(row.Value).FontSize(11f).FlexShrink(1)));
                }
            }

            content.Children([.. blocks]);
        }

        return ScrollView.Vertical().FlexGrow(1).Children(content);
    }

    // ──── Status bar ────

    private Element BuildStatusBar(InspectorPalette p)
    {
        string status = _inspector.IsPicking
            ? "Picking — click an element in the app window, or press Esc to cancel."
            : $"{_nodes.Count} node{(_nodes.Count == 1 ? "" : "s")} shown · ↑↓ move · ←→ collapse/expand · Esc clears";

        return Box.New()
            .Padding(new Thickness(4, 8))
            .FlexShrink(0)
            .Background(p.Surface)
            .Border(new Thickness(1, 0, 0, 0), p.Border)
            .Children(TextEl.New(status).FontSize(10f).TextColor(p.Muted).MaxLines(1)
                .TextOverflow(TextOverflow.Ellipsis));
    }
}
