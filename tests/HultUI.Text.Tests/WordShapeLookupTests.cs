using HultUI.Core.Layout;
using HultUI.Core.Styling;
using Xunit;

namespace HultUI.Text.Tests;

/// <summary>
/// A run's words come out of the word cache by a span of the run's text, with no string made
/// per word; ShapeByWords puts their glyphs into one array of the exact size, and MeasureString
/// adds their widths up without making glyphs at all. What comes out must be what came out when
/// every word was cut out as a string and its glyphs collected in a list (<see cref="WordModel"/>),
/// to the bit — over a shaper that kerns, ligates and has zero-width marks, two fonts, the digit
/// table and letter spacing — and the allocations these tests pin must stay gone.
/// </summary>
public class WordShapeLookupTests
{
    private static readonly ResolvedInheritedStyle Base = new(
        FontFamily: "TestFont", FontSize: 16f, FontWeight: FontWeight.Regular,
        TextColor: new(0, 0, 0, 255), TextDirection: TextDirection.Ltr, Cursor: CursorType.Default);

    private static readonly ResolvedInheritedStyle[] Styles =
    [
        Base,
        Base with { LetterSpacing = 1.25f },
        Base with { FontSize = 13f, LetterSpacing = -0.4f },
        Base with { FontSize = 20f },
    ];

    private static readonly string Acute = ((char)0x301).ToString();

    private static readonly string[] Texts =
    [
        "pretty red table chair house keyboard",
        "AVA To Wa office fifty fine",              // kerning and the fi ligature, inside words
        "double  spaced   words",
        "   leading and trailing   ",
        " ",
        "supercalifragilisticexpialidocious",
        "12345 67890 1029384756",                   // words of digits: the digit table
        "Item 4711 - 815 units 7",                  // digits between words
        "x1 2y 3.5 0",                              // digits inside words: the word cache
        "cafe" + Acute + " re" + Acute + "sume" + Acute,   // zero-width marks
        (char)0xAD + "soft hyphen first",           // a segment of no width
        "mixed Ελλη and latin text",                // two fonts: several runs
        "Ελληνικόκείμενο εδώ",                      // the fallback's words, the primary's space
        "7",
    ];

    // Everything after the first pass finds its words in the cache, by span.
    private static IEnumerable<string> TwoPasses(IEnumerable<string> texts)
    {
        foreach (var text in texts) yield return text;
        foreach (var text in texts) yield return string.Join(' ', text.Split(' ').Reverse());
    }

    [Fact]
    public void ShapeByWords_IsTheWordsShapedApart_ToTheBit()
    {
        var fonts = new WidthChangeEquivalenceTests.TwoFonts(negativeDescent: false);
        var model = new WidthChangeEquivalenceTests.ContextShaper();
        var pipeline = new TextPipeline(fonts, new WidthChangeEquivalenceTests.ContextShaper());
        int runs = 0;
        foreach (var style in Styles)
        foreach (var text in TwoPasses(Texts))
        foreach (var run in FontRuns(fonts, text, style))
        {
            var actual = pipeline.ShapeByWords(run.Text, run.Font, style.FontSize);
            WordModel.Same(WordModel.Run(model, run.Text, run.Font, style.FontSize, 0f), actual,
                $"\"{run.Text}\" in {run.Font.FamilyName} at {style.FontSize}");
            runs++;
        }
        Assert.True(runs > 100, $"compared only {runs} runs");
    }

    [Fact]
    public void MeasureString_IsTheWidthOfTheRunsItWouldShape_ToTheBit()
    {
        // Every prefix too, so words are cut short and looked up as part of a longer text.
        var fonts = new WidthChangeEquivalenceTests.TwoFonts(negativeDescent: false);
        var model = new WidthChangeEquivalenceTests.ContextShaper();
        var pipeline = new TextPipeline(fonts, new WidthChangeEquivalenceTests.ContextShaper());
        int measured = 0;
        foreach (var style in Styles)
        foreach (var text in TwoPasses(Texts))
        for (int n = 1; n <= text.Length; n++)
        {
            string prefix = text[..n];
            float expected = 0;
            foreach (var run in FontRuns(fonts, prefix, style))
                expected += WordModel.Run(model, run.Text, run.Font, style.FontSize, style.LetterSpacing).Width;
            float actual = pipeline.MeasureString(prefix, style);
            Assert.True(WordModel.Bits(expected) == WordModel.Bits(actual),
                $"\"{prefix}\" {style}: {actual}, expected {expected}");
            measured++;
        }
        Assert.True(measured > 1_000, $"measured only {measured} strings");
    }

    [Fact]
    public void ALaidOutLine_IsTheModelsRuns_WithLetterSpacing()
    {
        var fonts = new WidthChangeEquivalenceTests.TwoFonts(negativeDescent: false);
        var model = new WidthChangeEquivalenceTests.ContextShaper();
        var pipeline = new TextPipeline(fonts, new WidthChangeEquivalenceTests.ContextShaper());
        foreach (var style in Styles)
        foreach (var text in TwoPasses(Texts))
        {
            var line = Assert.Single(pipeline.Layout(text, style, float.PositiveInfinity, null).Lines);
            foreach (var run in line.Runs)
                WordModel.Same(WordModel.Run(model, run.Text, run.Font, run.FontSize, style.LetterSpacing), run,
                    $"\"{run.Text}\" of \"{text}\" {style}");
            Assert.True(WordModel.Bits(line.Width) == WordModel.Bits(pipeline.MeasureString(text, style)),
                $"\"{text}\" {style}: measured {pipeline.MeasureString(text, style)}, laid out {line.Width}");
        }
    }

    [Fact]
    public void WordsMetInOtherStrings_AreFoundInPlace_WithoutShaping()
    {
        var fonts = new WidthChangeEquivalenceTests.TwoFonts(negativeDescent: false);
        var shaper = new CountingShaper(new WidthChangeEquivalenceTests.ContextShaper());
        var pipeline = new TextPipeline(fonts, shaper);
        var font = fonts.Resolve(Base.FontFamily, Base.FontWeight, Base.FontSlant);

        pipeline.ShapeByWords("alpha beta", font, 16f);
        pipeline.ShapeByWords("gamma delta", font, 16f);
        int calls = shaper.Calls;
        Assert.True(calls >= 5, $"the words were shaped {calls} times");

        pipeline.ShapeByWords("delta alpha gamma beta", font, 16f);
        Assert.Equal(calls, shaper.Calls);
        pipeline.MeasureString("beta gamma", Base);
        pipeline.MeasureString("delta", Base);
        Assert.Equal(calls, shaper.Calls);
        // A word cut short is a word of its own: shaped once, then found in place too.
        pipeline.MeasureString("delta alph", Base);
        Assert.Equal(calls + 1, shaper.Calls);
        pipeline.MeasureString("alph gamma", Base);
        Assert.Equal(calls + 1, shaper.Calls);
    }

    [Fact]
    public void ASegmentLookedUpInPlace_HashesAndMatchesAsItsString()
    {
        var comparer = WordShapeKeyComparer.Instance;
        var font = new StubResolvedFont();
        var other = new StubResolvedFont();
        string[] words =
        [
            "a", "pretty", "12345", "  ", " ", "Ελλη", "東京", "e" + Acute,
            "x" + char.ConvertFromUtf32(0x1F600) + "y", (char)0xAD + "soft", new string('w', 300),
        ];
        foreach (var word in words)
        {
            string text = "<<" + word + ">>";
            var segment = text.AsSpan(2, word.Length);
            var key = new WordShapeKey(word, font, 16f);
            var span = WordSpan(segment, font, 16f);

            Assert.Equal(word.GetHashCode(), string.GetHashCode(segment));
            Assert.Equal(comparer.GetHashCode(key), comparer.GetHashCode(span));
            Assert.True(comparer.Equals(span, key), $"\"{word}\" in place is not \"{word}\"");
            var created = comparer.Create(span);
            Assert.Equal(key, created);
            Assert.NotSame(text, created.Segment);
            Assert.Equal(comparer.GetHashCode(key), comparer.GetHashCode(created));

            // Font instance and size are the key's too; and the characters, not the length.
            Assert.False(comparer.Equals(WordSpan(segment, other, 16f), key));
            Assert.False(comparer.Equals(WordSpan(segment, font, 16.5f), key));
            string changed = text[..^3] + (char)(text[^3] + 1) + text[^2..];
            Assert.False(comparer.Equals(WordSpan(changed.AsSpan(2, word.Length), font, 16f), key),
                $"\"{changed[2..^2]}\" matched \"{word}\"");
        }

        // Key against key, as the record's own equality has them for fonts that, like every
        // IResolvedFont, are equal only to themselves — and the same hash where equal.
        var pairs = new (WordShapeKey, WordShapeKey)[]
        {
            (new("word", font, 16f), new(new string("word".AsSpan()), font, 16f)),
            (new("word", font, 0f), new("word", font, -0f)),
            (new("word", font, 16f), new("word", other, 16f)),
            (new("word", font, 16f), new("Word", font, 16f)),
            (new("word", font, 16f), new("word", font, 16.25f)),
        };
        foreach (var (x, y) in pairs)
        {
            Assert.Equal(x.Equals(y), comparer.Equals(x, y));
            if (x.Equals(y)) Assert.Equal(comparer.GetHashCode(x), comparer.GetHashCode(y));
        }
    }

    [Fact]
    public void MeasuringWordsItHasMet_AllocatesNothing()
    {
        var pipeline = new TextPipeline(new WidthChangeEquivalenceTests.TwoFonts(negativeDescent: false),
            new WidthChangeEquivalenceTests.ContextShaper());
        const string text = "pretty red table 4711 chair";
        var spaced = Base with { LetterSpacing = 1.25f };
        pipeline.Layout(text, Base, float.PositiveInfinity, null);
        pipeline.MeasureString(text, Base);
        pipeline.MeasureString("pretty red tab", Base);
        pipeline.MeasureString("chair", spaced);

        long before = GC.GetAllocatedBytesForCurrentThread();
        pipeline.MeasureString(text, Base);
        pipeline.MeasureString(text, spaced);
        pipeline.MeasureString("pretty red tab", Base);
        pipeline.MeasureString("chair", spaced);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void ShapingWordsItHasMet_AllocatesTheRunAndOneArrayOfItsGlyphs()
    {
        var fonts = new WidthChangeEquivalenceTests.TwoFonts(negativeDescent: false);
        var pipeline = new TextPipeline(fonts, new WidthChangeEquivalenceTests.ContextShaper());
        var font = fonts.Resolve(Base.FontFamily, Base.FontWeight, Base.FontSlant);
        const string text = "pretty red table 4711 chair house keyboard";
        pipeline.ShapeByWords(text, font, 16f);

        long before = GC.GetAllocatedBytesForCurrentThread();
        var run = pipeline.ShapeByWords(text, font, 16f);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // 16 bytes a glyph, and two objects' headers and fields: the run and the array.
        long glyphBytes = 16L * run.Glyphs.Length;
        Assert.InRange(allocated, glyphBytes, glyphBytes + 160);
    }

    private static List<FontRun> FontRuns(IFontCollection fonts, string text, ResolvedInheritedStyle style) =>
        FontItemizer.Itemize(text, fonts, style.FontFamily, style.FontWeight, style.FontSlant, null, new List<FontRun>());

    // A segment's key in place, with its font's share of the hash as TextPipeline works it out.
    internal static WordSpanKey WordSpan(ReadOnlySpan<char> segment, IResolvedFont font, float fontSize) =>
        new(segment, font, fontSize, WordShapeKeyComparer.FontHash(font, fontSize));

    private sealed class CountingShaper(ITextShaper inner) : ITextShaper
    {
        public int Calls;

        public ShapedRun Shape(string text, IResolvedFont font, float fontSize, bool rtl = false)
        {
            Calls++;
            return inner.Shape(text, font, fontSize, rtl);
        }
    }
}

/// <summary>The cache under every text cache: least recently used out first, and a full cache
/// takes a new entry into the node of the one it evicts.</summary>
public class LruCacheTests
{
    private static WordSpanKey WordSpan(ReadOnlySpan<char> segment, IResolvedFont font, float fontSize) =>
        WordShapeLookupTests.WordSpan(segment, font, fontSize);

    [Fact]
    public void AFullCache_EvictsTheLeastRecentlyUsed()
    {
        var cache = new LruCache<int, string>(capacity: 3);
        cache.Put(1, "one");
        cache.Put(2, "two");
        cache.Put(3, "three");
        Assert.True(cache.TryGet(1, out _));     // 1 is now the most recent; 2 the least

        cache.Put(4, "four");
        Assert.False(cache.TryGet(2, out _));
        cache.Add(5, "five");                    // order now 5, 4, 1, 3: 3 goes
        Assert.False(cache.TryGet(3, out _));
        foreach (var (key, value) in new[] { (1, "one"), (4, "four"), (5, "five") })
        {
            Assert.True(cache.TryGet(key, out var found));
            Assert.Equal(value, found);
        }

        cache.Put(4, "FOUR");                    // a key put again takes the new value
        Assert.True(cache.TryGet(4, out var replaced));
        Assert.Equal("FOUR", replaced);
    }

    [Fact]
    public void AnInPlaceLookup_CountsAsAUse()
    {
        var font = new StubResolvedFont();
        var cache = new LruCache<WordShapeKey, int>(capacity: 2, WordShapeKeyComparer.Instance);
        cache.Add(new("alpha", font, 16f), 1);
        cache.Add(new("beta", font, 16f), 2);

        const string text = "alpha beta";
        var inPlace = cache.LookedUpBy<WordSpanKey>();
        Assert.True(inPlace.TryGet(WordSpan(text.AsSpan(0, 5), font, 16f), out int alpha));
        Assert.Equal(1, alpha);
        cache.Add(new("gamma", font, 16f), 3);

        Assert.True(inPlace.TryGet(WordSpan(text.AsSpan(0, 5), font, 16f), out _));
        Assert.False(inPlace.TryGet(WordSpan(text.AsSpan(6, 4), font, 16f), out _));
        Assert.False(cache.TryGet(new("beta", font, 16f), out _));

        // A view made before a Clear still sees the cache.
        cache.Clear();
        cache.Add(new("delta", font, 16f), 4);
        Assert.True(inPlace.TryGet(WordSpan("a delta".AsSpan(2), font, 16f), out int delta));
        Assert.Equal(4, delta);
    }

    [Fact]
    public void AFullCache_InsertsWithoutAllocating()
    {
        var cache = new LruCache<int, int>(capacity: 4);
        for (int i = 0; i < 8; i++) cache.Add(i, i);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 8; i < 1_008; i++)
        {
            if (!cache.TryGet(i, out _)) cache.Add(i, i);
            cache.Put(10_000 + i, i);
        }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.True(cache.TryGet(1_006, out _) && cache.TryGet(11_007, out _));
        Assert.False(cache.TryGet(1_005, out _));
    }
}
