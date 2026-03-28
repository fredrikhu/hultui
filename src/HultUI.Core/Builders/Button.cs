using HultUI.Core.Elements;

namespace HultUI.Core.Builders;

public static class Button
{
    public static ButtonElement New(string label, ButtonVariant variant = ButtonVariant.Primary) =>
        ButtonElement.New(label, variant);
}
