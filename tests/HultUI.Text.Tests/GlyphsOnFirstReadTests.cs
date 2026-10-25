using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using HultUI.Testing;
using Xunit;
using Xunit.Abstractions;

namespace HultUI.Text.Tests;

/// <summary>
/// A run the pipeline puts together from its word cache — a run of several words — makes its
/// glyph array the first time its glyphs are read (<see cref="ShapedRun.Glyphs"/>), not when it
/// is laid out: most lines laid out are never drawn. These hold the pipeline to making none until
/// then, the caches' weights included; to making then the array it would have made with the run,
/// to the bit, even once the word cache has let the run's words go; and a run to the size it was.
/// </summary>
public class GlyphsOnFirstReadTests(ITestOutputHelper output)
{
    private static readonly ResolvedInheritedStyle Base = new(
        FontFamily: "TestFont", FontSize: 16f, FontWeight: FontWeight.Regular,
        TextColor: new(0, 0, 0, 255), TextDirection: TextDirection.Ltr, Cursor: CursorType.Default);

    private static readonly string[] Words =
        ["pretty", "red", "table", "chair", "office", "AV", "To", "fifty", "4711", "keyboard", "house", "Wa"];

    private static string Paragraph(int words, int seed = 0) =>
        string.Join(' ', Enumerable.Range(0, words).Select(i => Words[(i * 7 + seed) % Words.Length]));

    private static TextPipeline Pipeline(bool composeWhenRead = true) =>
        new(new WidthChangeEquivalenceTests.TwoFonts(negativeDescent: false), new WidthChangeEquivalenceTests.ContextShaper())
        {
            ComposeGlyphsWhenRead = composeWhenRead,
        };

    // The runs that are a run of several words: a one-word run shares its word's own glyphs.
    private static IEnumerable<ShapedRun> SeveralWordRuns(TextLayout layout) =>
        layout.Lines.SelectMany(l => l.Runs).Where(r => !r.IsRtl && r.Text.Trim().Contains(' '));

    [Fact]
    public void AWrappedParagraph_MakesNoGlyphArray_UntilALineIsRead()
    {
        var pipeline = Pipeline();
        string text = Paragraph(60);

        // The single line of the whole text, kept and weighed in the shape cache; then its lines
        // at a width, kept and weighed in the layout cache.
        var single = pipeline.Layout(text, Base, float.PositiveInfinity, null);
        var wrapped = pipeline.Layout(text, Base, 200f, null);
        Assert.True(wrapped.Lines.Count > 5, $"{wrapped.Lines.Count} lines");
        Assert.True(pipeline.ShapeCacheUse.Bytes > 0 && pipeline.LayoutCacheUse.Bytes > 0, "nothing was weighed");

        var runs = SeveralWordRuns(single).Concat(SeveralWordRuns(wrapped)).ToList();
        Assert.True(runs.Count > 5, $"{runs.Count} runs of several words");
        Assert.All(runs, run => Assert.False(run.GlyphsComposed, $"\"{run.Text}\" has its glyphs"));

        // One line read: its runs have their glyphs, of the count they were weighed by; no other does.
        var read = wrapped.Lines[2];
        foreach (var run in read.Runs)
            Assert.Equal(run.GlyphCount, run.Glyphs.Length);
        foreach (var line in wrapped.Lines)
        foreach (var run in line.Runs.Where(r => r.Text.Trim().Contains(' ')))
            Assert.Equal(ReferenceEquals(line, read), run.GlyphsComposed);
    }

    [Fact]
    public void RememberedStyledAndBidiLines_AreWeighedWithoutMakingTheirGlyphs()
    {
        var pipeline = Pipeline();
        string styled = Paragraph(40, seed: 3);
        var spans = new[]
        {
            new TextSpan(7, 20, Color.Black, Weight: FontWeight.Bold),
            new TextSpan(60, 30, Color.Black, FontSize: 20f),
        };
        string bidi = "commit שלום merged by the team אבג into main and then some more words " + Paragraph(20, seed: 5);

        // A string's entry, and the lines it remembers (weighed when remembered), come at its
        // second layout; the other widths miss the layout cache and find or remember lines there.
        var layouts = new List<TextLayout>();
        foreach (float width in new[] { 150f, 151f, 152f, 190f })
        {
            layouts.Add(pipeline.Layout(styled, Base, width, null, TextOverflow.Clip, spans));
            layouts.Add(pipeline.Layout(bidi, Base, width, null));
        }
        Assert.True(pipeline.MemoLineCacheUse.Entries > 0, "no line was remembered");

        var runs = layouts.SelectMany(SeveralWordRuns).ToList();
        Assert.True(runs.Count > 20, $"{runs.Count} runs of several words");
        Assert.All(runs, run => Assert.False(run.GlyphsComposed, $"\"{run.Text}\" has its glyphs"));
    }

    [Fact]
    public void LayingOutAtNewWidths_AllocatesNoGlyphArrays()
    {
        // The paragraph at forty widths, as a resize drag lays it out, its words in the cache.
        static long Allocated(TextPipeline pipeline)
        {
            string text = Paragraph(120);
            pipeline.Layout(text, Base, float.PositiveInfinity, null);
            float width = 900;
            return Allocations.Least(() =>
            {
                for (int i = 0; i < 40; i++)
                    pipeline.Layout(text, Base, width -= 3.25f, null);
            });
        }
        long reference = Allocated(Pipeline(composeWhenRead: false));
        long lazy = Allocated(Pipeline());
        output.WriteLine($"40 widths of a 120-word paragraph: {reference:N0} B with glyphs made, {lazy:N0} B with glyphs made when read");
        Assert.True(lazy < reference * 0.6, $"{lazy:N0} B against {reference:N0} B with the glyphs made");
    }

    public static TheoryData<bool, bool> FontSets => new() { { false, false }, { true, false }, { false, true } };

    private static readonly ResolvedInheritedStyle[] Styles =
    [
        Base,
        Base with { LetterSpacing = 1.25f },
        Base with { FontSize = 13f, LineHeight = 22f },
        Base with { TextDirection = TextDirection.Rtl },
    ];

    private static readonly string[] Texts =
    [
        Paragraph(30),
        "AVA To Wa office fifty fine and more office fifty",
        "12345 67890 1029384756 and 4711 units of 815",
        "double  spaced   words  here   and there",
        "café résumé naïve words",
        "mixed Ελλη and latin text with Ελληνικό κείμενο εδώ in it",
        "commit שלום merged by the team אבג into main and then some",
        "emoji \U0001F600 in the \U0001F680 middle of a few words",
        "first line of words\nsecond line is longer than the first",
    ];

    private static readonly (int? MaxLines, TextOverflow Overflow)[] Modes =
    [
        (null, TextOverflow.Clip),
        (1, TextOverflow.Ellipsis),
        (2, TextOverflow.Ellipsis),
        (1, TextOverflow.Clip),
    ];

    [Theory]
    [MemberData(nameof(FontSets))]
    public void GlyphsReadLater_AreTheGlyphsMadeWithTheRun_ToTheBit(bool negativeDescent, bool shortFallback)
    {
        int compared = 0, readLater = 0;
        foreach (bool flushFirst in new[] { false, true })
        {
            var fonts = new WidthChangeEquivalenceTests.TwoFonts(negativeDescent, shortFallback);
            var reference = new TextPipeline(fonts, new WidthChangeEquivalenceTests.ContextShaper()) { ComposeGlyphsWhenRead = false };
            var lazy = new TextPipeline(fonts, new WidthChangeEquivalenceTests.ContextShaper());
            var laidOut = new List<(TextLayout Expected, TextLayout Actual, string Context)>();

            foreach (var style in Styles)
            foreach (var text in Texts)
            foreach (var spans in TestSpans.Variants(text).Prepend(null))
            foreach (var (maxLines, overflow) in Modes)
            for (float w = 40f; w < 700f; w += 47.5f)
                laidOut.Add((reference.Layout(text, style, w, maxLines, overflow, spans),
                    lazy.Layout(text, style, w, maxLines, overflow, spans),
                    $"\"{text}\" spans {spans?.Length ?? 0} w={w} lines={maxLines} {overflow} {style}"));

            // Every word of the corpus out of the word cache before any glyph is read: what was
            // laid out is put together from words shaped again.
            if (flushFirst)
                for (int i = 0; i < 2 * TextPipelineWordCacheEntries; i++)
                    lazy.Layout($"w{i:x5}", Base, float.PositiveInfinity, null);

            foreach (var (expected, actual, context) in laidOut)
            {
                readLater += actual.Lines.SelectMany(l => l.Runs).Count(r => !r.GlyphsComposed);
                LayoutAssert.Same(expected, actual, (flushFirst ? "after a flush: " : "") + context);
                compared++;
            }
        }
        Assert.True(compared > 5_000, $"compared only {compared} layouts");
        Assert.True(readLater > 5_000, $"only {readLater} runs made their glyphs when read");
    }

    // The word cache's entry bound (TextPipeline's _wordShapeCache).
    private const int TextPipelineWordCacheEntries = 8192;

    [Fact]
    public void ARun_IsNoBiggerForMakingItsGlyphsLater()
    {
        if (IntPtr.Size != 8) return;
        var font = new WidthChangeEquivalenceTests.TwoFonts(negativeDescent: false).Resolve("TestFont", FontWeight.Regular, FontSlant.Normal);
        GlyphPosition[] glyphs = [new(1, 0, 0, 5)];
        var runs = new ShapedRun[1_000];
        long bytes = Allocations.Least(() =>
        {
            for (int i = 0; i < runs.Length; i++)
                runs[i] = new ShapedRun("a", font, 16f, 5f, glyphs);
        });
        // Three references, three floats, the level and 16 bits of glyph count, and the header.
        Assert.Equal(56, bytes / runs.Length);
    }
}
