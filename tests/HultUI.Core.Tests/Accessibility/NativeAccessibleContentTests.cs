using HultUI.Core.Accessibility;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using Xunit;

namespace HultUI.Core.Tests.Accessibility;

/// <summary>
/// An element whose content is someone else's — a web page — names the windows that content's
/// accessibility tree lives in. The tree carries that to the node, and says so when it changes:
/// the windows turn up after the element does, and a bridge that never heard would go on
/// showing the element empty.
/// </summary>
public class NativeAccessibleContentTests
{
    private readonly LayoutEngine _layout = new(new StubMeasureContext());

    /// <summary>A group that reports native content, as a web view does.</summary>
    private sealed class Hosting : BoxElement
    {
        public NativeAccessibleContent Content;
        internal override NativeAccessibleContent NativeAccessibleContent => Content;
    }

    private (BoxElement Root, Hosting Host) Tree()
    {
        var root = new BoxElement { Role = AccessibilityRole.Application };
        root.Box.Width = 400;
        root.Box.Height = 400;
        var host = new Hosting { Role = AccessibilityRole.Group };
        host.Box.Width = 200;
        host.Box.Height = 100;
        root.AddChild(host);
        _layout.PerformLayout(root, 400, 400);
        return (root, host);
    }

    [Fact]
    public void AnOrdinaryElement_HasNone()
    {
        var (root, _) = Tree();

        var tree = AccessibilityTreeBuilder.Build(root);

        Assert.True(tree.NativeContent.IsEmpty);
        Assert.True(tree.Children[0].NativeContent.IsEmpty);
    }

    [Fact]
    public void TheNode_CarriesWhatTheElementReports()
    {
        var (root, host) = Tree();
        host.Content = new NativeAccessibleContent(0x1234, 0x5678);

        var node = AccessibilityTreeBuilder.Build(root).Children[0];

        Assert.Equal(new NativeAccessibleContent(0x1234, 0x5678), node.NativeContent);
        Assert.False(node.NativeContent.IsEmpty);
    }

    [Fact]
    public void ContentArrivingLater_IsReportedAsAChange_AndOnlyOnce()
    {
        var (root, host) = Tree();
        var before = AccessibilityTreeBuilder.Build(root);

        host.Content = new NativeAccessibleContent(0x1234);
        var found = AccessibilityTreeBuilder.Build(root);
        var diff = AccessibilityTreeDiffer.Diff(before, found);

        var change = Assert.Single(diff.PropertyChanged);
        Assert.Equal(AccessibilityProperties.NativeContent, change.PropertyName);
        Assert.Equal(host.Id, change.Node.ElementId);

        // The hit-test window turning up is a change too; nothing new is not.
        host.Content = new NativeAccessibleContent(0x1234, 0x5678);
        var complete = AccessibilityTreeBuilder.Build(root);
        Assert.Single(AccessibilityTreeDiffer.Diff(found, complete).PropertyChanged);
        Assert.True(AccessibilityTreeDiffer.Diff(complete, AccessibilityTreeBuilder.Build(root)).IsEmpty);
    }
}
