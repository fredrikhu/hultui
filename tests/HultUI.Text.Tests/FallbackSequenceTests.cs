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

    /// <summary>
    /// The itemizer answers what extends the character before it without the category lookup below
    /// U+0300: at every code point, in a face that has them all and one that has none, the answer
    /// is the whole rule's — the joiners, variation selectors and emoji tags always, the skin tones
    /// and the marks (Mn, Mc, Me) when the face has them. None of U+0000–U+02FF extends; U+0300
    /// does.
    /// </summary>
    [Fact]
    public void WhatExtendsTheCharacterBefore_IsTheWholeRule_AtEveryCodePoint()
    {
        foreach (var font in new[] { new FakeFont("All", _ => true), new FakeFont("None", _ => false) })
        {
            int belowMarks = 0;
            for (int cp = 0; cp <= 0x10FFFF; cp++)
            {
                bool always = cp is 0x200C or 0x200D or (>= 0xFE00 and <= 0xFE0F) or (>= 0xE0100 and <= 0xE01EF)
                    or (>= 0xE0020 and <= 0xE007F);
                bool ifInFace = cp is >= 0x1F3FB and <= 0x1F3FF
                    || System.Globalization.CharUnicodeInfo.GetUnicodeCategory(cp)
                        is System.Globalization.UnicodeCategory.NonSpacingMark
                        or System.Globalization.UnicodeCategory.SpacingCombiningMark
                        or System.Globalization.UnicodeCategory.EnclosingMark;
                bool rule = always || ifInFace && font.HasGlyph(cp);
                if (FontItemizer.Extends(cp, font) != rule)
                    Assert.Fail($"U+{cp:X4} in {font.FamilyName}: {!rule}, the whole rule says {rule}");
                if (cp < 0x300 && !rule) belowMarks++;
            }
            Assert.Equal(0x300, belowMarks);
        }
        Assert.True(FontItemizer.Extends(0x300, new FakeFont("All", _ => true)));
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
