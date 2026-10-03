using HultUI.Core.Accessibility;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using Xunit;

namespace HultUI.Core.Tests.Accessibility;

/// <summary>
/// Seam W11: content an element describes with data (a web page's tree) becomes nodes under
/// the element's own, with ids of their own, bounds in layout units, and offscreen marked.
/// </summary>
public class AccessibleContentTests
{
    internal sealed class Described : BoxElement, IAccessibleContentSource
    {
        public IReadOnlyList<AccessibleContentNode> Content = [];
        public float Scale = 2f;
        public readonly List<(int Key, AccessibleContentAction Action, string? Value)> Actions = [];
        public IReadOnlyList<AccessibleContentNode> AccessibleContent => Content;
        public float AccessibleContentScale => Scale;
        public void PerformAccessibleAction(int key, AccessibleContentAction action, string? value) => Actions.Add((key, action, value));
    }

    private static readonly AccessibleContentNode Page = new(1, AccessibilityRole.Document, "A page", new Rect(0, 0, 400, 300))
    {
        Children =
        [
            new(7, AccessibilityRole.Heading, "Settings", new Rect(20, 20, 200, 40)),
            new(-5, AccessibilityRole.Button, "Press me", new Rect(20, 100, 120, 40)) { IsFocusable = true, IsFocused = true },
            new(9, AccessibilityRole.Checkbox, "Remember me", new Rect(20, 600, 40, 40)) { IsToggled = false },
        ],
    };

    private static (Described Element, AccessibilityNode Tree) Build(IReadOnlyList<AccessibleContentNode> content, float scale = 2f)
    {
        var element = new Described { Content = content, Scale = scale, AccessibleName = "Web view" };
        element.Box.Width = 200;
        element.Box.Height = 150;
        element.Box.Padding = new Thickness(10);
        var root = BoxElement.New();
        root.Box.Width = 400;
        root.Box.Height = 300;
        root.AddChild(element);
        new LayoutEngine(new StubMeasureContext()).PerformLayout(root, 400, 300);
        return (element, AccessibilityTreeBuilder.Build(root));
    }

    [Fact]
    public void ContentIds_RoundTrip_AndCollideWithNothing()
    {
        var owner = new ElementId(12345);
        foreach (var key in new[] { 0, 1, -1, int.MaxValue, int.MinValue, -1000000009 })
        {
            var id = ContentNodeId.For(owner, key);
            Assert.True(ContentNodeId.TryDecode(id, out var o, out var k));
            Assert.Equal((owner, key), (o, k));
            Assert.False(VirtualItemId.TryDecode(id, out _, out _));
        }
        Assert.False(ContentNodeId.TryDecode(VirtualItemId.For(owner, 3), out _, out _));
        Assert.False(ContentNodeId.TryDecode(owner, out _, out _));
    }

    [Fact]
    public void TheContent_IsTheElementsChildren_InLayoutUnits()
    {
        var (element, tree) = Build([Page]);
        var node = tree.Children.Single(n => n.Name == "Web view");
        var page = Assert.Single(node.Children);
        Assert.Equal(AccessibilityRole.Document, page.Role);
        Assert.Equal(ContentNodeId.For(element.Id, 1), page.ElementId);
        // Device pixels at 2 per layout unit, from the content box at (10, 10).
        Assert.Equal(new Rect(10, 10, 200, 150), page.BoundingRect);

        var button = page.Children.Single(n => n.Role == AccessibilityRole.Button);
        Assert.Equal(ContentNodeId.For(element.Id, -5), button.ElementId);
        Assert.Equal(new Rect(20, 60, 60, 20), button.BoundingRect);
        Assert.True(button.IsFocusable);
        Assert.True(button.IsFocused);
        Assert.Equal(["Settings", "Press me", "Remember me"], page.Children.Select(c => c.Name));
    }

    [Fact]
    public void ContentOutsideTheElement_IsOffscreen()
    {
        var (_, tree) = Build([Page]);
        var page = tree.Children.Single(n => n.Name == "Web view").Children.Single();
        Assert.False(page.Children.Single(n => n.Name == "Press me").IsOffscreen);
        Assert.True(page.Children.Single(n => n.Name == "Remember me").IsOffscreen);   // 300 layout units down, the element is 150 high
    }

    [Fact]
    public void AChangedNode_IsAPropertyChange_NotANewNode()
    {
        var (element, before) = Build([Page]);
        element.Content =
        [
            Page with { Children = [Page.Children[0], Page.Children[1], Page.Children[2] with { IsToggled = true }] },
        ];
        var after = AccessibilityTreeBuilder.Build(element.Parent!);
        var diff = AccessibilityTreeDiffer.Diff(before, after);
        Assert.Empty(diff.Added);
        Assert.Empty(diff.Removed);
        Assert.Contains(diff.PropertyChanged, c => c.Node.Name == "Remember me" && c.PropertyName == AccessibilityProperties.IsToggled);
    }

    [Fact]
    public void NoContent_NoChildren()
    {
        var (_, tree) = Build([]);
        Assert.Empty(tree.Children.Single(n => n.Name == "Web view").Children);
    }

    [Fact]
    public void Clients_FollowTheBridge_UnlessForced()
    {
        var seen = new List<bool>();
        void Seen(bool active) => seen.Add(active);
        AccessibilityClients.ActiveChanged += Seen;
        var forced = AccessibilityClients.Force;
        try
        {
            AccessibilityClients.Force = null;
            AccessibilityClients.Report(false);
            seen.Clear();
            AccessibilityClients.Report(true);
            Assert.True(AccessibilityClients.Active);
            AccessibilityClients.Force = false;
            Assert.False(AccessibilityClients.Active);
            AccessibilityClients.Report(false);   // no change while forced
            AccessibilityClients.Force = null;
            Assert.False(AccessibilityClients.Active);
            Assert.Equal([true, false], seen);
        }
        finally
        {
            AccessibilityClients.ActiveChanged -= Seen;
            AccessibilityClients.Report(false);
            AccessibilityClients.Force = forced;
        }
    }
}
