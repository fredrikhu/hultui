using HultUI.Core.Elements;

namespace HultUI.Core.Builders;

public static class Text
{
    public static TextElement New(string content, [OriginFile] string? file = null, [OriginLine] int line = 0) => TextElement.New(content, file, line);
}
