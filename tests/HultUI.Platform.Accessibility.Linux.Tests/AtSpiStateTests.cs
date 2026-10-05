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

    /// <summary>The selected state came from IsToggled, which no tab and no radio button ever
    /// carried: Orca never heard either one called selected.</summary>
    [Fact]
    public void ASelectedTabIsSelected_AndAnotherOnlySelectable()
    {
        var open = Bits(Box.New().AccessibleRole(AccessibilityRole.Tab).AccessibleName("General").AccessibleSelected(true));
        Assert.True(Has(open, AtSpiConstants.StateSelectable));
        Assert.True(Has(open, AtSpiConstants.StateSelected));

        var other = Bits(Box.New().AccessibleRole(AccessibilityRole.Tab).AccessibleName("Inbox").AccessibleSelected(false));
        Assert.True(Has(other, AtSpiConstants.StateSelectable));
        Assert.False(Has(other, AtSpiConstants.StateSelected));
    }

    [Fact]
    public void ACheckedRadioButtonIsSelected()
    {
        Assert.True(Has(Bits(new RadioButtonElement { Label = "On", IsChecked = true }), AtSpiConstants.StateSelected));
        Assert.False(Has(Bits(new RadioButtonElement { Label = "Off" }), AtSpiConstants.StateSelected));
    }
}
