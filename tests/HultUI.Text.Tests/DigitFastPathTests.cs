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

    // ── Number punctuation (docs/plans/19-run-shaping.md) ──

    [Fact]
    public void TheMarks_AreInTheirSlots()
    {
        for (int i = 0; i < TextPipeline.NumberMarks.Length; i++)
            Assert.Equal(10 + i, TextPipeline.Slot(TextPipeline.NumberMarks[i]));
        for (char d = '0'; d <= '9'; d++)
            Assert.Equal(d - '0', TextPipeline.Slot(d));
        foreach (char other in "aZ (—$#")
            Assert.Equal(-1, TextPipeline.Slot(other));
    }

    [Fact]
    public void ANumberWithPunctuation_IsComposed_OnceEachOfItsMarksPassesItsProof()
    {
        var shaper = new CountingShaper(new StubTextShaper());
        var pipeline = new TextPipeline(new StubFontCollection(CharW), shaper);

        pipeline.Layout("42", DefaultStyle, 10_000, null);
        Assert.Equal(110, shaper.Calls);   // the digits' proof only: no mark met yet

        // The first minus sign proves '-': alone, beside itself, beside each digit both ways
        // round (20), and between every two digits (100).
        var signed = pipeline.Layout("-38913", DefaultStyle, 10_000, null);
        Assert.Equal(110 + 122, shaper.Calls);
        Assert.Equal(6 * CharW, signed.Size.Width, 2);

        // The next mark also meets the one proven before it, both ways round.
        pipeline.Layout("1,234", DefaultStyle, 10_000, null);
        Assert.Equal(110 + 122 + 124, shaper.Calls);

        // From then on numbers with either mark cost no shaper call, alone or inside a run.
        var label = pipeline.Layout("Item -7 of 1,234,567 - 12-3", DefaultStyle, 10_000, null);
        Assert.Equal(110 + 122 + 124 + 4, shaper.Calls);   // "Item", "of", " " and the lone "-": words
        Assert.Equal(27 * CharW, label.Size.Width, 2);

        var run = Assert.Single(Assert.Single(signed.Lines).Runs);
        for (int i = 0; i < run.Glyphs.Length; i++)
        {
            Assert.Equal((uint)"-38913"[i], run.Glyphs[i].GlyphId);
            Assert.Equal(i * CharW, run.Glyphs[i].X, 2);
            Assert.Equal(CharW, run.Glyphs[i].Advance, 2);
        }
    }

    [Fact]
    public void MarksAlone_AreWords_AndProveNothing()
    {
        var shaper = new CountingShaper(new StubTextShaper());
        var pipeline = new TextPipeline(new StubFontCollection(CharW), shaper);

        pipeline.Layout("-", DefaultStyle, 10_000, null);
        pipeline.Layout("...", DefaultStyle, 10_000, null);
        Assert.Equal(2, shaper.Calls);   // shaped as words; no digit proof, no mark proof
    }

    [Fact]
    public void AMarkThatKernsBesideADigit_IsRefused_AndTheOthersStillCompose()
    {
        var shaper = new CountingShaper(new MarkKerningShaper());
        var pipeline = new TextPipeline(new StubFontCollection(CharW), shaper);

        var refused = pipeline.Layout("3.5", DefaultStyle, 10_000, null);
        int afterProofs = shaper.Calls;
        // The shaper's kerned width, not the advances' sum.
        Assert.Equal(3 * CharW - MarkKerningShaper.Kern, refused.Size.Width, 2);

        // Refused for good: each new number with a period is shaped, and nothing is proven again.
        pipeline.Layout("7.25", DefaultStyle, 10_000, null);
        Assert.Equal(afterProofs + 1, shaper.Calls);

        // The digits and the other marks are not touched by it.
        pipeline.Layout("-5", DefaultStyle, 10_000, null);
        int afterMinus = shaper.Calls;
        pipeline.Layout("-66", DefaultStyle, 10_000, null);
        pipeline.Layout("2024", DefaultStyle, 10_000, null);
        Assert.Equal(afterMinus, shaper.Calls);
    }

    [Fact]
    public void AMarkWithAContextualFormBetweenDigits_IsRefused()
    {
        // The colon takes another glyph with a digit on each side: every pair passes, only the
        // digit–colon–digit triple shows it.
        var shaper = new CountingShaper(new ContextualColonShaper());
        var pipeline = new TextPipeline(new StubFontCollection(CharW), shaper);

        var time = pipeline.Layout("12:30", DefaultStyle, 10_000, null);
        var run = Assert.Single(Assert.Single(time.Lines).Runs);
        Assert.Equal(ContextualColonShaper.RaisedColon, run.Glyphs[2].GlyphId);

        int after = shaper.Calls;
        pipeline.Layout("09:41", DefaultStyle, 10_000, null);
        Assert.Equal(after + 1, shaper.Calls);
    }

    [Fact]
    public void TheReference_ComposesDigitsOnly()
    {
        var shaper = new CountingShaper(new StubTextShaper());
        var pipeline = new TextPipeline(new StubFontCollection(CharW), shaper) { ComposesNumberPunctuation = false };

        pipeline.Layout("42", DefaultStyle, 10_000, null);
        int afterProof = shaper.Calls;
        pipeline.Layout("-38913", DefaultStyle, 10_000, null);
        pipeline.Layout("-27146", DefaultStyle, 10_000, null);
        Assert.Equal(afterProof + 2, shaper.Calls);
    }

    // Monospace like StubTextShaper, but pulls a period 2px towards a digit before it.
    private sealed class MarkKerningShaper : ITextShaper
    {
        public const float Kern = 2f;

        public ShapedRun Shape(string text, IResolvedFont font, float fontSize, bool rtl = false) =>
            Positioned(text, font, fontSize, i => i > 0 && text[i] == '.' && char.IsAsciiDigit(text[i - 1]) ? Kern : 0f,
                i => (uint)text[i]);
    }

    // Monospace like StubTextShaper, but a colon with a digit on each side is another glyph.
    private sealed class ContextualColonShaper : ITextShaper
    {
        public const uint RaisedColon = 0xE03A;

        public ShapedRun Shape(string text, IResolvedFont font, float fontSize, bool rtl = false) =>
            Positioned(text, font, fontSize, _ => 0f,
                i => text[i] == ':' && i > 0 && i + 1 < text.Length && char.IsAsciiDigit(text[i - 1]) && char.IsAsciiDigit(text[i + 1])
                    ? RaisedColon : (uint)text[i]);
    }

    // One glyph per character, each pulled back by kern(i) before it is placed; advances derived
    // from the positions, as SkiaTextShaper derives them.
    private static ShapedRun Positioned(string text, IResolvedFont font, float fontSize, Func<int, float> kern, Func<int, uint> glyph)
    {
        var xs = new float[text.Length];
        float x = 0;
        for (int i = 0; i < text.Length; i++)
        {
            x -= kern(i);
            xs[i] = x;
            x += CharW;
        }
        var glyphs = new GlyphPosition[text.Length];
        for (int i = 0; i < text.Length; i++)
            glyphs[i] = new GlyphPosition(glyph(i), xs[i], 0, i < text.Length - 1 ? xs[i + 1] - xs[i] : x - xs[i]);
        return new ShapedRun(text, font, fontSize, x, glyphs);
    }

    private sealed class CountingShaper(ITextShaper inner) : ITextShaper
    {
        public int Calls { get; private set; }

        public ShapedRun Shape(string text, IResolvedFont font, float fontSize, bool rtl = false)
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

        public ShapedRun Shape(string text, IResolvedFont font, float fontSize, bool rtl = false)
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
