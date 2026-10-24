using HultUI.Core.Styling;
using Xunit;

namespace HultUI.Text.Tests;

/// <summary>
/// <see cref="TextPipeline.MonospaceAsciiAdvance"/>: the advance every printable ASCII character
/// has in a face, given only when the shaper proves it — every one of the 95 drawn from the face
/// itself, each alone and each beside every other one advancing by the same amount, one glyph a
/// character. A TextArea then takes a line of them that fits with a character to spare as fitting
/// without measuring it, so anything the proof lets through would draw a line clipped instead of
/// wrapped: a face that is not monospace, kerns a pair, joins two characters into a ligature or
/// lacks one of them must get no advance.
/// </summary>
public class MonospaceAsciiTests
{
    private static ResolvedInheritedStyle Style(float size = 12f) => new(
        FontFamily: "Mono", FontSize: size, FontWeight: FontWeight.Regular,
        TextColor: new(0, 0, 0, 255), TextDirection: TextDirection.Ltr, Cursor: CursorType.Default);

    [Fact]
    public void AFaceWhosePrintableAsciiAllAdvanceAlike_IsProvenMonospace()
    {
        var shaper = new Shaper();
        var pipeline = new TextPipeline(new Fonts(new Font()), shaper);
        float advance = pipeline.MonospaceAsciiAdvance(Style());
        Assert.Equal(0.6f * 12f, advance);

        // Every line of them measures its count times that, to within rounding.
        var rng = new Random(3);
        for (int i = 0; i < 300; i++)
        {
            var chars = new char[1 + rng.Next(200)];
            for (int c = 0; c < chars.Length; c++) chars[c] = (char)rng.Next(0x20, 0x7F);
            float width = pipeline.MeasureWidth(chars, Style());
            Assert.True(Math.Abs(width - chars.Length * advance) < advance / 100, $"\"{new string(chars)}\": {width}");
        }
    }

    [Fact]
    public void AFaceWithOneCharacterOfAnotherWidth_IsNot()
    {
        var font = new Font();
        font.Advances['i'] = 0.3f;
        Assert.Equal(0f, new TextPipeline(new Fonts(font), new Shaper()).MonospaceAsciiAdvance(Style()));
    }

    /// <summary>A pair kerned closer or apart only shows beside its partner: each character
    /// alone has the one advance.</summary>
    [Theory]
    [InlineData('A', 'V', -1f)]
    [InlineData('~', ' ', 0.5f)]
    [InlineData(' ', '!', 0.25f)]
    public void AFaceThatKernsAPair_IsNot(char first, char second, float kern)
    {
        var shaper = new Shaper { Kern = (first, second, kern) };
        Assert.Equal(0f, new TextPipeline(new Fonts(new Font()), shaper).MonospaceAsciiAdvance(Style()));
    }

    [Fact]
    public void AFaceThatJoinsAPairIntoALigature_IsNot()
    {
        var shaper = new Shaper { Ligature = ('f', 'i') };
        Assert.Equal(0f, new TextPipeline(new Fonts(new Font()), shaper).MonospaceAsciiAdvance(Style()));
    }

    /// <summary>A character the face has no glyph for is drawn from a fallback face, whatever its
    /// advance there: nothing about the face says what it is.</summary>
    [Fact]
    public void AFaceThatLacksACharacter_IsNot()
    {
        var font = new Font();
        font.Missing.Add('`');
        Assert.Equal(0f, new TextPipeline(new Fonts(font), new Shaper()).MonospaceAsciiAdvance(Style()));
    }

    [Fact]
    public void LetterSpacing_GetsNoAdvance()
    {
        var pipeline = new TextPipeline(new Fonts(new Font()), new Shaper());
        Assert.Equal(0f, pipeline.MonospaceAsciiAdvance(Style() with { LetterSpacing = 0.5f }));
    }

    /// <summary>Proven once per face and size, at the size asked: another size is another
    /// proof, another weight another face.</summary>
    [Fact]
    public void TheProof_IsMadeOncePerFaceAndSize()
    {
        var shaper = new Shaper();
        var pipeline = new TextPipeline(new Fonts(new Font()), shaper);
        Assert.Equal(0.6f * 12f, pipeline.MonospaceAsciiAdvance(Style()));
        int calls = shaper.Calls;
        Assert.InRange(calls, 2, 200);
        Assert.Equal(0.6f * 12f, pipeline.MonospaceAsciiAdvance(Style()));
        Assert.Equal(calls, shaper.Calls);
        Assert.Equal(0.6f * 20f, pipeline.MonospaceAsciiAdvance(Style(20f)));
        Assert.True(shaper.Calls > calls);
    }

    /// <summary>The proof shapes every ordered pair of printable ASCII characters side by side,
    /// in pieces short enough for the shaper's float pen to stay exact.</summary>
    [Fact]
    public void TheProofsPieces_HoldEveryPair()
    {
        string pairs = TextPipeline.AllAsciiPairs;
        Assert.Equal(95 * 95 + 1, pairs.Length);
        Assert.All(pairs, c => Assert.InRange(c, ' ', '~'));
        var seen = new HashSet<(char, char)>();
        foreach (var piece in TextPipeline.AsciiPairPieces)
        {
            Assert.InRange(piece.Length, 2, 256);
            for (int i = 1; i < piece.Length; i++) seen.Add((piece[i - 1], piece[i]));
        }
        Assert.Equal(95 * 95, seen.Count);
    }

    /// <summary>
    /// The least a face can kern a pair by is one unit of its em; the shaper's positions are in
    /// 1/512 of an em. Kerned by that, the face is not monospace; advances that differ only by
    /// the float rounding of a pen moving glyph by glyph (a millionth of a pixel) are.
    /// </summary>
    [Fact]
    public void APairKernedByOneUnit_IsNot_ButFloatRoundingIs()
    {
        var kerned = new Shaper { Kern = ('r', 'n', -12f / 512) };
        Assert.Equal(0f, new TextPipeline(new Fonts(new Font()), kerned).MonospaceAsciiAdvance(Style()));
        var noisy = new Shaper { Noise = 2e-6f };
        Assert.Equal(0.6f * 12f, new TextPipeline(new Fonts(new Font()), noisy).MonospaceAsciiAdvance(Style()));
    }

    private sealed class Fonts(Font font) : IFontCollection
    {
        public IResolvedFont Resolve(string familyName, FontWeight weight, FontSlant slant) => font;
        public IResolvedFont? MatchCharacter(int codepoint, FontWeight weight, FontSlant slant) => null;
    }

    private sealed class Font : IResolvedFont
    {
        public readonly Dictionary<char, float> Advances = new();
        public readonly HashSet<char> Missing = new();
        public float AdvanceOf(char c) => Advances.TryGetValue(c, out float a) ? a : 0.6f;
        public string FamilyName => "Mono";
        public float Ascent => -0.8f;
        public float Descent => 0.2f;
        public float Leading => 0f;
        public float LineHeight => 1f;
        public object NativeHandle => this;
        public bool HasGlyph(int codepoint) => codepoint > 0x7E || !Missing.Contains((char)codepoint);
        public void Dispose() { }
    }

    // Each character its face's advance; a kerned pair moves its second glyph, a ligature makes
    // one glyph of two.
    private sealed class Shaper : ITextShaper
    {
        public (char First, char Second, float By)? Kern;
        public (char First, char Second)? Ligature;
        // Added to every other glyph's advance after the first, as a float pen's rounding would.
        public float Noise;
        public int Calls;

        public ShapedRun Shape(string text, IResolvedFont font, float fontSize, bool rtl = false)
        {
            Calls++;
            var f = (Font)font;
            var glyphs = new List<GlyphPosition>();
            float x = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (Ligature is var (a, b) && text[i] == a && i + 1 < text.Length && text[i + 1] == b)
                {
                    float wide = (f.AdvanceOf(a) + f.AdvanceOf(b)) * fontSize * 0.9f;
                    glyphs.Add(new GlyphPosition(0xFB01, x, 0, wide));
                    x += wide;
                    i++;
                    continue;
                }
                float advance = f.AdvanceOf(text[i]) * fontSize;
                if (Kern is var (k1, k2, by) && i + 1 < text.Length && text[i] == k1 && text[i + 1] == k2)
                    advance += by;
                if (i % 2 == 1) advance += Noise;
                glyphs.Add(new GlyphPosition(text[i], x, 0, advance));
                x += advance;
            }
            return new ShapedRun(text, font, fontSize, x, [.. glyphs]);
        }
    }
}
