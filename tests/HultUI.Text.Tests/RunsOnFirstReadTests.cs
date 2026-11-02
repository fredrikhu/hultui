using System.Globalization;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using HultUI.Testing;
using Xunit;
using Xunit.Abstractions;

namespace HultUI.Text.Tests;

/// <summary>
/// A line the pipeline breaks, on the plain and the styled path, keeps its width, metrics and
/// extent and makes its runs and their strings the first time they are read
/// (<see cref="TextLine.Runs"/>): most lines laid out are never drawn. These hold the pipeline to
/// making none until then, the caches' weights included; to making then the runs it would have made
/// with the line, to the bit, whatever the pipeline has done since; each reader of a line to the
/// answer it gave; the caches to weighing a line as if its runs were made; and a line to the size it
/// was. The BiDi path makes its runs with the line.
/// </summary>
public class RunsOnFirstReadTests(ITestOutputHelper output)
{
    private static readonly ResolvedInheritedStyle Base = new(
        FontFamily: "TestFont", FontSize: 16f, FontWeight: FontWeight.Regular,
        TextColor: new(0, 0, 0, 255), TextDirection: TextDirection.Ltr, Cursor: CursorType.Default);

    private static readonly string[] Words =
        ["pretty", "red", "table", "chair", "office", "AV", "To", "fifty", "4711", "keyboard", "house", "Wa"];

    private static string Paragraph(int words, int seed = 0) =>
        string.Join(' ', Enumerable.Range(0, words).Select(i => Words[(i * 7 + seed) % Words.Length]));

    private static TextPipeline Pipeline(bool composeWhenRead = true, IFontCollection? fonts = null) =>
        new(fonts ?? new WidthChangeEquivalenceTests.TwoFonts(negativeDescent: false), new WidthChangeEquivalenceTests.ContextShaper())
        {
            ComposeRunsWhenRead = composeWhenRead,
        };

    private static TextSpan[] Bold(string text) =>
        [new TextSpan(7, 20, Color.Black, Weight: FontWeight.Bold), new TextSpan(40, Math.Min(30, text.Length - 40), Color.Black, FontSize: 20f)];

    [Fact]
    public void AWrappedParagraph_MakesNoRuns_UntilALineIsRead()
    {
        var pipeline = Pipeline();
        var layout = pipeline.Layout(Paragraph(60), Base, 200f, null);
        Assert.True(layout.Lines.Count > 5, $"{layout.Lines.Count} lines");
        Assert.True(pipeline.LayoutCacheUse.Bytes > 0, "nothing was weighed");
        Assert.All(layout.Lines, line => Assert.False(line.RunsMade, "a line made its runs when it was laid out"));

        // One line read: it has its runs, and no other line does.
        var read = layout.Lines[2];
        Assert.NotEmpty(read.Runs);
        foreach (var line in layout.Lines)
            Assert.Equal(ReferenceEquals(line, read), line.RunsMade);

        // Once: a second read finds them.
        Assert.Same(read.Runs, read.Runs);
    }

    [Theory]
    [InlineData(1, TextOverflow.Ellipsis)]
    [InlineData(2, TextOverflow.Ellipsis)]
    [InlineData(1, TextOverflow.Clip)]
    public void ATruncatedText_MakesNoRuns_UntilALineIsRead(int maxLines, TextOverflow overflow)
    {
        foreach (string text in new[] { Paragraph(30), Paragraph(12) + " Ελληνικό κείμενο " + Paragraph(12, seed: 3) })
        {
            var pipeline = Pipeline();
            var layout = pipeline.Layout(text, Base, 150f, maxLines, overflow);
            Assert.Equal(maxLines, layout.Lines.Count);
            Assert.All(layout.Lines, line => Assert.False(line.RunsMade, "a line made its runs when it was laid out"));
        }
    }

    [Fact]
    public void AStyledParagraph_MakesNoRuns_UntilALineIsRead_ItsFirstLayoutAndItsLaterOnes()
    {
        var pipeline = Pipeline();
        string text = Paragraph(60);
        var spans = Bold(text);
        foreach (float width in new[] { 200f, 201f, 240f })   // the second has an entry of its own
        {
            var layout = pipeline.Layout(text, Base, width, null, TextOverflow.Clip, spans);
            Assert.True(layout.Lines.Count > 5, $"{layout.Lines.Count} lines");
            Assert.All(layout.Lines, line => Assert.False(line.RunsMade, "a line made its runs when it was laid out"));
        }
        var ellipsized = pipeline.Layout(text, Base, 200f, 2, TextOverflow.Ellipsis, spans);
        Assert.All(ellipsized.Lines, line => Assert.False(line.RunsMade, "a line made its runs when it was laid out"));
    }

    [Fact]
    public void AFirstLayoutWithManySpans_MakesItsRunsNow_ALaterOneWhenRead()
    {
        // Each key typed in a paragraph is a string's first layout: a copy of hundreds of spans and
        // a second walk over them, for lines that are mostly on screen, was 11 % of a key.
        var pipeline = Pipeline();
        string text = Paragraph(200);
        var spans = Enumerable.Range(0, TextPipeline.MostSpansCopied + 1)
            .Select(i => new TextSpan(i * 8, 3, Color.Black, Weight: FontWeight.Bold)).ToArray();

        var first = pipeline.Layout(text, Base, 200f, null, TextOverflow.Clip, spans);
        Assert.All(first.Lines, line => Assert.True(line.RunsMade));

        // Its entry has a copy, made once for every width.
        var later = pipeline.Layout(text, Base, 240f, null, TextOverflow.Clip, spans);
        Assert.All(later.Lines, line => Assert.False(line.RunsMade));

        // As many as may be copied.
        string other = Paragraph(200, seed: 2);
        var fewer = pipeline.Layout(other, Base, 200f, null, TextOverflow.Clip, spans[..TextPipeline.MostSpansCopied]);
        Assert.All(fewer.Lines, line => Assert.False(line.RunsMade));
    }

    [Fact]
    public void AParagraph_AWidthSameRememberedLine_HasOneSetOfRuns_AndCopiesMakeThemOnce()
    {
        var pipeline = Pipeline();
        string text = Paragraph(60);
        var spans = Bold(text);

        // Two widths that break the same lines: the second's are copies of those the first
        // remembered, and each gets a line of its own for its OffsetY.
        pipeline.Layout(text, Base, 200f, null, TextOverflow.Clip, spans);   // the string's first layout: no entry yet
        var first = pipeline.Layout(text, Base, 200.25f, null, TextOverflow.Clip, spans);
        var second = pipeline.Layout(text, Base, 200.5f, null, TextOverflow.Clip, spans);
        Assert.Equal(first.Lines.Count, second.Lines.Count);
        Assert.NotSame(first.Lines[1], second.Lines[1]);
        Assert.False(second.Lines[1].RunsMade);

        Assert.Same(first.Lines[1].Runs, second.Lines[1].Runs);
        Assert.True(second.Lines[1].RunsMade);
    }

    [Fact]
    public void ALineOnTheBidiPath_MakesItsRuns_WithTheLine()
    {
        // Its width is that of right-to-left runs shaped: nothing is saved by not keeping them.
        var pipeline = Pipeline();
        var layout = pipeline.Layout("commit שלום merged by the team אבג into main and then some more words", Base, 120f, null);
        Assert.True(layout.Lines.Count > 1, $"{layout.Lines.Count} lines");
        Assert.All(layout.Lines, line => Assert.True(line.RunsMade));
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
        "double  spaced   words  here   and there   ",
        "café résumé naïve words",
        "mixed Ελλη and latin text with Ελληνικό κείμενο εδώ in it",
        "commit שלום merged by the team אבג into main and then some",
        "emoji \U0001F600 in the \U0001F680 middle of a few words",
        "first line of words\nsecond line is longer than the first\n",
        "averyveryveryveryveryveryveryverylongwordthathasnowheretobreakatallinthiscolumn and more",
    ];

    private static readonly (int? MaxLines, TextOverflow Overflow)[] Modes =
    [
        (null, TextOverflow.Clip),
        (1, TextOverflow.Ellipsis),
        (2, TextOverflow.Ellipsis),
        (1, TextOverflow.Clip),
        (null, TextOverflow.Ellipsis),
    ];

    [Theory]
    [MemberData(nameof(FontSets))]
    public void RunsReadLater_AreTheRunsMadeWithTheLine_ToTheBit(bool negativeDescent, bool shortFallback)
    {
        int compared = 0, readLater = 0;
        foreach (bool flushFirst in new[] { false, true })
        {
            var fonts = new WidthChangeEquivalenceTests.TwoFonts(negativeDescent, shortFallback);
            var reference = new TextPipeline(fonts, new WidthChangeEquivalenceTests.ContextShaper()) { ComposeRunsWhenRead = false };
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

            // Every word of the corpus out of the word cache before any line is read: what was
            // laid out is made from words shaped again.
            if (flushFirst)
                for (int i = 0; i < 2 * WordCacheEntries; i++)
                    lazy.Layout($"w{i:x5}", Base, float.PositiveInfinity, null);

            foreach (var (expected, actual, context) in laidOut)
            {
                readLater += actual.Lines.Count(l => !l.RunsMade);
                LayoutAssert.Same(expected, actual, (flushFirst ? "after a flush: " : "") + context);
                compared++;
            }
        }
        Assert.True(compared > 5_000, $"compared only {compared} layouts");
        Assert.True(readLater > 5_000, $"only {readLater} lines made their runs when read");
    }

    // The word cache's entry bound (TextPipeline's _wordShapeCache).
    private const int WordCacheEntries = 8192;

    [Fact]
    public void EveryReaderOfALine_AnswersAsItDidWithTheRunsMade()
    {
        string text = Paragraph(14) + " Ελληνικό κείμενο " + Paragraph(14, seed: 3);
        int compared = 0;
        foreach (var spans in new[] { null, Bold(text) })
        foreach (var (maxLines, overflow) in Modes)
        foreach (float width in new[] { 180f, 313f })
        {
            var expected = Pipeline(composeWhenRead: false).LayoutParagraph(text, Base, width, spans, maxLines, overflow);
            for (int l = 0; l < expected.Lines.Count; l++)
            {
                var box = expected.Lines[l];
                for (int offset = 0; offset <= box.Length; offset += 5)
                {
                    int at = box.Start + offset;
                    // A paragraph of its own for each question: each reader finds no runs made.
                    Assert.Equal(expected.CaretX(l, at), Fresh().CaretX(l, at));
                    Assert.Equal(expected.OffsetAtX(l, offset * 6f), Fresh().OffsetAtX(l, offset * 6f));
                    Assert.Equal(Boxes(expected, box.Start, at), Boxes(Fresh(), box.Start, at));
                    Assert.Equal(expected.TryMoveCaretVisually(l, at, true, out int moved),
                        Fresh().TryMoveCaretVisually(l, at, true, out int freshMoved));
                    Assert.Equal(moved, freshMoved);
                    Assert.Equal(expected.CaretBox(at), Fresh().CaretBox(at));
                    compared += 5;
                }
            }

            PipelineTextParagraph Fresh()
            {
                var paragraph = Pipeline().LayoutParagraph(text, Base, width, spans, maxLines, overflow);
                Assert.All(paragraph.TextLines, line => Assert.False(line.RunsMade && line.Length > 0, "a line made its runs"));
                return paragraph;
            }
        }
        Assert.True(compared > 1_000, $"{compared} answers compared");

        static List<Rect> Boxes(TextParagraph paragraph, int from, int to)
        {
            var boxes = new List<Rect>();
            paragraph.SelectionBoxes(from, to, boxes);
            return boxes;
        }
    }

    [Fact]
    public void RunsReadLater_AreInTheHanContextOfTheirParagraph()
    {
        // 東 alone says nothing, beside kana it is Japanese: the same character in two faces. The
        // lines are read with the pipeline on other paragraphs.
        var fonts = new ContextFonts();
        var pipeline = new TextPipeline(fonts, new WidthChangeEquivalenceTests.ContextShaper());
        string japanese = "東京に行きます and some more words after them";
        var layout = pipeline.Layout(japanese, Base, 90f, null);
        var ellipsized = pipeline.Layout(japanese, Base, 90f, 1, TextOverflow.Ellipsis);
        var styled = pipeline.Layout(japanese, Base, 90f, null, TextOverflow.Clip, [new TextSpan(0, 1, Color.Black, Weight: FontWeight.Bold)]);
        var chinese = pipeline.Layout("东京 and 漢字 in the same style with no kana in it", Base, 90f, null);
        pipeline.MeasureWidth("漢字", Base);

        foreach (var laidOut in new[] { layout, ellipsized, styled })
        {
            Assert.False(laidOut.Lines[0].RunsMade);
            var han = Assert.Single(laidOut.Lines[0].Runs, run => run.Text.Contains('東'));
            Assert.Equal("CJK ja", han.Font.FamilyName);
        }

        // The next paragraph, of the same style, is in its own.
        Assert.False(chinese.Lines[0].RunsMade);
        Assert.NotEqual("CJK ja", Assert.Single(chinese.Lines[0].Runs, run => run.Text.Contains('京')).Font.FamilyName);
    }

    [Fact]
    public void RunsReadLater_AreInTheCultureTheLineWasLaidOutIn()
    {
        // Han with nothing to say what it is follows the UI culture. The width was added up in that
        // culture's face, so the run that fills it stays in that face after the culture changes.
        var culture = CultureInfo.CurrentUICulture;
        try
        {
            var fonts = new ContextFonts();
            var pipeline = new TextPipeline(fonts, new WidthChangeEquivalenceTests.ContextShaper());
            CultureInfo.CurrentUICulture = new CultureInfo("zh-TW");
            var layout = pipeline.Layout("东京 and some more words after them", Base, 90f, null);

            CultureInfo.CurrentUICulture = new CultureInfo("ko-KR");
            var korean = CultureInfo.CurrentUICulture;
            var line = layout.Lines[0];
            Assert.False(line.RunsMade);
            var han = Assert.Single(line.Runs, run => run.Text.Contains('京'));
            Assert.Equal("CJK zh-TW", han.Font.FamilyName);
            Assert.Same(korean, CultureInfo.CurrentUICulture);   // and the reader's is left as it was

            // A line laid out from now on is in the new one.
            pipeline.Layout("东京", Base, float.PositiveInfinity, null);
            var next = pipeline.Layout("东京 and some other words after them", Base, 90f, null);
            Assert.Equal("CJK ko", Assert.Single(next.Lines[0].Runs, run => run.Text.Contains('京')).Font.FamilyName);
        }
        finally
        {
            CultureInfo.CurrentUICulture = culture;
        }
    }

    [Fact]
    public void RunsReadLater_AreCutWhereTheLayoutsSpansWere_WhatEverTheirListHoldsNow()
    {
        var fonts = new WidthChangeEquivalenceTests.TwoFonts(negativeDescent: false);
        var reference = Pipeline(composeWhenRead: false, fonts);
        var lazy = Pipeline(fonts: fonts);
        string text = Paragraph(60);
        foreach (float width in new[] { 200f, 240f })   // a string's first layout and a later one
        {
            var list = new List<TextSpan>(Bold(text));
            var expected = reference.Layout(text, Base, width, null, TextOverflow.Clip, list);
            var actual = lazy.Layout(text, Base, width, null, TextOverflow.Clip, list);

            // A caller filling its list again: the paragraph it was laid out from is gone.
            list.Clear();
            list.Add(new TextSpan(3, 90, Color.Black, FontSize: 30f));
            LayoutAssert.Same(expected, actual, $"width {width}");
        }
    }

    [Fact]
    public void ReadingALine_IsNoCallThatEndsAPrefixView()
    {
        // A prefix view is good until the next call that measures or lays out; reading a line is
        // neither, so a line drawn between two of its widths does not end it.
        var pipeline = Pipeline();
        var layout = pipeline.Layout(Paragraph(30), Base, 200f, null);
        string text = Paragraph(12, seed: 5);
        var view = pipeline.MeasurePrefixes(text, 0, text.Length, Base)!;
        float before = view.Width(9);
        Assert.NotEmpty(layout.Lines[1].Runs);
        Assert.Equal(before, view.Width(9));
    }

    [Fact]
    public void TheLayoutCache_WeighsALine_AsIfItsRunsWereMade()
    {
        // What a line holds once drawn cannot be added to the entry later: an entry weighed by what
        // its lines hold now would stay short of what they come to hold.
        foreach (string text in new[] { Paragraph(60), Paragraph(20) + " Ελληνικό κείμενο εδώ " + Paragraph(20, seed: 3) })
        foreach (var (maxLines, overflow) in Modes)
        {
            var reference = Pipeline(composeWhenRead: false);
            var lazy = Pipeline();
            reference.Layout(text, Base, 200f, maxLines, overflow);
            var layout = lazy.Layout(text, Base, 200f, maxLines, overflow);
            Assert.Contains(layout.Lines, line => !line.RunsMade);

            // Not short of it, and over by no more than a line replaced by its ellipsis and a
            // glyph a character where the shaper joined some.
            var (made, notMade) = (reference.LayoutCacheUse.Bytes, lazy.LayoutCacheUse.Bytes);
            Assert.InRange(notMade, made, made * 1.05);
        }
    }

    [Fact]
    public void TheRememberedLines_WeighALine_AsIfItsRunsWereMade()
    {
        string text = Paragraph(30) + " Ελληνικό κείμενο εδώ " + Paragraph(30, seed: 3);
        var spans = TestSpans.Variants(text).First();
        var reference = Pipeline(composeWhenRead: false);
        var lazy = Pipeline();
        foreach (float width in new[] { 200f, 201f, 240f, 280f })
        {
            reference.Layout(text, Base, width, null, TextOverflow.Clip, spans);
            lazy.Layout(text, Base, width, null, TextOverflow.Clip, spans);
        }
        reference.Layout(text, Base, 100_000f, null, TextOverflow.Clip, spans);   // the line of the whole text
        lazy.Layout(text, Base, 100_000f, null, TextOverflow.Clip, spans);
        Assert.True(reference.MemoLineCacheUse.Entries > 20, $"{reference.MemoLineCacheUse.Entries} lines remembered");
        Assert.Equal(reference.MemoLineCacheUse.Entries, lazy.MemoLineCacheUse.Entries);

        // A glyph a character, where the shaper joined some, and the text and spans the first line
        // keeps.
        long kept = 2 * text.Length + 56 * spans.Length;
        Assert.InRange(lazy.MemoLineCacheUse.Bytes, reference.MemoLineCacheUse.Bytes, reference.MemoLineCacheUse.Bytes * 1.05 + kept);
        Assert.InRange(lazy.MemoCacheUse.Bytes, reference.MemoCacheUse.Bytes, reference.MemoCacheUse.Bytes * 1.05);
    }

    [Fact]
    public void ALine_IsNoBiggerForMakingItsRunsLater()
    {
        if (IntPtr.Size != 8) return;
        IReadOnlyList<ShapedRun> runs = [];
        var lines = new TextLine[1_000];
        long bytes = Allocations.Least(() =>
        {
            for (int i = 0; i < lines.Length; i++)
                lines[i] = new TextLine(runs, 1f, 1f, 1f, 0f);
        });
        // One reference, four floats and three ints and OffsetY, and the header.
        Assert.Equal(56, bytes / lines.Length);
    }

    [Fact]
    public void LayingOutAtNewWidths_AllocatesNoRuns_OnlyWhatTheLinesKeep()
    {
        // The paragraph at forty widths, as a resize drag lays it out, its words in the cache.
        static long Allocated(TextPipeline pipeline, IReadOnlyList<TextSpan>? spans)
        {
            string text = Paragraph(120);
            float width = 900;
            for (int i = 0; i < 2; i++) pipeline.Layout(text, Base, width -= 3.25f, null, TextOverflow.Clip, spans);
            return Allocations.Least(() =>
            {
                for (int i = 0; i < 40; i++)
                    pipeline.Layout(text, Base, width -= 3.25f, null, TextOverflow.Clip, spans);
            });
        }
        foreach (var spans in new[] { null, TestSpans.Variants(Paragraph(120)).First() })
        {
            long reference = Allocated(Pipeline(composeWhenRead: false), spans);
            long lazy = Allocated(Pipeline(), spans);
            output.WriteLine($"40 widths of a 120-word paragraph, spans {spans?.Length ?? 0}: {reference:N0} B with runs made, {lazy:N0} B with runs made when read");
            Assert.True(lazy < reference * 0.6, $"{lazy:N0} B against {reference:N0} B with the runs made");
        }
    }

    [Fact]
    public void LabelsOfOneStyle_ShareWhatTheirLinesAreMadeFrom()
    {
        // A label truncated, a thousand of them in a list: a line, its source, and its layout. The
        // style they all share is made with the first: it would be a third of what not making the
        // line saves, for each.
        var pipeline = Pipeline();
        var labels = Enumerable.Range(0, 300).Select(i => $"{Paragraph(12, i)} {i}").ToArray();
        foreach (var label in labels) pipeline.Layout(label, Base, 120f, 1, TextOverflow.Ellipsis);   // their words, their entries
        float width = 120f;
        long a = Allocations.Least(() =>
        {
            width += 0.001f;
            foreach (var label in labels) pipeline.Layout(label, Base, width, 1, TextOverflow.Ellipsis);
        });
        output.WriteLine($"{a / labels.Length} B a label");
        Assert.True(a / labels.Length < 330, $"{a / labels.Length} B a label");
    }

    // A primary face without CJK, and for Han one face per language, as the real collection picks:
    // the paragraph's context, else the UI culture's.
    private sealed class ContextFonts : IFontCollection
    {
        private readonly Face _latin = new("Latin", cjk: false);
        private readonly Dictionary<string, Face> _cjk = new();

        public IResolvedFont Resolve(string familyName, FontWeight weight, FontSlant slant) => _latin;

        public IResolvedFont? MatchCharacter(int codepoint, FontWeight weight, FontSlant slant) =>
            MatchCharacter(codepoint, weight, slant, null);

        public IResolvedFont? MatchCharacter(int codepoint, FontWeight weight, FontSlant slant, string? hanContext)
        {
            string key = ScriptLanguage.ForCodepoint(codepoint, hanContext) ?? "none";
            if (!_cjk.TryGetValue(key, out var font)) _cjk[key] = font = new Face("CJK " + key, cjk: true);
            return font;
        }
    }

    private sealed class Face(string family, bool cjk) : IResolvedFont
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
}
