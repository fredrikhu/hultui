using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Platform.Accessibility.Linux;
using Xunit;

namespace HultUI.Platform.Accessibility.Linux.Tests;

/// <summary>
/// The state set an accessible reports. Expandable and expanded were defined and never sent,
/// so Orca read an expander, a tree folder, a dropdown and a menu-bar title as plain buttons,
/// open or not.
/// </summary>
public class AtSpiStateTests
{
    private static uint Bits(Element element)
    {
        var root = BoxElement.New();
        root.AddChild(element);
        var tree = AccessibilityTreeBuilder.Build(root);
        var node = tree.Children[0];
        return AtSpiMarshal.StateBits(AtSpiAccessible.From(node, isRoot: false, AtSpiConstants.RootPath, 0, 1f));
    }

    private static bool Has(uint bits, int state) => (bits & (1u << state)) != 0;

    [Fact]
    public void AClosedExpanderIsExpandable_AndAnOpenOneExpandedToo()
    {
        var closed = Bits(Expander.New("Details", TextElement.New("More")));
        Assert.True(Has(closed, AtSpiConstants.StateExpandable));
        Assert.False(Has(closed, AtSpiConstants.StateExpanded));

        var open = Bits(Expander.New("Details", TextElement.New("More")).DefaultExpanded());
        Assert.True(Has(open, AtSpiConstants.StateExpandable));
        Assert.True(Has(open, AtSpiConstants.StateExpanded));
    }

    [Fact]
    public void ANodeThatCannotOpenIsNeither()
    {
        var bits = Bits(ButtonElement.New("Save"));
        Assert.False(Has(bits, AtSpiConstants.StateExpandable));
        Assert.False(Has(bits, AtSpiConstants.StateExpanded));
    }
}
