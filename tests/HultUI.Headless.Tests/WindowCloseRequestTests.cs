using HultUI.Core.Builders;
using Xunit;

namespace HultUI.Headless.Tests;

using Text = HultUI.Core.Builders.Text;

/// <summary>
/// The system's request to close a window (Alt+F4, the title bar's or the task bar's close, a
/// WM_CLOSE) is asked of the app first (<see cref="Window.CloseRequested"/>), which can keep the
/// window open: "Close? There is unsaved work". It went straight to closing, so only an app's own
/// close button could ask. A quit (Cmd+Q, a signal, the session ending) is no request: every
/// window closes, and is told so (<see cref="Window.Closed"/>), which it was not.
/// </summary>
public class WindowCloseRequestTests
{
    private static HeadlessSession Mount()
    {
        var session = HeadlessSession.Create(400, 300);
        session.Window.SetRoot(Box.New().Children(Text.New("content")));
        session.PumpFrame();
        return session;
    }

    [Fact]
    public void TheSystemsCloseWithNoHandler_ClosesTheWindow()
    {
        using var session = Mount();
        var window = session.Window.Window;
        int closed = 0;
        window.Closed += () => closed++;

        session.Window.AskToClose();
        session.PumpFrame();

        Assert.False(window.IsOpen);
        Assert.Equal(1, closed);
    }

    [Fact]
    public void AHandlerThatCancels_KeepsTheWindowOpen_UntilTheAppClosesIt()
    {
        using var session = Mount();
        var window = session.Window.Window;
        int asked = 0, closed = 0;
        window.CloseRequested += request => { asked++; request.Cancel(); };
        window.Closed += () => closed++;

        session.Window.AskToClose();
        session.PumpFrame();
        Assert.True(window.IsOpen);
        Assert.Equal((1, 0), (asked, closed));

        // The user answered: the app closes it, and is not asked again.
        window.Close();
        Assert.False(window.IsOpen);
        Assert.Equal((1, 1), (asked, closed));
    }

    /// <summary>The question is asked after the platform's events, on the UI thread, so the handler
    /// can open the dialog that asks it.</summary>
    [Fact]
    public void AHandler_CanOpenADialogThatAsks()
    {
        using var session = Mount();
        var window = session.Window.Window;
        window.CloseRequested += request =>
        {
            request.Cancel();
            window.ShowOverlay(Box.New().Children(Text.New("Close? There is unsaved work")), 20, 20);
        };

        session.Window.AskToClose();
        session.PumpFrame();

        Assert.True(window.IsOpen);
        Assert.NotNull(session.Window.FindByText("Close? There is unsaved work"));
    }

    [Fact]
    public void RequestClose_AsksAsTheSystemsCloseDoes_AndCloseDoesNot()
    {
        using var session = Mount();
        var window = session.Window.Window;
        bool cancel = true;
        int asked = 0;
        window.CloseRequested += request => { asked++; if (cancel) request.Cancel(); };

        window.RequestClose();          // a custom title bar's close button
        Assert.True(window.IsOpen);
        cancel = false;
        window.RequestClose();
        Assert.False(window.IsOpen);
        Assert.Equal(2, asked);
    }

    [Fact]
    public void CloseItself_IsNotAsked()
    {
        using var session = Mount();
        var window = session.Window.Window;
        int asked = 0;
        window.CloseRequested += request => { asked++; request.Cancel(); };

        window.Close();

        Assert.False(window.IsOpen);
        Assert.Equal(0, asked);
    }

    /// <summary>One handler's cancel holds, whatever the handlers after it do; a handler that closes the window
    /// itself closes it once; a request made inside a handler is not asked again.</summary>
    [Fact]
    public void AnyCancelHolds_AndAHandlerClosingOrAskingAgainClosesOnce()
    {
        using var session = Mount();
        var window = session.Window.Window;
        int closed = 0, asked = 0;
        window.Closed += () => closed++;
        window.CloseRequested += request => request.Cancel();
        window.CloseRequested += _ => { asked++; window.RequestClose(); };

        window.RequestClose();
        Assert.True(window.IsOpen);
        Assert.Equal((1, 0), (asked, closed));

        using var other = Mount();
        var second = other.Window.Window;
        int secondClosed = 0;
        second.Closed += () => secondClosed++;
        second.CloseRequested += _ => second.Close();
        second.RequestClose();
        Assert.False(second.IsOpen);
        Assert.Equal(1, secondClosed);
    }

    /// <summary>A quit asks nothing and closes every window, each told it closed: an app saves its
    /// windows' places in Closed, and a quit skipped it.</summary>
    [Fact]
    public void AQuit_ClosesEveryWindow_WithoutAsking_AndTellsEachItClosed()
    {
        using var session = Mount();
        var window = session.Window.Window;
        int asked = 0, closed = 0;
        window.CloseRequested += request => { asked++; request.Cancel(); };
        window.Closed += () => closed++;

        session.Platform.QuitAsked = true;
        session.PumpFrame();

        Assert.False(window.IsOpen);
        Assert.False(session.Application.IsRunning);
        Assert.Equal((0, 1), (asked, closed));
    }
}
