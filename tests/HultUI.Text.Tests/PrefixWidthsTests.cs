using HultUI.Core.Styling;
using HultUI.Testing;
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

    /// <summary>
    /// A table is an array of exactly its segments, one of its runs and the two objects that hold
    /// them: it is built in scratch lists the pipeline keeps from one table to the next. In lists
    /// of its own, a table of 100 segments cost three times its arrays (every array a list doubles
    /// through on its way up, then the lists), and wrapping or truncating a line builds one.
    /// </summary>
    [Fact]
    public void ATable_AllocatesItsArraysAndNothingElse()
    {
        var pipeline = Create();
        var style = Style();
        string shorter = Words(100), longer = Words(300);
        pipeline.MeasurePrefixes(shorter, style);   // the words into the word cache
        pipeline.MeasurePrefixes(longer, style);

        long a = Allocations.Least(() => pipeline.MeasurePrefixes(shorter, style));
        long b = Allocations.Least(() => pipeline.MeasurePrefixes(longer, style));
        Assert.Equal(20L * 200, b - a);              // 20 bytes a segment, and nothing else grows
        Assert.InRange(a - 20L * 100, 0, 256);       // the rest (200 B): one run, the table, its answerer
    }

    /// <summary>The scratch list keeps room for a long line's segments between tables, but not
    /// for a whole document's on one line.</summary>
    [Fact]
    public void AHugeTable_LeavesNoHugeScratchBehind()
    {
        var pipeline = Create();
        pipeline.MeasurePrefixes(Words(1_000), Style());
        Assert.InRange(pipeline.PrefixScratchCapacity, 1_000, TextPipeline.KeptPrefixSegments);
        Assert.NotNull(pipeline.MeasurePrefixes(Words(40_000), Style()));
        Assert.InRange(pipeline.PrefixScratchCapacity, 0, TextPipeline.KeptPrefixSegments);
    }

    /// <summary>The scratch run list lets go of the text once its table is built: a run of a
    /// one-run text is the text itself.</summary>
    [Fact]
    public void ATable_KeepsNoTextAliveOnceBuilt()
    {
        var pipeline = Create();
        var text = TableOfANewText(pipeline);
        pipeline.Layout("the next paragraph", Style(), float.PositiveInfinity, maxLines: 1);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(text.IsAlive);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference TableOfANewText(TextPipeline pipeline)
    {
        string text = string.Concat("a text ", Words(20));
        Assert.NotNull(pipeline.MeasurePrefixes(text, Style()));
        return new WeakReference(text);
    }

    /// <summary>
    /// A range of a longer text measures what the substring of it does, prefix for prefix — a
    /// paragraph of its own, whose Han characters take the face its own kana or Hangul call for,
    /// not the whole text's: kana and Hangul around the range must not reach it.
    /// </summary>
    [Theory]
    [InlineData("東京に行きます")]
    [InlineData("大韓民國 서울")]
    [InlineData("カナ 漢字 한글 漢字")]
    [InlineData("漢字 only")]
    [InlineData("hello world  twice")]
    public void ARangeOfAText_MeasuresWhatItsSubstringDoes(string text)
    {
        foreach (string around in new[] { "", "かな ", "한글 ", "かな 한글 " })
            foreach (float letterSpacing in new[] { 0f, 0.5f })
            {
                var style = Style() with { LetterSpacing = letterSpacing };
                string document = around + text + " " + around;
                var pipeline = Create();
                var prefixes = pipeline.MeasurePrefixes(document, around.Length, text.Length, style);
                Assert.NotNull(prefixes);
                Assert.Equal(text.Length, prefixes.Length);
                for (int n = 0; n <= text.Length; n++)
                {
                    float expected = Create().Layout(text[..n], style, float.PositiveInfinity, maxLines: 1).Size.Width;
                    Assert.True(expected == prefixes.Width(n),
                        $"\"{text[..n]}\" in \"{document}\" spaced {letterSpacing}: laid out {expected}, from the range {prefixes.Width(n)}");
                }
            }
    }

    /// <summary>The view of a range is built in lists the pipeline keeps, and is good until the
    /// next call that measures or lays text out; after it, it says so instead of answering from
    /// whatever the lists hold by then.</summary>
    [Fact]
    public void ARangesWidths_AreGoodUntilTheNextCall()
    {
        var pipeline = Create();
        var prefixes = pipeline.MeasurePrefixes("one two three", 4, 3, Style())!;
        Assert.Equal(3 * 6f, prefixes.Width(3));
        pipeline.Layout("the next paragraph", Style(), float.PositiveInfinity, maxLines: 1);
        Assert.Throws<InvalidOperationException>(() => prefixes.Width(3));
    }

    /// <summary>A prefix that stops short of the range's kana is laid out the long way, which is a
    /// call into the pipeline of its own: the view it was asked through is still good after it.</summary>
    [Fact]
    public void ARangesWidths_StayGoodWhileAPrefixIsLaidOutTheLongWay()
    {
        var prefixes = Create().MeasurePrefixes("> 東京に行きます <", 2, 7, Style())!;
        Assert.Equal(2 * 15f, prefixes.Width(2));         // no kana: the face that says nothing
        Assert.Equal(3 * 10f, prefixes.Width(3));         // with it: the Japanese one
        Assert.Equal(1 * 15f, prefixes.Width(1));
        Assert.Equal(7 * 10f, prefixes.Width(7));
    }

    [Fact]
    public void ARangeThatIsNotTheSumOfItsWords_GetsNoTable()
    {
        var pipeline = Create();
        Assert.Null(pipeline.MeasurePrefixes("abc", 1, 0, Style()));
        Assert.Null(pipeline.MeasurePrefixes("one two\nlines", 4, 6, Style()));
        Assert.Null(pipeline.MeasurePrefixes("a שלום b", 0, 8, Style()));
        Assert.NotNull(pipeline.MeasurePrefixes("a b שלום", 0, 3, Style()));   // the Hebrew is past the range
        Assert.Null(new TextPipeline(new Fonts(), new FontAdvanceShaper()) { ReuseAcrossWidths = false }
            .MeasurePrefixes("the reference path", 4, 9, Style()));
        Assert.Throws<ArgumentOutOfRangeException>(() => pipeline.MeasurePrefixes("abc", 2, 2, Style()));
        var prefixes = pipeline.MeasurePrefixes("abcdef", 1, 3, Style())!;
        Assert.Throws<ArgumentOutOfRangeException>(() => prefixes.Width(4));
        Assert.Throws<ArgumentOutOfRangeException>(() => prefixes.Width(-1));
    }

    /// <summary>
    /// A range's table allocates nothing: no string of the range, no arrays of its own, no object
    /// to hold them — a text area's line-break search asks for one for every line it wraps, and
    /// those were nearly all a wrapped document's line pass allocated.
    /// </summary>
    [Fact]
    public void ARangesTable_AllocatesNothing()
    {
        var pipeline = Create();
        var style = Style();
        string document = "first line\n" + Words(1_000) + "\nlast line";
        pipeline.MeasurePrefixes(document, 11, 200, style);   // the words into the word cache, the lists grown
        pipeline.MeasurePrefixes(document, 11, 600, style);
        Assert.Equal(0, Allocations.Least(() =>
        {
            var prefixes = pipeline.MeasurePrefixes(document, 11, 600, style)!;
            for (int n = 0; n <= 600; n += 7) prefixes.Width(n);
            pipeline.MeasurePrefixes(document, 13, 200, style);
        }));

        // Han without kana on a line of a document with kana elsewhere: the range says nothing
        // about its Han, so every prefix is read off its table — none laid out the long way, as
        // they would be if the table took the document's kana for its own.
        string cjk = "かな\n" + string.Concat(Enumerable.Repeat("漢字 only ", 40)) + "\nend";
        var line = pipeline.MeasurePrefixes(cjk, 3, 300, style)!;
        for (int n = 0; n <= 300; n++) line.Width(n);
        Assert.Equal(0, Allocations.Least(() =>
        {
            var prefixes = pipeline.MeasurePrefixes(cjk, 3, 300, style)!;
            for (int n = 0; n <= 300; n += 3) prefixes.Width(n);
        }));
    }

    /// <summary>The view lets go of its text — a text area's whole document — at the next call
    /// that measures or lays text out, and keeps no room for a whole document's line.</summary>
    [Fact]
    public void ARangesView_KeepsNoTextNorHugeScratchAfterTheNextCall()
    {
        var pipeline = Create();
        var text = RangeOfANewText(pipeline);
        Assert.InRange(pipeline.PrefixViewCapacity, 40_000, int.MaxValue);
        pipeline.Layout("the next paragraph", Style(), float.PositiveInfinity, maxLines: 1);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(text.IsAlive);
        Assert.InRange(pipeline.PrefixViewCapacity, 0, TextPipeline.KeptPrefixSegments);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference RangeOfANewText(TextPipeline pipeline)
    {
        string text = string.Concat("a text ", Words(40_000));
        Assert.NotNull(pipeline.MeasurePrefixes(text, 2, text.Length - 2, Style()));
        return new WeakReference(text);
    }

    // A word and a space for every two segments.
    private static string Words(int segments) => string.Concat(Enumerable.Repeat("ab ", segments / 2));

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
