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

    /// <summary>A menu an app draws with elements: a menu of menu items, each selectable, whose
    /// action is a click. A menu item had no action, so a reader could not run one.</summary>
    [Fact]
    public void AMenusEntry_IsSelectable_AndRunsAsAClick()
    {
        var root = BoxElement.New();
        var menu = Box.New().AccessibleRole(AccessibilityRole.Menu);
        menu.AddChild(Box.New().AccessibleRole(AccessibilityRole.MenuItem).AccessibleName("Add"));
        root.AddChild(menu);

        var menuNode = AccessibilityTreeBuilder.Build(root).Children[0];
        Assert.Equal(AtSpiConstants.RoleMenu, AtSpiAccessible.RoleOf(menuNode));
        var entry = AtSpiAccessible.From(menuNode.Children[0], isRoot: false, AtSpiConstants.RootPath, 0, 1f);
        Assert.Equal(AtSpiConstants.RoleMenuItem, AtSpiAccessible.RoleOf(menuNode.Children[0]));
        Assert.Equal(AtSpiActionKind.Click, entry.Action);
        Assert.True(Has(AtSpiMarshal.StateBits(entry), AtSpiConstants.StateSelectable));
    }

    /// <summary>The window patches a menu's entry the pointer reached into the tree, and the
    /// bridge tells it as a list item's: state-changed:selected, with no new snapshot.</summary>
    [Fact]
    public void TheEntryThePointerReached_IsToldAsSelected()
    {
        var root = BoxElement.New();
        var menu = Box.New().AccessibleRole(AccessibilityRole.Menu);
        var item = Box.New().AccessibleRole(AccessibilityRole.MenuItem).AccessibleName("Add").AccessibleSelected(false);
        menu.AddChild(item);
        root.AddChild(menu);
        var tree = AccessibilityTreeBuilder.Build(root);
        var signals = new SignalRecorder();
        var bridge = new LinuxAccessibilityBridge(signals);
        bridge.ApplyChanges(AccessibilityTreeDiffer.Diff(null, tree), tree);
        signals.Lines.Clear();

        item.AccessibleSelected(true);
        var selected = AccessibilityTreeBuilder.Build(root);
        var patch = new TreeDiff();
        patch.PropertyChanged.Add(new PropertyChange(selected.Children[0].Children[0], AccessibilityProperties.IsSelected));
        bridge.ApplyPatchedProperties(patch, selected);

        var line = Assert.Single(signals.Lines);
        Assert.StartsWith("state-changed:selected ", line);
    }

    [Fact]
    public void ACheckedRadioButtonIsSelected()
    {
        Assert.True(Has(Bits(new RadioButtonElement { Label = "On", IsChecked = true }), AtSpiConstants.StateSelected));
        Assert.False(Has(Bits(new RadioButtonElement { Label = "Off" }), AtSpiConstants.StateSelected));
    }
}
