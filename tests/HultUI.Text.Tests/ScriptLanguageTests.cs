using HultUI.Text;
using Xunit;

namespace HultUI.Text.Tests;

/// <summary>
/// The language hint font fallback passes to the platform, from ICU's script property and
/// CLDR likely subtags. Null is "no hint" — the answer for characters that belong to
/// every script.
/// </summary>
public class ScriptLanguageTests
{
    [Theory]
    [InlineData(0x05D0, "he")]   // א Hebrew
    [InlineData(0x0628, "ar")]   // ب Arabic
    [InlineData(0x0E01, "th")]   // ก Thai
    [InlineData(0x0915, "hi")]   // क Devanagari
    [InlineData(0x3042, "ja")]   // あ Hiragana
    [InlineData(0xAC00, "ko")]   // 가 Hangul
    [InlineData(0x4E2D, "zh")]   // 中 Han — CLDR's default for the script
    [InlineData(0x0416, "ru")]   // Ж Cyrillic
    [InlineData(0x0041, "en")]   // A Latin
    public void AScriptCharacter_GetsItsLikelyLanguage(int codepoint, string expected)
    {
        Assert.Equal(expected, ScriptLanguage.ForCodepoint(codepoint));
    }

    [Theory]
    [InlineData(0x0020)]    // space — Common
    [InlineData(0x0031)]    // 1 — Common
    [InlineData(0x1F600)]   // 😀 Common, outside the BMP
    [InlineData(0x0301)]    // combining acute accent — Inherited
    [InlineData(0x0378)]    // unassigned — Unknown
    public void ACharacterOfNoOneScript_GetsNoHint(int codepoint)
    {
        Assert.Null(ScriptLanguage.ForCodepoint(codepoint));
    }
}
