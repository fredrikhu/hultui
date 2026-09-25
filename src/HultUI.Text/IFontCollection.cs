using HultUI.Core.Styling;

namespace HultUI.Text;

public interface IFontCollection
{
    IResolvedFont Resolve(string familyName, FontWeight weight, FontSlant slant);

    /// <summary>
    /// Find a fallback font that contains the given Unicode codepoint.
    /// Returns null if no font can render this codepoint.
    /// </summary>
    IResolvedFont? MatchCharacter(int codepoint, FontWeight weight, FontSlant slant);

    /// <summary>
    /// <see cref="MatchCharacter(int, FontWeight, FontSlant)"/>, told what the surrounding
    /// paragraph says its Han characters are (<see cref="ScriptLanguage.CjkContextOf"/>):
    /// the same ideograph needs a Japanese face beside kana and a Chinese one without.
    /// Collections that cannot choose by language ignore it, which is the default.
    /// </summary>
    IResolvedFont? MatchCharacter(int codepoint, FontWeight weight, FontSlant slant, string? hanContext)
        => MatchCharacter(codepoint, weight, slant);
}

public interface IResolvedFont : IDisposable
{
    string FamilyName { get; }
    float Ascent { get; }
    float Descent { get; }
    float Leading { get; }
    float LineHeight { get; }
    bool HasGlyph(int codepoint);

    /// <summary>
    /// Returns a platform-specific handle (e.g., SKTypeface) for use by the shaper/renderer.
    /// </summary>
    object NativeHandle { get; }
}
