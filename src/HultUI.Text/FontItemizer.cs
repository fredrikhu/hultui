using HultUI.Core.Styling;

namespace HultUI.Text;

/// <summary>
/// Splits text into segments where each segment uses a single font.
/// Handles font fallback for characters not covered by the primary font.
/// </summary>
internal static class FontItemizer
{
    public static List<FontRun> Itemize(string text, IFontCollection fonts,
        string familyName, FontWeight weight, FontSlant slant)
    {
        var runs = new List<FontRun>();
        if (string.IsNullOrEmpty(text))
            return runs;

        var primaryFont = fonts.Resolve(familyName, weight, slant);
        IResolvedFont? currentFont = null;
        int runStart = 0;

        int i = 0;
        while (i < text.Length)
        {
            int codepoint = char.IsHighSurrogate(text[i]) && i + 1 < text.Length
                ? char.ConvertToUtf32(text[i], text[i + 1])
                : text[i];
            int charLen = codepoint > 0xFFFF ? 2 : 1;

            // Resolve font for this codepoint
            IResolvedFont resolvedFont;
            if (primaryFont.HasGlyph(codepoint))
            {
                resolvedFont = primaryFont;
            }
            else
            {
                resolvedFont = fonts.MatchCharacter(codepoint, weight, slant) ?? primaryFont;
            }

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
}

internal readonly record struct FontRun(string Text, IResolvedFont Font);
