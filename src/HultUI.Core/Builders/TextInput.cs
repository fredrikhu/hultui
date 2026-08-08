using HultUI.Core.Elements;

namespace HultUI.Core.Builders;

public static class TextInput
{
    public static TextInputElement New(string text = "", [OriginFile] string? file = null, [OriginLine] int line = 0) => TextInputElement.New(text, file, line);
}
