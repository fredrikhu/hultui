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
            IResolvedFont resolvedFont;
            if (currentFont is not null && (Extends(codepoint, currentFont) || afterJoiner && currentFont.HasGlyph(codepoint)))
            {
                resolvedFont = currentFont;
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
