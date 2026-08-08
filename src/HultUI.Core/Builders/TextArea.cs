using HultUI.Core.Elements;

namespace HultUI.Core.Builders;

public static class TextArea
{
    public static TextAreaElement New(string text = "", [OriginFile] string? file = null, [OriginLine] int line = 0) => TextAreaElement.New(text, file, line);
}
