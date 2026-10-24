using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Primitives;
using Xunit;
using TextB = HultUI.Core.Builders.Text;

namespace HultUI.Headless.Tests;

/// <summary>
/// An overlay that opens or closes — a dropdown's list, a dialog, a popover — is in the
/// accessibility tree from the next frame. Opening one changed nothing the window looked at for
/// accessibility: the tree took it in only when something else had it rebuilt, which was usually
/// the control's own expanded state (a dropdown's, a date picker's). Since a leaf's expanded state
/// is patched without a rebuild (plan 17, round 4), a dropdown's list never reached a reader.
/// </summary>
public class A11yOverlayTests
{
    private static AccessibilityNode? Find(AccessibilityNode? node, Func<AccessibilityNode, bool> match)
    {
        if (node is null) return null;
        if (match(node)) return node;
        foreach (var child in node.Children)
            if (Find(child, match) is { } found) return found;
        return null;
    }

    private static (HeadlessSession, HeadlessAccessibilityBridge) Mount(Element root)
    {
        var session = HeadlessSession.Create(800, 600);
        var bridge = session.Window.InstallAccessibilityBridge();
        session.Window.SetRoot(root);
        session.PumpFrames(2);
        // Past the mount's layout refresh: nothing else asks for a tree.
        for (int i = 0; i < 2; i++) session.PumpFrame(TimeSpan.FromMilliseconds(200));
        return (session, bridge);
    }

    [Fact]
    public void AnOverlay_IsInTheTree_FromTheFrameAfterItOpens_AndGoneAfterItCloses()
    {
        var (session, bridge) = Mount(Flex.Column().Children(TextB.New("Main"), Button.New("Open")));
        using var _ = session;
        var label = TextB.New("In the overlay");

        var handle = session.Window.Window.ShowOverlay(Flex.Column().Padding(8).Children(label, Checkbox.New(false, "Option")), 50, 50);
        session.PumpFrame();
        Assert.NotNull(Find(bridge.CurrentTree, n => n.ElementId == label.Id));

        session.Window.Window.CloseOverlay(handle);
        session.PumpFrame();
        Assert.Null(Find(bridge.CurrentTree, n => n.ElementId == label.Id));
    }

    [Fact]
    public void ADropdownsList_IsInTheTree_WithItsExpandedState_WhenItOpens()
    {
        var dropdown = Dropdown.New(["Alpha", "Bravo", "Charlie"], selectedIndex: 0).Width(Dimension.Px(160));
        var (session, bridge) = Mount(Flex.Column().Padding(8).Children(TextB.New("Pick one"), dropdown));
        using var _ = session;

        session.Window.Click(dropdown);
        session.PumpFrame();

        Assert.True(Find(bridge.CurrentTree, n => n.ElementId == dropdown.Id)!.Expanded);
        Assert.NotNull(Find(bridge.CurrentTree, n => n.Name == "Charlie"));

        session.Window.PressKey(HultUI.Core.Events.KeyCode.Escape);
        session.PumpFrame();
        Assert.False(Find(bridge.CurrentTree, n => n.ElementId == dropdown.Id)!.Expanded);
        Assert.Null(Find(bridge.CurrentTree, n => n.Name == "Charlie"));
    }
}
