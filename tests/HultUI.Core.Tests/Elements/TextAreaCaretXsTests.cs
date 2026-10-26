using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using HultUI.Testing;
using Xunit;

namespace HultUI.Core.Tests.Elements;

/// <summary>
/// A text area's accessible text places each character of the rows on screen (and one either side)
/// with one ask of the render context a row (<see cref="IRenderContext.TextCaretXs"/>), not one a
/// character: asked a character at a time, each ask cut the row's string again and looked its
/// layout up again, O(rows × length²) a keystroke while a reader reads (plan 17, round 5; 709 KB a
/// key at 2,000 lines, 310 with the string cut once, then O(rows × length)).
/// </summary>
public class TextAreaCaretXsTests
{
    private readonly LayoutEngine _layout = new(new StubMeasureContext());

    private sealed class Counting : NullRenderContext, IRenderContext
    {
        public int CaretX, CaretXs;
        public readonly List<int> Lengths = [];

        float IRenderContext.TextCaretX(string lineText, ResolvedInheritedStyle style, int offset)
        {
            CaretX++;
            return 7f * Math.Min(offset, lineText.Length);
        }

        void IRenderContext.TextCaretXs(string lineText, ResolvedInheritedStyle style, Span<float> xs)
        {
            CaretXs++;
            Lengths.Add(lineText.Length);
            for (int k = 0; k < xs.Length; k++) xs[k] = 7f * Math.Min(k, lineText.Length);
        }
    }

    [Fact]
    public void TheText_AsksOnceARow_ForEveryBoundaryOnIt()
    {
        var text = string.Join('\n', Enumerable.Range(0, 200).Select(i => $"row {i} with a few words on it"));
        var area = TextAreaElement.New(text);
        area.Box.Width = 700;
        area.Box.Height = 300;
        var root = BoxElement.New();
        root.Box.Width = 900;
        root.Box.Height = 400;
        root.AddChild(area);
        StyleResolver.Resolve(root, Theme.Light.ToResolvedStyle());
        _layout.PerformLayout(root, 900, 400);
        EventDispatcher.DispatchDirect(new FocusEvent(), area);
        var ctx = new Counting();
        area.Render(ctx);
        ctx.CaretX = ctx.CaretXs = 0;
        ctx.Lengths.Clear();

        var accessible = area.AccessibleTextSource!.AccessibleText!;

        Assert.Equal(0, ctx.CaretX);
        Assert.InRange(ctx.CaretXs, 3, 40);   // the rows on screen and one either side
        Assert.All(ctx.Lengths, length => Assert.True(length > 0));
        // Each character's box from its two boundaries, the second row's 'r' at its start.
        int secondRow = accessible.LineStart(1);
        var box = accessible.Geometry!.CharacterBounds(accessible, secondRow + 1);
        Assert.Equal(7f, box.Width);
    }
}
