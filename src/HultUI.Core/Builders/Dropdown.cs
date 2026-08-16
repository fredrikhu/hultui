using HultUI.Core.Elements;

namespace HultUI.Core.Builders;

public static class Dropdown
{
    public static DropdownElement New(IReadOnlyList<string> items, int selectedIndex = -1,
        [OriginFile] string? file = null, [OriginLine] int line = 0) =>
        DropdownElement.New(items, selectedIndex, file, line);
}
