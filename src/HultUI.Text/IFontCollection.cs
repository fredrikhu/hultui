using HultUI.Core.Styling;

namespace HultUI.Text;

public interface IFontCollection
{
    /// <summary>
    /// The face for <paramref name="familyName"/>: one family, or a comma-separated list of
    /// which the first installed wins, where the generic <see cref="FontFamilies.Monospace"/>
    /// names the platform's monospace face. A collection that cannot tell what is installed
    /// may take the first name.
    /// </summary>
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

    /// <summary>
    /// The colour emoji face that has <paramref name="codepoint"/>, for a character that asks to be
    /// drawn as an emoji rather than as text (Unicode Technical Standard #51): one whose default is
    /// emoji (😀 ⭐), or one followed by U+FE0F (❤️ ™️ #️⃣), a skin tone or a joiner and another
    /// pictograph. It is asked before the primary face, which may have the character in black and
    /// white. Null when the collection knows no such face, and the character then falls back as any
    /// other does; the default knows none.
    /// </summary>
    IResolvedFont? MatchEmoji(int codepoint, FontWeight weight, FontSlant slant) => null;
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

    /// <summary>Whether the font says every glyph has the same advance.</summary>
    bool IsFixedPitch => false;

    /// <summary>The underline the font asks for, per unit of font size: where its stroke starts
    /// below the baseline (positive down) and how thick it is. Null when the font does not say.</summary>
    (float Position, float Thickness)? Underline => null;

    /// <summary>The strikeout the font asks for, per unit of font size, as <see cref="Underline"/>
    /// (its position is negative: above the baseline). Null when the font does not say.</summary>
    (float Position, float Thickness)? Strikeout => null;
}
