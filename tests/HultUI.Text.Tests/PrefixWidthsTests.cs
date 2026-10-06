using HultUI.Core.Styling;
using HultUI.Text;
using Xunit;

namespace HultUI.Text.Tests;

/// <summary>
/// <see cref="TextPipeline.MeasurePrefixes"/>: each prefix measures what it does laid out on a
/// line of its own, read off one table of the text. A prefix is a paragraph of its own, so one
/// that stops short of the text's kana or Hangul can draw its Han characters in another face
/// than the whole text does. The faces here differ in width, so a table that measured every
/// prefix in the whole text's face would answer wrong.
/// </summary>
public class PrefixWidthsTests
{
    [Theory]
    [InlineData("東京に行きます")]          // Han, then kana: Japanese from the third char
    [InlineData("大韓民國 서울")]          // Korean from the sixth
    [InlineData("カナ 漢字 한글 漢字")]    // Japanese, then both: no answer
    [InlineData("漢字 only")]              // never an answer
    [InlineData("hello world  twice")]
    public void EveryPrefix_MeasuresWhatItDoesOnItsOwnLine(string text)
    {
        foreach (float letterSpacing in new[] { 0f, 0.5f })
        {
            var style = Style() with { LetterSpacing = letterSpacing };
            var prefixes = Create().MeasurePrefixes(text, style);
            Assert.NotNull(prefixes);
            Assert.Equal(text.Length, prefixes.Length);
            for (int n = 0; n <= text.Length; n++)
            {
                float expected = Create().Layout(text[..n], style, float.PositiveInfinity, maxLines: 1).Size.Width;
                Assert.Equal(expected, prefixes.Width(n));
            }
        }
    }

    [Fact]
    public void APrefixWithoutTheKana_TakesTheFaceItsOwnLineDoes()
    {
        var prefixes = Create().MeasurePrefixes("東京に行きます", Style())!;
        Assert.Equal(2 * 15f, prefixes.Width(2));         // no kana: the face that says nothing
        Assert.Equal(3 * 10f, prefixes.Width(3));         // with it: the Japanese one
    }

    [Fact]
    public void TextThatIsNotTheSumOfItsWords_GetsNoTable()
    {
        var pipeline = Create();
        Assert.Null(pipeline.MeasurePrefixes("", Style()));
        Assert.Null(pipeline.MeasurePrefixes("two\nlines", Style()));
        Assert.Null(pipeline.MeasurePrefixes("שלום", Style()));
        Assert.Null(pipeline.MeasurePrefixes("left to right", Style() with { TextDirection = TextDirection.Rtl }));
        Assert.Null(new TextPipeline(new Fonts(), new FontAdvanceShaper()) { ReuseAcrossWidths = false }
            .MeasurePrefixes("the reference path", Style()));
    }

    [Fact]
    public void APrefixLongerThanTheText_IsAnError()
    {
        var prefixes = Create().MeasurePrefixes("abc", Style())!;
        Assert.Throws<ArgumentOutOfRangeException>(() => prefixes.Width(4));
        Assert.Throws<ArgumentOutOfRangeException>(() => prefixes.Width(-1));
    }

    private static TextPipeline Create() => new(new Fonts(), new FontAdvanceShaper());

    private static ResolvedInheritedStyle Style() => new(
        FontFamily: "Latin", FontSize: 10f, FontWeight: FontWeight.Regular,
        TextColor: new(0, 0, 0, 255), TextDirection: TextDirection.Ltr, Cursor: CursorType.Default);

    /// <summary>A primary without CJK, and a fallback face per Han context, each its own width.</summary>
    private sealed class Fonts : IFontCollection
    {
        private readonly Font _latin = new("Latin", cjk: false, advance: 0.6f);
        private readonly Dictionary<string, Font> _cjk = new();

        public IResolvedFont Resolve(string familyName, FontWeight weight, FontSlant slant) => _latin;

        public IResolvedFont? MatchCharacter(int codepoint, FontWeight weight, FontSlant slant)
            => MatchCharacter(codepoint, weight, slant, null);

        public IResolvedFont? MatchCharacter(int codepoint, FontWeight weight, FontSlant slant, string? hanContext)
        {
            string key = hanContext ?? "none";
            if (!_cjk.TryGetValue(key, out var font))
                _cjk[key] = font = new Font("CJK " + key, cjk: true, advance: key switch { "ja" => 1f, "ko" => 1.25f, _ => 1.5f });
            return font;
        }
    }

    private sealed class Font(string family, bool cjk, float advance) : IResolvedFont
    {
        public float Advance => advance;
        public string FamilyName => family;
        public float Ascent => -0.8f;
        public float Descent => 0.2f;
        public float Leading => 0f;
        public float LineHeight => 1f;
        public object NativeHandle => this;
        public bool HasGlyph(int codepoint) => cjk || codepoint < 0x2E80;
        public void Dispose() { }
    }

    private sealed class FontAdvanceShaper : ITextShaper
    {
        public ShapedRun Shape(string text, IResolvedFont font, float fontSize, bool rtl = false)
        {
            float advance = ((Font)font).Advance * fontSize;
            var glyphs = new GlyphPosition[text.Length];
            for (int i = 0; i < text.Length; i++)
                glyphs[i] = new GlyphPosition(GlyphId: text[i], X: i * advance, Y: 0, Advance: advance);
            return new ShapedRun(text, font, fontSize, text.Length * advance, glyphs);
        }
    }
}
