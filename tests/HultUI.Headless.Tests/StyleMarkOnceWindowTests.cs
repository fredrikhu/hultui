using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Styling;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// A mark for style walks a subtree once and says so (<see cref="DirtyFlags.StyleSubtree"/>); the
/// window's style pass ends the claim with the marks, so the next frame's marks walk again.
/// </summary>
public class StyleMarkOnceWindowTests
{
    private static IEnumerable<Element> All(Element e) => e.Children.SelectMany(All).Prepend(e);

    [Fact]
    public void TheStylePass_EndsTheClaim_SoEveryLaterMarkWalksAgain()
    {
        using var session = HeadlessSession.Create(300, 200);
        var text = TextElement.New("label");
        var root = Box.New().Children(Box.New().Children(Box.New().Children(text)));
        session.Window.SetRoot(root);
        session.PumpFrames(2);
        Assert.All(All(root), e => Assert.Equal(DirtyFlags.None,
            e.DirtyFlags & (DirtyFlags.Style | DirtyFlags.StyleSubtree)));

        foreach (float size in new[] { 20f, 24f, 11f })
        {
            root.Inherited.FontSize = size;
            root.MarkStyleDirty();
            session.PumpFrame();
            Assert.Equal(size, text.ResolvedInherited.FontSize);
        }
    }

    [Fact]
    public void WhileMinimized_EachFramesStylePass_EndsTheClaim()
    {
        // A minimized window's frames stop before the end-of-frame clear that a drawn frame
        // makes of every flag; the style pass's own clear is all that ends the claim there.
        using var session = HeadlessSession.Create(300, 200);
        var text = TextElement.New("label");
        var root = Box.New().Children(Box.New().Children(text));
        session.Window.SetRoot(root);
        session.PumpFrames(2);
        session.Window.Minimized = true;
        session.PumpFrame();

        foreach (float size in new[] { 20f, 24f, 11f })
        {
            root.Inherited.FontSize = size;
            root.MarkStyleDirty();
            Assert.False(session.PumpFrame(), "a minimized window drew a frame");
            Assert.Equal(size, text.ResolvedInherited.FontSize);
        }
        session.Window.Minimized = false;
        session.PumpFrame();
        Assert.Equal(11f, text.ResolvedInherited.FontSize);
    }

    [Fact]
    public void AThemeSwitch_InTheFrameOfARebuild_ReachesEveryElement()
    {
        using var session = HeadlessSession.Create(300, 200);
        var branch = Box.New().Children(Box.New().Children(TextElement.New("a")), TextElement.New("b"));
        var root = Box.New().Children(branch, Box.New().Children(TextElement.New("c")));
        session.Window.SetRoot(root);
        session.PumpFrames(2);

        branch.MarkStyleDirty();   // what a patch does
        session.Application.Theme = Theme.Dark;
        session.PumpFrame();

        Assert.All(All(root), e => Assert.Equal(Theme.Dark.TextColor, e.ResolvedInherited.TextColor));
    }

    [Fact]
    public void AnOverlaysContent_IsRestyledOnEveryFrameItIsMarked()
    {
        using var session = HeadlessSession.Create(400, 300);
        session.Window.SetRoot(Box.New());
        session.PumpFrame();
        var text = TextElement.New("in an overlay");
        var content = Box.New().FontSize(12f).Children(Box.New().Children(text));
        session.Window.Window.ShowOverlay(content, 10, 10);
        session.PumpFrame();
        Assert.Equal(12f, text.ResolvedInherited.FontSize);

        foreach (float size in new[] { 20f, 26f })
        {
            content.Inherited.FontSize = size;
            content.MarkStyleDirty();
            session.PumpFrame();
            Assert.Equal(size, text.ResolvedInherited.FontSize);
        }
    }
}
