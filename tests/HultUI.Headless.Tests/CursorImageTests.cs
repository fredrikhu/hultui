using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Styling;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// Image cursors (<see cref="CursorType.Image"/>): the window passes the picture of the hovered
/// element's resolved style to the platform, inherited with the cursor type as one value.
/// </summary>
public class CursorImageTests
{
    private static CursorImage Image() => new(2, 2, new byte[16], 0, 0);

    [Fact]
    public void MoveMouse_AppliesImageCursor()
    {
        using var session = HeadlessSession.Create();
        var image = Image();
        var view = Box.New().Width(100).Height(40).Cursor(image);
        session.Window.SetRoot(Flex.Column().Padding(10).Children(view));
        session.PumpFrame();

        session.Window.MoveMouse(60, 30);
        Assert.Equal(CursorType.Image, session.Window.LastCursor);
        Assert.Same(image, session.Window.LastCursorImage);

        session.Window.MoveMouse(5, 5);   // off the element
        Assert.Equal(CursorType.Default, session.Window.LastCursor);
        Assert.Null(session.Window.LastCursorImage);
    }

    [Fact]
    public void Children_InheritTheImage()
    {
        using var session = HeadlessSession.Create();
        var image = Image();
        var inner = Box.New().Width(50).Height(20);
        var view = Box.New().Width(100).Height(40).Cursor(image).Children(inner);
        session.Window.SetRoot(Flex.Column().Padding(10).Children(view));
        session.PumpFrame();

        session.Window.MoveMouse(15, 15);   // over the inner box
        Assert.Same(image, session.Window.LastCursorImage);
    }

    [Fact]
    public void ChildCursorType_ReplacesTheParentsImage()
    {
        using var session = HeadlessSession.Create();
        var field = Box.New().Width(50).Height(20).Cursor(CursorType.Text);
        var view = Box.New().Width(100).Height(40).Cursor(Image()).Children(field);
        session.Window.SetRoot(Flex.Column().Padding(10).Children(view));
        session.PumpFrame();

        session.Window.MoveMouse(15, 15);   // over the child
        Assert.Equal(CursorType.Text, session.Window.LastCursor);
        Assert.Null(session.Window.LastCursorImage);
    }

    [Fact]
    public void ChangedImage_ShowsWithoutThePointerMoving()
    {
        using var session = HeadlessSession.Create();
        var view = Box.New().Width(100).Height(40).Cursor(Image());
        session.Window.SetRoot(Flex.Column().Padding(10).Children(view));
        session.PumpFrame();
        session.Window.MoveMouse(60, 30);

        // A remote desktop's pointer changes shape while the local pointer stands still.
        var next = Image();
        view.Cursor(next);
        view.MarkStyleDirty();
        session.PumpFrame();

        Assert.Same(next, session.Window.LastCursorImage);
    }

    [Fact]
    public void ImageTypeWithoutAnImage_ShowsTheArrow()
    {
        using var session = HeadlessSession.Create();
        var view = Box.New().Width(100).Height(40);
        view.Inherited.Cursor = CursorType.Image;
        session.Window.SetRoot(Flex.Column().Padding(10).Children(view));
        session.PumpFrame();

        session.Window.MoveMouse(60, 30);
        Assert.Equal(CursorType.Default, session.Window.LastCursor);
    }
}
