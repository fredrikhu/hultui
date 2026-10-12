using HultUI.Core.Styling;
using HultUI.Rendering.Skia;
using HultUI.Text;
using SkiaSharp;
using Xunit;

namespace HultUI.Rendering.Skia.Tests;

/// <summary>
/// The faces a character that asks to be an emoji is drawn from on this machine. Before,
/// fontconfig answered a bare "who has 😀" with DejaVu Sans and so did the primary face for ❤️, so
/// Linux drew both in black and white; on Windows Segoe UI has # © ™ and was asked first, so #️⃣ ™️
/// ©️ were text. The names are the platforms' own; what holds everywhere is that the face is a
/// colour face and a plain ™ © # 1 stays in the text face.
/// </summary>
public class EmojiFaceTests
{
    private const int VS16 = 0xFE0F, Keycap = 0x20E3;

    public static TheoryData<string> AskForEmoji => new()
    {
        S(0x1F600),                 // 😀
        S(0x2764, VS16),            // ❤️
        S('#', VS16, Keycap),       // #️⃣
        S('1', VS16, Keycap),       // 1️⃣
        S(0x2122, VS16),            // ™️
        S(0xA9, VS16),              // ©️
        S(0x2615),                  // ☕
        S(0x261D, 0x1F3FD),         // ☝🏽
        S(0x1F468, 0x200D, 0x1F469, 0x200D, 0x1F467), // 👨‍👩‍👧
    };

    [ColourEmojiTheory]
    [MemberData(nameof(AskForEmoji))]
    public void ACharacterAskingForEmoji_IsOneRunInAColourFace(string emoji)
    {
        var runs = Runs("a " + emoji + " b");
        Assert.Equal(3, runs.Count);
        Assert.Equal(emoji, runs[1].Text);
        Assert.True(IsColour(runs[1].Font), $"{Hex(emoji)} drawn from {runs[1].Font.FamilyName}");
        Assert.Equal(PrimaryFamily, runs[0].Font.FamilyName);
        Assert.Equal(PrimaryFamily, runs[2].Font.FamilyName);
    }

    [ColourEmojiFact]
    public void PlainTradeMarkCopyrightHashAndDigits_StayInTheTextFace()
    {
        var run = Assert.Single(Runs(S(0x2122, ' ', 0xA9, ' ', 0xAE, ' ', '#', ' ', '1', ' ', '*')));
        Assert.Equal(PrimaryFamily, run.Font.FamilyName);
    }

    [ColourEmojiFact]
    public void ThePlatformsEmojiFace_IsTheOneAskedFor()
    {
        var face = new SkiaFontCollection().MatchEmoji('#', FontWeight.Regular, FontSlant.Normal);
        Assert.NotNull(face);
        if (OperatingSystem.IsWindows())
            Assert.Equal("Segoe UI Emoji", face.FamilyName);
        else if (OperatingSystem.IsLinux() && SKFontManager.Default.MatchFamily("Noto Color Emoji") is not null)
            Assert.Equal("Noto Color Emoji", face.FamilyName);
    }

    /// <summary>
    /// The emoji face and the ordinary fallback for one code point are kept apart: # asked for as
    /// an emoji is the colour face, and asked for as text the face fontconfig or DirectWrite gives
    /// it, in whichever order they are asked.
    /// </summary>
    [ColourEmojiFact]
    public void TheEmojiAnswer_AndTheTextAnswer_AreCachedApart()
    {
        var emojiFirst = new SkiaFontCollection();
        var emoji = emojiFirst.MatchEmoji('#', FontWeight.Regular, FontSlant.Normal);
        var text = emojiFirst.MatchCharacter('#', FontWeight.Regular, FontSlant.Normal);

        var textFirst = new SkiaFontCollection();
        var text2 = textFirst.MatchCharacter('#', FontWeight.Regular, FontSlant.Normal);
        var emoji2 = textFirst.MatchEmoji('#', FontWeight.Regular, FontSlant.Normal);

        Assert.True(emoji is not null && IsColour(emoji));
        Assert.True(emoji2 is not null && IsColour(emoji2));
        Assert.True(text is not null && !IsColour(text), $"# as text: {text?.FamilyName}");
        Assert.True(text2 is not null && !IsColour(text2), $"# as text: {text2?.FamilyName}");
    }

    [Fact]
    public void APinnedCollection_KnowsNoEmojiFace()
    {
        // Not disposed: SkiaSharp hands out one wrapper per native face, which other tests hold.
        var face = SKTypeface.FromFamilyName("sans-serif");
        Assert.Null(SkiaFontCollection.Pinned(face).MatchEmoji(0x1F600, FontWeight.Regular, FontSlant.Normal));
    }

    private static string PrimaryFamily => new SkiaFontCollection().Resolve("sans-serif", FontWeight.Regular, FontSlant.Normal).FamilyName;

    private static IReadOnlyList<ShapedRun> Runs(string text)
    {
        var pipeline = new TextPipeline(new SkiaFontCollection(), new SkiaTextShaper());
        var style = new ResolvedInheritedStyle(FontFamily: "sans-serif", FontSize: 16f, FontWeight: FontWeight.Regular,
            TextColor: new(0, 0, 0, 255), TextDirection: TextDirection.Ltr, Cursor: CursorType.Default);
        return Assert.Single(pipeline.Layout(text, style, 10_000, null).Lines).Runs;
    }

    internal static bool IsColour(IResolvedFont font)
    {
        var typeface = (SKTypeface)font.NativeHandle;
        return typeface.GetTableSize(Tag("COLR")) > 0 || typeface.GetTableSize(Tag("CBDT")) > 0
            || typeface.GetTableSize(Tag("sbix")) > 0 || typeface.GetTableSize(Tag("SVG ")) > 0;

        static uint Tag(string s) => (uint)(s[0] << 24 | s[1] << 16 | s[2] << 8 | s[3]);
    }

    private static string S(params int[] codePoints) => string.Concat(codePoints.Select(char.ConvertFromUtf32));

    private static string Hex(string s) => string.Join(" ", s.EnumerateRunes().Select(r => $"U+{r.Value:X4}"));
}

/// <summary>
/// A fact about the colour emoji face — and so one that needs one installed. A desktop has one
/// (Segoe UI Emoji, Apple Color Emoji, Noto Color Emoji); a lean container may not, and then
/// nothing changed for emoji and the test skips, saying what to install.
/// </summary>
public sealed class ColourEmojiFactAttribute : FactAttribute
{
    public ColourEmojiFactAttribute()
    {
        if (!ColourEmojiFace.Installed) Skip = ColourEmojiFace.Reason;
    }
}

/// <summary><see cref="ColourEmojiFactAttribute"/> for a theory.</summary>
public sealed class ColourEmojiTheoryAttribute : TheoryAttribute
{
    public ColourEmojiTheoryAttribute()
    {
        if (!ColourEmojiFace.Installed) Skip = ColourEmojiFace.Reason;
    }
}

internal static class ColourEmojiFace
{
    public const string Reason = "No colour emoji face is installed, so there is none to choose. "
        + "Install one — on Debian/Ubuntu: apt install fonts-noto-color-emoji.";

    public static bool Installed { get; } =
        new SkiaFontCollection().MatchEmoji(0x1F600, FontWeight.Regular, FontSlant.Normal) is { } face
        && EmojiFaceTests.IsColour(face);
}
