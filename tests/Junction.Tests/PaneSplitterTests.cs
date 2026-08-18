using System.Reflection;
using HultUI.Core.Elements;
using HultUI.Core.Styling;
using HultUI.Headless;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// The draggable border between the file list and the diff. The width is app state and the
/// pane is rebuilt from it, so what these check is that a drag survives the rebuild it
/// causes — a width written onto the element would be correct for one frame and then gone.
/// </summary>
[Collection("JunctionDiff")]
public class PaneSplitterTests
{
    [Fact]
    public void DraggingTheBorderWidensTheFileList()
    {
        DiffScrollTests.Hosted((session, app) =>
        {
            var handle = Handle(session);
            float before = Splitter(app).Width;
            var mid = Center(handle);

            session.Window.MouseDown(mid.X, mid.Y);
            session.Window.MoveMouse(mid.X + 90f, mid.Y);
            session.Window.MouseUp(mid.X + 90f, mid.Y);
            session.PumpFrames(2);

            Assert.Equal(before + 90f, Splitter(app).Width, 1);
            // And the column laid out at the new width, not just the state behind it.
            Assert.Equal(Splitter(app).Width, PaneLeftOf(session, Handle(session)).Layout.AbsoluteBorderBox.Width, 1);
        });
    }

    [Fact]
    public void TheDragIsClampedSoNeitherPaneCanBeSqueezedAway()
    {
        DiffScrollTests.Hosted((session, app) =>
        {
            var mid = Center(Handle(session));

            // All the way to one edge of the window, then all the way to the other. Kept
            // inside the window: a pointer past its edge hits nothing, so nothing hears the
            // move — same as the diff's own scrollbar drag.
            session.Window.MouseDown(mid.X, mid.Y);
            session.Window.MoveMouse(4f, mid.Y);
            session.PumpFrames(2);
            float narrow = Splitter(app).Width;

            session.Window.MoveMouse(1396f, mid.Y);
            session.Window.MouseUp(1396f, mid.Y);
            session.PumpFrames(2);
            float wide = Splitter(app).Width;

            Assert.Equal(Const(app, "MinFileListWidth"), narrow, 1);
            Assert.Equal(Const(app, "MaxFileListWidth"), wide, 1);
        });
    }

    [Fact]
    public void APointerMoveWithNoGrabDoesNotResize()
    {
        DiffScrollTests.Hosted((session, app) =>
        {
            float before = Splitter(app).Width;
            var mid = Center(Handle(session));

            session.Window.MoveMouse(mid.X, mid.Y);
            session.Window.MoveMouse(mid.X + 120f, mid.Y);
            session.PumpFrames(2);

            Assert.Equal(before, Splitter(app).Width, 1);
        });
    }

    private static PaneSplitter Splitter(JunctionApp app) =>
        (PaneSplitter)typeof(JunctionApp)
            .GetField("_changesSplit", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(app)!;

    private static float Const(JunctionApp app, string name) =>
        (float)typeof(JunctionApp).GetField(name, BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;

    private static HultUI.Core.Primitives.Point Center(Element e)
    {
        var b = e.Layout.AbsoluteBorderBox;
        return new HultUI.Core.Primitives.Point(b.X + b.Width / 2f, b.Y + b.Height / 2f);
    }

    /// <summary>The pane the splitter sizes — its sibling immediately to the left.</summary>
    private static Element PaneLeftOf(HeadlessSession session, Element handle)
    {
        var siblings = handle.Parent!.Children;
        for (int i = 1; i < siblings.Count; i++)
            if (ReferenceEquals(siblings[i], handle)) return siblings[i - 1];
        throw new Xunit.Sdk.XunitException("the handle has no pane to its left");
    }

    /// <summary>The splitter handle: the one box in the tree that asks for a resize cursor.</summary>
    private static Element Handle(HeadlessSession session)
    {
        Element? found = null;
        Walk(Root(session));
        return found ?? throw new Xunit.Sdk.XunitException("the splitter handle is not on screen");

        void Walk(Element? e)
        {
            if (found != null || e is null) return;
            if (e is BoxElement && e.Inherited.Cursor == CursorType.ResizeEW
                && e.Layout.AbsoluteBorderBox.Height > 0)
            { found = e; return; }
            foreach (var c in e.Children) Walk(c);
        }
    }

    private static Element? Root(HeadlessSession session) =>
        (Element?)typeof(HultUI.Window)
            .GetField("_root", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(session.Window.Window);
}
