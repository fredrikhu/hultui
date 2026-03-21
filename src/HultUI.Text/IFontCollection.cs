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
