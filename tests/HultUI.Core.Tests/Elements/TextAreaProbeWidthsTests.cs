using System.Reflection;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using Xunit;

namespace HultUI.Core.Tests.Elements;

/// <summary>
/// A line no prefix table covers (the context gives none, as the Skia context gives none for BiDi
/// text: right to left, emoji, Thai, marks) is searched one probe at a time, and each probe is the
/// width of a span of the document (<see cref="IRenderContext.MeasureTextWidth"/>), which the Skia
/// context finds again in its width cache when a drag asks for it at the next width. Each probe was
/// a string of its own, laid out whole (<see cref="IRenderContext.MeasureText"/>). The rows are the
/// same: the two give the same widths.
/// </summary>
public class TextAreaProbeWidthsTests
{
    private readonly LayoutEngine _layout = new(new StubMeasureContext());

    // Widths that differ from character to character, and how many of each kind of call it was asked.
    private class Widths : NullRenderContext, IRenderContext
    {
        public int Texts, Spans;

        public static float Width(ReadOnlySpan<char> text)
        {
            float w = 0;
            foreach (char c in text) w += 5f + c % 5;
            return w;
        }

        public override Size MeasureText(string text, ResolvedInheritedStyle style)
        {
            if (text != "X") Texts++;   // the line height
            return new(Width(text), style.FontSize);
        }

        public virtual float MeasureTextWidth(ReadOnlySpan<char> text, ResolvedInheritedStyle style)
        {
            Spans++;
            return Width(text);
        }
    }

    // The default: MeasureText(text.ToString()).Width.
    private sealed class TextsOnly : NullRenderContext
    {
        public override Size MeasureText(string text, ResolvedInheritedStyle style) => new(Widths.Width(text), style.FontSize);
    }

    private TextAreaElement Mount(string text, IRenderContext ctx)
    {
        var area = TextAreaElement.New(text);
        area.Box.Width = 700;
        area.Box.Height = 300;
        var root = BoxElement.New();
        root.Box.Width = 900;
        root.Box.Height = 400;
        root.AddChild(area);
        StyleResolver.Resolve(root, Theme.Light.ToResolvedStyle());
        _layout.PerformLayout(root, 900, 400);
        typeof(TextAreaElement).GetField("_renderCtx", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(area, ctx);
        return area;
    }

    private static readonly FieldInfo LinesValid =
        typeof(TextAreaElement).GetField("_linesValid", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static int[] Rows(TextAreaElement area, float width)
    {
        LinesValid.SetValue(area, false);
        return area.GetLineStarts(width, area.ResolvedInherited).ToArray();
    }

    [Fact]
    public void AWrappedLineWithoutAPrefixTable_IsSearchedThroughSpanWidths_ToTheSameRows()
    {
        // Arabic words and Latin ones, two long hard lines and a short one.
        string line = string.Join(' ', Enumerable.Range(0, 60).Select(i => i % 4 == 0 ? "dotnet" : new string((char)(0x0627 + i % 20), 3 + i % 6)));
        string text = line + "\nقصير\n" + line;
        var ctx = new Widths();
        var area = Mount(text, ctx);
        var reference = Mount(text, new TextsOnly());
        foreach (float width in new[] { 300f, 260f, 261f, 420f, 180f })
        {
            ctx.Texts = ctx.Spans = 0;
            var rows = Rows(area, width);
            Assert.Equal(Rows(reference, width), rows);
            Assert.True(rows.Length > 6, $"at {width} only {rows.Length} rows");
            Assert.True(ctx.Texts == 0, $"at {width} {ctx.Texts} probes were laid out as strings");
            Assert.True(ctx.Spans >= rows.Length, $"at {width} {ctx.Spans} widths of spans for {rows.Length} rows");
        }
    }
}
