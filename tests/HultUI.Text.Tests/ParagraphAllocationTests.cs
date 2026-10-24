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
