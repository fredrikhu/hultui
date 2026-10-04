using HultUI.Core.Styling;
using HultUI.Text;
using Xunit;

namespace HultUI.Text.Tests;

/// <summary>
/// Font fallback keeps a sequence in one font: what joins or modifies a character goes with it,
/// or the shaper never sees the sequence and draws its parts. A text face holds the zero-width
/// joiner, so 👨‍👩‍👧 came out as three people from the emoji font.
/// </summary>
public class FallbackSequenceTests
{
    private const string Family = "\U0001F468‍\U0001F469‍\U0001F467"; // 👨‍👩‍👧
    private const string ThumbsUp = "\U0001F44D\U0001F3FD";                       // 👍🏽

    [Fact]
    public void AJoinedEmojiSequence_IsOneRun_InTheEmojiFont()
    {
        var runs = Runs("a" + Family + "b");
        Assert.Equal([("a", "Text"), (Family, "Emoji"), ("b", "Text")], runs);
    }

    [Fact]
    public void ASkinTone_AndAVariationSelector_StayWithTheirEmoji()
    {
        Assert.Equal([(ThumbsUp, "Emoji")], Runs(ThumbsUp));
        Assert.Equal([("\U0001F600️", "Emoji")], Runs("\U0001F600️"));
    }

    [Fact]
    public void ACombiningMark_StaysWithItsBase_WhenItsFontHasIt()
    {
        Assert.Equal([("é", "Text")], Runs("é"));
    }

    [Fact]
    public void AJoiner_BetweenTextCharacters_DoesNotPullThemIntoAnotherFont()
    {
        Assert.Equal([("a‍b", "Text")], Runs("a‍b"));
    }

    private static List<(string, string)> Runs(string text)
    {
        var pipeline = new TextPipeline(new Fonts(), new FixedAdvanceShaper());
        var layout = pipeline.Layout(text, new ResolvedInheritedStyle(
            FontFamily: "Text", FontSize: 10f, FontWeight: FontWeight.Regular,
            TextColor: new(0, 0, 0, 255), TextDirection: TextDirection.Ltr, Cursor: CursorType.Default), 10_000, null);
        return Assert.Single(layout.Lines).Runs.Select(r => (r.Text, r.Font.FamilyName)).ToList();
    }

    /// <summary>A text face with ASCII, the joiners and selectors, and combining marks, as most
    /// have; an emoji face with the emoji, the joiner, the selectors and the skin tones.</summary>
    private sealed class Fonts : IFontCollection
    {
        private readonly FakeFont _text = new("Text", cp => cp < 0x0370 || cp is 0x200C or 0x200D or >= 0xFE00 and <= 0xFE0F);
        private readonly FakeFont _emoji = new("Emoji", cp => cp >= 0x1F000 || cp is 0x200D or >= 0xFE00 and <= 0xFE0F);

        public IResolvedFont Resolve(string familyName, FontWeight weight, FontSlant slant) => _text;

        public IResolvedFont? MatchCharacter(int codepoint, FontWeight weight, FontSlant slant) =>
            _emoji.HasGlyph(codepoint) ? _emoji : null;
    }

    private sealed class FakeFont(string family, Func<int, bool> has) : IResolvedFont
    {
        public string FamilyName => family;
        public float Ascent => -0.8f;
        public float Descent => 0.2f;
        public float Leading => 0f;
        public float LineHeight => 1f;
        public object NativeHandle => this;
        public bool HasGlyph(int codepoint) => has(codepoint);
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
