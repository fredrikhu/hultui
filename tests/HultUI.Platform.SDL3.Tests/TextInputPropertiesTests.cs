using HultUI.Core.Platform;
using HultUI.Platform.SDL3;
using Xunit;

namespace HultUI.Platform.SDL3.Tests;

/// <summary>
/// What each <see cref="TextInputKind"/> asks of SDL (docs/design/SOFT-KEYBOARD.md §2): the type,
/// capitalization and correction that pick a phone's keyboard, and Android's own input type for
/// the kinds SDL has no type for. The numbers are <c>android.text.InputType</c>'s.
/// </summary>
public class TextInputPropertiesTests
{
    [Fact]
    public void Text_CapitalizesSentencesAndCorrects_WithNoAndroidOverride()
    {
        var p = SDL3Window.TextInputProperties(TextInputKind.Text);
        Assert.Equal(SDL.SDL_TextInputType.SDL_TEXTINPUT_TYPE_TEXT, p.Type);
        Assert.Equal(SDL.SDL_Capitalization.SDL_CAPITALIZE_SENTENCES, p.Caps);
        Assert.True(p.Correct);
        Assert.Equal(0, p.Android);
    }

    [Fact]
    public void Password_IsSdlsHiddenPassword_NeverCorrected()
    {
        var p = SDL3Window.TextInputProperties(TextInputKind.Password);
        Assert.Equal(SDL.SDL_TextInputType.SDL_TEXTINPUT_TYPE_TEXT_PASSWORD_HIDDEN, p.Type);
        Assert.False(p.Correct);
        Assert.Equal(0, p.Android);
    }

    [Theory]
    [InlineData(TextInputKind.Number, 0x2)]              // CLASS_NUMBER
    [InlineData(TextInputKind.Decimal, 0x2 | 0x2000 | 0x1000)]   // with FLAG_DECIMAL and FLAG_SIGNED
    [InlineData(TextInputKind.Phone, 0x3)]               // CLASS_PHONE
    [InlineData(TextInputKind.Url, 0x1 | 0x10 | 0x80000)]        // TEXT, VARIATION_URI, NO_SUGGESTIONS
    public void TheKindsSdlHasNoTypeFor_CarryAndroidsInputType(TextInputKind kind, int inputType)
    {
        var p = SDL3Window.TextInputProperties(kind);
        Assert.Equal(inputType, p.Android);
        Assert.False(p.Correct);
    }

    [Theory]
    [InlineData(TextInputKind.Email, SDL.SDL_TextInputType.SDL_TEXTINPUT_TYPE_TEXT_EMAIL, SDL.SDL_Capitalization.SDL_CAPITALIZE_NONE, false)]
    [InlineData(TextInputKind.Username, SDL.SDL_TextInputType.SDL_TEXTINPUT_TYPE_TEXT_USERNAME, SDL.SDL_Capitalization.SDL_CAPITALIZE_NONE, false)]
    [InlineData(TextInputKind.Name, SDL.SDL_TextInputType.SDL_TEXTINPUT_TYPE_TEXT_NAME, SDL.SDL_Capitalization.SDL_CAPITALIZE_WORDS, false)]
    [InlineData(TextInputKind.Search, SDL.SDL_TextInputType.SDL_TEXTINPUT_TYPE_TEXT, SDL.SDL_Capitalization.SDL_CAPITALIZE_NONE, true)]
    public void TheKindsSdlHasATypeFor_UseIt(TextInputKind kind, SDL.SDL_TextInputType type, SDL.SDL_Capitalization caps, bool correct)
    {
        var p = SDL3Window.TextInputProperties(kind);
        Assert.Equal(type, p.Type);
        Assert.Equal(caps, p.Caps);
        Assert.Equal(correct, p.Correct);
        Assert.Equal(0, p.Android);
    }

    [Fact]
    public void EveryKind_HasAnEntry_OtherThanTheTextFallback()
    {
        // A kind added to the enum and not to the table would fall through to Text's entry.
        var text = SDL3Window.TextInputProperties(TextInputKind.Text);
        foreach (var kind in Enum.GetValues<TextInputKind>())
            if (kind != TextInputKind.Text)
                Assert.NotEqual(text, SDL3Window.TextInputProperties(kind));
    }
}
