using HultUI.Core.Elements;
using HultUI.Core.Layout;

namespace HultUI.Core.Builders;

public static class Flex
{
    public static BoxElement Row(float gap = 0)
    {
        var box = BoxElement.New();
        box.Flex.Direction = FlexDirection.Row;
        box.Flex.Gap = gap;
        return box;
    }

    public static BoxElement Column(float gap = 0)
    {
        var box = BoxElement.New();
        box.Flex.Direction = FlexDirection.Column;
        box.Flex.Gap = gap;
        return box;
    }
}
