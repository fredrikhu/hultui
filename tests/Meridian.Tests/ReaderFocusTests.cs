using HultUI.Core.Accessibility;
using HultUI.Core.Events;
using HultUI.Headless;
using Xunit;

namespace Meridian.Tests;

/// <summary>
/// Where the keyboard focus goes, which is what a screen reader follows: never nowhere after a
/// key or a click that had it somewhere. Hosted headless over a temp collection.
/// </summary>
public class ReaderFocusTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("meridian-focus-").FullName;

    public ReaderFocusTests()
    {
        File.WriteAllText(Path.Combine(_root, "get-ok.req"), "GET http://127.0.0.1:9/ok\nname Get ok\n");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private void Hosted(Action<HeadlessSession, MeridianApp> body)
    {
        using var session = HeadlessSession.Create(1440, 900);
        MeridianApp? app = null;
        try
        {
            app = new MeridianApp(session.Application, session.Window.Window);
            session.Application.SetRoot(app.Mount());
            app.OpenWorkspace(_root);
            session.PumpFrames(3);
            body(session, app);
        }
        finally { app?.Dispose(); }
    }

    private static HultUI.Core.Elements.Element Live(HeadlessSession s, string automationId) =>
        s.Window.FindByAutomationId(automationId)?.Element ?? throw new Xunit.Sdk.XunitException($"#{automationId} not found");

    /// <summary>Escape out of the new-request prompt gives the focus back to the "New request"
    /// button that opened it, and a reader is told so. It went nowhere: the field went with the
    /// prompt, and nothing took the focus after it.</summary>
    [Fact]
    public void EscapeOutOfThePrompt_GivesTheFocusBackToTheButtonThatOpenedIt()
    {
        Hosted((s, app) =>
        {
            var reader = s.Window.InstallAccessibilityBridge();
            s.PumpFrames(2);
            var opener = Live(s, "tree-new-request");
            s.Window.Click(opener);   // the press focuses the button, the click opens the prompt
            s.PumpFrames(3);
            Assert.Same(Live(s, "dialog-input"), s.Window.Window.FocusedElement);

            s.Window.PressKey(KeyCode.Escape);
            s.PumpFrames(3);
            Assert.Null(s.Window.FindByAutomationId("dialog-input"));
            Assert.Same(opener, s.Window.Window.FocusedElement);
            Assert.Equal(AccessibilityRole.Button, reader.FocusedNode?.Role);
            Assert.Equal("New request", reader.FocusedNode?.Name);
        });
    }

    /// <summary>The question does the same: Delete with the Send button focused asks to delete the
    /// open request, and Escape gives the focus back to Send.</summary>
    [Fact]
    public void EscapeOutOfTheQuestion_GivesTheFocusBackToWhatHadIt()
    {
        Hosted((s, app) =>
        {
            s.Window.FindByAutomationId("req-get-ok")!.Click();
            s.PumpFrames(3);
            var send = Live(s, "send");
            s.Window.FindByAutomationId("send")!.Focus();
            s.Window.PressKey(KeyCode.Delete);
            s.PumpFrames(3);
            Assert.NotSame(send, s.Window.Window.FocusedElement);   // the question's button has it

            s.Window.PressKey(KeyCode.Escape);
            s.PumpFrames(3);
            Assert.True(File.Exists(Path.Combine(_root, "get-ok.req")));
            Assert.Same(send, s.Window.Window.FocusedElement);
        });
    }

    /// <summary>A prompt submitted gives the focus back too, when its action gives it to nothing
    /// of its own: "New folder" makes the folder and the focus is on the button again.</summary>
    [Fact]
    public void SubmittingThePrompt_GivesTheFocusBackToTheButtonThatOpenedIt()
    {
        Hosted((s, app) =>
        {
            var opener = Live(s, "tree-new-folder");
            s.Window.Click(opener);
            s.PumpFrames(3);
            s.Window.TypeText("users");
            s.Window.PressKey(KeyCode.Enter);
            s.PumpFrames(3);
            Assert.True(Directory.Exists(Path.Combine(_root, "users")));
            Assert.Null(s.Window.FindByAutomationId("dialog-input"));
            Assert.Same(Live(s, "tree-new-folder"), s.Window.Window.FocusedElement);
        });
    }
}
