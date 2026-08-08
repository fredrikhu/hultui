using HultUI.Core.Elements;

namespace HultUI.Core.Builders;

public static class Button
{
    public static ButtonElement New(string label, ButtonVariant variant = ButtonVariant.Primary, [OriginFile] string? file = null, [OriginLine] int line = 0) =>
        ButtonElement.New(label, variant, file, line);
}
