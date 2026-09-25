using System.Globalization;
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
    [InlineData(0x3105, "zh-TW")]   // ㄅ Bopomofo — keeps its region; fontconfig ignores a bare "zh"
    [InlineData(0x0416, "ru")]   // Ж Cyrillic
    [InlineData(0x0041, "en")]   // A Latin
    public void AScriptCharacter_GetsItsLikelyLanguage(int codepoint, string expected)
    {
        Assert.Equal(expected, ScriptLanguage.ForCodepoint(codepoint, CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("ja-JP", "ja")]
    [InlineData("ko-KR", "ko")]
    [InlineData("zh-CN", "zh-CN")]
    [InlineData("zh-TW", "zh-TW")]
    [InlineData("zh-HK", "zh-HK")]
    [InlineData("zh-SG", "zh-SG")]
    [InlineData("zh-Hans", "zh-CN")]
    [InlineData("zh-Hant", "zh-TW")]
    [InlineData("zh-Hans-HK", "zh-CN")]     // the script wins: Simplified, read in Hong Kong
    [InlineData("zh-Hant-HK", "zh-HK")]
    [InlineData("en-US", "zh-CN")]          // not a Han reader: CLDR's default for the script
    [InlineData("sv-SE", "zh-CN")]
    [InlineData("", "zh-CN")]               // invariant
    public void Han_FollowsTheReadersCulture(string culture, string expected)
    {
        Assert.Equal(expected, ScriptLanguage.ForCodepoint(0x8AAA, new CultureInfo(culture)));   // 說
    }

    [Fact]
    public void KanaAndHangul_DoNotFollowTheCulture()
    {
        var chinese = new CultureInfo("zh-TW");
        Assert.Equal("ja", ScriptLanguage.ForCodepoint(0x3042, chinese));
        Assert.Equal("ko", ScriptLanguage.ForCodepoint(0xAC00, chinese));
    }

    [Theory]
    [InlineData(0x0020)]    // space — Common
    [InlineData(0x0031)]    // 1 — Common
    [InlineData(0x1F600)]   // 😀 Common, outside the BMP
    [InlineData(0x0301)]    // combining acute accent — Inherited
    [InlineData(0x0378)]    // unassigned — Unknown
    public void ACharacterOfNoOneScript_GetsNoHint(int codepoint)
    {
        Assert.Null(ScriptLanguage.ForCodepoint(codepoint, CultureInfo.InvariantCulture));
    }
}
