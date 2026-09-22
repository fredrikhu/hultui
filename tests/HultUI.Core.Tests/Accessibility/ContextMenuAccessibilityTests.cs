using HultUI.Core.Accessibility;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Styling;
using Xunit;

namespace HultUI.Core.Tests.Accessibility;

/// <summary>
/// A context menu draws its items rather than building an element for each, so without the
/// item-source contract a screen reader finds one empty box where the menu is. These pin
/// the items being published — and the separators not being.
/// </summary>
public class ContextMenuAccessibilityTests
{
    private readonly LayoutEngine _layout = new(new StubMeasureContext());

    private static ContextMenuElement Menu(params ContextMenuItem[] items) =>
        new(items, new ContextMenuTheme(), closeMenu: () => { });

    private AccessibilityNode BuildLaidOut(ContextMenuElement menu)
    {
        _layout.PerformLayout(menu, 300, 400);
        return AccessibilityTreeBuilder.Build(menu);
    }

    [Fact]
    public void EveryChoiceIsAnItemAndSeparatorsAreNot()
    {
        var tree = BuildLaidOut(Menu(
            new ContextMenuItem { Label = "Play" },
            ContextMenuItem.Separator(),
            new ContextMenuItem { Label = "Mark watched" }));

        Assert.Equal(AccessibilityRole.List, tree.Role);
        Assert.Equal(2, tree.Children.Count);
        Assert.Equal("Play", tree.Children[0].Name);
        Assert.Equal("Mark watched", tree.Children[1].Name);
    }

    [Fact]
    public void AnItemIsAnnouncedWithItsShortcut()
    {
        var tree = BuildLaidOut(Menu(new ContextMenuItem { Label = "Copy", ShortcutHint = "Ctrl C" }));

        Assert.Equal("Copy, Ctrl C", tree.Children[0].Name);
    }

    [Fact]
    public void InvokingAnItemRunsItsAction()
    {
        bool ran = false;
        var menu = Menu(
            new ContextMenuItem { Label = "First" },
            ContextMenuItem.Separator(),
            new ContextMenuItem { Label = "Second", Action = () => ran = true });

        // Index 1 in accessibility terms is the second *choice*, not the separator.
        ((IAccessibleItemSource)menu).ActivateItem(1);

        Assert.True(ran);
    }

    [Fact]
    public void ADisabledItemDoesNothingWhenInvoked()
    {
        bool ran = false;
        var menu = Menu(new ContextMenuItem { Label = "Nope", IsDisabled = true, Action = () => ran = true });

        ((IAccessibleItemSource)menu).ActivateItem(0);

        Assert.False(ran);
    }
}
