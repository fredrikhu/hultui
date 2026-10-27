using HultUI.Core.Layout;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using HultUI.Testing;
using Xunit;

namespace HultUI.Text.Tests;

/// <summary>
/// What a <see cref="PipelineTextParagraph"/> costs once its layout is cached: one array of
/// exactly its lines and one of their boxes, the object itself and nothing that grows with the
/// text but those two (a list of the lines and an array copied from it were 10 % of what
/// opening a long rich text document allocated), and the list a selection is cut into only once
/// a selection is drawn on it.
/// </summary>
public class ParagraphAllocationTests
{
    private static readonly ResolvedInheritedStyle Style = new(
        FontFamily: "Latin", FontSize: 10f, FontWeight: FontWeight.Regular,
        TextColor: new(0, 0, 0, 255), TextDirection: TextDirection.Ltr, Cursor: CursorType.Default);

    // n words of 8 characters: at 6 a character each (with its space, 54) fills a line of 60.
    private static string Lines(int n) => string.Join(' ', Enumerable.Repeat("abcdefgh", n));

    [Fact]
    public void AParagraph_AllocatesItsTwoArraysAndAFewObjects()
    {
        var pipeline = new TextPipeline(new Fonts(), new Shaper());
        string ten = Lines(10), forty = Lines(40);
        Assert.Equal(10, pipeline.LayoutParagraph(ten, Style, 60f, spans: null).TextLines.Count);
        Assert.Equal(40, pipeline.LayoutParagraph(forty, Style, 60f, spans: null).TextLines.Count);

        long a = Allocations.Least(() => pipeline.LayoutParagraph(ten, Style, 60f, spans: null));
        long b = Allocations.Least(() => pipeline.LayoutParagraph(forty, Style, 60f, spans: null));
        // 8 bytes a line in the lines' array, 20 in the boxes'.
        Assert.Equal(30L * (8 + 20), b - a);
        // The rest is the paragraph and the two arrays' headers (192 B): a selection list made up
        // front would be 32 B more.
        Assert.InRange(a - 10L * (8 + 20), 0, 200);
    }

    [Fact]
    public void AWrappedLine_AllocatesWhatItKeeps_AndNoListToItemizeIn()
    {
        var pipeline = new TextPipeline(new Fonts(), new Shaper());
        string ten = Lines(10), forty = Lines(40);
        float width = 60f;
        // Laid out once, so their shaping and break segments are kept; then each round at a width
        // not laid out before: a miss in the layout cache, and every line made again.
        Assert.Equal(10, pipeline.Layout(ten, Style, width, null).Lines.Count);
        Assert.Equal(40, pipeline.Layout(forty, Style, width, null).Lines.Count);
        long a = Allocations.Least(() => pipeline.Layout(ten, Style, width += 0.001f, null));
        long b = Allocations.Least(() => pipeline.Layout(forty, Style, width += 0.001f, null));

        // What a line keeps, and the layout's list of lines growing to 40 rather than 10.
        var line = pipeline.Layout(ten, Style, width, null).Lines[0];
        var made = new TextLine[100];
        long lineBytes = Allocations.Least(() =>
        {
            for (int i = 0; i < made.Length; i++) made[i] = new TextLine(line.Runs, 1f, 1f, 1f, 0f);
        }) / made.Length;
        long ListOf(int n) => Allocations.Least(() =>
        {
            var lines = new List<TextLine>();
            for (int i = 0; i < n; i++) lines.Add(line);
        });
        long listGrowth = ListOf(40) - ListOf(10);

        // Each line: its text trimmed ("abcdefgh"), 40 B, cut out of the paragraph once (the line
        // as it stands, "abcdefgh ", cut out first was 40 B more); a list of its one run with room
        // for one (32 + 32 B); the run, sharing its word's glyphs (56 B); and the line. Its font
        // runs go into a list the pipeline keeps: one made for each line was 120 B more, and the
        // list of its runs grown to room for four 24 B more.
        Assert.Equal(30L * (40 + 64 + 56 + lineBytes) + listGrowth, b - a);
    }

    [Fact]
    public void AnEllipsizedLabel_CutsOneStringForItsLine()
    {
        // The same label truncated nine characters further on, each at widths it was not laid out
        // at before: its line's text is the prefix and the mark in one string, 16 B longer for the
        // nine characters. The prefix cut out first and then joined to the mark was a second
        // string, 32 B in all.
        var pipeline = new TextPipeline(new Fonts(), new Shaper());
        string text = Lines(10);
        // 6 a character, the mark one of them: room for 19 or 28 characters and the mark, both
        // ending in the same "a" of a word cut short.
        float shortWidth = 6f * 20 + 3, longWidth = 6f * 29 + 3;
        Assert.Equal(19 + 1, Line(shortWidth).Length);
        Assert.Equal(28 + 1, Line(longWidth).Length);
        long a = Allocations.Least(() => Line(shortWidth += 0.001f));
        long b = Allocations.Least(() => Line(longWidth += 0.001f));
        Assert.Equal(16, b - a);

        TextLine Line(float width) =>
            Assert.Single(pipeline.Layout(text, Style, width, maxLines: 1, TextOverflow.Ellipsis).Lines);
    }

    public static TheoryData<string, bool> TextThatFits => new()
    {
        { "plain", false },
        { "styled", true },
        { "BiDi", false },
    };

    [Theory]
    [MemberData(nameof(TextThatFits))]
    public void EllipsisOnTextThatFits_AllocatesWhatClipDoes(string path, bool styled)
    {
        // Text that wraps into its lines with nothing cut off, at widths not laid out at before:
        // Ellipsis tests the last line and leaves the layout as it is, as Clip does. The test
        // handed the line builder as a lambda made a closure and a delegate on every layout with
        // Ellipsis: 136 B a layout more than Clip on the plain path, 152 styled, 160 BiDi.
        var pipeline = new TextPipeline(new Fonts(), new Shaper());
        string text = path == "BiDi" ? Lines(9) + " שלום" : Lines(10);
        TextSpan[]? spans = styled ? [new TextSpan(9, 8, new(0, 0, 0, 255), Weight: FontWeight.Bold)] : null;
        float width = 60f;
        var clip = pipeline.Layout(text, Style, width, null, TextOverflow.Clip, spans);
        var ellipsis = pipeline.Layout(text, Style, width, null, TextOverflow.Ellipsis, spans);
        Assert.Equal(10, clip.Lines.Count);
        Assert.Equal(clip.Lines.Count, ellipsis.Lines.Count);
        Assert.Equal(clip.Lines[^1].Width, ellipsis.Lines[^1].Width);

        long clipBytes = Allocations.Least(() => pipeline.Layout(text, Style, width += 0.001f, null, TextOverflow.Clip, spans));
        long ellipsisBytes = Allocations.Least(() =>
            pipeline.Layout(text, Style, width += 0.001f, null, TextOverflow.Ellipsis, spans));
        Assert.Equal(clipBytes, ellipsisBytes);
    }

    [Fact]
    public void AWrapsLastLine_IsSearchedWithoutATableOfItsOwn()
    {
        // The same paragraph but for its last line: four words of a letter each, or one word of the
        // same seven characters. A table of the last line's prefix widths made for its search — the
        // line cut out as a string, exact arrays of its runs and segments — cost 20 B more for each
        // segment; read out of the lists the pipeline keeps, seven segments cost what one does.
        var pipeline = new TextPipeline(new Fonts(), new Shaper());
        string one = Lines(10) + " abcdefg", seven = Lines(10) + " a b c d";
        float width = 60f;
        Assert.Equal(11, pipeline.Layout(one, Style, width, null).Lines.Count);
        Assert.Equal(11, pipeline.Layout(seven, Style, width, null).Lines.Count);
        long a = Allocations.Least(() => pipeline.Layout(one, Style, width += 0.001f, null));
        long b = Allocations.Least(() => pipeline.Layout(seven, Style, width += 0.001f, null));
        Assert.Equal(a, b);
    }

    [Fact]
    public void ASelectionDrawnOnAParagraph_IsCutIntoAListMadeOnce()
    {
        var pipeline = new TextPipeline(new Fonts(), new Shaper());
        var paragraph = pipeline.LayoutParagraph(Lines(4), Style, 60f, spans: null);
        var boxes = new List<HultUI.Core.Primitives.Rect>();
        paragraph.SelectionBoxes(2, 20, boxes);
        Assert.NotEmpty(boxes);
        Assert.Equal(0, Allocations.Least(() => { boxes.Clear(); paragraph.SelectionBoxes(2, 20, boxes); }));
    }

    private sealed class Fonts : IFontCollection
    {
        private readonly Font _font = new();
        public IResolvedFont Resolve(string familyName, FontWeight weight, FontSlant slant) => _font;
        public IResolvedFont? MatchCharacter(int codepoint, FontWeight weight, FontSlant slant) => null;
    }

    private sealed class Font : IResolvedFont
    {
        public string FamilyName => "Latin";
        public float Ascent => -0.8f;
        public float Descent => 0.2f;
        public float Leading => 0f;
        public float LineHeight => 1f;
        public object NativeHandle => this;
        public bool HasGlyph(int codepoint) => true;
        public void Dispose() { }
    }

    private sealed class Shaper : ITextShaper
    {
        public ShapedRun Shape(string text, IResolvedFont font, float fontSize, bool rtl = false)
        {
            float advance = 0.6f * fontSize;
            var glyphs = new GlyphPosition[text.Length];
            for (int i = 0; i < text.Length; i++)
                glyphs[i] = new GlyphPosition(text[i], i * advance, 0, advance);
            return new ShapedRun(text, font, fontSize, text.Length * advance, glyphs);
        }
    }
}
