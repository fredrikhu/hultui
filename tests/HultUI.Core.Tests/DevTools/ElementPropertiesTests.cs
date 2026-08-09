using System.Linq;
using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.DevTools;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using Xunit;

namespace HultUI.Core.Tests.DevTools;

/// <summary>
/// What the inspector reports about an element. The load-bearing property is that
/// describing an element is a pure read: a debugger that allocates style objects on,
/// or dirties, the tree it is describing changes the behaviour under investigation.
/// </summary>
public class ElementPropertiesTests
{
    private sealed class Sidebar : Component
    {
        public override Element Build() => Box.New();
    }

    // ──── Labels ────

    [Fact]
    public void TypeLabel_DropsTheElementSuffix()
    {
        Assert.Equal("Box", ElementProperties.TypeLabel(Box.New()));
        Assert.Equal("Text", ElementProperties.TypeLabel(Text.New("hi")));
    }

    [Fact]
    public void TypeLabel_SpellsOutGenerics()
    {
        var list = VirtualListElement<string>.New();

        Assert.Equal("VirtualList<String>", ElementProperties.TypeLabel(list));
    }

    [Fact]
    public void TypeLabel_ReportsTheComponent_NotTheHost()
    {
        var host = new Sidebar().Mount();

        // "ComponentHost" is framework machinery; the app author wrote "Sidebar".
        Assert.Equal("Sidebar", ElementProperties.TypeLabel(host));
    }

    [Fact]
    public void Descriptor_PrefersName_ThenAutomationId_ThenText()
    {
        // Written fluently on purpose: `Name` is also a property on Element, so this
        // only compiles if the extension is reached — see ElementExtensions.Name.
        var withName = Text.New("content").Name("greeting");
        Assert.Equal("#greeting", ElementProperties.Descriptor(withName));

        var withId = Text.New("content").AutomationId("greeting-id");
        Assert.Equal("#greeting-id", ElementProperties.Descriptor(withId));

        Assert.Equal("\"content\"", ElementProperties.Descriptor(Text.New("content")));
        Assert.Equal("", ElementProperties.Descriptor(Box.New()));
    }

    [Fact]
    public void Descriptor_FlattensAndElidesLongText()
    {
        var text = Text.New("line one\nline two " + new string('x', 80));

        var descriptor = ElementProperties.Descriptor(text);

        Assert.DoesNotContain('\n', descriptor);
        Assert.Contains('↵', descriptor);
        Assert.EndsWith("…\"", descriptor);
    }

    // ──── Purity ────

    [Fact]
    public void Describe_DoesNotAllocateStyleObjectsOnTheElement()
    {
        var element = Box.New();

        ElementProperties.Describe(element);

        // Element.Flex/Box/Position/Grid allocate on first *read* of the public
        // getters. Describing must go through the internal *Read accessors instead,
        // or merely looking at a 10k-element tree would allocate five style objects
        // per element. The shared defaults are the tell: a fresh element that was
        // only described still reports defaults, and the layout pass still sees
        // "this element never set anything".
        Assert.Equal(FlexDirection.Column, element.FlexRead.Direction);
        Assert.Same(FlexStyle.Default, element.FlexRead);
        Assert.Same(BoxStyle.Default, element.BoxRead);
        Assert.Same(PositionStyle.Default, element.PositionRead);
        Assert.Same(GridItemStyle.Default, element.GridItemRead);
    }

    [Fact]
    public void Describe_DoesNotDirtyTheElement()
    {
        var element = Box.New();
        element.DirtyFlags = DirtyFlags.None;

        ElementProperties.Describe(element);

        Assert.Equal(DirtyFlags.None, element.DirtyFlags);
    }

    // ──── Groups ────

    [Fact]
    public void Describe_AlwaysReportsMeasuredGeometry()
    {
        var element = Box.New();
        element.Layout = new LayoutResult(0, 0, 0, 0, default, default, default);

        // "It laid out to zero" is the answer to most questions that bring someone
        // to an inspector, so Layout is never omitted for being default.
        var layout = Group(element, "Layout");
        Assert.Equal("0 × 0", Row(layout, "Size"));
    }

    [Fact]
    public void Describe_OmitsRowsLeftAtTheirDefaults()
    {
        var sized = Box.New().Width(120);

        var box = Group(sized, "Box");

        // Width is set and shown; everything still at its default is not, so the pane
        // reads as "what this element does" rather than a wall of `auto`.
        Assert.Equal("120px", Row(box, "Width"));
        Assert.DoesNotContain(box.Rows, r => r.Name == "Height");
        Assert.DoesNotContain(box.Rows, r => r.Name == "Overflow X");

        // An element that sets nothing at all drops the group entirely.
        Assert.Null(FindGroup(Box.New(), "Box"));
    }

    [Fact]
    public void Describe_ReportsRowsTheElementActuallySets()
    {
        var element = Box.New().Width(120).Overflow(Overflow.Hidden).FlexGrow(2);

        Assert.Equal("120px", Row(Group(element, "Box"), "Width"));
        Assert.Equal("Hidden", Row(Group(element, "Box"), "Overflow X"));
        Assert.Equal("2", Row(Group(element, "Flex"), "Grow"));
    }

    [Fact]
    public void Describe_ListsWiredEventTypes()
    {
        var element = Box.New().OnClick(_ => { });

        // "Nothing is listening" is the usual answer to "why doesn't my click fire".
        Assert.Contains("Click", Row(Group(element, "State"), "Handlers"));
    }

    [Fact]
    public void Describe_OmitsTheHandlersRow_WhenNothingIsListening()
    {
        Assert.DoesNotContain(Group(Box.New(), "State").Rows, r => r.Name == "Handlers");
    }

    [Fact]
    public void Describe_HidesTheReconcilerSKeyButShowsAnAuthoredOne()
    {
        var host = new Sidebar().Mount();
        Assert.DoesNotContain(Group(host, "Element").Rows, r => r.Name == "Key");

        var keyed = Box.New();
        keyed.Key = "row-7";
        Assert.Equal("row-7", Row(Group(keyed, "Element"), "Key"));
    }

    [Fact]
    public void Describe_SkipsThePositionGroup_UntilPositioningIsUsed()
    {
        Assert.Null(FindGroup(Box.New(), "Position"));

        var absolute = Box.New();
        absolute.Position.Type = PositionType.Absolute;
        Assert.NotNull(FindGroup(absolute, "Position"));
    }

    [Fact]
    public void Describe_LabelsGridCoordinatesAsZeroBased()
    {
        var cell = Box.New();
        cell.GridItem.Row = 1;
        cell.GridItem.Column = 2;

        var group = Group(cell, "Grid item");

        // The framework's grid is 0-based while CSS lines are 1-based; an unlabelled
        // number here is exactly the off-by-one this inspector exists to catch.
        Assert.Equal("1", Row(group, "Row (0-based)"));
        Assert.Equal("2", Row(group, "Column (0-based)"));
    }

    // ──── Formatting ────

    [Fact]
    public void Format_Dimensions()
    {
        Assert.Equal("auto", ElementProperties.Format(Dimension.Auto));
        Assert.Equal("none", ElementProperties.Format(Dimension.None));
        Assert.Equal("12px", ElementProperties.Format(Dimension.Px(12)));
        Assert.Equal("50%", ElementProperties.Format(Dimension.Percent(50)));
    }

    [Fact]
    public void Format_CollapsesUniformThickness()
    {
        Assert.Equal("4", ElementProperties.Format(new Thickness(4)));
        Assert.Equal("1 2 3 4", ElementProperties.Format(new Thickness(1, 2, 3, 4)));
    }

    [Fact]
    public void Format_ColorsAsHex_WithAlphaOnlyWhenItMatters()
    {
        Assert.Equal("transparent", ElementProperties.Format(Color.Transparent));
        Assert.Equal("#FF0000", ElementProperties.Format(Color.Red));
        Assert.Equal("#FF000080", ElementProperties.Format(new Color(255, 0, 0, 128)));
    }

    [Fact]
    public void ToText_RendersEveryGroupForTheClipboard()
    {
        var element = Box.New().Width(120);
        element.Name = "sidebar";

        var text = ElementProperties.ToText(element);

        Assert.StartsWith("Box #sidebar", text);
        Assert.Contains("Layout", text);
        Assert.Contains("  Width: 120px", text);
    }

    // ──── Helpers ────

    private static PropertyGroup? FindGroup(Element element, string name) =>
        ElementProperties.Describe(element).FirstOrDefault(g => g.Name == name);

    private static PropertyGroup Group(Element element, string name) =>
        FindGroup(element, name) ?? throw new Xunit.Sdk.XunitException($"No '{name}' group.");

    private static string Row(PropertyGroup group, string name) =>
        group.Rows.FirstOrDefault(r => r.Name == name).Value
        ?? throw new Xunit.Sdk.XunitException($"No '{name}' row in '{group.Name}'.");
}
