using HultUI.Core.Elements;

namespace HultUI.Core.Builders;

public static class Button
{
    public static ButtonElement New(string label, ButtonVariant variant = ButtonVariant.Primary, [OriginFile] string? file = null, [OriginLine] int line = 0) =>
        ButtonElement.New(label, variant, file, line);

    /// <summary>An empty button of one variant; add a glyph, a label, or both as children.</summary>
    public static ButtonElement New(ButtonVariant variant, [OriginFile] string? file = null, [OriginLine] int line = 0) =>
        ButtonElement.New(variant, file, line);
}
