using HultUI.Core.Layout;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using HultUI.Core.Tests.Elements;
using Xunit;

namespace HultUI.Core.Tests.Rendering;

/// <summary>
/// <see cref="IRenderContext.TextCaretXs"/>'s default asks <see cref="IRenderContext.TextCaretX"/>
/// for each boundary, so a context that does not lay text out answers exactly what it answered one
/// boundary at a time.
/// </summary>
public class TextCaretXsDefaultTests
{
    private sealed class Counting : NullRenderContext, IRenderContext
    {
        public int Calls;

        float IRenderContext.TextCaretX(string lineText, ResolvedInheritedStyle style, int offset)
        {
            Calls++;
            return 10f * Math.Min(offset, lineText.Length) + 0.5f;
        }
    }

    private static readonly ResolvedInheritedStyle Style = new(
        FontFamily: "sans-serif", FontSize: 14f, FontWeight: FontWeight.Regular,
        TextColor: new(0, 0, 0, 255), TextDirection: TextDirection.Ltr, Cursor: CursorType.Default);

    [Fact]
    public void TheDefault_AsksTextCaretXForEachBoundary()
    {
        var counting = new Counting();
        IRenderContext context = counting;
        var xs = new float[7];
        context.TextCaretXs("hello", Style, xs);
        Assert.Equal([0.5f, 10.5f, 20.5f, 30.5f, 40.5f, 50.5f, 50.5f], xs);
        Assert.Equal(7, counting.Calls);
    }
}
