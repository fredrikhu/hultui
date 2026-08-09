using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using Xunit;

namespace HultUI.Core.Tests.Elements;

/// <summary>
/// Guards that elements added to the tree *after* the first frame still get
/// their inherited style resolved. Structural ops (AddChild/InsertChild/
/// ReplaceChildren) mark layout + children dirty; they must also flag the new
/// subtree (and its ancestor path) style-dirty, otherwise the style pass never
/// reaches it and its text measures with a zero font size — i.e. zero height,
/// so nothing paints even though the rows take up space.
/// </summary>
public class StructuralStyleTests
{
    // Text height mirrors the *resolved* font size, so an unstyled element
    // (font size 0) measures zero height — exactly the symptom under test.
    private sealed class FontSizeMeasureContext : IMeasureContext
    {
        public Size MeasureText(string text, ResolvedInheritedStyle style, float maxWidth, int? maxLines,
            TextOverflow overflow = TextOverflow.Clip)
            => string.IsNullOrEmpty(text) ? Size.Zero : new Size(text.Length * 7f, style.FontSize);
    }

    private static void ClearAllFlags(Element e)
    {
        e.DirtyFlags = DirtyFlags.None;
        foreach (var c in e.Children)
            ClearAllFlags(c);
    }

    // Runs one frame's worth of style + layout, matching the render loop: the
    // style pass only runs when the root is style-dirty.
    private static void Frame(LayoutEngine engine, Element root, Theme theme)
    {
        if ((root.DirtyFlags & DirtyFlags.Style) != 0)
            StyleResolver.Resolve(root, theme);
        engine.PerformLayout(root, 400, 400);
    }

    [Fact]
    public void ChildAddedAfterFirstFrame_IsStyledAndMeasuresNonZeroHeight()
    {
        var theme = Theme.Light;
        var engine = new LayoutEngine(new FontSizeMeasureContext());
        var root = Flex.Column();

        // First frame settles, then the render loop clears all dirty flags.
        Frame(engine, root, theme);
        ClearAllFlags(root);

        // A new row is added later (the benchmark's create/append scenario). The
        // text inherits its font size from the theme — it sets none of its own.
        var text = Text.New("hello");
        root.AddChild(text);

        Frame(engine, root, theme);

        Assert.True(text.ResolvedInherited.FontSize > 0,
            "child added after the first frame never got its inherited style resolved");
        Assert.True(text.Layout.AbsoluteBorderBox.Height > 0,
            "unstyled child measured zero height, so it would paint nothing");
    }

    [Fact]
    public void ReplaceChildren_AfterFirstFrame_StylesTheNewChildren()
    {
        var theme = Theme.Light;
        var engine = new LayoutEngine(new FontSizeMeasureContext());
        var root = Flex.Column();

        Frame(engine, root, theme);
        ClearAllFlags(root);

        // Replace the (empty) child set with freshly built rows, as the benchmark
        // does via ReplaceChildren.
        var a = Text.New("one");
        var b = Text.New("two");
        root.ReplaceChildren(a, b);

        Frame(engine, root, theme);

        Assert.True(a.Layout.AbsoluteBorderBox.Height > 0);
        Assert.True(b.Layout.AbsoluteBorderBox.Height > 0);
    }
}
