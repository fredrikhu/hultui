using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Dialogs;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using SkiaSharp;
using Xunit;
using TextB = HultUI.Core.Builders.Text;

namespace HultUI.Headless.Tests;

/// <summary>
/// An open overlay is a root of its own, so a window's new size reached it only when something in
/// it changed: a dialog's scrim stayed the old window's size with its panel centred in that, and a
/// command palette took the new size at its next key. The frame of a resize lays the open overlays
/// out and places them: pixels as a fresh open at that size. Both the resize a pass of the loop takes
/// and the one drawn from inside the platform's modal size loop go through the same step.
/// </summary>
public class OverlayResizeTests
{
    private const int Width = 800, Height = 600, NewWidth = 1000, NewHeight = 700;

    private static byte[] Pixels(HeadlessSession session)
    {
        using var image = session.Window.CapturePixels();
        using var bitmap = SKBitmap.FromImage(image);
        return bitmap.Bytes;
    }

    /// <summary>The window's page: something for the overlays to lie over.</summary>
    private static Element Page() =>
        Flex.Column().FlexGrow(1).Padding(12).Background(Color.White).Children(TextB.New("The page"), Button.New("Press"));

    private static HeadlessSession Open(int width, int height, Action<HeadlessSession> open)
    {
        var session = HeadlessSession.Create(width, height, options: HeadlessOptions.Golden());
        session.Application.Theme = Core.Styling.Theme.Light;
        session.Window.SetRoot(Page());
        session.PumpFrame();
        open(session);
        // Past a dialog's fade.
        for (int i = 0; i < 4; i++) session.PumpFrame(TimeSpan.FromMilliseconds(100));
        return session;
    }

    private static void ShowDialog(HeadlessSession session) =>
        _ = Dialog.Confirm(session.Window.Window, "Delete the branch?", "It has no other name.");

    private static void ShowPalette(HeadlessSession session)
    {
        var commands = new List<PaletteCommand>();
        for (int i = 0; i < 40; i++) commands.Add(new PaletteCommand($"Command {i}", () => { }));
        new CommandPalette(() => commands).Open(session.Window.Window);
    }

    private static void ShowMenu(HeadlessSession session) =>
        session.Window.Window.OpenContextMenu([new ContextMenuItem { Label = "Open" }, new ContextMenuItem { Label = "Delete" }],
            new Point(760, 560));

    [Theory]
    [InlineData("dialog")]
    [InlineData("palette")]
    [InlineData("menu")]
    public void TheFrameOfAResize_IsAFreshOpenAtThatSize(string what)
    {
        Action<HeadlessSession> open = what switch
        {
            "dialog" => ShowDialog,
            "palette" => ShowPalette,
            _ => ShowMenu,
        };
        byte[] want;
        using (var fresh = Open(NewWidth, NewHeight, open))
            want = Pixels(fresh);
        using var resized = Open(Width, Height, open);

        resized.Window.SetClientSize(NewWidth, NewHeight);

        Assert.Equal(want, Pixels(resized));
    }

    [Fact]
    public void ADialog_HasItsNewLayout_OnTheResizeFrame()
    {
        using var session = Open(Width, Height, ShowDialog);
        var scrim = Assert.Single(session.Window.OverlayContents);
        Assert.Equal(Width, scrim.Layout.Width);

        session.Window.SetClientSize(NewWidth, NewHeight);
        session.PumpFrame();

        Assert.Equal((float)NewWidth, scrim.Layout.Width);
        Assert.Equal((float)NewHeight, scrim.Layout.Height);
        var panel = scrim.Children[0].Layout.AbsoluteBorderBox;
        Assert.Equal(NewWidth / 2f, panel.X + panel.Width / 2, 1f);
        Assert.Equal(NewHeight / 2f, panel.Y + panel.Height / 2, 1f);
    }

    /// <summary>The same in the frame drawn from inside the modal size loop, where nothing comes
    /// round again.</summary>
    [Fact]
    public void ALiveResizeFrame_DrawsTheNewLayout()
    {
        using var session = Open(Width, Height, ShowDialog);
        var scrim = Assert.Single(session.Window.OverlayContents);
        var platform = (HeadlessPlatformWindow)session.Window.Window.PlatformWindow;
        var live = platform.LiveResizeFrame ?? throw new InvalidOperationException("the application set no live frame");

        platform.SetClientSize(NewWidth, NewHeight);
        platform.ResizePending = true;
        live();

        Assert.False(platform.ResizePending);
        Assert.Equal((float)NewWidth, scrim.Layout.Width);
        Assert.Equal((float)NewHeight, scrim.Layout.Height);
    }

    [Fact]
    public void AnOverlaySizedFromTheWindow_FollowsIt_WhereverItIs()
    {
        using var session = HeadlessSession.Create(Width, Height);
        session.Window.SetRoot(Page());
        session.PumpFrame();
        var half = Box.New().Width(Dimension.Percent(50)).Height(Dimension.Px(40)).Background(Color.Red);
        session.Window.Window.ShowOverlay(half, 10, 10);
        session.PumpFrame();
        Assert.Equal(Width / 2f, half.Layout.Width);

        session.Window.SetClientSize(NewWidth, Height);
        session.PumpFrame();

        Assert.Equal(NewWidth / 2f, half.Layout.Width);
    }

    /// <summary>An overlay is kept on screen as an open keeps it, from where it was asked to be: the
    /// window coming back gives it its place back.</summary>
    [Fact]
    public void AnOverlayAtAPoint_StaysInTheWindow_AndGoesBackWhenTheWindowDoes()
    {
        using var session = Open(Width, Height, ShowMenu);
        var menu = Assert.Single(session.Window.OverlayContents);
        var wide = menu.Layout.Width;
        var tall = menu.Layout.Height;
        var opened = (menu.OverlayOrigin.X, menu.OverlayOrigin.Y);

        session.Window.SetClientSize(300, 200);
        session.PumpFrame();
        Assert.Equal((300 - wide, 200 - tall), (menu.OverlayOrigin.X, menu.OverlayOrigin.Y));

        session.Window.SetClientSize(Width, Height);
        session.PumpFrame();
        Assert.Equal(opened, (menu.OverlayOrigin.X, menu.OverlayOrigin.Y));

        // What is asked for last is what it goes back to.
        session.Window.SetClientSize(1000, 1000);
        session.PumpFrame();
        Assert.Equal((760f, 560f), (menu.OverlayOrigin.X, menu.OverlayOrigin.Y));
    }

    [Fact]
    public void AnOverlayMovedByItsOwner_IsKeptInTheWindowFromThere()
    {
        using var session = HeadlessSession.Create(Width, Height);
        session.Window.SetRoot(Page());
        session.PumpFrame();
        var box = Box.New().Width(Dimension.Px(100)).Height(Dimension.Px(50)).Background(Color.Red);
        var handle = session.Window.Window.ShowOverlay(box, 10, 10);
        session.PumpFrame();

        session.Window.Window.UpdateOverlayPosition(handle, 600, 500);
        session.Window.SetClientSize(500, 400);
        session.PumpFrame();
        Assert.Equal((400f, 350f), (box.OverlayOrigin.X, box.OverlayOrigin.Y));

        session.Window.SetClientSize(Width, Height);
        session.PumpFrame();
        Assert.Equal((600f, 500f), (box.OverlayOrigin.X, box.OverlayOrigin.Y));
    }

    [Fact]
    public void AnAnchoredOverlay_IsPlacedAtItsNewSize()
    {
        using var session = HeadlessSession.Create(Width, Height);
        var anchor = Button.New("Anchor");
        session.Window.SetRoot(Flex.Column().FlexGrow(1).AlignItems(AlignItems.FlexEnd).Children(anchor));
        session.PumpFrame();
        var half = Box.New().Width(Dimension.Percent(50)).Height(Dimension.Px(40));
        session.Window.Window.ShowAnchoredOverlay(half, anchor,
            (size, window) => new Point(window.Width - size.Width, anchor.WindowBorderBox.Bottom));
        session.PumpFrame();

        session.Window.SetClientSize(NewWidth, Height);
        session.PumpFrame();

        Assert.Equal(NewWidth / 2f, half.Layout.Width);
        Assert.Equal(NewWidth / 2f, half.OverlayOrigin.X);
    }

    [Fact]
    public void AReader_IsToldTheOverlaysNewBounds_WithoutATreeForTheWindow()
    {
        using var session = HeadlessSession.Create(Width, Height);
        var bridge = session.Window.InstallAccessibilityBridge();
        session.Window.SetRoot(Page());
        session.PumpFrames(2);
        session.PumpFrame(TimeSpan.FromMilliseconds(200));
        ShowDialog(session);
        for (int i = 0; i < 4; i++) session.PumpFrame(TimeSpan.FromMilliseconds(100));

        session.Window.SetClientSize(NewWidth, NewHeight);
        session.PumpFrame(TimeSpan.FromMilliseconds(200));
        session.PumpFrame(TimeSpan.FromMilliseconds(200));

        var diff = AccessibilityTreeDiffer.Diff(bridge.CurrentTree, session.Window.Window.BuildAccessibilityTree());
        Assert.True(diff.IsEmpty, $"stale: +{diff.Added.Count} -{diff.Removed.Count} "
            + string.Join(", ", diff.PropertyChanged.Select(c => $"{c.PropertyName} of {c.Node.Role} '{c.Node.Name}'")));
    }

    /// <summary>A window with no overlay open does what it did: only the root is laid out.</summary>
    [Fact]
    public void WithNoOverlayOpen_AResizeIsTheRootsAlone()
    {
        using var session = HeadlessSession.Create(Width, Height);
        var label = TextB.New("The page");
        session.Window.SetRoot(Flex.Column().FlexGrow(1).Children(label));
        session.PumpFrame();

        session.Window.SetClientSize(NewWidth, NewHeight);
        session.PumpFrame();

        Assert.Empty(session.Window.OverlayContents);
        Assert.Equal((float)NewWidth, session.Window.Window.Root!.Layout.Width);
    }
}
