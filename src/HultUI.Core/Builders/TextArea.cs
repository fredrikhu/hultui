using HultUI.Core.Elements;

namespace HultUI.Core.Builders;

public static class TextArea
{
    public static TextAreaElement New(string text = "") => TextAreaElement.New(text);
}
