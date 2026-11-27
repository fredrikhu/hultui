using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// A dropdown a reader opens (TalkBack's double tap, UIA's Expand: the router's ExpandCollapse) gives
/// the reader's focus to its open list, on the chosen item, as an open menu does; choosing gives it
/// back to the dropdown. The list is an overlay at the tree's end, and TalkBack's next swipe went on
/// past the dropdown to what follows it on the page (a Galaxy S25, plan 20).
/// </summary>
public class A11yDropdownReaderFocusTests
{
    [Fact]
    public void ADropdownAReaderOpens_HoldsItsFocusOnTheChosenItem_AndGivesItBackOnChoosing()
    {
        using var session = HeadlessSession.Create(400, 600);
        var dropdown = Dropdown.New(["First choice", "Second choice", "Third choice"], 0);
        session.Window.SetRoot(Flex.Column().Width(400).Height(600)
            .Children(Button.New("Before"), dropdown, Button.New("After")));
        var bridge = session.Window.InstallAccessibilityBridge();
        session.PumpFrame();
        var router = new AccessibilityActionRouter(session.Window.Window);
        Assert.False(dropdown.IsFocused);

        router.ExpandCollapseElement(dropdown.Id, expand: true);
        router.DrainPendingActions();
        session.PumpFrame();
        session.PumpFrame();   // the list's node is in the tree from the frame after it opened
        Assert.True(dropdown.IsOpen);
        var holder = bridge.FocusedNode;
        Assert.True(holder is { Role: AccessibilityRole.List }, $"the reader's focus is on {holder?.Role} {holder?.Name}");
        Assert.Equal(0, holder!.CurrentItem);   // the chosen item: the reader's focus goes there

        // A double tap on another item chooses it and closes the list; the dropdown holds the
        // reader's focus again.
        router.InvokeElement(holder.Children[1].ElementId);
        router.DrainPendingActions();
        session.PumpFrame();
        session.PumpFrame();
        Assert.False(dropdown.IsOpen);
        Assert.Equal(1, dropdown.SelectedIndex);
        Assert.Equal(dropdown.Id, bridge.FocusedNode?.ElementId);
    }
}
