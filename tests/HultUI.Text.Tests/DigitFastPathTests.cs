using HultUI.Core.Styling;
using Xunit;

namespace HultUI.Text.Tests;

/// <summary>
/// The digit fast path composes all-digit segments from a per-(font,size)
/// advance table — but only after proving the font shapes digits context-free
/// (every digit = one zero-offset glyph, every digit pair = exactly summed
/// advances). These tests pin both sides of that gate with deterministic stub
/// shapers: a context-free font enables composition (observable as zero shaper
/// calls for new digit strings), and a font that kerns digit pairs is rejected,
/// keeping the shaper's kerned output byte-for-byte.
/// </summary>
public class DigitFastPathTests
{
    private const float CharW = 10f;

    private static readonly ResolvedInheritedStyle DefaultStyle = new(
        FontFamily: "TestFont", FontSize: 16f, FontWeight: FontWeight.Regular,
        TextColor: new(0, 0, 0, 255), TextDirection: TextDirection.Ltr, Cursor: CursorType.Default);

    [Fact]
    public void ContextFreeFont_PassesProof_NewDigitStringsNeedNoShaperCalls()
    {
        var shaper = new CountingShaper(new StubTextShaper());
        var pipeline = new TextPipeline(new StubFontCollection(CharW), shaper);

        // First digit layout triggers the one-time proof (110 shaper calls).
        var first = pipeline.Layout("111", DefaultStyle, 10_000, null);
        int afterProof = shaper.Calls;
        Assert.True(afterProof >= 110, $"expected proof calls, saw {afterProof}");
        Assert.Equal(3 * CharW, first.Size.Width, 2);

        // Every subsequent never-seen digit string composes from the table.
        var second = pipeline.Layout("2024", DefaultStyle, 10_000, null);
        var third = pipeline.Layout("998877", DefaultStyle, 10_000, null);

        Assert.Equal(afterProof, shaper.Calls);
        Assert.Equal(4 * CharW, second.Size.Width, 2);
        Assert.Equal(6 * CharW, third.Size.Width, 2);

        // Composed glyphs look exactly like the stub shaper's own output.
        var run = Assert.Single(Assert.Single(second.Lines).Runs);
        for (int i = 0; i < run.Glyphs.Length; i++)
        {
            Assert.Equal((uint)"2024"[i], run.Glyphs[i].GlyphId);
            Assert.Equal(i * CharW, run.Glyphs[i].X, 2);
            Assert.Equal(CharW, run.Glyphs[i].Advance, 2);
        }
    }

    [Fact]
    public void DigitKerningFont_FailsProof_AndKeepsTheShaperOutput()
    {
        // This font pulls the second of any digit pair 2px closer — exactly the
        // GPOS behavior the fast path must detect and refuse.
        var shaper = new CountingShaper(new DigitKerningShaper());
        var pipeline = new TextPipeline(new StubFontCollection(CharW), shaper);

        var first = pipeline.Layout("11", DefaultStyle, 10_000, null);
        int afterProof = shaper.Calls;

        // Width must be the shaper's kerned result, not the naive advance sum.
        Assert.Equal(2 * CharW - DigitKerningShaper.Kern, first.Size.Width, 2);

        // Rejected → every new digit string still costs a real shaper call.
        pipeline.Layout("22", DefaultStyle, 10_000, null);
        Assert.Equal(afterProof + 1, shaper.Calls);

        var third = pipeline.Layout("123", DefaultStyle, 10_000, null);
        Assert.Equal(afterProof + 2, shaper.Calls);
        Assert.Equal(3 * CharW - 2 * DigitKerningShaper.Kern, third.Size.Width, 2);
    }

    [Fact]
    public void MixedSegments_NeverTakeTheDigitPath()
    {
        var shaper = new CountingShaper(new StubTextShaper());
        var pipeline = new TextPipeline(new StubFontCollection(CharW), shaper);

        pipeline.Layout("42", DefaultStyle, 10_000, null);   // proof + composition
        int afterProof = shaper.Calls;

        // A digit-adjacent but non-pure segment goes through the normal path.
        pipeline.Layout("abc123", DefaultStyle, 10_000, null);
        Assert.Equal(afterProof + 1, shaper.Calls);
    }

    private sealed class CountingShaper(ITextShaper inner) : ITextShaper
    {
        public int Calls { get; private set; }

        public ShapedRun Shape(string text, IResolvedFont font, float fontSize)
        {
            Calls++;
            return inner.Shape(text, font, fontSize);
        }
    }

    // Monospace like StubTextShaper, but applies a kern adjustment between
    // consecutive digits (positions computed first, advances derived from them,
    // matching SkiaTextShaper's advance convention).
    private sealed class DigitKerningShaper : ITextShaper
    {
        public const float Kern = 2f;

        public ShapedRun Shape(string text, IResolvedFont font, float fontSize)
        {
            var xs = new float[text.Length];
            float x = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (i > 0 && char.IsAsciiDigit(text[i - 1]) && char.IsAsciiDigit(text[i]))
                    x -= Kern;
                xs[i] = x;
                x += CharW;
            }
            var glyphs = new GlyphPosition[text.Length];
            for (int i = 0; i < text.Length; i++)
            {
                float advance = i < text.Length - 1 ? xs[i + 1] - xs[i] : x - xs[i];
                glyphs[i] = new GlyphPosition((uint)text[i], xs[i], 0, advance);
            }
            return new ShapedRun(text, font, fontSize, x, glyphs);
        }
    }
}
