using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Headless;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// Seam A2: <see cref="Element.Announce"/> reaches the window's bridge once the frame's tree
/// holds the element, from the element's node; nothing is kept for a reader that is not there;
/// a toast says its message as it appears.
/// </summary>
public class AnnouncementTests
{
    [Fact]
    public void AnAnnouncement_ReachesTheBridge_FromTheElementsNode()
    {
        using var session = HeadlessSession.Create(400, 300);
        var status = Box.New().Width(100).Height(20).AccessibleName("Status");
        session.Window.SetRoot(Box.New().FlexGrow(1).Children(status));
        var bridge = session.Window.InstallAccessibilityBridge();
        session.PumpFrame();

        status.Announce("Saved");
        session.Window.FindByText("Status")!.Element.Announce("Deleted", AnnouncementPoliteness.Assertive);
        Assert.Empty(bridge.Announcements); // at the frame, once the tree holds the element
        session.PumpFrame();

        Assert.Equal([("Saved", AnnouncementPoliteness.Polite), ("Deleted", AnnouncementPoliteness.Assertive)],
            bridge.Announcements.Select(a => (a.Text, a.Politeness)));
        Assert.All(bridge.Announcements, a => Assert.Equal("Status", a.Source!.Name));
    }

    [Fact]
    public void WithNobodyListening_NothingIsKept()
    {
        using var session = HeadlessSession.Create(400, 300);
        var status = Box.New().Width(100).Height(20).AccessibleName("Status");
        session.Window.SetRoot(Box.New().FlexGrow(1).Children(status));
        session.PumpFrame();

        status.Announce("Nobody hears this");
        session.PumpFrame();
        var bridge = session.Window.InstallAccessibilityBridge();
        session.PumpFrames(2);
        Assert.Empty(bridge.Announcements);
    }

    [Fact]
    public void AnElementNotInAWindow_AnnouncesNothing()
    {
        // No window, no host: nothing to tell, and nothing thrown.
        Box.New().AccessibleName("Loose").Announce("Hello");
    }

    [Fact]
    public void AToast_SaysItsMessage_AnErrorAtOnce()
    {
        using var session = HeadlessSession.Create(400, 300);
        var host = Box.New().FlexGrow(1);
        session.Window.SetRoot(host);
        var bridge = session.Window.InstallAccessibilityBridge();
        session.PumpFrame();

        host.AddChild(ToastElement.New("Copied"));
        session.PumpFrames(2);
        var copied = Assert.Single(bridge.Announcements);
        Assert.Equal(("Copied", AnnouncementPoliteness.Polite), (copied.Text, copied.Politeness));

        bridge.ClearAnnouncements();
        host.AddChild(ToastElement.New("Could not save", ToastSeverity.Error));
        session.PumpFrames(2);
        var error = Assert.Single(bridge.Announcements);
        Assert.Equal(("Could not save", AnnouncementPoliteness.Assertive), (error.Text, error.Politeness));
    }

    [Fact]
    public void AToastReplacedByARebuild_SaysTheNewMessage()
    {
        using var session = HeadlessSession.Create(400, 300);
        var message = new Core.Reactivity.Signal<string>("First");
        session.Window.SetRoot(new Toasty(message).Mount());
        var bridge = session.Window.InstallAccessibilityBridge();
        session.PumpFrames(2);
        bridge.ClearAnnouncements();

        message.Value = "Second";
        session.PumpFrames(2);
        Assert.Equal("Second", Assert.Single(bridge.Announcements).Text);

        bridge.ClearAnnouncements();
        message.Value = "Second"; // no change, no word
        session.PumpFrames(2);
        Assert.Empty(bridge.Announcements);
    }

    private sealed class Toasty(Core.Reactivity.Signal<string> message) : Core.Components.Component
    {
        public override Element Build() => Box.New().FlexGrow(1).Children(ToastElement.New(message.Value));
    }
}
