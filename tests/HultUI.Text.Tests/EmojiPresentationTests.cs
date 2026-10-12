using System.Runtime.InteropServices;
using HultUI.Core.Styling;
using HultUI.Text;
using Xunit;

namespace HultUI.Text.Tests;

/// <summary>
/// A character that asks to be drawn as an emoji (Unicode Technical Standard #51) goes to the colour
/// emoji face before the primary face is asked, and one that does not stays in the text face. The
/// text face here has the emoji a text face has in black and white — DejaVu Sans has 😀 and ❤,
/// Segoe UI # © ™ and the keycap mark — so every case below drew from it before.
/// </summary>
public class EmojiPresentationTests
{
    private const int VS15 = 0xFE0E, VS16 = 0xFE0F, ZWJ = 0x200D, Keycap = 0x20E3;

    [Fact]
    public void ACharacterThatIsAnEmojiByDefault_IsDrawnFromTheEmojiFace()
    {
        Assert.Equal([(S(0x1F600), "Emoji")], Runs(S(0x1F600)));                          // 😀
        Assert.Equal([(S(0x2615), "Emoji")], Runs(S(0x2615)));                            // ☕, plain path
        Assert.Equal([("a ", "Text"), (S(0x1F600), "Emoji"), (" b", "Text")], Runs("a " + S(0x1F600) + " b"));
    }

    [Fact]
    public void TheEmojiSelector_AsksForTheEmojiFace_AndTheTextSelectorForTheTextFace()
    {
        Assert.Equal([(S(0x2764), "Text")], Runs(S(0x2764)));                             // ❤
        Assert.Equal([(S(0x2764, VS16), "Emoji")], Runs(S(0x2764, VS16)));                // ❤️
        Assert.Equal([(S(0x2764, VS15), "Text")], Runs(S(0x2764, VS15)));                 // ❤︎
        Assert.Equal([(S(0x1F600, VS15), "Text")], Runs(S(0x1F600, VS15)));               // 😀︎
        Assert.Equal([(S(0x2194), "Text")], Runs(S(0x2194)));                             // ↔
        Assert.Equal([(S(0x2194, VS16), "Emoji")], Runs(S(0x2194, VS16)));                // ↔️
    }

    [Fact]
    public void TradeMarkCopyrightAndRegistered_AreEmojiOnlyWithTheSelector()
    {
        Assert.Equal([(S(0x2122, ' ', 0xA9, ' ', 0xAE), "Text")], Runs(S(0x2122, ' ', 0xA9, ' ', 0xAE)));
        foreach (int c in new[] { 0x2122, 0xA9, 0xAE })
            Assert.Equal([(S(c, VS16), "Emoji")], Runs(S(c, VS16)));
        Assert.Equal([("x ", "Text"), (S(0x2122, VS16), "Emoji"), (" y", "Text")], Runs("x " + S(0x2122, VS16) + " y"));
    }

    [Fact]
    public void AKeycap_IsOneRunInTheEmojiFace_AndAPlainDigitStaysText()
    {
        foreach (char key in "#*05")
            Assert.Equal([(S(key, VS16, Keycap), "Emoji")], Runs(S(key, VS16, Keycap)));
        Assert.Equal([("# 1 * 42", "Text")], Runs("# 1 * 42"));
        Assert.Equal([(S('#', Keycap), "Text")], Runs(S('#', Keycap)));                   // no selector: not a keycap sequence
        Assert.Equal([("a", "Text"), (S('1', VS16, Keycap), "Emoji"), ("2", "Text")], Runs("a" + S('1', VS16, Keycap) + "2"));
    }

    [Fact]
    public void JoinedModifiedFlagAndTagSequences_AreOneRunInTheEmojiFace()
    {
        string eyeInBubble = S(0x1F441, ZWJ, 0x1F5E8);     // 👁‍🗨: both text by default
        string raisedFinger = S(0x261D, 0x1F3FD);          // ☝🏽: text by default, with a skin tone
        string heartOnFire = S(0x2764, VS16, ZWJ, 0x1F525); // ❤️‍🔥
        string flag = S(0x1F1F8, 0x1F1EA);                  // 🇸🇪
        string scotland = S(0x1F3F4, 0xE0067, 0xE0062, 0xE0073, 0xE0063, 0xE0074, 0xE007F);
        foreach (var sequence in new[] { eyeInBubble, raisedFinger, heartOnFire, flag, scotland })
            Assert.Equal([(sequence, "Emoji")], Runs(sequence));
        Assert.Equal([(S(0x1F441), "Text")], Runs(S(0x1F441)));                           // 👁 alone stays text
        Assert.Equal([(S(0x261D), "Text")], Runs(S(0x261D)));                             // ☝ alone too
    }

    [Fact]
    public void AJoinerInText_DoesNotAskForAnEmoji()
    {
        // A joiner between letters (Indic conjunct control, Arabic) or after a pictograph that
        // joins no other pictograph.
        Assert.Equal([(S('a', ZWJ, 'b'), "Text")], Runs(S('a', ZWJ, 'b')));
        Assert.Equal([(S(0x2764, ZWJ, 'b'), "Text")], Runs(S(0x2764, ZWJ, 'b')));
    }

    [Fact]
    public void AnEmojiTheEmojiFaceLacks_FallsBackAsBefore()
    {
        Assert.Equal([(S(0x231B), "Text")], Runs(S(0x231B)));                             // ⌛: the text face's
    }

    [Fact]
    public void ACollectionWithoutAnEmojiFace_KeepsEverythingInTheTextFace()
    {
        var pipeline = new TextPipeline(new Fonts(emojiFace: false), new AdvanceShaper());
        Assert.Equal([(S(0x1F600, ' ', '#', VS16, Keycap), "Text")], Runs(pipeline, S(0x1F600, ' ', '#', VS16, Keycap)));
    }

    [Fact]
    public void APrimaryFaceThatIsTheEmojiFace_KeepsOneRun()
    {
        var pipeline = new TextPipeline(new Fonts(emojiPrimary: true), new AdvanceShaper());
        Assert.Equal([(S('a', ' ', 0x1F600, ' ', 'b'), "Emoji")], Runs(pipeline, S('a', ' ', 0x1F600, ' ', 'b')));
    }

    [Fact]
    public void TheEmojiFaceIsAskedWithTheStylesWeightAndSlant()
    {
        var fonts = new Fonts();
        var pipeline = new TextPipeline(fonts, new AdvanceShaper());
        pipeline.Layout(S(0x1F600), Style() with { FontWeight = FontWeight.Bold, FontSlant = FontSlant.Italic }, 10_000, null);
        Assert.Equal((FontWeight.Bold, FontSlant.Italic), fonts.LastEmojiStyle);
    }

    // ── Caches: presentation is a function of the text, and the face is in every font-keyed key ──

    [Fact]
    public void TheSameCharacter_InTurnAsTextAndAsEmoji_IsNotServedFromTheOthersCacheEntry()
    {
        var pipeline = Pipeline();
        Assert.Equal([("#", "Text")], Runs(pipeline, "#"));
        Assert.Equal([(S('#', VS16, Keycap), "Emoji")], Runs(pipeline, S('#', VS16, Keycap)));
        Assert.Equal([("#", "Text")], Runs(pipeline, "#"));

        // The word cache keys on the face: ❤ the word, then ❤️ the word, then ❤ again.
        Assert.Equal([("a " + S(0x2764) + " b", "Text")], Runs(pipeline, "a " + S(0x2764) + " b"));
        Assert.Equal([("a ", "Text"), (S(0x2764, VS16), "Emoji"), (" b", "Text")], Runs(pipeline, "a " + S(0x2764, VS16) + " b"));
        Assert.Equal([("c " + S(0x2764) + " d", "Text")], Runs(pipeline, "c " + S(0x2764) + " d"));
    }

    [Fact]
    public void AWidth_IsTheWidthOfTheFacesTheLayoutDrawsFrom()
    {
        var pipeline = Pipeline();
        var style = Style();
        string keycap = S('1', VS16, Keycap);
        // The emoji face is twice as wide: the keycap's three chars measure 3 × 20, the digit 10.
        Assert.Equal(10f, pipeline.MeasureWidth("1", style));
        Assert.Equal(60f, pipeline.MeasureWidth(keycap, style));
        Assert.Equal(pipeline.Layout(keycap, style, 10_000, null).Size.Width, pipeline.MeasureWidth(keycap, style));
        Assert.Equal(10f, pipeline.MeasureWidth("1", style));
    }

    [Fact]
    public void APlainPathPrefixTable_MeasuresTheEmojiInTheEmojiFace()
    {
        // ⭐ and ✅ are below U+10000 and left to right: the plain path, whose prefix table must
        // read the faces the layout of each prefix does.
        string text = "a " + S(0x2B50) + " b" + S(0x2705);
        var style = Style();
        var prefixes = Pipeline().MeasurePrefixes(text, style);
        Assert.NotNull(prefixes);
        for (int n = 0; n <= text.Length; n++)
            Assert.Equal(Pipeline().Layout(text[..n], style, float.PositiveInfinity, maxLines: 1).Size.Width, prefixes.Width(n));
        Assert.Equal(2 * 10f + 20f, prefixes.Width(3));
    }

    // ── The tables are Unicode's ──

    /// <summary>
    /// The presentation tables against the running ICU's: every code point ICU has assigned, up to
    /// the tables' version, has the same <c>Emoji_Presentation</c> and <c>Extended_Pictographic</c>.
    /// An older ICU leaves out what it does not know yet; a newer one, what came after.
    /// </summary>
    [Fact]
    public void TheTables_AreUnicodes()
    {
        var has = IcuLibrary.GetDelegate<HasBinaryProperty>("u_hasBinaryProperty");
        var age = IcuLibrary.GetDelegate<CharAge>("u_charAge");
        var tables = EmojiPresentation.UnicodeVersion;
        int compared = 0;
        var version = new byte[4];
        for (int c = 0; c <= 0x10FFFF; c++)
        {
            age(c, version);
            var assigned = new Version(version[0], version[1]);
            if (assigned > tables) continue;                    // newer than the tables
            Assert.True(EmojiPresentation.IsExtendedPictographic(c) == (has(c, ExtendedPictographic) != 0), $"Extended_Pictographic U+{c:X4}");
            if (version[0] == 0) continue;                      // not assigned in this ICU
            Assert.True(EmojiPresentation.IsEmojiPresentation(c) == (has(c, EmojiPresentationProperty) != 0), $"Emoji_Presentation U+{c:X4}");
            compared++;
        }
        Assert.True(compared > 250_000, $"{compared} assigned code points compared");
    }

    /// <summary>
    /// The itemizer answers the common cases inline (FontItemizer.AsksForEmoji, and where in a
    /// text the first asking character can be): the same answer as the rules, for every code point
    /// alone and before each thing that can change it, and wherever in a text it stands.
    /// </summary>
    [Fact]
    public void TheItemizersShortcut_AnswersAsTheRulesDo()
    {
        string[] after = ["", S(VS16), S(VS15), S(VS16, Keycap), S(0x1F3FD), S(ZWJ, 0x1F5E8), S(ZWJ, 'b'), "a"];
        int asked = 0;
        for (int c = 0; c <= 0x10FFFF; c++)
        {
            if (c is >= 0xD800 and <= 0xDFFF) continue;
            if ((c is > 0x3400 and < 0x1F000 or > 0x1FFFF) && c % 97 != 0) continue;   // nothing asks there: a sample
            string ch = char.ConvertFromUtf32(c);
            foreach (var tail in after)
            {
                foreach (var head in new[] { "", "ab" })
                {
                    string text = head + ch + tail;
                    bool rules = EmojiPresentation.AsksForEmoji(text, head.Length, c, ch.Length);
                    bool shortcut = head.Length >= FontItemizer.FirstThatMayAskForEmoji(text)
                        && FontItemizer.AsksForEmoji(text, head.Length, c);
                    if (rules != shortcut) Assert.Fail($"U+{c:X4} before [{string.Join(" ", tail.EnumerateRunes().Select(r => $"U+{r.Value:X4}"))}]: rules {rules}, shortcut {shortcut}");
                    if (rules) asked++;
                }
            }
        }
        Assert.True(asked > 5_000, $"{asked} asked");

        // Below U+3300 every character emoji by default is a pictograph, which the shortcut relies on.
        for (int c = 0; c < 0x3300; c++)
            Assert.False(EmojiPresentation.IsEmojiPresentation(c) && !EmojiPresentation.IsExtendedPictographic(c), $"U+{c:X4}");
    }

    private const int EmojiPresentationProperty = 58, ExtendedPictographic = 64;   // UProperty

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate sbyte HasBinaryProperty(int c, int which);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void CharAge(int c, [Out] byte[] versionArray);

    private static string S(params int[] codePoints) => string.Concat(codePoints.Select(char.ConvertFromUtf32));

    private static TextPipeline Pipeline() => new(new Fonts(), new AdvanceShaper());

    private static List<(string, string)> Runs(string text) => Runs(Pipeline(), text);

    private static List<(string, string)> Runs(TextPipeline pipeline, string text) =>
        Assert.Single(pipeline.Layout(text, Style(), 10_000, null).Lines).Runs.Select(r => (r.Text, r.Font.FamilyName)).ToList();

    private static ResolvedInheritedStyle Style() => new(
        FontFamily: "Text", FontSize: 10f, FontWeight: FontWeight.Regular,
        TextColor: new(0, 0, 0, 255), TextDirection: TextDirection.Ltr, Cursor: CursorType.Default);

    /// <summary>
    /// A text face with ASCII, Latin-1, the joiners, selectors and keycap mark, and black-and-white
    /// pictographs (😀 ❤ ™ ↔ ☝ 👁 🗨 ☕ ⌛, the flag letters, the black flag and tags); an emoji face
    /// twice as wide with every pictograph but ⌛, the keycap bases, © ® ™, the joiner, the emoji
    /// selector and the skin tones.
    /// </summary>
    private sealed class Fonts : IFontCollection
    {
        private readonly bool _emojiFace;
        private readonly Font _text = new("Text", 1f, cp => cp < 0x0250 || cp is ZWJ or VS15 or VS16 or Keycap
            or 0x2122 or 0x2194 or 0x2764 or 0x261D or 0x2615 or 0x231B or 0x1F600 or 0x1F441 or 0x1F5E8
            or (>= 0x1F1E6 and <= 0x1F1FF) or 0x1F3F4 or (>= 0xE0020 and <= 0xE007F));
        private readonly Font _emoji = new("Emoji", 2f, EmojiHas);
        private readonly Font _primary;

        /// <param name="emojiFace">Whether the collection knows an emoji face.</param>
        /// <param name="emojiPrimary">Whether the primary face is the emoji face, as another
        /// wrapper of the same native face (a resolved family and a fallback are different objects).</param>
        public Fonts(bool emojiFace = true, bool emojiPrimary = false)
        {
            _emojiFace = emojiFace;
            _primary = emojiPrimary ? new Font("Emoji", 2f, EmojiHas, handle: _emoji) : _text;
        }

        private static bool EmojiHas(int cp) => cp != 0x231B
            && (cp is '#' or '*' or (>= '0' and <= '9') or 0xA9 or 0xAE or ' ' or (>= 'a' and <= 'z')
                or ZWJ or VS16 or Keycap or (>= 0x1F3FB and <= 0x1F3FF) or (>= 0xE0020 and <= 0xE007F)
                || cp >= 0x2000 && EmojiPresentation.IsExtendedPictographic(cp)
                || EmojiPresentation.IsEmojiPresentation(cp));

        public (FontWeight, FontSlant)? LastEmojiStyle { get; private set; }

        public IResolvedFont Resolve(string familyName, FontWeight weight, FontSlant slant) => _primary;

        public IResolvedFont? MatchCharacter(int codepoint, FontWeight weight, FontSlant slant) =>
            _emoji.HasGlyph(codepoint) ? _emoji : null;

        public IResolvedFont? MatchEmoji(int codepoint, FontWeight weight, FontSlant slant)
        {
            LastEmojiStyle = (weight, slant);
            return _emojiFace && _emoji.HasGlyph(codepoint) ? _emoji : null;
        }
    }

    private sealed class Font(string family, float advance, Func<int, bool> has, object? handle = null) : IResolvedFont
    {
        public float Advance => advance;
        public string FamilyName => family;
        public float Ascent => -0.8f;
        public float Descent => 0.2f;
        public float Leading => 0f;
        public float LineHeight => 1f;
        public object NativeHandle => handle ?? this;
        public bool HasGlyph(int codepoint) => has(codepoint);
        public void Dispose() { }
    }

    private sealed class AdvanceShaper : ITextShaper
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
