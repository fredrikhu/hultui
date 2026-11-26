using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Primitives;
using Xunit;
using TextB = HultUI.Core.Builders.Text;

namespace HultUI.Headless.Tests;

/// <summary>
/// <c>Window.SafeArea</c> (docs/design/ANDROID-PLATFORM.md §3): the edges Android's status bar,
/// gesture bar and cutouts cover. By default the root is laid out inside it, and is drawn,
/// hit-tested and read there; <c>LaysOutInSafeArea</c> off lays it across the whole window.
/// Overlays stay inside either way. Zero, the desktop, changes nothing.
/// </summary>
public sealed class SafeAreaTests
{
    // A phone's portrait bars, in layout units: status bar 24 on top, gesture bar 48 below.
    private static readonly Thickness Bars = new(Top: 24, Right: 0, Bottom: 48, Left: 0);

    private static (HeadlessSession Session, BoxElement Root) Started()
    {
        var session = HeadlessSession.Create(width: 400, height: 300);
        var root = Box.New();
        session.Window.SetRoot(root);
        session.PumpFrame();
        return (session, root);
    }

    [Fact]
    public void WithNoSafeArea_TheRootFillsTheWindow()
    {
        var (session, root) = Started();
        using var _ = session;

        Assert.Equal(Thickness.Zero, session.Window.Window.SafeArea);
        Assert.Equal(new Rect(0, 0, 400, 300), root.Layout.AbsoluteBorderBox);
    }

    [Fact]
    public void ASafeArea_IsToldAtOnce_AndTheNextFrameLaysTheRootOutInsideIt()
    {
        var (session, root) = Started();
        using var _ = session;
        var window = session.Window.Window;
        var told = new List<Thickness>();
        window.SafeAreaChanged += () => told.Add(window.SafeArea);

        session.Window.SetSafeArea(Bars);
        session.Window.SetSafeArea(Bars);   // the same: nothing more

        Assert.Equal([Bars], told);
        Assert.True(session.PumpFrame());
        Assert.Equal(new Rect(0, 24, 400, 228), root.Layout.AbsoluteBorderBox);
    }

    [Fact]
    public void LaysOutInSafeAreaOff_LaysTheRootAcrossTheWholeWindow_AndSafeAreaStillSays()
    {
        var (session, root) = Started();
        using var _ = session;
        session.Window.SetSafeArea(Bars);
        session.PumpFrame();

        session.Window.Window.LaysOutInSafeArea = false;
        session.PumpFrame();

        Assert.Equal(new Rect(0, 0, 400, 300), root.Layout.AbsoluteBorderBox);
        Assert.Equal(Bars, session.Window.Window.SafeArea);
    }

    [Fact]
    public void AButtonInTheMovedRoot_IsClickedWhereItIsDrawn()
    {
        using var session = HeadlessSession.Create(width: 400, height: 300);
        int clicks = 0;
        var button = Button.New("Go").OnClick(_ => clicks++);
        session.Window.SetRoot(Flex.Column().Children(button));
        session.Window.SetSafeArea(Bars);
        session.PumpFrame();

        Assert.True(button.Layout.AbsoluteBorderBox.Y >= 24, "the button sits under the status bar");
        session.Window.Click(button);
        // And a press where the button would be without the safe area misses it.
        session.Window.Click(button.Layout.AbsoluteBorderBox.X + 2, 2);

        Assert.Equal(1, clicks);
    }

    [Fact]
    public void AContextMenuOpenedAtTheBottom_StaysAboveTheGestureBar()
    {
        using var session = HeadlessSession.Create(width: 400, height: 300);
        var box = Box.New().FlexGrow(1);
        box.ContextMenu([new ContextMenuItem { Label = "Copy" }, new ContextMenuItem { Label = "Paste" }]);
        session.Window.SetRoot(Flex.Column().Children(box));
        session.Window.SetSafeArea(Bars);
        session.PumpFrame();

        session.Window.Click(390, 245, MouseButton.Right);   // just above the gesture bar
        session.PumpFrame();

        var menu = Assert.Single(session.Window.OverlayContents.OfType<ContextMenuElement>());
        float bottom = menu.OverlayOrigin.Y + menu.Layout.Height;
        float right = menu.OverlayOrigin.X + menu.Layout.Width;
        Assert.True(bottom <= 300 - 48 + 0.01f, $"the menu reaches {bottom}, under the gesture bar");
        Assert.True(right <= 400 + 0.01f, $"the menu reaches {right}, off the window");
    }

    [Fact]
    public void ARotation_ThatMovesTheBars_LaysTheRootOutAgain()
    {
        var (session, root) = Started();
        using var _ = session;
        session.Window.SetSafeArea(Bars);
        session.PumpFrame();

        // Landscape: the cutout and the gesture bar move to the sides.
        session.Window.SetClientSize(300, 400);
        session.Window.SetSafeArea(new Thickness(Top: 0, Right: 32, Bottom: 0, Left: 32));
        session.PumpFrame();

        Assert.Equal(new Rect(32, 0, 236, 400), root.Layout.AbsoluteBorderBox);
    }

    [Fact]
    public void AnAppReadsTheSafeArea_ToPadWhatItDrawsBehindTheBars()
    {
        using var session = HeadlessSession.Create(width: 400, height: 300);
        var toolbar = Box.New().Height(40f);
        var root = Flex.Column().Children(toolbar, TextB.New("content"));
        session.Window.SetRoot(root);
        var window = session.Window.Window;
        window.LaysOutInSafeArea = false;
        // A full-bleed app pads its own toolbar by the status bar.
        window.SafeAreaChanged += () => toolbar.Padding(new Thickness(window.SafeArea.Top, 0, 0, 0));

        session.Window.SetSafeArea(Bars);
        session.PumpFrame();

        Assert.Equal(0, root.Layout.AbsoluteBorderBox.Y);
        Assert.Equal(24, toolbar.Layout.Padding.Top);
    }
}
