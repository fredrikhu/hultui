using System.Linq;
using HultUI.Core.Builders;
using HultUI.Core.DevTools;
using HultUI.Core.Elements;
using Xunit;

namespace HultUI.Core.Tests.DevTools;

/// <summary>
/// The inspector's model: what the tree pane shows, how selection moves, and how the
/// picker commits. All of it is exercised against a real element tree — the panel and
/// the highlight are two renderings of exactly this state.
/// </summary>
public class ElementInspectorTests
{
    // root
    //  ├── header
    //  │    └── title (Text "Hello")
    //  └── body
    //       ├── first  (Text "One")
    //       └── second (Text "Two")
    private static (ElementInspector inspector, Element root, Element header, Element title,
        Element body, Element first, Element second) Build()
    {
        var title = Text.New("Hello");
        var header = Box.New().Children(title);
        var first = Text.New("One");
        var second = Text.New("Two");
        var body = Box.New().Children(first, second);
        var root = Box.New().Children(header, body);
        root.Name = "root";

        var inspector = new ElementInspector();
        inspector.SetRoots([root]);
        return (inspector, root, header, title, body, first, second);
    }

    [Fact]
    public void Flatten_ShowsOnlyTheRoot_WhenNothingIsExpanded()
    {
        var (inspector, root, _, _, _, _, _) = Build();

        var nodes = inspector.Flatten();

        var node = Assert.Single(nodes);
        Assert.Same(root, node.Element);
        Assert.True(node.HasChildren);
        Assert.False(node.IsExpanded);
    }

    [Fact]
    public void Flatten_DescendsIntoExpandedNodesOnly()
    {
        var (inspector, root, header, _, body, _, _) = Build();

        inspector.SetExpanded(root, true);

        var elements = inspector.Flatten().Select(n => n.Element).ToArray();
        Assert.Equal(new[] { root, header, body }, elements);
        // header is expandable but collapsed, so its title child stays hidden.
        Assert.All(inspector.Flatten().Skip(1), n => Assert.False(n.IsExpanded));
    }

    [Fact]
    public void Flatten_ReportsDepthForIndentation()
    {
        var (inspector, root, header, title, _, _, _) = Build();
        inspector.SetExpanded(root, true);
        inspector.SetExpanded(header, true);

        var nodes = inspector.Flatten();

        Assert.Equal(0, nodes.First(n => ReferenceEquals(n.Element, root)).Depth);
        Assert.Equal(1, nodes.First(n => ReferenceEquals(n.Element, header)).Depth);
        Assert.Equal(2, nodes.First(n => ReferenceEquals(n.Element, title)).Depth);
    }

    [Fact]
    public void Select_ExpandsTheAncestorsSoTheSelectionIsVisible()
    {
        var (inspector, root, header, title, _, _, _) = Build();

        inspector.Select(title);

        Assert.True(inspector.IsExpanded(root));
        Assert.True(inspector.IsExpanded(header));
        Assert.Contains(inspector.Flatten(), n => ReferenceEquals(n.Element, title));
    }

    [Fact]
    public void Pick_CommitsTheSelectionAndDisarmsThePicker()
    {
        var (inspector, _, _, title, _, _, _) = Build();
        inspector.SetPicking(true);
        inspector.SetHovered(title);

        inspector.Pick(title);

        Assert.Same(title, inspector.Selected);
        Assert.False(inspector.IsPicking);
        Assert.Null(inspector.Hovered);
    }

    [Fact]
    public void SetHovered_IsIgnoredUnlessThePickerIsArmed()
    {
        var (inspector, _, _, title, _, _, _) = Build();

        inspector.SetHovered(title);

        Assert.Null(inspector.Hovered);
    }

    [Fact]
    public void Highlighted_PrefersTheLivePreviewOverTheSelection()
    {
        var (inspector, _, _, title, _, first, _) = Build();
        inspector.Select(title);
        Assert.Same(title, inspector.Highlighted);

        inspector.SetPicking(true);
        inspector.SetHovered(first);

        Assert.Same(first, inspector.Highlighted);
    }

    [Fact]
    public void DisarmingThePicker_DropsThePreviewButKeepsTheSelection()
    {
        var (inspector, _, _, title, _, first, _) = Build();
        inspector.Select(title);
        inspector.SetPicking(true);
        inspector.SetHovered(first);

        inspector.SetPicking(false);

        Assert.Null(inspector.Hovered);
        Assert.Same(title, inspector.Selected);
        Assert.Same(title, inspector.Highlighted);
    }

    [Fact]
    public void MoveSelection_WalksTheVisibleTreeInDocumentOrder()
    {
        var (inspector, root, header, title, body, _, _) = Build();
        inspector.SetExpanded(root, true);
        inspector.SetExpanded(header, true);
        inspector.Select(root);

        inspector.MoveSelection(1);
        Assert.Same(header, inspector.Selected);

        inspector.MoveSelection(1);
        Assert.Same(title, inspector.Selected);

        // Past the last child of header, the next row is header's sibling — not a
        // wrap back to the top, and not a stop at the subtree boundary.
        inspector.MoveSelection(1);
        Assert.Same(body, inspector.Selected);
    }

    [Fact]
    public void MoveSelection_ClampsAtTheEndsInsteadOfWrapping()
    {
        var (inspector, root, _, _, _, _, _) = Build();
        inspector.Select(root);

        inspector.MoveSelection(-5);
        Assert.Same(root, inspector.Selected);

        inspector.MoveSelection(500);
        Assert.Same(root, inspector.Selected);   // only the root is visible
    }

    [Fact]
    public void MoveSelection_SelectsTheFirstRow_WhenNothingIsSelectedYet()
    {
        var (inspector, root, _, _, _, _, _) = Build();

        inspector.MoveSelection(1);

        Assert.Same(root, inspector.Selected);
    }

    [Fact]
    public void ExpandOrEnter_ExpandsFirst_ThenStepsIntoTheFirstChild()
    {
        var (inspector, root, header, _, _, _, _) = Build();
        inspector.Select(root);

        inspector.ExpandOrEnter();
        Assert.True(inspector.IsExpanded(root));
        Assert.Same(root, inspector.Selected);   // the first press only expands

        inspector.ExpandOrEnter();
        Assert.Same(header, inspector.Selected);
    }

    [Fact]
    public void CollapseOrExit_CollapsesFirst_ThenStepsOutToTheParent()
    {
        var (inspector, root, header, _, _, _, _) = Build();
        inspector.Select(header);
        inspector.SetExpanded(header, true);

        inspector.CollapseOrExit();
        Assert.False(inspector.IsExpanded(header));
        Assert.Same(header, inspector.Selected);

        inspector.CollapseOrExit();
        Assert.Same(root, inspector.Selected);
    }

    [Fact]
    public void Filter_ShowsMatchesAndTheAncestorsNeededToReachThem()
    {
        var (inspector, root, header, title, body, _, _) = Build();
        inspector.CollapseAll();

        inspector.Filter = "Hello";

        var elements = inspector.Flatten().Select(n => n.Element).ToArray();
        // The match plus its ancestor chain — and nothing from the unrelated subtree,
        // even though nothing was expanded.
        Assert.Equal(new[] { root, header, title }, elements);
        Assert.DoesNotContain(body, elements);
    }

    [Fact]
    public void Filter_MatchesTypeNamesToo()
    {
        var (inspector, _, _, _, _, _, _) = Build();

        inspector.Filter = "text";

        Assert.Equal(3, inspector.Flatten().Count(n => n.Element is TextElement));
    }

    [Fact]
    public void SelectionPath_RunsOutermostFirstAndEndsAtTheSelection()
    {
        var (inspector, root, header, title, _, _, _) = Build();
        inspector.Select(title);

        Assert.Equal(new[] { root, header, title }, inspector.SelectionPath());
    }

    [Fact]
    public void IsSelectionStale_ReportsAnElementThatLeftTheTree()
    {
        var (inspector, root, header, title, _, _, _) = Build();
        inspector.Select(title);
        Assert.False(inspector.IsSelectionStale);

        header.RemoveChild(title);

        // The selection is kept — its last known properties still read fine — but the
        // panel needs to be able to say the element is no longer live.
        Assert.True(inspector.IsSelectionStale);
        Assert.Same(title, inspector.Selected);
        Assert.NotNull(root);
    }

    [Fact]
    public void SetRoots_DropsExpansionState_WhenThePrimaryTreeIsReplaced()
    {
        var (inspector, root, _, _, _, _, _) = Build();
        inspector.SetExpanded(root, true);

        var other = Box.New().Children(Text.New("elsewhere"));
        inspector.SetRoots([other]);

        Assert.False(inspector.IsExpanded(root));
        Assert.Single(inspector.Flatten());
    }

    [Fact]
    public void SetRoots_KeepsExpansionState_WhenOverlayRootsComeAndGo()
    {
        var (inspector, root, _, _, _, _, _) = Build();
        inspector.SetExpanded(root, true);

        var overlay = Box.New().Children(Text.New("menu"));
        inspector.SetRoots([root, overlay]);

        Assert.True(inspector.IsExpanded(root));
        Assert.Contains(inspector.Flatten(), n => ReferenceEquals(n.Element, overlay));
    }

    [Fact]
    public void Changed_FiresForEveryStateTheRenderersReadFrom()
    {
        var (inspector, root, _, title, _, _, _) = Build();
        int changes = 0;
        inspector.Changed += () => changes++;

        inspector.Select(title);
        inspector.SetPicking(true);
        inspector.SetHovered(root);
        inspector.SetExpanded(root, false);
        inspector.Filter = "x";
        inspector.Clear();

        Assert.Equal(6, changes);
    }

    [Fact]
    public void Changed_DoesNotFireWhenNothingActuallyChanges()
    {
        var (inspector, _, _, title, _, _, _) = Build();
        inspector.Select(title);

        int changes = 0;
        inspector.Changed += () => changes++;

        inspector.Select(title);          // same selection
        inspector.SetPicking(false);      // already disarmed
        inspector.Filter = "";            // already empty

        Assert.Equal(0, changes);
    }
}
