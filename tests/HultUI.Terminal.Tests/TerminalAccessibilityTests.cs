using HultUI.Core.Accessibility;
using Xunit;

namespace HultUI.Terminal.Tests;

/// <summary>The terminal for a screen reader: a document named after the title, whose value is
/// the screen, told of a change once the output pauses.</summary>
public class TerminalAccessibilityTests
{
    private static bool ToldOf(HultUI.Headless.HeadlessAccessibilityBridge bridge, string property) =>
        bridge.Diffs.Any(d => d.PropertyChanged.Any(p => p.PropertyName == property && p.Node.Role == AccessibilityRole.Document));

    [Fact]
    public void TheScreen_IsTheValue_AndAChangeIsToldOnceTheOutputPauses()
    {
        AccessibilityClients.Force = true;
        try
        {
            using var t = Term.Shown();
            var bridge = t.Window.InstallAccessibilityBridge();
            t.Emit("hello\r\nworld");
            Assert.Equal("hello\nworld", t.Window.FindByRole(AccessibilityRole.Document)!.Value);
            t.Session.PumpFrame(TerminalElement.ScreenSettle + TimeSpan.FromMilliseconds(50));
            t.Session.PumpFrames(2);
            bridge.ClearDiffs();

            t.Emit(" again");
            t.Session.PumpFrames(3);
            Assert.False(ToldOf(bridge, AccessibilityProperties.Value)); // not a change a frame

            t.Session.PumpFrame(TerminalElement.ScreenSettle + TimeSpan.FromMilliseconds(50));
            t.Session.PumpFrames(2);
            Assert.True(ToldOf(bridge, AccessibilityProperties.Value));
            Assert.Equal("hello\nworld again", bridge.CurrentTree is { } tree ? FindDocument(tree)?.Value : null);
        }
        finally { AccessibilityClients.Force = null; }
    }

    [Fact]
    public void UnbrokenOutput_IsStillToldOf_AfterTheLimit()
    {
        AccessibilityClients.Force = true;
        try
        {
            using var t = Term.Shown();
            var bridge = t.Window.InstallAccessibilityBridge();
            t.Session.PumpFrames(2);
            bridge.ClearDiffs();

            // Output every 400 ms, never pausing for the 500 the settle waits.
            var waited = TimeSpan.Zero;
            while (waited < TerminalElement.ScreenSettleLimit + TimeSpan.FromMilliseconds(600))
            {
                t.Emit("x");
                t.Session.PumpFrame(TimeSpan.FromMilliseconds(400));
                waited += TimeSpan.FromMilliseconds(400);
            }
            Assert.True(ToldOf(bridge, AccessibilityProperties.Value));
        }
        finally { AccessibilityClients.Force = null; }
    }

    [Fact]
    public void ATitle_RenamesTheDocument_AtOnce()
    {
        AccessibilityClients.Force = true;
        try
        {
            using var t = Term.Shown();
            var bridge = t.Window.InstallAccessibilityBridge();
            t.Session.PumpFrames(2);
            bridge.ClearDiffs();

            t.Emit("\x1b]2;make test\x07");
            t.Session.PumpFrames(2);
            Assert.True(ToldOf(bridge, AccessibilityProperties.Name));
            Assert.Equal("make test", t.Window.FindByRole(AccessibilityRole.Document)!.Name);
        }
        finally { AccessibilityClients.Force = null; }
    }

    [Fact]
    public void ATitleSetBeforeTheElementListened_StillNamesIt()
    {
        // The demo shell's title arrived while the window was being built, and a screen reader
        // was told "Terminal" for good.
        using var session = Headless.HeadlessSession.Create(400, 300);
        var connection = new Testing.FakeConnection();
        using var controller = new TerminalController(connection);
        var element = TerminalElement.New(controller); // made, not yet in a window

        connection.Emit("\x1b]0;Demo shell\x07");
        session.PumpUntil(() => controller.Title == "Demo shell", TimeSpan.FromSeconds(10));

        session.Window.SetRoot(element);
        session.PumpFrames(2);
        Assert.Equal("Demo shell", session.Window.FindByRole(AccessibilityRole.Document)!.Name);
    }

    private static AccessibilityNode? FindDocument(AccessibilityNode node)
    {
        if (node.Role == AccessibilityRole.Document) return node;
        foreach (var child in node.Children)
            if (FindDocument(child) is { } found) return found;
        return null;
    }
}
