using System.Globalization;
using HultUI.Core.Styling;
using HultUI.Text;
using Xunit;

namespace HultUI.Text.Tests;

/// <summary>
/// The paragraph's own text decides its Han characters: kanji beside kana are Japanese
/// whoever reads them, so fallback must be told so — on every path that itemizes,
/// including wrapping, which measures line-break segments that are often one ideograph.
/// </summary>
public class CjkContextTests
{
    private const string Japanese = "東京に行きます";   // kanji + hiragana
    private const string Kanji = "东京";                // no kana: says nothing

    [Theory]
    [InlineData(Japanese, "ja")]
    [InlineData("ｶﾀｶﾅと漢字", "ja")]        // halfwidth katakana counts
    [InlineData("大韓民國 서울", "ko")]
    [InlineData(Kanji, null)]
    [InlineData("hello world", null)]
    [InlineData("カナ 한글 漢字", null)]    // both: no answer, the culture decides
    [InlineData("", null)]
    public void CjkContextOf_ReadsKanaAndHangul(string text, string? expected)
    {
        Assert.Equal(expected, ScriptLanguage.CjkContextOf(text));
    }

    [Fact]
    public void TheContext_WinsOverTheCulture_ForHanOnly()
    {
        var traditional = new CultureInfo("zh-TW");
        Assert.Equal("ja", ScriptLanguage.ForCodepoint(0x6771, traditional, hanContext: "ja"));   // 東
        Assert.Equal("zh-TW", ScriptLanguage.ForCodepoint(0x6771, traditional));
        Assert.Equal("he", ScriptLanguage.ForCodepoint(0x05D0, traditional, hanContext: "ja"));   // not Han
    }

    [Fact]
    public void Layout_TellsFallbackTheParagraphIsJapanese()
    {
        var (pipeline, fonts) = Create();
        pipeline.Layout(Japanese, Style(), 10_000, null);
        AssertEveryHanLookup(fonts, "ja");
    }

    [Fact]
    public void Wrapping_MeasuresEachSegmentInTheParagraphsContext()
    {
        // Narrow enough to wrap between ideographs: the segments measured here hold no
        // kana of their own.
        var (pipeline, fonts) = Create();
        var layout = pipeline.Layout(Japanese, Style(), 25, null);
        Assert.True(layout.Lines.Count > 1, "expected the text to wrap");
        AssertEveryHanLookup(fonts, "ja");
    }

    [Fact]
    public void MinContentWidth_MeasuresInTheParagraphsContext()
    {
        var (pipeline, fonts) = Create();
        pipeline.MinContentWidth(Japanese, Style());
        AssertEveryHanLookup(fonts, "ja");
    }

    [Fact]
    public void TheContext_DoesNotLeakIntoTheNextParagraph()
    {
        var (pipeline, fonts) = Create();
        pipeline.Layout(Japanese, Style(), 10_000, null);
        fonts.HanLookups.Clear();

        pipeline.Layout(Kanji, Style(), 10_000, null);
        AssertEveryHanLookup(fonts, null);
    }

    private static void AssertEveryHanLookup(RecordingFonts fonts, string? expected)
    {
        Assert.NotEmpty(fonts.HanLookups);
        Assert.All(fonts.HanLookups, l => Assert.Equal(expected, l.Context));
    }

    private static ResolvedInheritedStyle Style() => new(
        FontFamily: "Latin", FontSize: 10f, FontWeight: FontWeight.Regular,
        TextColor: new(0, 0, 0, 255), TextDirection: TextDirection.Ltr, Cursor: CursorType.Default);

    private static (TextPipeline, RecordingFonts) Create()
    {
        var fonts = new RecordingFonts();
        return (new TextPipeline(fonts, new FixedAdvanceShaper()), fonts);
    }

    /// <summary>A primary that lacks CJK, so every ideograph goes to fallback, which
    /// records the context it was given.</summary>
    private sealed class RecordingFonts : IFontCollection
    {
        private readonly FakeFont _latin = new("Latin", cjk: false);
        private readonly Dictionary<string, FakeFont> _cjk = new();
        public List<(int Codepoint, string? Context)> HanLookups { get; } = new();

        public IResolvedFont Resolve(string familyName, FontWeight weight, FontSlant slant) => _latin;

        public IResolvedFont? MatchCharacter(int codepoint, FontWeight weight, FontSlant slant)
            => MatchCharacter(codepoint, weight, slant, null);

        public IResolvedFont? MatchCharacter(int codepoint, FontWeight weight, FontSlant slant, string? hanContext)
        {
            if (codepoint is >= 0x4E00 and <= 0x9FFF) HanLookups.Add((codepoint, hanContext));
            string key = hanContext ?? "none";
            if (!_cjk.TryGetValue(key, out var font)) _cjk[key] = font = new FakeFont("CJK " + key, cjk: true);
            return font;
        }
    }

    private sealed class FakeFont(string family, bool cjk) : IResolvedFont
    {
        public string FamilyName => family;
        public float Ascent => -0.8f;
        public float Descent => 0.2f;
        public float Leading => 0f;
        public float LineHeight => 1f;
        public object NativeHandle => this;
        public bool HasGlyph(int codepoint) => cjk || codepoint < 0x2E80;
        public void Dispose() { }
    }

    private sealed class FixedAdvanceShaper : ITextShaper
    {
        public ShapedRun Shape(string text, IResolvedFont font, float fontSize, bool rtl = false)
        {
            var glyphs = new GlyphPosition[text.Length];
            for (int i = 0; i < text.Length; i++)
                glyphs[i] = new GlyphPosition(GlyphId: text[i], X: i * fontSize, Y: 0, Advance: fontSize);
            return new ShapedRun(text, font, fontSize, text.Length * fontSize, glyphs);
        }
    }
}
