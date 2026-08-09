using HultUI.Core.Elements;

namespace HultUI.Core.Builders;

public static class Box
{
    public static BoxElement New([OriginFile] string? file = null, [OriginLine] int line = 0) => BoxElement.New(file, line);
}
