using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Primitives;
using Xunit;

namespace HultUI.Headless.Tests;

using Text = HultUI.Core.Builders.Text;

/// <summary>
/// <see cref="Element.AccessibilityHidden"/>, as the web's aria-hidden: the element and everything
/// under it are out of the accessibility tree while it is set, and its text names nothing. For a page
/// a player covers: TalkBack swiped from Aperture's player into the film's page behind it (a Galaxy
/// S25, plan 20).
/// </summary>
public class A11yHiddenTests
{
    private static List<string> Names(AccessibilityNode node)
    {
        var names = new List<string>();
        void Walk(AccessibilityNode n)
        {
            if (n.Name is { Length: > 0 } name) names.Add(name);
            foreach (var child in n.Children) Walk(child);
        }
        Walk(node);
        return names;
    }

    [Fact]
    public void AHiddenElement_AndAllUnderIt_HaveNoNodes_UntilItIsShownAgain()
    {
        using var session = HeadlessSession.Create(400, 300);
        var page = Flex.Column().Children(Button.New("Play"), Text.New("The overview"));
        var player = Flex.Column().Children(Button.New("Pause"));
        var root = Flex.Column().Children(page, player);
        session.Window.SetRoot(root);
        session.Window.InstallAccessibilityBridge();
        session.PumpFrame();
        Assert.Contains("Play", Names(session.Window.AccessibilityTree));

        page.AccessibilityHidden = true;
        session.PumpFrame();
        var hidden = Names(session.Window.AccessibilityTree);
        Assert.DoesNotContain("Play", hidden);
        Assert.DoesNotContain("The overview", hidden);
        Assert.Contains("Pause", hidden);

        // And after a layout change: still out.
        player.Box.Width = Dimension.Px(200);
        player.MarkLayoutDirty();
        for (int i = 0; i < 4; i++) session.PumpFrame(TimeSpan.FromMilliseconds(100));
        hidden = Names(session.Window.AccessibilityTree);
        Assert.Contains("Pause", hidden);
        Assert.DoesNotContain("Play", hidden);

        page.AccessibilityHidden = false;
        session.PumpFrame();
        Assert.Contains("Play", Names(session.Window.AccessibilityTree));
        Assert.Contains("The overview", Names(session.Window.AccessibilityTree));
    }

    /// <summary>A hidden text is no part of the name a control takes from its content.</summary>
    [Fact]
    public void AHiddenText_NamesNothing()
    {
        using var session = HeadlessSession.Create(400, 300);
        var dot = Text.New("·");
        dot.AccessibilityHidden = true;
        var button = Box.New().OnClick(_ => { }).Children(Text.New("Save"), dot);
        session.Window.SetRoot(Flex.Column().Children(button));
        session.Window.InstallAccessibilityBridge();
        session.PumpFrame();
        Assert.Equal("Save", session.Window.FindByRole(AccessibilityRole.Button)?.Node.Name);
        Assert.DoesNotContain("·", Names(session.Window.AccessibilityTree));
    }
}
