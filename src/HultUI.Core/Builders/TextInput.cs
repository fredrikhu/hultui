using HultUI.Core.Elements;

namespace HultUI.Core.Builders;

public static class TextInput
{
    public static TextInputElement New(string text = "") => TextInputElement.New(text);
}
