using HultUI.Core.Styling;

namespace HultUI.Text;

/// <summary>
/// Splits text into segments where each segment uses a single font.
/// Handles font fallback for characters not covered by the primary font.
/// </summary>
internal static class FontItemizer
{
    /// <summary>The font runs of <paramref name="text"/>, into <paramref name="runs"/>, which is
    /// cleared first and returned.</summary>
    public static List<FontRun> Itemize(string text, IFontCollection fonts,
        string familyName, FontWeight weight, FontSlant slant, string? hanContext, List<FontRun> runs)
    {
        runs.Clear();
        if (string.IsNullOrEmpty(text))
            return runs;

        var primaryFont = fonts.Resolve(familyName, weight, slant);
        IResolvedFont? currentFont = null;
        int runStart = 0;
        bool afterJoiner = false;
        int emojiFrom = FirstThatMayAskForEmoji(text);

        int i = 0;
        while (i < text.Length)
        {
            // Guard against malformed input (lone or swapped surrogates).
            // Valid pairs are decoded to their codepoint; anything else is treated
            // as a BMP char so the shaper renders .notdef instead of crashing.
            int codepoint;
            int charLen;
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                codepoint = char.ConvertToUtf32(text[i], text[i + 1]);
                charLen = 2;
            }
            else
            {
                codepoint = text[i];
                charLen = 1;
            }

            // Resolve font for this codepoint. A character that extends the one before it stays in
            // that one's font: a sequence split across fonts is never shaped into its one glyph.
            // The joiners in 👨‍👩‍👧 are in most text faces, so each broke the family into three
            // people from the emoji font.
            // Then a character that asks to be an emoji (EmojiPresentation) goes to the colour emoji
            // face before the primary face is asked: text faces have many of them in black and white
            // — DejaVu Sans has 😀 and ❤, Segoe UI has # © ™ — so ❤️ and #️⃣ came out as text, and a
            // keycap split from its base into two fonts never became one glyph.
            IResolvedFont resolvedFont;
            if (currentFont is not null && (Extends(codepoint, currentFont) || afterJoiner && currentFont.HasGlyph(codepoint)))
            {
                resolvedFont = currentFont;
            }
            else if (i >= emojiFrom && AsksForEmoji(text, i, codepoint)
                     && fonts.MatchEmoji(codepoint, weight, slant) is { } emoji)
            {
                // A primary face that is itself the emoji face keeps one run with its spaces.
                resolvedFont = ReferenceEquals(emoji.NativeHandle, primaryFont.NativeHandle) ? primaryFont : emoji;
            }
            else if (primaryFont.HasGlyph(codepoint))
            {
                resolvedFont = primaryFont;
            }
            else
            {
                resolvedFont = fonts.MatchCharacter(codepoint, weight, slant, hanContext) ?? primaryFont;
            }
            afterJoiner = codepoint == ZeroWidthJoiner;

            // If font changed, emit the current run and start a new one
            if (currentFont is not null && !ReferenceEquals(resolvedFont, currentFont))
            {
                runs.Add(new FontRun(text[runStart..i], currentFont));
                runStart = i;
            }

            currentFont = resolvedFont;
            i += charLen;
        }

        // Emit final run
        if (currentFont is not null && runStart < text.Length)
            runs.Add(new FontRun(text[runStart..], currentFont));

        return runs;
    }

    private const int ZeroWidthJoiner = 0x200D;

    // Every character of every text itemized is asked whether it wants the emoji face, and text is
    // mostly letters: a call per character to EmojiPresentation cost itemizing 12 % a character
    // (ASCII 2.95 → 3.33 ns, box drawing 10.5 → 12.4) and a long document's open 6 % (JIT) and
    // 11 % (NativeAOT). So the common answers are worked out here, inline.

    /// <summary>
    /// Where in <paramref name="text"/> the first character that may ask for the emoji face can
    /// be, or <see cref="int.MaxValue"/> when none can. Below ‼ (U+203C) only a keycap base, © and
    /// ® can, and only before U+FE0F, which is above it: so a text with nothing from ‼ up asks
    /// nowhere — Latin, Greek, Cyrillic, Hebrew, Arabic, the Indic scripts, Thai, with their dashes
    /// and quotes — and in any text, nothing before the character just ahead of the first such
    /// one does. One vectorized pass.
    /// </summary>
    internal static int FirstThatMayAskForEmoji(string text)
    {
        // As NeedsBidi: the ushort search, because the char one boxes its bounds.
        int first = System.Runtime.InteropServices.MemoryMarshal.Cast<char, ushort>(text.AsSpan())
            .IndexOfAnyExceptInRange((ushort)0, (ushort)0x203B);
        return first < 0 ? int.MaxValue : Math.Max(0, first - 1);
    }

    /// <summary>
    /// <see cref="EmojiPresentation.AsksForEmoji"/>, the same answer, with the common ones worked
    /// out inline: below ‼ only a keycap base, © and ® ask, and only before U+FE0F; from ‼ to U+32FF
    /// only a pictograph does (and all that ask for emoji by default there are pictographs), one bit
    /// test; nothing from U+3300 (past ㊙) to U+1F000 (the Mahjong tiles) does.
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    internal static bool AsksForEmoji(string text, int i, int codepoint) =>
        codepoint < 0x203C
            ? i + 1 < text.Length && text[i + 1] == (char)EmojiPresentation.VariationSelector16
              && codepoint is '#' or '*' or (>= '0' and <= '9') or 0xA9 or 0xAE
            : codepoint < 0x3300
                ? EmojiPresentation.IsSymbolPictographic(codepoint) && EmojiPresentation.AsksForEmoji(text, i, codepoint, 1)
                : codepoint >= 0x1F000 && EmojiPresentation.AsksForEmoji(text, i, codepoint, codepoint > 0xFFFF ? 2 : 1);

    /// <summary>
    /// Whether <paramref name="codepoint"/> only extends the character before it, in
    /// <paramref name="font"/>: the joiners, variation selectors and emoji tag characters always
    /// (the shaper hides them, glyph or not); an emoji skin tone or a combining mark when the font
    /// has it.
    /// </summary>
    private static bool Extends(int codepoint, IResolvedFont font) => codepoint switch
    {
        ZeroWidthJoiner or 0x200C => true,
        >= 0xFE00 and <= 0xFE0F or >= 0xE0100 and <= 0xE01EF => true, // variation selectors
        >= 0xE0020 and <= 0xE007F => true,                           // emoji tags (subdivision flags)
        >= 0x1F3FB and <= 0x1F3FF => font.HasGlyph(codepoint),       // skin tones
        _ => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(codepoint)
                 is System.Globalization.UnicodeCategory.NonSpacingMark
                 or System.Globalization.UnicodeCategory.SpacingCombiningMark
                 or System.Globalization.UnicodeCategory.EnclosingMark
             && font.HasGlyph(codepoint),
    };
}

internal readonly record struct FontRun(string Text, IResolvedFont Font);
