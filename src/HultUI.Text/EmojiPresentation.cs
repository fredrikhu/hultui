namespace HultUI.Text;

/// <summary>
/// Which characters ask to be drawn as emoji — from a colour emoji face — rather than as text
/// (Unicode Technical Standard #51, "Presentation Style").
/// <list type="bullet">
/// <item>A character whose default is emoji presentation (<c>Emoji_Presentation=Yes</c>): 😀 ⭐ ✅,
/// a regional indicator of a flag, a skin tone.</item>
/// <item>Any pictograph, or a keycap base (<c>#</c>, <c>*</c>, a digit), followed by U+FE0F,
/// variation selector 16: ❤️ ™️ ©️ #️⃣.</item>
/// <item>A pictograph followed by a skin tone (☝🏽), or by a joiner and another pictograph (👁‍🗨): the
/// first of a modifier or a joined sequence, which is an emoji whatever its own default.</item>
/// </list>
/// U+FE0E, variation selector 15, asks for text: 😀︎ is not asked for as an emoji. Everything else —
/// plain ™ © # 1, ❤ and ↔ without a selector — keeps its default, text, and is drawn from the text
/// face as before.
///
/// <para>The tables are Unicode 16.0's <c>emoji-data.txt</c>, as ICU 77 has them; a test compares
/// them with the running ICU. Fixed rather than asked of ICU: the answer is the same on every
/// machine, whatever ICU it has, and a character costs a bit test, not a call into ICU.</para>
/// </summary>
internal static class EmojiPresentation
{
    internal const int VariationSelector15 = 0xFE0E, VariationSelector16 = 0xFE0F, ZeroWidthJoiner = 0x200D;

    /// <summary>The Unicode version the tables are from.</summary>
    internal static readonly Version UnicodeVersion = new(16, 0);

    // Emoji_Presentation=Yes: first and last code point of each range.
    private static readonly int[] PresentationRanges =
    [
        0x231A, 0x231B, 0x23E9, 0x23EC, 0x23F0, 0x23F0, 0x23F3, 0x23F3, 0x25FD, 0x25FE, 0x2614, 0x2615,
        0x2648, 0x2653, 0x267F, 0x267F, 0x2693, 0x2693, 0x26A1, 0x26A1, 0x26AA, 0x26AB, 0x26BD, 0x26BE,
        0x26C4, 0x26C5, 0x26CE, 0x26CE, 0x26D4, 0x26D4, 0x26EA, 0x26EA, 0x26F2, 0x26F3, 0x26F5, 0x26F5,
        0x26FA, 0x26FA, 0x26FD, 0x26FD, 0x2705, 0x2705, 0x270A, 0x270B, 0x2728, 0x2728, 0x274C, 0x274C,
        0x274E, 0x274E, 0x2753, 0x2755, 0x2757, 0x2757, 0x2795, 0x2797, 0x27B0, 0x27B0, 0x27BF, 0x27BF,
        0x2B1B, 0x2B1C, 0x2B50, 0x2B50, 0x2B55, 0x2B55, 0x1F004, 0x1F004, 0x1F0CF, 0x1F0CF, 0x1F18E, 0x1F18E,
        0x1F191, 0x1F19A, 0x1F1E6, 0x1F1FF, 0x1F201, 0x1F201, 0x1F21A, 0x1F21A, 0x1F22F, 0x1F22F, 0x1F232, 0x1F236,
        0x1F238, 0x1F23A, 0x1F250, 0x1F251, 0x1F300, 0x1F320, 0x1F32D, 0x1F335, 0x1F337, 0x1F37C, 0x1F37E, 0x1F393,
        0x1F3A0, 0x1F3CA, 0x1F3CF, 0x1F3D3, 0x1F3E0, 0x1F3F0, 0x1F3F4, 0x1F3F4, 0x1F3F8, 0x1F43E, 0x1F440, 0x1F440,
        0x1F442, 0x1F4FC, 0x1F4FF, 0x1F53D, 0x1F54B, 0x1F54E, 0x1F550, 0x1F567, 0x1F57A, 0x1F57A, 0x1F595, 0x1F596,
        0x1F5A4, 0x1F5A4, 0x1F5FB, 0x1F64F, 0x1F680, 0x1F6C5, 0x1F6CC, 0x1F6CC, 0x1F6D0, 0x1F6D2, 0x1F6D5, 0x1F6D7,
        0x1F6DC, 0x1F6DF, 0x1F6EB, 0x1F6EC, 0x1F6F4, 0x1F6FC, 0x1F7E0, 0x1F7EB, 0x1F7F0, 0x1F7F0, 0x1F90C, 0x1F93A,
        0x1F93C, 0x1F945, 0x1F947, 0x1F9FF, 0x1FA70, 0x1FA7C, 0x1FA80, 0x1FA89, 0x1FA8F, 0x1FAC6, 0x1FACE, 0x1FADC,
        0x1FADF, 0x1FAE9, 0x1FAF0, 0x1FAF8,
    ];

    // Extended_Pictographic=Yes: first and last code point of each range.
    private static readonly int[] PictographicRanges =
    [
        0x00A9, 0x00A9, 0x00AE, 0x00AE, 0x203C, 0x203C, 0x2049, 0x2049, 0x2122, 0x2122, 0x2139, 0x2139,
        0x2194, 0x2199, 0x21A9, 0x21AA, 0x231A, 0x231B, 0x2328, 0x2328, 0x2388, 0x2388, 0x23CF, 0x23CF,
        0x23E9, 0x23F3, 0x23F8, 0x23FA, 0x24C2, 0x24C2, 0x25AA, 0x25AB, 0x25B6, 0x25B6, 0x25C0, 0x25C0,
        0x25FB, 0x25FE, 0x2600, 0x2605, 0x2607, 0x2612, 0x2614, 0x2685, 0x2690, 0x2705, 0x2708, 0x2712,
        0x2714, 0x2714, 0x2716, 0x2716, 0x271D, 0x271D, 0x2721, 0x2721, 0x2728, 0x2728, 0x2733, 0x2734,
        0x2744, 0x2744, 0x2747, 0x2747, 0x274C, 0x274C, 0x274E, 0x274E, 0x2753, 0x2755, 0x2757, 0x2757,
        0x2763, 0x2767, 0x2795, 0x2797, 0x27A1, 0x27A1, 0x27B0, 0x27B0, 0x27BF, 0x27BF, 0x2934, 0x2935,
        0x2B05, 0x2B07, 0x2B1B, 0x2B1C, 0x2B50, 0x2B50, 0x2B55, 0x2B55, 0x3030, 0x3030, 0x303D, 0x303D,
        0x3297, 0x3297, 0x3299, 0x3299, 0x1F000, 0x1F0FF, 0x1F10D, 0x1F10F, 0x1F12F, 0x1F12F, 0x1F16C, 0x1F171,
        0x1F17E, 0x1F17F, 0x1F18E, 0x1F18E, 0x1F191, 0x1F19A, 0x1F1AD, 0x1F1E5, 0x1F201, 0x1F20F, 0x1F21A, 0x1F21A,
        0x1F22F, 0x1F22F, 0x1F232, 0x1F23A, 0x1F23C, 0x1F23F, 0x1F249, 0x1F3FA, 0x1F400, 0x1F53D, 0x1F546, 0x1F64F,
        0x1F680, 0x1F6FF, 0x1F774, 0x1F77F, 0x1F7D5, 0x1F7FF, 0x1F80C, 0x1F80F, 0x1F848, 0x1F84F, 0x1F85A, 0x1F85F,
        0x1F888, 0x1F88F, 0x1F8AE, 0x1F8FF, 0x1F90C, 0x1F93A, 0x1F93C, 0x1F945, 0x1F947, 0x1FAFF, 0x1FC00, 0x1FFFD,
    ];

    // Below U+10000 both properties lie in U+2000…U+32FF, but for © and ®: a bit a code point
    // there, so kana and CJK punctuation, box drawing and arrows cost one test each.
    private const int BmpFirst = 0x2000, BmpEnd = 0x3300;
    private static readonly ulong[] PresentationBits = Bits(PresentationRanges);
    private static readonly ulong[] PictographicBits = Bits(PictographicRanges);

    /// <summary>Whether <paramref name="codepoint"/> is <c>Emoji_Presentation=Yes</c>.</summary>
    internal static bool IsEmojiPresentation(int codepoint) => Has(codepoint, PresentationBits, PresentationRanges);

    /// <summary>Whether <paramref name="codepoint"/> is <c>Extended_Pictographic=Yes</c>.</summary>
    internal static bool IsExtendedPictographic(int codepoint) =>
        codepoint is 0xA9 or 0xAE || Has(codepoint, PictographicBits, PictographicRanges);

    /// <summary><see cref="IsExtendedPictographic"/> for a code point from U+2000 to U+32FF, where
    /// the BMP's pictographs are: one bit test. Every <c>Emoji_Presentation</c> character there is
    /// one of them.</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    internal static bool IsSymbolPictographic(int codepoint)
    {
        int i = codepoint - BmpFirst;
        return (PictographicBits[i >> 6] & (1UL << i)) != 0;
    }

    /// <summary>
    /// Whether the character <paramref name="codepoint"/>, <paramref name="length"/> chars at
    /// <paramref name="index"/> of <paramref name="text"/>, asks to be drawn from an emoji face: by
    /// itself or by what follows it in <paramref name="text"/> (see the class).
    /// </summary>
    internal static bool AsksForEmoji(string text, int index, int codepoint, int length)
    {
        int after = index + length;
        // Below ©, only a keycap base can, and only with the selector after it; then nothing
        // but © and ® up to ‼.
        if (codepoint < 0xA9)
            return codepoint is '#' or '*' or (>= '0' and <= '9')
                && after < text.Length && text[after] == (char)VariationSelector16;
        if (codepoint < 0x203C)
            return codepoint is 0xA9 or 0xAE
                && after < text.Length && text[after] == (char)VariationSelector16;
        if (codepoint is >= BmpEnd and < 0x1F000) return false;
        if (codepoint < BmpEnd && !IsSymbolPictographic(codepoint)) return false;   // nor emoji by default

        int next = CodePointAt(text, after);
        if (next == VariationSelector15) return false;
        if (next == VariationSelector16) return IsEmojiPresentation(codepoint) || IsExtendedPictographic(codepoint);
        if (IsEmojiPresentation(codepoint)) return true;
        if (!IsExtendedPictographic(codepoint)) return false;
        if (next is >= 0x1F3FB and <= 0x1F3FF) return true;                     // a skin tone: ☝🏽
        return next == ZeroWidthJoiner && IsExtendedPictographic(CodePointAt(text, after + 1)); // 👁‍🗨
    }

    private static int CodePointAt(string text, int index)
    {
        if ((uint)index >= (uint)text.Length) return -1;
        char c = text[index];
        return char.IsHighSurrogate(c) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1])
            ? char.ConvertToUtf32(c, text[index + 1])
            : c;
    }

    private static bool Has(int codepoint, ulong[] bits, int[] ranges)
    {
        if (codepoint < 0x10000)
        {
            int i = codepoint - BmpFirst;
            return (uint)i < BmpEnd - BmpFirst && (bits[i >> 6] & (1UL << i)) != 0;
        }
        int lo = 0, hi = ranges.Length / 2 - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >>> 1;
            if (codepoint < ranges[2 * mid]) hi = mid - 1;
            else if (codepoint > ranges[2 * mid + 1]) lo = mid + 1;
            else return true;
        }
        return false;
    }

    private static ulong[] Bits(int[] ranges)
    {
        var bits = new ulong[(BmpEnd - BmpFirst + 63) / 64];
        for (int r = 0; r < ranges.Length; r += 2)
            for (int c = Math.Max(ranges[r], BmpFirst); c <= ranges[r + 1] && c < BmpEnd; c++)
                bits[(c - BmpFirst) >> 6] |= 1UL << (c - BmpFirst);
        return bits;
    }
}
