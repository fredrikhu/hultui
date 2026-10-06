using HultUI.Text;
using Xunit;

namespace HultUI.Text.Tests;

/// <summary>
/// Which text the BiDi path keeps. It used to take everything with a char at or above
/// U+0590; now only what can need it: something that can be right-to-left or steers
/// direction, or something that joins its neighbours into one user-perceived character,
/// which the plain path's cuts between chars would split.
/// </summary>
public class BidiRoutingTests
{
    [Theory]
    [InlineData(0x05D0, "Hebrew")]
    [InlineData(0x0627, "Arabic")]
    [InlineData(0x0710, "Syriac")]
    [InlineData(0x07C0, "NKo")]
    [InlineData(0xFB1D, "Hebrew presentation form")]
    [InlineData(0xFE70, "Arabic presentation form B")]
    [InlineData(0x200E, "left-to-right mark")]
    [InlineData(0x200F, "right-to-left mark")]
    [InlineData(0x202B, "right-to-left embedding")]
    [InlineData(0x2067, "right-to-left isolate")]
    [InlineData(0xD83D, "high surrogate (emoji)")]
    [InlineData(0xDE00, "low surrogate")]
    [InlineData(0x200D, "zero-width joiner")]
    [InlineData(0x200B, "zero-width space")]
    [InlineData(0xFE0F, "variation selector")]
    [InlineData(0x20E3, "enclosing keycap")]
    [InlineData(0x0903, "Devanagari visarga, a spacing mark")]
    [InlineData(0x093F, "Devanagari vowel sign i")]
    [InlineData(0x0E31, "Thai vowel mark")]
    [InlineData(0x1100, "Hangul leading jamo")]
    [InlineData(0x11A8, "Hangul trailing jamo")]
    [InlineData(0xD7B0, "Hangul jamo extended-B")]
    [InlineData(0x0D4E, "Malayalam dot reph, a prepend")]
    [InlineData(0x2028, "line separator")]
    [InlineData(0x2029, "paragraph separator")]
    [InlineData(0xFEFF, "byte order mark")]
    [InlineData(0x0E3B, "unassigned")]
    public void KeepsItOnTheBidiPath(int c, string what) =>
        Assert.True(TextPipeline.MayNeedBidi((char)c), what);

    [Theory]
    [InlineData(0x0061, "Latin")]
    [InlineData(0x00E9, "Latin-1")]
    [InlineData(0x0414, "Cyrillic")]
    [InlineData(0x0301, "a combining mark below U+0590: the plain path already had those")]
    [InlineData(0x2014, "em dash")]
    [InlineData(0x2013, "en dash")]
    [InlineData(0x2026, "ellipsis")]
    [InlineData(0x201C, "left double quote")]
    [InlineData(0x2022, "bullet")]
    [InlineData(0x2192, "rightwards arrow")]
    [InlineData(0x221E, "infinity")]
    [InlineData(0x2500, "box drawing")]
    [InlineData(0x2713, "check mark")]
    [InlineData(0x20AC, "euro sign")]
    [InlineData(0x1F00, "Greek extended")]
    [InlineData(0x1E0D, "Latin extended additional")]
    [InlineData(0x3001, "ideographic comma")]
    [InlineData(0x3042, "hiragana")]
    [InlineData(0x30AB, "katakana")]
    [InlineData(0x6771, "CJK ideograph")]
    [InlineData(0xAC00, "Hangul syllable")]
    [InlineData(0xFF21, "full-width Latin")]
    [InlineData(0xFF76, "half-width katakana")]
    [InlineData(0xE000, "private use (icon fonts)")]
    [InlineData(0x0E01, "Thai consonant")]
    public void LeavesItOnThePlainPath(int c, string what) =>
        Assert.False(TextPipeline.MayNeedBidi((char)c), what);
}
