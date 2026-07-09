using HultUI.Core.Accessibility;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using Xunit;

namespace HultUI.Core.Tests.Accessibility;

public class AccessibilityTreeBuilderTests
{
    private readonly LayoutEngine _layout = new(new StubMeasureContext());

    private void Layout(Element root, float w = 400, float h = 400)
    {
        _layout.PerformLayout(root, w, h);
    }

    [Fact]
    public void TextElement_InfersLabelRole()
    {
        var text = TextElement.New("Hello");
        var container = BoxElement.New();
        container.AddChild(text);
        Layout(container);

        var role = AccessibilityTreeBuilder.ResolveRole(text);
        Assert.Equal(AccessibilityRole.Label, role);
    }

    [Fact]
    public void ImageElement_InfersImageRole()
    {
        var img = new ImageElement();
        var role = AccessibilityTreeBuilder.ResolveRole(img);
        Assert.Equal(AccessibilityRole.Image, role);
    }

    [Fact]
    public void ScrollViewElement_InfersScrollViewRole()
    {
        var sv = ScrollViewElement.New();
        var role = AccessibilityTreeBuilder.ResolveRole(sv);
        Assert.Equal(AccessibilityRole.ScrollView, role);
    }

    [Fact]
    public void CanvasElement_InfersGroupRole()
    {
        var canvas = CanvasElement.New();
        var role = AccessibilityTreeBuilder.ResolveRole(canvas);
        Assert.Equal(AccessibilityRole.Group, role);
    }

    [Fact]
    public void BoxWithClickHandler_InfersButtonRole()
    {
        var box = BoxElement.New();
        box.On<ClickEvent>(_ => { });
        var role = AccessibilityTreeBuilder.ResolveRole(box);
        Assert.Equal(AccessibilityRole.Button, role);
    }

    [Fact]
    public void PlainBox_InfersNoneRole()
    {
        var box = BoxElement.New();
        var role = AccessibilityTreeBuilder.ResolveRole(box);
        Assert.Equal(AccessibilityRole.None, role);
    }

    [Fact]
    public void ExplicitRole_OverridesInference()
    {
        var text = TextElement.New("Click me");
        text.Role = AccessibilityRole.Button;
        var role = AccessibilityTreeBuilder.ResolveRole(text);
        Assert.Equal(AccessibilityRole.Button, role);
    }

    [Fact]
    public void ResolveName_UsesAccessibleName_OverTextContent()
    {
        var text = TextElement.New("Hello");
        text.AccessibleName = "Greeting label";
        var name = AccessibilityTreeBuilder.ResolveName(text);
        Assert.Equal("Greeting label", name);
    }

    [Fact]
    public void ResolveName_FallsBackToTextContent()
    {
        var text = TextElement.New("Hello");
        var name = AccessibilityTreeBuilder.ResolveName(text);
        Assert.Equal("Hello", name);
    }

    [Fact]
    public void ResolveName_FallsBackToElementName()
    {
        var box = BoxElement.New();
        box.Name = "sidebar";
        var name = AccessibilityTreeBuilder.ResolveName(box);
        Assert.Equal("sidebar", name);
    }

    [Fact]
    public void Build_CreatesTreeFromElements()
    {
        var root = BoxElement.New();
        root.Role = AccessibilityRole.Application;
        root.Box.Width = 400;
        root.Box.Height = 400;

        var text = TextElement.New("Hello");
        root.AddChild(text);

        Layout(root);

        var tree = AccessibilityTreeBuilder.Build(root);

        Assert.Equal(AccessibilityRole.Application, tree.Role);
        Assert.Single(tree.Children);
        Assert.Equal(AccessibilityRole.Label, tree.Children[0].Role);
        Assert.Equal("Hello", tree.Children[0].Name);
    }

    [Fact]
    public void Build_FlattensDecorativeContainers()
    {
        // root(Application) > wrapper(plain box) > text(Label)
        // The plain box should be flattened out
        var root = BoxElement.New();
        root.Role = AccessibilityRole.Application;
        root.Box.Width = 400;
        root.Box.Height = 400;

        var wrapper = BoxElement.New(); // no role, no name — decorative
        var text = TextElement.New("Content");
        wrapper.AddChild(text);
        root.AddChild(wrapper);

        Layout(root);

        var tree = AccessibilityTreeBuilder.Build(root);

        // Text should be promoted directly under root, wrapper flattened
        Assert.Single(tree.Children);
        Assert.Equal(AccessibilityRole.Label, tree.Children[0].Role);
        Assert.Equal("Content", tree.Children[0].Name);
    }

    [Fact]
    public void Build_PreservesSemanticContainers()
    {
        var root = BoxElement.New();
        root.Role = AccessibilityRole.Application;
        root.Box.Width = 400;
        root.Box.Height = 400;

        var nav = BoxElement.New();
        nav.Role = AccessibilityRole.Group;
        nav.AccessibleName = "Navigation";

        var text = TextElement.New("Home");
        nav.AddChild(text);
        root.AddChild(nav);

        Layout(root);

        var tree = AccessibilityTreeBuilder.Build(root);

        Assert.Single(tree.Children);
        var navNode = tree.Children[0];
        Assert.Equal(AccessibilityRole.Group, navNode.Role);
        Assert.Equal("Navigation", navNode.Name);
        Assert.Single(navNode.Children);
        Assert.Equal("Home", navNode.Children[0].Name);
    }

    [Fact]
    public void Build_IncludesClickableBoxAsButton()
    {
        var root = BoxElement.New();
        root.Role = AccessibilityRole.Application;
        root.Box.Width = 400;
        root.Box.Height = 400;

        var button = BoxElement.New();
        button.AccessibleName = "Submit";
        button.On<ClickEvent>(_ => { });
        root.AddChild(button);

        Layout(root);

        var tree = AccessibilityTreeBuilder.Build(root);

        Assert.Single(tree.Children);
        Assert.Equal(AccessibilityRole.Button, tree.Children[0].Role);
        Assert.Equal("Submit", tree.Children[0].Name);
    }

    [Fact]
    public void ResolveName_Button_DerivesNameFromChildText()
    {
        var button = BoxElement.New();
        button.On<ClickEvent>(_ => { });
        button.AddChild(TextElement.New("Click Me"));

        var name = AccessibilityTreeBuilder.ResolveName(button);
        Assert.Equal("Click Me", name);
    }

    [Fact]
    public void Build_TextOnlyButton_TakesChildTextAsNameAndIsLeaf()
    {
        var root = BoxElement.New();
        root.Role = AccessibilityRole.Application;
        root.Box.Width = 400;
        root.Box.Height = 400;

        var button = BoxElement.New();
        button.On<ClickEvent>(_ => { });
        button.AddChild(TextElement.New("Click Me"));
        root.AddChild(button);

        Layout(root);

        var tree = AccessibilityTreeBuilder.Build(root);

        Assert.Single(tree.Children);
        var btn = tree.Children[0];
        Assert.Equal(AccessibilityRole.Button, btn.Role);
        Assert.Equal("Click Me", btn.Name);
        // The text was folded into the button's name, not exposed as a child.
        Assert.Empty(btn.Children);
    }

    [Fact]
    public void Build_ExplicitlyNamedButton_KeepsChildText()
    {
        var root = BoxElement.New();
        root.Role = AccessibilityRole.Application;
        root.Box.Width = 400;
        root.Box.Height = 400;

        var button = BoxElement.New();
        button.AccessibleName = "Save document";
        button.On<ClickEvent>(_ => { });
        button.AddChild(TextElement.New("Save"));
        root.AddChild(button);

        Layout(root);

        var tree = AccessibilityTreeBuilder.Build(root);

        var btn = tree.Children[0];
        Assert.Equal("Save document", btn.Name);
        // Explicit name may differ from the text, so the label is preserved.
        Assert.Single(btn.Children);
        Assert.Equal("Save", btn.Children[0].Name);
    }

    [Fact]
    public void Build_ButtonWithNonTextContent_DerivesNameButKeepsChildren()
    {
        var root = BoxElement.New();
        root.Role = AccessibilityRole.Application;
        root.Box.Width = 400;
        root.Box.Height = 400;

        var button = BoxElement.New();
        button.On<ClickEvent>(_ => { });
        var icon = new ImageElement();
        icon.AccessibleName = "icon";
        button.AddChild(icon);
        button.AddChild(TextElement.New("Delete"));
        root.AddChild(button);

        Layout(root);

        var tree = AccessibilityTreeBuilder.Build(root);

        var btn = tree.Children[0];
        Assert.Equal(AccessibilityRole.Button, btn.Role);
        Assert.Equal("Delete", btn.Name);
        // A non-label descendant (the image) means children are preserved.
        Assert.Contains(btn.Children, c => c.Role == AccessibilityRole.Image);
    }

    [Fact]
    public void Build_ButtonWithText_ThroughDecorativeWrapper_DerivesName()
    {
        var root = BoxElement.New();
        root.Role = AccessibilityRole.Application;
        root.Box.Width = 400;
        root.Box.Height = 400;

        var button = BoxElement.New();
        button.On<ClickEvent>(_ => { });
        var wrapper = BoxElement.New(); // decorative
        wrapper.AddChild(TextElement.New("Submit"));
        button.AddChild(wrapper);
        root.AddChild(button);

        Layout(root);

        var tree = AccessibilityTreeBuilder.Build(root);

        var btn = tree.Children[0];
        Assert.Equal("Submit", btn.Name);
        Assert.Empty(btn.Children);
    }

    private static AccessibilityNode? FindByName(AccessibilityNode node, string name)
    {
        if (node.Name == name) return node;
        foreach (var child in node.Children)
            if (FindByName(child, name) is { } found) return found;
        return null;
    }

    [Fact]
    public void Build_ScrollOffset_ShiftsDescendantBoundsButNotScrollView()
    {
        var root = BoxElement.New();
        root.Role = AccessibilityRole.Application;
        root.Box.Width = 200;
        root.Box.Height = 400;

        var scroll = ScrollViewElement.New();
        scroll.AccessibleName = "viewport";
        scroll.Box.Width = 200;
        scroll.Box.Height = 100;

        var child = BoxElement.New();
        child.AccessibleName = "row";
        child.Box.Width = 200;
        child.Box.Height = 500; // taller than the viewport, so it can scroll
        scroll.AddChild(child);
        root.AddChild(scroll);

        Layout(root, 200, 400);
        scroll.UpdateContentSize();

        var before = AccessibilityTreeBuilder.Build(root);
        var childBefore = FindByName(before, "row")!.BoundingRect;
        var viewportBefore = FindByName(before, "viewport")!.BoundingRect;

        scroll.ScrollOffsetY = 50;
        Assert.Equal(50, scroll.ScrollOffsetY); // sanity: the offset actually took

        var after = AccessibilityTreeBuilder.Build(root);
        var childAfter = FindByName(after, "row")!.BoundingRect;
        var viewportAfter = FindByName(after, "viewport")!.BoundingRect;

        // The scrolled content moves up by the offset; the viewport stays put.
        Assert.Equal(childBefore.Y - 50, childAfter.Y, 3);
        Assert.Equal(childBefore.X, childAfter.X, 3);
        Assert.Equal(viewportBefore.Y, viewportAfter.Y, 3);
    }

    [Fact]
    public void ScrollOffsetChange_NotifiesBoundingRect()
    {
        var scroll = ScrollViewElement.New();
        scroll.Box.Width = 200;
        scroll.Box.Height = 100;
        var child = BoxElement.New();
        child.Box.Width = 200;
        child.Box.Height = 500;
        scroll.AddChild(child);
        Layout(scroll, 200, 100);
        scroll.UpdateContentSize();

        var observer = new RecordingObserver();
        scroll.AccessibilityObserver = observer;

        scroll.ScrollOffsetY = 40;

        Assert.Contains(observer.Changes,
            c => c.Property == AccessibilityProperties.BoundingRect);
    }

    private sealed class RecordingObserver : IElementPropertyObserver
    {
        public List<(Element Element, string Property)> Changes { get; } = [];

        public void OnElementPropertyChanged(Element element, string propertyName)
            => Changes.Add((element, propertyName));
    }

    [Fact]
    public void Build_SetsCorrectBoundingRects()
    {
        var root = BoxElement.New();
        root.Box.Width = 400;
        root.Box.Height = 300;
        root.Role = AccessibilityRole.Application;

        Layout(root);

        var tree = AccessibilityTreeBuilder.Build(root);

        Assert.Equal(400, tree.BoundingRect.Width);
        Assert.Equal(300, tree.BoundingRect.Height);
    }

    [Fact]
    public void Build_DeepNesting_FlattensMultipleLevels()
    {
        // root > wrapper1 > wrapper2 > wrapper3 > text
        // All wrappers are decorative, text should end up directly under root
        var root = BoxElement.New();
        root.Role = AccessibilityRole.Application;
        root.Box.Width = 400;
        root.Box.Height = 400;

        var w1 = BoxElement.New();
        var w2 = BoxElement.New();
        var w3 = BoxElement.New();
        var text = TextElement.New("Deep");

        w3.AddChild(text);
        w2.AddChild(w3);
        w1.AddChild(w2);
        root.AddChild(w1);

        Layout(root);

        var tree = AccessibilityTreeBuilder.Build(root);

        Assert.Single(tree.Children);
        Assert.Equal(AccessibilityRole.Label, tree.Children[0].Role);
        Assert.Equal("Deep", tree.Children[0].Name);
    }

    [Fact]
    public void IsSemantic_TrueForExplicitRole()
    {
        var box = BoxElement.New();
        box.Role = AccessibilityRole.Group;
        Assert.True(AccessibilityTreeBuilder.IsSemantic(box));
    }

    [Fact]
    public void IsSemantic_TrueForAccessibleName()
    {
        var box = BoxElement.New();
        box.AccessibleName = "Nav";
        Assert.True(AccessibilityTreeBuilder.IsSemantic(box));
    }

    [Fact]
    public void IsSemantic_TrueForAccessibleDescription()
    {
        var box = BoxElement.New();
        box.AccessibleDescription = "Main navigation area";
        Assert.True(AccessibilityTreeBuilder.IsSemantic(box));
    }

    [Fact]
    public void IsSemantic_FalseForPlainBox()
    {
        var box = BoxElement.New();
        Assert.False(AccessibilityTreeBuilder.IsSemantic(box));
    }

    [Fact]
    public void Build_MixedSemanticAndDecorative()
    {
        // root(Application) > [decorative, button, decorative > text]
        var root = BoxElement.New();
        root.Role = AccessibilityRole.Application;
        root.Box.Width = 400;
        root.Box.Height = 400;

        var decorative1 = BoxElement.New(); // will be flattened (no children → nothing promoted)
        var button = BoxElement.New();
        button.On<ClickEvent>(_ => { });
        button.AccessibleName = "OK";

        var decorative2 = BoxElement.New(); // will be flattened, promoting text
        var text = TextElement.New("Footer");
        decorative2.AddChild(text);

        root.AddChild(decorative1);
        root.AddChild(button);
        root.AddChild(decorative2);

        Layout(root);

        var tree = AccessibilityTreeBuilder.Build(root);

        // decorative1 has no children → nothing promoted
        // button stays as Button
        // decorative2 flattened, text promoted
        Assert.Equal(2, tree.Children.Count);
        Assert.Equal(AccessibilityRole.Button, tree.Children[0].Role);
        Assert.Equal("OK", tree.Children[0].Name);
        Assert.Equal(AccessibilityRole.Label, tree.Children[1].Role);
        Assert.Equal("Footer", tree.Children[1].Name);
    }
}
