using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using HultUI.Core.Threading;
using Xunit;

namespace HultUI.Core.Tests.Elements;

/// <summary>
/// <see cref="TextElement.MaxLines"/> and <see cref="TextElement.Overflow"/> decide what a label
/// measures, so changing either on a label already laid out lays it out again — as a change of
/// its text does. They were plain properties: only a rebuild's patch noticed a change, and a label
/// changed in place kept its old size, its flex base and its minimum.
/// </summary>
public class TextLayoutPropertyTests
{
    /// <summary>Measures as the stub does, but an ellipsis takes room of its own: a truncated label
    /// is narrower with one than clipped, as in the real pipeline.</summary>
    private sealed class OverflowContext : IMeasureContext
    {
        private readonly StubMeasureContext _stub = new();

        public Size MeasureText(string text, ResolvedInheritedStyle style, float maxWidth, int? maxLines,
            TextOverflow overflow = TextOverflow.Clip)
        {
            var size = _stub.MeasureText(text, style, maxWidth, maxLines, overflow);
            return overflow == TextOverflow.Ellipsis && text.Length * 7f > maxWidth && maxLines == 1
                ? size with { Width = size.Width - 5f }
                : size;
        }
    }

    private const string Long = "a label far too long for the column it sits in";

    private static (Element Root, TextElement Label, LayoutEngine Engine) LaidOut(IMeasureContext context)
    {
        var label = Text.New(Long);
        var root = Flex.Column().AlignItems(AlignItems.FlexStart).Children(
            Box.New().Width(Dimension.Px(100)).AlignItems(AlignItems.FlexStart).Children(label));
        var engine = new LayoutEngine(context);
        StyleResolver.Resolve(root);
        engine.PerformLayout(root, 800, 600);
        return (root, label, engine);
    }

    [Fact]
    public void AMaxLinesChangeLaysTheLabelOutAgain()
    {
        var (root, label, engine) = LaidOut(new StubMeasureContext());
        Assert.True(label.Layout.Height > 14f);   // wrapped

        label.MaxLines = 1;
        engine.PerformLayout(root, 800, 600);
        Assert.Equal(14f, label.Layout.Height);

        label.MaxLines = null;
        engine.PerformLayout(root, 800, 600);
        Assert.True(label.Layout.Height > 14f);
    }

    [Fact]
    public void AnOverflowChangeLaysTheLabelOutAgain()
    {
        var (root, label, engine) = LaidOut(new OverflowContext());
        label.MaxLines = 1;
        engine.PerformLayout(root, 800, 600);
        float clipped = label.Layout.Width;

        label.Overflow = TextOverflow.Ellipsis;
        engine.PerformLayout(root, 800, 600);

        Assert.Equal(clipped - 5f, label.Layout.Width);
    }

    /// <summary>
    /// A label built off the UI thread with <c>.MaxLines(1)</c> and <c>.TextOverflow(…)</c> does
    /// not throw where the thread is checked (Debug builds): a new label is marked for layout and
    /// paint already, and marking it again — the only part that checks — is skipped. Once it is
    /// laid out, a change on the UI thread lays it out again, and the same change from another
    /// thread still throws. The setters began marking in 19b94e9; until then building such a label
    /// anywhere touched no mark.
    /// </summary>
    [Fact]
    public void ALabelBuiltOffTheUIThread_TakesMaxLines_AndALaidOutOneStillChecks()
    {
        bool wasEnforcing = UIThread.Enforce;
        UIThread.Enforce = true;
        UIThread.Bind();
        try
        {
            TextElement? built = null;
            Assert.Null(RunOffThread(() => built = Text.New(Long).MaxLines(1).TextOverflow(TextOverflow.Ellipsis)));
            var label = built!;

            var root = Flex.Column().AlignItems(AlignItems.FlexStart).Children(
                Box.New().Width(Dimension.Px(100)).AlignItems(AlignItems.FlexStart).Children(label));
            var engine = new LayoutEngine(new StubMeasureContext());
            StyleResolver.Resolve(root);
            engine.PerformLayout(root, 800, 600);
            Assert.Equal(14f, label.Layout.Height);

            label.MaxLines = null;
            engine.PerformLayout(root, 800, 600);
            Assert.True(label.Layout.Height > 14f);   // wrapped again

            Assert.IsType<InvalidOperationException>(RunOffThread(() => label.MaxLines = 2));
        }
        finally
        {
            UIThread.Unbind();
            UIThread.Enforce = wasEnforcing;
        }
    }

    private static Exception? RunOffThread(Action action)
    {
        Exception? caught = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { caught = ex; }
        });
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "off-thread work did not finish");
        return caught;
    }

    [Fact]
    public void WritingTheSameValueMarksNothing()
    {
        var (root, label, engine) = LaidOut(new StubMeasureContext());
        label.MaxLines = 2;
        label.Overflow = TextOverflow.Ellipsis;
        engine.PerformLayout(root, 800, 600);
        root.DirtyFlags = DirtyFlags.None;
        label.DirtyFlags = DirtyFlags.None;

        label.MaxLines = 2;
        label.Overflow = TextOverflow.Ellipsis;

        Assert.Equal(DirtyFlags.None, label.DirtyFlags);
        Assert.Equal(DirtyFlags.None, root.DirtyFlags);
    }
}
