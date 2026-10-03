namespace HultUI.Core.Styling;

/// <summary>
/// Family names the renderer resolves per platform, for use wherever a font family goes:
/// <c>.FontFamily(FontFamilies.Monospace)</c>, a theme, a <see cref="ResolvedInheritedStyle"/>.
/// </summary>
/// <remarks>
/// <para>A family may also be a list, as in CSS: <c>"JetBrains Mono, Consolas, monospace"</c>
/// takes the first family that is installed. A list chooses the face; a character that face
/// lacks still falls back to whichever installed font has it, as it does for any face.</para>
/// <para>A single name that is not installed draws in the platform's default face, as it always
/// has. Ending a list with a generic name is how to say what should happen instead.</para>
/// </remarks>
public static class FontFamilies
{
    /// <summary>
    /// The platform's monospace face: Cascadia Mono, then Consolas, on Windows; Menlo on macOS;
    /// elsewhere what fontconfig's <c>monospace</c> names (usually DejaVu Sans Mono), or else
    /// the first of DejaVu Sans Mono, Liberation Mono and Noto Sans Mono that is installed.
    /// </summary>
    public const string Monospace = "monospace";

    /// <summary>The default interface face: what an unstyled element draws in.</summary>
    public const string SansSerif = "sans-serif";
}
