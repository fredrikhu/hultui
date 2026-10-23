using HultUI.Core.Builders;
using HultUI.Core.Dialogs;
using HultUI.Core.Elements;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// <see cref="Window.CloseOverlays"/>: everything a window shows above its content goes, each by
/// its own dismissal path, and the dialogs too when asked. A window could close its light-dismiss
/// overlays (DismissPopups) and one overlay by its handle, but not a dialog it did not open: a
/// "Close?" confirmation naming a person stayed above the lock screen that replaced the desk.
/// </summary>
public class CloseOverlaysTests
{
    [Fact]
    public async Task ClosesEveryOverlay_AndTheDialogsOnlyWhenAsked()
    {
        using var session = HeadlessSession.Create(500, 400);
        var root = Flex.Column().Children(Box.New().Width(50).Height(20));
        session.Window.SetRoot(root);
        session.PumpFrame();
        var window = session.Window.Window;

        var answer = Dialog.Confirm(window, "Close the app?", "Anna Andersson is open.");
        int ownerTold = 0, lightTold = 0;
        MenuCloseReason? menuClosed = null;
        window.ShowOverlay(Box.New().Width(40).Height(40), 10, 10, dismissOnOutsideClick: false, onDismissed: () => ownerTold++);
        window.ShowOverlay(Box.New().Width(40).Height(40), 60, 10, onDismissed: () => lightTold++);
        window.OpenContextMenu([new ContextMenuItem { Label = "Copy", Action = () => { } }], new(100, 100), () => menuClosed = MenuCloseReason.Dismissed);
        session.PumpFrame();
        Assert.Equal(4, session.Window.OverlayContents.Count);

        window.CloseOverlays();
        session.PumpFrame();

        Assert.Single(session.Window.OverlayContents);   // the dialog's
        Assert.False(answer.IsCompleted);
        Assert.Equal(1, ownerTold);
        Assert.Equal(1, lightTold);
        Assert.Equal(MenuCloseReason.Dismissed, menuClosed);

        window.CloseOverlays(includeDialogs: true);
        session.PumpFrame();

        Assert.Empty(session.Window.OverlayContents);
        Assert.True(answer.IsCompleted);
        Assert.False(await answer);   // cancelled, as Escape cancels it
        Assert.Equal(1, ownerTold);
    }

    [Fact]
    public async Task ClosesADialogThatEscapeDoesNotClose_WithItsDefaultResult()
    {
        using var session = HeadlessSession.Create(500, 400);
        session.Window.SetRoot(Box.New());
        session.PumpFrame();
        var window = session.Window.Window;

        var answer = Dialog.Show<string>(window, c => Box.New().Width(100).Height(50),
            new DialogOptions { DismissOnEscape = false, DismissOnScrimClick = false });
        session.PumpFrame();

        window.CloseOverlays(includeDialogs: true);

        Assert.Empty(session.Window.OverlayContents);
        Assert.True(answer.IsCompleted);
        Assert.Null(await answer);
    }
}
